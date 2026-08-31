using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The simplest useful hole function: a channel above a threshold becomes a hole. Chunk-local, so it
/// needs no halo. Mirrors <see cref="ChannelMaskAlpha"/>'s threshold/softness/invert shape, but the
/// softness only decides how far out from the threshold a cell still counts as fully covered — the
/// output is a hard boolean, not coverage, since a quad is either cut or it is not.
/// </summary>
public sealed class ChannelMaskHole : ILandscapeHoleFunction
{
    public static readonly LandscapeParameter Mask =
        LandscapeParameter.Channel("mask", "Mask", LandscapeChannelAccess.Read, "Channel deciding where the hole is.", LandscapeChannelFormat.Scalar);

    public static readonly LandscapeParameter Threshold =
        LandscapeParameter.Float("threshold", "Threshold", 0.5f, 0.0f, 1.0f, "Mask value at and above which a cell becomes a hole.");

    public static readonly LandscapeParameter Invert =
        LandscapeParameter.Bool("invert", "Invert", false, "Hole where the mask is low instead of high.");

    public string Id => "builtin.hole.channel_mask";

    public string DisplayName => "Channel Mask Hole";

    public string Description => "Cuts a hole wherever a channel crosses a threshold.";

    public int Version => 1;

    public float MaxSampleRadius => 0.0f;

    public IReadOnlyList<LandscapeParameter> Parameters { get; } = LandscapeParameter.List(Mask, Threshold, Invert);

    public void Evaluate(in LandscapeEvalContext context, bool[] holes)
    {
        float threshold = context.Float(Threshold);
        bool invert = context.Bool(Invert);
        int resolution = context.Resolution;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float mask = context.SampleChannel(Mask, context.WorldAt(x, y, vertices: false));
                bool isHole = invert ? mask < threshold : mask >= threshold;

                // Write-only-true contract: never clear a cell another surviving claim already set.
                if (isHole)
                {
                    holes[(y * resolution) + x] = true;
                }
            }
        }
    }
}
