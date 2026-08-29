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

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Background)]
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
        model.FormatId = "f";
        model.Materials = "m";
        Assert.AreEqual(baseline + 5, model.Revision, "each distinct authored-field write should bump once");

        var network = new VertexNetwork();
        network.AddVertex(Vector3.Zero);
        model.ReplaceNetwork(network);
        Assert.AreEqual(baseline + 6, model.Revision, "ReplaceNetwork should bump the revision too");
    }

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Background)]
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

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Main)]
    public static void Two_placements_of_one_model_share_bounds_content_version_and_the_build_cache()
    {
        EditorContext context = NewContext("__wms_procedural_model_sharing_test__");
        ProceduralMeshSystem system = context.ProceduralMeshes;
        ProceduralModel model = NewTubeModel(context, id: 1);

        var entityA = new SceneEntity();
        var entityB = new SceneEntity();
        var componentA = new ProceduralMeshComponent(system) { ModelId = model.RecordId };
        var componentB = new ProceduralMeshComponent(system) { ModelId = model.RecordId };
        entityA.AddComponent(componentA);
        entityB.AddComponent(componentB);

        Assert.AreEqual(componentA.ContentVersion, componentB.ContentVersion);
        Assert.IsTrue(componentA.LocalBounds.Size.IsEqualApprox(componentB.LocalBounds.Size));

        ModelAsset builtA = system.Build(model);
        ModelAsset builtB = system.Build(model);
        Assert.IsTrue(ReferenceEquals(builtA, builtB), "one model should build once and be shared by every placement");

        int revisionBefore = model.Revision;
        VertexNetwork moved = model.Network.Clone();
        moved.MoveVertex(moved.Vertices[1].Id, new Vector3(5.0f, 0.0f, 0.0f));
        model.ReplaceNetwork(moved);
        Assert.Greater(model.Revision, revisionBefore);

        ModelAsset builtAfterEdit = system.Build(model);
        Assert.IsFalse(ReferenceEquals(builtA, builtAfterEdit), "a revision bump should invalidate the cached build");
        Assert.AreEqual(componentA.ContentVersion, componentB.ContentVersion, "both placements should still agree after the shared model changed");
    }

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Main)]
    public static void System_update_refreshes_every_other_placement_when_one_edits_the_shared_model()
    {
        EditorContext context = NewContext("__wms_procedural_model_update_test__");
        ProceduralMeshSystem system = context.ProceduralMeshes;
        ProceduralModel model = NewTubeModel(context, id: 1);

        var entityA = new SceneEntity();
        var entityB = new SceneEntity();
        entityA.AddComponent(new ProceduralMeshComponent(system) { ModelId = model.RecordId });
        entityB.AddComponent(new ProceduralMeshComponent(system) { ModelId = model.RecordId });
        context.Scene.Add(entityA);
        context.Scene.Add(entityB);
        entityA.CreateRepresentation(context.Root);
        entityB.CreateRepresentation(context.Root);

        var componentA = entityA.Component<ProceduralMeshComponent>()!;
        var componentB = entityB.Component<ProceduralMeshComponent>()!;
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

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Main)]
    public static void Set_network_command_pins_the_model_and_emits_one_chunk_impact_per_placement()
    {
        EditorContext context = NewContext("__wms_procedural_model_setnetwork_test__");
        ProceduralMeshSystem system = context.ProceduralMeshes;
        ProceduralModel model = NewTubeModel(context, id: 1);

        var entityA = new SceneEntity { Map = new MapId(1) };
        var entityB = new SceneEntity { Map = new MapId(1) };
        var componentA = new ProceduralMeshComponent(system) { ModelId = model.RecordId };
        entityA.AddComponent(componentA);
        entityB.AddComponent(new ProceduralMeshComponent(system) { ModelId = model.RecordId });
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

    [EditorTest(Category = "ProceduralMesh", Thread = TestThread.Main)]
    public static void Dangling_model_id_renders_the_placeholder_instead_of_throwing()
    {
        EditorContext context = NewContext("__wms_procedural_model_dangling_test__");
        var component = new ProceduralMeshComponent(context.ProceduralMeshes) { ModelId = 999 };
        var entity = new SceneEntity();
        entity.AddComponent(component);

        Node3D node = component.BuildNode();
        Assert.IsNotNull(node.GetNodeOrNull("MissingProceduralMesh"));
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
