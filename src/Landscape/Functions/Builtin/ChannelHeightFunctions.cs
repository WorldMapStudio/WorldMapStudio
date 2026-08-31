using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Adds a channel to the accumulated height, scaled. The commutative case: order against other
/// additive functions does not matter, which is exactly why it is not the only height function.
/// </summary>
public sealed class ChannelHeightOffset : ILandscapeHeightFunction
{
    public static readonly LandscapeParameter Mask =
        LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read, "Channel scaling the offset.", LandscapeChannelFormat.Scalar);

    public static readonly LandscapeParameter Amount =
        LandscapeParameter.Float("amount", "Amount", 1.0f, -1024.0f, 1024.0f, "World units added where the mask is fully set.");

    public string Id => "builtin.height.channel_offset";

    public string DisplayName => "Channel Offset";

    public string Description => "Adds a scaled channel to the accumulated height.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Mask, Amount);

    public void Evaluate(in LandscapeEvalContext context, float[] heights)
    {
        float amount = context.Float(Amount);
        int resolution = context.Resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector3 world = context.WorldAt(x, y, vertices: true);
                heights[(y * resolution) + x] += context.SampleChannel(Mask, world) * amount;
            }
        }
    }
}

/// <summary>
/// Pulls the accumulated height toward a target where a channel is set — a road bed or a building
/// pad. The reason height functions are not restricted to adding: run after a hill that raises, this
/// cuts a flat bed into it, whereas an additive function would only lift the hill further.
/// </summary>
public sealed class ChannelHeightFlatten : ILandscapeHeightFunction
{
    public static readonly LandscapeParameter Mask =
        LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read, "Channel deciding where flattening applies.", LandscapeChannelFormat.Scalar);

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

    public void Evaluate(in LandscapeEvalContext context, float[] heights)
    {
        float target = context.Float(Target);
        float strength = Mathf.Clamp(context.Float(Strength), 0.0f, 1.0f);
        int resolution = context.Resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = (y * resolution) + x;
                Vector3 world = context.WorldAt(x, y, vertices: true);
                float mask = context.SampleChannel(Mask, world);

                // Reads what earlier layers built and pulls it toward the target. Running this after
                // a raise gives a flat bed cut into the hill; running it before would let the hill
                // bump straight back through.
                heights[index] = Mathf.Lerp(heights[index], target, mask * strength);
            }
        }
    }
}
