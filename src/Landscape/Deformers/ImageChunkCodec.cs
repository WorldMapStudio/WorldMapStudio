using System.IO;
using System.IO.Compression;

namespace WorldMapStudio;

/// <summary>
/// Encodes and decodes one chunk's raw pixel buffer for the <c>image_chunks</c> table. A paint mask is
/// overwhelmingly runs of 0 and 255, so deflate routinely shrinks a chunk 20-100x — but the format is
/// tagged per row rather than assumed, so a chunk that doesn't compress well is stored raw instead of
/// paying deflate's overhead for nothing, and the codec itself can change later without a migration.
/// </summary>
public static class ImageChunkCodec
{
    public const byte FormatRaw = 0;
    public const byte FormatDeflate = 1;

    public static (byte Format, byte[] Bytes) Encode(byte[] pixels)
    {
        using var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(pixels, 0, pixels.Length);
        }

        byte[] deflated = compressed.ToArray();
        return deflated.Length < pixels.Length ? (FormatDeflate, deflated) : (FormatRaw, pixels);
    }

    /// <summary><paramref name="chunkSize"/> is the tile's side length and <paramref name="stride"/> its
    /// bytes per pixel (<see cref="PaintImage.Stride"/>) — the decompressed length is always
    /// <c>chunkSize * chunkSize * stride</c>, since every chunk is stored at its image's fixed tile size
    /// regardless of how much of it falls inside the canvas.</summary>
    public static byte[] Decode(byte format, byte[] bytes, int chunkSize, int stride)
    {
        if (format != FormatDeflate)
        {
            return bytes;
        }

        var pixels = new byte[chunkSize * chunkSize * stride];
        using var source = new MemoryStream(bytes);
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);

        int offset = 0;
        int read;
        while (offset < pixels.Length && (read = deflate.Read(pixels, offset, pixels.Length - offset)) > 0)
        {
            offset += read;
        }

        return pixels;
    }
}
