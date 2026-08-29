using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Renders <see cref="StandardMeshMaterial"/> descriptions. What every model rendered through before formats had their own material types.</summary>
[Subsystem(nameof(MeshMaterialSystem))]
public sealed class StandardMeshMaterialType : IMeshMaterialType
{
    public StandardMeshMaterialType(MeshMaterialSystem system)
    {
    }

    public float Priority => 0.0f;

    public string Id => StandardMeshMaterial.TypeId;

    public string DisplayName => "Standard";

    public string Description => "Generic texture + tint material used by OBJ, glTF and untyped procedural meshes.";

    public int Version => 1;

    public IReadOnlyList<MeshParameter> Parameters => StandardMeshMaterial.Parameters;

    // Matches the hardcoded default a procedural mesh surface rendered with before material slots
    // existed (see ProceduralMeshOutputBuilder's old texture/tint overload) — most procedural output
    // is a thin, open surface (a tube, a ribbon) that looks wrong back-face-culled.
    public MeshMaterial Default => StandardMeshMaterial.Describe(albedo: new Color(0.72f, 0.74f, 0.78f), twoSided: true);

    public Material Build(in MeshMaterialBuildContext context)
    {
        var result = new StandardMaterial3D
        {
            AlbedoColor = context.Color(StandardMeshMaterial.Albedo),
            Roughness = 0.9f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = context.Bool(StandardMeshMaterial.TwoSided) ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
            ShadingMode = context.Bool(StandardMeshMaterial.Unlit) ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
            DepthDrawMode = context.Bool(StandardMeshMaterial.DepthWrite) ? BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly : BaseMaterial3D.DepthDrawModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            TextureRepeat = context.Bool(StandardMeshMaterial.RepeatTexture),
            VertexColorUseAsAlbedo = context.Bool(StandardMeshMaterial.VertexColor),
        };

        ApplyBlendMode(result, context);

        string texturePath = context.Texture(StandardMeshMaterial.Texture);
        if (texturePath.Length > 0)
        {
            AssignTextureAsync(context.Assets, result, texturePath);
        }

        return result;
    }

    private static void ApplyBlendMode(StandardMaterial3D target, in MeshMaterialBuildContext context)
    {
        switch (context.Choice(StandardMeshMaterial.Blend))
        {
            case "alpha_cutout":
                target.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                target.AlphaScissorThreshold = context.Float(StandardMeshMaterial.AlphaCutoff);
                target.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
                break;
            case "alpha_blend":
                target.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                target.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
                break;
            case "additive":
                target.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                target.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                break;
            case "modulate":
            case "modulate2x":
                // Godot has no "multiply by 2" blend mode; modulate2x is approximated as a plain multiply.
                target.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
                target.BlendMode = BaseMaterial3D.BlendModeEnum.Mul;
                break;
            default:
                target.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
                target.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
                break;
        }
    }

    private static void AssignTextureAsync(AssetSystem assets, StandardMaterial3D target, string texturePath)
    {
        Task<Texture2D?> pending = assets.LoadTextureAssetAsync(texturePath);
        if (pending.IsCompletedSuccessfully)
        {
            ApplyTexture(target, pending.Result);
            return;
        }

        WorkQueue.Schedule("Assign Model Texture", async work =>
        {
            Texture2D? texture = await pending.ConfigureAwait(false);
            await work.SwitchToMain();
            ApplyTexture(target, texture);
        });
    }

    private static void ApplyTexture(StandardMaterial3D target, Texture2D? texture)
    {
        target.AlbedoTexture = texture;
        if (texture != null)
        {
            target.AlbedoColor = Colors.White;
        }
    }
}
