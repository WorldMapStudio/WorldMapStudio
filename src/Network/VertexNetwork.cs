using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace WorldMapStudio;

/// <summary>A vertex in local space. <see cref="StickToTerrain"/> marks it as not authoring its own
/// height at all — the editing tool locks its Y on every move and displays it dropped onto the
/// terrain under it, for vertices (a road's centreline, a mesh's foundation line) whose real height
/// should always come from the ground rather than from anything the user drags.</summary>
public sealed record NetworkVertex(int Id, Vector3 Position, bool StickToTerrain = false);

public sealed record NetworkEdge(int Id, int A, int B);

/// <summary>An ordered loop of at least 3 vertex ids — an n-gon, not pre-triangulated, so a
/// consuming function decides how to triangulate it (same freedom the flat index buffer already
/// gives <see cref="ProceduralOutputBuilder.AddSurface(ProceduralOutputSlot, string, IReadOnlyList{Vector3}, IReadOnlyList{int}, MeshMaterial, IReadOnlyList{Vector3}, IReadOnlyList{Vector2})"/>).</summary>
public sealed record NetworkFace(int Id, IReadOnlyList<int> Vertices);

/// <summary>The vertex/face outcome of duplicating or extruding a subgraph — a selected face keeps
/// being a face at the new vertices, rather than degrading into loose vertices/edges.</summary>
public sealed record NetworkSubgraph(IReadOnlyList<int> VertexIds, IReadOnlyList<int> FaceIds);

/// <summary>
/// A plain vertex/edge/face graph in local space, with no notion of what it represents — a procedural
/// mesh's skeleton and a road's centreline are both just this. Shared so their editing tool, undo
/// command and serialization are shared too.
/// </summary>
public sealed class VertexNetwork
{
    private readonly List<NetworkVertex> _vertices = [];
    private readonly List<NetworkEdge> _edges = [];
    private readonly List<NetworkFace> _faces = [];
    private int _nextVertexId = 1;
    private int _nextEdgeId = 1;
    private int _nextFaceId = 1;
    private int _version;

    public IReadOnlyList<NetworkVertex> Vertices => _vertices;

    public IReadOnlyList<NetworkEdge> Edges => _edges;

    public IReadOnlyList<NetworkFace> Faces => _faces;

    public int Version => _version;

    public int AddVertex(Vector3 position, bool stickToTerrain = false)
    {
        int id = _nextVertexId++;
        _vertices.Add(new NetworkVertex(id, position, stickToTerrain));
        _version++;
        return id;
    }

