using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Supplies and stores the landscape settings of a map, for one storage. Self-registers into a
/// concrete storage with [Subsystem(nameof(ThatStorage))], exactly like <see cref="IMapSource"/> —
/// a plugin whose maps come from a game's own tables keeps their landscape settings there too.
/// </summary>
public interface ILandscapeSettingsSource : ISubsystem
{
    /// <summary>Whether this source can be written to; false for a read-only game client.</summary>
    bool CanEdit { get; }

    /// <summary>The map's settings, or null if this source has none for it.</summary>
    Task<LandscapeSettings?> LoadAsync(MapId map);

    /// <summary>Writes the map's settings, inserting them if the map had none.</summary>
    Task SaveAsync(MapId map, LandscapeSettings settings);
}
