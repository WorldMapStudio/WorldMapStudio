using System;
using Godot;

namespace WorldMapStudio;

/// <summary>How one component of a <see cref="PaintImage"/>'s pixels is stored. <see cref="Byte"/> is
/// the original 8-bit-per-channel format, saturating at [0,255] the way a color or coverage mask always
/// has; <see cref="Float32"/> stores a raw 32-bit float per channel instead — unclamped, and free of the
/// 256-level banding a byte channel shows once painted values feed something continuous, like a
/// heightmap. Restricted to a scalar (<see cref="PaintImage.Components"/> == 1) image — see
/// <see cref="PaintImage.ConfigureNew"/> — so nothing downstream needs a float-aware color blend.</summary>
public enum PaintImagePixelFormat
{
    Byte,
    Float32,
}

/// <summary>Shared component-level read/write helpers so <see cref="PaintImage"/>, <see cref="ImageSampler"/>,
/// and <see cref="PaintImageTextures"/> agree on exactly one interpretation of a chunk's raw bytes per
/// <see cref="PaintImagePixelFormat"/>. Indexed by <em>element</em> — one component's slot, counted the
/// same way regardless of format — rather than a byte offset, so a caller never needs to know the
/// format's byte width itself.</summary>
public static class PaintImagePixelIO
{
    public static int ElementSize(this PaintImagePixelFormat format) => format == PaintImagePixelFormat.Float32 ? 4 : 1;

    /// <summary>The raw stored value at <paramref name="elementIndex"/> — 0..255 for
    /// <see cref="PaintImagePixelFormat.Byte"/>, unclamped for <see cref="PaintImagePixelFormat.Float32"/>.</summary>
    public static float Read(byte[] pixels, int elementIndex, PaintImagePixelFormat format) =>
        format == PaintImagePixelFormat.Float32
            ? BitConverter.ToSingle(pixels, elementIndex * 4)
            : pixels[elementIndex];

    public static void Write(byte[] pixels, int elementIndex, PaintImagePixelFormat format, float value)
    {
        if (format == PaintImagePixelFormat.Float32)
        {
            BitConverter.TryWriteBytes(pixels.AsSpan(elementIndex * 4, 4), value);
        }
        else
        {
            pixels[elementIndex] = (byte)Math.Clamp(Mathf.RoundToInt(value), 0, 255);
        }
    }
}
