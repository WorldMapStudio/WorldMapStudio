using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>Maps <see cref="LandscapeChannel"/> to and from the Editor storage's <c>landscape_channels</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeChannelFactory(EditorStorage storage)
    : EditorCatalogFactory<LandscapeChannel, LandscapeChannelRecord>(storage)
{
    protected override DbSet<LandscapeChannelRecord> Set(EditorDbContext context) => context.LandscapeChannels;



    protected override LandscapeChannel ToEntity(LandscapeChannelRecord record) => new()
    {
        RecordId = record.Id,
        Map = new MapId(record.MapId),
        Name = record.Name,
        Resolution = record.Resolution,
        BitDepth = record.BitDepth,
    };

    protected override void WriteRecord(LandscapeChannel entity, LandscapeChannelRecord record)
    {
        record.MapId = entity.Map.Value;
        record.Name = entity.Name;
        record.Resolution = entity.Resolution;
        record.BitDepth = entity.BitDepth;
    }
}

/// <summary>Maps <see cref="LandscapeLayer"/> to and from the Editor storage's <c>landscape_layers</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeLayerFactory(EditorStorage storage)
    : EditorCatalogFactory<LandscapeLayer, LandscapeLayerRecord>(storage)
{
    protected override DbSet<LandscapeLayerRecord> Set(EditorDbContext context) => context.LandscapeLayers;



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

/// <summary>Maps <see cref="LandscapeMaterial"/> to and from the Editor storage's <c>landscape_materials</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeMaterialFactory(EditorStorage storage)
    : EditorCatalogFactory<LandscapeMaterial, LandscapeMaterialRecord>(storage)
{
    protected override DbSet<LandscapeMaterialRecord> Set(EditorDbContext context) => context.LandscapeMaterials;



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
    }
}
