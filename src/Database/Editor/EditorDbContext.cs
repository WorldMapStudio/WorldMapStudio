using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>EF Core context for the built-in Editor storage. Kept short-lived: create one per unit of work.</summary>
public sealed class EditorDbContext(DbContextOptions<EditorDbContext> options) : DbContext(options)
{
    public DbSet<EmptyRecord> Empties => Set<EmptyRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<EmptyRecord>(entity =>
        {
            entity.ToTable("empties");
            entity.HasKey(record => record.Id);
        });
    }
}
