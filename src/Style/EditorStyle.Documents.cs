using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// The editable-document half of <see cref="EditorStyle"/>: create/duplicate/rename/delete user
/// styles, and save/save-as/revert/undo the active one's <see cref="Working"/> document. Shared by
/// the Style Editor window and <c>wms.style</c> — neither duplicates this logic, they just call it.
/// </summary>
public static partial class EditorStyle
{
    public static bool IsActiveReadOnly => !Entries.TryGetValue(ActiveName, out StyleEntry? entry) || entry.Source != StyleSource.User;

    public static Dictionary<string, StyleColorValue> WorkingDict(StyleValueSpace space) => space switch
    {
        StyleValueSpace.Palette => Working.Palette,
        StyleValueSpace.ImGuiColor => Working.Colors,
        StyleValueSpace.Token => Working.Tokens,
        _ => throw new ArgumentOutOfRangeException(nameof(space)),
    };

    public static IReadOnlyDictionary<string, System.Numerics.Vector4> ResolvedDict(StyleValueSpace space) => space switch
    {
        StyleValueSpace.Palette => Active.Palette,
        StyleValueSpace.ImGuiColor => Active.Colors.ToDictionary(static p => p.Key.ToString(), static p => p.Value, StringComparer.Ordinal),
        StyleValueSpace.Token => Active.Tokens,
        _ => throw new ArgumentOutOfRangeException(nameof(space)),
    };

    /// <summary>Replaces <see cref="Working"/> wholesale (used by the Style Editor's local undo stack)
    /// and marks it dirty — the caller is restoring a snapshot taken mid-edit, not a saved state.</summary>
    public static void ReplaceWorking(StyleDocument document)
    {
        Working = document;
        IsWorkingDirty = true;
        RecomputeActive();
    }

    /// <summary>Discards unsaved edits, reloading <see cref="Working"/> from the active style's
    /// last-saved (or shipped/built-in) state.</summary>
    public static void Revert()
    {
        Working = ResolveDocument(ActiveName).Clone();
        IsWorkingDirty = false;
        if (_pendingConflictName == ActiveName)
        {
            _pendingConflictName = null;
        }

        RecomputeActive();
    }

    public static bool Save(out string? error)
    {
        error = null;
        if (IsActiveReadOnly)
        {
            error = "This style is read-only; use Save As to create an editable copy.";
            return false;
        }

        StyleEntry entry = Entries[ActiveName];
        if (!TryWriteDocument(entry.Path, Working, out error))
        {
            return false;
        }

        entry.Document = Working.Clone();
        entry.LastWriteTicks = ToUnixSeconds(SafeGetLastWriteTimeUtc(entry.Path));
        entry.HasExternalConflict = false;
        IsWorkingDirty = false;
        if (_pendingConflictName == ActiveName)
        {
            _pendingConflictName = null;
        }

        return true;
    }

    public static bool SaveAs(string newName, out string? error)
    {
        error = null;
        string safeName = SafeFileName(newName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            error = "Name is required.";
            return false;
        }

        if (BuiltInStyles.IsBuiltIn(newName) || Entries.ContainsKey(newName))
        {
            error = $"A style named '{newName}' already exists.";
            return false;
        }

        StyleDocument copy = Working.Clone();
        copy.Name = newName;
        copy.Extends ??= "Dark";

        string path = Path.Combine(_userStylesDir, safeName + ".json");
        if (!TryWriteDocument(path, copy, out error))
        {
            return false;
        }

        Entries[newName] = new StyleEntry { Source = StyleSource.User, Path = path, Document = copy.Clone(), LastWriteTicks = ToUnixSeconds(SafeGetLastWriteTimeUtc(path)) };
        Activate(newName);
        return true;
    }

    /// <summary>Writes a new, empty (all-inherited) user style extending <paramref name="extends"/>
    /// and activates it.</summary>
    public static bool Create(string name, string extends, out string? error)
    {
        error = null;
        string safeName = SafeFileName(name);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            error = "Name is required.";
            return false;
        }

