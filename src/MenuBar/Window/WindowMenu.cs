using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "Window" menu: every <see cref="Window"/> grouped by category, plus the Layout Profiles
/// dialog. Self-registers with <see cref="MenuBarManager"/>, after Scene.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class WindowMenu : IMainMenu
{
    private readonly EditorContext _context;
    private readonly ShortcutAction _layoutProfilesShortcut;
    private bool _layoutProfilesOpen;
    private string _profileName = string.Empty;
    private string? _profileMessage;
    private bool _profileMessageIsError;

    public float Priority => 1f;

    // Built before WindowManager, so it is only read from Draw and DrawOverlay, never here.
    private WindowManager Windows => _context.WindowManager;

    public WindowMenu(MenuBarManager manager)
    {
        _context = manager.Context;
        _layoutProfilesShortcut = _context.Shortcuts.Register(
            "window.layout-profiles",
            "Window",
            "Layout Profiles",
            new KeyboardShortcut(ImGuiKey.L, ShortcutModifiers.Alt),
            () => _layoutProfilesOpen = true);
    }

    public void Draw() => ImGuiEx.Menu("Window", DrawMenuItems);

    public void DrawOverlay() => DrawLayoutProfilesWindow();

    private void DrawMenuItems()
    {
        foreach (Window window in Windows.Windows.Where(window => window.Category is null))
        {
            window.DrawMenuItem(Windows.ShortcutLabel(window));
        }

        foreach (IGrouping<string, Window> category in Windows.Windows
            .Where(window => window.Category is not null)
            .GroupBy(window => window.Category!)
            .OrderBy(category => category.Key))
        {
            ImGuiEx.Menu(category.Key, () =>
            {
                foreach (Window window in category)
                {
                    window.DrawMenuItem(Windows.ShortcutLabel(window));
                }
            });
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Layout Profiles...", _layoutProfilesShortcut.ShortcutLabel))
        {
            _layoutProfilesOpen = true;
        }
    }

    private void DrawLayoutProfilesWindow()
    {
        if (!_layoutProfilesOpen)
        {
            return;
        }

        ImGuiLayoutProfiles store = Windows.LayoutProfiles;

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
            if (store.SaveProfile(_profileName, out string? error))
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
            if (store.SaveCurrent(out string? error))
            {
                SetProfileMessage("Saved current layout.", false);
            }
            else
            {
                SetProfileMessage(error ?? "Could not save current layout.", true);
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled(store.Folder);

        IReadOnlyList<string> profiles = store.Profiles();
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
                    if (store.LoadProfile(profile, out string? error))
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

        string? message = _profileMessage ?? Windows.LayoutLoadError;
        if (!string.IsNullOrEmpty(message))
        {
            ImGui.Separator();
            ImGui.TextColored(
                _profileMessage is null || _profileMessageIsError
                    ? new System.Numerics.Vector4(1.0f, 0.35f, 0.30f, 1.0f)
                    : new System.Numerics.Vector4(0.45f, 0.85f, 0.55f, 1.0f),
                message);
        }

        ImGui.End();
    }

    private void SetProfileMessage(string message, bool isError)
    {
        _profileMessage = message;
        _profileMessageIsError = isError;
    }
}
