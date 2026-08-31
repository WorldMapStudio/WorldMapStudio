using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Ray-vs-triangle picking against real mesh geometry, used by click selection so that clicking a
/// model selects it only when the cursor is actually over one of its faces — not merely inside its
/// bounding box, which for anything hollow, thin, or L-shaped covers a lot of empty space.
///
/// Triangles are read straight off each surface's vertex/index arrays rather than via
/// <see cref="Mesh.GetFaces"/>: the latter goes through <c>generate_triangle_mesh()</c>, which
/// silently yields nothing for surfaces it can't process and would turn "no geometry to test" into
/// "never selectable" with no way to tell the two apart. Results are cached per <see cref="Mesh"/>,
/// because a click otherwise re-reads every surface array of every in-view model.
/// </summary>
public static class MeshPicking
{
    // Keyed on the mesh instance so a cache entry dies with the mesh it describes; the surface arrays
    // of a loaded model asset never change under us, and a re-imported asset is a new Mesh object.
    private static readonly ConditionalWeakTable<Mesh, Vector3[]> TriangleCache = new();

    /// <summary>
    /// This mesh's triangles as flat vertex triples in mesh-local space. Empty when the mesh has no
    /// triangle surfaces (a point cloud, a line gizmo, or a surface that isn't
    /// <see cref="Mesh.PrimitiveType.Triangles"/>).
    /// </summary>
    public static Vector3[] Triangles(Mesh mesh)
    {
        if (TriangleCache.TryGetValue(mesh, out Vector3[]? cached))
        {
            return cached;
        }

        Vector3[] triangles = ExtractTriangles(mesh);
        TriangleCache.AddOrUpdate(mesh, triangles);
        return triangles;
    }

    /// <summary>
    /// Cheap reject against a mesh's world-space bounds, so a click only pays for
    /// <see cref="TryRayTriangles"/> — and the per-node <c>AffineInverse()</c> that feeds it — on the
    /// handful of meshes the ray is actually near, not every mesh under the clicked entity.
    /// </summary>
    public static bool TryRayBox(Vector3 origin, Vector3 dir, Aabb bounds)
    {
        Vector3 lo = bounds.Position;
        Vector3 hi = bounds.End;

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis];
            float d = dir[axis];
            if (Mathf.Abs(d) < 1e-8f)
            {
                if (o < lo[axis] || o > hi[axis])
                {
                    return false;
                }

                continue;
            }

            float t1 = (lo[axis] - o) / d;
            float t2 = (hi[axis] - o) / d;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax)
            {
                return false;
            }
        }

        return tMax >= 0.0f;
    }

    /// <summary>
    /// Nearest intersection of the ray with <paramref name="triangles"/>, if any is nearer than
    /// <paramref name="best"/>, which it updates in place.
    ///
    /// <paramref name="dir"/> need not be normalised: when the caller transforms a world ray into a
    /// mesh's local space, the direction picks up that space's scale, and leaving it unnormalised is
    /// exactly what keeps the returned <paramref name="best"/> a distance along the original world
    /// ray — so hits on differently scaled meshes stay directly comparable.
    /// </summary>
    public static bool TryRayTriangles(Vector3[] triangles, Vector3 origin, Vector3 dir, ref float best)
    {
        bool hit = false;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            if (TryRayTriangle(origin, dir, triangles[i], triangles[i + 1], triangles[i + 2], out float t) && t < best)
            {
                best = t;
                hit = true;
            }
        }

        return hit;
    }

    /// <summary>
    /// Möller–Trumbore, deliberately two-sided: a lot of WoW geometry is single-sided planes viewed
    /// from whichever side the camera happens to be on, and back-face culling the pick would make
    /// those unselectable from behind even though they're plainly visible.
    /// </summary>
    public static bool TryRayTriangle(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
    {
        t = 0.0f;
        Vector3 e1 = v1 - v0;
        Vector3 e2 = v2 - v0;
        Vector3 p = d.Cross(e2);
        float det = e1.Dot(p);
        if (Mathf.Abs(det) < 1e-12f)
        {
            return false;
        }

        float invDet = 1.0f / det;
        Vector3 tv = o - v0;
        float u = tv.Dot(p) * invDet;
        if (u < 0.0f || u > 1.0f)
        {
            return false;
        }

        Vector3 q = tv.Cross(e1);
        float v = d.Dot(q) * invDet;
        if (v < 0.0f || u + v > 1.0f)
        {
            return false;
        }

        float hitT = e2.Dot(q) * invDet;
        if (hitT < 0.0f)
        {
            return false;
        }

        t = hitT;
        return true;
    }

    /// <summary>
    /// Cheap check for "would <see cref="Triangles"/> return anything" — the same primitive-type
    /// filter <see cref="ExtractTriangles"/> applies, exposed separately so a caller can tell whether a
    /// mesh has real geometry at all without paying for the marshaled <c>SurfaceGetArrays</c> call and
    /// flattened vertex copy that only matters once something is actually worth testing.
    /// </summary>
    public static bool HasTriangleSurface(Mesh mesh)
    {
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            if (mesh is not ArrayMesh array || array.SurfaceGetPrimitiveType(surface) == Mesh.PrimitiveType.Triangles)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector3[] ExtractTriangles(Mesh mesh)
    {
        var triangles = new List<Vector3>();
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            // Only ArrayMesh exposes the per-surface primitive type; a PrimitiveMesh (the box we draw
            // for a model that failed to load, marker gizmos) is triangles by construction.
            if (mesh is ArrayMesh array && array.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles)
            {
                continue;
            }

            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
            if (arrays.Count <= (int)Mesh.ArrayType.Vertex)
            {
                continue;
            }

            if (arrays[(int)Mesh.ArrayType.Vertex].As<Vector3[]>() is not { Length: > 0 } vertices)
            {
                continue;
            }

            // An indexed surface lists its triangles in the index array; a non-indexed one is already
            // three-vertices-per-triangle in order.
            int[]? indices = arrays.Count > (int)Mesh.ArrayType.Index
                ? arrays[(int)Mesh.ArrayType.Index].As<int[]>()
                : null;

            if (indices is { Length: > 0 })
            {
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    triangles.Add(vertices[indices[i]]);
                    triangles.Add(vertices[indices[i + 1]]);
                    triangles.Add(vertices[indices[i + 2]]);
                }
            }
            else
            {
                for (int i = 0; i + 2 < vertices.Length; i += 3)
                {
                    triangles.Add(vertices[i]);
                    triangles.Add(vertices[i + 1]);
                    triangles.Add(vertices[i + 2]);
                }
            }
        }

        return triangles.ToArray();
    }
}
