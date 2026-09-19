using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's tool windows as <see cref="ISubsystem"/>s. Windows declare
/// [Subsystem(nameof(WindowManager))] and are constructed automatically by the generated
/// InitializeSubsystems(), replacing manual registration of each window in <see cref="Editor"/>.
/// The "Window" menu that lists them is <see cref="WindowMenu"/>.
/// </summary>
[Subsystem(nameof(EditorContext))]
[SubsystemHost(typeof(Window))]
public sealed partial class WindowManager : ISubsystemHost, ISubsystem
{
    /// <summary>The editor's shared systems, forwarded to the windows.</summary>
    public EditorContext Context { get; }

    public ImGuiLayoutProfiles LayoutProfiles { get; }

    /// <summary>Why the saved layout could not be loaded at startup, if it could not.</summary>
    public string? LayoutLoadError { get; }

    public IEnumerable<Window> Windows => Subsystems;

    private readonly Dictionary<Window, ShortcutAction> _windowShortcuts = [];

    public WindowManager(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
        RegisterWindowShortcuts();
        LayoutProfiles = new ImGuiLayoutProfiles(this);
        if (!LayoutProfiles.LoadCurrent(out string? error))
        {
            LayoutLoadError = $"Could not load current layout: {error}";
        }
    }

    /// <summary>Opens and focuses the Catalog Browser at <paramref name="catalogName"/>/<paramref name="key"/> —
    /// what a <see cref="CatalogReferenceField"/> falls back to when drawn somewhere with no
    /// <c>navigate</c> callback of its own (an inspector window, rather than the browser itself).</summary>
    public bool OpenCatalogEntry(string catalogName, string key) =>
        Windows.OfType<CatalogBrowserWindow>().FirstOrDefault()?.Open(catalogName, key) ?? false;

    public void Draw()
    {
        foreach (Window window in Windows)
        {
            window.Draw();
        }

        LayoutProfiles.UpdateAutosave();
    }

    /// <summary>The label of the shortcut that toggles <paramref name="window"/>, for its menu entry.</summary>
    public string? ShortcutLabel(Window window) =>
        _windowShortcuts.TryGetValue(window, out ShortcutAction? shortcut) ? shortcut.ShortcutLabel : null;

    private void RegisterWindowShortcuts()
    {
        foreach (Window window in Windows)
        {
            ShortcutAction action = Context.Shortcuts.Register(
                $"window.{StableId(window.Title)}",
                "Window",
                window.Title,
                window.DefaultShortcut,
                () => window.IsOpen = !window.IsOpen);
            _windowShortcuts[window] = action;
        }
    }

    private static string StableId(string value)
    {
        IEnumerable<char> chars = value.Trim().ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-');
        return string.Join('-', new string(chars.ToArray()).Split('-', System.StringSplitOptions.RemoveEmptyEntries));
    }
}
