using System;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Draws a <see cref="MeshParameter"/> list against a <see cref="MeshParameterValues"/> bag, shared by the
/// Mesh Materials window, <c>LandscapeMaterialsWindow</c> and the procedural inspector's material-slot
/// editor so none of them repeats the same drag/undo bracketing.
///
/// A numeric or color drag is bracketed into a single undo step by comparing the serialized bag at
/// activation to the bag once the drag ends; a checkbox, combo or texture pick has no such bracket, so
/// it is recorded the moment it changes.
/// </summary>
public sealed class MeshParameterEditor
{
    private readonly TextureAssetPicker _texturePicker;
    private readonly LandscapeSystem _landscape;
    private string? _dragBefore;

    public MeshParameterEditor(TextureAssetPicker texturePicker, LandscapeSystem landscape)
    {
        _texturePicker = texturePicker;
        _landscape = landscape;
    }

    public void DrawModals() => _texturePicker.Draw();

    /// <summary>
    /// Draws every parameter. <paramref name="serialized"/> must be the bag's serialized form as it
    /// was before this call (i.e. what <paramref name="values"/> was parsed from) — it is the
    /// "before" side of the undo pair a drag records. <paramref name="onChanged"/> receives the
    /// before/after serialized pair whenever a parameter's value actually changes.
    /// </summary>
    public void Draw(
        System.Collections.Generic.IReadOnlyList<MeshParameter> parameters,
        MeshParameterValues values,
        string serialized,
        Action<string, string> onChanged)
    {
        foreach (MeshParameter parameter in parameters)
        {
            ImGui.PushID(parameter.Name);
            DrawOne(parameter, values, serialized, onChanged);

            if (parameter.Description.Length > 0 && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(parameter.Description);
            }

            ImGui.PopID();
        }
    }

    private void DrawOne(MeshParameter parameter, MeshParameterValues values, string serialized, Action<string, string> onChanged)
    {
        switch (parameter.Kind)
        {
            case MeshParameterKind.Float:
            {
                float value = values.GetFloat(parameter);
                if (ImGui.DragFloat(parameter.DisplayName, ref value, 0.01f, parameter.Min, parameter.Max))
                {
                    values.Set(parameter, value);
                }

                TrackDrag(values, serialized, onChanged);
                break;
            }

            case MeshParameterKind.Int:
            {
                int value = values.GetInt(parameter);
                if (ImGui.DragInt(parameter.DisplayName, ref value, 1.0f, (int)parameter.Min, (int)parameter.Max))
                {
                    values.Set(parameter, value);
                }

                TrackDrag(values, serialized, onChanged);
                break;
            }

            case MeshParameterKind.Bool:
            {
                bool value = values.GetBool(parameter);
                if (ImGui.Checkbox(parameter.DisplayName, ref value))
                {
                    values.Set(parameter, value);
                    onChanged(serialized, values.Serialize());
                }

                break;
            }

            case MeshParameterKind.Color:
            {
                Godot.Color color = values.GetColor(parameter);
                var value = new System.Numerics.Vector4(color.R, color.G, color.B, color.A);
                if (ImGui.ColorEdit4(parameter.DisplayName, ref value))
                {
                    values.Set(parameter, new Godot.Color(value.X, value.Y, value.Z, value.W));
                }

                TrackDrag(values, serialized, onChanged);
                break;
            }

            case MeshParameterKind.Texture:
            {
                string texture = values.GetTexture(parameter);
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{parameter.DisplayName}:");
                ImGui.SameLine();
                ImGui.TextDisabled(texture.Length == 0 ? "(none)" : texture);
                ImGui.SameLine();
                if (ImGui.Button($"Browse##{parameter.Name}"))
                {
                    _texturePicker.Browse(texture, selected =>
                    {
                        values.Set(parameter, selected);
                        onChanged(serialized, values.Serialize());
                    });
                }

                if (texture.Length > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Clear##{parameter.Name}"))
                    {
                        values.Set(parameter, "");
                        onChanged(serialized, values.Serialize());
                    }
                }

                break;
            }

            case MeshParameterKind.Choice:
            {
                string bound = values.GetChoice(parameter);
                MeshParameterOption? current = System.Linq.Enumerable.FirstOrDefault(parameter.Options, option => option.Value == bound);
                string label = current?.DisplayName ?? (bound.Length == 0 ? "(none)" : bound);

                if (ImGui.BeginCombo(parameter.DisplayName, label))
                {
                    foreach (MeshParameterOption option in parameter.Options)
                    {
                        if (ImGui.Selectable($"{option.DisplayName}##{option.Value}", option.Value == bound))
                        {
                            values.Set(parameter, option.Value);
                            onChanged(serialized, values.Serialize());
                        }

                        if (option.Description.Length > 0 && ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(option.Description);
                        }
                    }

                    ImGui.EndCombo();
                }

                break;
            }

            case MeshParameterKind.Channel:
            {
                string bound = values.GetChannel(parameter);
                string label = bound.Length == 0 ? "(none)" : bound;

                if (ImGui.BeginCombo(parameter.DisplayName, label))
                {
                    if (ImGui.Selectable("(none)", bound.Length == 0))
                    {
                        values.Set(parameter, "");
                        onChanged(serialized, values.Serialize());
                    }

                    foreach (LandscapeChannel channel in _landscape.Catalog.Channels)
                    {
                        if (ImGui.Selectable(channel.Name, channel.Name == bound))
                        {
                            values.Set(parameter, channel.Name);
                            onChanged(serialized, values.Serialize());
                        }
                    }

                    ImGui.EndCombo();
                }

                break;
            }
        }
    }

    private void TrackDrag(MeshParameterValues values, string serialized, Action<string, string> onChanged)
    {
        if (ImGui.IsItemActivated())
        {
            _dragBefore = serialized;
        }

        if (!ImGui.IsItemDeactivatedAfterEdit() || _dragBefore == null)
        {
            return;
        }

        string before = _dragBefore;
        _dragBefore = null;

        string after = values.Serialize();
        if (before != after)
        {
            onChanged(before, after);
        }
    }
}
