using Godot;

namespace WorldMapStudio;

public sealed class PrefabRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Prefab";

    public double AnchorX { get; set; }

    public double AnchorY { get; set; }

    public double AnchorZ { get; set; }
}

/// <summary>Maps <see cref="Prefab"/> to and from the Editor storage's <c>wms_prefabs</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class PrefabFactory(EditorStorage storage)
    : EditorCatalogFactory<Prefab, PrefabRecord>(storage)
{
    protected override string TableName => "wms_prefabs";

    protected override Prefab ToEntity(PrefabRecord record) => new()
    {
        RecordId = record.Id,
        Name = record.Name,
        Anchor = new Vector3((float)record.AnchorX, (float)record.AnchorY, (float)record.AnchorZ),
    };

    protected override void WriteRecord(Prefab entity, PrefabRecord record)
    {
        record.Name = entity.Name;
        record.AnchorX = entity.Anchor.X;
        record.AnchorY = entity.Anchor.Y;
        record.AnchorZ = entity.Anchor.Z;
    }
}
