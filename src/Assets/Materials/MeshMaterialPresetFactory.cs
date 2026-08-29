using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>Maps <see cref="MeshMaterialPreset"/> to and from the Editor storage's <c>mesh_material_presets</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class MeshMaterialPresetFactory(EditorStorage storage)
    : EditorCatalogFactory<MeshMaterialPreset, MeshMaterialPresetRecord>(storage)
{
    protected override DbSet<MeshMaterialPresetRecord> Set(EditorDbContext context) => context.MeshMaterialPresets;

    protected override MeshMaterialPreset ToEntity(MeshMaterialPresetRecord record) => new()
    {
        RecordId = record.Id,
        Name = record.Name,
        TypeId = record.TypeId,
        Parameters = record.Parameters,
    };

    protected override void WriteRecord(MeshMaterialPreset entity, MeshMaterialPresetRecord record)
    {
        record.Name = entity.Name;
        record.TypeId = entity.TypeId;
        record.Parameters = entity.Parameters;
    }
}
