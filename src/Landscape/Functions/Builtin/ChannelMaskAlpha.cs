using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// The simplest useful alpha function: a channel <em>is</em> the coverage, remapped through a
/// threshold and a softness. Chunk-local, so it needs no halo — an entity that wants its edges to
/// fade across a chunk border does that when it writes the channel.
/// </summary>
public sealed class ChannelMaskAlpha : ILandscapeAlphaFunction
{
    public static readonly LandscapeParameter Mask =
        LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read, "Channel supplying coverage.");

    public static readonly LandscapeParameter Threshold =
        LandscapeParameter.Float("threshold", "Threshold", 0.5f, 0.0f, 1.0f, "Mask value that becomes half coverage.");

    public static readonly LandscapeParameter Softness =
        LandscapeParameter.Float("softness", "Softness", 0.25f, 0.0f, 1.0f, "Width of the fade around the threshold. Zero is a hard edge.");

    public static readonly LandscapeParameter Invert =
        LandscapeParameter.Bool("invert", "Invert", false, "Cover where the mask is low instead of high.");

    public string Id => "builtin.alpha.channel_mask";

    public string DisplayName => "Channel Mask";

    public string Description => "Uses a channel directly as coverage, remapped through a threshold and softness.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } =
        LandscapeParameter.List(Mask, Threshold, Softness, Invert);
}
