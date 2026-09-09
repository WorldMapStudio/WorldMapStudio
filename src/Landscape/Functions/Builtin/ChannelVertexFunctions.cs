using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Pulls the accumulated vertex color toward a target where a channel is set — the vertex-color
/// equivalent of <see cref="ChannelHeightFlatten"/>. Starts from white each build, so a chunk nothing
/// binds this on renders with its base texture unchanged.
/// </summary>
public sealed class ChannelVertexColorTint : ILandscapeVertexColorFunction
{
    public static readonly LandscapeParameter Mask =
        LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read, "Channel deciding where the tint applies.", LandscapeChannelFormat.Scalar);

    public static readonly LandscapeParameter Tint =
        LandscapeParameter.Color("color", "Color", Colors.White, "Color to tint toward.");

    public static readonly LandscapeParameter Strength =
        LandscapeParameter.Float("strength", "Strength", 1.0f, 0.0f, 1.0f, "How far toward the color a fully set mask pulls.");

    public string Id => "builtin.vertex_color.channel_tint";

    public string DisplayName => "Channel Vertex Color Tint";

    public string Description => "Pulls accumulated vertex color toward a target where a channel is set.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Mask, Tint, Strength);

    public void Evaluate(in LandscapeEvalContext context, Color[] colors)
    {
        Color target = context.Color(Tint);
        float strength = Mathf.Clamp(context.Float(Strength), 0.0f, 1.0f);
        int resolution = context.Resolution;
        LandscapeChannelBinding binding = context.ChannelBinding(Mask);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = (y * resolution) + x;
                Vector3 world = context.WorldAt(x, y, vertices: true);
                float mask = context.SampleChannel(binding, world);

                // Reads what earlier layers built and pulls it toward the target, the same
                // accumulate-and-transform shape as height's flatten.
                colors[index] = colors[index].Lerp(target, mask * strength);
            }
        }
    }
}

/// <summary>
/// Adds a scaled color to the accumulated vertex light — the vertex-light equivalent of
/// <see cref="ChannelHeightOffset"/>. Starts from black each build, so a chunk nothing binds this on
/// adds nothing.
/// </summary>
public sealed class ChannelVertexLightAdd : ILandscapeVertexLightFunction
{
    public static readonly LandscapeParameter Mask =
        LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read, "Channel scaling the light.", LandscapeChannelFormat.Scalar);

    public static readonly LandscapeParameter Tint =
        LandscapeParameter.Color("color", "Color", Colors.White, "Color of the light added where the mask is fully set.");

    public static readonly LandscapeParameter Amount =
        LandscapeParameter.Float("amount", "Amount", 1.0f, 0.0f, 8.0f, "Intensity of the added light.");

    public string Id => "builtin.vertex_light.channel_add";

    public string DisplayName => "Channel Vertex Light Add";

    public string Description => "Adds a scaled color to the accumulated vertex light.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Mask, Tint, Amount);

    public void Evaluate(in LandscapeEvalContext context, Color[] light)
    {
        Color color = context.Color(Tint);
        float amount = context.Float(Amount);
        int resolution = context.Resolution;
        LandscapeChannelBinding binding = context.ChannelBinding(Mask);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = (y * resolution) + x;
                Vector3 world = context.WorldAt(x, y, vertices: true);
                float mask = context.SampleChannel(binding, world);
                float scale = mask * amount;

                light[index] += new Color(color.R * scale, color.G * scale, color.B * scale, 0.0f);
            }
        }
    }
}

/// <summary>
/// Blends the accumulated vertex color toward a color channel's own RGB(A) — the counterpart to
/// <see cref="ChannelVertexColorTint"/> for a source that already carries its own varying color
/// (e.g. a painted <see cref="PaintImage"/>) rather than one fixed tint masked by a scalar. Weighted
/// by the source channel's own alpha, or by <see cref="Mask"/> when one is bound — useful when the
/// source is RGB with no alpha of its own to weigh by.
/// </summary>
public sealed class ChannelVertexColorPaint : ILandscapeVertexColorFunction
{
    public static readonly LandscapeParameter Source = LandscapeParameter.Channel(
        "source", "Source", LandscapeChannelAccess.Read,
        "RGB(A) channel supplying the color to paint.", LandscapeChannelFormat.Color);

    public static readonly LandscapeParameter Mask = LandscapeParameter.Channel(
        "mask", "Mask", LandscapeChannelAccess.Read,
        "Optional channel scaling how far the paint applies. Unbound uses the source's own alpha " +
        "(fully opaque if it has none).", LandscapeChannelFormat.Scalar, optional: true);

    public static readonly LandscapeParameter Strength =
        LandscapeParameter.Float("strength", "Strength", 1.0f, 0.0f, 1.0f, "Overall intensity of the paint.");

    public string Id => "builtin.vertex_color.channel_paint";

    public string DisplayName => "Channel Vertex Color Paint";

    public string Description => "Blends accumulated vertex color toward a color channel's own RGB(A).";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Source, Mask, Strength);

    public void Evaluate(in LandscapeEvalContext context, Color[] colors)
    {
        float strength = Mathf.Clamp(context.Float(Strength), 0.0f, 1.0f);
        LandscapeChannelBinding mask = context.ChannelBinding(Mask);
        LandscapeChannelBinding source = context.ChannelBinding(Source);
        bool hasMask = !mask.IsEmpty;
        int resolution = context.Resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = (y * resolution) + x;
                Vector3 world = context.WorldAt(x, y, vertices: true);
                Color sampled = context.SampleChannelColor(source, world);
                float weight = Mathf.Clamp((hasMask ? context.SampleChannel(mask, world) : sampled.A) * strength, 0.0f, 1.0f);

                if (weight <= 0.0f)
                {
                    continue;
                }

                // Same accumulate-and-transform shape as ChannelVertexColorTint, just toward a color
                // this build read from the channel instead of one fixed on the material.
                colors[index] = colors[index].Lerp(sampled, weight);
            }
        }
    }
}

/// <summary>
/// Adds a color channel's own RGB, scaled by <see cref="Amount"/>, to the accumulated vertex light —
/// the counterpart to <see cref="ChannelVertexLightAdd"/> for a source that already carries its own
/// varying color rather than one fixed tint scaled by a mask.
/// </summary>
public sealed class ChannelVertexLightPaint : ILandscapeVertexLightFunction
{
    public static readonly LandscapeParameter Source = LandscapeParameter.Channel(
        "source", "Source", LandscapeChannelAccess.Read,
        "RGB(A) channel supplying the light color to add.", LandscapeChannelFormat.Color);

    public static readonly LandscapeParameter Amount =
        LandscapeParameter.Float("amount", "Amount", 1.0f, 0.0f, 8.0f, "Intensity of the added light.");

    public string Id => "builtin.vertex_light.channel_paint";

    public string DisplayName => "Channel Vertex Light Paint";

    public string Description => "Adds a color channel's own RGB, scaled by amount, to the accumulated vertex light.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Source, Amount);

    public void Evaluate(in LandscapeEvalContext context, Color[] light)
    {
        float amount = context.Float(Amount);
        int resolution = context.Resolution;
        LandscapeChannelBinding source = context.ChannelBinding(Source);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = (y * resolution) + x;
                Vector3 world = context.WorldAt(x, y, vertices: true);
                Color sampled = context.SampleChannelColor(source, world);

                light[index] += new Color(sampled.R * amount, sampled.G * amount, sampled.B * amount, 0.0f);
            }
        }
    }
}
