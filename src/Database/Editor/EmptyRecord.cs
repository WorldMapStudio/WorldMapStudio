namespace WorldMapStudio;

/// <summary>EF Core row backing an <see cref="EmptyEntity"/> in the Editor storage.</summary>
public sealed class EmptyRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Empty";

    public int Shape { get; set; }

    public double PosX { get; set; }

    public double PosY { get; set; }

    public double PosZ { get; set; }

    public double RotX { get; set; }

    public double RotY { get; set; }

    public double RotZ { get; set; }

    public double RotW { get; set; } = 1.0;
}
