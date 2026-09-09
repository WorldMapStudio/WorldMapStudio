using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Encodes and decodes one chunk of a disk-backed <see cref="PaintImage"/> as a standalone image file
/// — PNG for a <see cref="PaintImagePixelFormat.Byte"/> image (<c>L8</c> / <c>RGB8</c> / <c>RGBA8</c>
/// by component count), OpenEXR for a <see cref="PaintImagePixelFormat.Float32"/> one (PNG has no float
/// pixel format).
///
/// A chunk's in-memory buffer is always the image's full <see cref="PaintImage.ChunkSize"/> tile;
/// <see cref="Encode"/> crops it to the live <paramref name="fillWidth"/> x <paramref name="fillHeight"/>
/// sub-rect so an edge tile writes a file the natural size a user would expect, and <see cref="Decode"/>
/// expands whatever it reads back into a fresh zero-padded tile buffer — the same
/// <c>chunkSize * chunkSize * stride</c> length invariant <see cref="ImageChunkCodec.Decode"/> keeps.
/// </summary>
public static class ImageDiskCodec
{
    public static Image.Format GodotFormat(int components, PaintImagePixelFormat format) => format == PaintImagePixelFormat.Float32
        ? Image.Format.Rf
        : components switch
        {
            4 => Image.Format.Rgba8,
            3 => Image.Format.Rgb8,
            _ => Image.Format.L8,
        };

    public static byte[] Encode(byte[] tile, int chunkSize, int stride, int components, PaintImagePixelFormat format, int fillWidth, int fillHeight)
    {
        int width = Math.Clamp(fillWidth, 1, chunkSize);
        int height = Math.Clamp(fillHeight, 1, chunkSize);
        byte[] compact = CompactRows(tile, chunkSize, stride, width, height);

        var image = Image.CreateEmpty(width, height, false, GodotFormat(components, format));
        image.SetData(width, height, false, GodotFormat(components, format), compact);

        return format == PaintImagePixelFormat.Float32
            ? image.SaveExrToBuffer(grayscale: true)
            : image.SavePngToBuffer();
    }

    public static byte[] Decode(byte[] fileBytes, string extension, int chunkSize, int stride, int components, PaintImagePixelFormat format)
    {
        var image = new Image();
        Error error = extension.ToLowerInvariant() switch
        {
            ".exr" => image.LoadExrFromBuffer(fileBytes),
            _ => image.LoadPngFromBuffer(fileBytes),
        };

        var tile = new byte[chunkSize * chunkSize * stride];
        if (error != Error.Ok)
        {
            return tile;
        }

        Image.Format target = GodotFormat(components, format);
        if (image.GetFormat() != target)
        {
            image.Convert(target);
        }

        int width = Math.Min(image.GetWidth(), chunkSize);
        int height = Math.Min(image.GetHeight(), chunkSize);
        ExpandRows(image.GetData(), tile, chunkSize, stride, width, height, image.GetWidth());
        return tile;
    }

    // Gathers the top-left width x height sub-rect out of a chunkSize-strided tile into a dense buffer
    // Godot's SetData can take (its rows are width * stride, not chunkSize * stride).
    private static byte[] CompactRows(byte[] tile, int chunkSize, int stride, int width, int height)
    {
        if (width == chunkSize && height == chunkSize)
        {
            return tile;
        }

        var compact = new byte[width * height * stride];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(tile, y * chunkSize * stride, compact, y * width * stride, width * stride);
        }

        return compact;
    }

    // The inverse: scatters a dense width x height buffer back into a zero-padded chunkSize tile.
    private static void ExpandRows(byte[] dense, byte[] tile, int chunkSize, int stride, int width, int height, int sourceWidth)
    {
        for (int y = 0; y < height; y++)
        {
            Array.Copy(dense, y * sourceWidth * stride, tile, y * chunkSize * stride, width * stride);
        }
    }
}
