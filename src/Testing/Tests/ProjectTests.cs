using System;
using System.Buffers.Binary;
using System.Linq;
using System.IO;
using Godot;

namespace WorldMapStudio;

/// <summary>Covers project-level settings behaviour.</summary>
public static class ProjectTests
{
    [EditorTest(Category = "Project")]
    public static void Project_round_trips_through_disk()
    {
        var project = new Project
        {
            Name = "__wms_test_roundtrip__",
            AxisConvention = AxisConvention.Create(SignedAxis.PosZ, SignedAxis.PosX, SignedAxis.NegY),
        };
        project.GetOrAddStorageConnection("Editor", new StorageConnection { Database = "editor", Port = 3399, LaunchServer = true });
        project.AssetSources.Add(new AssetSourceSettings
        {
            Id = "loose",
            Name = "Loose Textures",
            Type = AssetSourceType.FileSystem,
            Enabled = true,
            RootPath = "D:\\Textures",
        });
        project.AssetSources.Add(new AssetSourceSettings
        {
            Id = "more",
            Name = "More Textures",
            Type = AssetSourceType.FileSystem,
            Enabled = false,
            RootPath = "E:\\Textures",
        });

        try
        {
            ProjectStore.Save(project);
            Project? loaded = ProjectStore.LoadAll().FirstOrDefault(p => p.Name == project.Name);

            Assert.IsNotNull(loaded, "saved project should load back");
            Assert.AreEqual(SignedAxis.PosZ, loaded!.AxisConvention.X);
            Assert.AreEqual(SignedAxis.NegY, loaded.AxisConvention.Z);
            Assert.IsTrue(loaded.StorageConnections.ContainsKey("Editor"));
            Assert.AreEqual(3399, loaded.StorageConnections["Editor"].Port);
            Assert.IsTrue(loaded.StorageConnections["Editor"].LaunchServer);
            Assert.AreEqual(2, loaded.AssetSources.Count);
            Assert.AreEqual("loose", loaded.AssetSources[0].Id);
            Assert.AreEqual("Loose Textures", loaded.AssetSources[0].Name);
            Assert.AreEqual(AssetSourceType.FileSystem, loaded.AssetSources[1].Type);
            Assert.IsFalse(loaded.AssetSources[1].Enabled);
        }
        finally
        {
            ProjectStore.Delete(project);
        }
    }

    [EditorTest(Category = "Project")]
    public static void Storage_connection_is_added_once_and_reused()
    {
        var project = new Project { Name = "Test" };

        StorageConnection first = project.GetOrAddStorageConnection("Editor", new StorageConnection { Database = "editor" });
        Assert.AreEqual("editor", first.Database);

        // A later call must return the stored connection, not overwrite a user's edits with defaults.
        first.Port = 3399;
        StorageConnection second = project.GetOrAddStorageConnection("Editor", new StorageConnection { Port = 3312 });
        Assert.AreEqual(3399, second.Port);
        Assert.IsTrue(ReferenceEquals(first, second));
    }

