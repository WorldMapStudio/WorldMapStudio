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
    /// Takes a context that has already been constructed and started (by <see cref="LoadingScreen"/>),
    /// so opening the editor is instant rather than blocking on the database.
    /// </summary>
    public Editor(EditorContext context)
    {
        _context = context;
    }

    public void Start()
    {
    }

    public IScene? Update()
    {
        MenuBarManager menuBar = _context.MenuBarManager;

        ImGuiEx.MainMenuBar(() =>
        {
            menuBar.Draw();

            ImGui.Separator();
            ImGui.TextDisabled(_context.Project.Name);
        });

        ImGui.DockSpaceOverViewport();

        menuBar.WindowManager.Draw();

        return menuBar.FileMenuManager.ExitRequested ? null : this;
    }
}
