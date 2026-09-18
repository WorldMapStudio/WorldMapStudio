using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// The delete confirmation popup shared by the map picker's "Delete map…" and the stray-data cleanup
/// list: a "delete everything" checkbox (on by default) with the counted rows underneath, and one
/// checkbox per resource kind referenced only by the target — offered only while "delete everything" is
/// ticked, since otherwise the entities that would make them unused are staying right where they are.
/// Counting runs on a worker; the popup shows "Counting…" until it lands rather than blocking the UI
/// thread for it. Drawn every frame regardless of which other popup (if any) opened it, so it survives
/// that popup closing.
/// </summary>
public sealed class MapDeleteConfirmPopup
{
    private static readonly Vector4 ErrorColor = new(0.95f, 0.5f, 0.4f, 1.0f);

    private readonly string _popupId;

    private string _title = "";
    private Func<MapDeleteOptions, WorkHandle?>? _delete;
    private Task<MapContents>? _contentsTask;
    private bool _deleteContents = true;
    private readonly HashSet<Type> _deleteResources = [];
    private string? _error;
    private bool _open;

    public MapDeleteConfirmPopup(string popupId)
    {
        _popupId = popupId;
    }

    /// <summary>A caller's delete method, matching both <see cref="MapSystem.Delete"/> and
    /// <see cref="MapSystem.PurgeStrayMap"/>: it reports its error via <c>out string?</c> and returns
    /// the scheduled work, or null if it couldn't start.</summary>
    public delegate WorkHandle? DeleteAction(MapDeleteOptions options, out string? error);

    /// <summary>Opens the popup for one target and kicks off its content count.</summary>
    public void Begin(MapSystem maps, MapId target, string title, DeleteAction delete)
    {
        _title = title;
        _deleteContents = true;
        _deleteResources.Clear();
        _error = null;
        _contentsTask = maps.DescribeContentsAsync(target);
        _delete = options => delete(options, out _error);
        _open = true;
        ImGui.OpenPopup(_popupId);
    }

    public void Draw()
    {
        if (_delete == null)
        {
            return;
        }

        bool open = _open;
        ImGuiEx.PopupModal(_popupId, true, ref open, ImGuiWindowFlags.AlwaysAutoResize, () =>
        {
            ImGui.TextUnformatted($"Delete {_title}?");
            ImGui.Spacing();

            bool ready = _contentsTask is { IsCompleted: true };
            if (!ready)
            {
                ImGui.TextDisabled("Counting…");
            }
            else if (_contentsTask!.IsFaulted)
            {
                ImGui.TextColored(ErrorColor, _contentsTask.Exception!.GetBaseException().Message);
            }
            else
            {
                DrawContents(_contentsTask.Result);
            }

            ImGui.Spacing();
            ImGui.TextDisabled("This can't be undone.");

            if (_error != null)
            {
                ImGui.TextColored(ErrorColor, _error);
            }

            ImGui.BeginDisabled(!ready || _contentsTask!.IsFaulted);
            if (ImGui.Button("Delete", new Vector2(100, 0)))
            {
                var options = new MapDeleteOptions(_deleteContents, _deleteResources);
                if (_delete(options) != null)
                {
                    _open = false;
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _open = false;
                ImGui.CloseCurrentPopup();
            }
        });

        _open = open;
    }

    private void DrawContents(MapContents contents)
    {
        ImGui.Checkbox("Delete everything in this map", ref _deleteContents);

        ImGui.Indent();
        if (contents.Data.Count == 0)
        {
            ImGui.TextDisabled("Nothing to delete.");
        }
        else
        {
            foreach ((string label, int count) in contents.Data)
            {
                ImGui.TextDisabled($"{label}  {count:N0}");
            }
        }

        ImGui.Unindent();

        if (!_deleteContents)
        {
            ImGui.TextDisabled("Entities placed in it keep their rows and their map id.");
            return;
        }

        if (contents.MapOnlyResources.Count == 0)
        {
            return;
        }

        ImGui.Spacing();
        foreach ((Type resourceType, string label, int count) in contents.MapOnlyResources)
        {
            bool ticked = _deleteResources.Contains(resourceType);
            if (ImGui.Checkbox($"Also delete {label.ToLowerInvariant()} only this map uses ({count:N0})", ref ticked))
            {
                if (ticked)
                {
                    _deleteResources.Add(resourceType);
                }
                else
                {
                    _deleteResources.Remove(resourceType);
                }
            }
        }
    }
}
