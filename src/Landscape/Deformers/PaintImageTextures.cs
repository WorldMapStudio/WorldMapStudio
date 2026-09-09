using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
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
    /// <summary>One chunk's own pixels, widened to RGBA8: a scalar image becomes true grayscale
    /// (R=G=B=pixel, fully opaque) — what <see cref="ImageComponent"/>'s Object display mode painted
    /// onto that chunk's quad before an image could carry color — an RGB image is opaque with its own
    /// color, and an RGBA image copies straight through. The same widening
    /// <see cref="LandscapeChannelPool.SampleColor"/> applies to a channel and <see cref="ImageSampler.SampleColor"/>
    /// applies to a sample.</summary>
    public static Image ChunkImage(PaintImage image, ImageChunkCoord coord)
    {
        byte[] rgba = WriteChunkRgba(image, coord, null, out int width, out int height);
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }

    /// <summary>
    /// <see cref="ChunkImage"/> into a caller-owned buffer: fills <paramref name="destination"/> with
    /// the widened pixels, reusing it when it is already the right length and allocating otherwise,
    /// and returns whichever buffer now holds them.
    ///
    /// This form exists because a stroke re-uploads every chunk the brush reached on every frame it
    /// drags. A brush wide enough to span several chunks otherwise allocates a full-tile array and a
    /// fresh Godot <see cref="Image"/> per chunk per frame, which costs more than the stamp that
    /// dirtied them — see <see cref="ImageComponent.SyncChunkNodes"/> for the reuse this enables.
    /// </summary>
    public static byte[] WriteChunkRgba(PaintImage image, ImageChunkCoord coord, byte[]? destination, out int width, out int height)
    {
        (width, height, int stride, byte[] source) = ChunkSource(image, coord);
        byte[] rgba = Fit(destination, width * height * 4);
        int components = image.Components;

        // One uint per pixel rather than four bytes: every widening below settles all four channels of
        // a pixel at once, and the scalar cases settle them to four equal ones.
        Span<uint> packed = MemoryMarshal.Cast<byte, uint>(rgba.AsSpan(0, width * height * 4));

        // Float32 is scalar-only (see PaintImage.ConfigureNew), so it is always the grayscale case.
        if (image.Format == PaintImagePixelFormat.Float32)
        {
            ReadOnlySpan<float> values = MemoryMarshal.Cast<byte, float>(source);
            for (int y = 0; y < height; y++)
            {
                ExpandGrayRow(values.Slice(y * stride, width), packed.Slice(y * width, width));
            }

            return rgba;
        }

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> sourceRow = source.AsSpan(y * stride * components, width * components);
            Span<uint> targetRow = packed.Slice(y * width, width);
            switch (components)
            {
                case 1:
                    for (int x = 0; x < width; x++)
                    {
                        targetRow[x] = sourceRow[x] * 0x01010101u;
                    }

                    break;
                case 4:
                    sourceRow.CopyTo(MemoryMarshal.AsBytes(targetRow));
                    break;
                default:
                    for (int x = 0; x < width; x++)
                    {
                        int s = x * components;
                        targetRow[x] = sourceRow[s] | ((uint)sourceRow[s + 1] << 8) |
                            ((uint)sourceRow[s + 2] << 16) | 0xFF000000u;
                    }

                    break;
            }
        }

        return rgba;
    }

    /// <summary>One row of scalar float pixels widened to opaque grayscale — four equal bytes per
    /// pixel, packed as one uint. Values outside [0,1] saturate, the same squash <see cref="ReadByte"/>
    /// applies. Eight-wide where AVX2 is available: this row is essentially the whole cost of
    /// previewing a painted Float32 chunk, and a chunk is a full tile of them.</summary>
    private static void ExpandGrayRow(ReadOnlySpan<float> source, Span<uint> destination)
    {
        ref float src = ref MemoryMarshal.GetReference(source);
        ref uint dst = ref MemoryMarshal.GetReference(destination);
        int n = destination.Length;
        int i = 0;

        if (Avx2.IsSupported)
        {
            Vector256<float> scale = Vector256.Create(255.0f);
            Vector256<float> floor = Vector256<float>.Zero;
            Vector256<float> ceiling = Vector256.Create(255.0f);
            Vector256<int> broadcast = Vector256.Create(0x01010101);

            for (; i <= n - Vector256<float>.Count; i += Vector256<float>.Count)
            {
                // vmaxps yields its second operand when either is NaN, so a NaN pixel saturates to 0
                // here rather than reaching the convert as an out-of-range lane. The convert itself
                // rounds half to even, matching the scalar tail.
                Vector256<float> scaled = Avx.Multiply(Vector256.LoadUnsafe(ref src, (nuint)i), scale);
                Vector256<int> level = Avx.ConvertToVector256Int32(Avx.Min(Avx.Max(scaled, floor), ceiling));
                Avx2.MultiplyLow(level, broadcast).AsUInt32().StoreUnsafe(ref dst, (nuint)i);
            }
        }

        for (; i < n; i++)
        {
            Unsafe.Add(ref dst, i) = ToByte(Unsafe.Add(ref src, i)) * 0x01010101u;
        }
    }

    private static byte[] Fit(byte[]? buffer, int length) =>
        buffer is { } existing && existing.Length == length ? existing : new byte[length];

    public static ImageTexture ChunkTexture(PaintImage image, ImageChunkCoord coord) =>
        ImageTexture.CreateFromImage(ChunkImage(image, coord));

    /// <summary>An image ramping from <paramref name="baseColor"/> (including its own alpha) at a
    /// source pixel value of 0 to <paramref name="fullColor"/> at 255, for one chunk — what
    /// <see cref="ImageComponent"/>'s LandscapeOverlay display mode projects as that chunk's own
    /// <see cref="Decal"/>. Independent alpha on each end lets the ramp be fully transparent to opaque,
    /// opaque to opaque (e.g. black to white), or anything between.</summary>
    public static Image ChunkTintedImage(PaintImage image, ImageChunkCoord coord, Color baseColor, Color fullColor)
    {
        byte[] rgba = WriteChunkTintedRgba(image, coord, baseColor, fullColor, null, out int width, out int height);
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }

    /// <summary><see cref="ChunkTintedImage"/> into a caller-owned buffer, for the same reason
    /// <see cref="WriteChunkRgba"/> has one.</summary>
    public static byte[] WriteChunkTintedRgba(PaintImage image, ImageChunkCoord coord, Color baseColor, Color fullColor, byte[]? destination, out int width, out int height)
    {
        (width, height, int stride, byte[] source) = ChunkSource(image, coord);
        byte[] rgba = Fit(destination, width * height * 4);

        // One lerp per possible byte value instead of one per pixel, amortizing the cost across every
        // pixel sharing a value.
        Span<uint> ramp = stackalloc uint[256];
        for (int i = 0; i < ramp.Length; i++)
        {
            float t = i / 255.0f;
            ramp[i] = ToByte(Mathf.Lerp(baseColor.R, fullColor.R, t)) |
                ((uint)ToByte(Mathf.Lerp(baseColor.G, fullColor.G, t)) << 8) |
                ((uint)ToByte(Mathf.Lerp(baseColor.B, fullColor.B, t)) << 16) |
                ((uint)ToByte(Mathf.Lerp(baseColor.A, fullColor.A, t)) << 24);
        }

        Span<uint> packed = MemoryMarshal.Cast<byte, uint>(rgba.AsSpan(0, width * height * 4));
        PaintImagePixelFormat format = image.Format;
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * stride;
            Span<uint> targetRow = packed.Slice(y * width, width);
            for (int x = 0; x < width; x++)
            {
                targetRow[x] = ramp[ReadByte(source, sourceRow + x, format)];
            }
        }

        return rgba;
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

        if (image.Components == 1)
        {
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

        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color color = sampler.SampleColor(u, v);
                int o = row + (x * 4);
                rgba[o] = ToByte(color.R);
                rgba[o + 1] = ToByte(color.G);
                rgba[o + 2] = ToByte(color.B);
                rgba[o + 3] = ToByte(color.A);
            }
        }

        Image colorRaw = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        return ImageTexture.CreateFromImage(colorRaw);
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
        byte[] source = image.CopyChunkBytes(coord) ?? new byte[size * size * image.Stride];
        return (Math.Max(1, width), Math.Max(1, height), size, source);
    }

    /// <summary>One component's value at <paramref name="elementIndex"/>, widened to a display byte —
    /// the raw byte as-is for <see cref="PaintImagePixelFormat.Byte"/>, or a stored float clamped into
    /// [0,255] for <see cref="PaintImagePixelFormat.Float32"/>, the same way an unbounded scalar (e.g. a
    /// heightmap image) is squashed down for a preview everywhere else a sampler is used.</summary>
    private static byte ReadByte(byte[] source, int elementIndex, PaintImagePixelFormat format) =>
        format == PaintImagePixelFormat.Float32
            ? ToByte(PaintImagePixelIO.Read(source, elementIndex, format))
            : source[elementIndex];

    // Written out rather than Mathf.Clamp(Mathf.RoundToInt(...)): both of those are uninlined
    // cross-assembly calls and this runs for every texel of every chunk a stroke touches, every frame.
    // MathF.Round keeps the round-half-to-even the Godot pair had; NaN saturates to 0.
    private static byte ToByte(float channel)
    {
        float scaled = channel * 255.0f;
        return scaled >= 255.0f ? (byte)255 : scaled > 0.0f ? (byte)MathF.Round(scaled) : (byte)0;
    }
}
