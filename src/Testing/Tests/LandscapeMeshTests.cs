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
        int[] indices = LandscapeChunkMesh.BuildIndices(resolution);

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
        int[] indices = LandscapeChunkMesh.BuildIndices(resolution);

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
}
