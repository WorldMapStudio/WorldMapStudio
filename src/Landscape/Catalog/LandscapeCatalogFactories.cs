using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>Maps <see cref="LandscapeChannel"/> to and from the Editor storage's <c>landscape_channels</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeChannelFactory(EditorStorage storage)
    : EditorCatalogFactory<LandscapeChannel, LandscapeChannelRecord>(storage)
{
    protected override DbSet<LandscapeChannelRecord> Set(EditorDbContext context) => context.LandscapeChannels;

    protected override int? RecordId(LandscapeChannel entity) => entity.RecordId;

    protected override void SetRecordId(LandscapeChannel entity, int id) => entity.RecordId = id;

    protected override LandscapeChannel ToEntity(LandscapeChannelRecord record) => new()
    {
        RecordId = record.Id,
        Name = record.Name,
        Resolution = record.Resolution,
        BitDepth = record.BitDepth,
    };

    protected override void WriteRecord(LandscapeChannel entity, LandscapeChannelRecord record)
    {
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

    protected override int? RecordId(LandscapeLayer entity) => entity.RecordId;

    protected override void SetRecordId(LandscapeLayer entity, int id) => entity.RecordId = id;

    protected override LandscapeLayer ToEntity(LandscapeLayerRecord record) => new()
    {
        RecordId = record.Id,
        Name = record.Name,
        Kind = (LandscapeLayerKind)record.Kind,
        IsBase = record.IsBase,
        Priority = record.Priority,
        DrawOrder = record.DrawOrder,
    };

    protected override void WriteRecord(LandscapeLayer entity, LandscapeLayerRecord record)
    {
        record.Name = entity.Name;
        record.Kind = (int)entity.Kind;
        record.IsBase = entity.IsBase;
        record.Priority = entity.Priority;
        record.DrawOrder = entity.DrawOrder;
    }
}

/// <summary>Maps <see cref="LandscapeTextureMaterial"/> to and from the Editor storage's <c>landscape_materials</c> table.</summary>
[Subsystem(nameof(EditorStorage))]
public sealed class LandscapeMaterialFactory(EditorStorage storage)
    : EditorCatalogFactory<LandscapeTextureMaterial, LandscapeMaterialRecord>(storage)
{
    protected override DbSet<LandscapeMaterialRecord> Set(EditorDbContext context) => context.LandscapeMaterials;

    protected override int? RecordId(LandscapeTextureMaterial entity) => entity.RecordId;

    protected override void SetRecordId(LandscapeTextureMaterial entity, int id) => entity.RecordId = id;

    protected override LandscapeTextureMaterial ToEntity(LandscapeMaterialRecord record) => new()
    {
        RecordId = record.Id,
        Name = record.Name,
        TexturePath = record.TexturePath,
        AlphaFunction = record.AlphaFunction,
        AlphaParameters = record.AlphaParameters,
        HeightFunction = record.HeightFunction,
        HeightParameters = record.HeightParameters,
    };

    protected override void WriteRecord(LandscapeTextureMaterial entity, LandscapeMaterialRecord record)
    {
        record.Name = entity.Name;
        record.TexturePath = entity.TexturePath;
        record.AlphaFunction = entity.AlphaFunction;
        record.AlphaParameters = entity.AlphaParameters;
        record.HeightFunction = entity.HeightFunction;
        record.HeightParameters = entity.HeightParameters;
    }
}
