#nullable enable
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The editor scene: a docked ImGui workspace of tool windows over the 3D viewport.
/// </summary>
public sealed class Editor : IScene
{
    private readonly EditorContext _context;

    /// <summary>
    /// Takes a context that <see cref="LoadingScreen"/> has already started and loaded, so opening the
    /// editor is instant rather than blocking on the database.
    /// </summary>
    public Editor(EditorContext context)
    {
        _context = context;
    }

    public void Start()
    {
        // The only startup work left on the main thread: no database behind it, and it has to happen
        // once every module (built-in and plugin) has finished constructing, before the binder
        // reflects over them.
        _context.Scripting.Startup();
    }

    public IScene? Update()
    {
        // Checked first, before any subsystem's own per-frame Update runs: once something has asked
        // for a reload (an aborted session, a batch operation), nothing here should keep acting on
        // state that is about to be dropped.
        if (_context.PendingReloadReason != null)
        {
            return new WorldReload(_context, this);
        }

        MenuBarManager menuBar = _context.MenuBarManager;

        // Regardless of which windows are open, the same way the viewport's fly camera keeps moving.
        _context.Frame.Tick();

        ImGuiEx.MainMenuBar(() =>
        {
            menuBar.Draw();

            ImGui.Separator();
            ImGui.TextDisabled($"{_context.Project.Name} — {_context.Maps.Current.DisplayName}");
        });

        ImGui.DockSpaceOverViewport();

        _context.WindowManager.Draw();
        menuBar.DrawOverlay();

        if (_context.ExitRequested)
        {
            _context.WindowManager.LayoutProfiles.SaveCurrent(out _);
            return null;
        }

        return this;
    }
}
