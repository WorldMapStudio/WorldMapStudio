namespace WorldMapStudio;

/// <summary>Maps <see cref="ProceduralModel"/> to and from the Editor storage's <c>wms_procedural_models</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class ProceduralModelFactory(EditorStorage storage)
    : EditorCatalogFactory<ProceduralModel, ProceduralModelRecord>(storage)
{
    protected override string TableName => "wms_procedural_models";

    protected override ProceduralModel ToEntity(ProceduralModelRecord record)
    {
        var entity = new ProceduralModel
        {
            RecordId = record.Id,
            Name = record.Name,
            FunctionId = record.FunctionId,
            Parameters = record.Parameters,
            Formats = record.Formats,
            Materials = record.Materials,
        };
        entity.LoadNetwork(VertexNetwork.Parse(record.NetworkJson));
        return entity;
    }

    protected override void WriteRecord(ProceduralModel entity, ProceduralModelRecord record)
    {
        record.Name = entity.Name;
        record.FunctionId = entity.FunctionId;
        record.Parameters = entity.Parameters;
        record.Formats = entity.Formats;
        record.Materials = entity.Materials;
        record.NetworkJson = entity.Network.Serialize();
    }
}
