using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Loads texture formats supported by Godot from provider-supplied bytes.</summary>
[Subsystem(nameof(AssetSystem))]
public sealed class GodotTextureLoader : ITextureLoader
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp",
        ".exr",
        ".jpg",
        ".jpeg",
        ".png",
        ".svg",
        ".tga",
        ".webp",
    };

    public GodotTextureLoader(AssetSystem assets)
    {
    }

    public bool CanLoad(string path) => Extensions.Contains(AssetPath.Extension(path));

    public async Task<Image?> LoadTextureImageAsync(AssetSystem assets, string path)
    {
        byte[]? bytes = await assets.ReadAssetBytesAsync(path).ConfigureAwait(false);
        if (bytes == null)
        {
            return null;
        }

        var image = new Image();
        Error error = AssetPath.Extension(path).ToLowerInvariant() switch
        {
            ".bmp" => image.LoadBmpFromBuffer(bytes),
            ".exr" => image.LoadExrFromBuffer(bytes),
            ".jpg" or ".jpeg" => image.LoadJpgFromBuffer(bytes),
            ".png" => image.LoadPngFromBuffer(bytes),
            ".svg" => image.LoadSvgFromBuffer(bytes),
            ".tga" => image.LoadTgaFromBuffer(bytes),
            ".webp" => image.LoadWebpFromBuffer(bytes),
            _ => Error.Unavailable,
        };

        return error == Error.Ok ? image : null;
    }
}
