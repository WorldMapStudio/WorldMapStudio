using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Declares one or more tables into a storage's EF Core model. Self-registers with
/// [Subsystem(nameof(ThatStorage))], exactly like <see cref="ICatalogEntityFactory"/> — but for a table
/// with no corresponding <see cref="IEntity"/> to hang the configuration off of (most raw reference
/// tables, including every one <c>DBImport.csx</c> dumps). A class that already self-registers as an
/// <see cref="IEntityFactory"/>, <see cref="IMapSource"/>, or <see cref="ILandscapeSettingsSource"/> for
/// a table implements this alongside that instead of needing a separate class — <see cref="Configure"/>
/// is the same shape as <see cref="IEntityFactory.Configure"/> on purpose.
///
/// Gathered generically by the owning storage's <c>CreateContext</c> and passed to its <c>DbContext</c>,
/// so no table's owner has to be named by hand in <c>OnModelCreating</c>.
/// </summary>
public interface ITableConfiguration : ISubsystem
{
    void Configure(ModelBuilder model);
}
