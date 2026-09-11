using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>Covers <see cref="VertexNetwork.OrderedChainFrom"/> and <see cref="VertexNetwork.Endpoints"/>,
/// the graph maths a stored node order (e.g. a taxi path's direction) is reconciled against.</summary>
public static class VertexNetworkChainTests
{
    [EditorTest(Category = "Network", Thread = TestThread.Background)]
    public static void OrderedChainFrom_walks_an_open_chain_to_its_far_end()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);
        network.AddEdge(b, c);

        IReadOnlyList<int>? order = network.OrderedChainFrom(a, b);

        Assert.IsNotNull(order);
        Assert.IsTrue(order!.SequenceEqual(new[] { a, b, c }));
        Assert.IsTrue(network.Endpoints().OrderBy(id => id).SequenceEqual(new[] { a, c }.OrderBy(id => id)));
    }

    [EditorTest(Category = "Network", Thread = TestThread.Background)]
    public static void OrderedChainFrom_walks_a_closed_loop_back_to_start_without_repeating_it()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(1.0f, 0.0f, 1.0f));
        network.AddEdge(a, b);
        network.AddEdge(b, c);
        network.AddEdge(c, a);

        IReadOnlyList<int>? order = network.OrderedChainFrom(a, b);

        Assert.IsNotNull(order);
        Assert.IsTrue(order!.SequenceEqual(new[] { a, b, c }));
        Assert.AreEqual(0, network.Endpoints().Count, "a closed loop has no degree-1 vertex");
    }

    [EditorTest(Category = "Network", Thread = TestThread.Background)]
    public static void OrderedChainFrom_returns_null_for_a_branch()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        int c = network.AddVertex(new Vector3(2.0f, 0.0f, 0.0f));
        int d = network.AddVertex(new Vector3(1.0f, 0.0f, 1.0f));
        network.AddEdge(a, b);
        network.AddEdge(b, c);
        network.AddEdge(b, d);

        Assert.IsNull(network.OrderedChainFrom(a, b));
    }

    [EditorTest(Category = "Network", Thread = TestThread.Background)]
    public static void OrderedChainFrom_returns_null_when_an_anchor_is_missing()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);

        Assert.IsNull(network.OrderedChainFrom(a, 12345));
        Assert.IsNull(network.OrderedChainFrom(12345, a));
    }
}
