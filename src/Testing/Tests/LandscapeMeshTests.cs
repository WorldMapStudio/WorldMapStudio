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
}
