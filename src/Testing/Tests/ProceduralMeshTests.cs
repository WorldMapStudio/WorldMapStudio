using System.Collections.Generic;
using System.Linq;
using Godot;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

public static class ProceduralMeshTests
{
    private sealed class SingleLinearFunction : IProceduralFunction
    {
        public string Id => "test.single_linear";
        public string DisplayName => "Single Linear";
        public string Description => "";
        public int Version => 1;
        public NetworkCapabilities Capabilities => new(AllowsMultipleGraphs: false, AllowsBranching: false);
        public System.Collections.Generic.IReadOnlyList<MeshParameter> Parameters { get; } = [];
        public void Build(in ProceduralBuildContext context, ProceduralOutputBuilder output) { }
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Parameter_values_round_trip_and_keep_unknown_keys()
    {
        var values = new MeshParameterValues();
        values.Set(TubeNetworkMeshFunction.Radius, 1.25f);
        values.Set(TubeNetworkMeshFunction.Segments, 12);
        values.Set(StandardMeshMaterial.Texture, "textures/stone.png");
        values.Set(StandardMeshMaterial.Albedo, new Color(0.1f, 0.2f, 0.3f, 0.4f));

        MeshParameterValues parsed = MeshParameterValues.Parse(values.Serialize());

        Assert.AreApproximatelyEqual(1.25, parsed.GetFloat(TubeNetworkMeshFunction.Radius), 1e-5);
        Assert.AreEqual(12, parsed.GetInt(TubeNetworkMeshFunction.Segments));
        Assert.AreEqual("textures/stone.png", parsed.GetTexture(StandardMeshMaterial.Texture));
        Assert.AreApproximatelyEqual(0.3, parsed.GetColor(StandardMeshMaterial.Albedo).B, 1e-5);

        var retired = MeshParameter.Float("retired", "Retired", 0.0f, 0.0f, 1.0f);
        parsed.Set(retired, 9.0f);
        Assert.IsTrue(MeshParameterValues.Parse(parsed.Serialize()).Raw.ContainsKey("retired"));
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Network_serializes_and_rejects_invalid_edges()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));

        Assert.IsNotNull(network.AddEdge(a, b));
        Assert.IsNull(network.AddEdge(a, b), "duplicate undirected edges should be ignored");
        Assert.IsNull(network.AddEdge(a, a), "self edges should be ignored");
        Assert.IsNull(network.AddEdge(a, 999), "edges to missing vertices should be ignored");

        VertexNetwork parsed = VertexNetwork.Parse(network.Serialize());
        Assert.AreEqual(2, parsed.Vertices.Count);
        Assert.AreEqual(1, parsed.Edges.Count);
        Assert.AreEqual(network.Fingerprint(), parsed.Fingerprint());
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Split_merge_duplicate_and_extrude_update_the_graph()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        int edge = network.AddEdge(a, b)!.Value;

        int middle = network.SplitEdge(edge)!.Value;
        Assert.AreEqual(3, network.Vertices.Count);
        Assert.AreEqual(2, network.Edges.Count);
        Assert.AreApproximatelyEqual(1.0, network.Vertex(middle)!.Position.X, 1e-5);

        int kept = network.MergeVertices([a, middle])!.Value;
        Assert.AreEqual(2, network.Vertices.Count);
        Assert.IsNotNull(network.Vertex(kept));

        var duplicated = network.DuplicateSubgraph([kept], [], [], new Vector3(0.0f, 1.0f, 0.0f));
        Assert.AreEqual(1, duplicated.VertexIds.Count);
        Assert.AreEqual(3, network.Vertices.Count);

