using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>EF Core context for the built-in Editor storage. Kept short-lived: create one per unit of work.</summary>
public sealed class EditorDbContext(DbContextOptions<EditorDbContext> options) : DbContext(options)
{
    public DbSet<EmptyRecord> Empties => Set<EmptyRecord>();

    public DbSet<MapRecord> Maps => Set<MapRecord>();

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
    }
}
