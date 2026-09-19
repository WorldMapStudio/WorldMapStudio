using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Lists every tag with how many loaded entities carry it, and edits the catalog: create, rename,
/// recolour, delete. Each tag can also select the loaded entities that carry it, or be put on or taken
/// off the current selection.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class TagsWindow : Window
{
    private readonly EditorContext _context;

    private string _newName = string.Empty;
    private string _error = string.Empty;
    private int _renaming = -1;
    private bool _focusRename;
    private string _renameBuffer = string.Empty;

    // Loaded counts are a walk over every loaded entity per tag; held until the registry or a tag moves.
    private readonly Dictionary<int, int> _counts = [];
    private int _countsSceneVersion = -1;
    private int _countsTagVersion = -1;

    public TagsWindow(WindowManager manager)
        : base("Tags", startOpen: false, defaultSize: new Vector2(320, 360))
    {
        _context = manager.Context;
    }

    protected override void DrawContent()
    {
        DrawCreate();
        ImGui.Separator();

        List<EntityTagDefinition> tags = _context.Tags.Definitions.ToList();
        if (tags.Count == 0)
        {
            ImGui.TextDisabled("No tags yet.");
            return;
        }

        RefreshCounts();
        IReadOnlyList<SceneEntity> selection = _context.Selection.Selected.OfType<SceneEntity>().ToList();

        if (!ImGui.BeginChild("tags-scroll", Vector2.Zero, true))
        {
            ImGui.EndChild();
            return;
        }

        EntityTagDefinition? doomed = null;
        foreach (EntityTagDefinition tag in tags)
        {
            if (tag.RecordId is not int id)
            {
                continue;
            }

            ImGui.PushID(id);
            DrawTag(tag, id, selection, ref doomed);
            ImGui.PopID();
        }

        ImGui.EndChild();

        // Deleted after the list has drawn, since it changes the list being walked.
        if (doomed != null)
        {
            _context.Tags.Delete(doomed);
        }
    }

    private void DrawCreate()
    {
        ImGui.SetNextItemWidth(-90.0f);
        bool create = ImGui.InputTextWithHint("##new-tag", "New tag name...", ref _newName, EntityTagDefinitionFactory.NameMaxLength, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        create |= ImGui.Button("Create");
        if (create)
        {
            try
            {
                _context.Tags.Create(_newName);
                _newName = string.Empty;
                _error = string.Empty;
            }
            catch (InvalidOperationException e)
            {
                _error = e.Message;
            }
        }

        if (_error.Length > 0)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), _error);
        }
    }

    private void DrawTag(EntityTagDefinition tag, int id, IReadOnlyList<SceneEntity> selection, ref EntityTagDefinition? doomed)
    {
        Vector4 packed = TagColors.ToVector4(tag.Color);
        var color = new Vector3(packed.X, packed.Y, packed.Z);
        if (ImGui.ColorEdit3("##color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            _context.Tags.SetColor(tag, TagColors.FromVector4(new Vector4(color, 1.0f)));
        }

        ImGui.SameLine();
        if (_renaming == id)
        {
            ImGui.SetNextItemWidth(140.0f);
            if (_focusRename)
            {
                ImGui.SetKeyboardFocusHere();
                _focusRename = false;
            }

            // Enter or leaving the field commits; Escape restores the old text first, so it renames to itself.
            bool entered = ImGui.InputText("##rename", ref _renameBuffer, EntityTagDefinitionFactory.NameMaxLength, ImGuiInputTextFlags.EnterReturnsTrue);
            if (entered || ImGui.IsItemDeactivated())
            {
                CommitRename(tag);
            }
        }
        else
        {
            ImGui.Text(tag.Name);
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _renaming = id;
                _renameBuffer = tag.Name;
                _focusRename = true;
            }
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"({_counts.GetValueOrDefault(id)} loaded)");

        if (ImGui.SmallButton("Select loaded"))
        {
            SelectLoaded(id);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(selection.Count == 0);
        if (ImGui.SmallButton("Add to selection"))
        {
            _context.Tags.Add(selection, id);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Remove from selection"))
        {
            _context.Tags.Remove(selection, id);
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.SmallButton("Delete"))
        {
            doomed = tag;
        }

        ImGui.Separator();
    }

    private void CommitRename(EntityTagDefinition tag)
    {
        try
        {
            _context.Tags.Rename(tag, _renameBuffer);
            _error = string.Empty;
        }
        catch (InvalidOperationException e)
        {
            _error = e.Message;
        }

        _renaming = -1;
    }

    private void SelectLoaded(int tagId)
    {
        _context.Selection.Clear();
        foreach (SceneEntity entity in _context.Scene.InView)
        {
            if (entity.Tags.Contains(tagId))
            {
                _context.Selection.Add(entity);
            }
        }
    }

    private void RefreshCounts()
    {
        if (_countsSceneVersion == _context.Scene.Version && _countsTagVersion == _context.Scene.TagVersion)
        {
            return;
        }

        _countsSceneVersion = _context.Scene.Version;
        _countsTagVersion = _context.Scene.TagVersion;
        _counts.Clear();
        foreach (SceneEntity entity in _context.Scene.Entities)
        {
            foreach (int tagId in entity.Tags)
            {
                _counts[tagId] = _counts.GetValueOrDefault(tagId) + 1;
            }
        }
    }
}
