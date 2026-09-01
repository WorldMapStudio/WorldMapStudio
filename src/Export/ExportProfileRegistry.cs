using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Persists <see cref="ExportProfile"/>s as one JSON file per profile under the project's own folder,
/// mirroring <see cref="ImGuiLayoutProfiles"/>'s file-per-profile approach. Loaded once per
/// <see cref="ExportSystem"/> (i.e. once per project, since both are recreated on project switch).
/// </summary>
public sealed class ExportProfileRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _folder;
    private readonly List<ExportProfile> _profiles = new();

    public ExportProfileRegistry(EditorContext context)
    {
        _folder = Path.Combine(ProjectStore.ProjectFolder(context.Project.Name), "export-profiles");
        Load();
    }

    public IReadOnlyList<ExportProfile> Profiles => _profiles;

    public ExportProfile Create(string name, string exporterId, JsonObject defaultSettings)
    {
        var profile = new ExportProfile
        {
            Name = name,
            ExporterId = exporterId,
            Settings = defaultSettings,
        };

        _profiles.Add(profile);
        Persist(profile);
        return profile;
    }

    public void Rename(ExportProfile profile, string name)
    {
        profile.Name = name;
        Persist(profile);
    }

    public void SaveSettings(ExportProfile profile, JsonObject settings)
    {
        profile.Settings = settings;
        Persist(profile);
    }

    public void Delete(ExportProfile profile)
    {
        _profiles.Remove(profile);

        try
        {
            string path = PathFor(profile.Id);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"[Export] Failed to delete profile '{profile.Name}': {e.Message}");
        }
    }

    private void Load()
    {
        if (!Directory.Exists(_folder))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(_folder, "*.json"))
        {
            try
            {
                ExportProfile? profile = JsonSerializer.Deserialize<ExportProfile>(File.ReadAllText(file), JsonOptions);
                if (profile != null)
                {
                    _profiles.Add(profile);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                GD.PushError($"[Export] Failed to load export profile '{file}': {e.Message}");
            }
        }
    }

    private void Persist(ExportProfile profile)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(PathFor(profile.Id), JsonSerializer.Serialize(profile, JsonOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"[Export] Failed to save profile '{profile.Name}': {e.Message}");
        }
    }

    private string PathFor(string id) => Path.Combine(_folder, $"{id}.json");
}
