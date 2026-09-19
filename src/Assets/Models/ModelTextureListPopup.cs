using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Shows the distinct textures a model's surfaces reference as a thumbnail grid, read from whichever
/// <see cref="IMeshMaterialType"/> each surface's material uses rather than assuming the built-in
/// vocabulary, so a format-specific material (e.g. an M2/WMO one) is covered too. Card/thumbnail
/// drawing mirrors <see cref="TextureSelectionDialog"/>'s picker grid, minus selection/filtering
/// since this is a read-only listing rather than a picker. Owned and drawn by
/// <see cref="ModelRendererComponentType"/>.
/// </summary>
public sealed class ModelTextureListPopup
{
    private const string PopupId = "Model Textures";
    private static readonly Vector2 BodySize = new(768, 432);

    private const float CardWidth = 160.0f;
    private const float ThumbnailSize = 128.0f;
    private const float LabelHeight = 22.0f;
    private const float Padding = 6.0f;

    private readonly AssetSystem _assets;
    private readonly MeshMaterialSystem _materials;
    private readonly Dictionary<string, Task<Texture2D?>> _previews = new();

    private bool _openRequested;
    private bool _active;
    private List<string> _textures = [];

    public ModelTextureListPopup(AssetSystem assets, MeshMaterialSystem materials)
    {
        _assets = assets;
        _materials = materials;
    }

    public void Open(ModelAsset model)
    {
        _textures = CollectTextures(model);
        _previews.Clear();
        _openRequested = true;
    }

    public void Draw()
    {
        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;
            _active = true;
        }

        if (!_active)
        {
            return;
        }

        bool open = true;
        ImGuiEx.PopupModal(PopupId, true, ref open, ImGuiWindowFlags.AlwaysAutoResize, () =>
        {
            if (_textures.Count == 0)
            {
                ImGui.TextDisabled("(no textures)");
            }
            else
            {
                ImGui.Text($"{_textures.Count} texture(s):");
                DrawGrid();
            }

            if (ImGui.Button("Close", new Vector2(120, 0)))
            {
                open = false;
            }
        });

        if (!open)
        {
            _active = false;
        }
    }

    private void DrawGrid()
    {
        ImGui.BeginChild("ModelTextureGrid", BodySize, true);

        float available = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        int columns = Math.Max(1, (int)((available + spacing) / (CardWidth + spacing)));

        for (int i = 0; i < _textures.Count; i++)
        {
            if (i % columns != 0)
            {
                ImGui.SameLine();
            }

            DrawCard(_textures[i]);
        }

        ImGui.EndChild();
    }

    private void DrawCard(string path)
    {
        var size = new Vector2(CardWidth, ThumbnailSize + LabelHeight + Padding * 2.0f);
        Vector2 origin = ImGui.GetCursorScreenPos();

        ImGui.PushID(path);
        ImGui.Dummy(size);
        bool hovered = ImGui.IsItemHovered();

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), 4.0f);

        float thumbnailX = origin.X + (CardWidth - ThumbnailSize) * 0.5f;
        Vector2 thumbnailMin = new(thumbnailX, origin.Y + Padding);
        Vector2 thumbnailMax = thumbnailMin + new Vector2(ThumbnailSize, ThumbnailSize);
        DrawPreview(path, thumbnailMin, thumbnailMax);

        draw.AddRect(origin, origin + size, ImGui.GetColorU32(ImGuiCol.Border), 4.0f);

        Vector2 labelMin = new(origin.X + Padding, thumbnailMax.Y + Padding);
        Vector2 labelMax = new(origin.X + CardWidth - Padding, origin.Y + size.Y);
        draw.PushClipRect(labelMin, labelMax, true);
        DrawCenteredText(draw, labelMin, labelMax.X, ImGui.GetColorU32(ImGuiCol.Text), AssetPath.FileName(path));
        draw.PopClipRect();

        if (hovered)
        {
            ImGui.SetTooltip(path);
        }

        ImGui.PopID();
    }

    private void DrawPreview(string path, Vector2 min, Vector2 max)
    {
        Task<Texture2D?> preview = PreviewTask(path);
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        if (preview.IsCompletedSuccessfully && preview.Result != null)
        {
            draw.AddImage((IntPtr)preview.Result.GetRid().Id, min, max);
            return;
        }

        draw.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.WindowBg), 2.0f);
        string label = preview.IsCompleted ? "Failed" : "Loading";
        Vector2 textSize = ImGui.CalcTextSize(label);
        draw.AddText((min + max) * 0.5f - textSize * 0.5f, ImGui.GetColorU32(ImGuiCol.TextDisabled), label);
    }

    private static void DrawCenteredText(ImDrawListPtr draw, Vector2 lineMin, float lineMaxX, uint color, string text)
    {
        Vector2 textSize = ImGui.CalcTextSize(text);
        float available = lineMaxX - lineMin.X;
        float x = lineMin.X + Math.Max(0.0f, (available - textSize.X) * 0.5f);
        draw.AddText(new Vector2(x, lineMin.Y), color, text);
    }

    private Task<Texture2D?> PreviewTask(string path)
    {
        if (_previews.TryGetValue(path, out Task<Texture2D?>? preview))
        {
            return preview;
        }

        preview = _assets.LoadTextureAssetAsync(path);
        _previews[path] = preview;
        return preview;
    }

    private List<string> CollectTextures(ModelAsset model)
    {
        var textures = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModelSurface surface in model.Surfaces)
        {
            foreach (MeshMaterial material in surface.Materials)
            {
                if (_materials.Find(material.TypeId) is not { } type)
                {
                    continue;
                }

                foreach (MeshParameter parameter in type.Parameters)
                {
                    if (parameter.Kind != MeshParameterKind.Texture)
                    {
                        continue;
                    }

                    string path = material.Values.GetTexture(parameter);
                    if (path.Length > 0)
                    {
                        textures.Add(path);
                    }
                }
            }
        }

        return [.. textures];
    }
}
