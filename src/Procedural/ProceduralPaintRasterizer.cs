using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Scatters a <see cref="ProceduralPaint"/>'s strokes into a chunk's channel buffers. Lifted from the
/// original road deformer's per-segment scatter so every paint-emitting procedural function shares one
/// implementation instead of every author copying it. Each stroke only touches
/// the texels within its own bounding box (grown by its radius), so a stroke crossing a chunk's corner
/// costs a corner's worth of work, not a whole chunk.
///
/// Writes go only to the chunk in <paramref name="context"/> and never read a channel back — the
/// stage-3 landscape invariant (pure scatter, chunk-local writes) that <see cref="ILandscapeDeformer"/>
/// depends on. Coverage composites with <c>max</c>, so two overlapping strokes (e.g. two chains
/// meeting at a junction) blend into one smooth region instead of doubling up.
/// </summary>
public static class ProceduralPaintRasterizer
{
    public static void Rasterize(ProceduralPaint paint, Transform3D transform, in LandscapeRasterContext context)
    {
        if (paint.Strokes.Count == 0)
        {
            return;
        }

        Vector3 chunkOrigin = context.Grid.OriginOf(context.Coord);
        float chunkSize = context.Grid.ChunkSize;

        foreach (ProceduralStroke stroke in paint.Strokes)
        {
            LandscapeChannel? channel = context.Channel(stroke.Channel);
            LandscapeChannelWriter? writer = channel != null ? context.Writer(stroke.Channel) : null;
            if (writer is not { } w || channel == null)
            {
                continue;
            }

            Vector3 worldA = transform * stroke.A;
            Vector3 worldB = transform * stroke.B;
            float minX = Mathf.Min(worldA.X, worldB.X) - stroke.Radius;
            float maxX = Mathf.Max(worldA.X, worldB.X) + stroke.Radius;
            float minZ = Mathf.Min(worldA.Z, worldB.Z) - stroke.Radius;
            float maxZ = Mathf.Max(worldA.Z, worldB.Z) + stroke.Radius;

            if (maxX < chunkOrigin.X || minX > chunkOrigin.X + chunkSize || maxZ < chunkOrigin.Z || minZ > chunkOrigin.Z + chunkSize)
            {
                continue;
            }

            Scatter(w, channel.Resolution, context, worldA, worldB, stroke.Radius, stroke.Falloff, stroke.Value, minX, maxX, minZ, maxZ);
        }
    }

    private static void Scatter(
        LandscapeChannelWriter writer,
        int resolution,
        in LandscapeRasterContext context,
        Vector3 worldA,
        Vector3 worldB,
        float radius,
        float falloff,
        Color value,
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
        bool writeColor = writer.Components > 1;

        for (int z = zStart; z <= zEnd; z++)
        {
            for (int x = xStart; x <= xEnd; x++)
            {
                Vector3 world = context.TexelCentre(resolution, x, z);
                float distance = DistanceToSegmentXZ(world, worldA, worldB);
                float weight = Weight(distance, radius, falloff);
                if (weight <= 0.0f)
                {
                    continue;
                }

                if (writeColor)
                {
                    Color current = writer.GetColor(x, z);
                    writer.SetColor(x, z, new Color(
                        Mathf.Max(current.R, value.R * weight),
                        Mathf.Max(current.G, value.G * weight),
                        Mathf.Max(current.B, value.B * weight),
                        Mathf.Max(current.A, value.A * weight)));
                }
                else
                {
                    writer.Set(x, z, Mathf.Max(writer.Get(x, z), weight));
                }
            }
        }
    }

    /// <summary>Coverage profile: 1 within the solid core, smoothstepped to 0 across the falloff band,
    /// 0 beyond <paramref name="radius"/>. Same shape <see cref="StampComponent"/> and the original
    /// <c>RoadPath.Weight</c> used, kept here now that every stroke shares one rasterizer.</summary>
    public static float Weight(float distance, float radius, float falloff)
    {
        if (radius <= 0.0f || distance >= radius)
        {
            return 0.0f;
        }

        float solid = radius * (1.0f - falloff);
        if (distance <= solid)
        {
            return 1.0f;
        }

        float fade = radius - solid;
        return fade <= 0.0f ? 1.0f : Mathf.SmoothStep(0.0f, 1.0f, 1.0f - ((distance - solid) / fade));
    }

    public static float DistanceToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
    {
        float dx = b.X - a.X;
        float dz = b.Z - a.Z;
        float lenSq = (dx * dx) + (dz * dz);
        float t = lenSq < 1e-10f ? 0.0f : Mathf.Clamp((((point.X - a.X) * dx) + ((point.Z - a.Z) * dz)) / lenSq, 0.0f, 1.0f);
        float cx = a.X + (dx * t);
        float cz = a.Z + (dz * t);
        float ddx = point.X - cx;
        float ddz = point.Z - cz;
        return Mathf.Sqrt((ddx * ddx) + (ddz * ddz));
    }
}
