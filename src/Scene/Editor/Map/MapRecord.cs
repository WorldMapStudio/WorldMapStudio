namespace WorldMapStudio;

/// <summary>EF Core row backing a <see cref="Map"/> in the Editor storage's <c>maps</c> table.</summary>
public sealed class MapRecord
{
    /// <summary>The map id itself, chosen by the user rather than generated (see the model config).</summary>
    public int Id { get; set; }

    public string Name { get; set; } = "Map";
}
