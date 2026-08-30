using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A named, saved raster — width, height, and a painted grayscale byte buffer. Catalog-backed like
/// <see cref="ProceduralModel"/>, so an <see cref="ImageComponent"/> merely references one by id
/// instead of owning the data: many placements can share one image, and painting it from any of
/// them updates every placement.
///
/// Named <c>PaintImage</c> rather than the more obvious <c>Image</c> because this type lives in the
/// same namespace as, and every file here brings in with <c>using Godot;</c>, Godot's own
/// <see cref="Godot.Image"/> — a bare <c>Image</c> here would silently shadow it everywhere.
/// </summary>
public sealed class PaintImage : CatalogEntity, IKeyedCatalogEntity
{
    private string _name = "Image";
    private int _width = 256;
    private int _height = 256;
    private byte[] _pixels = new byte[256 * 256];

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            Revision++;
        }
    }

    public int Width => _width;

    public int Height => _height;

    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>
    /// Bumped by every mutation. A plain counter rather than a content hash — unlike
    /// <see cref="ProceduralModel.NetworkFingerprint"/>, an image's buffer can be megabytes, and a
    /// paint stroke replaces it every frame while dragging, so hashing the bytes on every change
    /// would be far too costly to pay continuously. Identity plus this counter is enough to notice
    /// "this image changed since I last looked" without needing to know how.
    /// </summary>
    public int Revision { get; private set; }

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    public byte[] CopyPixels() => (byte[])_pixels.Clone();

    /// <summary>Replaces the pixel buffer wholesale, keeping the current resolution. Falls back to a
    /// blank buffer if the given data does not match <see cref="Width"/> x <see cref="Height"/>.</summary>
    public void ReplacePixels(byte[] pixels)
    {
        _pixels = pixels.Length == _width * _height ? (byte[])pixels.Clone() : new byte[_width * _height];
        Revision++;
    }

    /// <summary>Changes resolution, bilinear-resampling the existing content into the new size.</summary>
    public void Resize(int width, int height)
    {
        width = Math.Clamp(width, 1, 4096);
        height = Math.Clamp(height, 1, 4096);
        if (width == _width && height == _height)
        {
            return;
        }

        byte[] resized = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            int oldY = Math.Clamp((int)((y + 0.5f) * _height / height), 0, _height - 1);
            for (int x = 0; x < width; x++)
            {
                int oldX = Math.Clamp((int)((x + 0.5f) * _width / width), 0, _width - 1);
                resized[(y * width) + x] = _pixels[(oldY * _width) + oldX];
            }
        }

        _width = width;
        _height = height;
        _pixels = resized;
        Revision++;
    }

    /// <summary>Replaces both resolution and content at once — the shape a persistence load and an
    /// undo apply/revert both need, as opposed to <see cref="Resize"/>'s resampling.</summary>
    public void LoadPixels(int width, int height, byte[] pixels)
    {
        _width = Math.Clamp(width, 1, 4096);
        _height = Math.Clamp(height, 1, 4096);
        _pixels = pixels.Length == _width * _height ? (byte[])pixels.Clone() : new byte[_width * _height];
        Revision++;
    }

    /// <summary>Stamps a soft circular brush centred at normalized UV coordinates, with the brush
    /// radius given in the same normalized units along each axis (a caller with a non-square world
    /// footprint passes different radii per axis so the brush reads as round in world space).</summary>
    public bool Paint(float u, float v, float radiusU, float radiusV, float opacity, bool erase)
    {
        if (radiusU <= 0.0f || radiusV <= 0.0f)
        {
            return false;
        }

        int minX = Math.Clamp(Mathf.FloorToInt((u - radiusU) * _width), 0, _width - 1);
        int maxX = Math.Clamp(Mathf.CeilToInt((u + radiusU) * _width), 0, _width - 1);
        int minY = Math.Clamp(Mathf.FloorToInt((v - radiusV) * _height), 0, _height - 1);
        int maxY = Math.Clamp(Mathf.CeilToInt((v + radiusV) * _height), 0, _height - 1);
        byte amount = (byte)Math.Clamp(Mathf.RoundToInt(Mathf.Clamp(opacity, 0.0f, 1.0f) * 255.0f), 0, 255);
        bool changed = false;

        for (int py = minY; py <= maxY; py++)
        {
            float cy = (py + 0.5f) / _height;
            float dy = (cy - v) / radiusV;
            for (int px = minX; px <= maxX; px++)
            {
                float cx = (px + 0.5f) / _width;
                float dx = (cx - u) / radiusU;
                float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                if (distance > 1.0f)
                {
                    continue;
                }

                float weight = Mathf.SmoothStep(0.0f, 1.0f, 1.0f - distance);
                int delta = Mathf.RoundToInt(amount * weight);
                int index = (py * _width) + px;
                byte before = _pixels[index];
                byte after = erase
                    ? (byte)Math.Max(0, before - delta)
                    : (byte)Math.Min(255, before + delta);

                if (after != before)
                {
                    _pixels[index] = after;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Revision++;
        }

        return changed;
    }

    /// <summary>Bilinear sample at normalized UV coordinates, returned in the 0..1 range.</summary>
    public float Sample(float u, float v)
    {
        float x = Mathf.Clamp(u * _width - 0.5f, 0.0f, _width - 1.0f);
        float y = Mathf.Clamp(v * _height - 0.5f, 0.0f, _height - 1.0f);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Math.Min(x0 + 1, _width - 1);
        int y1 = Math.Min(y0 + 1, _height - 1);
        float tx = x - x0;
        float ty = y - y0;

        float a = Mathf.Lerp(_pixels[(y0 * _width) + x0], _pixels[(y0 * _width) + x1], tx);
        float b = Mathf.Lerp(_pixels[(y1 * _width) + x0], _pixels[(y1 * _width) + x1], tx);
        return Mathf.Lerp(a, b, ty) / 255.0f;
    }
}
