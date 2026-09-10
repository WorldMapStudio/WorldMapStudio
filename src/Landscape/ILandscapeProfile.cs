using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Supplies the landscape settings of a known export target, so a user picks "the format I am
/// shipping to" instead of hand-entering resolutions and limits they can get wrong.
///
/// The editor defines no export format itself — a plugin for a specific game registers its profile
/// with [Subsystem(nameof(LandscapeSystem))] and its numbers become selectable when a map's landscape
/// is set up.
/// </summary>
public interface ILandscapeProfile : ISubsystem
{
    /// <summary>Name shown when picking a profile.</summary>
    string Name { get; }

    /// <summary>One line on what this profile targets.</summary>
    string Description { get; }

    /// <summary>A fresh settings object for this target. Callers own and may edit the result.</summary>
    LandscapeSettings CreateSettings();

    /// <summary>Terrain attributes this target needs — created for the map when its landscape is set
    /// up with this profile. Defaulted to empty, so a profile that has no custom per-chunk output
    /// declares nothing.</summary>
    IReadOnlyList<TerrainAttributeSeed> SeedAttributes() => [];
}
