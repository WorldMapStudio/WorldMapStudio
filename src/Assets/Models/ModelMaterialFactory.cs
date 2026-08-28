using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Turns a format-agnostic <see cref="ModelMaterial"/> description into a Godot material.</summary>
public static class ModelMaterialFactory
{
    public static StandardMaterial3D Build(AssetSystem assets, ModelMaterial material)
    {
        var result = new StandardMaterial3D
        {
            AlbedoColor = material.AlbedoColor,
            Roughness = 0.9f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = material.TwoSided ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
            ShadingMode = material.Unlit ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
            DepthDrawMode = material.DepthWrite ? BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly : BaseMaterial3D.DepthDrawModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            TextureRepeat = material.RepeatTexture,
            VertexColorUseAsAlbedo = material.UseVertexColor,
        };

        ApplyBlendMode(result, material);

        if (material.TexturePath.Length > 0)
        {
            AssignTextureAsync(assets, result, material.TexturePath);
        }

        return result;
    }

    private static void ApplyBlendMode(StandardMaterial3D target, ModelMaterial material)
    {
        switch (material.BlendMode)
        {
            case ModelBlendMode.AlphaCutout:
                target.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                target.AlphaScissorThreshold = material.AlphaCutoff;
                target.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
                break;
            case ModelBlendMode.AlphaBlend:
                target.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                target.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
                break;
            case ModelBlendMode.Additive:
                target.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                target.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                break;
            case ModelBlendMode.Modulate:
            case ModelBlendMode.Modulate2x:
                // Godot has no "multiply by 2" blend mode; Modulate2x is approximated as a plain multiply.
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
