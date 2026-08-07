using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// A named project selected into the editor. Serialization (disk layout) is not modelled yet, so for
/// now a project is a display name plus its editing settings, held in memory by
/// <see cref="ProjectSelect"/>. The <see cref="AxisConvention"/> is the coordinate system the user
/// works in; the editor routes everything through it when talking to Godot. Each registered storage's
/// database connection is part of the project too, added automatically the first time it is needed.
/// </summary>
public sealed class Project
{
    public required string Name { get; set; }

    public AxisConvention AxisConvention { get; set; } = AxisConvention.GodotDefault;

    /// <summary>Per-storage database connection settings, keyed by storage name.</summary>
    public Dictionary<string, StorageConnection> StorageConnections { get; init; } = new();

    /// <summary>Returns the stored connection for a storage, adding <paramref name="defaults"/> if absent.</summary>
    public StorageConnection GetOrAddStorageConnection(string storageName, StorageConnection defaults)
    {
        if (!StorageConnections.TryGetValue(storageName, out StorageConnection? connection))
        {
            connection = defaults;
            StorageConnections[storageName] = connection;
        }

        return connection;
    }
}
