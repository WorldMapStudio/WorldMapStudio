using System.Collections.Generic;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Lists the distinct texture paths a model's surfaces reference, read from whichever
/// <see cref="IMeshMaterialType"/> each surface's material uses rather than assuming the built-in
/// vocabulary, so a format-specific material (e.g. an M2/WMO one) is covered too. Owned and drawn by
/// <see cref="ModelRendererComponentType"/>, modelled on <see cref="SavePrefabPopup"/>.
/// </summary>
public sealed class ModelTextureListPopup
{
    private const string PopupId = "Model Textures";

    private readonly MeshMaterialSystem _materials;

    private bool _openRequested;
    private bool _active;
    private string _listing = "";
    private int _count;

    public ModelTextureListPopup(MeshMaterialSystem materials)
    {
        _materials = materials;
    }

    public void Open(ModelAsset model)
    {
        SortedSet<string> textures = CollectTextures(model);
        _listing = string.Join('\n', textures);
        _count = textures.Count;
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
            if (_count == 0)
            {
                ImGui.TextDisabled("(no textures)");
            }
            else
            {
                ImGui.Text($"{_count} texture(s):");
                ImGui.InputTextMultiline("##textures", ref _listing, (uint)_listing.Length + 1,
                    new Vector2(500, 300), ImGuiInputTextFlags.ReadOnly);
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

    private SortedSet<string> CollectTextures(ModelAsset model)
    {
        var textures = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
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

        return textures;
    }
}
