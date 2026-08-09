namespace WorldMapStudio;

/// <summary>
/// The built-in starting point: a four-texture chunk, which is the budget most splatting formats
/// land on. Not a real export target — it exists so a map can have a landscape before anyone has
/// written a profile for the game they are shipping to.
/// </summary>
[Subsystem(nameof(LandscapeSystem))]
public sealed class GenericProfile : ILandscapeProfile
{
    public GenericProfile(LandscapeSystem landscape)
    {
    }

    public float Priority => -100.0f; // first in the list, as the default choice

    public string Name => "Generic (4 textures)";

    public string Description => "A neutral four-texture chunk. Start here, then switch to a profile for your target format.";

    public LandscapeSettings CreateSettings() => new()
    {
        ProfileName = Name,
        ChunkWorldSize = 64.0f,
        ChunkHeightResolution = 33,
        ChunkAlphaResolution = 64,
        HeightEncoding = HeightEncoding.Float32,
        AlphaBitDepth = 8,
        AlphaNormalized = false,
        ChunkLimit = 64,
        TextureLimit = 4,
    };
}
