using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace WorldMapStudio;

public sealed record NetworkVertex(int Id, Vector3 Position);

public sealed record NetworkEdge(int Id, int A, int B);

/// <summary>
/// A plain vertex/edge graph in local space, with no notion of what it represents — a procedural
/// mesh's skeleton and a road's centreline are both just this. Shared so their editing tool, undo
/// command and serialization are shared too.
/// </summary>
public sealed class VertexNetwork
{
    private readonly List<NetworkVertex> _vertices = [];
    private readonly List<NetworkEdge> _edges = [];
    private int _nextVertexId = 1;
    private int _nextEdgeId = 1;
    private int _version;

    public IReadOnlyList<NetworkVertex> Vertices => _vertices;

    public IReadOnlyList<NetworkEdge> Edges => _edges;

    public int Version => _version;

    public int AddVertex(Vector3 position)
    {
        int id = _nextVertexId++;
        _vertices.Add(new NetworkVertex(id, position));
        _version++;
        return id;
    }

    public int? AddEdge(int a, int b)
    {
        if (a == b || Vertex(a) == null || Vertex(b) == null || HasEdge(a, b))
        {
            return null;
        }

        int id = _nextEdgeId++;
        _edges.Add(new NetworkEdge(id, a, b));
        _version++;
        return id;
    }

    public bool RemoveVertex(int id)
    {
        int removed = _vertices.RemoveAll(vertex => vertex.Id == id);
        if (removed == 0)
        {
            return false;
        }

        _edges.RemoveAll(edge => edge.A == id || edge.B == id);
        _version++;
        return true;
    }

    public bool RemoveEdge(int id)
    {
        if (_edges.RemoveAll(edge => edge.Id == id) == 0)
        {
            return false;
        }

        _version++;
        return true;
    }

    public bool MoveVertex(int id, Vector3 position)
    {
        int index = _vertices.FindIndex(vertex => vertex.Id == id);
        if (index < 0)
        {
            return false;
        }

        _vertices[index] = _vertices[index] with { Position = position };
        _version++;
        return true;
    }

    public int? SplitEdge(int edgeId)
    {
        NetworkEdge? edge = Edge(edgeId);
        NetworkVertex? a = edge == null ? null : Vertex(edge.A);
        NetworkVertex? b = edge == null ? null : Vertex(edge.B);
        if (edge == null || a == null || b == null)
        {
            return null;
        }

        RemoveEdge(edge.Id);
        int middle = AddVertex((a.Position + b.Position) * 0.5f);
        AddEdge(a.Id, middle);
        AddEdge(middle, b.Id);
        return middle;
    }

    public int? MergeVertices(IEnumerable<int> vertexIds)
    {
        int[] ids = vertexIds.Distinct().Where(id => Vertex(id) != null).ToArray();
        if (ids.Length == 0)
        {
            return null;
        }

        Vector3 position = Vector3.Zero;
        foreach (int id in ids)
        {
            position += Vertex(id)!.Position;
        }

        position /= ids.Length;
        int kept = ids[0];
        MoveVertex(kept, position);
        HashSet<int> removed = ids.Skip(1).ToHashSet();
        for (int i = 0; i < _edges.Count; i++)
        {
            NetworkEdge edge = _edges[i];
            int a = removed.Contains(edge.A) ? kept : edge.A;
            int b = removed.Contains(edge.B) ? kept : edge.B;
            _edges[i] = edge with { A = a, B = b };
        }

        _vertices.RemoveAll(vertex => removed.Contains(vertex.Id));
        RemoveInvalidAndDuplicateEdges();
        _version++;
        return kept;
    }

    public IReadOnlyList<int> DuplicateSubgraph(IEnumerable<int> vertexIds, IEnumerable<int> edgeIds, Vector3 offset)
    {
        HashSet<int> selectedVertices = vertexIds.ToHashSet();
        foreach (int edgeId in edgeIds)
        {
            if (Edge(edgeId) is { } edge)
            {
                selectedVertices.Add(edge.A);
                selectedVertices.Add(edge.B);
            }
        }

        var map = new Dictionary<int, int>();
        foreach (int oldId in selectedVertices)
        {
            if (Vertex(oldId) is { } vertex)
            {
                map[oldId] = AddVertex(vertex.Position + offset);
            }
        }

        foreach (NetworkEdge edge in _edges.ToArray())
        {
            if (map.TryGetValue(edge.A, out int a) && map.TryGetValue(edge.B, out int b))
            {
                AddEdge(a, b);
            }
        }

        return map.Values.ToList();
    }

    public IReadOnlyList<int> Extrude(IEnumerable<int> vertexIds, IEnumerable<int> edgeIds, Vector3 offset)
    {
        HashSet<int> source = vertexIds.ToHashSet();
        foreach (int edgeId in edgeIds)
        {
            if (Edge(edgeId) is { } edge)
            {
                source.Add(edge.A);
                source.Add(edge.B);
            }
        }

        var map = new Dictionary<int, int>();
        foreach (int id in source)
        {
            if (Vertex(id) is { } vertex)
            {
                map[id] = AddVertex(vertex.Position + offset);
            }
        }

        foreach ((int oldId, int newId) in map)
        {
            AddEdge(oldId, newId);
        }

        foreach (int edgeId in edgeIds)
        {
            if (Edge(edgeId) is { } edge && map.TryGetValue(edge.A, out int a) && map.TryGetValue(edge.B, out int b))
            {
                AddEdge(a, b);
            }
        }

        return map.Values.ToList();
    }

    public NetworkVertex? Vertex(int id) => _vertices.FirstOrDefault(vertex => vertex.Id == id);

    public NetworkEdge? Edge(int id) => _edges.FirstOrDefault(edge => edge.Id == id);

