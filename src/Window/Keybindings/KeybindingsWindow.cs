using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(WindowManager))]
public sealed class KeybindingsWindow : Window
{
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.K, ShortcutModifiers.Alt);

    private readonly ShortcutSystem _shortcuts;
    private ShortcutAction? _capturing;
    private string _filter = string.Empty;

    public KeybindingsWindow(WindowManager manager)
        : base("Keybindings", startOpen: false, defaultSize: new Vector2(680.0f, 460.0f))
    {
        _shortcuts = manager.Context.Shortcuts;
    }

    protected override void OnBeforeDraw()
    {
        if (!IsOpen)
        {
            _capturing = null;
        }

        _shortcuts.Suspended = _capturing is not null;
    }

    protected override void DrawContent()
    {
        ImGui.InputTextWithHint("Filter", "Action or group", ref _filter, 128);

        if (_capturing is { } action)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.45f, 0.75f, 1.0f, 1.0f), $"Press keys for {action.Name}");

            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                _capturing = null;
            }
            else if (ShortcutSystem.TryReadPressedShortcut(out KeyboardShortcut shortcut))
            {
                _shortcuts.SetShortcut(action, shortcut);
                _capturing = null;
            }
        }

        ImGui.Separator();

        if (!ImGui.BeginTable("KeybindingsTable", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Resizable))
        {
            return;
        }

        ImGui.TableSetupColumn("Action");
        ImGui.TableSetupColumn("Shortcut", ImGuiTableColumnFlags.WidthFixed, 120.0f);
        ImGui.TableSetupColumn("Default", ImGuiTableColumnFlags.WidthFixed, 120.0f);
        ImGui.TableSetupColumn("Conflict", ImGuiTableColumnFlags.WidthFixed, 120.0f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 132.0f);
        ImGui.TableHeadersRow();

        foreach (ShortcutAction rowAction in _shortcuts.Actions.Where(MatchesFilter))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text($"{rowAction.Group} / {rowAction.Name}");

            ImGui.TableNextColumn();
            ImGui.TextDisabled(string.IsNullOrEmpty(rowAction.ShortcutLabel) ? "Unbound" : rowAction.ShortcutLabel);

            ImGui.TableNextColumn();
            ImGui.TextDisabled(string.IsNullOrEmpty(rowAction.DefaultLabel) ? "Unbound" : rowAction.DefaultLabel);

            ImGui.TableNextColumn();
            ShortcutAction[] conflicts = _shortcuts.ConflictsFor(rowAction).ToArray();
            if (conflicts.Length > 0)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.55f, 0.35f, 1.0f), conflicts[0].Name);
            }
            else
            {
                ImGui.TextDisabled("-");
            }

            ImGui.TableNextColumn();
            ImGui.PushID(rowAction.Id);
            if (ImGui.SmallButton(_capturing == rowAction ? "Cancel" : "Bind"))
            {
                _capturing = _capturing == rowAction ? null : rowAction;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
            {
                _shortcuts.SetShortcut(rowAction, KeyboardShortcut.None);
                if (_capturing == rowAction)
                {
                    _capturing = null;
                }
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(!rowAction.HasCustomShortcut);
            if (ImGui.SmallButton("Reset"))
            {
                _shortcuts.ResetShortcut(rowAction);
            }
            ImGui.EndDisabled();
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private bool MatchesFilter(ShortcutAction action)
    {
        if (string.IsNullOrWhiteSpace(_filter))
        {
            return true;
        }

        return action.Name.Contains(_filter, System.StringComparison.OrdinalIgnoreCase)
               || action.Group.Contains(_filter, System.StringComparison.OrdinalIgnoreCase);
    }
}
