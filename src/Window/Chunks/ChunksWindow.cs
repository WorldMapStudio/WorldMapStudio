using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Lists the loaded terrain chunks — walked out of the <see cref="LandscapeTerrainBatch"/>es that
/// draw them — split out of the <see cref="OutlineWindow"/> because chunks are derived and there can
/// be a lot of them. Clicking an entry selects the batch it belongs to (Ctrl/Shift to add or remove),
/// since a chunk is no longer a selectable entity of its own.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ChunksWindow : Window
{
    public override string? Category => "Landscape";
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.Z, ShortcutModifiers.Alt);

    private readonly SceneEntityRegistry _scene;
    private readonly SelectionSystem _selection;

    // The row list is O(loaded chunks) to build — thousands at a large batch/view size — and only
    // changes when the registry does, so it is held across frames rather than rebuilt each draw.
    private readonly List<(ChunkCoord Coord, LandscapeTerrainBatch Batch)> _rows = [];
    private int _rowsVersion = -1;

    public ChunksWindow(WindowManager manager)
        : base("Chunks", startOpen: false, defaultSize: new Vector2(240, 400))
    {
        _scene = manager.Context.Scene;
        _selection = manager.Context.Selection;
    }

    protected override void DrawContent()
    {
        if (_rowsVersion != _scene.Version)
        {
            _rowsVersion = _scene.Version;
            _rows.Clear();
            _rows.AddRange(_scene.InView.OfType<LandscapeTerrainBatch>()
                .SelectMany(batch => batch.Chunks.Keys.Select(coord => (coord, batch)))
                .OrderBy(row => row.coord.X)
                .ThenBy(row => row.coord.Y));
        }

        List<(ChunkCoord Coord, LandscapeTerrainBatch Batch)> rows = _rows;

        if (rows.Count == 0)
        {
            ImGui.TextDisabled("No chunks loaded.");
            return;
        }

        unsafe
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(rows.Count);
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    DrawRow(rows[i].Coord, rows[i].Batch);
                }
            }

            clipper.End();
            clipper.Destroy();
        }
    }

    private void DrawRow(ChunkCoord coord, LandscapeTerrainBatch batch)
    {
        if (!ImGui.Selectable($"Chunk {coord}##{coord.X}_{coord.Y}", _selection.IsSelected(batch)))
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.KeyCtrl || io.KeyShift)
        {
            _selection.Toggle(batch);
        }
        else
        {
            _selection.Set(batch);
        }
    }
}