        var extruded = network.Extrude(duplicated.VertexIds, [], [], new Vector3(0.0f, 0.0f, 1.0f));
        Assert.AreEqual(1, extruded.VertexIds.Count);
        Assert.IsTrue(network.Edges.Count >= 2, "extrusion should connect old and new vertices");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Faces_create_boundary_edges_cascade_on_removal_and_round_trip()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(1.0f, 1.0f, 0.0f));
        int d = network.AddVertex(new Vector3(0.0f, 1.0f, 0.0f));

        Assert.IsNull(network.AddFace([a, b]), "a face needs at least 3 vertices");

        network.AddFace([a, b, c, d]);
        Assert.AreEqual(4, network.Edges.Count, "AddFace should create its missing boundary edges");
        Assert.IsNull(network.AddFace([c, d, a, b]), "a rotated duplicate of an existing face should be rejected");

        VertexNetwork parsed = VertexNetwork.Parse(network.Serialize());
        Assert.AreEqual(1, parsed.Faces.Count);
        Assert.AreEqual(network.Fingerprint(), parsed.Fingerprint());

        NetworkEdge boundaryEdge = network.Edges.First(e => (e.A == a && e.B == b) || (e.A == b && e.B == a));
        network.RemoveEdge(boundaryEdge.Id);
        Assert.AreEqual(0, network.Faces.Count, "removing a boundary edge should drop the face it supports");

        network.AddFace([a, b, c, d]);
        network.RemoveVertex(c);
        Assert.AreEqual(0, network.Faces.Count, "removing a vertex should drop faces that reference it");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Merge_and_duplicate_carry_faces_along()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(1.0f, 1.0f, 0.0f));
        int d = network.AddVertex(new Vector3(0.0f, 1.0f, 0.0f));
        int face = network.AddFace([a, b, c, d])!.Value;

        var duplicated = network.DuplicateSubgraph([], [], [face], new Vector3(0.0f, 0.0f, 1.0f));
        Assert.AreEqual(4, duplicated.VertexIds.Count);
        Assert.AreEqual(1, duplicated.FaceIds.Count, "duplicating a face should reproduce it at the new vertices");
        Assert.AreEqual(2, network.Faces.Count);

        int e = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        int kept = network.MergeVertices([b, e])!.Value;
        NetworkFace merged = network.Face(face)!;
        Assert.AreEqual(4, merged.Vertices.Count, "merging an unrelated vertex into a face's vertex should not change the face's shape");
        Assert.IsTrue(merged.Vertices.Contains(kept));
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Extruding_a_face_creates_side_walls_and_moves_the_cap()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(1.0f, 1.0f, 0.0f));
        int d = network.AddVertex(new Vector3(0.0f, 1.0f, 0.0f));
        int face = network.AddFace([a, b, c, d])!.Value;

        var extruded = network.Extrude([], [], [face], new Vector3(0.0f, 0.0f, 1.0f));

        Assert.AreEqual(4, extruded.VertexIds.Count, "extruding a quad face should create 4 new vertices");
        Assert.AreEqual(5, extruded.FaceIds.Count, "the 4 new side walls plus the moved cap");
        Assert.IsTrue(extruded.FaceIds.Contains(face), "the cap keeps the original face's id rather than a fresh one");
        Assert.AreEqual(5, network.Faces.Count, "4 side walls plus the moved cap");
        Assert.AreEqual(8, network.Vertices.Count, "4 original vertices plus 4 extruded ones");

        NetworkFace cap = network.Face(face)!;
        Assert.IsFalse(cap.Vertices.Contains(a), "the cap should reference the new, extruded vertices rather than the originals");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Extruding_two_adjacent_faces_skips_the_shared_interior_edge()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(1.0f, 1.0f, 0.0f));
        int d = network.AddVertex(new Vector3(0.0f, 1.0f, 0.0f));
        int e = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        int f = network.AddVertex(new Vector3(2.0f, 1.0f, 0.0f));
        int faceLeft = network.AddFace([a, b, c, d])!.Value;
        int faceRight = network.AddFace([b, e, f, c])!.Value;

        var extruded = network.Extrude([], [], [faceLeft, faceRight], new Vector3(0.0f, 0.0f, 1.0f));

        // Two quads sharing one edge have 6 outer boundary edges total (8 minus the 2 halves of the
        // shared one) — a wall per outer edge, no wall along the shared b-c edge.
        Assert.AreEqual(6, extruded.FaceIds.Count - 2, "6 outer walls, not 8 — the shared edge gets none");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Subdivide_a_quad_face_creates_a_centre_fan_of_four_quads()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(2.0f, 2.0f, 0.0f));
        int d = network.AddVertex(new Vector3(0.0f, 2.0f, 0.0f));
        int face = network.AddFace([a, b, c, d])!.Value;

        var result = network.Subdivide([], [face]);

        Assert.AreEqual(5, result.VertexIds.Count, "4 edge midpoints plus 1 centre vertex");
        Assert.AreEqual(4, result.FaceIds.Count, "the quad becomes 4 quads");
        Assert.AreEqual(4, network.Faces.Count);
        Assert.AreEqual(9, network.Vertices.Count, "4 original + 5 new");
        Assert.IsNull(network.Face(face), "the original face id should be gone, replaced by 4 new ones");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Subdividing_only_some_of_a_faces_edges_keeps_it_as_one_larger_ngon()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(1.0f, 1.0f, 0.0f));
        int d = network.AddVertex(new Vector3(0.0f, 1.0f, 0.0f));
        int face = network.AddFace([a, b, c, d])!.Value;
        NetworkEdge ab = network.Edges.First(e => (e.A == a && e.B == b) || (e.A == b && e.B == a));

        var result = network.Subdivide([ab.Id], []);

        Assert.AreEqual(1, result.VertexIds.Count);
        Assert.AreEqual(0, result.FaceIds.Count, "no centre fan when only one of the face's edges was cut");
        NetworkFace updated = network.Face(face)!;
        Assert.AreEqual(5, updated.Vertices.Count, "the face keeps its id and shape, gaining the new midpoint on its boundary");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Loop_cut_splits_a_ring_of_two_quads()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(1.0f, 1.0f, 0.0f));
        int d = network.AddVertex(new Vector3(0.0f, 1.0f, 0.0f));
        int e = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        int f = network.AddVertex(new Vector3(2.0f, 1.0f, 0.0f));
        network.AddFace([a, b, c, d]);
        network.AddFace([b, e, f, c]);
        NetworkEdge shared = network.Edges.First(edge => (edge.A == b && edge.B == c) || (edge.A == c && edge.B == b));

        var result = network.LoopCut(shared.Id);

        Assert.AreEqual(3, result.VertexIds.Count, "3 new midpoints: the seed edge plus one on each side of the ring");
        Assert.AreEqual(4, result.FaceIds.Count, "each of the 2 quads the ring crosses splits into 2");
        Assert.AreEqual(4, network.Faces.Count, "the two original quads are replaced by 4 smaller ones");
        Assert.AreEqual(9, network.Vertices.Count, "6 original vertices plus 3 midpoints");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Loop_cut_on_a_lone_quad_does_not_wall_off_the_boundary_edges()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(1.0f, 1.0f, 0.0f));
        int d = network.AddVertex(new Vector3(0.0f, 1.0f, 0.0f));
        int face = network.AddFace([a, b, c, d])!.Value;
        NetworkEdge ab = network.Edges.First(edge => (edge.A == a && edge.B == b) || (edge.A == b && edge.B == a));

        var result = network.LoopCut(ab.Id);

        Assert.AreEqual(2, result.VertexIds.Count, "the seed edge and its one opposite edge each get a midpoint");
        Assert.AreEqual(2, result.FaceIds.Count, "the single quad splits into 2");
        Assert.AreEqual(2, network.Faces.Count);
        Assert.IsNull(network.Face(face));
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Network_bounds_follow_off_origin_vertices()
    {
        var network = new VertexNetwork();
        network.AddVertex(new Vector3(10.0f, -1.0f, 2.0f));
        network.AddVertex(new Vector3(15.0f, 3.0f, 8.0f));

        Aabb bounds = network.Bounds();

        Assert.AreApproximatelyEqual(10.0, bounds.Position.X, 1e-5);
        Assert.AreApproximatelyEqual(-1.0, bounds.Position.Y, 1e-5);
        Assert.AreApproximatelyEqual(5.0, bounds.Size.X, 1e-5);
        Assert.AreApproximatelyEqual(6.0, bounds.Size.Z, 1e-5);
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Function_topology_capabilities_validate_network_shape()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        int d = network.AddVertex(new Vector3(3.0f, 0.0f, 0.0f));
        int separate = network.AddVertex(new Vector3(10.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);
        network.AddEdge(a, c);
        network.AddEdge(a, d);

        Assert.AreEqual(2, network.ConnectedGraphCount(), "the isolated vertex counts as its own graph");
        Assert.IsTrue(network.HasBranches());

        var function = new SingleLinearFunction();
        var problems = network.ValidateFor(function.DisplayName, function.Capabilities);
        Assert.AreEqual(2, problems.Count);
        Assert.IsTrue(problems[0].Contains("only one connected graph"));
        Assert.IsTrue(problems[1].Contains("only linear graphs"));
        Assert.IsNotNull(network.Vertex(separate));
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Validate_for_reports_faces_ignored_by_a_function_that_does_not_use_them()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(0.0f, 1.0f, 0.0f));
        network.AddFace([a, b, c]);

        var problems = network.ValidateFor("Tube Network", NetworkCapabilities.Default);
        Assert.AreEqual(1, problems.Count);
        Assert.IsTrue(problems[0].Contains("will be ignored"));

        Assert.AreEqual(0, network.ValidateFor("Panel Network", new NetworkCapabilities(AllowsFaces: true)).Count);
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Main)]
    public static void Panel_network_builds_a_triangulated_surface_per_face()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(1.0f, 1.0f, 0.0f));
        int d = network.AddVertex(new Vector3(0.0f, 1.0f, 0.0f));
        network.AddFace([a, b, c, d]);

        var values = new MeshParameterValues();
        var output = new ProceduralOutputBuilder();
        new PanelNetworkMeshFunction(null!).Build(new ProceduralBuildContext(network, values, null!), output);

        ProceduralBuildResult result = output.Build([PanelNetworkMeshFunction.Output], _ => MeshModelFormat.FormatId);
        Assert.AreEqual(1, result.Models.Count);
        ModelAsset built = result.Models[0].Asset;
        Assert.AreEqual(1, built.Surfaces.Count);
        Assert.AreApproximatelyEqual(1.0, built.LocalBounds.Size.X, 1e-4);
        Assert.AreApproximatelyEqual(1.0, built.LocalBounds.Size.Y, 1e-4);
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Polygon_triangulator_handles_a_concave_ngon_correctly()
    {
        // An L-shaped hexagon (concave at (2,2)); a naive vertex-0 fan produces a triangle that pokes
        // outside the shape here, so this is exactly the case that would render/pick wrongly.
        NVector2[] loop =
        [
            new NVector2(0.0f, 0.0f), new NVector2(4.0f, 0.0f), new NVector2(4.0f, 2.0f),
            new NVector2(2.0f, 2.0f), new NVector2(2.0f, 4.0f), new NVector2(0.0f, 4.0f),
        ];

        var triangles = PolygonTriangulator.Triangulate(loop);

        Assert.AreEqual((loop.Length - 2) * 3, triangles.Count, "an n-gon triangulates into exactly n-2 triangles");

        float area = 0.0f;
        for (int i = 0; i < triangles.Count; i += 3)
        {
            NVector2 a = loop[triangles[i]];
            NVector2 b = loop[triangles[i + 1]];
            NVector2 c = loop[triangles[i + 2]];
            area += System.Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) * 0.5f;
        }

        Assert.AreApproximatelyEqual(12.0, area, 1e-3, "the triangles' combined area should match the L-shape's true area, not a fan that overshoots the concave corner");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Main)]
    public static void Panel_network_triangulates_a_pentagon_face_without_overlap()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(2.0f, 2.0f, 0.0f));
        int d = network.AddVertex(new Vector3(1.0f, 3.0f, 0.0f));
        int e = network.AddVertex(new Vector3(0.0f, 2.0f, 0.0f));
        network.AddFace([a, b, c, d, e]);

        var values = new MeshParameterValues();
        var output = new ProceduralOutputBuilder();
        new PanelNetworkMeshFunction(null!).Build(new ProceduralBuildContext(network, values, null!), output);

        ProceduralBuildResult result = output.Build([PanelNetworkMeshFunction.Output], _ => MeshModelFormat.FormatId);
        Assert.AreEqual(1, result.Models.Count);
        ModelAsset built = result.Models[0].Asset;
        Assert.AreEqual(1, built.Surfaces.Count);
        Assert.AreEqual(1, built.Surfaces[0].Mesh.GetSurfaceCount());
        Assert.AreApproximatelyEqual(2.0, built.LocalBounds.Size.X, 1e-4);
        Assert.AreApproximatelyEqual(3.0, built.LocalBounds.Size.Y, 1e-4);
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Main)]
    public static void Tube_network_builds_valid_surface()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);

        var values = new MeshParameterValues();
        values.Set(TubeNetworkMeshFunction.Segments, 6);
        values.Set(TubeNetworkMeshFunction.Radius, 0.5f);
        var output = new ProceduralOutputBuilder();
        new TubeNetworkMeshFunction(null!).Build(new ProceduralBuildContext(network, values, null!), output);

        ProceduralBuildResult result = output.Build([TubeNetworkMeshFunction.Output], _ => MeshModelFormat.FormatId);
        Assert.AreEqual(1, result.Models.Count);
        ModelAsset built = result.Models[0].Asset;
        Assert.AreEqual(1, built.Surfaces.Count);
        Assert.AreEqual(1, built.Surfaces[0].Mesh.GetSurfaceCount());
        Assert.Greater(built.LocalBounds.Size.X, 1.9f);

        // A 6-segment hexagonal cross-section doesn't fill its full 1.0 diameter on every axis: with
        // vertices at 0/60/120/180/240/300 degrees, the Y extent is radius * sqrt(3) ~= 0.866, not 1.0.
        Assert.Greater(built.LocalBounds.Size.Y, 0.85f);
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Main)]
    public static void Fence_network_welds_posts_to_rails_with_correct_winding()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(4.0f, 1.0f, 0.0f));
        network.AddEdge(a, b);

        var values = new MeshParameterValues();
        values.Set(FenceNetworkMeshFunction.PostSpacing, 2.0f);
        values.Set(FenceNetworkMeshFunction.RailCount, 2);
        values.Set(FenceNetworkMeshFunction.RailWaveDetail, 0); // isolate post/weld geometry from waviness subdivision
        var output = new ProceduralOutputBuilder();
        new FenceNetworkMeshFunction(null!).Build(new ProceduralBuildContext(network, values, null!), output);

        ProceduralBuildResult result = output.Build([FenceNetworkMeshFunction.Output], _ => MeshModelFormat.FormatId);
        Assert.AreEqual(1, result.Models.Count);
        ModelAsset built = result.Models[0].Asset;
        Assert.AreEqual(1, built.Surfaces.Count);

        Godot.Collections.Array arrays = built.Surfaces[0].Mesh.SurfaceGetArrays(0);
        var positions = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
        var normals = (Vector3[])arrays[(int)Mesh.ArrayType.Normal];
        var indices = (int[])arrays[(int)Mesh.ArrayType.Index];

        // 3 posts (2 endpoints + 1 filled in at the spacing-implied midpoint), each an open-topped box
        // (5 quads = 20 verts/30 indices) plus a 4-triangle pyramid cap (12 verts/12 indices) = 32/42;
        // and 2 rail spans of 2 rails each — 4 full 6-quad boxes (24 verts/36 indices) apiece.
        Assert.AreEqual((3 * 32) + (4 * 24), positions.Length);
        Assert.AreEqual((3 * 42) + (4 * 36), indices.Length);

        AssertEveryTriangleFacesItsDeclaredNormal(positions, normals, indices);
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Main)]
    public static void Fence_rail_waviness_subdivides_without_moving_endpoints_or_breaking_winding()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(4.0f, 1.0f, 0.0f));
        network.AddEdge(a, b);

        var values = new MeshParameterValues();
        values.Set(FenceNetworkMeshFunction.PostSpacing, 5.0f); // longer than the edge: no filled-in posts
        values.Set(FenceNetworkMeshFunction.RailCount, 1);
        values.Set(FenceNetworkMeshFunction.PostCapHeight, 0.0f); // isolate the rail geometry from the cap
        values.Set(FenceNetworkMeshFunction.RailWaveDetail, 3);
        values.Set(FenceNetworkMeshFunction.RailWaviness, 0.2f);
        var output = new ProceduralOutputBuilder();
        new FenceNetworkMeshFunction(null!).Build(new ProceduralBuildContext(network, values, null!), output);

        ProceduralBuildResult result = output.Build([FenceNetworkMeshFunction.Output], _ => MeshModelFormat.FormatId);
        ModelAsset built = result.Models[0].Asset;
        Godot.Collections.Array arrays = built.Surfaces[0].Mesh.SurfaceGetArrays(0);
        var positions = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
        var normals = (Vector3[])arrays[(int)Mesh.ArrayType.Normal];
        var indices = (int[])arrays[(int)Mesh.ArrayType.Index];

        // 2 flat-topped posts (6-quad box, 24 verts/36 indices), plus one rail built as a single ribbon:
        // 3 subdivisions make 2^3 = 8 segments, each contributing 4 side quads, plus exactly 2 end caps
        // (not one pair per segment — the whole point of a ribbon over a chain of boxes) = 34 quads.
        Assert.AreEqual((2 * 24) + (34 * 4), positions.Length);
        Assert.AreEqual((2 * 36) + (34 * 6), indices.Length);

        // Waviness must never move the rail's actual endpoints — those are what weld it to its posts.
        // The ribbon has no vertex on the centreline, so check that its end ring stays centred on the
        // weld point (every ring corner is centre ± half-width ± half-thickness, so they average to it).
        Vector3 railStart = new(0.0f, 1.0f, 0.0f); // post a's ground point, lifted by the rail height
        Vector3 railEnd = new(4.0f, 2.0f, 0.0f); // post b's ground point, lifted by the rail height
        Assert.IsTrue(EndRingCentre(positions, railStart).IsEqualApprox(railStart), "waviness moved the rail's start away from its post");
        Assert.IsTrue(EndRingCentre(positions, railEnd).IsEqualApprox(railEnd), "waviness moved the rail's end away from its post");

        AssertEveryTriangleFacesItsDeclaredNormal(positions, normals, indices);
    }

    /// <summary>The centre of the ribbon's end ring at a weld point: the four distinct vertex
    /// positions nearest it (its cross-section corners, each = centre ± half-width ± half-thickness),
    /// which average back to the centre exactly when waviness has not disturbed the endpoint. Nearer
    /// than the post's own top corners or the ribbon's next subdivision point.</summary>
    private static Vector3 EndRingCentre(Vector3[] positions, Vector3 weld)
    {
        Vector3[] ring = positions
            .Distinct()
            .OrderBy(p => p.DistanceSquaredTo(weld))
            .Take(4)
            .ToArray();

        Vector3 sum = Vector3.Zero;
        foreach (Vector3 p in ring)
        {
            sum += p;
        }

        return ring.Length == 0 ? new Vector3(float.NaN, float.NaN, float.NaN) : sum / ring.Length;
    }

    /// <summary>Godot's front face is clockwise seen from the front — the same rule LandscapeMeshTests
    /// pins for the terrain grid, generalised here to arbitrarily oriented faces: whichever way a face's
    /// normal points, its winding must satisfy (b-a)x(c-a) anti-parallel to that normal.</summary>
    private static void AssertEveryTriangleFacesItsDeclaredNormal(Vector3[] positions, Vector3[] normals, int[] indices)
    {
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 pa = positions[indices[i]];
            Vector3 pb = positions[indices[i + 1]];
            Vector3 pc = positions[indices[i + 2]];
            Vector3 wound = (pb - pa).Cross(pc - pa);
            Assert.IsTrue(wound.Dot(normals[indices[i]]) < 0.0f,
                $"triangle {i / 3} is wound the wrong way and would be culled from its declared normal side");
        }
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Model_revision_bumps_on_authored_changes_but_not_on_no_ops()
    {
        var model = new ProceduralModel();
        int baseline = model.Revision;

        model.Name = model.Name;
        Assert.AreEqual(baseline, model.Revision, "setting the same value should not bump the revision");

        model.Name = "Changed";
        Assert.AreEqual(baseline + 1, model.Revision);

        model.FunctionId = "other.function";
        model.Parameters = "p";
        model.Formats = "f";
        model.Materials = "m";
        Assert.AreEqual(baseline + 5, model.Revision, "each distinct authored-field write should bump once");

        var network = new VertexNetwork();
        network.AddVertex(Vector3.Zero);
        model.ReplaceNetwork(network);
        Assert.AreEqual(baseline + 6, model.Revision, "ReplaceNetwork should bump the revision too");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Model_content_version_and_fingerprint_are_cached_against_revision()
    {
        var model = new ProceduralModel();
        int version0 = model.ContentVersion;
        string fingerprint0 = model.NetworkFingerprint;

        Assert.AreEqual(version0, model.ContentVersion, "reading twice without a revision bump should hit the cache");

        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);
        model.ReplaceNetwork(network);

        Assert.AreNotEqual(fingerprint0, model.NetworkFingerprint, "a network replacement should invalidate the cached fingerprint");
        Assert.AreNotEqual(version0, model.ContentVersion, "a network replacement should invalidate the cached content version");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Main)]
    public static void Two_placements_of_one_model_share_bounds_content_version_and_the_build_cache()
    {
        EditorContext context = NewContext("__wms_procedural_model_sharing_test__");
        ProceduralSystem system = context.Procedural;
        ProceduralModel model = NewTubeModel(context, id: 1);

        var entityA = new MapSceneEntity();
        var entityB = new MapSceneEntity();
        var componentA = new ProceduralComponent(system) { ModelId = model.RecordId };
        var componentB = new ProceduralComponent(system) { ModelId = model.RecordId };
        entityA.AddComponent(componentA);
        entityB.AddComponent(componentB);

        Assert.AreEqual(componentA.ContentVersion, componentB.ContentVersion);
        Assert.IsTrue(componentA.LocalBounds.Size.IsEqualApprox(componentB.LocalBounds.Size));

        ProceduralBuildResult builtA = system.Build(model);
        ProceduralBuildResult builtB = system.Build(model);
        Assert.IsTrue(ReferenceEquals(builtA, builtB), "one model should build once and be shared by every placement");

        int revisionBefore = model.Revision;
        VertexNetwork moved = model.Network.Clone();
        moved.MoveVertex(moved.Vertices[1].Id, new Vector3(5.0f, 0.0f, 0.0f));
        model.ReplaceNetwork(moved);
        Assert.Greater(model.Revision, revisionBefore);

        ProceduralBuildResult builtAfterEdit = system.Build(model);
        Assert.IsFalse(ReferenceEquals(builtA, builtAfterEdit), "a revision bump should invalidate the cached build");
        Assert.AreEqual(componentA.ContentVersion, componentB.ContentVersion, "both placements should still agree after the shared model changed");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Main)]
    public static void System_update_refreshes_every_other_placement_when_one_edits_the_shared_model()
    {
        EditorContext context = NewContext("__wms_procedural_model_update_test__");
        ProceduralSystem system = context.Procedural;
        ProceduralModel model = NewTubeModel(context, id: 1);

        var entityA = new MapSceneEntity();
        var entityB = new MapSceneEntity();
        entityA.AddComponent(new ProceduralComponent(system) { ModelId = model.RecordId });
        entityB.AddComponent(new ProceduralComponent(system) { ModelId = model.RecordId });
        context.Scene.Add(entityA);
        context.Scene.Add(entityB);
        entityA.CreateRepresentation(context.Root);
        entityB.CreateRepresentation(context.Root);

        var componentA = entityA.Component<ProceduralComponent>()!;
        var componentB = entityB.Component<ProceduralComponent>()!;
        system.Update();
        Assert.IsFalse(componentA.NeedsRefresh);
        Assert.IsFalse(componentB.NeedsRefresh);

        VertexNetwork moved = model.Network.Clone();
        moved.MoveVertex(moved.Vertices[1].Id, new Vector3(5.0f, 0.0f, 0.0f));
        componentA.ReplaceNetwork(moved);

        Assert.IsFalse(componentA.NeedsRefresh, "editing through a placement should rebuild its own representation immediately");
        Assert.IsTrue(componentB.NeedsRefresh, "the other placement has not rebuilt yet");

        system.Update();
        Assert.IsFalse(componentB.NeedsRefresh, "the guarded sweep should have refreshed every other placement");
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Main)]
    public static void Set_network_command_pins_the_model_and_emits_one_chunk_impact_per_placement()
    {
        EditorContext context = NewContext("__wms_procedural_model_setnetwork_test__");
        ProceduralSystem system = context.Procedural;
        ProceduralModel model = NewTubeModel(context, id: 1);

        var entityA = new MapSceneEntity { Map = new MapId(1) };
        var entityB = new MapSceneEntity { Map = new MapId(1) };
        var componentA = new ProceduralComponent(system) { ModelId = model.RecordId };
        entityA.AddComponent(componentA);
        entityB.AddComponent(new ProceduralComponent(system) { ModelId = model.RecordId });
        context.Scene.Add(entityA);
        context.Scene.Add(entityB);

        VertexNetwork before = model.Network.Clone();
        VertexNetwork after = before.Clone();
        after.MoveVertex(after.Vertices[1].Id, new Vector3(9.0f, 0.0f, 0.0f));

        var command = new SetNetworkCommand(componentA, before, after, "Move network vertices");

        Assert.AreEqual(1, command.Targets.Count);
        Assert.IsTrue(ReferenceEquals(model, command.Targets[0]), "the command should pin the model, not the placement entity");
        Assert.AreEqual(2, command.ChunkImpacts.Count, "one impact per loaded entity referencing the model");

        command.Apply();
        Assert.IsTrue(model.Network.Vertex(after.Vertices[1].Id)!.Position.IsEqualApprox(new Vector3(9.0f, 0.0f, 0.0f)));

        command.Revert();
        Assert.IsTrue(model.Network.Vertex(before.Vertices[1].Id)!.Position.IsEqualApprox(before.Vertices[1].Position));
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Main)]
    public static void Dangling_model_id_builds_an_empty_node_instead_of_throwing()
    {
        EditorContext context = NewContext("__wms_procedural_model_dangling_test__");
        var component = new ProceduralComponent(context.Procedural) { ModelId = 999 };
        var entity = new MapSceneEntity();
        entity.AddComponent(component);

        Node3D node = component.BuildNode();
        Assert.AreEqual(0, node.GetChildCount());
    }

    private static EditorContext NewContext(string name) =>
        new(new Node3D(), new Project { Name = name });

    private static ProceduralModel NewTubeModel(EditorContext context, int id)
    {
        var model = new ProceduralModel { RecordId = id, Name = $"Model {id}" };
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);
        model.ReplaceNetwork(network);
        context.Catalog.Add(model);
        return model;
    }
}
