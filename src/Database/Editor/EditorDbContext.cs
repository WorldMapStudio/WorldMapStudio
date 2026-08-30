using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// EF Core context for the built-in Editor storage. Kept short-lived: create one per unit of work.
///
/// Scene-component tables are not declared here: each registered <see cref="ISceneComponentPersistence"/>
/// contributes its own via <see cref="Configure"/>, so a plugin's component gets a table the same way a
/// built-in one does. See <see cref="EditorStorage.CreateContext"/> for where the persister list comes
/// from, and <see cref="ISceneComponentPersistence"/> for the model-caching assumption this relies on.
/// </summary>
public sealed class EditorDbContext : DbContext
{
    private readonly IReadOnlyList<ISceneComponentPersistence> _componentPersistence;
    private readonly IReadOnlyList<IEntityFactory> _entityFactories;

    public EditorDbContext(
        DbContextOptions<EditorDbContext> options,
        IReadOnlyList<ISceneComponentPersistence> componentPersistence,
        IReadOnlyList<IEntityFactory> entityFactories)
        : base(options)
    {
        _componentPersistence = componentPersistence;
        _entityFactories = entityFactories;
    }

    public DbSet<SceneEntityRecord> SceneEntities => Set<SceneEntityRecord>();

    public DbSet<MapRecord> Maps => Set<MapRecord>();

    public DbSet<LandscapeChannelRecord> LandscapeChannels => Set<LandscapeChannelRecord>();

    public DbSet<LandscapeLayerRecord> LandscapeLayers => Set<LandscapeLayerRecord>();

    public DbSet<LandscapeMaterialRecord> LandscapeMaterials => Set<LandscapeMaterialRecord>();

    public DbSet<MeshMaterialPresetRecord> MeshMaterialPresets => Set<MeshMaterialPresetRecord>();

    public DbSet<ProceduralModelRecord> ProceduralModels => Set<ProceduralModelRecord>();

    public DbSet<PaintImageRecord> Images => Set<PaintImageRecord>();

    public DbSet<ImageChunkRecord> ImageChunks => Set<ImageChunkRecord>();

    public DbSet<ImageDisplayLayerRecord> ImageDisplayLayers => Set<ImageDisplayLayerRecord>();

    public DbSet<PrefabRecord> Prefabs => Set<PrefabRecord>();

    public DbSet<LandscapeSettingsRecord> LandscapeSettings => Set<LandscapeSettingsRecord>();

    public DbSet<ChunkChangeRecord> ChunkChanges => Set<ChunkChangeRecord>();

    public DbSet<ExportedChunkRecord> ExportedChunks => Set<ExportedChunkRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SceneEntityRecord>(entity =>
        {
            entity.ToTable("scene_entities");
            entity.HasKey(record => record.Id);
            entity.HasOne(record => record.Parent)
                .WithMany()
                .HasForeignKey(record => record.ParentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<MapRecord>(entity =>
        {
            entity.ToTable("maps");
            entity.HasKey(record => record.Id);

            // The map id is the user's own (it matches the game's map ids), not a generated key.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<LandscapeChannelRecord>(entity =>
        {
            entity.ToTable("landscape_channels");
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<LandscapeLayerRecord>(entity =>
        {
            entity.ToTable("landscape_layers");
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<LandscapeMaterialRecord>(entity =>
        {
            entity.ToTable("landscape_materials");
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<MeshMaterialPresetRecord>(entity =>
        {
            entity.ToTable("mesh_material_presets");
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<ProceduralModelRecord>(entity =>
        {
            entity.ToTable("procedural_models");
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<PaintImageRecord>(entity =>
        {
            entity.ToTable("images");
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<ImageChunkRecord>(entity =>
        {
            entity.ToTable("image_chunks");
            entity.HasKey(record => new { record.ImageId, record.ChunkX, record.ChunkY });
        });

        model.Entity<ImageDisplayLayerRecord>(entity =>
        {
            entity.ToTable("image_display_layers");
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<PrefabRecord>(entity =>
        {
            entity.ToTable("prefabs");
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<LandscapeSettingsRecord>(entity =>
        {
            entity.ToTable("landscape_settings");

            // One landscape per map, so the map id is the key rather than a generated one.
            entity.HasKey(record => record.MapId);
            entity.Property(record => record.MapId).ValueGeneratedNever();
        });

        model.Entity<ChunkChangeRecord>(entity =>
        {
            entity.ToTable("chunk_changes");
            entity.HasKey(record => new { record.MapId, record.ChunkX, record.ChunkY });
            entity.Property(record => record.ContentHash).HasMaxLength(64);
        });

        model.Entity<ExportedChunkRecord>(entity =>
        {
            entity.ToTable("exported_chunks");
            entity.HasKey(record => new { record.ExporterId, record.MapId, record.ChunkX, record.ChunkY });
            entity.Property(record => record.ExporterId).HasMaxLength(128);
            entity.Property(record => record.ContentHash).HasMaxLength(64);
        });

        foreach (ISceneComponentPersistence persistence in _componentPersistence)
        {
            persistence.Configure(model);
        }

        // Catalog (and, redundantly but harmlessly, scene) factories that target this storage and
        // need their own table — e.g. a plugin's catalog living in tables the core does not declare.
        foreach (IEntityFactory factory in _entityFactories)
        {
            factory.Configure(model);
        }
    }
}
