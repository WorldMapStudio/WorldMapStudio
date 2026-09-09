using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Browses configured asset sources for the file(s) that back a disk <see cref="PaintImage"/>: a lone
/// image is offered as a single-chunk image, a folder holding two or more tiles named to a
/// <c>{x}_{y}</c> pattern is offered as a tiled one. Confirming probes the file(s) and hands back a
/// <see cref="DiskImageSourceResult"/> with the geometry the creation form fills itself from.
/// </summary>
public sealed class DiskImageSourceSelectionOperation : IModalOperation<DiskImageSourceSelectionContext>
{
    private static readonly Vector2 BodySize = new(720, 360);
    private static readonly string[] Extensions = [".png", ".exr"];

    private sealed record Entry(string SourceId, string Path, bool IsTiled, string TilePattern, int TileCount);

    private string _filter = "";
    private string _pattern = PaintImage.DefaultDiskTilePattern;
    private List<Entry>? _entries;
    private Task<IReadOnlyList<AssetRef>>? _listTask;
    private Entry? _selected;
    private Task<DiskImageSourceResult?>? _probe;

    public ModalOperationState Draw(DiskImageSourceSelectionContext context)
    {
        ImGui.Text("Select Disk Image Source");
        ImGui.Separator();

        ImGui.SetNextItemWidth(BodySize.X - 220.0f);
        ImGui.InputTextWithHint("##filter", "Filter paths...", ref _filter, 256);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200.0f);
        if (ImGui.InputTextWithHint("##pattern", "tile pattern", ref _pattern, 64))
        {
            _entries = null;
        }

        _listTask ??= context.Assets.ListTextureAssetsAsync();
        if (_entries == null && _listTask.IsCompletedSuccessfully)
        {
            _entries = BuildEntries(_listTask.Result);
        }

        ImGui.BeginChild("DiskImageSourceList", BodySize, true, ImGuiWindowFlags.None);
        if (_entries == null)
        {
            ImGui.TextDisabled("Indexing assets...");
        }
        else
        {
            DrawList(context);
        }

        ImGui.EndChild();

        DrawSelectionSummary();
        ImGui.Separator();

        bool ready = _probe is { IsCompletedSuccessfully: true, Result: not null };
        if (!ready)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Select", new Vector2(120, 0)) && ready)
        {
            context.Select(_probe!.Result!);
            return ModalOperationState.Confirmed;
        }

