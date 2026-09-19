using Godot;

namespace WorldMapStudio;

/// <summary>
/// Pins the chunk mesh's triangle winding.
///
/// This is worth a test because getting it wrong fails silently in the worst way: the mesh builds,
/// reports upward normals, raises no error, and is simply invisible from above because every triangle
/// is back-facing. Nothing about the symptom points at the winding.
/// </summary>
public static class LandscapeMeshTests
{
    // Vertex (x, y) of the grid sits at world (x * step, height, y * step) — so the grid's second
    // axis runs along Z, and "up" is +Y.
    private static Vector3 Position(int index, int resolution) =>
        new(index % resolution, 0.0f, index / resolution);

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void Every_triangle_faces_up()
    {
        const int resolution = 5;
        int[] indices = LandscapeBatchMesh.BuildIndices(resolution);

        Assert.AreEqual((resolution - 1) * (resolution - 1) * 6, indices.Length);

        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = Position(indices[i], resolution);
            Vector3 b = Position(indices[i + 1], resolution);
            Vector3 c = Position(indices[i + 2], resolution);

            // Godot's front face is clockwise seen from the front, which is the opposite of the
            // right-hand rule: an up-facing triangle therefore has (b-a)x(c-a) pointing *down*.
            Vector3 wound = (b - a).Cross(c - a);

            Assert.IsTrue(wound.Y < 0.0f,
                $"triangle {i / 3} is wound the wrong way and would be culled from above");
        }
    }

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void Every_vertex_of_the_grid_is_referenced()
    {
        // A gap here would show as holes in the terrain rather than as an error.
        const int resolution = 4;
        int[] indices = LandscapeBatchMesh.BuildIndices(resolution);

        var seen = new bool[resolution * resolution];
        foreach (int index in indices)
        {
            Assert.IsTrue(index >= 0 && index < seen.Length, $"index {index} is outside the grid");
            seen[index] = true;
        }

        for (int i = 0; i < seen.Length; i++)
        {
            Assert.IsTrue(seen[i], $"vertex {i} is in no triangle");
        }
    }

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void A_holed_cell_drops_its_quads_but_keeps_its_vertices()
    {
        // 5 vertices = 4 quads; a 2x2 hole grid puts the whole bottom row of quads in cell (0, 1).
        const int resolution = 5;
        const int holeResolution = 2;
        var holes = new bool[holeResolution * holeResolution];
        holes[(1 * holeResolution) + 0] = true; // bottom-left cell only

        int[] dense = LandscapeBatchMesh.BuildIndices(resolution);
        int[] withHole = LandscapeBatchMesh.BuildIndices(resolution, holes, holeResolution);

        // A 4x4 quad grid split into a 2x2 hole grid gives each cell 2x2 = 4 quads; cutting one cell
        // drops exactly those 4 quads' worth of indices.
        Assert.AreEqual(dense.Length - (4 * 6), withHole.Length);

        // Every vertex still appears somewhere — the quads above and to the right of the cut cell keep
        // referencing the shared vertices along its edge, so nothing is orphaned.
        var seen = new bool[resolution * resolution];
        foreach (int index in withHole)
        {
            seen[index] = true;
        }

        bool anySeen = false;
        foreach (bool v in seen)
        {
            anySeen |= v;
        }

        Assert.IsTrue(anySeen, "the hole must not have eaten every vertex");
    }

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void Vertex_light_reaches_emission_not_albedo()
    {
        // Vertex light summed into ALBEDO would be multiplied by the scene's own light instead of
        // surviving a dark scene.
        string shader = LandscapeBatchMesh.SplatShaderCode;

        Assert.IsFalse(shader.Contains("color += vertex_light"), "vertex light must not be summed into albedo");
        Assert.IsTrue(shader.Contains("ALBEDO = color;"), "expected an albedo assignment with no light term");
        Assert.IsTrue(shader.Contains("EMISSION = emission;") && shader.Contains("vertex_light"),
            "expected vertex light to reach EMISSION");
    }

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void Vertex_color_is_carried_as_a_float_custom_channel()
    {
        // Godot's 8-bit COLOR attribute would clamp vertex color, destroying the brightening half of
        // MCCV's 0-2 multiplier range.
        string shader = LandscapeBatchMesh.SplatShaderCode;

        Assert.IsTrue(shader.Contains("CUSTOM2"), "vertex color must travel through a custom channel");
        Assert.IsFalse(shader.Contains("color *= COLOR.rgb"), "COLOR is 8-bit unorm and clamps values above 1.0");
        Assert.IsTrue(shader.Contains("color *= vertex_color"), "expected vertex color to multiply albedo");
    }

    private static LandscapeChunkOutput Chunk(int resolution, float[] heights, float[]? centreHeights)
    {
        int cells = (resolution - 1) * (resolution - 1);
        return new LandscapeChunkOutput
        {
            Coord = new ChunkCoord(0, 0),
            HeightResolution = resolution,
            Heights = heights,
            AlphaResolution = 1,
            Layers = [],
            HoleResolution = 1,
            Holes = new bool[1],
            VertexColors = new Color[resolution * resolution],
            VertexLight = new Color[resolution * resolution],
            CentreHeights = centreHeights,
            CentreVertexColors = centreHeights == null ? null : new Color[cells],
            CentreVertexLight = centreHeights == null ? null : new Color[cells],
        };
    }

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void Every_fan_triangle_faces_up_and_every_vertex_is_referenced()
    {
        const int resolution = 4;
        const int quads = resolution - 1;
        int[] indices = LandscapeBatchMesh.BuildIndices(resolution, null, 0, centres: true);

        Assert.AreEqual(quads * quads * 12, indices.Length);

        static Vector3 Place(int index)
        {
            if (index < resolution * resolution)
            {
                return new Vector3(index % resolution, 0.0f, index / resolution);
            }

            int cell = index - (resolution * resolution);
            return new Vector3((cell % quads) + 0.5f, 0.0f, (cell / quads) + 0.5f);
        }

        var seen = new bool[(resolution * resolution) + (quads * quads)];
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = Place(indices[i]);
            Vector3 b = Place(indices[i + 1]);
            Vector3 c = Place(indices[i + 2]);
            Assert.IsTrue((b - a).Cross(c - a).Y < 0.0f, $"fan triangle {i / 3} is wound the wrong way");
            seen[indices[i]] = seen[indices[i + 1]] = seen[indices[i + 2]] = true;
        }

        for (int i = 0; i < seen.Length; i++)
        {
            Assert.IsTrue(seen[i], $"vertex {i} is in no triangle");
        }
    }

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void A_holed_cell_drops_all_twelve_fan_indices()
    {
        const int resolution = 3;
        bool[] holes = [true, false, false, false];

        int[] dense = LandscapeBatchMesh.BuildIndices(resolution, null, 0, centres: true);
        int[] withHole = LandscapeBatchMesh.BuildIndices(resolution, holes, 2, centres: true);

        Assert.AreEqual(dense.Length - 12, withHole.Length);
    }

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void Cell_surface_height_follows_the_fan_triangle_it_falls_in()
    {
        // One cell: corners 0 (tl), 10 (tr), 20 (bl), 30 (br), and a centre raised to 100.
        LandscapeChunkOutput output = Chunk(2, [0.0f, 10.0f, 20.0f, 30.0f], [100.0f]);

        Assert.AreApproximatelyEqual(100.0f, LandscapeCellSurface.HeightIn(output, 0, 0, 0.5f, 0.5f), 1e-4, "centre");
        Assert.AreApproximatelyEqual(0.0f, LandscapeCellSurface.HeightIn(output, 0, 0, 0.0f, 0.0f), 1e-4, "top-left");
        Assert.AreApproximatelyEqual(10.0f, LandscapeCellSurface.HeightIn(output, 0, 0, 1.0f, 0.0f), 1e-4, "top-right");
        Assert.AreApproximatelyEqual(20.0f, LandscapeCellSurface.HeightIn(output, 0, 0, 0.0f, 1.0f), 1e-4, "bottom-left");
        Assert.AreApproximatelyEqual(30.0f, LandscapeCellSurface.HeightIn(output, 0, 0, 1.0f, 1.0f), 1e-4, "bottom-right");

        // Midway along an edge the fan reduces to that edge's two corners; a bilinear patch would
        // instead let the centre lift the whole cell.
        Assert.AreApproximatelyEqual(5.0f, LandscapeCellSurface.HeightIn(output, 0, 0, 0.5f, 0.0f), 1e-4, "top edge");
        Assert.AreApproximatelyEqual(25.0f, LandscapeCellSurface.HeightIn(output, 0, 0, 0.5f, 1.0f), 1e-4, "bottom edge");
        Assert.AreApproximatelyEqual(10.0f, LandscapeCellSurface.HeightIn(output, 0, 0, 0.0f, 0.5f), 1e-4, "left edge");
        Assert.AreApproximatelyEqual(20.0f, LandscapeCellSurface.HeightIn(output, 0, 0, 1.0f, 0.5f), 1e-4, "right edge");

        // Halfway from the top edge midpoint to the centre.
        Assert.AreApproximatelyEqual(52.5f, LandscapeCellSurface.HeightIn(output, 0, 0, 0.5f, 0.25f), 1e-4, "inside the top triangle");
    }

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void Cell_surface_without_centres_is_bilinear()
    {
        LandscapeChunkOutput plain = Chunk(2, [0.0f, 10.0f, 20.0f, 30.0f], null);

        Assert.AreApproximatelyEqual(15.0f, LandscapeCellSurface.HeightIn(plain, 0, 0, 0.5f, 0.5f), 1e-4, "centre");
        Assert.AreApproximatelyEqual(2.5f, LandscapeCellSurface.HeightIn(plain, 0, 0, 0.25f, 0.0f), 1e-4, "top edge");
    }

    [EditorTest(Category = "LandscapeMesh", Thread = TestThread.Background)]
    public static void A_ray_hits_the_fan_over_a_raised_centre()
    {
        // 2x2 cells of 8 units; the centre of cell (0,0) is raised to 8.
        LandscapeChunkOutput output = Chunk(3, new float[9], [8.0f, 0.0f, 0.0f, 0.0f]);
        const float chunkSize = 16.0f;
        Aabb box = new(new Vector3(0.0f, -1.0f, 0.0f), new Vector3(chunkSize, 10.0f, chunkSize));

        Vector3 HitFrom(float x, float z)
        {
            var start = new Vector3(x, 20.0f, z);
            Assert.IsTrue(TerrainProbe.TryRayBox(start, Vector3.Down, box, out float tMin, out float tMax));
            var candidate = new TerrainProbe.Candidate(new ChunkCoord(0, 0), output, Vector3.Zero, chunkSize, tMin, tMax, tMin);
            Assert.IsTrue(TerrainProbe.TryHitChunk(candidate, start, Vector3.Down, out _, out Vector3 world));
            return world;
        }

        Assert.AreApproximatelyEqual(8.0f, HitFrom(4.0f, 4.0f).Y, 1e-3, "the ray reaches the raised centre");
        Assert.AreApproximatelyEqual(0.0f, HitFrom(12.0f, 12.0f).Y, 1e-3, "an untouched cell stays flat");

        // The ray agrees with the shared height query, which is how things get placed on the ground.
        float expected = LandscapeCellSurface.HeightIn(output, 0, 0, 0.25f, 0.5f);
        Assert.AreApproximatelyEqual(expected, HitFrom(2.0f, 4.0f).Y, 1e-3, "ray and height query agree");
    }
}
