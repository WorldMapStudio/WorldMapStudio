using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>EF Core context for the built-in Editor storage. Kept short-lived: create one per unit of work.</summary>
public sealed class EditorDbContext(DbContextOptions<EditorDbContext> options) : DbContext(options)
{
    public DbSet<EmptyRecord> Empties => Set<EmptyRecord>();

    public DbSet<MapRecord> Maps => Set<MapRecord>();

    public DbSet<LandscapeChannelRecord> LandscapeChannels => Set<LandscapeChannelRecord>();

    public DbSet<LandscapeLayerRecord> LandscapeLayers => Set<LandscapeLayerRecord>();

    public DbSet<LandscapeMaterialRecord> LandscapeMaterials => Set<LandscapeMaterialRecord>();

    public DbSet<LandscapeSettingsRecord> LandscapeSettings => Set<LandscapeSettingsRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<EmptyRecord>(entity =>
        {
            entity.ToTable("empties");
            entity.HasKey(record => record.Id);
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
        });

        model.Entity<LandscapeLayerRecord>(entity =>
        {
            entity.ToTable("landscape_layers");
            entity.HasKey(record => record.Id);
        });

        model.Entity<LandscapeMaterialRecord>(entity =>
        {
            entity.ToTable("landscape_materials");
            entity.HasKey(record => record.Id);
        });

        model.Entity<LandscapeSettingsRecord>(entity =>
        {
            entity.ToTable("landscape_settings");

            // One landscape per map, so the map id is the key rather than a generated one.
            entity.HasKey(record => record.MapId);
            entity.Property(record => record.MapId).ValueGeneratedNever();
        });
    }
}
