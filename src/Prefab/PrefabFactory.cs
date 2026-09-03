using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

public sealed class PrefabRecord : IKeyedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = "Prefab";
}

/// <summary>Maps <see cref="Prefab"/> to and from the Editor storage's <c>prefabs</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class PrefabFactory(EditorStorage storage)
    : EditorCatalogFactory<Prefab, PrefabRecord>(storage)
{
    protected override DbSet<PrefabRecord> Set(EditorDbContext context) => context.Prefabs;

    protected override string TableName => "prefabs";

    protected override Prefab ToEntity(PrefabRecord record) => new()
    {
        RecordId = record.Id,
        Name = record.Name,
    };

    protected override void WriteRecord(Prefab entity, PrefabRecord record)
    {
        record.Name = entity.Name;
    }
}
