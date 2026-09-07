#nullable enable
using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Opens the editor straight into a project when one is named on the command line, skipping the main
/// menu and project picker. A checkout keeps a source-controlled project config and points its launch
/// task at it:
/// <code>godot . -- --project path/to/project.json</code>
/// The config is the same document <see cref="ProjectStore"/> writes; relative paths inside it resolve
/// against the config file's own directory. A config that fails to load stops startup at a
/// <see cref="StartupErrorScene"/> rather than falling through to the menu.
/// </summary>
public static class ProjectAutostart
{
    private const string Flag = "--project";

    /// <summary>The autostart scene, or null when no <c>--project</c> was passed and startup should
    /// proceed to the main menu as usual.</summary>
    public static IScene? Resolve(Node3D root)
    {
        string? configPath = ConfigPathFromArgs(OS.GetCmdlineUserArgs());
        if (configPath == null)
        {
            return null;
        }

        try
        {
            Project project = ProjectStore.Read(configPath);
            return LoadingScreen.OpenProject(root, project);
        }
        catch (Exception e)
        {
            GD.PushError($"[ProjectAutostart] Failed to open '{configPath}': {e}");
            return new StartupErrorScene($"Could not open project config '{configPath}'", e.Message);
        }
    }

    private static string? ConfigPathFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(Flag + "=", StringComparison.Ordinal))
            {
                return arg[(Flag.Length + 1)..];
            }

            if (arg == Flag && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
