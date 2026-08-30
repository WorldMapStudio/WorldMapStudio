namespace WorldMapStudio;

/// <summary>One fixed-size tile of a chunked <see cref="PaintImage"/>'s pixel data. Only chunks that
/// have actually been painted exist — see <see cref="ImageChunkTable"/> for what an absent coord
/// means.</summary>
public sealed class ImageChunk
{
    public ImageChunk(byte[] pixels)
    {
        Pixels = pixels;
    }

    public byte[] Pixels { get; }
}
