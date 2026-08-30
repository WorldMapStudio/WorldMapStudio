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

    /// <summary>A texture fading from fully transparent to <paramref name="color"/> as the source
    /// pixel value rises — what <see cref="ImageComponent"/>'s LandscapeOverlay display mode projects
    /// as a <see cref="Decal"/>.</summary>
    public static ImageTexture Tinted(PaintImage image, Color color)
    {
        byte[] source = image.CopyPixels();
        byte[] rgba = new byte[source.Length * 4];
        byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(color.R * 255.0f), 0, 255);
        byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(color.G * 255.0f), 0, 255);
        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(color.B * 255.0f), 0, 255);
        for (int i = 0; i < source.Length; i++)
        {
            int o = i * 4;
            rgba[o] = r;
            rgba[o + 1] = g;
            rgba[o + 2] = b;
            rgba[o + 3] = source[i];
        }

        Image raw = Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(raw);
    }
}