        if (BuiltInStyles.IsBuiltIn(name) || Entries.ContainsKey(name))
        {
            error = $"A style named '{name}' already exists.";
            return false;
        }

        StyleDocument document = new() { Name = name, Extends = string.IsNullOrWhiteSpace(extends) ? "Dark" : extends };
        string path = Path.Combine(_userStylesDir, safeName + ".json");
        if (!TryWriteDocument(path, document, out error))
        {
            return false;
        }

        Entries[name] = new StyleEntry { Source = StyleSource.User, Path = path, Document = document, LastWriteTicks = ToUnixSeconds(SafeGetLastWriteTimeUtc(path)) };
        Activate(name);
        return true;
    }

    public static bool Duplicate(string name, string newName, out string? error)
    {
        error = null;
        if (!Entries.ContainsKey(name) && !BuiltInStyles.IsBuiltIn(name))
        {
            error = $"Style '{name}' not found.";
            return false;
        }

        string safeName = SafeFileName(newName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            error = "Name is required.";
            return false;
        }

        if (BuiltInStyles.IsBuiltIn(newName) || Entries.ContainsKey(newName))
        {
            error = $"A style named '{newName}' already exists.";
            return false;
        }

        StyleDocument copy = ResolveDocument(name).Clone();
        copy.Name = newName;
        if (string.IsNullOrWhiteSpace(copy.Extends) && BuiltInStyles.IsBuiltIn(name))
        {
            // Duplicating a built-in root: the copy extends it rather than becoming a second root.
            copy.Extends = name;
        }

        string path = Path.Combine(_userStylesDir, safeName + ".json");
        if (!TryWriteDocument(path, copy, out error))
        {
            return false;
        }

        Entries[newName] = new StyleEntry { Source = StyleSource.User, Path = path, Document = copy.Clone(), LastWriteTicks = ToUnixSeconds(SafeGetLastWriteTimeUtc(path)) };
        return true;
    }

    public static bool Rename(string name, string newName, out string? error)
    {
        error = null;
        if (!Entries.TryGetValue(name, out StyleEntry? entry) || entry.Source != StyleSource.User)
        {
            error = "Only user styles can be renamed.";
            return false;
        }

        string safeName = SafeFileName(newName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            error = "Name is required.";
            return false;
        }

        string newPath = Path.Combine(_userStylesDir, safeName + ".json");
        if (!newPath.Equals(entry.Path, StringComparison.OrdinalIgnoreCase) && (File.Exists(newPath) || Entries.ContainsKey(newName)))
        {
            error = $"A style named '{newName}' already exists.";
            return false;
        }

        entry.Document.Name = newName;
        if (!TryWriteDocument(newPath, entry.Document, out error))
        {
            return false;
        }

        if (!newPath.Equals(entry.Path, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteFile(entry.Path);
        }

        Entries.Remove(name);
        Entries[newName] = new StyleEntry { Source = StyleSource.User, Path = newPath, Document = entry.Document, LastWriteTicks = ToUnixSeconds(SafeGetLastWriteTimeUtc(newPath)) };

        // Anything else that extended the old name now points at a name that no longer resolves.
        foreach (StyleEntry other in Entries.Values)
        {
            if (other.Document.Extends == name)
            {
                other.Document.Extends = newName;
            }
        }

        if (ActiveName == name)
        {
            Activate(newName);
        }

        return true;
    }

    public static bool Delete(string name, out string? error)
    {
        error = null;
        if (!Entries.TryGetValue(name, out StyleEntry? entry) || entry.Source != StyleSource.User)
        {
            error = "Only user styles can be deleted.";
            return false;
        }

        TryDeleteFile(entry.Path);
        Entries.Remove(name);

        foreach (StyleEntry other in Entries.Values)
        {
            if (other.Document.Extends == name)
            {
                other.Document.Extends = "Dark";
            }
        }

        if (ActiveName == name)
        {
            Activate("Dark");
        }
        else
        {
            RecomputeActive();
        }

        return true;
    }

    private static bool TryWriteDocument(string path, StyleDocument document, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, document.ToJson());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string SafeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string trimmed = name.Trim();
        return new string(trimmed.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
