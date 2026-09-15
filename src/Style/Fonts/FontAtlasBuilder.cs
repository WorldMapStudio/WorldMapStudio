using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Rebuilds the ImGui font atlas from the active <see cref="ResolvedStyle"/>'s font slots. Owned by
/// <see cref="GodotImGui"/> and driven every frame from <see cref="EditorStyle.ApplyPending"/>; it
/// diffs a font-only signature itself, so a color/var-only style change never touches fonts. A load
/// failure never takes the editor down — the affected slot silently falls back to ImGui's built-in
/// font and a <see cref="StyleProblem"/> is recorded.
/// </summary>
public sealed class FontAtlasBuilder
{
    private const double ThrottleMs = 150.0;

    private readonly Dictionary<string, (GCHandle Handle, int Length)> _pinnedBytes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _sinceLastRebuild = Stopwatch.StartNew();

    private string? _appliedSignature;
    private Texture2D? _fontTexture;

    public void RebuildIfNeeded(GodotImGui owner, ImGuiIOPtr io, ResolvedStyle style)
    {
        string signature = ComputeSignature(style);
        if (signature == _appliedSignature)
        {
            return;
        }

        if (_appliedSignature is not null && _sinceLastRebuild.Elapsed.TotalMilliseconds < ThrottleMs)
        {
            return;
        }

        Rebuild(owner, io, style);
        _appliedSignature = signature;
        _sinceLastRebuild.Restart();
    }

    private unsafe void Rebuild(GodotImGui owner, ImGuiIOPtr io, ResolvedStyle style)
    {
        io.Fonts.Clear();

        float scale = style.FontScale > 0f ? style.FontScale : 1.0f;
        List<StyleFont> declared = StyleTokenRegistry.Fonts.OrderBy(static f => f.Id, StringComparer.Ordinal).ToList();

        // "Ui" is the atlas's first font — ImGui's implicit default for everything drawn without an
        // explicit PushFont — so it has to be added first regardless of registration order.
        StyleFont? ui = declared.FirstOrDefault(static f => f.Id == "ui");
        List<StyleFont> ordered = ui is null ? declared : [ui, .. declared.Where(f => f != ui)];

        List<StyleProblem> problems = [];
        List<(StyleFont Font, ImFontPtr Pointer)> built = new(ordered.Count);

        foreach (StyleFont font in ordered)
        {
            style.FontSlots.TryGetValue(font.Id, out StyleFontSlotValue? slot);
            float size = MathF.Max(4f, (slot?.Size ?? font.DefaultSize) * scale);

            (ImFontPtr pointer, bool isBuiltinDefault) = LoadPrimary(io, font, slot, size, problems);
            built.Add((font, pointer));

            if (!isBuiltinDefault)
            {
                MergeDefaultFallback(io);
            }
        }

        if (ordered.Count == 0)
        {
            io.Fonts.AddFontDefault();
        }

        io.Fonts.Build();
        UploadTexture(io);

        foreach ((StyleFont font, ImFontPtr pointer) in built)
        {
            font.Pointer = pointer;
        }

        EditorStyle.ReportFontProblems(problems);
    }

