using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Builds viewport-ready <see cref="ImageTexture"/>s from a <see cref="PaintImage"/>'s chunk data.
/// Per-chunk rather than whole-canvas: <see cref="ImageComponent"/> builds one node per resident chunk
/// (see <see cref="ImageComponent.BuildNode"/>), each textured independently, so texture memory scales
/// with how much of an image is actually resident rather than with its declared width/height — the
/// difference between a few chunks' worth of RGBA8 and, for a canvas at the far end of what
/// <c>.godot/ImageChunkPlan.md</c> aims for, tens of gigabytes for one texture.
///
/// <see cref="Overview"/> is the one place this still looks at more than a single chunk, and even then
/// only through <see cref="PaintImage.CreateSampler"/> at a capped resolution — never a dense
/// reconstruction of the whole canvas.
/// </summary>
public static class PaintImageTextures
{
    /// <summary>A true-grayscale texture (R=G=B=pixel, fully opaque) for one chunk — what
    /// <see cref="ImageComponent"/>'s Object display mode paints onto that chunk's quad.</summary>
    public static ImageTexture ChunkGrayscale(PaintImage image, ImageChunkCoord coord)
    {
        int size = image.ChunkSize;
        byte[] source = image.CopyChunkBytes(coord) ?? new byte[size * size];
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

        Image raw = Image.CreateFromData(size, size, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(raw);
    }

    /// <summary>A texture ramping from <paramref name="baseColor"/> (including its own alpha) at a
    /// source pixel value of 0 to <paramref name="fullColor"/> at 255, for one chunk — what
    /// <see cref="ImageComponent"/>'s LandscapeOverlay display mode projects as that chunk's own
    /// <see cref="Decal"/>. Independent alpha on each end lets the ramp be fully transparent to opaque,
    /// opaque to opaque (e.g. black to white), or anything between.</summary>
    public static ImageTexture ChunkTinted(PaintImage image, ImageChunkCoord coord, Color baseColor, Color fullColor)
    {
        int size = image.ChunkSize;
        byte[] source = image.CopyChunkBytes(coord) ?? new byte[size * size];
        byte[] rgba = new byte[source.Length * 4];

        // One lerp per possible byte value instead of one per pixel, amortizing the cost across every
        // pixel sharing a value — the same trick as the pre-chunking version of this method.
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

        Image raw = Image.CreateFromData(size, size, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(raw);
    }

    /// <summary>A single-channel texture capped at <paramref name="maxSize"/> per axis, downsampled
    /// from the whole canvas via <see cref="PaintImage.CreateSampler"/> — what a picker or images-list
    /// preview shows instead of the per-chunk viewport textures. Bounded regardless of canvas size, and
    /// correct (if incomplete) even for a chunk that is not currently resident, which samples as zero
    /// exactly as it does everywhere else a sampler is used.</summary>
    public static ImageTexture Overview(PaintImage image, int maxSize = 256)
    {
        int width = Math.Min(image.Width, maxSize);
        int height = Math.Min(image.Height, maxSize);
        ImageSampler sampler = image.CreateSampler();
        var pixels = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                pixels[row + x] = ToByte(sampler.Sample(u, v));
            }
        }

        Image raw = Image.CreateFromData(width, height, false, Image.Format.R8, pixels);
        return ImageTexture.CreateFromImage(raw);
    }

    private static byte ToByte(float channel) => (byte)Mathf.Clamp(Mathf.RoundToInt(channel * 255.0f), 0, 255);
}
