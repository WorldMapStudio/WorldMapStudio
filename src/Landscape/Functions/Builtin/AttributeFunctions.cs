using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Writes a fixed value into a terrain attribute wherever a mask covers — the authoring workhorse:
/// paint a mask, say what it means. With no mask bound it writes everywhere. Scalar: a wider attribute
/// is filled by several writes on the same material, each swizzled to one component.
/// </summary>
public sealed class AttributeConstant : ILandscapeAttributeFunction
{
    public static readonly LandscapeParameter Value =
        LandscapeParameter.AttributeValue("value", "Value", "What to write where the mask covers.");

    public static readonly LandscapeParameter Mask = LandscapeParameter.Channel(
        "mask", "Mask", LandscapeChannelAccess.Read, "Where to write. Unbound means everywhere.",
        LandscapeChannelFormat.Scalar, optional: true);

    public static readonly LandscapeParameter Threshold = LandscapeParameter.Float(
        "threshold", "Threshold", 0.5f, 0.0f, 1.0f, "Mask value at and above which a cell is written.");

    public string Id => "builtin.attribute.constant";

    public string DisplayName => "Attribute Constant";

    public string Description => "Writes a fixed value where a mask covers (everywhere with no mask).";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Value, Mask, Threshold);

    public void Evaluate(in LandscapeEvalContext context, in TerrainAttributeWriter cells)
    {
        uint value = context.UInt(Value);
        float threshold = context.Float(Threshold);
        LandscapeChannelBinding mask = context.ChannelBinding(Mask);
        int resolution = context.Resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                if (mask.IsEmpty || context.ReadChannel(mask, context.WorldAt(x, y, vertices: false)) >= threshold)
                {
                    cells.Set(x, y, value);
                }
            }
        }
    }
}

/// <summary>
/// Copies a channel's value into a terrain attribute, read at the single nearest texel and rounded —
/// how bulk imported data arrives, and how a procedural source can drive ids. Optionally gated by a
/// mask.
/// </summary>
public sealed class AttributeChannelValue : ILandscapeAttributeFunction
{
    public static readonly LandscapeParameter Source = LandscapeParameter.Channel(
        "source", "Source", LandscapeChannelAccess.Read, "Channel whose value becomes the attribute.",
        LandscapeChannelFormat.Scalar);

    public static readonly LandscapeParameter Mask = LandscapeParameter.Channel(
        "mask", "Mask", LandscapeChannelAccess.Read, "Where to write. Unbound means everywhere.",
        LandscapeChannelFormat.Scalar, optional: true);

    public static readonly LandscapeParameter Threshold = LandscapeParameter.Float(
        "threshold", "Threshold", 0.5f, 0.0f, 1.0f, "Mask value at and above which a cell is written.");

    public string Id => "builtin.attribute.channel_value";

    public string DisplayName => "Attribute From Channel";

    public string Description => "Copies a channel's value into an attribute, read nearest-texel and rounded.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Source, Mask, Threshold);

    public void Evaluate(in LandscapeEvalContext context, in TerrainAttributeWriter cells)
    {
        LandscapeChannelBinding source = context.ChannelBinding(Source);
        LandscapeChannelBinding mask = context.ChannelBinding(Mask);
        float threshold = context.Float(Threshold);
        int resolution = context.Resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector3 world = context.WorldAt(x, y, vertices: false);
                if (!mask.IsEmpty && context.ReadChannel(mask, world) < threshold)
                {
                    continue;
                }

                float raw = context.ReadChannel(source, world);
                cells.Set(x, y, raw <= 0.0f ? 0u : (uint)Mathf.RoundToInt(raw));
            }
        }
    }
}

/// <summary>
/// Sets or clears bits in a terrain attribute wherever a mask covers, so several layers can each
/// contribute one flag. Reads what earlier layers wrote and rewrites it, like height.
/// </summary>
public sealed class AttributeFlags : ILandscapeAttributeFunction
{
    public static readonly LandscapeParameter Bits =
        LandscapeParameter.AttributeValue("bits", "Bits", "The bit(s) to set or clear.");

    public static readonly LandscapeParameter Clear = LandscapeParameter.Bool(
        "clear", "Clear", false, "Clear the bits instead of setting them.");

    public static readonly LandscapeParameter Mask = LandscapeParameter.Channel(
        "mask", "Mask", LandscapeChannelAccess.Read, "Where to act. Unbound means everywhere.",
        LandscapeChannelFormat.Scalar, optional: true);

    public static readonly LandscapeParameter Threshold = LandscapeParameter.Float(
        "threshold", "Threshold", 0.5f, 0.0f, 1.0f, "Mask value at and above which a cell is acted on.");

    public string Id => "builtin.attribute.flags";

    public string DisplayName => "Attribute Flags";

    public string Description => "ORs or ANDs-NOT bits into an attribute where a mask covers.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Bits, Clear, Mask, Threshold);

    public void Evaluate(in LandscapeEvalContext context, in TerrainAttributeWriter cells)
    {
        uint bits = context.UInt(Bits);
        bool clear = context.Bool(Clear);
        float threshold = context.Float(Threshold);
        LandscapeChannelBinding mask = context.ChannelBinding(Mask);
        int resolution = context.Resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                if (!mask.IsEmpty && context.ReadChannel(mask, context.WorldAt(x, y, vertices: false)) < threshold)
                {
                    continue;
                }

                uint current = cells.Get(x, y);
                cells.Set(x, y, clear ? current & ~bits : current | bits);
            }
        }
    }
}
