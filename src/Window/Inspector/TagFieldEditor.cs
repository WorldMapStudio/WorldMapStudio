using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "Tags" field of the inspector: a chip per tag the selection carries, and a combo to add more. Drawn
/// by <see cref="InspectorWindow"/> under whichever inspector the selection resolves to, so it reads
/// and behaves the same for an editor-authored entity and one stored in a game table.
///
/// Across a multi-selection a tag that only some of the targets carry draws as a partial chip; clicking
/// it gives it to the rest, while clicking a full chip takes it off all of them.
/// </summary>
public sealed class TagFieldEditor(EditorContext editor)
{
    private const string NewTagPopup = "New tag";

    private string _newName = string.Empty;
    private string _error = string.Empty;
    private bool _openNewTag;
    private IReadOnlyList<SceneEntity> _newTagTargets = [];

    public void Draw(InspectorContext context, IReadOnlyList<SceneEntity> targets) =>
        context.Fields.Field("Tags", () => DrawField(targets));

    private void DrawField(IReadOnlyList<SceneEntity> targets)
    {
        List<SceneEntity> taggable = targets.Where(editor.Tags.CanTag).ToList();
        if (taggable.Count == 0)
        {
            ImGui.TextDisabled("This kind of entity can't be tagged.");
            return;
        }

        ImGui.Text(taggable.Count < targets.Count ? $"Tags ({taggable.Count} of {targets.Count} can be tagged)" : "Tags");

        DrawChips(taggable);
        DrawAddCombo(taggable);
        DrawNewTagPopup();
    }

    private void DrawChips(List<SceneEntity> taggable)
    {
        float right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        bool first = true;
        foreach (EntityTagDefinition tag in editor.Tags.Definitions)
        {
            if (tag.RecordId is not int id)
            {
                continue;
            }

            int carrying = taggable.Count(entity => entity.Tags.Contains(id));
            if (carrying == 0)
            {
                continue;
            }

            bool full = carrying == taggable.Count;
            string label = full ? $"{tag.Name} ×" : $"{tag.Name} {carrying}/{taggable.Count}";
            float width = ImGui.CalcTextSize(label).X + (ImGui.GetStyle().FramePadding.X * 2.0f);
            if (!first)
            {
                if (ImGui.GetCursorScreenPos().X + ImGui.GetStyle().ItemSpacing.X + width < right)
                {
                    ImGui.SameLine();
                }
            }

            first = false;
            ImGui.PushStyleColor(ImGuiCol.Button, TagColors.ToVector4(tag.Color, full ? 1.0f : 0.45f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, TagColors.ToVector4(tag.Color, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, TagColors.ToVector4(tag.Color, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, TagColors.TextOn(tag.Color));
            bool clicked = ImGui.SmallButton($"{label}##tag{id}");
            ImGui.PopStyleColor(4);

            if (clicked)
            {
                if (full)
                {
                    editor.Tags.Remove(taggable, id);
                }
                else
                {
                    editor.Tags.Add(taggable, id);
                }
            }
        }
    }

    private void DrawAddCombo(List<SceneEntity> taggable)
    {
        ImGui.SetNextItemWidth(-1.0f);
        if (!ImGui.BeginCombo("##add-tag", "Add tag..."))
        {
            return;
        }

        foreach (EntityTagDefinition tag in editor.Tags.Definitions)
        {
            if (tag.RecordId is not int id || taggable.All(entity => entity.Tags.Contains(id)))
            {
                continue;
            }

            ImGui.PushStyleColor(ImGuiCol.Text, TagColors.ToVector4(tag.Color));
            bool chosen = ImGui.Selectable($"{tag.Name}##add{id}");
            ImGui.PopStyleColor();
            if (chosen)
            {
                editor.Tags.Add(taggable, id);
            }
        }

        ImGui.Separator();
        if (ImGui.Selectable("New tag..."))
        {
            _openNewTag = true;
            _newTagTargets = taggable.ToArray();
            _newName = string.Empty;
            _error = string.Empty;
        }

        ImGui.EndCombo();
    }

    private void DrawNewTagPopup()
    {
        if (_openNewTag)
        {
            ImGui.OpenPopup(NewTagPopup);
            _openNewTag = false;
        }

        if (!ImGui.BeginPopup(NewTagPopup))
        {
            return;
        }

        bool create = ImGui.InputText("Name", ref _newName, EntityTagDefinitionFactory.NameMaxLength, ImGuiInputTextFlags.EnterReturnsTrue);
        create |= ImGui.Button("Create and apply");
        if (create)
        {
            try
            {
                editor.Tags.Create(_newName, tagWith: _newTagTargets);
                ImGui.CloseCurrentPopup();
            }
            catch (InvalidOperationException e)
            {
                _error = e.Message;
            }
        }

        if (_error.Length > 0)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.4f, 0.4f, 1.0f), _error);
        }

        ImGui.EndPopup();
    }
}
