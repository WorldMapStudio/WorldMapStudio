#nullable enable
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The editor scene: a docked ImGui workspace of tool windows over the 3D viewport. This is the
/// content that used to live directly on the root <c>WorldMapStudio</c> node, now hosted behind
/// <see cref="IScene"/> so the app can route through a main menu and project selection first.
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
        MenuBarManager menuBar = _context.MenuBarManager;

        // Entering another map swaps to that map's landscape settings.
        _context.Landscape.Update();

        // Resumes any script await-ing a Task-returning [ScriptFunction] whose Task has completed
        // since last frame (never blocks, mirrors WorkQueue's own per-frame main-thread pump), and
        // fires any registered wms.events handlers for what changed since last frame.
        _context.Scripting.Update();

        ImGuiEx.MainMenuBar(() =>
        {
            menuBar.Draw();

            ImGui.Separator();
            ImGui.TextDisabled($"{_context.Project.Name} — {_context.Maps.Current.DisplayName}");
        });

        ImGui.DockSpaceOverViewport();

        menuBar.WindowManager.Draw();
        menuBar.DrawOverlay();

        if (menuBar.FileMenuManager.ExitRequested)
        {
            menuBar.WindowManager.LayoutProfiles.SaveCurrent(out _);
            return null;
        }

        return this;
    }
}
