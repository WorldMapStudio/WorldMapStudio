using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The preview image shown for each map in the map picker: a downscaled snapshot of the 3D viewport,
/// taken when the user opens the picker. Stored as a PNG beside the project's data rather than in the
/// database, so previews never turn into a schema change or a dolt diff.
/// </summary>
public sealed class MapThumbnails
{
    public const int Width = 256;
    public const int Height = 144;

    private readonly string _folder;
    private readonly Dictionary<MapId, ImageTexture?> _cache = [];

    public MapThumbnails(string projectFolder)
    {
        _folder = Path.Combine(projectFolder, "thumbnails");
    }

    /// <summary>The ImGui texture handle for a map's preview, or zero when it has none yet.</summary>
    public IntPtr TextureId(MapId map)
    {
        if (!_cache.TryGetValue(map, out ImageTexture? texture))
        {
            texture = Load(map);
            _cache[map] = texture;
        }

        return texture == null ? IntPtr.Zero : (IntPtr)texture.GetRid().Id;
    }

    /// <summary>Downscales a viewport snapshot and stores it as the map's preview. Main thread only.</summary>
    public void Save(MapId map, Image source)
    {
        try
        {
            Directory.CreateDirectory(_folder);

            Image thumbnail = Downscale(source);
            Error error = thumbnail.SavePng(PathFor(map));
            if (error != Error.Ok)
            {
                GD.PushError($"[Map] Failed to write thumbnail for {map}: {error}.");
                return;
            }

            _cache[map] = ImageTexture.CreateFromImage(thumbnail);
        }
        catch (Exception e)
        {
            GD.PushError($"[Map] Failed to save thumbnail for {map}: {e.Message}");
        }
    }

    /// <summary>Drops a deleted map's preview, so re-using the id later doesn't inherit its picture.</summary>
    public void Remove(MapId map)
    {
        _cache.Remove(map);

        try
        {
            File.Delete(PathFor(map));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Map] Failed to delete thumbnail for {map}: {e.Message}");
        }
    }

    private ImageTexture? Load(MapId map)
    {
        string path = PathFor(map);
        if (!File.Exists(path))
        {
            return null;
        }

        Image? image = Image.LoadFromFile(path);
        return image == null ? null : ImageTexture.CreateFromImage(image);
    }

    private string PathFor(MapId map) => Path.Combine(_folder, $"{map.Value}.png");

    // Centre-crop to the thumbnail's aspect before resizing, so a wide viewport doesn't squash the
    // preview. Godot's Image is mutated in place by Resize, hence the copy that GetRegion returns.
    private static Image Downscale(Image source)
    {
        int width = source.GetWidth();
        int height = source.GetHeight();
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The viewport produced an empty image.");
        }

        int cropHeight = Math.Clamp(Mathf.RoundToInt(width * (float)Height / Width), 1, height);
        int cropWidth = Math.Clamp(Mathf.RoundToInt(cropHeight * (float)Width / Height), 1, width);

        Image thumbnail = source.GetRegion(new Rect2I((width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight));
        thumbnail.Resize(Width, Height, Image.Interpolation.Lanczos);
        return thumbnail;
    }
}