    /// <summary>
    /// Builds a network in one pass from a generator's vertex positions and faces (0-based indices into
    /// that list), synthesising each face loop's boundary edges. Skips the per-call topology checks
    /// <see cref="AddVertex"/>/<see cref="AddFace"/> run for interactive editing — <see cref="AddFace"/>
    /// scans every existing face and edge, so adding faces one by one is O(n²) and a mesh-scale network
    /// (thousands of faces) hangs. The caller owns validity: distinct positions, loops of 3+ in-range
    /// distinct indices, no duplicate faces. Vertex ids are <c>index + 1</c>.
    /// </summary>
    public static VertexNetwork FromMesh(IReadOnlyList<Vector3> vertices, IReadOnlyList<IReadOnlyList<int>> faces)
    {
        var network = new VertexNetwork();
        for (int i = 0; i < vertices.Count; i++)
        {
            network._vertices.Add(new NetworkVertex(i + 1, vertices[i]));
        }

        network._nextVertexId = vertices.Count + 1;

        var edgeKeys = new HashSet<(int, int)>();
        foreach (IReadOnlyList<int> loop in faces)
        {
            if (loop.Count < 3)
            {
                continue;
            }

            var ids = new int[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                ids[i] = loop[i] + 1;
            }

            network._faces.Add(new NetworkFace(network._nextFaceId++, ids));

            for (int i = 0; i < ids.Length; i++)
            {
                int a = ids[i];
                int b = ids[(i + 1) % ids.Length];
                (int, int) key = a < b ? (a, b) : (b, a);
                if (edgeKeys.Add(key))
                {
                    network._edges.Add(new NetworkEdge(network._nextEdgeId++, a, b));
                }
            }
        }

        network._version++;
        return network;
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

    /// <summary>Adds a face from an ordered loop of at least 3 distinct, existing vertices, creating
    /// any missing boundary edges (consecutive pairs, wrapping last→first) so the face's boundary is
    /// always backed by real edges. Rejects a loop that duplicates an existing face's vertex set,
    /// regardless of starting point or winding direction.</summary>
    public int? AddFace(IReadOnlyList<int> vertexIds)
    {
        int[] ids = vertexIds.Distinct().ToArray();
        if (ids.Length < 3 || ids.Any(id => Vertex(id) == null))
        {
            return null;
        }

        HashSet<int> shape = ids.ToHashSet();
        if (_faces.Any(face => face.Vertices.Count == shape.Count && face.Vertices.ToHashSet().SetEquals(shape)))
        {
            return null;
        }

        for (int i = 0; i < ids.Length; i++)
        {
            AddEdge(ids[i], ids[(i + 1) % ids.Length]);
        }

        int id = _nextFaceId++;
        _faces.Add(new NetworkFace(id, ids));
        _version++;
        return id;
    }

    public bool RemoveFace(int id)
    {
        if (_faces.RemoveAll(face => face.Id == id) == 0)
        {
            return false;
        }

        _version++;
        return true;
    }

    public bool RemoveVertex(int id)
    {
        int removed = _vertices.RemoveAll(vertex => vertex.Id == id);
        if (removed == 0)
        {
            return false;
        }

        _edges.RemoveAll(edge => edge.A == id || edge.B == id);
        _faces.RemoveAll(face => face.Vertices.Contains(id));
        _version++;
        return true;
    }

    public bool RemoveEdge(int id)
    {
        NetworkEdge? edge = Edge(id);
        if (edge == null || _edges.RemoveAll(e => e.Id == id) == 0)
        {
            return false;
        }

        _faces.RemoveAll(face => FaceUsesEdge(face, edge.A, edge.B));
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

    public bool SetStickToTerrain(int id, bool stickToTerrain)
    {
        int index = _vertices.FindIndex(vertex => vertex.Id == id);
        if (index < 0)
        {
            return false;
        }

        _vertices[index] = _vertices[index] with { StickToTerrain = stickToTerrain };
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
        int middle = AddVertex((a.Position + b.Position) * 0.5f, a.StickToTerrain && b.StickToTerrain);
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

        for (int i = 0; i < _faces.Count; i++)
        {
            NetworkFace face = _faces[i];
            int[] remapped = face.Vertices.Select(v => removed.Contains(v) ? kept : v).ToArray();
            int[] collapsed = CollapseConsecutiveDuplicates(remapped);
            _faces[i] = face with { Vertices = collapsed };
        }

        _vertices.RemoveAll(vertex => removed.Contains(vertex.Id));
        RemoveInvalidAndDuplicateEdges();
        RemoveInvalidAndDuplicateFaces();
        _version++;
        return kept;
    }

    public NetworkSubgraph DuplicateSubgraph(IEnumerable<int> vertexIds, IEnumerable<int> edgeIds, IEnumerable<int> faceIds, Vector3 offset)
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

        NetworkFace[] selectedFaces = faceIds.Select(Face).OfType<NetworkFace>().ToArray();
        foreach (NetworkFace face in selectedFaces)
        {
            foreach (int v in face.Vertices)
            {
                selectedVertices.Add(v);
            }
        }

        var map = new Dictionary<int, int>();
        foreach (int oldId in selectedVertices)
        {
            if (Vertex(oldId) is { } vertex)
            {
                map[oldId] = AddVertex(vertex.Position + offset, vertex.StickToTerrain);
            }
        }

        foreach (NetworkEdge edge in _edges.ToArray())
        {
            if (map.TryGetValue(edge.A, out int a) && map.TryGetValue(edge.B, out int b))
            {
                AddEdge(a, b);
            }
        }

        var newFaces = new List<int>();
        foreach (NetworkFace face in selectedFaces)
        {
            if (face.Vertices.All(map.ContainsKey) && AddFace(face.Vertices.Select(v => map[v]).ToArray()) is int faceId)
            {
                newFaces.Add(faceId);
            }
        }

        return new NetworkSubgraph(map.Values.ToList(), newFaces);
    }

    /// <summary>Offsets a selection into a new, connected copy: new vertices linked to their sources
    /// by fresh edges. A selected face's cap moves to the new vertices (keeping its id) rather than
    /// leaving a duplicate behind, and a new quad side wall fills the gap along every edge on the
    /// selection's outer boundary — an edge shared by two selected faces is interior and gets no wall,
    /// matching Blender's region extrude. Loose selected vertices/edges (no face) get only the
    /// connecting edges, exactly as before.</summary>
    public NetworkSubgraph Extrude(IEnumerable<int> vertexIds, IEnumerable<int> edgeIds, IEnumerable<int> faceIds, Vector3 offset)
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

        NetworkFace[] selectedFaces = faceIds.Select(Face).OfType<NetworkFace>().ToArray();
        foreach (NetworkFace face in selectedFaces)
        {
            foreach (int v in face.Vertices)
            {
                source.Add(v);
            }
        }

        var map = new Dictionary<int, int>();
        foreach (int id in source)
        {
            if (Vertex(id) is { } vertex)
            {
                map[id] = AddVertex(vertex.Position + offset, vertex.StickToTerrain);
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

        var newFaces = new List<int>();
        if (selectedFaces.Length > 0)
        {
            var boundaryUseCount = new Dictionary<(int, int), int>();
            foreach (NetworkFace face in selectedFaces)
            {
                for (int i = 0; i < face.Vertices.Count; i++)
                {
                    (int, int) key = BoundaryKey(face.Vertices[i], face.Vertices[(i + 1) % face.Vertices.Count]);
                    boundaryUseCount[key] = boundaryUseCount.GetValueOrDefault(key) + 1;
                }
            }

            foreach (NetworkFace face in selectedFaces)
            {
                for (int i = 0; i < face.Vertices.Count; i++)
                {
                    int a = face.Vertices[i];
                    int b = face.Vertices[(i + 1) % face.Vertices.Count];
                    if (boundaryUseCount[BoundaryKey(a, b)] == 1 && map.TryGetValue(a, out int na) && map.TryGetValue(b, out int nb))
                    {
                        if (AddFace([a, b, nb, na]) is int wallId)
                        {
                            newFaces.Add(wallId);
                        }
                    }
                }

                if (face.Vertices.All(map.ContainsKey) && MoveFace(face.Id, face.Vertices.Select(v => map[v]).ToArray()))
                {
                    newFaces.Add(face.Id);
                }
            }
        }

        return new NetworkSubgraph(map.Values.ToList(), newFaces);
    }

    private static (int, int) BoundaryKey(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>Repoints an existing face at a different vertex loop, keeping its id — used to "move"
    /// a face's cap during an extrude rather than creating a new one at the offset position.</summary>
    private bool MoveFace(int id, IReadOnlyList<int> vertices)
    {
        int index = _faces.FindIndex(face => face.Id == id);
        if (index < 0)
        {
            return false;
        }

        _faces[index] = _faces[index] with { Vertices = vertices };
        _version++;
        return true;
    }

    /// <summary>Blender's Subdivide: splits every selected edge at its midpoint (patching any face
    /// that used it so the face keeps referencing real edges instead of losing them), then replaces
    /// any face whose <em>entire</em> boundary ended up subdivided with a fan of quads around a new
    /// centre vertex — the default "cut a quad into 4 quads" behaviour, generalized to any n-gon. A
    /// face with only some of its edges selected keeps its shape (now an n-gon with extra vertices
    /// along the cut edges) rather than attempting a partial tessellation.</summary>
    public NetworkSubgraph Subdivide(IEnumerable<int> edgeIds, IEnumerable<int> faceIds)
    {
        HashSet<int> selectedEdges = edgeIds.ToHashSet();
        var targets = new HashSet<int>(faceIds);
        foreach (NetworkFace face in _faces)
        {
            if (targets.Contains(face.Id) || face.Vertices.Count < 3)
            {
                continue;
            }

            bool everyBoundaryEdgeSelected = true;
            for (int i = 0; i < face.Vertices.Count; i++)
            {
                NetworkEdge? edge = FindEdge(face.Vertices[i], face.Vertices[(i + 1) % face.Vertices.Count]);
                if (edge == null || !selectedEdges.Contains(edge.Id))
                {
                    everyBoundaryEdgeSelected = false;
                    break;
                }
            }

            if (everyBoundaryEdgeSelected)
            {
                targets.Add(face.Id);
            }
        }

        var edgesToSplit = new HashSet<int>(selectedEdges);
        foreach (int faceId in targets)
        {
            NetworkFace face = Face(faceId)!;
            for (int i = 0; i < face.Vertices.Count; i++)
            {
                if (FindEdge(face.Vertices[i], face.Vertices[(i + 1) % face.Vertices.Count]) is { } edge)
                {
                    edgesToSplit.Add(edge.Id);
                }
            }
        }

        var newVertices = new List<int>();
        foreach (int edgeId in edgesToSplit)
        {
            if (SplitEdgePreservingFaces(edgeId) is int mid)
            {
                newVertices.Add(mid);
            }
        }

        var newFaces = new List<int>();
        foreach (int faceId in targets)
        {
            NetworkFace? face = Face(faceId);
            IReadOnlyList<int>? loop = face?.Vertices;
            if (loop == null || loop.Count < 6 || loop.Count % 2 != 0)
            {
                continue; // not every edge actually gained a midpoint (a degenerate/duplicate loop) — leave it
            }

            int n = loop.Count / 2;
            Vector3 centre = Vector3.Zero;
            bool allStick = true;
            for (int i = 0; i < n; i++)
            {
                NetworkVertex corner = Vertex(loop[i * 2])!;
                centre += corner.Position;
                allStick &= corner.StickToTerrain;
            }

            int ctr = AddVertex(centre / n, allStick);
            newVertices.Add(ctr);
            RemoveFace(faceId);
            for (int i = 0; i < n; i++)
            {
                int v = loop[i * 2];
                int mNext = loop[i * 2 + 1];
                int mPrev = loop[(i * 2 - 1 + loop.Count) % loop.Count];
                if (AddFace([v, mNext, ctr, mPrev]) is int newFaceId)
                {
                    newFaces.Add(newFaceId);
                }
            }
        }

        _version++;
        return new NetworkSubgraph(newVertices, newFaces);
    }

    /// <summary>Blender's Ctrl+R: walks the ring of quads sharing <paramref name="seedEdgeId"/>'s
    /// direction — an edge's "opposite" edge in a quad face, continued into whichever other face
    /// shares that opposite edge — splitting every ring edge at its midpoint and connecting each
    /// crossed quad's two new midpoints with a fresh edge, cutting that quad in two. Ring-walking
    /// only continues through quads (four-vertex faces), exactly like Blender's own loop cut; it
    /// stops at a boundary or a non-quad neighbour. Always cuts at the centre — there is no
    /// interactive slide-to-reposition phase, and a ring that closes into a full loop does not cut
    /// its final wrap-around quad (a known limitation, not a general closed-ring cutter).</summary>
    public NetworkSubgraph LoopCut(int seedEdgeId)
    {
        if (Edge(seedEdgeId) is not { } seed)
        {
            return new NetworkSubgraph([], []);
        }

        // Each walk steps into one of the seed edge's two faces; seeding each call with the other
        // face as its "came from" keeps the two from both entering the same face and stalling on the
        // first shared edge — that is what lets the ring extend on both sides of the seed, not one.
        var seedFaces = FacesUsingEdge(seed.A, seed.B);
        int forwardFrom = seedFaces.Count > 1 ? seedFaces[1] : -1;
        int backwardFrom = seedFaces.Count > 0 ? seedFaces[0] : -1;

        var ring = new List<int> { seedEdgeId };
        ExtendRing(ring, seed.A, seed.B, forwardFrom, prepend: false);
        ExtendRing(ring, seed.A, seed.B, backwardFrom, prepend: true);

        var crossedFaces = new List<int>();
        for (int i = 0; i < ring.Count - 1; i++)
        {
            NetworkEdge e1 = Edge(ring[i])!;
            NetworkEdge e2 = Edge(ring[i + 1])!;
            NetworkFace? quad = _faces.FirstOrDefault(f => f.Vertices.Count == 4 && FaceUsesEdge(f, e1.A, e1.B) && FaceUsesEdge(f, e2.A, e2.B));
            crossedFaces.Add(quad?.Id ?? -1);
        }

        var midpointByEdge = new Dictionary<int, int>();
        var newVertices = new List<int>();
        foreach (int edgeId in ring)
        {
            if (SplitEdgePreservingFaces(edgeId) is int mid)
            {
                midpointByEdge[edgeId] = mid;
                newVertices.Add(mid);
            }
        }

        var newFaces = new List<int>();
        for (int i = 0; i < crossedFaces.Count; i++)
        {
            if (crossedFaces[i] < 0 || Face(crossedFaces[i]) is not { } face ||
                !midpointByEdge.TryGetValue(ring[i], out int m1) || !midpointByEdge.TryGetValue(ring[i + 1], out int m2))
            {
                continue;
            }

            List<int> loop = face.Vertices.ToList();
            int idx1 = loop.IndexOf(m1);
            int idx2 = loop.IndexOf(m2);
            if (idx1 < 0 || idx2 < 0)
            {
                continue;
            }

            List<int> arcA = Arc(loop, idx1, idx2);
            List<int> arcB = Arc(loop, idx2, idx1);
            RemoveFace(face.Id);
            if (AddFace(arcA) is int fa)
            {
                newFaces.Add(fa);
            }

            if (AddFace(arcB) is int fb)
            {
                newFaces.Add(fb);
            }
        }

        _version++;
        return new NetworkSubgraph(newVertices, newFaces);
    }

    /// <summary>The cyclic run of <paramref name="loop"/> from index <paramref name="from"/> to
    /// <paramref name="to"/> inclusive, walking forward and wrapping — one of the two arcs a loop cut
    /// splits a face's boundary into.</summary>
    private static List<int> Arc(List<int> loop, int from, int to)
    {
        var arc = new List<int>();
        for (int i = from; ; i = (i + 1) % loop.Count)
        {
            arc.Add(loop[i]);
            if (i == to)
            {
                break;
            }
        }

        return arc;
    }

    /// <summary>Walks outward from a seed edge through adjacent quads (the face on the side not
    /// already visited), following each quad's "opposite" boundary edge, appending or prepending
    /// every ring edge found. Stops at an open boundary, a non-quad neighbour, or the ring closing
    /// back on itself.</summary>
    private void ExtendRing(List<int> ring, int seedA, int seedB, int fromFaceId, bool prepend)
    {
        int a = seedA;
        int b = seedB;
        while (true)
        {
            int nextFaceId = -1;
            foreach (int candidateId in FacesUsingEdge(a, b))
            {
                if (candidateId != fromFaceId)
                {
                    nextFaceId = candidateId;
                    break;
                }
            }

            if (nextFaceId < 0 || Face(nextFaceId) is not { } quad || quad.Vertices.Count != 4)
            {
                return;
            }

            int index = BoundaryEdgeIndex(quad, a, b);
            if (index < 0)
            {
                return;
            }

            int oppIndex = (index + 2) % 4;
            int oa = quad.Vertices[oppIndex];
            int ob = quad.Vertices[(oppIndex + 1) % 4];
            if (FindEdge(oa, ob) is not { } oppEdge || ring.Contains(oppEdge.Id))
            {
                return;
            }

            if (prepend)
            {
                ring.Insert(0, oppEdge.Id);
            }
            else
            {
                ring.Add(oppEdge.Id);
            }

            fromFaceId = nextFaceId;
            a = oa;
            b = ob;
        }
    }

    private List<int> FacesUsingEdge(int a, int b)
    {
        var result = new List<int>();
        foreach (NetworkFace face in _faces)
        {
            if (FaceUsesEdge(face, a, b))
            {
                result.Add(face.Id);
            }
        }

        return result;
    }

    /// <summary>Splits an edge at its midpoint like <see cref="SplitEdge"/>, but instead of letting
    /// <see cref="RemoveEdge"/>'s cascade delete any face that used it, patches every such face's loop
    /// in place to route through the new midpoint — the edge is gone, but the face's boundary stays
    /// whole and valid.</summary>
    private int? SplitEdgePreservingFaces(int edgeId)
    {
        NetworkEdge? edge = Edge(edgeId);
        NetworkVertex? a = edge == null ? null : Vertex(edge.A);
        NetworkVertex? b = edge == null ? null : Vertex(edge.B);
        if (edge == null || a == null || b == null)
        {
            return null;
        }

        int mid = AddVertex((a.Position + b.Position) * 0.5f, a.StickToTerrain && b.StickToTerrain);
        for (int i = 0; i < _faces.Count; i++)
        {
            NetworkFace face = _faces[i];
            int index = BoundaryEdgeIndex(face, edge.A, edge.B);
            if (index < 0)
            {
                continue;
            }

            var vertices = face.Vertices.ToList();
            vertices.Insert(index + 1, mid);
            _faces[i] = face with { Vertices = vertices };
        }

        _edges.RemoveAll(e => e.Id == edgeId);
        AddEdge(a.Id, mid);
        AddEdge(mid, b.Id);
        _version++;
        return mid;
    }

    private NetworkEdge? FindEdge(int a, int b) =>
        _edges.FirstOrDefault(edge => (edge.A == a && edge.B == b) || (edge.A == b && edge.B == a));

    /// <summary>Index i such that <c>(loop[i], loop[i+1])</c> is the boundary pair (a,b), in either
    /// direction; -1 if the face's loop does not have that edge on its boundary.</summary>
    private static int BoundaryEdgeIndex(NetworkFace face, int a, int b)
    {
        IReadOnlyList<int> vertices = face.Vertices;
        for (int i = 0; i < vertices.Count; i++)
        {
            int x = vertices[i];
            int y = vertices[(i + 1) % vertices.Count];
            if ((x == a && y == b) || (x == b && y == a))
            {
                return i;
            }
        }

        return -1;
    }

    public NetworkVertex? Vertex(int id) => _vertices.FirstOrDefault(vertex => vertex.Id == id);

    public NetworkEdge? Edge(int id) => _edges.FirstOrDefault(edge => edge.Id == id);

    public NetworkFace? Face(int id) => _faces.FirstOrDefault(face => face.Id == id);

    public bool HasEdge(int a, int b) =>
        _edges.Any(edge => (edge.A == a && edge.B == b) || (edge.A == b && edge.B == a));

    private static bool FaceUsesEdge(NetworkFace face, int a, int b)
    {
        IReadOnlyList<int> vertices = face.Vertices;
        for (int i = 0; i < vertices.Count; i++)
        {
            int x = vertices[i];
            int y = vertices[(i + 1) % vertices.Count];
            if ((x == a && y == b) || (x == b && y == a))
            {
                return true;
            }
        }

        return false;
    }

    private static int[] CollapseConsecutiveDuplicates(IReadOnlyList<int> loop)
    {
        var result = new List<int>();
        for (int i = 0; i < loop.Count; i++)
        {
            if (i == 0 || loop[i] != result[^1])
            {
                result.Add(loop[i]);
            }
        }

        if (result.Count > 1 && result[0] == result[^1])
        {
            result.RemoveAt(result.Count - 1);
        }

        return result.ToArray();
    }

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

    /// <summary>Every vertex of degree 1 — the open ends of an unbranched chain. Empty for a closed
    /// loop, which has none.</summary>
    public IReadOnlyList<int> Endpoints()
    {
        Dictionary<int, List<int>> adjacency = BuildAdjacency();
        return _vertices.Where(vertex => adjacency[vertex.Id].Count == 1).Select(vertex => vertex.Id).ToList();
    }

    /// <summary>
    /// Walks the single unbranched chain (or closed loop) this network forms, starting at
    /// <paramref name="startId"/> and stepping first toward <paramref name="secondId"/>. Null unless the
    /// network is exactly one connected, unbranched run of edges — <see cref="ConnectedGraphCount"/> is
    /// 1 and <see cref="HasBranches"/> is false — containing both ids joined by an edge; the same walk
    /// serves an open chain (stops at the far endpoint) and a closed loop (stops back at
    /// <paramref name="startId"/>, not repeated in the result).
    /// </summary>
    public IReadOnlyList<int>? OrderedChainFrom(int startId, int secondId)
    {
        if (startId == secondId || Vertex(startId) == null || Vertex(secondId) == null || !HasEdge(startId, secondId))
        {
            return null;
        }

        if (ConnectedGraphCount() != 1 || HasBranches())
        {
            return null;
        }

        Dictionary<int, List<int>> adjacency = BuildAdjacency();
        var order = new List<int> { startId };
        int previous = startId;
        int current = secondId;
        while (true)
        {
            order.Add(current);
            if (current == startId)
            {
                order.RemoveAt(order.Count - 1);
                return order;
            }

            List<int> neighbours = adjacency[current];
            if (neighbours.Count == 1)
            {
                return order;
            }

            int next = neighbours[0] == previous ? neighbours[1] : neighbours[0];
            previous = current;
            current = next;
        }
    }

    /// <summary>Checks this network's topology against a consumer's requirements — e.g. a procedural
    /// mesh function that only knows how to build along a single, unbranched run of edges.</summary>
    public IReadOnlyList<string> ValidateFor(string displayName, NetworkCapabilities capabilities)
    {
        var problems = new List<string>();
        int graphs = ConnectedGraphCount();
        if (!capabilities.AllowsMultipleGraphs && graphs > 1)
        {
            problems.Add($"{displayName} accepts only one connected graph, but this network has {graphs}.");
        }

        if (!capabilities.AllowsBranching && HasBranches())
        {
            problems.Add($"{displayName} accepts only linear graphs; one or more vertices have more than two connected edges.");
        }

        if (!capabilities.AllowsFaces && _faces.Count > 0)
        {
            problems.Add($"{displayName} does not use faces; {_faces.Count} authored face(s) will be ignored.");
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

    public VertexNetwork Clone()
    {
        var copy = new VertexNetwork();
        copy._vertices.AddRange(_vertices);
        copy._edges.AddRange(_edges);
        foreach (NetworkFace face in _faces)
        {
            copy._faces.Add(new NetworkFace(face.Id, face.Vertices.ToArray()));
        }

        copy._nextVertexId = _nextVertexId;
        copy._nextEdgeId = _nextEdgeId;
        copy._nextFaceId = _nextFaceId;
        return copy;
    }

    public string Fingerprint() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize()))).ToLowerInvariant();

    public string Serialize()
    {
        var dto = new NetworkDto
        {
            NextVertexId = _nextVertexId,
            NextEdgeId = _nextEdgeId,
            NextFaceId = _nextFaceId,
            Vertices = _vertices.Select(v => new VertexDto { Id = v.Id, X = v.Position.X, Y = v.Position.Y, Z = v.Position.Z, StickToTerrain = v.StickToTerrain }).ToList(),
            Edges = _edges.Select(e => new EdgeDto { Id = e.Id, A = e.A, B = e.B }).ToList(),
            Faces = _faces.Select(f => new FaceDto { Id = f.Id, Vertices = f.Vertices.ToList() }).ToList(),
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

            network._vertices.AddRange(dto.Vertices.Select(v => new NetworkVertex(v.Id, new Vector3(v.X, v.Y, v.Z), v.StickToTerrain)));
            network._edges.AddRange(dto.Edges.Select(e => new NetworkEdge(e.Id, e.A, e.B)));
            network._faces.AddRange(dto.Faces.Select(f => new NetworkFace(f.Id, f.Vertices)));
            network._nextVertexId = Math.Max(dto.NextVertexId, network._vertices.Select(v => v.Id).DefaultIfEmpty().Max() + 1);
            network._nextEdgeId = Math.Max(dto.NextEdgeId, network._edges.Select(e => e.Id).DefaultIfEmpty().Max() + 1);
            network._nextFaceId = Math.Max(dto.NextFaceId, network._faces.Select(f => f.Id).DefaultIfEmpty().Max() + 1);
            network.RemoveInvalidAndDuplicateEdges();
            network.RemoveInvalidAndDuplicateFaces();
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

    private void RemoveInvalidAndDuplicateFaces()
    {
        HashSet<int> vertices = _vertices.Select(vertex => vertex.Id).ToHashSet();
        var seen = new HashSet<string>();
        _faces.RemoveAll(face =>
        {
            HashSet<int> distinct = face.Vertices.ToHashSet();
            if (distinct.Count < 3 || face.Vertices.Any(id => !vertices.Contains(id)))
            {
                return true;
            }

            string key = string.Join(',', distinct.OrderBy(id => id));
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
        public int NextFaceId { get; set; } = 1;
        public List<VertexDto> Vertices { get; set; } = [];
        public List<EdgeDto> Edges { get; set; } = [];
        public List<FaceDto> Faces { get; set; } = [];
    }

    private sealed class VertexDto
    {
        public int Id { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public bool StickToTerrain { get; set; }
    }

    private sealed class EdgeDto
    {
        public int Id { get; set; }
        public int A { get; set; }
        public int B { get; set; }
    }

    private sealed class FaceDto
    {
        public int Id { get; set; }
        public List<int> Vertices { get; set; } = [];
    }
}
