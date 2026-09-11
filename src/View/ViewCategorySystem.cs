using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Registered <see cref="IViewCategory"/>s and which of them are currently hidden — the per-type
/// viewport visibility filter behind the "View" menu. Hosted as its own subsystem rather than by
/// <c>ViewMenu</c> so scripting and the HTTP endpoint can reach it whether or not the menu is
/// drawing, the same reason <see cref="ToolSystem"/> hosts <c>IToolFactory</c> rather than the tool
/// window owning it.
/// </summary>
public sealed partial class ViewCategorySystem : ISubsystemHost
{
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IViewCategory> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];

    // Visible is read multiple times a frame once consumers exist; rebuilding it from scratch on
    // every read would repeat the InView scan that consumer already pays for. Mirrors how InView
    // itself caches over SceneEntityRegistry.Version.
    private readonly List<SceneEntity> _visible = [];
    private int _visibleSceneVersion = -1;
    private int _visibleFilterVersion = -1;

    public ViewCategorySystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
        DiscoverFrom(Subsystems.OfType<IViewCategory>());
    }

    public EditorContext Context { get; }

    public IReadOnlyList<IViewCategory> All { get; private set; } = [];

    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Bumped on every hidden-set change, so a cache built over it (e.g. <see cref="Visible"/>)
    /// knows when to rebuild.</summary>
    public int Version { get; private set; }

    public void DiscoverFrom(IEnumerable<IViewCategory> categories)
    {
        _byId.Clear();
        _warnings.Clear();

        foreach (IViewCategory category in categories)
        {
            Register(category);
        }

        All = _byId.Values
            .OrderBy(category => category.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(category => category.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string warning in _warnings)
        {
            GD.PushWarning($"[ViewCategory] Category skipped - {warning}");
        }
    }

    private void Register(IViewCategory category)
    {
        if (category.Id.Trim().Length == 0)
        {
            _warnings.Add($"{category.GetType().Name}: has an empty Id.");
            return;
        }

        if (_byId.TryGetValue(category.Id, out IViewCategory? existing))
        {
            _warnings.Add($"{category.GetType().Name}: id '{category.Id}' is already used by {existing.GetType().Name}.");
            return;
        }

        _byId[category.Id] = category;
    }

    public IViewCategory? Find(string id) =>
        id.Length > 0 && _byId.TryGetValue(id, out IViewCategory? category) ? category : null;

    public bool IsHidden(string categoryId) => _hidden.Contains(categoryId);

    public void SetHidden(string categoryId, bool hidden)
    {
        bool changed = hidden ? _hidden.Add(categoryId) : _hidden.Remove(categoryId);
        if (changed)
        {
            Version++;
        }
    }

    public void ShowAll()
    {
        if (_hidden.Count == 0)
        {
            return;
        }

        _hidden.Clear();
        Version++;
    }

    /// <summary>True when any category that includes this entity is currently hidden — unions, not
    /// intersections.</summary>
    public bool IsHidden(SceneEntity entity)
    {
        if (_hidden.Count == 0)
        {
            return false;
        }

        foreach (IViewCategory category in All)
        {
            if (_hidden.Contains(category.Id) && category.Includes(entity))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><see cref="SceneEntityRegistry.InView"/> with whatever a hidden category excludes
    /// filtered out, rebuilt only when scene membership or the hidden set has moved since the last
    /// read.</summary>
    public IReadOnlyList<SceneEntity> Visible
    {
        get
        {
            int sceneVersion = Context.Scene.Version;
            if (_visibleSceneVersion == sceneVersion && _visibleFilterVersion == Version)
            {
                return _visible;
            }

            _visibleSceneVersion = sceneVersion;
            _visibleFilterVersion = Version;
            _visible.Clear();
            foreach (SceneEntity entity in Context.Scene.InView)
            {
                if (!IsHidden(entity))
                {
                    _visible.Add(entity);
                }
            }

            return _visible;
        }
    }
}
