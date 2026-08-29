using Godot;

namespace WorldMapStudio;

public static class ProceduralMeshTests
{
    private sealed class SingleLinearFunction : IProceduralMeshFunction
    {
        public string Id => "test.single_linear";
        public string DisplayName => "Single Linear";
        public string Description => "";
        public int Version => 1;
        public bool AllowsMultipleGraphs => false;
        public bool AllowsBranching => false;
        public float Priority => 0f;
        public System.Collections.Generic.IReadOnlyList<MeshParameter> Parameters { get; } = [];
        public void Build(in ProceduralMeshBuildContext context, ProceduralMeshOutputBuilder output) { }
    }

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Background)]
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

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Background)]
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

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Background)]
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

        var duplicated = network.DuplicateSubgraph([kept], [], new Vector3(0.0f, 1.0f, 0.0f));
        Assert.AreEqual(1, duplicated.Count);
        Assert.AreEqual(3, network.Vertices.Count);

        var extruded = network.Extrude(duplicated, [], new Vector3(0.0f, 0.0f, 1.0f));
        Assert.AreEqual(1, extruded.Count);
        Assert.IsTrue(network.Edges.Count >= 2, "extrusion should connect old and new vertices");
    }

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Background)]
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

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Background)]
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
        var problems = network.ValidateFor(function.DisplayName, function.AllowsMultipleGraphs, function.AllowsBranching);
        Assert.AreEqual(2, problems.Count);
        Assert.IsTrue(problems[0].Contains("only one connected graph"));
        Assert.IsTrue(problems[1].Contains("only linear graphs"));
        Assert.IsNotNull(network.Vertex(separate));
    }

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Main)]
    public static void Tube_network_builds_valid_surface()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);

        var values = new MeshParameterValues();
        values.Set(TubeNetworkMeshFunction.Segments, 6);
        values.Set(TubeNetworkMeshFunction.Radius, 0.5f);
        var output = new ProceduralMeshOutputBuilder();
        new TubeNetworkMeshFunction(null!).Build(new ProceduralMeshBuildContext(network, values, null!), output);

        ModelAsset built = output.Build(MeshModelFormat.FormatId);
        Assert.AreEqual(1, built.Surfaces.Count);
        Assert.AreEqual(1, built.Surfaces[0].Mesh.GetSurfaceCount());
        Assert.Greater(built.LocalBounds.Size.X, 1.9f);
        Assert.Greater(built.LocalBounds.Size.Y, 0.9f);
    }
}
