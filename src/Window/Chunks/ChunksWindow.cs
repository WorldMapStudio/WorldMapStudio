using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Lists the loaded <see cref="LandscapeChunk"/>s, split out of the <see cref="OutlineWindow"/>
/// because chunks are derived (computed from the landscape, not authored, and never reparented) and
/// there can be a lot of them — a flat, coordinate-sorted list suits them better than the outline's
/// hierarchy. Clicking an entry selects it (Ctrl/Shift to add or remove), mirroring the outline.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ChunksWindow : Window
{
    public override string? Category => "Landscape";
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.Z, ShortcutModifiers.Alt);

    private readonly SceneEntityRegistry _scene;
    private readonly SelectionSystem _selection;

    public ChunksWindow(WindowManager manager)
        : base("Chunks", startOpen: false, defaultSize: new Vector2(240, 400))
    {
        _scene = manager.Context.Scene;
        _selection = manager.Context.Selection;
    }

    protected override void DrawContent()
    {
        var chunks = _scene.InView.OfType<LandscapeChunk>()
            .OrderBy(chunk => chunk.Coord.X)
            .ThenBy(chunk => chunk.Coord.Y)
            .ToList();

        if (chunks.Count == 0)
        {
            ImGui.TextDisabled("No chunks loaded.");
            return;
        }

        foreach (LandscapeChunk chunk in chunks)
        {
            DrawChunk(chunk);
        }
    }

    private void DrawChunk(LandscapeChunk chunk)
    {
        if (!ImGui.Selectable($"{chunk.DisplayName}##{chunk.Id.Value}", _selection.IsSelected(chunk)))
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.KeyCtrl || io.KeyShift)
        {
            _selection.Toggle(chunk);
        }
        else
        {
            _selection.Set(chunk);
        }
    }
}
