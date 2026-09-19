using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>Paint images and their display layers, exposed to JS as <c>wms.images</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class ImagesScriptApi : IScriptModule
{
    private const int DefaultChunkSize = 256;

    private readonly EditorContext _context;

    public string Name => "images";

    public ImagesScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    private ImageSystem Images => _context.Images;

    [ScriptFunction]
    public ImageDescriptor[] List() => Images.Images.Select(Describe).ToArray();

    [ScriptFunction]
    public ImageLayerDescriptor[] Layers() => Images.DisplayLayers.Select(Describe).ToArray();

    /// <summary>
    /// Creates a database-backed image, undoably. <paramref name="options"/>: <c>name</c>, <c>width</c> and
    /// <c>height</c> in pixels, <c>chunkSize</c> (default 256), <c>components</c> (1, 3 or 4) and
    /// <c>format</c> (<c>"Byte"</c> or <c>"Float32"</c>, the latter for a single component only).
    /// </summary>
    [ScriptFunction]
    public ImageDescriptor Create(object? options)
    {
        IDictionary<string, object?> map = Options(options);
        int chunkSize = Int(map, "chunkSize", DefaultChunkSize);
        int components = Int(map, "components", 1);
        var spec = new NewImageSpec
        {
            Name = Text(map, "name") ?? "Image",
            Width = Int(map, "width", chunkSize),
            Height = Int(map, "height", chunkSize),
            ChunkSize = chunkSize,
            Components = components,
            Format = Text(map, "format") is { } format
                ? Enum.Parse<PaintImagePixelFormat>(format, ignoreCase: true)
                : PaintImagePixelFormat.Byte,
        };

        if (components is not (1 or 3 or 4))
        {
            throw new ArgumentException("components must be 1, 3 or 4.");
        }

        return Describe(Record(Images.BuildCreateCommand(spec, out PaintImage image), image));
    }

    /// <summary>Creates an image backed by a PNG or EXR file, or by a folder of tiles when
    /// <c>path</c> is a directory. <paramref name="options"/>: <c>name</c>, <c>path</c>,
    /// <c>tilePattern</c> (for a folder, default <c>{x}_{y}.png</c>).</summary>
    [ScriptFunction]
    public ImageDescriptor CreateFromDisk(object? options)
    {
        IDictionary<string, object?> map = Options(options);
        string path = Text(map, "path") ?? throw new ArgumentException("path is required.");
        string pattern = Text(map, "tilePattern") ?? PaintImage.DefaultDiskTilePattern;

        DiskImageSourceResult? source = Directory.Exists(path)
            ? BlockingWork.Run(() => DiskImageProbe.ProbeFolderAsync(path, pattern))
            : BlockingWork.Run(() => DiskImageProbe.ProbeFileAsync(path));
        if (source == null)
        {
            throw new InvalidOperationException($"'{path}' is not a readable image file or tile folder.");
        }

        var spec = new NewImageSpec
        {
            Name = Text(map, "name") ?? "Image",
            Width = source.Width,
            Height = source.Height,
            ChunkSize = source.ChunkSize,
            Components = source.Components,
            Format = source.Format,
            DiskPath = source.Path,
            DiskTilePattern = source.IsTiled ? source.TilePattern : "",
        };

        return Describe(Record(Images.BuildCreateCommand(spec, out PaintImage image), image));
    }

    /// <summary>Copies an image, undoably. The copy is always database-backed.</summary>
    [ScriptFunction]
    public ImageDescriptor Duplicate(int id)
    {
        PaintImage image = Require(id);
        return Describe(Record(Images.BuildDuplicateCommand(image, out PaintImage clone), clone));
    }

    /// <summary>Deletes an image, undoably. Refused while an entity uses it.</summary>
    [ScriptFunction]
    public void Delete(int id)
    {
        IEditCommand command = Images.BuildDeleteCommand(Require(id));
        command.Apply();
        _context.EditSessions.Record(command);
    }

    /// <summary>The image's first component at (u, v) in 0-1, bilinear. 0-1 for a byte image, raw for a float one.</summary>
    [ScriptFunction]
    public double Sample(int id, double u, double v) => Require(id).CreateSampler().Sample((float)u, (float)v);

    /// <summary>The image at (u, v) as [r, g, b, a] in 0-1.</summary>
    [ScriptFunction]
    public double[] SampleColor(int id, double u, double v)
    {
        Color color = Require(id).CreateSampler().SampleColor((float)u, (float)v);
        return [color.R, color.G, color.B, color.A];
    }

    private PaintImage Require(int id) =>
        Images.FindImage(id) ?? throw new InvalidOperationException($"No image with id {id}.");

    private T Record<T>(IEditCommand command, T result)
    {
        command.Apply();
        _context.EditSessions.Record(command);
        return result;
    }

    private ImageDescriptor Describe(PaintImage image) => new(image, Images.UsageCount(image.RecordId ?? -1));

    private ImageLayerDescriptor Describe(ImageDisplayLayer layer) =>
        new(layer, Images.DisplayLayerUsageCount(layer.RecordId ?? -1));

    private static IDictionary<string, object?> Options(object? options) =>
        ScriptJson.AsMap(options) is { } map
            ? new Dictionary<string, object?>(map, StringComparer.OrdinalIgnoreCase)
            : throw new ArgumentException("An options object is required.");

    private static string? Text(IDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private static int Int(IDictionary<string, object?> map, string key, int fallback) =>
        map.TryGetValue(key, out object? value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : fallback;
}

/// <summary>A <see cref="PaintImage"/> as <c>wms.images</c> reports it.</summary>
public sealed class ImageDescriptor
{
    public ImageDescriptor(PaintImage image, int usageCount)
    {
        Id = image.RecordId;
        Name = image.Name;
        Width = image.Width;
        Height = image.Height;
        ChunkSize = image.ChunkSize;
        Components = image.Components;
        Format = image.Format.ToString();
        StorageKind = image.StorageKind.ToString();
        UsageCount = usageCount;
    }

    [ScriptProperty] public int? Id { get; }
    [ScriptProperty] public string Name { get; }
    [ScriptProperty] public int Width { get; }
    [ScriptProperty] public int Height { get; }
    [ScriptProperty] public int ChunkSize { get; }
    [ScriptProperty] public int Components { get; }
    [ScriptProperty] public string Format { get; }
    [ScriptProperty] public string StorageKind { get; }
    [ScriptProperty] public int UsageCount { get; }
}

/// <summary>An <see cref="ImageDisplayLayer"/> as <c>wms.images</c> reports it.</summary>
public sealed class ImageLayerDescriptor
{
    public ImageLayerDescriptor(ImageDisplayLayer layer, int usageCount)
    {
        Id = layer.RecordId;
        Name = layer.Name;
        DisplayMode = layer.DisplayMode.ToString();
        ColorSource = layer.ColorSource.ToString();
        UsageCount = usageCount;
    }

    [ScriptProperty] public int? Id { get; }
    [ScriptProperty] public string Name { get; }
    [ScriptProperty] public string DisplayMode { get; }
    [ScriptProperty] public string ColorSource { get; }
    [ScriptProperty] public int UsageCount { get; }
}
