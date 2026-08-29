using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public sealed partial class ProceduralMeshSystem : ISubsystemHost
{
    // Bounds the built-model cache the same way MeshMaterialSystem bounds its built-material cache:
    // dragging a shared model's vertices bumps its Revision every frame, so without an LRU cap a long
    // drag would grow this without limit.
    private const int MaxCachedBuilds = 256;

    /// <summary>What an unbound or dangling <see cref="ProceduralMeshComponent"/> builds.</summary>
    public static readonly ModelAsset EmptyOutput = new ProceduralMeshOutputBuilder().Build(MeshModelFormat.FormatId);

    private readonly Dictionary<string, IProceduralMeshFunction> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];

    private readonly Dictionary<string, LinkedListNode<(string Key, ModelAsset Asset)>> _buildCache = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, ModelAsset Asset)> _buildCacheOrder = new();

    private (int CatalogVersion, int RevisionSum) _lastUpdateTick = (-1, -1);

    public ProceduralMeshSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
        DiscoverFrom(Subsystems.OfType<IProceduralMeshFunction>());
    }

    public EditorContext Context { get; }

    public IReadOnlyList<IProceduralMeshFunction> All { get; private set; } = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public int Version { get; private set; }

    /// <summary>The loaded model catalog. Membership comes from <see cref="EditorContext.Catalog"/>.</summary>
    public IEnumerable<ProceduralModel> Models => Context.Catalog.OfType<ProceduralModel>();

    public IProceduralMeshFunction? Find(string id) =>
        id.Length > 0 && _byId.TryGetValue(id, out IProceduralMeshFunction? function) ? function : null;

    public ProceduralModel? FindModel(int? id) =>
        id is int value ? Models.FirstOrDefault(model => model.RecordId == value) : null;

    /// <summary>How many loaded scene entities currently reference this model — what the picker and
    /// the models window show so an edit or delete does not surprise the user.</summary>
    public int UsageCount(int modelId) =>
        Context.Scene.Entities.Count(entity => entity.Component<ProceduralMeshComponent>()?.ModelId == modelId);

    /// <summary>Loads the model catalog whole, replacing what is loaded. Called after the migration
    /// gate, like <see cref="MeshMaterialSystem.LoadCatalog"/>.</summary>
    public void LoadCatalog()
    {
        Context.Database.LoadCatalog<ProceduralModel>();
        Version++;
    }

    /// <summary>
    /// Notices a model changed since a loaded placement last built its representation — from another
    /// entity, a window, an undo, or a script — and rebuilds that placement. Called once per frame,
    /// like <see cref="LandscapeSystem.Update"/>.
    ///
    /// Guarded by a cheap running tick (catalog membership plus the sum of every model's
    /// <see cref="ProceduralModel.Revision"/>) so the per-entity walk below only runs on a frame where
    /// something about a model actually moved, rather than every frame regardless.
    /// </summary>
    public void Update()
    {
        var tick = (Context.Catalog.Version, SumRevisions());
        if (tick == _lastUpdateTick)
        {
            return;
        }

        _lastUpdateTick = tick;

        foreach (SceneEntity entity in Context.Scene.Entities)
        {
            if (entity.Component<ProceduralMeshComponent>() is not { } component)
            {
                continue;
            }

            if (entity.IsRepresented && component.NeedsRefresh)
            {
                entity.RefreshRepresentation();
            }

            ReportDangling(entity, component);
        }
    }

    /// <summary>Surfaces a placement whose <see cref="ProceduralMeshComponent.ModelId"/> no longer
    /// resolves — e.g. the model was removed by an undo, a script, or a failed load — through
    /// <see cref="ProblemSystem"/> rather than throwing. One scope per entity, so a fixed reference
    /// clears its own report the next time this runs.</summary>
    private void ReportDangling(SceneEntity entity, ProceduralMeshComponent component)
    {
        string scope = $"procedural-mesh:{entity.Id.Value}";
        if (component.ModelId is not int id || component.Model != null)
        {
            Context.Problems.Replace(scope, []);
            return;
        }

        Context.Problems.Replace(scope,
        [
            new Problem
            {
                Key = "dangling-model",
                Severity = ProblemSeverity.Warning,
                Category = "Procedural Mesh",
                Kind = "DanglingModel",
                Message = $"{entity.DisplayName} references procedural model #{id}, which no longer exists.",
                Entity = entity.Id,
            }
        ]);
    }

    private int SumRevisions()
    {
        int sum = 0;
        foreach (ProceduralModel model in Models)
        {
            sum += model.Revision;
        }

        return sum;
    }

    /// <summary>Builds a model, from cache when nothing about it (or the presets/function it depends
    /// on) has changed since the last build.</summary>
    public ModelAsset Build(ProceduralModel model)
    {
        string key = BuildCacheKey(model);
        lock (_buildCacheOrder)
        {
            if (_buildCache.TryGetValue(key, out LinkedListNode<(string Key, ModelAsset Asset)>? node))
            {
                _buildCacheOrder.Remove(node);
                _buildCacheOrder.AddFirst(node);
                return node.Value.Asset;
            }
        }

        ModelAsset built = BuildUncached(model);

        lock (_buildCacheOrder)
        {
            if (!_buildCache.ContainsKey(key))
            {
                var node = new LinkedListNode<(string Key, ModelAsset Asset)>((key, built));
                _buildCacheOrder.AddFirst(node);
                _buildCache[key] = node;

                while (_buildCacheOrder.Count > MaxCachedBuilds)
                {
                    LinkedListNode<(string Key, ModelAsset Asset)>? last = _buildCacheOrder.Last;
                    if (last == null)
                    {
                        break;
                    }

                    _buildCacheOrder.RemoveLast();
                    _buildCache.Remove(last.Value.Key);
                }
            }
        }

        return built;
    }

    private string BuildCacheKey(ProceduralModel model) =>
        $"{model.RecordId}|{model.Revision}|{Find(model.FunctionId)?.Version ?? 0}|{Context.MeshMaterials.PresetContentVersion}";

    private ModelAsset BuildUncached(ProceduralModel model)
    {
        var builder = new ProceduralMeshOutputBuilder();
        if (Find(model.FunctionId) is not { } function)
        {
            return builder.Build(MeshModelFormat.FormatId);
        }

        string formatId = ResolveFormatId(model, function);
        IModelFormat? format = Context.ModelFormats.Find(formatId);
        IMeshMaterialType? materialType = format != null ? Context.MeshMaterials.Find(format.MaterialTypeId) : null;
        string materialTypeId = materialType?.Id ?? StandardMeshMaterial.TypeId;

        IReadOnlyDictionary<string, MeshMaterial> materials = ResolveMaterials(model, function, materialTypeId);

        function.Build(new ProceduralMeshBuildContext(
            model.Network,
            MeshParameterValues.Parse(model.Parameters),
            Context.Assets,
            format,
            materialType,
            slot => materials.TryGetValue(slot.Name, out MeshMaterial? material) ? material : new MeshMaterial(materialTypeId, new MeshParameterValues())),
            builder);
        return builder.Build(formatId);
    }

    /// <summary>Public overload for inspector/UI callers that only have the model, not its bound function.</summary>
    public string ResolveFormatId(ProceduralModel model) =>
        Find(model.FunctionId) is { } function ? ResolveFormatId(model, function) : MeshModelFormat.FormatId;

    /// <summary>
    /// The stored <see cref="ProceduralModel.FormatId"/> if the function still allows it, else the
    /// function's first supported format, else the plain authorable mesh format.
    /// </summary>
    private static string ResolveFormatId(ProceduralModel model, IProceduralMeshFunction function)
    {
        bool allowsAnyFormat = function.SupportedFormats.Count == 0;
        if (model.FormatId.Length > 0 && (allowsAnyFormat || function.SupportedFormats.Contains(model.FormatId)))
        {
            return model.FormatId;
        }

        return allowsAnyFormat ? MeshModelFormat.FormatId : function.SupportedFormats[0];
    }

    private Dictionary<string, MeshMaterial> ResolveMaterials(ProceduralModel model, IProceduralMeshFunction function, string materialTypeId)
    {
        var result = new Dictionary<string, MeshMaterial>(StringComparer.Ordinal);
        if (function.MaterialSlots.Count == 0)
        {
            return result;
        }

        ProceduralMeshMaterialBindings bindings = ProceduralMeshMaterialBindings.Parse(model.Materials);
        foreach (MeshMaterialSlot slot in function.MaterialSlots)
        {
            result[slot.Name] = ResolveSlot(bindings, slot, materialTypeId);
        }

        return result;
    }

    private MeshMaterial ResolveSlot(ProceduralMeshMaterialBindings bindings, MeshMaterialSlot slot, string materialTypeId)
    {
        if (bindings.GetPresetId(slot) is { } presetId)
        {
            MeshMaterialPreset? preset = Context.MeshMaterials.Presets.FirstOrDefault(p => p.RecordId == presetId);
            if (preset != null && preset.TypeId == materialTypeId)
            {
                return preset.ToMaterial();
            }
        }

        if (bindings.GetInlineValues(slot) is { } inline)
        {
            return new MeshMaterial(materialTypeId, inline);
        }

        return Context.MeshMaterials.Find(materialTypeId)?.Default ?? new MeshMaterial(materialTypeId, new MeshParameterValues());
    }

    public void DiscoverFrom(IEnumerable<IProceduralMeshFunction> functions)
    {
        _byId.Clear();
        _warnings.Clear();

        foreach (IProceduralMeshFunction function in functions)
        {
            Register(function);
        }

        All = _byId.Values.OrderBy(function => function.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        Version++;

        foreach (string warning in _warnings)
        {
            GD.PushWarning($"[ProceduralMesh] Function skipped - {warning}");
        }
    }

    private void Register(IProceduralMeshFunction function)
    {
        if (function.Id.Trim().Length == 0)
        {
            _warnings.Add($"{function.GetType().Name}: has an empty Id.");
            return;
        }

        if (_byId.TryGetValue(function.Id, out IProceduralMeshFunction? existing))
        {
            _warnings.Add($"{function.GetType().Name}: id '{function.Id}' is already used by {existing.GetType().Name}.");
            return;
        }

        if (DuplicateParameter(function) is { } duplicate)
        {
            _warnings.Add($"{function.GetType().Name}: declares parameter '{duplicate}' twice.");
            return;
        }

        _byId[function.Id] = function;
    }

    private static string? DuplicateParameter(IProceduralMeshFunction function)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MeshParameter parameter in function.Parameters)
        {
            if (!seen.Add(parameter.Name))
            {
                return parameter.Name;
            }
        }

        return null;
    }
}
