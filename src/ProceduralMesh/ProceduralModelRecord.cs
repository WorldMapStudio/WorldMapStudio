namespace WorldMapStudio;

/// <summary>EF Core row backing a <see cref="ProceduralModel"/> in the Editor storage.</summary>
public sealed class ProceduralModelRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Model";

    public string FunctionId { get; set; } = "";

    public string Parameters { get; set; } = "";

    public string FormatId { get; set; } = "";

    public string Materials { get; set; } = "";

    public string NetworkJson { get; set; } = "";
}
