using System;
using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A road: a <see cref="VertexNetwork"/> edited exactly like a procedural mesh, but painting a centre
/// and shoulder mask along a spline through it instead of building geometry. Authored flat — vertex
/// height is never stored or read, so the road follows whatever the terrain under it already does.
///
/// Claims nothing itself: like <see cref="StampComponent"/> and <see cref="DrawingTargetComponent"/>,
/// it only writes into named channels (see <see cref="Rasterize"/>). Which layer and material those
/// channels end up painting is <see cref="LandscapeMaterialBindComponent"/>'s job, kept separate so a
/// road never hardcodes a mapping that belongs to the project's catalog.
///
/// Rasterizes by walking <see cref="RoadPath.Segments"/> rather than scanning every texel — each
/// segment only touches the texels within its own bounding box, so a road crossing a chunk's corner
/// costs a corner's worth of work, not a whole chunk.
/// </summary>
public sealed class RoadComponent : SceneComponent, ISceneBoundsProvider, ITransformPolicy, ILandscapeDeformer, INetworkEditable
{
    private VertexNetwork _network = new();
    private float _centreWidth = 4.0f;
    private float _shoulderWidth = 3.0f;
    private float _falloff = 0.35f;
    private RoadPath _path;

    public RoadComponent()
    {
        _path = RoadPath.Build(_network, _centreWidth, _shoulderWidth, _falloff);
    }

    public float CentreWidth
    {
        get => _centreWidth;
        set
        {
            float clamped = Mathf.Max(0.0f, value);
            if (Mathf.IsEqualApprox(_centreWidth, clamped))
            {
                return;
            }

            _centreWidth = clamped;
            RebuildPath();
        }
    }

    public float ShoulderWidth
    {
        get => _shoulderWidth;
        set
        {
            float clamped = Mathf.Max(0.0f, value);
            if (Mathf.IsEqualApprox(_shoulderWidth, clamped))
            {
                return;
            }

            _shoulderWidth = clamped;
            RebuildPath();
        }
    }

    public float Falloff
    {
        get => _falloff;
        set
        {
            float clamped = Mathf.Clamp(value, 0.0f, 1.0f);
            if (Mathf.IsEqualApprox(_falloff, clamped))
            {
                return;
            }

            _falloff = clamped;
            RebuildPath();
        }
    }

    public string CentreChannel { get; set; } = "";

    public string ShoulderChannel { get; set; } = "";

    public VertexNetwork Network => _network;

    public bool PlanarXZ => true;

    /// <summary>The published centreline snapshot. Rebuilt synchronously, on whatever thread calls a
    /// setter or <see cref="ReplaceNetwork"/> — always the main thread, since those are only ever
    /// called from the inspector or the viewport tool. <see cref="Rasterize"/> runs on a worker against
    /// the live component and must only ever read this reference, never rebuild it: a lazy rebuild
    /// there would race the very edit that invalidated it.</summary>
    public RoadPath Path => _path;

    public override string TypeId => "landscape-road";

    public override string DisplayName => "Road";

    public SelfRotation SelfRotation => SelfRotation.HeightOnly;

    public bool UsesTerrainHeight => true;

    public Aabb LocalBounds
    {
        get
        {
            Aabb flat = _path.LocalBounds;
            return new Aabb(
                new Vector3(flat.Position.X, -LandscapeGrid.NominalHeightExtent, flat.Position.Z),
                new Vector3(flat.Size.X, LandscapeGrid.NominalHeightExtent * 2.0f, flat.Size.Z));
        }
    }

    public string DeformerKey => Entity.RecordId is { } id
        ? $"entity:{id}:road"
        : $"entity:new:{Entity.Id.Value}:road";

    public Aabb InfluenceBounds => Entity.Transform * LocalBounds;

    public override int ContentVersion
    {
        get
        {
            var hash = new HashCode();
            hash.Add(_centreWidth);
            hash.Add(_shoulderWidth);
            hash.Add(_falloff);
            hash.Add(CentreChannel);
            hash.Add(ShoulderChannel);
            hash.Add(_network.Fingerprint());
            return hash.ToHashCode();
        }
    }

    public void ReplaceNetwork(VertexNetwork network)
    {
        _network = network.Clone();
        RebuildPath();
    }