    private unsafe (ImFontPtr Pointer, bool IsBuiltinDefault) LoadPrimary(
        ImGuiIOPtr io, StyleFont font, StyleFontSlotValue? slot, float size, List<StyleProblem> problems)
    {
        string? path;
        int faceIndex = 0;

        if (!string.IsNullOrEmpty(slot?.File))
        {
            path = slot.File;
        }
        else if (!string.IsNullOrEmpty(slot?.Family))
        {
            path = ResolveSystemFont(slot.Family, slot.Weight ?? 400, slot.Italic ?? false, out faceIndex);
        }
        else
        {
            // Neither a family nor a file was set for this slot at all — its default is ImGui's own font.
            path = null;
        }

        if (path is null)
        {
            return (io.Fonts.AddFontDefault(), true);
        }

        (GCHandle Handle, int Length)? pinned = GetOrLoadPinned(path);
        if (pinned is null)
        {
            problems.Add(new StyleProblem($"fonts.{font.Id}", $"Could not load '{path}'; using the built-in default font."));
            return (io.Fonts.AddFontDefault(), true);
        }

        ImFontConfig* native = ImGuiNative.ImFontConfig_ImFontConfig();
        ImFontConfigPtr config = new(native);
        config.FontDataOwnedByAtlas = false;
        config.FontNo = faceIndex;
        config.PixelSnapH = true;

        ImFontPtr result = io.Fonts.AddFontFromMemoryTTF(pinned.Value.Handle.AddrOfPinnedObject(), pinned.Value.Length, size, config);
        ImGuiNative.ImFontConfig_destroy(native);

        if (result.NativePtr == null)
        {
            problems.Add(new StyleProblem($"fonts.{font.Id}", $"Failed to parse '{path}'; using the built-in default font."));
            return (io.Fonts.AddFontDefault(), true);
        }

        return (result, false);
    }

    private static unsafe void MergeDefaultFallback(ImGuiIOPtr io)
    {
        ImFontConfig* native = ImGuiNative.ImFontConfig_ImFontConfig();
        ImFontConfigPtr config = new(native);
        config.MergeMode = true;
        io.Fonts.AddFontDefault(config);
        ImGuiNative.ImFontConfig_destroy(native);
    }

    private static string? ResolveSystemFont(string family, int weight, bool italic, out int faceIndex)
    {
        SystemFontVariant? variant = SystemFontCatalog.Variants(family)
            .FirstOrDefault(v => v.Weight == weight && v.Italic == italic);

        if (variant is not null)
        {
            faceIndex = variant.FaceIndex;
            return variant.Path;
        }

        // The family's variants haven't been probed yet (picker never opened for it this session) —
        // fall back to a direct OS lookup so the slot still renders something reasonable.
        faceIndex = 0;
        try
        {
            string path = Godot.OS.GetSystemFontPath(family, weight, 100, italic);
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private (GCHandle Handle, int Length)? GetOrLoadPinned(string path)
    {
        if (_pinnedBytes.TryGetValue(path, out (GCHandle Handle, int Length) cached))
        {
            return cached;
        }

        byte[]? bytes = SafeReadAllBytes(path);
        if (bytes is null)
        {
            return null;
        }

        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        (GCHandle handle, int Length) entry = (handle, bytes.Length);
        _pinnedBytes[path] = entry;
        return entry;
    }

    private static byte[]? SafeReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private unsafe void UploadTexture(ImGuiIOPtr io)
    {
        io.Fonts.GetTexDataAsRGBA32(out byte* pixelData, out int width, out int height, out int bytesPerPixel);

        byte[] pixels = new byte[width * height * bytesPerPixel];
        Marshal.Copy((IntPtr)pixelData, pixels, 0, pixels.Length);

        Image image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);

        // Built before the old texture reference is dropped: the renderer may still be reading the
        // old RID for a frame that's already in flight.
        Texture2D newTexture = ImageTexture.CreateFromImage(image);
        io.Fonts.SetTexID((IntPtr)newTexture.GetRid().Id);
        io.Fonts.ClearTexData();

        _fontTexture = newTexture;
    }

    private static string ComputeSignature(ResolvedStyle style)
    {
        System.Text.StringBuilder sb = new();
        sb.Append(style.FontScale.ToString("R"));

        foreach (StyleFont font in StyleTokenRegistry.Fonts.OrderBy(static f => f.Id, StringComparer.Ordinal))
        {
            style.FontSlots.TryGetValue(font.Id, out StyleFontSlotValue? slot);
            sb.Append('|').Append(font.Id)
              .Append(':').Append(slot?.File ?? string.Empty)
              .Append(':').Append(slot?.Family ?? string.Empty)
              .Append(':').Append(slot?.Weight ?? 400)
              .Append(':').Append(slot?.Italic ?? false)
              .Append(':').Append(slot?.Size ?? font.DefaultSize);
        }

        return sb.ToString();
    }
}
