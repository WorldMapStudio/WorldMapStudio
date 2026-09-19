using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Generates one <see cref="IViewCategory"/> per registered <see cref="IModelFormat"/> — M2, WMO,
/// Liquid, Mesh, and anything a future format registers — so a format shows or hides as one toggle
/// regardless of whether an entity carries it as an imported <see cref="ModelRendererComponent"/>
/// placement or an authored <see cref="ProceduralComponent"/> output. No format-specific code lives
/// here or anywhere else in the filter; a format added later gets its toggle for free.
/// </summary>
[Subsystem(nameof(ViewCategorySystem))]
public sealed class ModelFormatViewCategories : IViewCategorySource
{
    private readonly EditorContext _context;

    public ModelFormatViewCategories(ViewCategorySystem system)
    {
        _context = system.Context;
    }

    public IEnumerable<IViewCategory> Categories() =>
        _context.ModelFormats.All.Select(format => new ModelFormatViewCategory(format, _context.Assets, _context.Procedural));
}

/// <summary>
/// One toggle over every entity whose rendered output is a given <see cref="IModelFormat"/> — an
/// imported <see cref="ModelRendererComponent"/> placement of that format, or a
/// <see cref="ProceduralComponent"/> whose bound model binds that format on any output slot. A
/// procedural entity whose outputs span two formats belongs to both categories, and is hidden if
/// either is.
/// </summary>
public sealed class ModelFormatViewCategory : IViewCategory
{
    private readonly IModelFormat _format;
    private readonly AssetSystem _assets;
    private readonly ProceduralSystem _procedural;

    public ModelFormatViewCategory(IModelFormat format, AssetSystem assets, ProceduralSystem procedural)
    {
        _format = format;
        _assets = assets;
        _procedural = procedural;
    }

    // Not discovered by the subsystem generator (see ModelFormatViewCategories.Categories), so this
    // only needs to satisfy ISubsystem — nothing orders these against a differently-sourced category.
    public float Priority => 20f;

    public string Id => $"view.format.{_format.Id}";

    public string DisplayName => _format.DisplayName;

    public string Group => "Models";

    public bool Includes(SceneEntity entity)
    {
        if (entity.Component<ModelRendererComponent>() is { } renderer &&
            _assets.FindModelFormat(renderer.ModelPath)?.Id == _format.Id)
        {
            return true;
        }

        return entity.Component<ProceduralComponent>() is { } procedural && IncludesProcedural(procedural);
    }

    private bool IncludesProcedural(ProceduralComponent component)
    {
        if (component.Model is not { } model || _procedural.Find(model.FunctionId) is not { } function)
        {
            return false;
        }

        foreach (ProceduralOutputSlot slot in function.Outputs)
        {
            if (_procedural.ResolveFormatId(model, slot) == _format.Id)
            {
                return true;
            }
        }

        return false;
    }
}
