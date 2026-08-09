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
/// <b>Sampling is global, not chunk-local.</b> <see cref="Sample"/> takes a world position and
/// resolves which chunk owns it, so two neighbouring chunks asking about the same point get the same
/// answer. That is what makes a shared chunk edge continuous by construction rather than by the
/// function author remembering to fade out.
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
    /// The writable buffer for one channel in one chunk, created zeroed on first use. Rasterization
    /// writes only through this, and only for the chunk it is rasterizing.
    /// </summary>
    public float[]? Buffer(string channelName, ChunkCoord coord)
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

        int length = channel.Resolution * channel.Resolution;
        float[] buffer = Rent(length);
        _buffers[key] = buffer;
        return buffer;
    }

    /// <summary>Whether a chunk's buffer exists yet, without creating one.</summary>
    public bool Has(string channelName, ChunkCoord coord) => _buffers.ContainsKey((channelName, coord));

    /// <summary>
    /// Bilinearly samples a channel at a world position, crossing chunk borders freely. Positions
    /// outside the block's halo read as zero — which is why a function's declared sample radius has
    /// to be honest about how far it reaches.
    /// </summary>
    public float Sample(string channelName, Vector3 world)
    {
        if (Channel(channelName) is not { } channel)
        {
            return 0.0f;
        }

        int resolution = channel.Resolution;

        // Texel centres sit at (i + 0.5), so the continuous coordinate of a world point is offset by
        // half a texel before the corners are taken.
        float gx = (world.X / Grid.ChunkSize * resolution) - 0.5f;
        float gy = (world.Z / Grid.ChunkSize * resolution) - 0.5f;

        int x0 = Mathf.FloorToInt(gx);
        int y0 = Mathf.FloorToInt(gy);
        float fx = gx - x0;
        float fy = gy - y0;

        float topLeft = Texel(channelName, resolution, x0, y0);
        float topRight = Texel(channelName, resolution, x0 + 1, y0);
        float bottomLeft = Texel(channelName, resolution, x0, y0 + 1);
        float bottomRight = Texel(channelName, resolution, x0 + 1, y0 + 1);

        return Mathf.Lerp(
            Mathf.Lerp(topLeft, topRight, fx),
            Mathf.Lerp(bottomLeft, bottomRight, fx),
            fy);
    }

    /// <summary>The world position of a channel texel centre, for rasterizers walking a chunk.</summary>
    public Vector3 TexelCentre(ChunkCoord coord, int resolution, int x, int y)
    {
        Vector3 origin = Grid.OriginOf(coord);
        float step = Grid.ChunkSize / resolution;
        return new Vector3(origin.X + ((x + 0.5f) * step), 0.0f, origin.Z + ((y + 0.5f) * step));
    }

    // One texel by global index: the chunk that owns it is a function of the index alone, so every
    // caller resolves the same point to the same chunk and the same value.
    private float Texel(string channelName, int resolution, int globalX, int globalY)
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
        return buffer[(localY * resolution) + localX];
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
