using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Persists projects as self-contained folders under %LocalAppData%\WorldMapStudio\projects\&lt;name&gt;\,
/// each with a project.json (name, axis convention, per-storage database connections) alongside its
/// managed dolt data. This is the "project settings" migrations and streaming rely on.
/// </summary>
public static class ProjectStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ProjectsRoot => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "WorldMapStudio", "projects");

    /// <summary>The folder holding a project's settings and data.</summary>
    public static string ProjectFolder(string projectName) => Path.Combine(ProjectsRoot, Sanitize(projectName));

    public static List<Project> LoadAll()
    {
        var projects = new List<Project>();
        if (!Directory.Exists(ProjectsRoot))
        {
            return projects;
        }

        foreach (string folder in Directory.GetDirectories(ProjectsRoot))
        {
            string file = Path.Combine(folder, "project.json");
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                ProjectDto? dto = JsonSerializer.Deserialize<ProjectDto>(File.ReadAllText(file), Options);
                if (dto != null)
                {
                    projects.Add(FromDto(dto));
                }
            }
            catch (Exception e)
            {
                GD.PushError($"[Project] Failed to load '{file}': {e.Message}");
            }
        }

        return projects;
    }

    public static void Save(Project project)
    {
        try
        {
            string folder = ProjectFolder(project.Name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "project.json"), JsonSerializer.Serialize(ToDto(project), Options));
        }
        catch (Exception e)
        {
            GD.PushError($"[Project] Failed to save '{project.Name}': {e.Message}");
        }
    }

    public static void Delete(Project project)
    {
        try
        {
            string folder = ProjectFolder(project.Name);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[Project] Failed to delete '{project.Name}': {e.Message}");
        }
    }

    private static ProjectDto ToDto(Project project) => new()
    {
        Name = project.Name,
        AxisX = project.AxisConvention.X,
        AxisY = project.AxisConvention.Y,
        AxisZ = project.AxisConvention.Z,
        StorageConnections = new Dictionary<string, StorageConnection>(project.StorageConnections),
        AssetSources = project.AssetSources.Select(CloneAssetSource).ToList(),
    };

    private static Project FromDto(ProjectDto dto) => new()
    {
        Name = dto.Name,
        AxisConvention = AxisConvention.Create(dto.AxisX, dto.AxisY, dto.AxisZ),
        StorageConnections = new Dictionary<string, StorageConnection>(dto.StorageConnections),
        AssetSources = (dto.AssetSources ?? []).Select(CloneAssetSource).ToList(),
    };

    private static AssetSourceSettings CloneAssetSource(AssetSourceSettings source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Type = source.Type,
        Enabled = source.Enabled,
        RootPath = source.RootPath,
    };

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Length == 0 ? "project" : name;
    }

    private sealed class ProjectDto
    {
        public string Name { get; set; } = string.Empty;
        public SignedAxis AxisX { get; set; } = SignedAxis.PosX;
        public SignedAxis AxisY { get; set; } = SignedAxis.PosY;
        public SignedAxis AxisZ { get; set; } = SignedAxis.PosZ;
        public Dictionary<string, StorageConnection> StorageConnections { get; set; } = new();
        public List<AssetSourceSettings> AssetSources { get; set; } = [];
    }
}
