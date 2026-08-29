using System.Collections.Generic;
using System.Numerics;

namespace WorldMapStudio;

/// <summary>
/// Ear-clipping triangulation of a simple 2D polygon (convex or concave, either winding). Shared by
/// <see cref="NetworkEditTool"/> (screen-space face rendering and picking) and
/// <see cref="PanelNetworkMeshFunction"/> (UV-space mesh output) so an authored n-gon face renders and
/// builds correctly instead of the naive vertex-0 fan both used to do, which only works for convex
/// polygons already wound starting from a "safe" vertex.
/// </summary>
public static class PolygonTriangulator
{
    /// <summary>Triangulates <paramref name="points"/> (a simple polygon loop, at least 3 points) into
    /// index triples into that same list. Falls back to a plain vertex-0 fan for whatever remains if
    /// ear-clipping cannot make further progress (self-intersecting or degenerate input) — degrading
    /// gracefully rather than throwing or returning nothing.</summary>
    public static List<int> Triangulate(IReadOnlyList<Vector2> points)
    {
        var result = new List<int>();
        if (points.Count < 3)
        {
            return result;
        }

        var remaining = new List<int>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            remaining.Add(i);
        }

        if (SignedArea(points, remaining) < 0.0f)
        {
            remaining.Reverse();
        }

        int guard = remaining.Count * remaining.Count + 8;
        while (remaining.Count > 3 && guard-- > 0)
        {
            if (!TryClipOneEar(points, remaining, result))
            {
                break;
            }
        }

        // Ear-clipping stalled (self-intersecting input) — fan whatever is left so something renders.
        for (int i = 1; i < remaining.Count - 1; i++)
        {
            result.Add(remaining[0]);
            result.Add(remaining[i]);
            result.Add(remaining[i + 1]);
        }

        return result;
    }

    private static bool TryClipOneEar(IReadOnlyList<Vector2> points, List<int> remaining, List<int> result)
    {
        int n = remaining.Count;
        for (int i = 0; i < n; i++)
        {
            int prev = remaining[(i - 1 + n) % n];
            int cur = remaining[i];
            int next = remaining[(i + 1) % n];
            if (!IsConvex(points[prev], points[cur], points[next]))
            {
                continue;
            }

            bool containsOther = false;
            for (int j = 0; j < n; j++)
            {
                int candidate = remaining[j];
                if (candidate == prev || candidate == cur || candidate == next)
                {
                    continue;
                }

                if (PointInTriangle(points[candidate], points[prev], points[cur], points[next]))
                {
                    containsOther = true;
                    break;
                }
            }

            if (containsOther)
            {
                continue;
            }

            result.Add(prev);
            result.Add(cur);
            result.Add(next);
            remaining.RemoveAt(i);
            return true;
        }

        return false;
    }

    private static float SignedArea(IReadOnlyList<Vector2> points, List<int> order)
    {
        float area = 0.0f;
        for (int i = 0; i < order.Count; i++)
        {
            Vector2 a = points[order[i]];
            Vector2 b = points[order[(i + 1) % order.Count]];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5f;
    }

    private static bool IsConvex(Vector2 prev, Vector2 cur, Vector2 next) => Cross(cur - prev, next - cur) >= 0.0f;

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p - a, b - a);
        float d2 = Cross(p - b, c - b);
        float d3 = Cross(p - c, a - c);
        bool hasNegative = d1 < 0.0f || d2 < 0.0f || d3 < 0.0f;
        bool hasPositive = d1 > 0.0f || d2 > 0.0f || d3 > 0.0f;
        return !(hasNegative && hasPositive);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
