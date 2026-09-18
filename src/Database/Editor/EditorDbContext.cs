using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// EF Core context for the built-in Editor storage. Kept short-lived: create one per unit of work.
///
/// No table is declared here, including the editor's own built-in ones: every table is declared by
/// whichever self-registered subsystem owns it, via one of three seams depending on what else that
/// owner already is — an <see cref="IEntityFactory"/> or <see cref="ISceneComponentPersistence"/>
/// declares its own table(s) through the <c>Configure</c> it already has for staging entities/
/// components; anything else (a map source, a landscape settings source, or a table with no entity at
/// all) implements <see cref="ITableConfiguration"/> instead. See
/// <see cref="EditorStorage.CreateContext"/> for where those lists come from, and
/// <see cref="ISceneComponentPersistence"/> for the model-caching assumption this relies on.
///
/// Declares no <c>DbSet</c> either — <c>context.Set&lt;TRecord&gt;()</c> works identically to a named
/// property and needs nothing declared here, so a named one exists only where it earns its keep
/// (readability at a call site used more than once): as a <c>partial</c> addition in that table's own
/// owning file, right next to the <see cref="ITableConfiguration"/>/<see cref="IEntityFactory"/> that
/// declares the table itself. See e.g. <see cref="SceneEntityFactory"/> or <c>ChunkChangeRecords.cs</c>.
/// </summary>
public sealed partial class EditorDbContext : DbContext
{
    private readonly IReadOnlyList<ISceneComponentPersistence> _componentPersistence;
    private readonly IReadOnlyList<IEntityFactory> _entityFactories;
    private readonly IReadOnlyList<ITableConfiguration> _tableConfigurations;

    public EditorDbContext(
        DbContextOptions<EditorDbContext> options,
        IReadOnlyList<ISceneComponentPersistence> componentPersistence,
        IReadOnlyList<IEntityFactory> entityFactories,
        IReadOnlyList<ITableConfiguration> tableConfigurations)
        : base(options)
    {
        _componentPersistence = componentPersistence;
        _entityFactories = entityFactories;
        _tableConfigurations = tableConfigurations;
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Every table, built-in or plugin — declared entirely by its self-registered owner, in
        // whichever of the three seams fits what that owner already is. See the class doc.
        foreach (ITableConfiguration configuration in _tableConfigurations)
        {
            configuration.Configure(model);
        }

        foreach (ISceneComponentPersistence persistence in _componentPersistence)
        {
            persistence.Configure(model);
        }

        foreach (IEntityFactory factory in _entityFactories)
        {
            factory.Configure(model);
        }
    }
}
