namespace WorldMapStudio;

/// <summary>A chunk's position in a <see cref="PaintImage"/>'s chunk grid — not a pixel coordinate.</summary>
public readonly record struct ImageChunkCoord(int X, int Y)
{
    public override string ToString() => $"({X}, {Y})";
}