    [EditorTest(Category = "Project")]
    public static void Filesystem_asset_provider_lists_assets()
    {
        string root = Path.Combine(Path.GetTempPath(), "__wms_assets_test__");
        string nested = Path.Combine(root, "Tiles");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, "grass.png"), "");
        File.WriteAllText(Path.Combine(nested, "stone.jpg"), "");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "");

        try
        {
            var provider = new FileSystemAssetProvider(null!);
            var source = new AssetSourceSettings { Id = "textures", Name = "Textures", RootPath = root };

            var assets = provider.ListAssets(source).OrderBy(asset => asset.Path).ToList();

            Assert.AreEqual(3, assets.Count);
            Assert.IsTrue(assets.Any(asset =>
                asset.Path == "grass.png" &&
                asset.Kind == AssetKind.Unknown));
            Assert.IsTrue(assets.Any(asset => asset.Path == "notes.txt"));
            Assert.IsTrue(assets.Any(asset => asset.Path == Path.Combine("Tiles", "stone.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [EditorTest(Category = "Project")]
    public static void Asset_system_lists_provider_backed_model_assets()
    {
        string root = Path.Combine(Path.GetTempPath(), "__wms_model_assets_test__");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "tree.obj"), "");
        File.WriteAllText(Path.Combine(root, "rock.gltf"), "{}");
        File.WriteAllText(Path.Combine(root, "grass.png"), "");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "");

        try
        {
            var project = new Project { Name = "__wms_model_asset_listing__" };
            project.AssetSources.Add(new AssetSourceSettings
            {
                Id = "models",
                Name = "Loose Models",
                Type = AssetSourceType.FileSystem,
                Enabled = true,
                RootPath = root,
            });
            var context = new EditorContext(new Godot.Node3D(), project);

            var models = context.Assets.ListModelAssets().OrderBy(asset => asset.Path).ToList();

            Assert.AreEqual(2, models.Count);
            Assert.IsTrue(models.Any(asset => asset.Path == "rock.gltf" && asset.Kind == AssetKind.Model));
            Assert.IsTrue(models.Any(asset => asset.Path == "tree.obj" && asset.Kind == AssetKind.Model));
            Assert.IsFalse(models.Any(asset => asset.Path == "grass.png"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [EditorTest(Category = "Project")]
    public static void Obj_model_loader_builds_a_provider_backed_mesh()
    {
        string root = Path.Combine(Path.GetTempPath(), "__wms_obj_model_test__");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "triangle.obj"), """
v 0 0 0
v 1 0 0
v 0 1 0
vt 0 0
vt 1 0
vt 0 1
f 1/1 2/2 3/3
""");

        try
        {
            ModelAsset? model = LoadModelFrom(root, "triangle.obj");

            Assert.IsNotNull(model);
            Assert.AreEqual(1, model!.Surfaces.Count);
            Assert.IsTrue(model.LocalBounds.Size.Length() > 0.0f);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [EditorTest(Category = "Project")]
    public static void Gltf_model_loader_builds_a_provider_backed_mesh()
    {
        string root = Path.Combine(Path.GetTempPath(), "__wms_gltf_model_test__");
        Directory.CreateDirectory(root);
        string buffer = Convert.ToBase64String(GltfTriangleBuffer());
        File.WriteAllText(Path.Combine(root, "triangle.gltf"), $$"""
{
  "asset": { "version": "2.0" },
  "buffers": [{ "uri": "data:application/octet-stream;base64,{{buffer}}", "byteLength": 42 }],
  "bufferViews": [
    { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
    { "buffer": 0, "byteOffset": 36, "byteLength": 6 }
  ],
  "accessors": [
    { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
    { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
  ],
  "meshes": [{ "primitives": [{ "attributes": { "POSITION": 0 }, "indices": 1 }] }]
}
""");

        try
        {
            ModelAsset? model = LoadModelFrom(root, "triangle.gltf");

            Assert.IsNotNull(model);
            Assert.AreEqual(1, model!.Surfaces.Count);
            Assert.IsTrue(model.LocalBounds.Size.Length() > 0.0f);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [EditorTest(Category = "Project")]
    public static void Model_part_transform_is_applied_to_instantiated_node()
    {
        var transform = new Transform3D(Basis.Identity, new Vector3(3, 0, 0));
        var part = new ModelPart("offset", transform, [], []);
        var model = new ModelAsset("offset.synthetic", [part]);
        var context = new EditorContext(new Godot.Node3D(), new Project { Name = "__wms_model_part_transform_test__" });

        Node3D node = model.Instantiate(context.Assets);
        var partNode = (Node3D)node.GetChild(0);

        Assert.IsTrue(partNode.Transform.Origin.IsEqualApprox(transform.Origin));
    }

    [EditorTest(Category = "Project")]
    public static void Model_local_bounds_ignore_reference_only_parts()
    {
        ArrayMesh mesh = BuildTriangleMesh(new Vector3(10, 0, 0));
        var geometryPart = new ModelPart("geometry", Transform3D.Identity, [new ModelSurface("tri", mesh, new ModelMaterial())], []);
        var referencePart = new ModelPart("refs", Transform3D.Identity, [], [new ModelReference("child", "unused.synthetic", Transform3D.Identity)]);
        var model = new ModelAsset("bounds.synthetic", [geometryPart, referencePart]);

        Aabb expected = ModelAsset.CombineBounds([geometryPart.LocalBounds]);

        Assert.IsTrue(model.LocalBounds.Position.IsEqualApprox(expected.Position));
        Assert.IsTrue(model.LocalBounds.Size.IsEqualApprox(expected.Size));
    }

    [EditorTest(Category = "Project")]
    public static void Model_self_reference_terminates_without_resolving()
    {
        var selfReference = new ModelReference("self", "loop.synthetic", Transform3D.Identity);
        var part = new ModelPart("root", Transform3D.Identity, [], [selfReference]);
        var model = new ModelAsset("loop.synthetic", [part]);
        var context = new EditorContext(new Godot.Node3D(), new Project { Name = "__wms_model_cycle_test__" });

        Node3D node = model.Instantiate(context.Assets);
        var partNode = (Node3D)node.GetChild(0);
        var anchor = (Node3D)partNode.GetChild(0);

        Assert.AreEqual(0, anchor.GetChildCount());
    }

    private static ArrayMesh BuildTriangleMesh(Vector3 offset)
    {
        Vector3[] vertices = [offset, offset + new Vector3(1, 0, 0), offset + new Vector3(0, 1, 0)];
        int[] indices = [0, 1, 2];
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    [EditorTest(Category = "Project")]
    public static void Asset_source_ids_are_path_safe_and_unique()
    {
        var sources = new[]
        {
            new AssetSourceSettings { Id = "textures" },
            new AssetSourceSettings { Id = "textures2" },
        };

        Assert.IsNull(AssetSourceId.Validate("terrain-textures", sources, null));
        Assert.IsNull(AssetSourceId.Validate("terrain.textures_01", sources, null));
        Assert.AreEqual("textures3", AssetSourceId.Unique("textures", sources.Select(source => source.Id)));
        Assert.IsNotNull(AssetSourceId.Validate("", sources, null));
        Assert.IsNotNull(AssetSourceId.Validate("terrain textures", sources, null));
        Assert.IsNotNull(AssetSourceId.Validate("textures", sources, null));
    }

    private static ModelAsset? LoadModelFrom(string root, string path)
    {
        var project = new Project { Name = "__wms_model_asset_loading__" };
        project.AssetSources.Add(new AssetSourceSettings
        {
            Id = "models",
            Name = "Models",
            Type = AssetSourceType.FileSystem,
            Enabled = true,
            RootPath = root,
        });
        var context = new EditorContext(new Godot.Node3D(), project);
        return context.Assets.LoadModelAsset(path);
    }

    private static byte[] GltfTriangleBuffer()
    {
        byte[] bytes = new byte[42];
        WriteFloat(bytes, 0, 0.0f);
        WriteFloat(bytes, 4, 0.0f);
        WriteFloat(bytes, 8, 0.0f);
        WriteFloat(bytes, 12, 1.0f);
        WriteFloat(bytes, 16, 0.0f);
        WriteFloat(bytes, 20, 0.0f);
        WriteFloat(bytes, 24, 0.0f);
        WriteFloat(bytes, 28, 1.0f);
        WriteFloat(bytes, 32, 0.0f);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(36, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(38, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(40, 2), 2);
        return bytes;
    }

    private static void WriteFloat(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset, 4), value);
}