        if (!ready)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            return ModalOperationState.Cancelled;
        }

        return ModalOperationState.Running;
    }

    public void OnClose()
    {
        _entries = null;
        _listTask = null;
        _selected = null;
        _probe = null;
    }

    private void DrawList(DiskImageSourceSelectionContext context)
    {
        string filter = _filter.Trim();
        List<Entry> visible = _entries!
            .Where(entry => filter.Length == 0 || entry.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.IsTiled)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (visible.Count == 0)
        {
            ImGui.TextDisabled("No .png / .exr assets match.");
            return;
        }

        foreach (Entry entry in visible)
        {
            bool selected = ReferenceEquals(entry, _selected);
            string label = entry.IsTiled
                ? $"[tiles] {entry.Path}/  ({entry.TileCount} × {entry.TilePattern})##{entry.Path}:t"
                : $"[file]  {entry.Path}##{entry.Path}:f";
            if (ImGui.Selectable(label, selected))
            {
                _selected = entry;
                _probe = ProbeAsync(context.Assets, entry);
            }
        }
    }

    private void DrawSelectionSummary()
    {
        if (_selected == null)
        {
            ImGui.TextDisabled("Nothing selected.");
            return;
        }

        if (_probe is not { IsCompleted: true })
        {
            ImGui.TextDisabled($"Probing {_selected.Path}...");
            return;
        }

        if (_probe.Result is not { } result)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f),
                $"{_selected.Path} is unreadable, or (single file) larger than 4096 px — supply tiles instead.");
            return;
        }

        ImGui.TextDisabled(
            $"{result.Width} x {result.Height} px · chunk {result.ChunkSize} · {ComponentsLabel(result.Components)}" +
            $"{(result.Format == PaintImagePixelFormat.Float32 ? " · f32" : "")}" +
            $"{(result.IsTiled ? " · tiled" : " · single")}");
    }

    private List<Entry> BuildEntries(IReadOnlyList<AssetRef> assets)
    {
        System.Text.RegularExpressions.Regex matcher = ImageDiskStore.TilePatternRegex(_pattern);
        var entries = new List<Entry>();
        var byDirectory = new Dictionary<(string Source, string Dir), int>();

        foreach (AssetRef asset in assets)
        {
            if (!Extensions.Contains(AssetPath.Extension(asset.Path).ToLowerInvariant()))
            {
                continue;
            }

            string normalized = AssetPath.Normalize(asset.Path);
            int slash = normalized.LastIndexOf('/');
            string directory = slash >= 0 ? normalized[..slash] : "";
            entries.Add(new Entry(asset.SourceId, normalized, IsTiled: false, "", 0));

            if (matcher.IsMatch(AssetPath.FileName(normalized)))
            {
                byDirectory.TryGetValue((asset.SourceId, directory), out int count);
                byDirectory[(asset.SourceId, directory)] = count + 1;
            }
        }

        foreach (((string source, string directory), int count) in byDirectory)
        {
            if (count >= 2)
            {
                entries.Add(new Entry(source, directory, IsTiled: true, _pattern.Trim(), count));
            }
        }

        return entries;
    }

    private static Task<DiskImageSourceResult?> ProbeAsync(AssetSystem assets, Entry entry) =>
        entry.IsTiled ? ProbeTiledAsync(assets, entry) : ProbeSingleAsync(assets, entry);

    private static async Task<DiskImageSourceResult?> ProbeSingleAsync(AssetSystem assets, Entry entry)
    {
        byte[]? bytes = await assets.ReadAssetBytesFromAsync(entry.SourceId, entry.Path).ConfigureAwait(false);
        if (bytes is not { Length: > 0 } || Decode(bytes, entry.Path) is not { } image)
        {
            return null;
        }

        int longest = Math.Max(image.GetWidth(), image.GetHeight());
        if (longest is < 1 or > 4096)
        {
            // A single-chunk image is exactly one tile, so it cannot exceed PaintImage's max chunk
            // size — anything bigger has to come in as tiles.
            return null;
        }

        (int components, PaintImagePixelFormat format) = Describe(image, entry.Path);
        return new DiskImageSourceResult(entry.SourceId, entry.Path, "", IsTiled: false,
            image.GetWidth(), image.GetHeight(), Math.Max(16, longest), components, format);
    }

    private static async Task<DiskImageSourceResult?> ProbeTiledAsync(AssetSystem assets, Entry entry)
    {
        IReadOnlyList<string> paths = await assets.ListSourcePathsAsync(entry.SourceId, entry.Path).ConfigureAwait(false);
        System.Text.RegularExpressions.Regex matcher = ImageDiskStore.TilePatternRegex(entry.TilePattern);

        var coords = new List<ImageChunkCoord>();
        foreach (string path in paths)
        {
            System.Text.RegularExpressions.Match match = matcher.Match(AssetPath.FileName(path));
            if (match.Success)
            {
                coords.Add(new ImageChunkCoord(
                    int.Parse(match.Groups["x"].Value),
                    int.Parse(match.Groups["y"].Value)));
            }
        }

        if (coords.Count == 0)
        {
            return null;
        }

        int maxX = coords.Max(c => c.X);
        int maxY = coords.Max(c => c.Y);

        Image? probe = await ReadTileAsync(assets, entry, coords[0]).ConfigureAwait(false);
        if (probe == null)
        {
            return null;
        }

        (int components, PaintImagePixelFormat format) = Describe(probe, entry.TilePattern);
        int chunkSize = Math.Clamp(probe.GetWidth(), 16, 4096);

        int lastColW = await TileEdgeAsync(assets, entry, new ImageChunkCoord(maxX, 0), chunkSize, wantWidth: true).ConfigureAwait(false);
        int lastRowH = await TileEdgeAsync(assets, entry, new ImageChunkCoord(0, maxY), chunkSize, wantWidth: false).ConfigureAwait(false);

        int width = (maxX * chunkSize) + lastColW;
        int height = (maxY * chunkSize) + lastRowH;
        return new DiskImageSourceResult(entry.SourceId, entry.Path, entry.TilePattern, IsTiled: true,
            width, height, chunkSize, components, format);
    }

    private static async Task<Image?> ReadTileAsync(AssetSystem assets, Entry entry, ImageChunkCoord coord)
    {
        string file = entry.TilePattern.Replace("{x}", coord.X.ToString()).Replace("{y}", coord.Y.ToString());
        string path = entry.Path.Length == 0 ? file : $"{entry.Path}/{file}";
        byte[]? bytes = await assets.ReadAssetBytesFromAsync(entry.SourceId, path).ConfigureAwait(false);
        return bytes is { Length: > 0 } ? Decode(bytes, path) : null;
    }

    private static async Task<int> TileEdgeAsync(AssetSystem assets, Entry entry, ImageChunkCoord coord, int chunkSize, bool wantWidth)
    {
        Image? tile = await ReadTileAsync(assets, entry, coord).ConfigureAwait(false);
        if (tile == null)
        {
            return chunkSize;
        }

        return wantWidth ? tile.GetWidth() : tile.GetHeight();
    }

    private static Image? Decode(byte[] bytes, string path)
    {
        var image = new Image();
        Error error = AssetPath.Extension(path).ToLowerInvariant() == ".exr"
            ? image.LoadExrFromBuffer(bytes)
            : image.LoadPngFromBuffer(bytes);
        return error == Error.Ok ? image : null;
    }

    private static (int Components, PaintImagePixelFormat Format) Describe(Image image, string path)
    {
        if (AssetPath.Extension(path).ToLowerInvariant() == ".exr")
        {
            return (1, PaintImagePixelFormat.Float32);
        }

        int components = image.GetFormat() switch
        {
            Image.Format.L8 or Image.Format.R8 => 1,
            Image.Format.Rgb8 => 3,
            Image.Format.Rgba8 or Image.Format.La8 => 4,
            _ => image.DetectAlpha() != Image.AlphaMode.None ? 4 : 3,
        };

        return (components, PaintImagePixelFormat.Byte);
    }

    private static string ComponentsLabel(int components) => components switch
    {
        3 => "RGB",
        4 => "RGBA",
        _ => "Scalar",
    };
}
