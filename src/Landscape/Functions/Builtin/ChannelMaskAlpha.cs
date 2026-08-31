using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The simplest useful alpha function: a channel <em>is</em> the coverage, remapped through a
/// threshold and a softness. Chunk-local, so it needs no halo — an entity that wants its edges to
/// fade across a chunk border does that when it writes the channel.
/// </summary>
public sealed class ChannelMaskAlpha : ILandscapeAlphaFunction
{
    public static readonly LandscapeParameter Mask =
        LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read, "Channel supplying coverage.", LandscapeChannelFormat.Scalar);

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

    public void Evaluate(in LandscapeEvalContext context, float[] alpha)
    {
        float threshold = context.Float(Threshold);
        float softness = context.Float(Softness);
        bool invert = context.Bool(Invert);
        int resolution = context.Resolution;

        // A zero-width fade would divide by zero; treat it as the hard edge the user asked for.
        float low = threshold - (softness * 0.5f);
        float high = threshold + (softness * 0.5f);

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float mask = context.SampleChannel(Mask, context.WorldAt(x, y, vertices: false));
                float coverage = softness <= 0.0f
                    ? (mask >= threshold ? 1.0f : 0.0f)
                    : Mathf.Clamp((mask - low) / (high - low), 0.0f, 1.0f);

                alpha[(y * resolution) + x] = invert ? 1.0f - coverage : coverage;
            }
        }
    }
}
