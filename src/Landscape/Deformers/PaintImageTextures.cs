using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Builds viewport-ready textures from a <see cref="PaintImage"/>'s chunk data. Per-chunk rather than
/// whole-canvas: <see cref="ImageComponent"/> builds one node per resident chunk (see
/// <see cref="ImageComponent.BuildNode"/>), each textured independently, so texture memory scales with
/// how much of an image is actually resident rather than with its declared width/height.
///
/// Each builder comes in two forms: a <c>...Image</c> returning a raw <see cref="Image"/>, and a
/// wrapper making a fresh <see cref="ImageTexture"/> from it. The split exists so a repaint can call
/// <see cref="ImageTexture.Update(Image)"/> on the texture it already has — re-uploading one chunk's
/// pixels rather than allocating a new GPU texture per chunk per frame, which is what a paint stroke
/// would otherwise cost.
///
/// <see cref="Overview"/> is the one place this looks at more than a single chunk, and even then only
/// through <see cref="PaintImage.CreateSampler"/> at a capped resolution — never a dense
/// reconstruction of the whole canvas.
/// </summary>
public static class PaintImageTextures
{
    /// <summary>A true-grayscale image (R=G=B=pixel, fully opaque) for one chunk — what
    /// <see cref="ImageComponent"/>'s Object display mode paints onto that chunk's quad.</summary>
    public static Image ChunkGrayscaleImage(PaintImage image, ImageChunkCoord coord)
    {
        (int width, int height, int stride, byte[] source) = ChunkSource(image, coord);
        byte[] rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * stride;
            int targetRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                byte value = source[sourceRow + x];
                int o = targetRow + (x * 4);
                rgba[o] = value;
                rgba[o + 1] = value;
                rgba[o + 2] = value;
                rgba[o + 3] = 255;
            }
        }

        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }

    public static ImageTexture ChunkGrayscale(PaintImage image, ImageChunkCoord coord) =>
        ImageTexture.CreateFromImage(ChunkGrayscaleImage(image, coord));

    /// <summary>An image ramping from <paramref name="baseColor"/> (including its own alpha) at a
    /// source pixel value of 0 to <paramref name="fullColor"/> at 255, for one chunk — what
    /// <see cref="ImageComponent"/>'s LandscapeOverlay display mode projects as that chunk's own
    /// <see cref="Decal"/>. Independent alpha on each end lets the ramp be fully transparent to opaque,
    /// opaque to opaque (e.g. black to white), or anything between.</summary>
    public static Image ChunkTintedImage(PaintImage image, ImageChunkCoord coord, Color baseColor, Color fullColor)
    {
        (int width, int height, int stride, byte[] source) = ChunkSource(image, coord);
        byte[] rgba = new byte[width * height * 4];

        // One lerp per possible byte value instead of one per pixel, amortizing the cost across every
        // pixel sharing a value.
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

        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * stride;
            int targetRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                (byte r, byte g, byte b, byte a) = ramp[source[sourceRow + x]];
                int o = targetRow + (x * 4);
                rgba[o] = r;
                rgba[o + 1] = g;
                rgba[o + 2] = b;
                rgba[o + 3] = a;
            }
        }

        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }

    public static ImageTexture ChunkTinted(PaintImage image, ImageChunkCoord coord, Color baseColor, Color fullColor) =>
        ImageTexture.CreateFromImage(ChunkTintedImage(image, coord, baseColor, fullColor));

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

    /// <summary>One chunk's pixels plus the size of the region actually inside the canvas. A chunk on
    /// the right or bottom edge is only partly covered when <see cref="PaintImage.ChunkSize"/> does not
    /// divide the canvas evenly; its texture is built at the covered size, not the full tile size, so
    /// the quad it lands on shows those pixels 1:1 instead of stretching a full tile (padding included)
    /// across a narrower footprint. <c>stride</c> is the buffer's real row length, which stays the full
    /// tile size regardless.</summary>
    private static (int Width, int Height, int Stride, byte[] Source) ChunkSource(PaintImage image, ImageChunkCoord coord)
    {
        int size = image.ChunkSize;
        int width = Math.Clamp(image.Width - (coord.X * size), 0, size);
        int height = Math.Clamp(image.Height - (coord.Y * size), 0, size);
        byte[] source = image.CopyChunkBytes(coord) ?? new byte[size * size];
        return (Math.Max(1, width), Math.Max(1, height), size, source);
    }

    private static byte ToByte(float channel) => (byte)Mathf.Clamp(Mathf.RoundToInt(channel * 255.0f), 0, 255);
}
