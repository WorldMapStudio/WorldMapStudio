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

/// <summary>
/// App-lifetime owner of the active editor style — exists before any project is open (the main
/// menu, project-select, and the startup error screen all draw ImGui), so unlike most editor state
/// it cannot live on <see cref="EditorContext"/>. Discovers shipped (<c>res://styles</c>) and user
/// (<c>user://styles</c>) style files, resolves the active one through <see cref="StyleResolver"/>,
/// and re-applies it to the live <c>ImGuiStyle</c> whenever it changes.
/// </summary>
public static partial class EditorStyle
{
    private const string ShippedStylesResDir = "res://styles";
    private const double HotReloadIntervalSeconds = 1.0;

    private sealed class StyleEntry
    {
        public required StyleSource Source { get; init; }
        public required string Path { get; init; }
        public required StyleDocument Document { get; set; }
        public ulong LastWriteTicks { get; set; }
        public bool HasExternalConflict { get; set; }
    }

    private sealed class ActiveStyleFile
    {
        public int Version { get; set; } = 1;
        public string Active { get; set; } = "Dark";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Dictionary<string, StyleEntry> Entries = new(StringComparer.Ordinal);
    private static readonly Stopwatch HotReloadClock = Stopwatch.StartNew();

    private static bool _initialized;
    private static string _userStylesDir = string.Empty;
    private static string _activeFilePath = string.Empty;
    private static int _appliedGeneration = -1;
    private static string? _pendingConflictName;

    private static readonly List<StyleProblem> RuntimeProblems = [];

    public static ResolvedStyle Active { get; private set; } = null!;
    public static string ActiveName { get; private set; } = "Dark";
    public static int Generation { get; private set; }
    public static string UserStylesDirectory => _userStylesDir;

    /// <summary>Color/var/token resolution problems plus font load problems from the last atlas
    /// rebuild (see <see cref="ReportFontProblems"/>) — everything the Style Editor's Problems tab shows.</summary>
    public static IReadOnlyList<StyleProblem> Problems => RuntimeProblems.Count == 0
        ? Active.Problems
        : Active.Problems.Concat(RuntimeProblems).ToList();

    /// <summary>Called by <see cref="FontAtlasBuilder"/> after every rebuild with whatever font slots
    /// failed to load. Replaces the previous set — stale font problems from a since-fixed slot don't linger.</summary>
    internal static void ReportFontProblems(IReadOnlyList<StyleProblem> problems)
    {
        RuntimeProblems.Clear();
        RuntimeProblems.AddRange(problems);
    }

    /// <summary>The active style's own values as loaded, mutated live by the Style Editor window and
    /// re-resolved into <see cref="Active"/> on every change — the editor is its own preview.</summary>
    public static StyleDocument Working { get; private set; } = new();

    public static bool IsWorkingDirty { get; private set; }

    /// <summary>Set when the active style's file changed on disk while <see cref="Working"/> held
    /// unsaved edits. Cleared by discarding or overwriting the on-disk copy.</summary>
    public static bool HasExternalConflict => _pendingConflictName == ActiveName;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        _userStylesDir = ProjectSettings.GlobalizePath("user://styles");
        Directory.CreateDirectory(_userStylesDir);
        _activeFilePath = Path.Combine(_userStylesDir, "active.json");

        StyleTokenRegistry.EnsureDiscovered();
        LoadShipped();
        LoadUser();

        Activate(LoadActiveName() ?? "Dark", persist: false);
    }

