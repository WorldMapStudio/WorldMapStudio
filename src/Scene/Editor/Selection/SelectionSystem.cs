using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// The editor-wide selection, shared by the viewport, outline and inspector so they always agree.
/// Keyed on <see cref="IEntity"/>. <see cref="Version"/> bumps on every change so views can cheaply
/// tell when to refresh without subscribing.
/// </summary>
public sealed class SelectionSystem
{
    private readonly List<IEntity> _selected = [];

    public IReadOnlyList<IEntity> Selected => _selected;

    public int Version { get; private set; }

    public bool IsSelected(IEntity entity) => _selected.Contains(entity);

    public void Clear()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        foreach (IEntity entity in _selected)
        {
            Notify(entity, false);
        }

        _selected.Clear();
        Version++;
    }

    public void Set(IEntity entity)
    {
        Clear();
        Add(entity);
    }

    public void Add(IEntity entity)
    {
        if (_selected.Contains(entity))
        {
            return;
        }

        _selected.Add(entity);
        Notify(entity, true);
        Version++;
    }

    public void Remove(IEntity entity)
    {
        if (!_selected.Remove(entity))
        {
            return;
        }

        Notify(entity, false);
        Version++;
    }

    public void Toggle(IEntity entity)
    {
        if (_selected.Contains(entity))
        {
            Remove(entity);
        }
        else
        {
            Add(entity);
        }
    }

    private static void Notify(IEntity entity, bool selected)
    {
        if (entity is SceneEntity scene)
        {
            scene.OnSelectionChanged(selected);
        }
    }
}
