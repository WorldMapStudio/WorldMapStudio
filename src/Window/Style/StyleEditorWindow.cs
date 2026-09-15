using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Edits <see cref="EditorStyle.Working"/> live — every widget here writes straight into it and calls
/// <see cref="EditorStyle.NotifyWorkingChanged"/>, so the editor re-themes itself as the preview.
/// Editor-scene-only (no main-menu entry outside a loaded project); the active style still applies
/// everywhere, including the main menu and project-select screens.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed partial class StyleEditorWindow : Window
{
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.S, ShortcutModifiers.Alt);

    private const int MaxUndo = 50;

    private readonly List<string> _undoStack = [];
    private readonly ModalConfirm _deleteConfirm = new("Delete Style", "Delete this style? This cannot be undone.", "Delete");
    private readonly ModalConfirm _unsavedConfirm = new("Unsaved Changes", "This style has unsaved changes. Discard them?", "Discard");

    private string? _pendingSwitchTo;
    private string? _statusMessage;
    private bool _statusIsError;

    // Name-prompt popup (New / Duplicate / Rename / Save As all share this).
    private string _promptTitle = string.Empty;
    private string _promptText = string.Empty;
    private string _promptExtends = "Dark";
    private bool _promptShowExtends;
    private Action<string>? _promptConfirm;
    private bool _promptOpenRequested;

    // Row-flash: hovering a swatch briefly overrides that value to magenta in the live style.
    private StyleValueSpace _flashSpace;
    private string? _flashId;
    private StyleColorValue? _flashOriginal;
    private bool _flashHadOverride;
    private readonly Stopwatch _flashClock = new();
    private static readonly Vector4 FlashColor = new(1f, 0f, 1f, 1f);
    private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(500);

    public StyleEditorWindow(WindowManager manager)
        : base("Style Editor", startOpen: false, defaultSize: new Vector2(780f, 640f))
    {
    }

    protected override void OnBeforeDraw()
    {
        if (_flashId is not null && _flashClock.Elapsed > FlashDuration)
        {
            EndRowFlash();
        }
    }

    protected override void DrawContent()
    {
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Z, false))
        {
            Undo();
        }

        DrawHeader();
        ImGui.Separator();

        if (ImGui.BeginTabBar("StyleEditorTabs"))
        {
            DrawTab("Palette", DrawPaletteTab);
            DrawTab("Interface", DrawInterfaceTab);
            DrawTab("Editor Colors", DrawTokensTab);
            DrawTab("Sizes", DrawSizesTab);
            DrawTab("Fonts", DrawFontsTab);
            DrawTab(EditorStyle.Problems.Count > 0 ? $"Problems ({EditorStyle.Problems.Count})" : "Problems", DrawProblemsTab);
            DrawTab("Preview", DrawPreviewTab);
            ImGui.EndTabBar();
        }

        DrawNamePromptPopup();
        DrawConfirmModals();

        if (_statusMessage is not null)
        {
            ImGui.Separator();
            if (_statusIsError)
            {
                ImGuiEx.TextColored(CommonColors.Error, _statusMessage);
            }
            else
            {
                ImGui.TextDisabled(_statusMessage);
            }
        }
    }

    private static void DrawTab(string label, Action draw)
    {
        if (!ImGui.BeginTabItem(label))
        {
            return;
        }

        ImGui.Spacing();
        draw();
        ImGui.EndTabItem();
    }

    // ---- Header --------------------------------------------------------------------------------

    private void DrawHeader()
    {
        IReadOnlyList<StyleDescriptor> available = EditorStyle.Available();
        StyleDescriptor? active = available.FirstOrDefault(d => d.Name == EditorStyle.ActiveName);

        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("##style-picker", EditorStyle.ActiveName))
        {
            foreach (StyleDescriptor descriptor in available)
            {
                bool isSelected = descriptor.Name == EditorStyle.ActiveName;
                string label = descriptor.Source switch
                {
                    StyleSource.BuiltIn => $"{descriptor.Name} (built-in)",
                    StyleSource.Shipped => $"{descriptor.Name} (shipped)",
                    _ => descriptor.Name,
                };

                if (ImGui.Selectable(label, isSelected))
                {
                    RequestSwitch(descriptor.Name);
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("New..."))
        {
            OpenPrompt("New Style", string.Empty, showExtends: true, EditorStyle.ActiveName, name =>
            {
                if (!EditorStyle.Create(name, _promptExtends, out string? error))
                {
                    SetStatus(error!, true);
                }
            });
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Duplicate..."))
        {
            OpenPrompt("Duplicate Style", $"{EditorStyle.ActiveName} Copy", showExtends: false, string.Empty, name =>
            {
                if (!EditorStyle.Duplicate(EditorStyle.ActiveName, name, out string? error))
                {
                    SetStatus(error!, true);
                }
            });
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(active is not { Source: StyleSource.User });
        if (ImGui.SmallButton("Rename..."))
        {
            OpenPrompt("Rename Style", EditorStyle.ActiveName, showExtends: false, string.Empty, name =>
            {
                if (!EditorStyle.Rename(EditorStyle.ActiveName, name, out string? error))
                {
                    SetStatus(error!, true);
                }
            });
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Delete"))
        {
            _deleteConfirm.Show();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.SmallButton("Open Folder"))
        {
            Godot.OS.ShellOpen(EditorStyle.UserStylesDirectory);
        }

        DrawSaveControls(active);
        DrawConflictBanner();
    }

    private void DrawSaveControls(StyleDescriptor? active)
    {
        bool readOnly = EditorStyle.IsActiveReadOnly;

        if (EditorStyle.IsWorkingDirty)
        {
            ImGui.SameLine();
            ImGuiEx.TextColored(CommonColors.Warning, "* unsaved");
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(readOnly || !EditorStyle.IsWorkingDirty);
        if (ImGui.SmallButton("Save"))
        {
            if (!EditorStyle.Save(out string? error))
            {
                SetStatus(error!, true);
            }
            else
            {
                SetStatus("Saved.", false);
            }
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.SmallButton("Save As..."))
        {
            OpenPrompt("Save Style As", readOnly ? $"{EditorStyle.ActiveName} Copy" : EditorStyle.ActiveName, showExtends: false, string.Empty, name =>
            {
                if (!EditorStyle.SaveAs(name, out string? error))
                {
                    SetStatus(error!, true);
                }
            });
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!EditorStyle.IsWorkingDirty);
        if (ImGui.SmallButton("Revert"))
        {
            EditorStyle.Revert();
            _undoStack.Clear();
        }
        ImGui.EndDisabled();

        if (readOnly)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(active?.Source == StyleSource.Shipped ? "(shipped, read-only)" : "(built-in, read-only)");
        }
    }

    private void DrawConflictBanner()
    {
        if (!EditorStyle.HasExternalConflict)
        {
            return;
        }

        ImGuiEx.TextColored(CommonColors.Warning, "This style's file changed on disk while you had unsaved edits.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Keep Mine"))
        {
            _ = EditorStyle.Save(out _);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Load Disk Version"))
        {
            EditorStyle.Revert();
            _undoStack.Clear();
        }
    }

    private void RequestSwitch(string name)
    {
        if (name == EditorStyle.ActiveName)
        {
            return;
        }

        if (EditorStyle.IsWorkingDirty)
        {
            _pendingSwitchTo = name;
            _unsavedConfirm.Show();
            return;
        }

        EditorStyle.Activate(name);
        _undoStack.Clear();
    }

    private void DrawConfirmModals()
    {
        if (_unsavedConfirm.Draw(canBeClosed: true) == ModalOperationState.Confirmed && _pendingSwitchTo is { } target)
        {
            EditorStyle.Activate(target);
            _undoStack.Clear();
            _pendingSwitchTo = null;
        }

        if (_deleteConfirm.Draw(canBeClosed: true) == ModalOperationState.Confirmed)
        {
            if (!EditorStyle.Delete(EditorStyle.ActiveName, out string? error))
            {
                SetStatus(error!, true);
            }

            _undoStack.Clear();
        }
    }

    // ---- Name prompt (New / Duplicate / Rename / Save As) --------------------------------------

    private void OpenPrompt(string title, string initialText, bool showExtends, string extendsSeed, Action<string> onConfirm)
    {
        _promptTitle = title;
        _promptText = initialText;
        _promptShowExtends = showExtends;
        _promptExtends = string.IsNullOrEmpty(extendsSeed) ? "Dark" : extendsSeed;
        _promptConfirm = onConfirm;
        _promptOpenRequested = true;
    }

    private void DrawNamePromptPopup()
    {
        if (_promptConfirm is null)
        {
            return;
        }

        const string popupId = "##StyleNamePrompt";
        if (_promptOpenRequested)
        {
            ImGui.OpenPopup(popupId);
            _promptOpenRequested = false;
        }

        ImGui.SetNextWindowSize(new Vector2(360f, 0f), ImGuiCond.Always);
        bool isOpen = true;
        if (ImGui.BeginPopupModal(popupId, ref isOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
        {
            ImGui.TextUnformatted(_promptTitle);
            ImGui.Separator();

            ImGui.SetNextItemWidth(-1f);
            bool submitted = ImGui.InputTextWithHint("##name", "Style name", ref _promptText, 128, ImGuiInputTextFlags.EnterReturnsTrue);

            if (_promptShowExtends)
            {
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.BeginCombo("##extends", _promptExtends))
                {
                    foreach (StyleDescriptor descriptor in EditorStyle.Available())
                    {
                        if (ImGui.Selectable(descriptor.Name, descriptor.Name == _promptExtends))
                        {
                            _promptExtends = descriptor.Name;
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            ImGui.Spacing();
            bool confirmClicked = ImGui.Button("OK");
            ImGui.SameLine();
            bool cancelClicked = ImGui.Button("Cancel");

            if ((submitted || confirmClicked) && !string.IsNullOrWhiteSpace(_promptText))
            {
                _promptConfirm(_promptText.Trim());
                _promptConfirm = null;
                ImGui.CloseCurrentPopup();
            }
            else if (cancelClicked || !isOpen)
            {
                _promptConfirm = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
        else
        {
            _promptConfirm = null;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        _statusMessage = message;
        _statusIsError = isError;
    }

    // ---- Shared editing plumbing (used by the Colors/Sizes/Fonts tabs) -------------------------

    private static Dictionary<string, StyleColorValue> WorkingColors(StyleValueSpace space) => EditorStyle.WorkingDict(space);

    private void PushUndo()
    {
        _undoStack.Add(EditorStyle.Working.ToJson());
        if (_undoStack.Count > MaxUndo)
        {
            _undoStack.RemoveAt(0);
        }
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        string snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        EditorStyle.ReplaceWorking(StyleDocument.Parse(snapshot));
    }

    private void SetColorOverride(StyleValueSpace space, string id, StyleColorValue value)
    {
        WorkingColors(space)[id] = value;
        EditorStyle.NotifyWorkingChanged();
    }

    private void RemoveColorOverride(StyleValueSpace space, string id)
    {
        if (!WorkingColors(space).ContainsKey(id))
        {
            return;
        }

        PushUndo();
        WorkingColors(space).Remove(id);
        EditorStyle.NotifyWorkingChanged();
    }

    private static Vector4 ResolvedColor(StyleValueSpace space, string id) => space switch
    {
        StyleValueSpace.Palette => EditorStyle.Active.Palette.TryGetValue(id, out Vector4 v) ? v : FlashColor,
        StyleValueSpace.ImGuiColor => Enum.TryParse(id, out ImGuiCol col) && EditorStyle.Active.Colors.TryGetValue(col, out Vector4 c) ? c : FlashColor,
        StyleValueSpace.Token => EditorStyle.Active.Tokens.TryGetValue(id, out Vector4 t) ? t : FlashColor,
        _ => FlashColor,
    };

    private void BeginRowFlash(StyleValueSpace space, string id)
    {
        if (_flashId == id && _flashSpace == space)
        {
            _flashClock.Restart();
            return;
        }

        if (_flashId is not null)
        {
            EndRowFlash();
        }

        Dictionary<string, StyleColorValue> dict = WorkingColors(space);
        _flashHadOverride = dict.TryGetValue(id, out StyleColorValue? original);
        _flashOriginal = original;
        _flashSpace = space;
        _flashId = id;
        _flashClock.Restart();

        dict[id] = new StyleColorLiteral(FlashColor);
        EditorStyle.NotifyWorkingChanged();
    }

    private void EndRowFlash()
    {
        if (_flashId is not { } id)
        {
            return;
        }

        Dictionary<string, StyleColorValue> dict = WorkingColors(_flashSpace);
        if (_flashHadOverride && _flashOriginal is not null)
        {
            dict[id] = _flashOriginal;
        }
        else
        {
            dict.Remove(id);
        }

        _flashId = null;
        _flashOriginal = null;
        EditorStyle.NotifyWorkingChanged();
    }
}
