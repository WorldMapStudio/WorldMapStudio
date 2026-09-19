using System;

namespace WorldMapStudio;

/// <summary>A texel grid's world mapping: texel (x, y) covers the world rectangle starting at
/// (<see cref="OriginX"/> + x * <see cref="TexelWidth"/>, <see cref="OriginZ"/> + y * <see cref="TexelHeight"/>).</summary>
public readonly record struct BrushGrid(
    float OriginX,
    float OriginZ,
    float TexelWidth,
    float TexelHeight,
    int Width,
    int Height);

/// <summary>Receives one covered texel and its brush weight.</summary>
public delegate void BrushTexelVisitor(int x, int y, float weight);

/// <summary>Walks the texels of a grid a dab covers, for targets that write a texel grid.</summary>
public static class BrushRaster
{
    /// <summary>Whether the dab's disc overlaps the grid at all.</summary>
    public static bool Overlaps(in BrushGrid grid, in BrushDab dab) =>
        dab.Center.X + dab.Radius > grid.OriginX &&
        dab.Center.X - dab.Radius < grid.OriginX + (grid.Width * grid.TexelWidth) &&
        dab.Center.Z + dab.Radius > grid.OriginZ &&
        dab.Center.Z - dab.Radius < grid.OriginZ + (grid.Height * grid.TexelHeight);

    /// <summary>
    /// Calls <paramref name="visit"/> for every texel whose centre is inside the dab, with its
    /// <see cref="BrushFalloff"/> weight. Weights come from each texel's world position, so a dab
    /// that spans two grids paints identically to one unbroken grid. Returns how many texels were visited.
    /// </summary>
    public static int Walk(in BrushGrid grid, in BrushDab dab, BrushTexelVisitor visit)
    {
        if (dab.Radius <= 0.0f)
        {
            return 0;
        }

        int minX = Math.Max((int)MathF.Floor((dab.Center.X - dab.Radius - grid.OriginX) / grid.TexelWidth), 0);
        int maxX = Math.Min((int)MathF.Ceiling((dab.Center.X + dab.Radius - grid.OriginX) / grid.TexelWidth), grid.Width - 1);
        int minY = Math.Max((int)MathF.Floor((dab.Center.Z - dab.Radius - grid.OriginZ) / grid.TexelHeight), 0);
        int maxY = Math.Min((int)MathF.Ceiling((dab.Center.Z + dab.Radius - grid.OriginZ) / grid.TexelHeight), grid.Height - 1);

        float inverseRadius = 1.0f / dab.Radius;
        int visited = 0;
        for (int y = minY; y <= maxY; y++)
        {
            float dz = (grid.OriginZ + ((y + 0.5f) * grid.TexelHeight) - dab.Center.Z) * inverseRadius;
            for (int x = minX; x <= maxX; x++)
            {
                float dx = (grid.OriginX + ((x + 0.5f) * grid.TexelWidth) - dab.Center.X) * inverseRadius;
                float distance = MathF.Sqrt((dx * dx) + (dz * dz));
                if (distance > 1.0f)
                {
                    continue;
                }

                float weight = BrushFalloff.Weight(distance, dab.Hardness);
                if (weight <= 0.0f)
                {
                    continue;
                }

                visit(x, y, weight);
                visited++;
            }
        }

        return visited;
    }
}
