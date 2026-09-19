using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class ShortcutSystem : IFrameParticipant
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly ImGuiKey[] BindableKeys =
    [
        ImGuiKey.A, ImGuiKey.B, ImGuiKey.C, ImGuiKey.D, ImGuiKey.E, ImGuiKey.F, ImGuiKey.G,
        ImGuiKey.H, ImGuiKey.I, ImGuiKey.J, ImGuiKey.K, ImGuiKey.L, ImGuiKey.M, ImGuiKey.N,
        ImGuiKey.O, ImGuiKey.P, ImGuiKey.Q, ImGuiKey.R, ImGuiKey.S, ImGuiKey.T, ImGuiKey.U,
        ImGuiKey.V, ImGuiKey.W, ImGuiKey.X, ImGuiKey.Y, ImGuiKey.Z,
        ImGuiKey._0, ImGuiKey._1, ImGuiKey._2, ImGuiKey._3, ImGuiKey._4,
        ImGuiKey._5, ImGuiKey._6, ImGuiKey._7, ImGuiKey._8, ImGuiKey._9,
        ImGuiKey.F1, ImGuiKey.F2, ImGuiKey.F3, ImGuiKey.F4, ImGuiKey.F5, ImGuiKey.F6,
        ImGuiKey.F7, ImGuiKey.F8, ImGuiKey.F9, ImGuiKey.F10, ImGuiKey.F11, ImGuiKey.F12,
        ImGuiKey.Delete, ImGuiKey.Backspace, ImGuiKey.Enter, ImGuiKey.Escape, ImGuiKey.Space,
        ImGuiKey.Tab, ImGuiKey.LeftArrow, ImGuiKey.RightArrow, ImGuiKey.UpArrow, ImGuiKey.DownArrow,
        ImGuiKey.PageUp, ImGuiKey.PageDown,
    ];

    private readonly Dictionary<string, ShortcutAction> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KeyboardShortcut> _saved = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly string _legacyPath;

    public IEnumerable<ShortcutAction> Actions => _actions.Values
        .OrderBy(static action => action.Group, StringComparer.Ordinal)
        .ThenBy(static action => action.Name, StringComparer.Ordinal);

    public bool Suspended { get; set; }

    public ShortcutSystem()
    {
        string root = ProjectSettings.GlobalizePath("user://imgui-layouts");
        _path = Path.Combine(root, "keybindings.json");
        _legacyPath = Path.Combine(ProjectSettings.GlobalizePath("user://keybindings"), "keybindings.json");
        Load();
    }

    public ShortcutAction Register(
        string id,
        string group,
        string name,
        KeyboardShortcut defaultShortcut,
        Action execute,
        Func<bool>? enabled = null)
    {
        if (_actions.TryGetValue(id, out ShortcutAction? existing))
        {
            return existing;
        }

        ShortcutAction action = new(id, group, name, defaultShortcut, execute, enabled);
        if (_saved.TryGetValue(id, out KeyboardShortcut saved))
        {
            action.Shortcut = saved;
        }

        _actions.Add(id, action);
        return action;
    }

    public string Label(string id) => _actions.TryGetValue(id, out ShortcutAction? action)
        ? action.ShortcutLabel
        : string.Empty;

    public float TickPriority => 5f;

    public void Update()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (Suspended || io.WantTextInput)
        {
            return;
        }

        foreach (ShortcutAction action in Actions)
        {
            if (action.IsEnabled && action.Shortcut.IsPressed(io))
            {
                action.Execute();
                return;
            }
        }
    }

    public void SetShortcut(ShortcutAction action, KeyboardShortcut shortcut)
    {
        action.Shortcut = shortcut;
        _saved[action.Id] = shortcut;
        Save();
    }

    public void ResetShortcut(ShortcutAction action)
    {
        action.Shortcut = action.DefaultShortcut;
        _saved.Remove(action.Id);
        Save();
    }

    public IReadOnlyList<ShortcutAction> ConflictsFor(ShortcutAction action) => Actions
        .Where(other => !ReferenceEquals(other, action) && other.Shortcut.ConflictsWith(action.Shortcut))
        .ToArray();

    public static bool TryReadPressedShortcut(out KeyboardShortcut shortcut)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        ShortcutModifiers modifiers = KeyboardShortcut.CurrentModifiers(io);
        foreach (ImGuiKey key in BindableKeys)
        {
            if (ImGui.IsKeyPressed(key, false))
            {
                shortcut = new KeyboardShortcut(key, modifiers);
                return true;
            }
        }

        shortcut = KeyboardShortcut.None;
        return false;
    }

    private void Load()
    {
        string path = File.Exists(_path) ? _path : _legacyPath;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            ShortcutFile? file = JsonSerializer.Deserialize<ShortcutFile>(File.ReadAllText(path), JsonOptions);
            if (file?.Bindings is null)
            {
                return;
            }

            foreach (ShortcutRecord record in file.Bindings)
            {
                if (!string.IsNullOrWhiteSpace(record.Id)
                    && KeyboardShortcut.TryParse(record.Shortcut, out KeyboardShortcut shortcut))
                {
                    _saved[record.Id] = shortcut;
                }
            }

            if (path == _legacyPath)
            {
                Save();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    private void Save()
    {
        ShortcutFile file = new()
        {
            Version = 1,
            Bindings = _saved
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new ShortcutRecord { Id = pair.Key, Shortcut = pair.Value.DisplayName })
                .ToList(),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
    }

    private sealed class ShortcutFile
    {
        public int Version { get; set; }
        public List<ShortcutRecord>? Bindings { get; set; }
    }

    private sealed class ShortcutRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Shortcut { get; set; } = string.Empty;
    }
}
