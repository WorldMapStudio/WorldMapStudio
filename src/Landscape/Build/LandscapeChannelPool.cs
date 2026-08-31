using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Scratch channel storage for one block of chunks plus its halo.
///
/// Values are held as floats whatever bit depth the channel declares — depth is a storage concern and
/// channels are never stored. Buffers are pooled and reused across blocks because the whole point of
/// building a block at a time is that a chunk's channels are read by its neighbours.
///
/// A buffer holds <see cref="LandscapeChannel.Components"/> floats per texel, interleaved
/// (<c>[texel0.c0, texel0.c1, ..., texel1.c0, ...]</c>), so a 1-component channel's buffer is exactly
/// what it always was and a 3/4-component one is the same layout at a wider stride — nothing here
/// needs to special-case which.
///
/// <b>Sampling is global, not chunk-local.</b> <see cref="SampleScalar"/> and <see cref="SampleColor"/>
/// take a world position and resolve which chunk owns it, so two neighbouring chunks asking about the
/// same point get the same answer. That is what makes a shared chunk edge continuous by construction
/// rather than by the function author remembering to fade out.
/// </summary>
public sealed class LandscapeChannelPool
{
    private readonly Dictionary<(string Channel, ChunkCoord Coord), float[]> _buffers = [];
    private readonly Dictionary<string, LandscapeChannel> _channels = [];
    private readonly Stack<float[]> _spare = [];
    private readonly LandscapeSettings _settings;

    public LandscapeChannelPool(LandscapeSettings settings, IEnumerable<LandscapeChannel> channels)
    {
        _settings = settings;
        Grid = new LandscapeGrid(settings);

        foreach (LandscapeChannel channel in channels)
        {
            _channels[channel.Name] = channel;
        }
    }

    public LandscapeGrid Grid { get; }

    /// <summary>Returns every buffer to the free list, keeping the allocations for the next block.</summary>
    public void Reset()
    {
        foreach (float[] buffer in _buffers.Values)
        {
            _spare.Push(buffer);
        }

        _buffers.Clear();
    }

    public LandscapeChannel? Channel(string name) =>
        name.Length > 0 && _channels.TryGetValue(name, out LandscapeChannel? channel) ? channel : null;

    /// <summary>
    /// The writable buffer for one channel in one chunk, created zeroed on first use — length
    /// <c>Resolution² × Components</c>, interleaved per texel. Rasterization writes only through this,
    /// and only for the chunk it is rasterizing. Wrapped as a <see cref="LandscapeChannelWriter"/> for
    /// callers outside this file — see <see cref="LandscapeRasterContext.Writer"/>.
    /// </summary>
    internal float[]? Buffer(string channelName, ChunkCoord coord)
    {
        if (Channel(channelName) is not { } channel)
        {
            return null;
        }

        var key = (channelName, coord);
        if (_buffers.TryGetValue(key, out float[]? existing))
        {
            return existing;
        }

        int length = channel.Resolution * channel.Resolution * channel.Components;
        float[] buffer = Rent(length);
        _buffers[key] = buffer;
        return buffer;
    }

    /// <summary>Whether a chunk's buffer exists yet, without creating one.</summary>
    public bool Has(string channelName, ChunkCoord coord) => _buffers.ContainsKey((channelName, coord));

    /// <summary>
    /// Bilinearly samples a channel's first component at a world position — the whole value for a
    /// scalar channel, or its red component for a wider one (the same "native scalar read of a color
    /// channel is red" rule <see cref="SampleScalar"/> applies to a native binding). Crosses chunk
    /// borders freely; positions outside the block's halo read as zero.
    /// </summary>
    public float Sample(string channelName, Vector3 world)
    {
        if (Channel(channelName) is not { } channel)
        {
            return 0.0f;
        }

        return SampleComponent(channel, world, 0);
    }

    /// <summary>
    /// Bilinearly samples a channel as a scalar, honoring <paramref name="binding"/>'s swizzle. A
    /// native binding reads the channel's first component (its whole value if scalar, its red
    /// component otherwise); <see cref="LandscapeSwizzle.Rgb"/>/<see cref="LandscapeSwizzle.Rgba"/>
    /// reduce to luminance, since asking for "the color" as a single number has no other sensible
    /// meaning.
    /// </summary>
    public float SampleScalar(in LandscapeChannelBinding binding, Vector3 world)
    {
        if (Channel(binding.Channel) is not { } channel)
        {
            return 0.0f;
        }

        if (binding.Swizzle == LandscapeSwizzle.Native)
        {
            return SampleComponent(channel, world, 0);
        }

        return LandscapeChannelBinding.Extract(SampleNative(channel, world), binding.Swizzle);
    }

