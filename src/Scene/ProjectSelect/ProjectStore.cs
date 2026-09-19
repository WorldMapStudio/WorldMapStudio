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
///
/// The same document shape is what <see cref="Read"/> loads from an arbitrary path, so a checkout can
/// keep a source-controlled project config and have the editor open straight into it (see
/// <see cref="ProjectAutostart"/>).
/// </summary>
public static class ProjectStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
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
                ProjectDocument? doc = JsonSerializer.Deserialize<ProjectDocument>(File.ReadAllText(file), Options);
                if (doc != null)
                {
                    projects.Add(FromDocument(doc));
                }
            }
            catch (Exception e)
            {
                GD.PushError($"[Project] Failed to load '{file}': {e.Message}");
            }
        }

        return projects;
    }

    /// <summary>
    /// Loads a project from an explicit file, resolving any relative <see cref="StorageConnection.RepositoryPath"/>
    /// <see cref="AssetSourceSettings.RootPath"/> and <see cref="Project.Paths"/> value against the file's own
    /// directory so the config travels with the checkout. Throws on a missing file or malformed document.
    /// </summary>
    public static Project Read(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Project config not found: {fullPath}");
        }

        ProjectDocument doc = JsonSerializer.Deserialize<ProjectDocument>(File.ReadAllText(fullPath), Options)
            ?? throw new InvalidDataException($"Project config is empty or null: {fullPath}");

        Project project = FromDocument(doc);
        ResolvePaths(project, Path.GetDirectoryName(fullPath)!);
        return project;
    }

    public static void Save(Project project)
    {
        try
        {
            string folder = ProjectFolder(project.Name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "project.json"), JsonSerializer.Serialize(ToDocument(project), Options));
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

    private static void ResolvePaths(Project project, string baseDir)
    {
        foreach (StorageConnection connection in project.StorageConnections.Values)
        {
            connection.RepositoryPath = Resolve(connection.RepositoryPath, baseDir);
        }

        foreach (AssetSourceSettings source in project.AssetSources)
        {
            source.RootPath = Resolve(source.RootPath, baseDir);
        }

        foreach (string key in project.Paths.Keys.ToList())
        {
            project.Paths[key] = Resolve(project.Paths[key], baseDir);
        }
    }

    private static string Resolve(string path, string baseDir) =>
        path.Length == 0 || Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));

    private static ProjectDocument ToDocument(Project project) => new()
    {
        Name = project.Name,
        AxisX = project.AxisConvention.X,
        AxisY = project.AxisConvention.Y,
        AxisZ = project.AxisConvention.Z,
        StorageConnections = new Dictionary<string, StorageConnection>(project.StorageConnections),
        AssetSources = project.AssetSources.Select(CloneAssetSource).ToList(),
        Paths = new Dictionary<string, string>(project.Paths),
    };

    private static Project FromDocument(ProjectDocument doc) => new()
    {
        Name = doc.Name,
        AxisConvention = AxisConvention.Create(doc.AxisX, doc.AxisY, doc.AxisZ),
        StorageConnections = new Dictionary<string, StorageConnection>(doc.StorageConnections),
        AssetSources = (doc.AssetSources ?? []).Select(CloneAssetSource).ToList(),
        Paths = new Dictionary<string, string>(doc.Paths ?? new Dictionary<string, string>()),
    };

    private static AssetSourceSettings CloneAssetSource(AssetSourceSettings source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Type = source.Type,
        Enabled = source.Enabled,
        RootPath = source.RootPath,
        Properties = new Dictionary<string, string>(source.Properties),
        ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(source.ExtensionData),
    };

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Length == 0 ? "project" : name;
    }

    private sealed class ProjectDocument
    {
        public string Name { get; set; } = string.Empty;
        public SignedAxis AxisX { get; set; } = SignedAxis.PosX;
        public SignedAxis AxisY { get; set; } = SignedAxis.PosY;
        public SignedAxis AxisZ { get; set; } = SignedAxis.PosZ;
        public Dictionary<string, StorageConnection> StorageConnections { get; set; } = new();
        public List<AssetSourceSettings> AssetSources { get; set; } = [];
        public Dictionary<string, string>? Paths { get; set; }
    }
}
