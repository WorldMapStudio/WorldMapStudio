using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Draws the editable fields of a <see cref="StorageConnection"/>: whether to launch a managed dolt
/// server or connect to one already running, plus host/port/database/credentials. Returns true on
/// the frames any field changed.
/// </summary>
public static class StorageConnectionEditor
{
    public static bool Draw(StorageConnection connection)
    {
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

        return changed;
    }
}