    /// <summary>
    /// Bilinearly samples a channel as a color, honoring <paramref name="binding"/>'s swizzle. A
    /// native (or <see cref="LandscapeSwizzle.Rgba"/>) binding widens per the channel's own component
    /// count — a scalar channel becomes <c>(v,v,v,v)</c>, a 3-component one <c>(r,g,b,1)</c> — so a
    /// mask read as a color is an opaque grey of the same coverage, and a color-and-coverage channel's
    /// alpha survives untouched.
    /// </summary>
    public Color SampleColor(in LandscapeChannelBinding binding, Vector3 world)
    {
        if (Channel(binding.Channel) is not { } channel)
        {
            return default;
        }

        Color native = SampleNative(channel, world);
        return binding.Swizzle switch
        {
            LandscapeSwizzle.Native or LandscapeSwizzle.Rgba => native,
            LandscapeSwizzle.Rgb => new Color(native.R, native.G, native.B, 1.0f),
            LandscapeSwizzle.R => Grey(native.R),
            LandscapeSwizzle.G => Grey(native.G),
            LandscapeSwizzle.B => Grey(native.B),
            LandscapeSwizzle.A => Grey(native.A),
            LandscapeSwizzle.Luminance => Grey(LandscapeChannelBinding.Extract(native, LandscapeSwizzle.Luminance)),
            _ => native,
        };

        static Color Grey(float v) => new(v, v, v, 1.0f);
    }

    /// <summary>The world position of a channel texel centre, for rasterizers walking a chunk.</summary>
    public Vector3 TexelCentre(ChunkCoord coord, int resolution, int x, int y)
    {
        Vector3 origin = Grid.OriginOf(coord);
        float step = Grid.ChunkSize / resolution;
        return new Vector3(origin.X + ((x + 0.5f) * step), 0.0f, origin.Z + ((y + 0.5f) * step));
    }

    // Widens a channel's own components to a Color per the fixed rule described on SampleColor,
    // regardless of what a caller's binding eventually reduces that to.
    private Color SampleNative(LandscapeChannel channel, Vector3 world)
    {
        float r = SampleComponent(channel, world, 0);
        if (channel.Components == 1)
        {
            return new Color(r, r, r, r);
        }

        float g = SampleComponent(channel, world, 1);
        float b = SampleComponent(channel, world, 2);
        if (channel.Components == 3)
        {
            return new Color(r, g, b, 1.0f);
        }

        float a = SampleComponent(channel, world, 3);
        return new Color(r, g, b, a);
    }

    private float SampleComponent(LandscapeChannel channel, Vector3 world, int component)
    {
        int resolution = channel.Resolution;
        int components = channel.Components;

        // Texel centres sit at (i + 0.5), so the continuous coordinate of a world point is offset by
        // half a texel before the corners are taken.
        float gx = (world.X / Grid.ChunkSize * resolution) - 0.5f;
        float gy = (world.Z / Grid.ChunkSize * resolution) - 0.5f;

        int x0 = Mathf.FloorToInt(gx);
        int y0 = Mathf.FloorToInt(gy);
        float fx = gx - x0;
        float fy = gy - y0;

        float topLeft = Texel(channel.Name, resolution, components, x0, y0, component);
        float topRight = Texel(channel.Name, resolution, components, x0 + 1, y0, component);
        float bottomLeft = Texel(channel.Name, resolution, components, x0, y0 + 1, component);
        float bottomRight = Texel(channel.Name, resolution, components, x0 + 1, y0 + 1, component);

        return Mathf.Lerp(
            Mathf.Lerp(topLeft, topRight, fx),
            Mathf.Lerp(bottomLeft, bottomRight, fx),
            fy);
    }

    // One texel by global index: the chunk that owns it is a function of the index alone, so every
    // caller resolves the same point to the same chunk and the same value.
    private float Texel(string channelName, int resolution, int components, int globalX, int globalY, int component)
    {
        var coord = new ChunkCoord(
            _settings.OriginChunkX + FloorDiv(globalX, resolution),
            _settings.OriginChunkY + FloorDiv(globalY, resolution));

        if (!_buffers.TryGetValue((channelName, coord), out float[]? buffer))
        {
            return 0.0f;
        }

        int localX = Mod(globalX, resolution);
        int localY = Mod(globalY, resolution);
        return buffer[(((localY * resolution) + localX) * components) + component];
    }

    private float[] Rent(int length)
    {
        while (_spare.Count > 0)
        {
            float[] candidate = _spare.Pop();
            if (candidate.Length == length)
            {
                System.Array.Clear(candidate);
                return candidate;
            }
        }

        return new float[length];
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : ((value + 1) / divisor) - 1;

    private static int Mod(int value, int divisor)
    {
        int remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}
