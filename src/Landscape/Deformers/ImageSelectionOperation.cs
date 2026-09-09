using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// The searchable "pick an image" popup, modelled on <see cref="ProceduralModelSelectionOperation"/>.
/// The preview is a flat grayscale texture built straight from the raw pixel buffer (matching
/// <see cref="Godot.Image.Format.R8"/>, the same one-byte-per-pixel layout
/// <see cref="LandscapeBatchMesh"/> uses for alpha textures) rather than a rendered 3D scene, so there
/// is no preview-owner node to construct with.
/// </summary>
public sealed class ImageSelectionOperation : IModalOperation<ImageSelectionContext>
{
    private static readonly Vector2 BodySize = new(720, 380);
    private static readonly Vector2 PreviewSize = new(220, 220);

    private string _filter = "";
    private int? _previewId;
    private bool _previewIdSet;
    private int _cachedRevision = -1;
    private int? _cachedId;
    private ImageTexture? _previewTexture;

    public ModalOperationState Draw(ImageSelectionContext context)
    {
        if (!_previewIdSet)
        {
            _previewId = context.CurrentId;
            _previewIdSet = true;
        }

        ImGui.Text("Select Image");
        ImGui.Separator();

        ImGui.SetNextItemWidth(BodySize.X);
        ImGui.InputTextWithHint("##filter", "Filter images...", ref _filter, 128);

        ImGui.BeginChild("ImageSelectionBody", BodySize, true, ImGuiWindowFlags.None);
        DrawList(context);
        ImGui.SameLine();
        DrawPreviewPanel(context);
        ImGui.EndChild();

        ImGui.Separator();
        if (ImGui.Button("Clear", new Vector2(120, 0)))
        {
            context.Select(null);
            return ModalOperationState.Confirmed;
        }

        ImGui.SameLine();
        bool canSelect = _previewId != null;
        if (!canSelect)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Select", new Vector2(120, 0)))
        {
            context.Select(_previewId);
            return ModalOperationState.Confirmed;
        }

        if (!canSelect)
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
        _previewId = null;
        _previewIdSet = false;
        _cachedId = null;
        _cachedRevision = -1;
        _previewTexture = null;
    }

    private void DrawList(ImageSelectionContext context)
    {
        Vector2 listSize = new(BodySize.X - PreviewSize.X - 24.0f, BodySize.Y - 8.0f);
        ImGui.BeginChild("ImageSelectionList", listSize, true, ImGuiWindowFlags.None);

        List<PaintImage> images = FilteredImages(context);
        if (images.Count == 0)
        {
            ImGui.TextDisabled(context.System.Images.Any() ? "No images match the filter." : "No images yet.");
            ImGui.EndChild();
            return;
        }

        ImGui.TextDisabled($"{images.Count} images");

        foreach (PaintImage image in images)
        {
            int uses = context.System.UsageCount(image.RecordId ?? -1);
            bool selected = image.RecordId == _previewId;
            if (ImGui.Selectable($"#{image.RecordId} · {image.Name} · {image.Width}x{image.Height} · {uses} uses##{image.RecordId}", selected))
            {
                _previewId = image.RecordId;
            }
        }

        ImGui.EndChild();
    }

    private List<PaintImage> FilteredImages(ImageSelectionContext context)
    {
        string filter = _filter.Trim();
        IEnumerable<PaintImage> images = context.System.Images.OrderBy(image => image.Name, StringComparer.OrdinalIgnoreCase);
        if (filter.Length == 0)
        {
            return images.ToList();
        }

        return images.Where(image =>
            image.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            image.RecordId?.ToString() == filter)
            .ToList();
    }

    private void DrawPreviewPanel(ImageSelectionContext context)
    {
        ImGui.BeginGroup();
        PaintImage? image = _previewId is int id ? context.System.FindImage(id) : null;
        string current = image == null ? "(none)" : $"#{image.RecordId} {image.Name}";
        ImGui.TextDisabled($"Preview: {current}");

        if (image == null)
        {
            ImGui.Dummy(PreviewSize);
        }
        else
        {
            ImGui.Image((IntPtr)Texture(image).GetRid().Id, PreviewSize);
        }

        PaintImage? currentImage = context.CurrentId is int currentId ? context.System.FindImage(currentId) : null;
        ImGui.TextDisabled(currentImage == null ? "Current: (none)" : $"Current: #{currentImage.RecordId} {currentImage.Name}");
        ImGui.EndGroup();
    }

    /// <summary>Rebuilds the preview texture only when the previewed image or its
    /// <see cref="PaintImage.ViewRevision"/> changed, since this allocates a texture. Keyed on
    /// <c>ViewRevision</c> rather than <c>ContentRevision</c> so a chunk streaming in updates the
    /// preview even though nothing was actually edited.</summary>
    private ImageTexture Texture(PaintImage image)
    {
        if (_previewTexture != null && _cachedId == image.RecordId && _cachedRevision == image.ViewRevision)
        {
            return _previewTexture;
        }

        _previewTexture = PaintImageTextures.Overview(image);
        _cachedId = image.RecordId;
        _cachedRevision = image.ViewRevision;
        return _previewTexture;
    }
}
