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

    private ImageChunkCoord _cursorCoord;
    private byte[]? _cursorPixels;
    private bool _cursorValid;

    internal ImageSampler(ImageChunkTable table, int width, int height, int chunkSize)
    {
        _table = table;
        _width = width;
        _height = height;
        _chunkSize = chunkSize;
        _cursorCoord = default;
        _cursorPixels = null;
        _cursorValid = false;
    }

    /// <summary>Bilinear sample at normalized UV coordinates, returned in the 0..1 range.</summary>
    public float Sample(float u, float v)
    {
        float x = Mathf.Clamp((u * _width) - 0.5f, 0.0f, _width - 1.0f);
        float y = Mathf.Clamp((v * _height) - 0.5f, 0.0f, _height - 1.0f);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Math.Min(x0 + 1, _width - 1);
        int y1 = Math.Min(y0 + 1, _height - 1);
        float tx = x - x0;
        float ty = y - y0;

        float a = Mathf.Lerp(PixelAt(x0, y0), PixelAt(x1, y0), tx);
        float b = Mathf.Lerp(PixelAt(x0, y1), PixelAt(x1, y1), tx);
        return Mathf.Lerp(a, b, ty) / 255.0f;
    }

    private byte PixelAt(int x, int y)
    {
        var coord = new ImageChunkCoord(x / _chunkSize, y / _chunkSize);
        if (!_cursorValid || coord != _cursorCoord)
        {
            _cursorValid = _table.TryGet(coord, out ImageChunk? chunk);
            _cursorPixels = chunk?.Pixels;
            _cursorCoord = coord;
        }

        if (_cursorPixels is not { } pixels)
        {
            return 0;
        }

        int localX = x - (coord.X * _chunkSize);
        int localY = y - (coord.Y * _chunkSize);
        return pixels[(localY * _chunkSize) + localX];
    }
}
