using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's tool windows as <see cref="ISubsystem"/>s. Windows declare
/// [Subsystem(nameof(WindowManager))] and are constructed automatically by the generated
/// InitializeSubsystems(), replacing manual registration of each window in <see cref="Editor"/>.
/// WindowManager is itself a top-level menu, self-registered with <see cref="MenuBarManager"/>.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed partial class WindowManager : ISubsystemHost, IMainMenu
{
    public float Priority => 1f;

    /// <summary>The editor's shared systems, forwarded to the windows.</summary>
    public EditorContext Context { get; }

    public ImGuiLayoutProfiles LayoutProfiles { get; }

    public IEnumerable<Window> Windows => Subsystems.Cast<Window>();

    private readonly Dictionary<Window, ShortcutAction> _windowShortcuts = [];
    private ShortcutAction _layoutProfilesShortcut = null!;
    private bool _layoutProfilesOpen;
    private string _profileName = string.Empty;
    private string? _profileMessage;
    private bool _profileMessageIsError;

    public WindowManager(MenuBarManager manager)
    {
        Context = manager.Context;
        InitializeSubsystems();
        RegisterWindowShortcuts();
        _layoutProfilesShortcut = Context.Shortcuts.Register(
            "window.layout-profiles",
            "Window",
            "Layout Profiles",
            new KeyboardShortcut(ImGuiKey.L, ShortcutModifiers.Alt),
            () => _layoutProfilesOpen = true);
        LayoutProfiles = new ImGuiLayoutProfiles(this);
        if (!LayoutProfiles.LoadCurrent(out string? error))
        {
            _profileMessage = $"Could not load current layout: {error}";
            _profileMessageIsError = true;
        }
    }

    public void Draw()
    {
        foreach (Window window in Windows)
        {
            window.Draw();
        }

        DrawLayoutProfilesWindow();
        LayoutProfiles.UpdateAutosave();
    }

    public void DrawMenuItems()
    {
        foreach (Window window in Windows)
        {
            _windowShortcuts.TryGetValue(window, out ShortcutAction? shortcut);
            window.DrawMenuItem(shortcut?.ShortcutLabel);
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Layout Profiles...", _layoutProfilesShortcut.ShortcutLabel))
        {
            _layoutProfilesOpen = true;
        }
    }

    void IMainMenu.Draw() => ImGuiEx.Menu("Window", DrawMenuItems);

    private void DrawLayoutProfilesWindow()
    {
        if (!_layoutProfilesOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(420.0f, 320.0f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Layout Profiles", ref _layoutProfilesOpen))
        {
            ImGui.End();
            return;
        }

        ImGui.InputTextWithHint("Name", "Profile name", ref _profileName, 128);
        ImGui.SameLine();
        if (ImGui.Button("Save"))
        {
            if (LayoutProfiles.SaveProfile(_profileName, out string? error))
            {
                SetProfileMessage($"Saved '{_profileName.Trim()}'.", false);
            }
            else
            {
                SetProfileMessage(error ?? "Could not save profile.", true);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Save Current"))
        {
            if (LayoutProfiles.SaveCurrent(out string? error))
            {
                SetProfileMessage("Saved current layout.", false);
            }
            else
            {
                SetProfileMessage(error ?? "Could not save current layout.", true);
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled(LayoutProfiles.Folder);

        IReadOnlyList<string> profiles = LayoutProfiles.Profiles();
        if (profiles.Count == 0)
        {
            ImGui.TextDisabled("No saved profiles.");
        }
        else if (ImGui.BeginTable("LayoutProfilesTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Profile");
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 72.0f);
            ImGui.TableHeadersRow();

            foreach (string profile in profiles)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(profile);
                ImGui.TableNextColumn();
                ImGui.PushID(profile);
                if (ImGui.SmallButton("Load"))
                {
                    if (LayoutProfiles.LoadProfile(profile, out string? error))
                    {
                        _profileName = profile;
                        SetProfileMessage($"Loaded '{profile}'.", false);
                    }
                    else
                    {
                        SetProfileMessage(error ?? "Could not load profile.", true);
                    }
                }
                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (!string.IsNullOrEmpty(_profileMessage))
        {
            ImGui.Separator();
            ImGui.TextColored(
                _profileMessageIsError
                    ? new System.Numerics.Vector4(1.0f, 0.35f, 0.30f, 1.0f)
                    : new System.Numerics.Vector4(0.45f, 0.85f, 0.55f, 1.0f),
                _profileMessage);
        }

        ImGui.End();
    }

    private void SetProfileMessage(string message, bool isError)
    {
        _profileMessage = message;
        _profileMessageIsError = isError;
    }

    private void RegisterWindowShortcuts()
    {
        foreach (Window window in Windows)
        {
            ShortcutAction action = Context.Shortcuts.Register(
                $"window.{StableId(window.Title)}",
                "Window",
                window.Title,
                DefaultWindowShortcut(window.Title),
                () => window.IsOpen = !window.IsOpen);
            _windowShortcuts[window] = action;
        }
    }

    private static KeyboardShortcut DefaultWindowShortcut(string title) => title switch
    {
        "Chunks" => Alt(ImGuiKey.Z),
        "Compute Materials" => Alt(ImGuiKey.N),
        "Environment" => Alt(ImGuiKey.Y),
        "Export" => Alt(ImGuiKey.X),
        "Inspector" => Alt(ImGuiKey.I),
        "Keybindings" => Alt(ImGuiKey.K),
        "Landscape" => Alt(ImGuiKey.H),
        "Landscape Debug" => Alt(ImGuiKey.B),
        "Landscape Materials" => Alt(ImGuiKey.M),
        "Outline" => Alt(ImGuiKey.O),
        "Performance" => Alt(ImGuiKey.P),
        "Problems" => Alt(ImGuiKey.R),
        "Script Console" => Alt(ImGuiKey.F),
        "Test Runner" => Alt(ImGuiKey.J),
        "Tools" => Alt(ImGuiKey.T),
        "Undo History" => Alt(ImGuiKey.U),
        "Viewport" => Alt(ImGuiKey.V),
        "Work Queue" => Alt(ImGuiKey.W),
        "Work Queue Tester" => Alt(ImGuiKey.Q),
        _ => KeyboardShortcut.None,
    };

    private static KeyboardShortcut Alt(ImGuiKey key) => new(key, ShortcutModifiers.Alt);

    private static string StableId(string value)
    {
        IEnumerable<char> chars = value.Trim().ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-');
        return string.Join('-', new string(chars.ToArray()).Split('-', System.StringSplitOptions.RemoveEmptyEntries));
    }
}