    public override SceneComponent Clone()
    {
        var clone = new RoadComponent
        {
            CentreWidth = CentreWidth,
            ShoulderWidth = ShoulderWidth,
            Falloff = Falloff,
            CentreChannel = CentreChannel,
            ShoulderChannel = ShoulderChannel,
        };
        clone.ReplaceNetwork(_network);
        return clone;
    }

    private void RebuildPath()
    {
        _path = RoadPath.Build(_network, _centreWidth, _shoulderWidth, _falloff);
        Owner?.RefreshRepresentation();
    }

    public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context) => [];

    public void Rasterize(in LandscapeRasterContext context)
    {
        RoadPath path = _path;
        if (path.Segments.Count == 0)
        {
            return;
        }

        float[]? centreBuffer = CentreChannel.Length > 0 ? context.Buffer(CentreChannel) : null;
        float[]? shoulderBuffer = ShoulderChannel.Length > 0 ? context.Buffer(ShoulderChannel) : null;
        if (centreBuffer == null && shoulderBuffer == null)
        {
            return;
        }

        int centreResolution = centreBuffer != null ? context.Channel(CentreChannel)!.Resolution : 0;
        int shoulderResolution = shoulderBuffer != null ? context.Channel(ShoulderChannel)!.Resolution : 0;

        Transform3D transform = Entity.Transform;
        Vector3 chunkOrigin = context.Grid.OriginOf(context.Coord);
        float chunkSize = context.Grid.ChunkSize;

        foreach (RoadSegment segment in path.Segments)
        {
            Vector3 worldA = transform * segment.A;
            Vector3 worldB = transform * segment.B;
            float minX = Mathf.Min(worldA.X, worldB.X) - path.OuterRadius;
            float maxX = Mathf.Max(worldA.X, worldB.X) + path.OuterRadius;
            float minZ = Mathf.Min(worldA.Z, worldB.Z) - path.OuterRadius;
            float maxZ = Mathf.Max(worldA.Z, worldB.Z) + path.OuterRadius;

            if (maxX < chunkOrigin.X || minX > chunkOrigin.X + chunkSize || maxZ < chunkOrigin.Z || minZ > chunkOrigin.Z + chunkSize)
            {
                continue;
            }

            if (centreBuffer != null)
            {
                Scatter(centreBuffer, centreResolution, context, worldA, worldB, path.CentreHalf, path.Falloff, minX, maxX, minZ, maxZ);
            }

            if (shoulderBuffer != null)
            {
                Scatter(shoulderBuffer, shoulderResolution, context, worldA, worldB, path.OuterRadius, path.Falloff, minX, maxX, minZ, maxZ);
            }
        }
    }

    /// <summary>Writes one segment's contribution into one channel, touching only the texels within
    /// the segment's own bounding box (already grown by the outer radius by the caller) rather than
    /// scanning the whole chunk.</summary>
    private static void Scatter(
        float[] buffer,
        int resolution,
        in LandscapeRasterContext context,
        Vector3 worldA,
        Vector3 worldB,
        float radius,
        float falloff,
        float minX,
        float maxX,
        float minZ,
        float maxZ)
    {
        Vector3 origin = context.Grid.OriginOf(context.Coord);
        float step = context.Grid.ChunkSize / resolution;
        int xStart = Math.Clamp(Mathf.FloorToInt(((minX - origin.X) / step) - 0.5f), 0, resolution - 1);
        int xEnd = Math.Clamp(Mathf.CeilToInt(((maxX - origin.X) / step) - 0.5f), 0, resolution - 1);
        int zStart = Math.Clamp(Mathf.FloorToInt(((minZ - origin.Z) / step) - 0.5f), 0, resolution - 1);
        int zEnd = Math.Clamp(Mathf.CeilToInt(((maxZ - origin.Z) / step) - 0.5f), 0, resolution - 1);

        for (int z = zStart; z <= zEnd; z++)
        {
            for (int x = xStart; x <= xEnd; x++)
            {
                Vector3 world = context.TexelCentre(resolution, x, z);
                float distance = RoadPath.DistanceToSegmentXZ(world, worldA, worldB);
                float weight = RoadPath.Weight(distance, radius, falloff);
                if (weight <= 0.0f)
                {
                    continue;
                }

                int index = (z * resolution) + x;
                buffer[index] = Mathf.Max(buffer[index], weight);
            }
        }
    }
}
