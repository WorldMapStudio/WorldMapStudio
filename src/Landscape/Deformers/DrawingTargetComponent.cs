using System;
using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

public sealed class DrawingTargetComponent : SceneComponent, ISceneBoundsProvider, ITransformPolicy, ILandscapeDeformer, IDrawingTargetComponent
{
    private const float BoundsHeight = 2.0f;

    private int _width = 256;
    private int _height = 256;
    private float _worldSizeX = 64.0f;
    private float _worldSizeZ = 64.0f;
    private byte[] _pixels = new byte[256 * 256];
    private int _paintVersion;

    public int Width
    {
        get => _width;
        private set => _width = Math.Clamp(value, 1, 4096);
    }

    public int Height
    {
        get => _height;
        private set => _height = Math.Clamp(value, 1, 4096);
    }

    public float WorldSizeX
    {
        get => _worldSizeX;
        set => _worldSizeX = Mathf.Max(0.5f, value);
    }

    public float WorldSizeZ
    {
        get => _worldSizeZ;
        set => _worldSizeZ = Mathf.Max(0.5f, value);
    }

    public float Strength { get; set; } = 1.0f;

    public string Channel { get; set; } = "";

    public ReadOnlySpan<byte> Pixels => _pixels;

    public override string TypeId => "drawing-target";

    public override string DisplayName => "Drawing Target";

    public SelfRotation SelfRotation => SelfRotation.HeightOnly;

    public bool UsesTerrainHeight => true;

    public Aabb LocalBounds => new(
        new Vector3(-WorldSizeX * 0.5f, -BoundsHeight * 0.5f, -WorldSizeZ * 0.5f),
        new Vector3(WorldSizeX, BoundsHeight, WorldSizeZ));

    public string DeformerKey => Entity.RecordId is { } id
        ? $"entity:{id}:drawing-target"
        : $"entity:new:{Entity.Id.Value}:drawing-target";

    public Aabb InfluenceBounds => Entity.Transform * LocalBounds;

    public override int ContentVersion
    {
        get
        {
            var hash = new HashCode();
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(WorldSizeX);
            hash.Add(WorldSizeZ);
            hash.Add(Strength);
            hash.Add(Channel);
            hash.Add(_paintVersion);
            return hash.ToHashCode();
        }
    }

    public override SceneComponent Clone()
    {
        var clone = new DrawingTargetComponent
        {
            WorldSizeX = WorldSizeX,
            WorldSizeZ = WorldSizeZ,
            Strength = Strength,
            Channel = Channel,
        };
        clone.LoadPixels(Width, Height, CopyPixels());
        return clone;
    }

    public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context) => [];

    public void Rasterize(in LandscapeRasterContext context)
    {
        if (context.Channel(Channel) is not { } channel || context.Buffer(Channel) is not { } buffer)
        {
            return;
        }

        int resolution = channel.Resolution;
        Transform3D inverse = Entity.Transform.AffineInverse();

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector3 local = inverse * context.TexelCentre(resolution, x, y);
                if (!TryLocalToUv(local, out float u, out float v))
                {
                    continue;
                }

                float value = Sample(u, v) * Strength;
                if (value <= 0.0f)
                {
                    continue;
                }

                int index = (y * resolution) + x;
                buffer[index] = Mathf.Min(1.0f, Mathf.Max(buffer[index], value));
            }
        }
    }

    public bool Paint(Vector3 local, float radius, float opacity, bool erase)
    {
        if (!TryLocalToUv(local, out float u, out float v))
        {
            return false;
        }

        float radiusX = radius / WorldSizeX;
        float radiusY = radius / WorldSizeZ;
        if (radiusX <= 0.0f || radiusY <= 0.0f)
        {
            return false;
        }

        int minX = Math.Clamp(Mathf.FloorToInt((u - radiusX) * Width), 0, Width - 1);
        int maxX = Math.Clamp(Mathf.CeilToInt((u + radiusX) * Width), 0, Width - 1);
        int minY = Math.Clamp(Mathf.FloorToInt((v - radiusY) * Height), 0, Height - 1);
        int maxY = Math.Clamp(Mathf.CeilToInt((v + radiusY) * Height), 0, Height - 1);
        byte amount = (byte)Math.Clamp(Mathf.RoundToInt(Mathf.Clamp(opacity, 0.0f, 1.0f) * 255.0f), 0, 255);
        bool changed = false;

        for (int py = minY; py <= maxY; py++)
        {
            float cy = (py + 0.5f) / Height;
            float dy = (cy - v) / radiusY;
            for (int px = minX; px <= maxX; px++)
            {
                float cx = (px + 0.5f) / Width;
                float dx = (cx - u) / radiusX;
                float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                if (distance > 1.0f)
                {
                    continue;
                }

                float weight = Mathf.SmoothStep(0.0f, 1.0f, 1.0f - distance);
                int delta = Mathf.RoundToInt(amount * weight);
                int index = (py * Width) + px;
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
            _paintVersion++;
        }

        return changed;
    }

    public byte[] CopyPixels() => (byte[])_pixels.Clone();

    public void ReplacePixels(byte[] pixels)
    {
        if (pixels.Length != Width * Height)
        {
            throw new ArgumentException("Pixel buffer does not match the drawing target resolution.", nameof(pixels));
        }

        _pixels = (byte[])pixels.Clone();
        _paintVersion++;
    }

    public void Resize(int width, int height)
    {
        width = Math.Clamp(width, 1, 4096);
        height = Math.Clamp(height, 1, 4096);
        if (width == Width && height == Height)
        {
            return;
        }

        byte[] resized = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            int oldY = Math.Clamp((int)((y + 0.5f) * Height / height), 0, Height - 1);
            for (int x = 0; x < width; x++)
            {
                int oldX = Math.Clamp((int)((x + 0.5f) * Width / width), 0, Width - 1);
                resized[(y * width) + x] = _pixels[(oldY * Width) + oldX];
            }
        }

        Width = width;
        Height = height;
        _pixels = resized;
        _paintVersion++;
    }

    public void LoadPixels(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels.Length == Width * Height ? (byte[])pixels.Clone() : new byte[Width * Height];
        _paintVersion++;
    }

    private bool TryLocalToUv(Vector3 local, out float u, out float v)
    {
        u = (local.X / WorldSizeX) + 0.5f;
        v = (local.Z / WorldSizeZ) + 0.5f;
        return u >= 0.0f && u <= 1.0f && v >= 0.0f && v <= 1.0f;
    }

    private float Sample(float u, float v)
    {
        float x = Mathf.Clamp(u * Width - 0.5f, 0.0f, Width - 1.0f);
        float y = Mathf.Clamp(v * Height - 0.5f, 0.0f, Height - 1.0f);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Math.Min(x0 + 1, Width - 1);
        int y1 = Math.Min(y0 + 1, Height - 1);
        float tx = x - x0;
        float ty = y - y0;

        float a = Mathf.Lerp(_pixels[(y0 * Width) + x0], _pixels[(y0 * Width) + x1], tx);
        float b = Mathf.Lerp(_pixels[(y1 * Width) + x0], _pixels[(y1 * Width) + x1], tx);
        return Mathf.Lerp(a, b, ty) / 255.0f;
    }
}
