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

    /// <summary>Whether this chunk's in-memory pixels differ from what storage last saw. Set by
    /// <see cref="PaintImage"/> on an edit, cleared by a commit's write-back — never by a load, which
    /// starts a chunk clean by construction. What keeps <see cref="ImageResidencySystem"/> from ever
    /// evicting a chunk with unsaved work.</summary>
    public bool Dirty { get; internal set; }

    /// <summary>Bumped every time this chunk's pixels change. What lets a viewport representation
    /// re-upload only the chunks a brush actually touched instead of every chunk the image has — see
    /// <see cref="ImageComponent.SyncChunkNodes"/>. Compared for difference, never ordering, so a
    /// reloaded chunk starting back at zero is harmless.</summary>
    public int Revision { get; internal set; }
}
