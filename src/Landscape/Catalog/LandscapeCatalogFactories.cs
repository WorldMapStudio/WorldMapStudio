namespace WorldMapStudio;

/// <summary>Maps <see cref="LandscapeChannel"/> to and from the Editor storage's <c>wms_landscape_channels</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeChannelFactory(EditorStorage storage)
    : EditorCatalogFactory<LandscapeChannel, LandscapeChannelRecord>(storage)
{
    protected override string TableName => "wms_landscape_channels";

    protected override LandscapeChannel ToEntity(LandscapeChannelRecord record) => new()
    {
        RecordId = record.Id,
        Map = new MapId(record.MapId),
        Name = record.Name,
        Resolution = record.Resolution,
        BitDepth = record.BitDepth,
        Components = record.Components,
    };

    protected override void WriteRecord(LandscapeChannel entity, LandscapeChannelRecord record)
    {
        record.MapId = entity.Map.Value;
        record.Name = entity.Name;
        record.Resolution = entity.Resolution;
        record.BitDepth = entity.BitDepth;
        record.Components = entity.Components;
    }
}

/// <summary>Maps <see cref="TerrainAttribute"/> to and from the Editor storage's <c>wms_landscape_attributes</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class TerrainAttributeFactory(EditorStorage storage)
    : EditorCatalogFactory<TerrainAttribute, TerrainAttributeRecord>(storage)
{
    protected override string TableName => "wms_landscape_attributes";

    protected override TerrainAttribute ToEntity(TerrainAttributeRecord record) => new()
    {
        RecordId = record.Id,
        Map = new MapId(record.MapId),
        Key = record.Key,
        Name = record.Name,
        Description = record.Description,
        CellsPerChunkEdge = record.CellsPerChunkEdge,
        Components = record.Components,
        ElementWidth = record.ElementWidth,
        ComponentNames = record.ComponentNames,
        Kind = (TerrainAttributeKind)record.Kind,
        CatalogName = record.CatalogName,
        DefaultValue = unchecked((uint)record.DefaultValue),
        Seeded = record.Seeded,
    };

    protected override void WriteRecord(TerrainAttribute entity, TerrainAttributeRecord record)
    {
        record.MapId = entity.Map.Value;
        record.Key = entity.Key;
        record.Name = entity.Name;
        record.Description = entity.Description;
        record.CellsPerChunkEdge = entity.CellsPerChunkEdge;
        record.Components = entity.Components;
        record.ElementWidth = entity.ElementWidth;
        record.ComponentNames = entity.ComponentNames;
        record.Kind = (int)entity.Kind;
        record.CatalogName = entity.CatalogName;
        record.DefaultValue = entity.DefaultValue;
        record.Seeded = entity.Seeded;
    }
}

/// <summary>Maps <see cref="TerrainAttributeValue"/> to and from the Editor storage's <c>wms_landscape_attribute_values</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class TerrainAttributeValueFactory(EditorStorage storage)
    : EditorCatalogFactory<TerrainAttributeValue, TerrainAttributeValueRecord>(storage)
{
    protected override string TableName => "wms_landscape_attribute_values";

    protected override TerrainAttributeValue ToEntity(TerrainAttributeValueRecord record) => new()
    {
        RecordId = record.Id,
        Map = new MapId(record.MapId),
        AttributeId = record.AttributeId,
        Value = record.Value,
        Name = record.Name,
    };

    protected override void WriteRecord(TerrainAttributeValue entity, TerrainAttributeValueRecord record)
    {
        record.MapId = entity.Map.Value;
        record.AttributeId = entity.AttributeId;
        record.Value = entity.Value;
        record.Name = entity.Name;
    }
}

/// <summary>Maps <see cref="LandscapeLayer"/> to and from the Editor storage's <c>wms_landscape_layers</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeLayerFactory(EditorStorage storage)
    : EditorCatalogFactory<LandscapeLayer, LandscapeLayerRecord>(storage)
{
    protected override string TableName => "wms_landscape_layers";

    protected override LandscapeLayer ToEntity(LandscapeLayerRecord record) => new()
    {
        RecordId = record.Id,
        Map = new MapId(record.MapId),
        Name = record.Name,
        IsBase = record.IsBase,
        Priority = record.Priority,
        DrawOrder = record.DrawOrder,
    };

    protected override void WriteRecord(LandscapeLayer entity, LandscapeLayerRecord record)
    {
        record.MapId = entity.Map.Value;
        record.Name = entity.Name;
        record.IsBase = entity.IsBase;
        record.Priority = entity.Priority;
        record.DrawOrder = entity.DrawOrder;
    }
}

/// <summary>Maps <see cref="LandscapeMaterial"/> to and from the Editor storage's <c>wms_landscape_materials</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeMaterialFactory(EditorStorage storage)
    : EditorCatalogFactory<LandscapeMaterial, LandscapeMaterialRecord>(storage)
{
    protected override string TableName => "wms_landscape_materials";

    protected override LandscapeMaterial ToEntity(LandscapeMaterialRecord record) => new()
    {
        RecordId = record.Id,
        Map = new MapId(record.MapId),
        Name = record.Name,
        TexturePath = record.TexturePath,
        AlphaFunction = record.AlphaFunction,
        AlphaParameters = record.AlphaParameters,
        HeightFunction = record.HeightFunction,
        HeightParameters = record.HeightParameters,
        HoleFunction = record.HoleFunction,
        HoleParameters = record.HoleParameters,
        VertexColorFunction = record.VertexColorFunction,
        VertexColorParameters = record.VertexColorParameters,
        VertexLightFunction = record.VertexLightFunction,
        VertexLightParameters = record.VertexLightParameters,
    };

    protected override void WriteRecord(LandscapeMaterial entity, LandscapeMaterialRecord record)
    {
        record.MapId = entity.Map.Value;
        record.Name = entity.Name;
        record.TexturePath = entity.TexturePath;
        record.AlphaFunction = entity.AlphaFunction;
        record.AlphaParameters = entity.AlphaParameters;
        record.HeightFunction = entity.HeightFunction;
        record.HeightParameters = entity.HeightParameters;
        record.HoleFunction = entity.HoleFunction;
        record.HoleParameters = entity.HoleParameters;
        record.VertexColorFunction = entity.VertexColorFunction;
        record.VertexColorParameters = entity.VertexColorParameters;
        record.VertexLightFunction = entity.VertexLightFunction;
        record.VertexLightParameters = entity.VertexLightParameters;
    }
}
