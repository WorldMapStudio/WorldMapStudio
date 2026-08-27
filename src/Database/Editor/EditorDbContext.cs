using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>EF Core context for the built-in Editor storage. Kept short-lived: create one per unit of work.</summary>
public sealed class EditorDbContext(DbContextOptions<EditorDbContext> options) : DbContext(options)
{
    public DbSet<SceneEntityRecord> SceneEntities => Set<SceneEntityRecord>();

    public DbSet<SceneMarkerComponentRecord> SceneMarkerComponents => Set<SceneMarkerComponentRecord>();

    public DbSet<SceneStampComponentRecord> SceneStampComponents => Set<SceneStampComponentRecord>();

    public DbSet<SceneDrawingTargetComponentRecord> SceneDrawingTargetComponents => Set<SceneDrawingTargetComponentRecord>();

    public DbSet<SceneLandscapeMaterialBindComponentRecord> SceneLandscapeMaterialBindComponents =>
        Set<SceneLandscapeMaterialBindComponentRecord>();

    public DbSet<SceneLandscapeMaterialBindEntryRecord> SceneLandscapeMaterialBindEntries =>
        Set<SceneLandscapeMaterialBindEntryRecord>();

    public DbSet<MapRecord> Maps => Set<MapRecord>();

    public DbSet<LandscapeChannelRecord> LandscapeChannels => Set<LandscapeChannelRecord>();

    public DbSet<LandscapeLayerRecord> LandscapeLayers => Set<LandscapeLayerRecord>();

    public DbSet<LandscapeMaterialRecord> LandscapeMaterials => Set<LandscapeMaterialRecord>();

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

        model.Entity<SceneMarkerComponentRecord>(entity =>
        {
            entity.ToTable("scene_marker_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneMarkerComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SceneStampComponentRecord>(entity =>
        {
            entity.ToTable("scene_stamp_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneStampComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SceneDrawingTargetComponentRecord>(entity =>
        {
            entity.ToTable("scene_drawing_target_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneDrawingTargetComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SceneLandscapeMaterialBindComponentRecord>(entity =>
        {
            entity.ToTable("scene_landscape_material_bind_components");
            entity.HasKey(record => record.EntityId);
            entity.HasOne(record => record.Entity)
                .WithOne()
                .HasForeignKey<SceneLandscapeMaterialBindComponentRecord>(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SceneLandscapeMaterialBindEntryRecord>(entity =>
        {
            entity.ToTable("scene_landscape_material_bind_entries");
            entity.HasKey(record => new { record.EntityId, record.SortOrder });
            entity.HasOne(record => record.Component)
                .WithMany()
                .HasForeignKey(record => record.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
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
    }
}
