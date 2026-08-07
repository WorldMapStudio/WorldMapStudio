using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Lists the registered storages and lets the user edit how each reaches its dolt database: connect
/// to a running server or launch one, plus host/port/credentials. Edits are saved into the project's
/// settings. Actually connecting is stubbed until the migration flow lands.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class DatabaseWindow : Window
{
    private readonly DatabaseSystem _database;
    private readonly MigrationSystem _migrations;
    private readonly Project _project;

    public DatabaseWindow(WindowManager manager)
        : base("Database", startOpen: false, defaultSize: new Vector2(360, 420))
    {
        _database = manager.Context.Database;
        _migrations = manager.Context.Migrations;
        _project = manager.Context.Project;
    }

    protected override void DrawContent()
    {
        if (ImGui.Button("Check schema"))
        {
            _migrations.Check();
        }

        ImGui.Separator();

        bool changed = false;
        foreach (Storage storage in _database.Storages)
        {
            if (ImGui.CollapsingHeader(storage.Name, ImGuiTreeNodeFlags.DefaultOpen))
            {
                changed |= DrawConnection(storage);
            }
        }

        if (changed)
        {
            ProjectStore.Save(_project);
        }
    }

    private static bool DrawConnection(Storage storage)
    {
        StorageConnection connection = storage.Connection;
        ImGui.PushID(storage.Name);
        bool changed = false;

        bool launch = connection.LaunchServer;
        if (ImGui.Checkbox("Launch dolt sql-server", ref launch))
        {
            connection.LaunchServer = launch;
            changed = true;
        }

        if (connection.LaunchServer)
        {
            string repo = connection.RepositoryPath;
            if (ImGui.InputText("Repository", ref repo, 512))
            {
                connection.RepositoryPath = repo;
                changed = true;
            }
        }

        string host = connection.Host;
        if (ImGui.InputText("Host", ref host, 256))
        {
            connection.Host = host;
            changed = true;
        }

        int port = connection.Port;
        if (ImGui.InputInt("Port", ref port))
        {
            connection.Port = port;
            changed = true;
        }

        string database = connection.Database;
        if (ImGui.InputText("Database", ref database, 256))
        {
            connection.Database = database;
            changed = true;
        }

        string user = connection.User;
        if (ImGui.InputText("User", ref user, 256))
        {
            connection.User = user;
            changed = true;
        }

        string password = connection.Password;
        if (ImGui.InputText("Password", ref password, 256, ImGuiInputTextFlags.Password))
        {
            connection.Password = password;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.BeginDisabled();
        ImGui.Button("Connect");
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("(not implemented yet)");

        ImGui.PopID();
        return changed;
    }
}
