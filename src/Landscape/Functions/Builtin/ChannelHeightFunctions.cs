using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Adds a channel to the accumulated height, scaled. The commutative case: order against other
/// additive functions does not matter, which is exactly why it is not the only height function.
/// </summary>
public sealed class ChannelHeightOffset : ILandscapeHeightFunction
{
    public static readonly LandscapeParameter Mask =
        LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read, "Channel scaling the offset.");

    public static readonly LandscapeParameter Amount =
        LandscapeParameter.Float("amount", "Amount", 1.0f, -1024.0f, 1024.0f, "World units added where the mask is fully set.");

    public string Id => "builtin.height.channel_offset";

    public string DisplayName => "Channel Offset";

    public string Description => "Adds a scaled channel to the accumulated height.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Mask, Amount);
}

/// <summary>
/// Pulls the accumulated height toward a target where a channel is set — a road bed or a building
/// pad. The reason height functions are not restricted to adding: run after a hill that raises, this
/// cuts a flat bed into it, whereas an additive function would only lift the hill further.
/// </summary>
public sealed class ChannelHeightFlatten : ILandscapeHeightFunction
{
    public static readonly LandscapeParameter Mask =
        LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read, "Channel deciding where flattening applies.");

    public static readonly LandscapeParameter Target =
        LandscapeParameter.Float("target", "Target height", 0.0f, -8192.0f, 8192.0f, "World height to flatten toward.");

    public static readonly LandscapeParameter Strength =
        LandscapeParameter.Float("strength", "Strength", 1.0f, 0.0f, 1.0f, "How far toward the target a fully set mask pulls.");

    public string Id => "builtin.height.channel_flatten";

    public string DisplayName => "Channel Flatten";

    public string Description => "Pulls accumulated height toward a target where a channel is set. Order-dependent by design.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Mask, Target, Strength);
}
