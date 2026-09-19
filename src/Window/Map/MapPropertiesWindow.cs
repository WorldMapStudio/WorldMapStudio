using System;
using System.Linq;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// One map's settings: the built-in General block plus every registered
/// <see cref="IMapPropertiesSection"/>, each its own collapsible header — roomier than the map
/// picker's card popup. A header combo picks which map is shown; "Follow current map" (on by
/// default) tracks <see cref="MapSystem.CurrentMap"/> instead.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class MapPropertiesWindow : Window
{
    private static readonly Vector4 ErrorColor = new(0.95f, 0.5f, 0.4f, 1.0f);

    public override string? Category => "World";

    private readonly EditorContext _context;

    private MapId? _target;
    private bool _followCurrent = true;

    private MapId? _renameBufferMap;
    private string _renameBuffer = string.Empty;
    private string? _renameError;

    public MapPropertiesWindow(WindowManager manager)
        : base("Map Properties", startOpen: false, defaultSize: new Vector2(420.0f, 480.0f))
    {
        _context = manager.Context;
    }

    /// <summary>Opens and focuses the window on <paramref name="map"/>. Follow-current turns off unless
    /// <paramref name="map"/> is already the open map.</summary>
    public void Open(MapId map)
    {
        IsOpen = true;
        ImGui.SetWindowFocus(Title);
        _target = map;
        _followCurrent = map == _context.Maps.CurrentMap;
    }

    protected override void OnBeforeDraw()
    {
        if (_followCurrent)
        {
            _target = _context.Maps.CurrentMap;
        }
    }

    protected override void DrawContent()
    {
        Map? target = ResolveTarget();

        DrawHeader(target);
        ImGui.Separator();

        if (target is null)
        {
            ImGui.TextDisabled("No map to show.");
            return;
        }

        bool isCurrent = target.Id == _context.Maps.CurrentMap;

        DrawGeneral(target, isCurrent);

        foreach (IMapPropertiesSection section in _context.MapProperties.All)
        {
            if (!section.AppliesTo(target))
            {
                continue;
            }

            ImGui.PushID(section.Label);
            if (ImGui.CollapsingHeader(section.Label, ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawSection(section, target, isCurrent);
            }

            ImGui.PopID();
        }
    }

    // Falls back to the current map if the target was deleted or a reload dropped it from the list.
    private Map? ResolveTarget()
    {
        if (_target is not { } id)
        {
            return _context.Maps.Maps.Count > 0 ? _context.Maps.Current : null;
        }

        Map? map = _context.Maps.Maps.FirstOrDefault(candidate => candidate.Id == id);
        if (map is not null)
        {
            return map;
        }

        _target = _context.Maps.CurrentMap;
        return _context.Maps.Current;
    }

    private void DrawHeader(Map? target)
    {
        ImGui.SetNextItemWidth(260.0f);
        string label = target is null ? "(none)" : $"{target.DisplayName} (id {target.Id.Value})";
        if (ImGui.BeginCombo("##targetMap", label))
        {
            foreach (Map map in _context.Maps.Maps)
            {
                bool selected = map.Id == target?.Id;
                if (ImGui.Selectable($"{map.DisplayName} (id {map.Id.Value})##{map.Id.Value}", selected) && !selected)
                {
                    _target = map.Id;
                    _followCurrent = false;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Checkbox("Follow current map", ref _followCurrent) && _followCurrent)
        {
            _target = _context.Maps.CurrentMap;
        }
    }

    private void DrawGeneral(Map target, bool isCurrent)
    {
        if (!ImGui.CollapsingHeader("General", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (_renameBufferMap != target.Id)
        {
            _renameBufferMap = target.Id;
            _renameBuffer = target.Name;
            _renameError = null;
        }

        ImGui.Text($"Id: {target.Id.Value}");

        bool canEdit = target.Source is { CanEdit: true };
        ImGui.BeginDisabled(!canEdit);

        ImGui.SetNextItemWidth(220.0f);
        bool committed = ImGui.InputText("Name##rename", ref _renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button("Rename") || committed)
        {
            _renameError = _context.Maps.Rename(target, _renameBuffer);
        }

        ImGui.EndDisabled();

        if (!canEdit)
        {
            ImGui.TextDisabled("This map comes from a read-only source.");
        }

        if (_renameError != null)
        {
            ImGui.TextColored(ErrorColor, _renameError);
        }

        ImGui.Text($"Source: {target.Source?.GetType().Name ?? "(none)"}");

        if (!isCurrent && ImGui.Button("Open this map"))
        {
            _context.Maps.Enter(target);
        }
    }

    // One broken plugin section shouldn't take the whole window down with it.
    private void DrawSection(IMapPropertiesSection section, Map target, bool isCurrent)
    {
        try
        {
            section.Draw(_context, target, isCurrent);
        }
        catch (Exception e)
        {
            ImGui.TextColored(ErrorColor, $"'{section.Label}' failed to draw: {e.Message}");
        }
    }
}
