using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>A named pointer to another model asset, placed at <paramref name="Transform"/> relative to its owning part.</summary>
public sealed record ModelReference(string Name, string Path, Transform3D Transform);

public sealed record ModelSurface(
    string Name,
    ArrayMesh Mesh,
    ModelMaterial Material)
{
    public Aabb LocalBounds => Mesh.GetAabb();
}

/// <summary>A rigid sub-group of a model: its own geometry plus any nested references, at one transform.</summary>
public sealed record ModelPart(
    string Name,
    Transform3D Transform,
    IReadOnlyList<ModelSurface> Surfaces,
    IReadOnlyList<ModelReference> References)
{
    public Aabb LocalBounds => ModelAsset.CombineBounds(Surfaces.Select(surface => surface.LocalBounds));
}

public sealed class ModelInstantiateOptions
{
    /// <summary>Caps how many reference hops (e.g. WMO doodad -> M2) are resolved before giving up.</summary>
    public int MaxDepth { get; init; } = 4;
}

public sealed class ModelAsset
{
    private static readonly Color[] FallbackColours =
    [
        new(0.68f, 0.72f, 0.78f),
        new(0.74f, 0.62f, 0.52f),
        new(0.55f, 0.70f, 0.58f),
        new(0.70f, 0.58f, 0.68f),
    ];

    public ModelAsset(string path, IEnumerable<ModelPart> parts, Aabb? boundsHint = null)
    {
        Path = path;
        Parts = parts.Select(FilterEmptySurfaces).ToList();
        Surfaces = Parts.SelectMany(part => part.Surfaces).ToList();
        LocalBounds = boundsHint ?? CombineBounds(Parts.Where(part => part.Surfaces.Count > 0).Select(part => part.LocalBounds));
    }

    public ModelAsset(string path, IEnumerable<ModelSurface> surfaces, Aabb? boundsHint = null)
        : this(path, [new ModelPart("", Transform3D.Identity, surfaces.ToList(), [])], boundsHint)
    {
    }

    public string Path { get; }

    /// <summary>Rigid sub-groups making up this model. A flat-surface loader produces exactly one, identity-transformed part.</summary>
    public IReadOnlyList<ModelPart> Parts { get; }

    /// <summary>Convenience projection of every surface across every part, ignoring transforms and references.</summary>
    public IReadOnlyList<ModelSurface> Surfaces { get; }

    public Aabb LocalBounds { get; }

    /// <summary>
    /// Builds the viewport node. References (e.g. WMO doodads) are resolved asynchronously: an empty
    /// anchor is added immediately at the reference's transform and populated once its target loads.
    /// </summary>
    public Node3D Instantiate(AssetSystem assets, ModelInstantiateOptions? options = null)
    {
        options ??= new ModelInstantiateOptions();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path };
        return Instantiate(assets, options.MaxDepth, depth: 0, visited);
    }

    public static Aabb CombineBounds(IEnumerable<Aabb> bounds)
    {
        using IEnumerator<Aabb> enumerator = bounds.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return new Aabb(-Vector3.One * 0.5f, Vector3.One);
        }

        Aabb combined = enumerator.Current;
        while (enumerator.MoveNext())
        {
            combined = combined.Merge(enumerator.Current);
        }

        return combined.Size.LengthSquared() <= 0.0001f
            ? new Aabb(-Vector3.One * 0.5f, Vector3.One)
            : combined;
    }

    public static Color FallbackColor(int index) => FallbackColours[Math.Abs(index) % FallbackColours.Length];

    private Node3D Instantiate(AssetSystem assets, int maxDepth, int depth, IReadOnlySet<string> visited)
    {
        var root = new Node3D { Name = $"Model:{AssetPath.FileName(Path)}" };
        foreach (ModelPart part in Parts)
        {
            var partNode = new Node3D { Name = part.Name.Length == 0 ? "Part" : part.Name, Transform = part.Transform };
            root.AddChild(partNode);

            for (int i = 0; i < part.Surfaces.Count; i++)
            {
                ModelSurface surface = part.Surfaces[i];
                partNode.AddChild(new MeshInstance3D
                {
                    Name = surface.Name.Length == 0 ? $"Surface{i}" : surface.Name,
                    Mesh = surface.Mesh,
                    MaterialOverride = ModelMaterialFactory.Build(assets, surface.Material),
                });
            }

            foreach (ModelReference reference in part.References)
            {
                AttachReference(assets, partNode, reference, maxDepth, depth, visited);
            }
        }

        return root;
    }

    private static void AttachReference(AssetSystem assets, Node3D parent, ModelReference reference, int maxDepth, int depth, IReadOnlySet<string> visited)
    {
        var anchor = new Node3D { Name = reference.Name.Length == 0 ? "Reference" : reference.Name, Transform = reference.Transform };
        parent.AddChild(anchor);

        // Depth/cycle guards are per-branch: siblings that reference the same model (common for
        // repeated WMO doodads) must each resolve independently rather than dedupe against each other.
        if (depth >= maxDepth || visited.Contains(reference.Path))
        {
            return;
        }

        var branchVisited = new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase) { reference.Path };

        Task<ModelAsset?> task = assets.LoadModelAssetAsync(reference.Path);
        if (task.IsCompletedSuccessfully)
        {
            AttachResolved(assets, anchor, task.Result, maxDepth, depth, branchVisited);
            return;
        }

        WorkQueue.Schedule("Resolve Model Reference", async work =>
        {
            ModelAsset? resolved = await task.ConfigureAwait(false);
            await work.SwitchToMain();
            if (GodotObject.IsInstanceValid(anchor))
            {
                AttachResolved(assets, anchor, resolved, maxDepth, depth, branchVisited);
            }
        });
    }

    private static void AttachResolved(AssetSystem assets, Node3D anchor, ModelAsset? resolved, int maxDepth, int depth, IReadOnlySet<string> visited)
    {
        if (resolved == null)
        {
            return;
        }

        anchor.AddChild(resolved.Instantiate(assets, maxDepth, depth + 1, visited));
    }

    private static ModelPart FilterEmptySurfaces(ModelPart part)
    {
        List<ModelSurface> filtered = part.Surfaces.Where(surface => surface.Mesh.GetSurfaceCount() > 0).ToList();
        return filtered.Count == part.Surfaces.Count ? part : part with { Surfaces = filtered };
    }
}