    public bool HasEdge(int a, int b) =>
        _edges.Any(edge => (edge.A == a && edge.B == b) || (edge.A == b && edge.B == a));

    public int ConnectedGraphCount()
    {
        if (_vertices.Count == 0)
        {
            return 0;
        }

        Dictionary<int, List<int>> adjacency = BuildAdjacency();
        HashSet<int> visited = [];
        int count = 0;
        foreach (NetworkVertex vertex in _vertices)
        {
            if (!visited.Add(vertex.Id))
            {
                continue;
            }

            count++;
            var stack = new Stack<int>();
            stack.Push(vertex.Id);
            while (stack.Count > 0)
            {
                int current = stack.Pop();
                foreach (int next in adjacency[current])
                {
                    if (visited.Add(next))
                    {
                        stack.Push(next);
                    }
                }
            }
        }

        return count;
    }

    public bool HasBranches() => BuildAdjacency().Values.Any(neighbours => neighbours.Count > 2);

    /// <summary>Checks this network's topology against a consumer's requirements — e.g. a procedural
    /// mesh function that only knows how to build along a single, unbranched run of edges.</summary>
    public IReadOnlyList<string> ValidateFor(string displayName, bool allowsMultipleGraphs, bool allowsBranching)
    {
        var problems = new List<string>();
        int graphs = ConnectedGraphCount();
        if (!allowsMultipleGraphs && graphs > 1)
        {
            problems.Add($"{displayName} accepts only one connected graph, but this network has {graphs}.");
        }

        if (!allowsBranching && HasBranches())
        {
            problems.Add($"{displayName} accepts only linear graphs; one or more vertices have more than two connected edges.");
        }

        return problems;
    }

    public Aabb Bounds()
    {
        if (_vertices.Count == 0)
        {
            return new Aabb(-Vector3.One * 0.5f, Vector3.One);
        }

        Vector3 min = _vertices[0].Position;
        Vector3 max = _vertices[0].Position;
        foreach (NetworkVertex vertex in _vertices.Skip(1))
        {
            min = new Vector3(Mathf.Min(min.X, vertex.Position.X), Mathf.Min(min.Y, vertex.Position.Y), Mathf.Min(min.Z, vertex.Position.Z));
            max = new Vector3(Mathf.Max(max.X, vertex.Position.X), Mathf.Max(max.Y, vertex.Position.Y), Mathf.Max(max.Z, vertex.Position.Z));
        }

        Vector3 size = max - min;
        if (size.LengthSquared() <= 0.0001f)
        {
            min -= Vector3.One * 0.5f;
            size = Vector3.One;
        }

        return new Aabb(min, size);
    }

    public VertexNetwork Clone() => Parse(Serialize());

    public string Fingerprint() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize()))).ToLowerInvariant();

    public string Serialize()
    {
        var dto = new NetworkDto
        {
            NextVertexId = _nextVertexId,
            NextEdgeId = _nextEdgeId,
            Vertices = _vertices.Select(v => new VertexDto { Id = v.Id, X = v.Position.X, Y = v.Position.Y, Z = v.Position.Z }).ToList(),
            Edges = _edges.Select(e => new EdgeDto { Id = e.Id, A = e.A, B = e.B }).ToList(),
        };
        return JsonSerializer.Serialize(dto);
    }

    public static VertexNetwork Parse(string serialized)
    {
        var network = new VertexNetwork();
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return network;
        }

        try
        {
            NetworkDto? dto = JsonSerializer.Deserialize<NetworkDto>(serialized);
            if (dto == null)
            {
                return network;
            }

            network._vertices.AddRange(dto.Vertices.Select(v => new NetworkVertex(v.Id, new Vector3(v.X, v.Y, v.Z))));
            network._edges.AddRange(dto.Edges.Select(e => new NetworkEdge(e.Id, e.A, e.B)));
            network._nextVertexId = Math.Max(dto.NextVertexId, network._vertices.Select(v => v.Id).DefaultIfEmpty().Max() + 1);
            network._nextEdgeId = Math.Max(dto.NextEdgeId, network._edges.Select(e => e.Id).DefaultIfEmpty().Max() + 1);
            network.RemoveInvalidAndDuplicateEdges();
            network._version++;
        }
        catch (JsonException)
        {
        }

        return network;
    }

    private void RemoveInvalidAndDuplicateEdges()
    {
        HashSet<int> vertices = _vertices.Select(vertex => vertex.Id).ToHashSet();
        var seen = new HashSet<(int, int)>();
        _edges.RemoveAll(edge =>
        {
            if (edge.A == edge.B || !vertices.Contains(edge.A) || !vertices.Contains(edge.B))
            {
                return true;
            }

            (int, int) key = edge.A < edge.B ? (edge.A, edge.B) : (edge.B, edge.A);
            return !seen.Add(key);
        });
    }

    private Dictionary<int, List<int>> BuildAdjacency()
    {
        var adjacency = _vertices.ToDictionary(vertex => vertex.Id, _ => new List<int>());
        foreach (NetworkEdge edge in _edges)
        {
            if (!adjacency.ContainsKey(edge.A) || !adjacency.ContainsKey(edge.B))
            {
                continue;
            }

            adjacency[edge.A].Add(edge.B);
            adjacency[edge.B].Add(edge.A);
        }

        return adjacency;
    }

    private sealed class NetworkDto
    {
        public int NextVertexId { get; set; } = 1;
        public int NextEdgeId { get; set; } = 1;
        public List<VertexDto> Vertices { get; set; } = [];
        public List<EdgeDto> Edges { get; set; } = [];
    }

    private sealed class VertexDto
    {
        public int Id { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    private sealed class EdgeDto
    {
        public int Id { get; set; }
        public int A { get; set; }
        public int B { get; set; }
    }
}
