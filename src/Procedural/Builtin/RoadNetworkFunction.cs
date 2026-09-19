using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Turns a <see cref="VertexNetwork"/> into a road: a centre and a surrounding shoulder mask painted
/// along a Catmull-Rom spline through the network, using the chain-extraction and flattening
/// <see cref="RoadPath"/> already provides. Emits no model outputs — this is a paint-only function,
/// the reason <see cref="ProceduralComponent"/> renders nothing for a placement bound to it beyond the
/// tool's own overlay.
///
/// Authored flat: vertex height is never stored or read (<see cref="PlanarNetwork"/>), so the road follows whatever
/// the terrain under it already does. The two textures are picked as channel names rather than layer/material combos
/// at the function level.
/// </summary>
[Subsystem(nameof(ProceduralSystem))]
public sealed class RoadNetworkFunction : IProceduralFunction
{
    public static readonly MeshParameter CentreWidth =
        MeshParameter.Float("centre_width", "Centre width", 4.0f, 0.0f, 256.0f, "Width of the road's centre texture.");

    public static readonly MeshParameter ShoulderWidth =
        MeshParameter.Float("shoulder_width", "Shoulder width", 3.0f, 0.0f, 256.0f, "Width of the shoulder beyond the centre.");

    public static readonly MeshParameter Falloff =
        MeshParameter.Float("falloff", "Falloff", 0.35f, 0.0f, 1.0f, "Fraction of each texture's width that fades rather than sitting solid.");

    public static readonly MeshParameter CentreChannel =
        MeshParameter.Channel("centre_channel", "Centre channel", "Landscape channel the centre texture reads.");

    public static readonly MeshParameter ShoulderChannel =
        MeshParameter.Channel("shoulder_channel", "Shoulder channel", "Landscape channel the shoulder texture reads.");

    public string Id => "builtin.procedural.road";

    public string DisplayName => "Road";

    public string Description => "Paints a road centre and shoulder along a spline through the network.";

    public int Version => 1;

    /// <summary>Vertex height is never stored or read — see the class comment.</summary>
    public bool PlanarNetwork => true;

    public SelfRotation SelfRotation => SelfRotation.HeightOnly;

    public SelfScale SelfScale => SelfScale.None;

    public IReadOnlyList<MeshParameter> Parameters { get; } =
        MeshParameter.List(CentreWidth, ShoulderWidth, Falloff, CentreChannel, ShoulderChannel);

    /// <summary>No model outputs: this function only paints (see <see cref="ProceduralOutputBuilder.AddStroke"/>).</summary>
    public IReadOnlyList<ProceduralOutputSlot> Outputs => [];

    public RoadNetworkFunction(ProceduralSystem system)
    {
    }

    public void Build(in ProceduralBuildContext context, ProceduralOutputBuilder output)
    {
        float centreWidth = Mathf.Max(0.0f, context.Float(CentreWidth));
        float shoulderWidth = Mathf.Max(0.0f, context.Float(ShoulderWidth));
        float falloff = Mathf.Clamp(context.Float(Falloff), 0.0f, 1.0f);
        string centreChannel = context.Channel(CentreChannel);
        string shoulderChannel = context.Channel(ShoulderChannel);

        if (centreChannel.Length == 0 && shoulderChannel.Length == 0)
        {
            return;
        }

        RoadPath path = RoadPath.Build(context.Network, centreWidth, shoulderWidth, falloff);
        foreach (RoadSegment segment in path.Segments)
        {
            if (centreChannel.Length > 0)
            {
                output.AddStroke(centreChannel, segment.A, segment.B, path.CentreHalf, falloff);
            }

            if (shoulderChannel.Length > 0)
            {
                output.AddStroke(shoulderChannel, segment.A, segment.B, path.OuterRadius, falloff);
            }
        }
    }
}
