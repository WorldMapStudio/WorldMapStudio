using System.Collections.Concurrent;
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
    // One table per channel, fixed at construction, rather than a single table keyed on
    // (channel name, coord): a channel is sampled per texel and hashing its name on every one of those
    // lookups dominated a build. The per-channel tables stay concurrent because stage 3 of a build
    // rasterizes the block's chunks in parallel (see LandscapeBuilder's invariant). Each chunk still
    // owns its own buffers outright — one coord is rasterized by one thread — so the only thing
    // crossing threads here is the table the buffers hang off and the free list they are rented from.
    private readonly Dictionary<string, ChannelStore> _stores = [];
    private readonly ConcurrentStack<float[]> _spare = new();
    private readonly LandscapeSettings _settings;

    public LandscapeChannelPool(LandscapeSettings settings, IEnumerable<LandscapeChannel> channels)
    {
        _settings = settings;
        Grid = new LandscapeGrid(settings);

        foreach (LandscapeChannel channel in channels)
        {
            _stores[channel.Name] = new ChannelStore(channel);
        }
    }

    public LandscapeGrid Grid { get; }

    /// <summary>Returns every buffer to the free list, keeping the allocations for the next block.</summary>
    public void Reset()
    {
        foreach (ChannelStore store in _stores.Values)
        {
            foreach (float[] buffer in store.Buffers.Values)
            {
                _spare.Push(buffer);
            }

            store.Buffers.Clear();
        }
    }

    public LandscapeChannel? Channel(string name) => Store(name)?.Channel;

    /// <summary>
    /// The writable buffer for one channel in one chunk, created zeroed on first use — length
    /// <c>Resolution² × Components</c>, interleaved per texel. Rasterization writes only through this,
    /// and only for the chunk it is rasterizing. Wrapped as a <see cref="LandscapeChannelWriter"/> for
    /// callers outside this file — see <see cref="LandscapeRasterContext.Writer"/>.
    /// </summary>
    internal float[]? Buffer(string channelName, ChunkCoord coord)
    {
        if (Store(channelName) is not { } store)
        {
            return null;
        }

        if (store.Buffers.TryGetValue(coord, out float[]? existing))
        {
            return existing;
        }

        LandscapeChannel channel = store.Channel;
        int length = channel.Resolution * channel.Resolution * channel.Components;
        float[] buffer = Rent(length);
        if (store.Buffers.TryAdd(coord, buffer))
        {
            return buffer;
        }

        // Lost a race for a key no other thread should have been writing. Hand the buffer back rather
        // than drop it, and use the winner, so the two never disagree about which array a chunk owns.
        _spare.Push(buffer);
        return store.Buffers[coord];
    }

    /// <summary>Whether a chunk's buffer exists yet, without creating one.</summary>
    public bool Has(string channelName, ChunkCoord coord) =>
        Store(channelName) is { } store && store.Buffers.ContainsKey(coord);

    /// <summary>
    /// Bilinearly samples a channel's first component at a world position — the whole value for a
    /// scalar channel, or its red component for a wider one (the same "native scalar read of a color
    /// channel is red" rule <see cref="SampleScalar"/> applies to a native binding). Crosses chunk
    /// borders freely; positions outside the block's halo read as zero.
    /// </summary>
    public float Sample(string channelName, Vector3 world) =>
        Store(channelName) is { } store ? Resolve(store, world).Read(0) : 0.0f;

    /// <summary>
    /// Bilinearly samples a channel as a scalar, honoring <paramref name="binding"/>'s swizzle. A
    /// native binding reads the channel's first component (its whole value if scalar, its red
    /// component otherwise); <see cref="LandscapeSwizzle.Rgb"/>/<see cref="LandscapeSwizzle.Rgba"/>
    /// reduce to luminance, since asking for "the color" as a single number has no other sensible
    /// meaning.
    /// </summary>
    public float SampleScalar(in LandscapeChannelBinding binding, Vector3 world)
    {
        if (Store(binding.Channel) is not { } store)
        {
            return 0.0f;
        }

        Bilinear taps = Resolve(store, world);
        if (binding.Swizzle == LandscapeSwizzle.Native)
        {
            return taps.Read(0);
        }

        // A swizzle naming one component reads that component, rather than widening the channel to a
        // Color and throwing three quarters of it away — which is what an "adt_alpha:g" style binding,
        // the shape every splat slot uses, was paying per texel.
        int component = ComponentOf(store.Channel, binding.Swizzle);
        return component >= 0
            ? taps.Read(component)
            : LandscapeChannelBinding.Extract(Widen(store.Channel, taps), binding.Swizzle);
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
        if (Store(binding.Channel) is not { } store)
        {
            return default;
        }

        Color native = Widen(store.Channel, Resolve(store, world));
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

    /// <summary>
    /// Reads a channel as a scalar at the single nearest (containing) texel — no bilinear blend —
    /// honoring <paramref name="binding"/>'s swizzle. A terrain-attribute function reading a discrete
    /// id must use this, never <see cref="SampleScalar"/>: blending across a texel or chunk edge turns
    /// a valid id (1519 next to 12) into an invalid one (something near 700). Positions outside the
    /// block's halo read as zero, like the bilinear samplers.
    /// </summary>
    public float ReadScalarNearest(in LandscapeChannelBinding binding, Vector3 world)
    {
        if (Store(binding.Channel) is not { } store)
        {
            return 0.0f;
        }

        LandscapeChannel channel = store.Channel;
        int resolution = channel.Resolution;
        int components = channel.Components;

        int gx = Mathf.FloorToInt(world.X / Grid.ChunkSize * resolution);
        int gy = Mathf.FloorToInt(world.Z / Grid.ChunkSize * resolution);

        var cursor = new StoreCursor(store);
        Tap tap = Texel(ref cursor, resolution, components, gx, gy);

        if (binding.Swizzle == LandscapeSwizzle.Native)
        {
            return tap.Read(0);
        }

        int component = ComponentOf(channel, binding.Swizzle);
        return component >= 0 ? tap.Read(component) : tap.Read(0);
    }

    /// <summary>The world position of a channel texel centre, for rasterizers walking a chunk.</summary>
    public Vector3 TexelCentre(ChunkCoord coord, int resolution, int x, int y)
    {
        Vector3 origin = Grid.OriginOf(coord);
        float step = Grid.ChunkSize / resolution;
        return new Vector3(origin.X + ((x + 0.5f) * step), 0.0f, origin.Z + ((y + 0.5f) * step));
    }

    private ChannelStore? Store(string name) =>
        name.Length > 0 && _stores.TryGetValue(name, out ChannelStore? store) ? store : null;

    // Widens a channel's own components to a Color per the fixed rule described on SampleColor,
    // regardless of what a caller's binding eventually reduces that to.
    private static Color Widen(LandscapeChannel channel, in Bilinear taps)
    {
        float r = taps.Read(0);
        if (channel.Components == 1)
        {
            return new Color(r, r, r, r);
        }

        float g = taps.Read(1);
        float b = taps.Read(2);
        return channel.Components == 3
            ? new Color(r, g, b, 1.0f)
            : new Color(r, g, b, taps.Read(3));
    }

    // Which single component a swizzle reduces to for a channel of this width, or -1 when the answer
    // is not one stored component — luminance, or the alpha of a 3-component channel, which Widen
    // fills in as a constant. Mirrors Widen exactly so the two can never disagree.
    private static int ComponentOf(LandscapeChannel channel, LandscapeSwizzle swizzle)
    {
        if (channel.Components == 1)
        {
            return swizzle is LandscapeSwizzle.R or LandscapeSwizzle.G or LandscapeSwizzle.B or LandscapeSwizzle.A
                ? 0
                : -1;
        }

        return swizzle switch
        {
            LandscapeSwizzle.R => 0,
            LandscapeSwizzle.G => 1,
            LandscapeSwizzle.B => 2,
            LandscapeSwizzle.A => channel.Components == 4 ? 3 : -1,
            _ => -1,
        };
    }

    // The four bilinear corners of a world position, resolved once. Which chunk owns a corner is a
    // function of the global texel index alone, so every caller resolves the same point to the same
    // chunk and the same value — and reading several components of one sample then costs four array
    // reads each instead of four chunk lookups each.
    private Bilinear Resolve(ChannelStore store, Vector3 world)
    {
        LandscapeChannel channel = store.Channel;
        int resolution = channel.Resolution;
        int components = channel.Components;

        // Texel centres sit at (i + 0.5), so the continuous coordinate of a world point is offset by
        // half a texel before the corners are taken.
        float gx = (world.X / Grid.ChunkSize * resolution) - 0.5f;
        float gy = (world.Z / Grid.ChunkSize * resolution) - 0.5f;

        int x0 = Mathf.FloorToInt(gx);
        int y0 = Mathf.FloorToInt(gy);

        var cursor = new StoreCursor(store);
        return new Bilinear(
            Texel(ref cursor, resolution, components, x0, y0),
            Texel(ref cursor, resolution, components, x0 + 1, y0),
            Texel(ref cursor, resolution, components, x0, y0 + 1),
            Texel(ref cursor, resolution, components, x0 + 1, y0 + 1),
            gx - x0,
            gy - y0);
    }

    private Tap Texel(ref StoreCursor cursor, int resolution, int components, int globalX, int globalY)
    {
        var coord = new ChunkCoord(
            _settings.OriginChunkX + FloorDiv(globalX, resolution),
            _settings.OriginChunkY + FloorDiv(globalY, resolution));

        if (cursor.Buffer(coord) is not { } buffer)
        {
            return default;
        }

        int localX = Mod(globalX, resolution);
        int localY = Mod(globalY, resolution);
        return new Tap(buffer, (((localY * resolution) + localX) * components));
    }

    private float[] Rent(int length)
    {
        while (_spare.TryPop(out float[]? candidate))
        {
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

    private sealed class ChannelStore(LandscapeChannel channel)
    {
        public LandscapeChannel Channel { get; } = channel;

        public ConcurrentDictionary<ChunkCoord, float[]> Buffers { get; } = new();
    }

    // Remembers the last chunk a corner resolved to. The four corners of one bilinear sample land in
    // the same chunk except right on a chunk edge, so only the first of them pays the table lookup.
    private struct StoreCursor(ChannelStore store)
    {
        private ChunkCoord _coord;
        private float[]? _buffer;
        private bool _valid;

        public float[]? Buffer(ChunkCoord coord)
        {
            if (_valid && _coord == coord)
            {
                return _buffer;
            }

            store.Buffers.TryGetValue(coord, out _buffer);
            _coord = coord;
            _valid = true;
            return _buffer;
        }
    }

    // One bilinear corner: the buffer that owns it and the element index its first component sits at,
    // or no buffer at all for a position outside the block, which reads as zero.
    private readonly struct Tap(float[]? buffer, int index)
    {
        public float Read(int component) => buffer is { } texels ? texels[index + component] : 0.0f;
    }

    private readonly struct Bilinear(Tap topLeft, Tap topRight, Tap bottomLeft, Tap bottomRight, float fx, float fy)
    {
        public float Read(int component) => Mathf.Lerp(
            Mathf.Lerp(topLeft.Read(component), topRight.Read(component), fx),
            Mathf.Lerp(bottomLeft.Read(component), bottomRight.Read(component), fx),
            fy);
    }
}
