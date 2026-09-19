using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>How badly a preset catalog issue breaks things. Mirrors <c>LandscapeIssueSeverity</c>.</summary>
public enum MeshMaterialIssueSeverity
{
    Warning,
    Error,
}

/// <summary>One thing wrong with the authored preset catalog, shown while editing it.</summary>
public readonly record struct MeshMaterialIssue(MeshMaterialIssueSeverity Severity, string Message)
{
    public override string ToString() => $"{Severity}: {Message}";
}

/// <summary>
/// Registry of <see cref="IMeshMaterialType"/>s and the catalog of saved <see cref="MeshMaterialPreset"/>s
/// built from them. Also the single place a <see cref="MeshMaterial"/>
/// description turns into a Godot <see cref="Material"/>, cached by <see cref="MeshMaterial.Key"/> so a
/// WMO with hundreds of batches sharing a handful of distinct materials builds each one once.
/// </summary>
public sealed partial class MeshMaterialSystem : ISubsystemHost, IWorldParticipant
{
    // Bounds the built-material cache so a long asset-browsing session cannot grow it without limit
    // (see AssetSystem's own unbounded texture cache, flagged in WowModelsPlan.md, for the failure
    // mode this avoids repeating).
    private const int MaxCachedMaterials = 4096;

    private readonly Dictionary<string, IMeshMaterialType> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];

    private readonly Dictionary<string, LinkedListNode<(string Key, Material Material)>> _materialCache = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, Material Material)> _materialCacheOrder = new();
    private int _cacheGeneration;

    public MeshMaterialSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
        DiscoverFrom(Subsystems.OfType<IMeshMaterialType>());
    }

    public EditorContext Context { get; }

    public IReadOnlyList<IMeshMaterialType> All { get; private set; } = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public int Version { get; private set; }

    /// <summary>The loaded preset catalog. Membership comes from <see cref="Context"/>'s catalog registry.</summary>
    public IEnumerable<MeshMaterialPreset> Presets => Context.Catalog.OfType<MeshMaterialPreset>();

    /// <summary>
    /// Changes whenever anything about a loaded preset that affects rendering changes. Computed
    /// rather than cached: presets are live entities edited in place, so registry membership alone
    /// (<see cref="CatalogEntityRegistry.Version"/>) would miss an in-place edit, exactly as
    /// <c>LandscapeCatalog.ContentVersion</c> documents for landscape materials.
    /// </summary>
    public int PresetContentVersion
    {
        get
        {
            var hash = new HashCode();
            foreach (MeshMaterialPreset preset in Presets)
            {
                hash.Add(preset.RecordId);
                hash.Add(preset.Name);
                hash.Add(preset.TypeId);
                hash.Add(preset.Parameters);
            }

            return hash.ToHashCode();
        }
    }

    // Catalog rows themselves come from DatabaseSystem's own IWorldParticipant, which bulk-loads every
    // registered catalog type before anything that resolves against one.
    float IWorldParticipant.LoadPriority => 2f;

    string? IWorldParticipant.LoadStep => "Loading mesh materials";

    void IWorldParticipant.LoadWorld() => Version++;

    void IWorldParticipant.UnloadWorld()
    {
        ClearBuildCache();
        Version++;
    }

    /// <summary>The creation of a preset, not yet applied. A null <paramref name="name"/> takes the next
    /// free "Material N". Identified now, so a slot binding created in the same session can reference it.</summary>
    public CreateCatalogEntityCommand BuildCreatePresetCommand(string? name, string typeId, out MeshMaterialPreset preset)
    {
        preset = new MeshMaterialPreset
        {
            Name = name ?? UniqueName("Material", Presets.Select(existing => existing.Name)),
            TypeId = typeId,
        };

        Context.Catalog.AssignId(preset);
        return new CreateCatalogEntityCommand(Context.Catalog, preset);
    }

    /// <summary>The deletion of <paramref name="preset"/>, not yet applied.</summary>
    public DeleteCatalogEntityCommand BuildDeletePresetCommand(MeshMaterialPreset preset) =>
        new(Context.Catalog, preset);

    private static string UniqueName(string prefix, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken);
        for (int i = 1; ; i++)
        {
            string candidate = $"{prefix} {i}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Everything wrong with the preset catalog, worst first. Empty means it is usable.</summary>
    public IReadOnlyList<MeshMaterialIssue> Validate()
    {
        var issues = new List<MeshMaterialIssue>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (MeshMaterialPreset preset in Presets)
        {
            if (preset.Name.Trim().Length == 0)
            {
                issues.Add(new MeshMaterialIssue(MeshMaterialIssueSeverity.Error, "A material has no name."));
            }
            else if (!seenNames.Add(preset.Name))
            {
                issues.Add(new MeshMaterialIssue(MeshMaterialIssueSeverity.Error, $"Two materials are named '{preset.Name}'."));
            }

            if (Find(preset.TypeId) == null)
            {
                issues.Add(new MeshMaterialIssue(MeshMaterialIssueSeverity.Error,
                    $"Material '{preset.Name}' uses type '{preset.TypeId}', which nothing provides."));
            }
        }

        return issues.OrderByDescending(issue => issue.Severity).ToList();
    }

    public IMeshMaterialType? Find(string id) =>
        id.Length > 0 && _byId.TryGetValue(id, out IMeshMaterialType? type) ? type : null;

    /// <summary>Turns a description into a Godot material, from cache when nothing about it has changed.</summary>
    public Material Build(MeshMaterial material)
    {
        string key = $"{_cacheGeneration}|{material.Key}";
        lock (_materialCacheOrder)
        {
            if (_materialCache.TryGetValue(key, out LinkedListNode<(string Key, Material Material)>? node))
            {
                _materialCacheOrder.Remove(node);
                _materialCacheOrder.AddFirst(node);
                return node.Value.Material;
            }
        }

        Material built = Find(material.TypeId) is { } type
            ? type.Build(new MeshMaterialBuildContext(Context.Assets, material.Values))
            : MissingMaterial();

        lock (_materialCacheOrder)
        {
            if (!_materialCache.ContainsKey(key))
            {
                var node = new LinkedListNode<(string Key, Material Material)>((key, built));
                _materialCacheOrder.AddFirst(node);
                _materialCache[key] = node;

                while (_materialCacheOrder.Count > MaxCachedMaterials)
                {
                    LinkedListNode<(string Key, Material Material)>? last = _materialCacheOrder.Last;
                    if (last == null)
                    {
                        break;
                    }

                    _materialCacheOrder.RemoveLast();
                    _materialCache.Remove(last.Value.Key);
                }
            }
        }

        return built;
    }

    /// <summary>Drops every cached built material, e.g. when a material type's assets are reloaded.</summary>
    public void ClearBuildCache()
    {
        lock (_materialCacheOrder)
        {
            _cacheGeneration++;
            _materialCache.Clear();
            _materialCacheOrder.Clear();
        }
    }

    public void DiscoverFrom(IEnumerable<IMeshMaterialType> types)
    {
        _byId.Clear();
        _warnings.Clear();

        foreach (IMeshMaterialType type in types)
        {
            Register(type);
        }

        All = _byId.Values.OrderBy(type => type.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        Version++;
        ClearBuildCache();

        foreach (string warning in _warnings)
        {
            GD.PushWarning($"[MeshMaterial] Type skipped - {warning}");
        }
    }

    private void Register(IMeshMaterialType type)
    {
        if (type.Id.Trim().Length == 0)
        {
            _warnings.Add($"{type.GetType().Name}: has an empty Id.");
            return;
        }

        if (_byId.TryGetValue(type.Id, out IMeshMaterialType? existing))
        {
            _warnings.Add($"{type.GetType().Name}: id '{type.Id}' is already used by {existing.GetType().Name}.");
            return;
        }

        if (DuplicateParameter(type) is { } duplicate)
        {
            _warnings.Add($"{type.GetType().Name}: declares parameter '{duplicate}' twice.");
            return;
        }

        _byId[type.Id] = type;
    }

    private static string? DuplicateParameter(IMeshMaterialType type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MeshParameter parameter in type.Parameters)
        {
            if (!seen.Add(parameter.Name))
            {
                return parameter.Name;
            }
        }

        return null;
    }

    private static Material MissingMaterial() => new StandardMaterial3D
    {
        AlbedoColor = new Color(0.75f, 0.2f, 0.35f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
