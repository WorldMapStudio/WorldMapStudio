using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// A named project selected into the editor. Serialization (disk layout) is not modelled yet, so for
/// now a project is a display name plus its editing settings, held in memory by
/// <see cref="ProjectSelect"/>. The <see cref="AxisConvention"/> is the coordinate system the user
/// works in; the editor routes everything through it when talking to Godot. Each registered storage's
/// database location is part of the project too, added automatically the first time it is needed.
/// </summary>
public sealed class Project
{
    public required string Name { get; set; }

    public AxisConvention AxisConvention { get; set; } = AxisConvention.GodotDefault;

    /// <summary>Per-storage database locations, keyed by storage name.</summary>
    public Dictionary<string, StorageLocation> StorageLocations { get; init; } = new();

    /// <summary>Named directories, keyed by an opaque name owned by whoever reads it. Relative paths in a
    /// config file resolve against that file's directory.</summary>
    public Dictionary<string, string> Paths { get; init; } = new();

    /// <summary>Configured asset sources. Multiple entries may use the same source type.</summary>
    public List<AssetSourceSettings> AssetSources { get; init; } = [];

    /// <summary>Returns the stored location for a storage, adding <paramref name="defaults"/> if absent.</summary>
    public StorageLocation GetOrAddStorageLocation(string storageName, StorageLocation defaults)
    {
        if (!StorageLocations.TryGetValue(storageName, out StorageLocation? location))
        {
            location = defaults;
            StorageLocations[storageName] = location;
        }

        return location;
    }
}
