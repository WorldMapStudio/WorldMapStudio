using System.Collections.Generic;
using System.Threading.Tasks;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Lists data left behind by maps deleted before the delete-contents cleanup existed — an id with rows
/// in <see cref="IMapScopedData"/> owners but no <c>wms_maps</c> row of its own — and reuses the map
/// delete popup to purge each one, without the map-row step a real map's delete has. See
/// <see cref="MapSystem.FindStrayMapIdsAsync"/>.
/// </summary>
public sealed class StrayMapCleanupOperation : IModalOperation<MapSystem>
{
    private static readonly Vector4 ErrorColor = new(0.95f, 0.5f, 0.4f, 1.0f);
    private static readonly Vector2 BodySize = new(420, 320);

    private readonly MapDeleteConfirmPopup _deletePopup = new("Clean Up Stray Map Data");

    private Task<IReadOnlyList<int>>? _idsTask;
    private WorkHandle? _pendingPurge;

    public ModalOperationState Draw(MapSystem maps)
    {
        _idsTask ??= maps.FindStrayMapIdsAsync();

        // A finished purge leaves the list stale — refetch so the id it cleared (or its remainder)
        // drops out.
        if (_pendingPurge is { } purge && purge.State is WorkState.Completed or WorkState.Faulted or WorkState.Cancelled)
        {
            _pendingPurge = null;
            _idsTask = maps.FindStrayMapIdsAsync();
        }

        ImGui.TextUnformatted("Clean Up Deleted Maps' Data");
        ImGui.Separator();
        ImGui.TextDisabled("Data left behind by maps deleted before this cleanup existed.");
        ImGui.Spacing();

        ImGuiEx.Child("StrayMapList", BodySize, true, ImGuiWindowFlags.None, () => DrawList(maps));

        _deletePopup.Draw();

        var state = ModalOperationState.Running;
        if (ImGui.Button("Close", new Vector2(120, 0)))
        {
            state = ModalOperationState.Cancelled;
        }

        return state;
    }

    private void DrawList(MapSystem maps)
    {
        if (_idsTask is not { IsCompleted: true })
        {
            ImGui.TextDisabled("Looking…");
            return;
        }

        if (_idsTask.IsFaulted)
        {
            ImGui.TextColored(ErrorColor, _idsTask.Exception!.GetBaseException().Message);
            return;
        }

        IReadOnlyList<int> ids = _idsTask.Result;
        if (ids.Count == 0)
        {
            ImGui.TextDisabled("Nothing to clean up.");
            return;
        }

        foreach (int id in ids)
        {
            ImGui.PushID(id);
            ImGui.TextUnformatted($"Map id {id}");
            ImGui.SameLine();
            if (ImGui.Button("Clean up…"))
            {
                _deletePopup.Begin(maps, new MapId(id), $"map {id}'s stray data", (MapDeleteOptions options, out string? error) =>
                {
                    WorkHandle? handle = maps.PurgeStrayMap(id, options, out error);
                    _pendingPurge = handle;
                    return handle;
                });
            }

            ImGui.PopID();
        }
    }
}
