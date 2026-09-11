using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public sealed partial class ProceduralSystem : ISubsystemHost, IWorldParticipant
{
    // Bounds the built-model cache the same way MeshMaterialSystem bounds its built-material cache:
    // dragging a shared model's vertices bumps its Revision every frame, so without an LRU cap a long
    // drag would grow this without limit.
    private const int MaxCachedBuilds = 256;

    private readonly Dictionary<string, IProceduralFunction> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];

    private readonly Dictionary<string, LinkedListNode<(string Key, ProceduralBuildResult Result)>> _buildCache = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, ProceduralBuildResult Result)> _buildCacheOrder = new();

    private (int CatalogVersion, int SceneVersion, int RevisionTick) _lastUpdateTick = (-1, -1, -1);

    // Published as one immutable snapshot rather than a table kept up to date in place, mirroring
    // ImageSystem.CatalogIndex: ProceduralComponent.Model is read several times a frame per placement,
    // and a scan resolving models off the main thread reads it too, so a fresh scan of the whole
    // registry per read is not acceptable.
    private sealed record CatalogIndex(int Version, IReadOnlyList<ProceduralModel> Models, Dictionary<int, ProceduralModel> ById);

    private CatalogIndex _index = new(-1, [], []);

    public ProceduralSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
        DiscoverFrom(Subsystems.OfType<IProceduralFunction>());
    }

    public EditorContext Context { get; }

    public IReadOnlyList<IProceduralFunction> All { get; private set; } = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public int Version { get; private set; }

    /// <summary>The loaded model catalog. Membership comes from <see cref="EditorContext.Catalog"/>,
    /// materialized once per <see cref="CatalogEntityRegistry.Version"/> rather than rescanned on every
    /// read — see <see cref="Index"/>.</summary>
    public IReadOnlyList<ProceduralModel> Models => Index().Models;

    /// <summary>Record ids of every currently loaded model, off the same published snapshot
    /// <see cref="Models"/> reads. What a publishing scan (<see cref="StreamingSystem"/>,
    /// <see cref="PrefabSystem"/>) filters against in <see cref="ProceduralComponentPersistence.LoadAsync"/>
    /// so it never re-reads an already-resident model's network.</summary>
    public IReadOnlyCollection<int> LoadedModelIds => Index().ById.Keys;

    public IProceduralFunction? Find(string id) =>
        id.Length > 0 && _byId.TryGetValue(id, out IProceduralFunction? function) ? function : null;

    public ProceduralModel? FindModel(int? id)
    {
        if (id is not int value)
        {
            return null;
        }

        CatalogIndex index = Index();
        if (index.ById.TryGetValue(value, out ProceduralModel? cached) && cached.RecordId == value)
        {
            return cached;
        }

        // Verified rather than trusted: a record id is assigned when an entity is first saved, which
        // the registry's membership version never sees, so an index entry can name an entity whose id
        // has since moved. A miss falls back to a scan and does not write what it finds — the snapshot
        // read here is shared with every other thread reading it.
        foreach (ProceduralModel candidate in index.Models)
        {
            if (candidate.RecordId == value)
            {
                return candidate;
            }
        }

        return null;
    }

    private CatalogIndex Index()
    {
        CatalogIndex current = _index;
        int version = Context.Catalog.Version;
        if (current.Version == version)
        {
            return current;
        }

        var models = new List<ProceduralModel>();
        var byId = new Dictionary<int, ProceduralModel>();

        foreach (ProceduralModel model in Context.Catalog.OfType<ProceduralModel>())
        {
            models.Add(model);
            if (model.RecordId is int id)
            {
                byId[id] = model;
            }
        }

        var built = new CatalogIndex(version, models, byId);
        _index = built;
        return built;
    }

    /// <summary>How many loaded scene entities currently reference this model — what the picker and
    /// the models window show so an edit or delete does not surprise the user.</summary>
    public int UsageCount(int modelId) =>
        Context.Scene.Entities.Count(entity => entity.Component<ProceduralComponent>()?.ModelId == modelId);

    /// <summary>Every loaded placement of a model — what a shared-model edit has to invalidate, since
    /// the authored data lives on the model and not on any one placement.</summary>
    public IEnumerable<SceneEntity> PlacementsOf(ProceduralModel model) =>
        model.RecordId is int id
            ? Context.Scene.Entities.Where(entity => entity.Component<ProceduralComponent>()?.ModelId == id)
            : [];

    // Catalog rows themselves come from DatabaseSystem's own IWorldParticipant, which bulk-loads every
    // registered catalog type before anything that resolves against one.
    float IWorldParticipant.LoadPriority => 3f;

    string? IWorldParticipant.LoadStep => "Loading procedural models";

    void IWorldParticipant.LoadWorld() => Version++;

    void IWorldParticipant.UnloadWorld()
    {
        lock (_buildCacheOrder)
        {
            _buildCache.Clear();
            _buildCacheOrder.Clear();
        }

        _lastUpdateTick = (-1, -1, -1);
        _index = new CatalogIndex(-1, [], []);
        Version++;
    }

    /// <summary>
    /// Notices a model changed since a loaded placement last built its representation — from another
    /// entity, a window, an undo, or a script — and rebuilds that placement. Called once per frame,
    /// like <see cref="LandscapeSystem.Update"/>.
    ///
    /// Guarded by a cheap running tick (catalog membership, scene membership, and
    /// <see cref="ProceduralModel.RevisionTick"/>) so the per-entity walk below only runs on a
    /// frame where something actually moved, rather than every frame regardless. Scene membership is
    /// part of the tick because a newly streamed-in placement needs its paint published the first time
    /// it is seen — a model whose revision never changes would otherwise leave <see cref="ProceduralComponent.Paint"/>
    /// stale until something unrelated bumped the tick.
    /// </summary>
    public void Update()
    {
        var tick = (Context.Catalog.Version, Context.Scene.Version, ProceduralModel.RevisionTick);
        if (tick == _lastUpdateTick)
        {
            return;
        }

        _lastUpdateTick = tick;

        foreach (SceneEntity entity in Context.Scene.Entities)
        {
            if (entity.Component<ProceduralComponent>() is not { } component)
            {
                continue;
            }

            ReportDangling(entity, component);

            if (!entity.IsRepresented)
            {
                Context.Problems.Replace(MissingChannelScope(entity), []);
                continue;
            }

            if (component.NeedsRefresh)
            {
                entity.RefreshRepresentation();
            }

            ReportMissingChannels(entity, component);
        }
    }

    /// <summary>Surfaces a placement whose <see cref="ProceduralComponent.ModelId"/> no longer
    /// resolves — e.g. the model was removed by an undo, a script, or a failed load — through
    /// <see cref="ProblemSystem"/> rather than throwing. One scope per entity, so a fixed reference
    /// clears its own report the next time this runs.</summary>
    private void ReportDangling(SceneEntity entity, ProceduralComponent component)
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

    /// <summary>Surfaces a placement whose published paint names a channel the open map's
    /// <see cref="LandscapeCatalog"/> does not declare — otherwise a global model placed in a map that
    /// lacks the channel just quietly paints nothing.</summary>
    private void ReportMissingChannels(SceneEntity entity, ProceduralComponent component)
    {
        string scope = MissingChannelScope(entity);
        List<string> missing = component.Paint.Channels
            .Where(channel => Context.Landscape.Catalog.Channels.All(existing => existing.Name != channel))
            .ToList();

        if (missing.Count == 0)
        {
            Context.Problems.Replace(scope, []);
            return;
        }

        Context.Problems.Replace(scope,
        [
            new Problem
            {
                Key = "missing-channel",
                Severity = ProblemSeverity.Warning,
                Category = "Procedural Mesh",
                Kind = "MissingChannel",
                Message = $"{entity.DisplayName} paints channel(s) {string.Join(", ", missing)}, which this map does not declare.",
                Entity = entity.Id,
            }
        ]);
    }

    private static string MissingChannelScope(SceneEntity entity) => $"procedural-mesh-channels:{entity.Id.Value}";

    /// <summary>Builds a model, from cache when nothing about it (or the presets/function it depends
    /// on) has changed since the last build.</summary>
    public ProceduralBuildResult Build(ProceduralModel model)
    {
        string key = BuildCacheKey(model);
        lock (_buildCacheOrder)
        {
            if (_buildCache.TryGetValue(key, out LinkedListNode<(string Key, ProceduralBuildResult Result)>? node))
            {
                _buildCacheOrder.Remove(node);
                _buildCacheOrder.AddFirst(node);
                return node.Value.Result;
            }
        }

        ProceduralBuildResult built = BuildUncached(model);

        lock (_buildCacheOrder)
        {
            if (!_buildCache.ContainsKey(key))
            {
                var node = new LinkedListNode<(string Key, ProceduralBuildResult Result)>((key, built));
                _buildCacheOrder.AddFirst(node);
                _buildCache[key] = node;

                while (_buildCacheOrder.Count > MaxCachedBuilds)
                {
                    LinkedListNode<(string Key, ProceduralBuildResult Result)>? last = _buildCacheOrder.Last;
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

    private ProceduralBuildResult BuildUncached(ProceduralModel model)
    {
        var builder = new ProceduralOutputBuilder();
        if (Find(model.FunctionId) is not { } function)
        {
            return ProceduralBuildResult.Empty;
        }

        var formatIds = new Dictionary<ProceduralOutputSlot, string>();
        var formats = new Dictionary<ProceduralOutputSlot, IModelFormat?>();
        var materialTypes = new Dictionary<ProceduralOutputSlot, IMeshMaterialType?>();
        var materials = new Dictionary<ProceduralOutputSlot, IReadOnlyDictionary<string, MeshMaterial>>();

        foreach (ProceduralOutputSlot slot in function.Outputs)
        {
            string formatId = ResolveFormatId(model, slot);
            IModelFormat? format = Context.ModelFormats.Find(formatId);
            IMeshMaterialType? materialType = format != null ? Context.MeshMaterials.Find(format.MaterialTypeId) : null;
            string materialTypeId = materialType?.Id ?? StandardMeshMaterial.TypeId;

            formatIds[slot] = formatId;
            formats[slot] = format;
            materialTypes[slot] = materialType;
            materials[slot] = ResolveMaterials(model, slot, materialTypeId);
        }

        function.Build(new ProceduralBuildContext(
            model.Network,
            MeshParameterValues.Parse(model.Parameters),
            Context.Assets,
            slot => formats.GetValueOrDefault(slot),
            slot => materialTypes.GetValueOrDefault(slot),
            (slot, materialSlot) => ResolveSlotMaterial(materials, materialTypes, slot, materialSlot)),
            builder);

        return builder.Build(function.Outputs, slot => formatIds.GetValueOrDefault(slot, MeshModelFormat.FormatId));
    }

    /// <summary>Public overload for inspector/UI callers that only have the model, not its bound function.</summary>
    public string ResolveFormatId(ProceduralModel model, ProceduralOutputSlot slot)
    {
        string? stored = ProceduralFormats.Parse(model.Formats).Get(slot);
        bool allowsAnyFormat = slot.SupportedFormats.Count == 0;
        if (stored is { Length: > 0 } && (allowsAnyFormat || slot.SupportedFormats.Contains(stored)))
        {
            return stored;
        }

        return allowsAnyFormat ? MeshModelFormat.FormatId : slot.SupportedFormats[0];
    }

    private Dictionary<string, MeshMaterial> ResolveMaterials(ProceduralModel model, ProceduralOutputSlot slot, string materialTypeId)
    {
        var result = new Dictionary<string, MeshMaterial>(StringComparer.Ordinal);
        if (slot.MaterialSlots.Count == 0)
        {
            return result;
        }

        ProceduralBindings bindings = ProceduralBindings.Parse(model.Materials);
        foreach (MeshMaterialSlot materialSlot in slot.MaterialSlots)
        {
            result[materialSlot.Name] = ResolveSlot(bindings, slot, materialSlot, materialTypeId);
        }

        return result;
    }

    private MeshMaterial ResolveSlot(ProceduralBindings bindings, ProceduralOutputSlot slot, MeshMaterialSlot materialSlot, string materialTypeId)
    {
        if (bindings.GetPresetId(slot, materialSlot) is { } presetId)
        {
            MeshMaterialPreset? preset = Context.MeshMaterials.Presets.FirstOrDefault(p => p.RecordId == presetId);
            if (preset != null && preset.TypeId == materialTypeId)
            {
                return preset.ToMaterial();
            }
        }

        if (bindings.GetInlineValues(slot, materialSlot) is { } inline)
        {
            return new MeshMaterial(materialTypeId, inline);
        }

        return Context.MeshMaterials.Find(materialTypeId)?.Default ?? new MeshMaterial(materialTypeId, new MeshParameterValues());
    }

    /// <summary>Resolves one material slot at build time, using whichever output it belongs to's
    /// resolved bindings and falling back to that output's material type default.</summary>
    private static MeshMaterial ResolveSlotMaterial(
        Dictionary<ProceduralOutputSlot, IReadOnlyDictionary<string, MeshMaterial>> materials,
        Dictionary<ProceduralOutputSlot, IMeshMaterialType?> materialTypes,
        ProceduralOutputSlot slot,
        MeshMaterialSlot materialSlot)
    {
        if (materials.TryGetValue(slot, out IReadOnlyDictionary<string, MeshMaterial>? bySlot) &&
            bySlot.TryGetValue(materialSlot.Name, out MeshMaterial? material))
        {
            return material;
        }

        string materialTypeId = materialTypes.GetValueOrDefault(slot)?.Id ?? StandardMeshMaterial.TypeId;
        return new MeshMaterial(materialTypeId, new MeshParameterValues());
    }

    public void DiscoverFrom(IEnumerable<IProceduralFunction> functions)
    {
        _byId.Clear();
        _warnings.Clear();

        foreach (IProceduralFunction function in functions)
        {
            Register(function);
        }

        All = _byId.Values.OrderBy(function => function.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        Version++;

        foreach (string warning in _warnings)
        {
            GD.PushWarning($"[Procedural] Function skipped - {warning}");
        }
    }

    private void Register(IProceduralFunction function)
    {
        if (function.Id.Trim().Length == 0)
        {
            _warnings.Add($"{function.GetType().Name}: has an empty Id.");
            return;
        }

        if (_byId.TryGetValue(function.Id, out IProceduralFunction? existing))
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

    private static string? DuplicateParameter(IProceduralFunction function)
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