    public static IReadOnlyList<StyleDescriptor> Available()
    {
        List<StyleDescriptor> list = [];
        foreach (string name in BuiltInStyles.Names)
        {
            list.Add(new StyleDescriptor { Name = name, Source = StyleSource.BuiltIn });
        }

        foreach (StyleEntry entry in Entries.Values.OrderBy(static e => e.Document.Name, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(new StyleDescriptor { Name = entry.Document.Name, Source = entry.Source, Extends = entry.Document.Extends });
        }

        return list;
    }

    public static void Activate(string name, bool persist = true)
    {
        StyleDocument document = ResolveDocument(name);
        ActiveName = document.Name;
        Working = document.Clone();
        IsWorkingDirty = false;
        if (_pendingConflictName == ActiveName)
        {
            _pendingConflictName = null;
        }

        RecomputeActive();

        if (persist)
        {
            SaveActiveName();
        }
    }

    /// <summary>Call after mutating <see cref="Working"/> directly (palette/vars/colors/tokens/fonts).
    /// Re-resolves immediately so every edit previews live.</summary>
    public static void NotifyWorkingChanged()
    {
        IsWorkingDirty = true;
        RecomputeActive();
    }

    /// <summary>Called by <see cref="GodotImGui"/> once per frame, before <c>ImGui.NewFrame()</c> —
    /// the only point it's legal to touch <c>ImGuiStyle</c> or rebuild the font atlas.
    /// <paramref name="fonts"/> diffs a font-only signature itself, so a color/var-only change here
    /// never triggers an atlas rebuild.</summary>
    public static void ApplyPending(ImGuiStylePtr style, ImGuiIOPtr io, FontAtlasBuilder fonts, GodotImGui owner)
    {
        CheckHotReload();
        fonts.RebuildIfNeeded(owner, io, Active);

        if (_appliedGeneration == Generation)
        {
            return;
        }

        Active.Apply(style);
        _appliedGeneration = Generation;
    }

    private static StyleDocument ResolveDocument(string name)
    {
        if (Entries.TryGetValue(name, out StyleEntry? entry))
        {
            return entry.Document;
        }

        bool builtIn = BuiltInStyles.IsBuiltIn(name);
        return new StyleDocument { Name = builtIn ? name : "Dark", Extends = builtIn ? null : "Dark" };
    }

    private static StyleDocument? LookupParentDocument(string name) =>
        Entries.TryGetValue(name, out StyleEntry? entry) ? entry.Document : null;

    private static void RecomputeActive()
    {
        Active = StyleResolver.Resolve(Working, LookupParentDocument, StyleTokenRegistry.ColorDefaults, StyleTokenRegistry.SizeDefaults);
        Generation++;
    }

    private static void CheckHotReload()
    {
        if (HotReloadClock.Elapsed.TotalSeconds < HotReloadIntervalSeconds)
        {
            return;
        }

        HotReloadClock.Restart();

        bool changed = false;
        foreach (StyleEntry entry in Entries.Values)
        {
            ulong currentTicks = entry.Source == StyleSource.Shipped
                ? Godot.FileAccess.GetModifiedTime(entry.Path)
                : ToUnixSeconds(SafeGetLastWriteTimeUtc(entry.Path));

            if (currentTicks == entry.LastWriteTicks)
            {
                continue;
            }

            entry.LastWriteTicks = currentTicks;

            bool isActiveEntry = entry.Document.Name == ActiveName;
            if (isActiveEntry && IsWorkingDirty)
            {
                _pendingConflictName = ActiveName;
                continue;
            }

            if (!ReloadEntry(entry))
            {
                continue;
            }

            if (isActiveEntry)
            {
                Working = entry.Document.Clone();
                IsWorkingDirty = false;
            }

            changed = true;
        }

        if (changed)
        {
            RecomputeActive();
        }
    }

    private static bool ReloadEntry(StyleEntry entry)
    {
        string? json = ReadFile(entry.Source, entry.Path);
        if (json is null)
        {
            return false;
        }

        StyleDocument document = StyleDocument.Parse(json);
        if (string.IsNullOrWhiteSpace(document.Name))
        {
            document.Name = entry.Document.Name;
        }

        entry.Document = document;
        entry.HasExternalConflict = false;
        return true;
    }

    private static void LoadShipped()
    {
        if (!Godot.DirAccess.DirExistsAbsolute(ShippedStylesResDir))
        {
            return;
        }

        foreach (string fileName in Godot.DirAccess.GetFilesAt(ShippedStylesResDir))
        {
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string resPath = $"{ShippedStylesResDir}/{fileName}";
            string? json = ReadFile(StyleSource.Shipped, resPath);
            if (json is null)
            {
                continue;
            }

            AddEntry(StyleSource.Shipped, resPath, fileName[..^".json".Length], json);
        }
    }

    private static void LoadUser()
    {
        if (!Directory.Exists(_userStylesDir))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(_userStylesDir, "*.json"))
        {
            if (Path.GetFileName(path).Equals("active.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? json = ReadFile(StyleSource.User, path);
            if (json is null)
            {
                continue;
            }

            AddEntry(StyleSource.User, path, Path.GetFileNameWithoutExtension(path), json);
        }
    }

    private static void AddEntry(StyleSource source, string path, string fallbackName, string json)
    {
        StyleDocument document = StyleDocument.Parse(json);
        if (string.IsNullOrWhiteSpace(document.Name))
        {
            document.Name = fallbackName;
        }

        ulong ticks = source == StyleSource.Shipped
            ? Godot.FileAccess.GetModifiedTime(path)
            : ToUnixSeconds(SafeGetLastWriteTimeUtc(path));

        Entries[document.Name] = new StyleEntry { Source = source, Path = path, Document = document, LastWriteTicks = ticks };
    }

    private static string? ReadFile(StyleSource source, string path)
    {
        if (source == StyleSource.Shipped)
        {
            using Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            return file?.GetAsText();
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTime SafeGetLastWriteTimeUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static ulong ToUnixSeconds(DateTime utc) => (ulong)Math.Max(0, ((DateTimeOffset)DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds());

    private static string? LoadActiveName()
    {
        if (!File.Exists(_activeFilePath))
        {
            return null;
        }

        try
        {
            ActiveStyleFile? file = JsonSerializer.Deserialize<ActiveStyleFile>(File.ReadAllText(_activeFilePath), JsonOptions);
            return file?.Active;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void SaveActiveName()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_activeFilePath)!);
            File.WriteAllText(_activeFilePath, JsonSerializer.Serialize(new ActiveStyleFile { Version = 1, Active = ActiveName }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
