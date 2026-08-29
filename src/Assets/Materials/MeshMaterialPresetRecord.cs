namespace WorldMapStudio;

/// <summary>EF Core row backing a <see cref="MeshMaterialPreset"/> in the Editor storage.</summary>
public sealed class MeshMaterialPresetRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Material";

    public string TypeId { get; set; } = "";

    public string Parameters { get; set; } = "";
}
