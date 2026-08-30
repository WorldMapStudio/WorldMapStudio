using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Builds viewport-ready <see cref="ImageTexture"/>s from a <see cref="PaintImage"/>'s raw pixel
/// buffer. A tight byte-array expansion rather than a per-pixel <c>SetPixel</c> loop, since this
/// buffer can be up to 4096x4096 — see <see cref="ImageSelectionOperation"/> for the same
/// <see cref="Image.CreateFromData"/> pattern used for the picker's own preview.
/// </summary>
public static class PaintImageTextures
{
    /// <summary>A true-grayscale texture (R=G=B=pixel, fully opaque) — what
    /// <see cref="ImageComponent"/>'s Object display mode paints onto its mesh.</summary>
    public static ImageTexture Grayscale(PaintImage image)
    {
        byte[] source = image.CopyPixels();
        byte[] rgba = new byte[source.Length * 4];
        for (int i = 0; i < source.Length; i++)
        {
            int o = i * 4;
            byte value = source[i];
            rgba[o] = value;
            rgba[o + 1] = value;
            rgba[o + 2] = value;
            rgba[o + 3] = 255;
        }

        Image raw = Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(raw);
    }

    /// <summary>A texture ramping from <paramref name="baseColor"/> (including its own alpha) at a
    /// source pixel value of 0 to <paramref name="fullColor"/> at 255 — what
    /// <see cref="ImageComponent"/>'s LandscapeOverlay display mode projects as a <see cref="Decal"/>.
    /// Independent alpha on each end lets the ramp be fully transparent to opaque, opaque to opaque
    /// (e.g. black to white), or anything between.</summary>
    public static ImageTexture Tinted(PaintImage image, Color baseColor, Color fullColor)
    {
        byte[] source = image.CopyPixels();
        byte[] rgba = new byte[source.Length * 4];

        // One lerp per possible byte value instead of one per pixel — this buffer can be up to
        // 4096x4096, so a 256-entry ramp amortizes the cost across every pixel sharing a value.
        Span<(byte R, byte G, byte B, byte A)> ramp = stackalloc (byte, byte, byte, byte)[256];
        for (int i = 0; i < ramp.Length; i++)
        {
            float t = i / 255.0f;
            ramp[i] = (
                ToByte(Mathf.Lerp(baseColor.R, fullColor.R, t)),
                ToByte(Mathf.Lerp(baseColor.G, fullColor.G, t)),
                ToByte(Mathf.Lerp(baseColor.B, fullColor.B, t)),
                ToByte(Mathf.Lerp(baseColor.A, fullColor.A, t)));
        }

        for (int i = 0; i < source.Length; i++)
        {
            int o = i * 4;
            (byte r, byte g, byte b, byte a) = ramp[source[i]];
            rgba[o] = r;
            rgba[o + 1] = g;
            rgba[o + 2] = b;
            rgba[o + 3] = a;
        }

        Image raw = Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(raw);
    }

    private static byte ToByte(float channel) => (byte)Mathf.Clamp(Mathf.RoundToInt(channel * 255.0f), 0, 255);
}
