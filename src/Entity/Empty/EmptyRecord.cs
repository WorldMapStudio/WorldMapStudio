namespace WorldMapStudio;

/// <summary>EF Core row backing an <see cref="EmptyEntity"/> in the Editor storage.</summary>
public sealed class EmptyRecord
{
    public int Id { get; set; }

    public int MapId { get; set; }

    public string Name { get; set; } = "Empty";

    public int Shape { get; set; }

    public double PosX { get; set; }

    public double PosY { get; set; }

    public double PosZ { get; set; }

    public double RotX { get; set; }

    public double RotY { get; set; }

    public double RotZ { get; set; }

    public double RotW { get; set; } = 1.0;

    // World-space bounds, written from SceneEntity.WorldBounds on save so the streaming scan can ask
    // for overlap in SQL. Derived from position + rotation + local bounds; never edited directly.
    public double MinX { get; set; }

    public double MinY { get; set; }

    public double MinZ { get; set; }

    public double MaxX { get; set; }

    public double MaxY { get; set; }

    public double MaxZ { get; set; }
}
