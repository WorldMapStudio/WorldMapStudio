using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Bilinear sampler over a chunked image's published <see cref="ImageChunkTable"/>. Caches the last
/// chunk touched so a scanline of taps — what <see cref="ImageComponent.Rasterize"/> walks — hits the
/// same chunk repeatedly instead of paying a dictionary lookup on every one of the four bilinear taps.
///
/// Built once per build (see <see cref="PaintImage.CreateSampler"/>) rather than looked up per call on
/// <see cref="PaintImage"/> itself, so a landscape build reads a consistent snapshot even while the
/// image is being painted concurrently on the main thread.
/// </summary>
public struct ImageSampler
{
    private readonly ImageChunkTable _table;
    private readonly int _width;
    private readonly int _height;
    private readonly int _chunkSize;
    private readonly int _components;
    private readonly PaintImagePixelFormat _format;

    private ImageChunkCoord _cursorCoord;
    private byte[]? _cursorPixels;
    private bool _cursorValid;

    internal ImageSampler(ImageChunkTable table, int width, int height, int chunkSize, int components, PaintImagePixelFormat format)
    {
        _table = table;
        _width = width;
        _height = height;
        _chunkSize = chunkSize;
        _components = components;
        _format = format;
        _cursorCoord = default;
        _cursorPixels = null;
        _cursorValid = false;
    }

    /// <summary>Bilinear sample of the image's first component, returned in the 0..1 range for a
    /// <see cref="PaintImagePixelFormat.Byte"/> image — the whole value for a scalar image, its red
    /// component for a color one — or unclamped, straight from storage, for a
    /// <see cref="PaintImagePixelFormat.Float32"/> one.</summary>
    public float Sample(float u, float v) => SampleComponent(u, v, 0);

    /// <summary>Bilinear sample as a color: native for a 3/4-component image, or the value replicated
    /// into every channel (fully opaque) for a scalar one — the same widening
    /// <see cref="LandscapeChannelPool.SampleColor"/> applies to a channel.</summary>
    public Color SampleColor(float u, float v)
    {
        float r = SampleComponent(u, v, 0);
        if (_components == 1)
        {
            return new Color(r, r, r, r);
        }

        float g = SampleComponent(u, v, 1);
        float b = SampleComponent(u, v, 2);
        return _components == 3 ? new Color(r, g, b, 1.0f) : new Color(r, g, b, SampleComponent(u, v, 3));
    }

    private float SampleComponent(float u, float v, int component)
    {
        float x = Mathf.Clamp((u * _width) - 0.5f, 0.0f, _width - 1.0f);
        float y = Mathf.Clamp((v * _height) - 0.5f, 0.0f, _height - 1.0f);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Math.Min(x0 + 1, _width - 1);
        int y1 = Math.Min(y0 + 1, _height - 1);
        float tx = x - x0;
        float ty = y - y0;

        float a = Mathf.Lerp(PixelAt(x0, y0, component), PixelAt(x1, y0, component), tx);
        float b = Mathf.Lerp(PixelAt(x0, y1, component), PixelAt(x1, y1, component), tx);
        float raw = Mathf.Lerp(a, b, ty);
        return _format == PaintImagePixelFormat.Float32 ? raw : raw / 255.0f;
    }

    private float PixelAt(int x, int y, int component)
    {
        var coord = new ImageChunkCoord(x / _chunkSize, y / _chunkSize);

        // Caches an absent chunk as well as a present one. _cursorValid used to mean "the lookup
        // found something", so every tap landing on an unpainted chunk missed the cache and repeated
        // the dictionary lookup — the common case on a large, mostly-empty canvas, where all four
        // bilinear taps of every texel paid a fresh lookup only to return zero.
        if (!_cursorValid || coord != _cursorCoord)
        {
            _table.TryGet(coord, out ImageChunk? chunk);
            _cursorPixels = chunk?.Pixels;
            _cursorCoord = coord;
            _cursorValid = true;
        }

        if (_cursorPixels is not { } pixels)
        {
            return 0.0f;
        }

        int localX = x - (coord.X * _chunkSize);
        int localY = y - (coord.Y * _chunkSize);
        int elementIndex = (((localY * _chunkSize) + localX) * _components) + component;
        return PaintImagePixelIO.Read(pixels, elementIndex, _format);
    }
}
