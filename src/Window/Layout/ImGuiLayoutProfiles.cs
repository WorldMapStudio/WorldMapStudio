using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class ImGuiLayoutProfiles
{
    private const string CurrentName = "current";
    private const double AutosaveIntervalSeconds = 2.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly WindowManager _windows;
    private readonly string _currentPath;
    private readonly string _profilesPath;
    private readonly Stopwatch _autosaveClock = Stopwatch.StartNew();
    private string _lastSavedJson = string.Empty;

    public ImGuiLayoutProfiles(WindowManager windows)
    {
        _windows = windows;

        string root = ProjectSettings.GlobalizePath("user://imgui-layouts");
        _profilesPath = Path.Combine(root, "profiles");
        _currentPath = Path.Combine(root, $"{CurrentName}.json");
        Directory.CreateDirectory(_profilesPath);
    }

    public string Folder => Path.GetDirectoryName(_currentPath) ?? _profilesPath;

    public IReadOnlyList<string> Profiles()
    {
        if (!Directory.Exists(_profilesPath))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(_profilesPath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool LoadCurrent(out string? error) => LoadPath(_currentPath, out error);

    public bool LoadProfile(string name, out string? error)
    {
        string? path = ProfilePath(name, mustAlreadyExist: true, out error);
        return path is not null && LoadPath(path, out error);
    }

    public bool SaveCurrent(out string? error)
    {
        string json = CaptureJson();
        if (!WriteJson(_currentPath, json, out error))
        {
            return false;
        }

        _lastSavedJson = json;
        return true;
    }

    public bool SaveProfile(string name, out string? error)
    {
        string? path = ProfilePath(name, mustAlreadyExist: false, out error);
        return path is not null && WriteJson(path, CaptureJson(), out error);
    }

    public void UpdateAutosave()
    {
        if (_autosaveClock.Elapsed.TotalSeconds < AutosaveIntervalSeconds)
        {
            return;
        }

        _autosaveClock.Restart();
        string json = CaptureJson();
        if (json == _lastSavedJson)
        {
            return;
        }

        if (WriteJson(_currentPath, json, out _))
        {
            _lastSavedJson = json;
        }
    }

    private bool LoadPath(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            LayoutProfileFile? file = JsonSerializer.Deserialize<LayoutProfileFile>(File.ReadAllText(path), JsonOptions);
            if (file is null)
            {
                error = "Layout profile is empty.";
                return false;
            }

            if (!string.IsNullOrEmpty(file.ImGuiIni))
            {
                ImGui.LoadIniSettingsFromMemory(file.ImGuiIni);
            }

            if (file.Windows is not null)
            {
                Dictionary<string, bool> openByTitle = file.Windows
                    .GroupBy(static window => window.Title, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => group.Last().IsOpen, StringComparer.Ordinal);

                foreach (Window window in _windows.Windows)
                {
                    if (openByTitle.TryGetValue(window.Title, out bool isOpen))
                    {
                        window.IsOpen = isOpen;
                    }
                }
            }

            _lastSavedJson = path == _currentPath ? CaptureJson() : _lastSavedJson;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return false;
        }
    }

    private string CaptureJson()
    {
        LayoutProfileFile file = new()
        {
            Version = 1,
            ImGuiIni = SaveIniToString(),
            Windows = _windows.Windows
                .Select(static window => new WindowOpenState { Title = window.Title, IsOpen = window.IsOpen })
                .OrderBy(static window => window.Title, StringComparer.Ordinal)
                .ToList(),
        };

        return JsonSerializer.Serialize(file, JsonOptions);
    }

    private static string SaveIniToString()
    {
        string data = ImGui.SaveIniSettingsToMemory(out uint size);
        return size == 0 ? string.Empty : data;
    }

    private static bool WriteJson(string path, string json, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private string? ProfilePath(string name, bool mustAlreadyExist, out string? error)
    {
        error = null;
        string safeName = SafeProfileName(name);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            error = "Profile name is required.";
            return null;
        }

        string path = Path.Combine(_profilesPath, $"{safeName}.json");
        if (mustAlreadyExist && !File.Exists(path))
        {
            error = $"Profile '{safeName}' does not exist.";
            return null;
        }

        return path;
    }

    private static string SafeProfileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string trimmed = name.Trim();
        return new string(trimmed.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private sealed class LayoutProfileFile
    {
        public int Version { get; set; }
        public string ImGuiIni { get; set; } = string.Empty;
        public List<WindowOpenState>? Windows { get; set; }
    }

    private sealed class WindowOpenState
    {
        public string Title { get; set; } = string.Empty;
        public bool IsOpen { get; set; }
    }
}
