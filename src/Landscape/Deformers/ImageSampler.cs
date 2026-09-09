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

    // The cached chunk's pixel-space origin, so a tap can be tested against its span with two int
    // comparisons instead of rebuilding an ImageChunkCoord and comparing that.
    private int _cursorBaseX;
    private int _cursorBaseY;
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
        _cursorBaseX = 0;
        _cursorBaseY = 0;
        _cursorPixels = null;
        _cursorValid = false;
    }

    /// <summary>Bilinear sample of the image's first component, returned in the 0..1 range for a
    /// <see cref="PaintImagePixelFormat.Byte"/> image — the whole value for a scalar image, its red
    /// component for a color one — or unclamped, straight from storage, for a
    /// <see cref="PaintImagePixelFormat.Float32"/> one.</summary>
    public float Sample(float u, float v) => Resolve(u, v).Read(0);

    /// <summary>Bilinear sample as a color: native for a 3/4-component image, or the value replicated
    /// into every channel (fully opaque) for a scalar one — the same widening
    /// <see cref="LandscapeChannelPool.SampleColor"/> applies to a channel.</summary>
    public Color SampleColor(float u, float v)
    {
        Bilinear taps = Resolve(u, v);
        float r = taps.Read(0);
        if (_components == 1)
        {
            return new Color(r, r, r, r);
        }

        float g = taps.Read(1);
        float b = taps.Read(2);
        return _components == 3 ? new Color(r, g, b, 1.0f) : new Color(r, g, b, taps.Read(3));
    }

    // The four bilinear corners of one tap, resolved once. Reading a second component of the same
    // sample is then four array reads rather than another round of clamping, flooring and chunk
    // lookups — which is what a 4-component image cost per texel.
    private Bilinear Resolve(float u, float v)
    {
        float x = Mathf.Clamp((u * _width) - 0.5f, 0.0f, _width - 1.0f);
        float y = Mathf.Clamp((v * _height) - 0.5f, 0.0f, _height - 1.0f);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Math.Min(x0 + 1, _width - 1);
        int y1 = Math.Min(y0 + 1, _height - 1);

        return new Bilinear(
            PixelAt(x0, y0),
            PixelAt(x1, y0),
            PixelAt(x0, y1),
            PixelAt(x1, y1),
            x - x0,
            y - y0,
            _components,
            _format);
    }

    private Tap PixelAt(int x, int y)
    {
        // A scanline of taps stays inside one chunk for a run of _chunkSize pixels, so only a tap that
        // actually leaves the cached chunk's pixel span pays the divisions and the table lookup.
        // Caches an absent chunk as well as a present one — on a large, mostly-empty canvas every tap
        // otherwise repeated the lookup only to return zero.
        if (!_cursorValid ||
            x < _cursorBaseX || x >= _cursorBaseX + _chunkSize ||
            y < _cursorBaseY || y >= _cursorBaseY + _chunkSize)
        {
            int chunkX = x / _chunkSize;
            int chunkY = y / _chunkSize;
            _table.TryGet(new ImageChunkCoord(chunkX, chunkY), out ImageChunk? chunk);
            _cursorPixels = chunk?.Pixels;
            _cursorBaseX = chunkX * _chunkSize;
            _cursorBaseY = chunkY * _chunkSize;
            _cursorValid = true;
        }

        if (_cursorPixels is not { } pixels)
        {
            return default;
        }

        int localX = x - _cursorBaseX;
        int localY = y - _cursorBaseY;
        return new Tap(pixels, ((localY * _chunkSize) + localX) * _components);
    }

    // One bilinear corner: the chunk pixels that own it and the element index its first component sits
    // at, or no pixels at all for a corner in an absent chunk, which reads as zero.
    private readonly struct Tap(byte[]? pixels, int index)
    {
        public float Read(int component, PaintImagePixelFormat format) =>
            pixels is { } texels ? PaintImagePixelIO.Read(texels, index + component, format) : 0.0f;
    }

    private readonly struct Bilinear(
        Tap topLeft, Tap topRight, Tap bottomLeft, Tap bottomRight, float tx, float ty, int components, PaintImagePixelFormat format)
    {
        public float Read(int component)
        {
            if (component >= components)
            {
                return 0.0f;
            }

            float a = Mathf.Lerp(topLeft.Read(component, format), topRight.Read(component, format), tx);
            float b = Mathf.Lerp(bottomLeft.Read(component, format), bottomRight.Read(component, format), tx);
            float raw = Mathf.Lerp(a, b, ty);
            return format == PaintImagePixelFormat.Float32 ? raw : raw / 255.0f;
        }
    }
}
