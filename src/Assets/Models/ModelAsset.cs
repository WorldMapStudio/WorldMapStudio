using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public sealed record ModelSurface(
    string Name,
    ArrayMesh Mesh,
    string TexturePath,
    Color AlbedoColor)
{
    public Aabb LocalBounds => Mesh.GetAabb();
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

    public ModelAsset(string path, IEnumerable<ModelSurface> surfaces)
    {
        Path = path;
        Surfaces = surfaces.Where(surface => surface.Mesh.GetSurfaceCount() > 0).ToList();
        LocalBounds = CombineBounds(Surfaces.Select(surface => surface.LocalBounds));
    }

    public string Path { get; }

    public IReadOnlyList<ModelSurface> Surfaces { get; }

    public Aabb LocalBounds { get; }

    public Node3D Instantiate(AssetSystem assets)
    {
        var root = new Node3D { Name = $"Model:{AssetPath.FileName(Path)}" };
        if (Surfaces.Count == 0)
        {
            return root;
        }

        for (int i = 0; i < Surfaces.Count; i++)
        {
            ModelSurface surface = Surfaces[i];
            root.AddChild(new MeshInstance3D
            {
                Name = surface.Name.Length == 0 ? $"Surface{i}" : surface.Name,
                Mesh = surface.Mesh,
                MaterialOverride = BuildMaterial(assets, surface, i),
            });
        }

        return root;
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

    private static StandardMaterial3D BuildMaterial(AssetSystem assets, ModelSurface surface, int index)
    {
        Texture2D? texture = surface.TexturePath.Length > 0 ? assets.LoadTextureAsset(surface.TexturePath) : null;
        return new StandardMaterial3D
        {
            AlbedoTexture = texture,
            AlbedoColor = texture == null ? surface.AlbedoColor : Colors.White,
            Roughness = 0.9f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
    }

    public static Color FallbackColor(int index) => FallbackColours[Math.Abs(index) % FallbackColours.Length];
}
