using System;
using System.Linq;

namespace WorldMapStudio;

/// <summary>A read-only snapshot of one registered <see cref="IViewCategory"/>, safe to hand to JS.</summary>
public sealed class ViewCategoryDescriptor
{
    public ViewCategoryDescriptor(IViewCategory category, bool hidden)
    {
        Id = category.Id;
        Name = category.DisplayName;
        Group = category.Group;
        Hidden = hidden;
    }

    [ScriptProperty]
    public string Id { get; }

    [ScriptProperty]
    public string Name { get; }

    [ScriptProperty]
    public string Group { get; }

    [ScriptProperty]
    public bool Hidden { get; }
}

/// <summary>
/// The View menu's per-type visibility filter, plus the remaining overlay toggles and streaming
/// dials it sits beside — none of which had a script surface before this — exposed to JS as
/// <c>wms.view</c>. Lighting has no property of its own here: it is hidden and shown like every
/// other category, via <see cref="Hide"/>/<see cref="Show"/> with <see cref="LightingViewCategory.CategoryId"/>.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class ViewScriptApi : IScriptModule
{
    private readonly EditorContext _context;
    private readonly ViewCategorySystem _categories;
    private readonly ViewSettings _view;

    public string Name => "view";

    public ViewScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
        _categories = system.Context.ViewCategories;
        _view = system.Context.View;
    }

    /// <summary>Every registered view category and whether it is currently hidden.</summary>
    [ScriptFunction]
    public ViewCategoryDescriptor[] List() =>
        _categories.All.Select(category => new ViewCategoryDescriptor(category, _categories.IsHidden(category.Id))).ToArray();

    [ScriptFunction]
    public void Hide(string id) => _categories.SetHidden(Resolve(id).Id, true);

    [ScriptFunction]
    public void Show(string id) => _categories.SetHidden(Resolve(id).Id, false);

    [ScriptFunction]
    public void Toggle(string id)
    {
        IViewCategory category = Resolve(id);
        _categories.SetHidden(category.Id, !_categories.IsHidden(category.Id));
    }

    [ScriptFunction]
    public void ShowAll() => _categories.ShowAll();

    private IViewCategory Resolve(string id) =>
        _categories.Find(id) ?? throw new InvalidOperationException($"No view category named '{id}'.");

    [ScriptProperty]
    public bool Grid => _view.ShowGrid;

    [ScriptFunction]
    public void SetGrid(bool shown) => _view.ShowGrid = shown;

    [ScriptProperty]
    public bool ChunkEdges => _view.ShowChunkEdges;

    [ScriptFunction]
    public void SetChunkEdges(bool shown) => _view.ShowChunkEdges = shown;

    [ScriptProperty]
    public bool TerrainVertexColor => _view.ShowTerrainVertexColor;

    [ScriptFunction]
    public void SetTerrainVertexColor(bool shown) => _view.ShowTerrainVertexColor = shown;

    [ScriptProperty]
    public bool TerrainVertexLight => _view.ShowTerrainVertexLight;

    [ScriptFunction]
    public void SetTerrainVertexLight(bool shown) => _view.ShowTerrainVertexLight = shown;

    [ScriptProperty]
    public bool EnvironmentVolumes => _view.ShowEnvironmentVolumes;

    [ScriptFunction]
    public void SetEnvironmentVolumes(bool shown) => _view.ShowEnvironmentVolumes = shown;

    [ScriptProperty]
    public int ViewDistanceChunks => _view.ViewDistanceChunks;

    [ScriptFunction]
    public void SetViewDistanceChunks(int chunks)
    {
        _view.ViewDistanceChunks = Math.Max(1, chunks);

        // Streaming reads this only when a scan starts, same as ViewMenu's own drag control.
        _context.Streaming.Invalidate();
    }

    [ScriptProperty]
    public int TerrainBatchChunks => _view.TerrainBatchChunks;

    [ScriptFunction]
    public void SetTerrainBatchChunks(int chunks)
    {
        _view.TerrainBatchChunks = Math.Clamp(chunks, 1, LandscapeTerrainBatch.MaxTerrainBatchChunks);
        _context.Streaming.ReloadTerrain();
    }
}
