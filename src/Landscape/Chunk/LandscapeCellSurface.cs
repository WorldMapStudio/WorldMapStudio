using Godot;

namespace WorldMapStudio;

/// <summary>
/// The shape of the surface inside one height-grid cell. With cell centres a cell is a 4-triangle fan
/// around its centre vertex; without, it is the bilinear patch over its corners. Everything that
/// places something on the terrain reads it here so none of them can disagree.
/// </summary>
public static class LandscapeCellSurface
{
    /// <summary>
    /// Height at (<paramref name="fx"/>, <paramref name="fz"/>) inside cell (<paramref name="cellX"/>,
    /// <paramref name="cellY"/>), both in 0..1 across the cell.
    /// </summary>
    public static float HeightIn(LandscapeChunkOutput output, int cellX, int cellY, float fx, float fz)
    {
        float tl = output.HeightAt(cellX, cellY);
        float tr = output.HeightAt(cellX + 1, cellY);
        float bl = output.HeightAt(cellX, cellY + 1);
        float br = output.HeightAt(cellX + 1, cellY + 1);

        if (!output.HasCellCentres)
        {
            return Mathf.Lerp(Mathf.Lerp(tl, tr, fx), Mathf.Lerp(bl, br, fx), fz);
        }

        float centre = output.CentreHeightAt(cellX, cellY);
        float dx = fx - 0.5f;
        float dz = fz - 0.5f;

        // The fan triangle the point falls in is the one on the side it is furthest from the centre
        // along; each weight is the point's barycentric coordinate for one corner of that side.
        float u;
        float v;
        float a;
        float b;
        if (Mathf.Abs(dz) >= Mathf.Abs(dx))
        {
            if (dz < 0.0f)
            {
                (u, v, a, b) = (-dx - dz, dx - dz, tl, tr);
            }
            else
            {
                (u, v, a, b) = (dz - dx, dx + dz, bl, br);
            }
        }
        else if (dx < 0.0f)
        {
            (u, v, a, b) = (-dx - dz, dz - dx, tl, bl);
        }
        else
        {
            (u, v, a, b) = (dx - dz, dx + dz, tr, br);
        }

        return centre + (u * (a - centre)) + (v * (b - centre));
    }

    /// <summary>
    /// The area-weighted normal of the four triangles fanned around <paramref name="p"/>, from its
    /// four surrounding vertices in cyclic order, unnormalised. Up-facing for a ring that runs
    /// (-,-), (+,-), (+,+), (-,+) in X/Z.
    /// </summary>
    public static Vector3 FanNormal(Vector3 p, Vector3 n0, Vector3 n1, Vector3 n2, Vector3 n3) =>
        (n1 - p).Cross(n0 - p) +
        (n2 - p).Cross(n1 - p) +
        (n3 - p).Cross(n2 - p) +
        (n0 - p).Cross(n3 - p);
}
