using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// The map picker: a filterable grid of cards (preview thumbnail + name + id) where clicking a card
/// enters that map and right-clicking one renames or deletes it. Creating a map swaps the same grid
/// for a small inline form, so adding a map and opening it are one dialog rather than two — a freshly
/// created map is entered straight away.
/// </summary>
public sealed class MapSelectOperation : IModalOperation<MapSystem>
{
    private static readonly Vector4 ErrorColor = new(0.95f, 0.5f, 0.4f, 1.0f);
    private static readonly Vector2 BodySize = new(768, 432);

    private readonly EditorContext _context;

    public MapSelectOperation(EditorContext context)
    {
        _context = context;
    }

    private const float CardWidth = 240.0f;
    private const float ThumbnailHeight = 135.0f;
    private const float LabelHeight = 36.0f;
    private const float Padding = 6.0f;

    private string _filter = string.Empty;
    private bool _creating;
    private int _newId;
    private string _newName = string.Empty;
    private string? _createError;

    // Card context-menu state. Only one card's menu is open at a time, so one set of fields does.
    private string _renameBuffer = string.Empty;
    private MapId? _confirmDelete;
    private Map? _pendingDelete;
    private string? _cardError;

    /// <summary>The map the user picked, once the modal reports <see cref="ModalOperationState.Confirmed"/>.</summary>
    public Map? Selected { get; private set; }

    public ModalOperationState Draw(MapSystem maps)
    {
        ImGui.TextUnformatted(_creating ? "New Map" : "Open Map");
        ImGui.Separator();

        return _creating ? DrawCreate(maps) : DrawGrid(maps);
    }

    private ModalOperationState DrawGrid(MapSystem maps)
    {
        ImGui.SetNextItemWidth(BodySize.X - 160.0f);
        ImGui.InputTextWithHint("##filter", "Filter by name or id…", ref _filter, 128);

        ImGui.SameLine();

        if (ImGui.Button("New Map", new Vector2(150, 0)))
        {
            BeginCreate(maps);
        }

        if (maps.Error != null)
        {
            ImGui.TextColored(ErrorColor, maps.Error);
        }

        if (_cardError != null)
        {
            ImGui.TextColored(ErrorColor, _cardError);
        }

        Map? clicked = null;
        ImGuiEx.Child("MapGrid", BodySize, true, ImGuiWindowFlags.None, () =>
        {
            List<Map> visible = maps.Maps.Where(Matches).ToList();
            if (visible.Count == 0)
            {
                ImGui.TextDisabled(maps.Maps.Count == 0 ? "No maps yet." : "No maps match the filter.");
                return;
            }

            float available = ImGui.GetContentRegionAvail().X;
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            int columns = Math.Max(1, (int)((available + spacing) / (CardWidth + spacing)));

            for (int index = 0; index < visible.Count; index++)
            {
                if (index % columns != 0)
                {
                    ImGui.SameLine();
                }

                if (DrawCard(maps, visible[index]))
                {
                    clicked = visible[index];
                }
            }
        });

        // Deferred out of the loop above: deleting mutates the map list the grid is walking.
        if (_pendingDelete != null)
        {
            maps.Delete(_pendingDelete, MapDeleteOptions.ContentsOnly, out _cardError);
            _pendingDelete = null;
        }

        if (ImGui.Button("Close", new Vector2(120, 0)))
        {
            return ModalOperationState.Cancelled;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Right-click a map to rename or delete it.");

        if (clicked == null)
        {
            return ModalOperationState.Running;
        }

        Selected = clicked;
        return ModalOperationState.Confirmed;
    }

    private ModalOperationState DrawCreate(MapSystem maps)
    {
        var state = ModalOperationState.Running;

        ImGuiEx.Child("MapCreate", BodySize, true, ImGuiWindowFlags.None, () =>
        {
            ImGui.TextDisabled("The id is what entities store, so match the map ids your game data uses.");
            ImGui.Spacing();

            ImGui.SetNextItemWidth(160.0f);
            ImGui.InputInt("Id", ref _newId);
            _newId = Math.Max(0, _newId);

            ImGui.SetNextItemWidth(320.0f);
            ImGui.InputText("Name", ref _newName, 64);

            if (_createError != null)
            {
                ImGui.Spacing();
                ImGui.TextColored(ErrorColor, _createError);
            }
        });

        if (ImGui.Button("Create", new Vector2(120, 0)))
        {
            Map? created = maps.Create(_newId, _newName, out _createError);
            if (created != null)
            {
                Selected = created;
                state = ModalOperationState.Confirmed;
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            _creating = false;
            _createError = null;
        }

        return state;
    }

    private void BeginCreate(MapSystem maps)
    {
        _creating = true;
        _createError = null;
        _newId = maps.NextFreeId();
        _newName = $"Map {_newId}";
    }

    private bool Matches(Map map) =>
        _filter.Length == 0
        || map.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
        || map.Id.Value.ToString(CultureInfo.InvariantCulture).Contains(_filter, StringComparison.Ordinal);

    // Cards are drawn by hand: an invisible button takes the click and reserves the layout slot, then
    // the draw list paints the frame, the preview and the labels into that rectangle. The context menu
    // has to come straight after the button, since that is the item it attaches itself to.
    private bool DrawCard(MapSystem maps, Map map)
    {
        var size = new Vector2(CardWidth, ThumbnailHeight + LabelHeight + Padding * 2.0f);
        Vector2 origin = ImGui.GetCursorScreenPos();

        ImGui.PushID(map.Id.Value);
        bool clicked = ImGui.InvisibleButton("##card", size);
        bool hovered = ImGui.IsItemHovered();
        bool menuOpen = DrawCardMenu(maps, map);

        bool current = map.Id == maps.CurrentMap;
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), 4.0f);

        Vector2 thumbnailMin = origin + new Vector2(Padding, Padding);
        Vector2 thumbnailMax = thumbnailMin + new Vector2(CardWidth - Padding * 2.0f, ThumbnailHeight);
        IntPtr thumbnail = maps.Thumbnails.TextureId(map.Id);
        if (thumbnail != IntPtr.Zero)
        {
            draw.AddImage(thumbnail, thumbnailMin, thumbnailMax);
        }
        else
        {
            const string placeholder = "No preview";
            Vector2 textSize = ImGui.CalcTextSize(placeholder);
            draw.AddRectFilled(thumbnailMin, thumbnailMax, ImGui.GetColorU32(ImGuiCol.WindowBg), 2.0f);
            draw.AddText((thumbnailMin + thumbnailMax) * 0.5f - textSize * 0.5f, ImGui.GetColorU32(ImGuiCol.TextDisabled), placeholder);
        }

        draw.AddRect(
            origin,
            origin + size,
            ImGui.GetColorU32(current ? ImGuiCol.ButtonActive : ImGuiCol.Border),
            4.0f,
            ImDrawFlags.None,
            current ? 2.0f : 1.0f);

        // Long names are clipped rather than wrapped, so every card stays the same height.
        Vector2 labelMin = new(origin.X + Padding, thumbnailMax.Y + Padding);
        draw.PushClipRect(labelMin, new Vector2(origin.X + CardWidth - Padding, origin.Y + size.Y), true);
        draw.AddText(labelMin, ImGui.GetColorU32(ImGuiCol.Text), map.DisplayName);
        draw.AddText(
            labelMin + new Vector2(0.0f, ImGui.GetTextLineHeight()),
            ImGui.GetColorU32(ImGuiCol.TextDisabled),
            current ? $"id {map.Id.Value} · current" : $"id {map.Id.Value}");
        draw.PopClipRect();

        if (hovered && !menuOpen)
        {
            ImGui.SetTooltip($"{map.DisplayName} (id {map.Id.Value})");
        }

        ImGui.PopID();
        return clicked;
    }

    // Right-click actions on a card. Renaming is always on offer — entities reference a map by id, so
    // the name is only a label — while deleting takes a confirmation and is refused outright while an
    // edit session is open.
    private bool DrawCardMenu(MapSystem maps, Map map)
    {
        if (!ImGui.BeginPopupContextItem("##actions"))
        {
            return false;
        }

        if (ImGui.IsWindowAppearing())
        {
            _renameBuffer = map.Name;
            _confirmDelete = null;
            _cardError = null;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(220.0f);
        bool committed = ImGui.InputText("##rename", ref _renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.SameLine();

        if (ImGui.Button("Rename") || committed)
        {
            _cardError = maps.Rename(map, _renameBuffer);
            if (_cardError == null)
            {
                ImGui.CloseCurrentPopup();
            }
        }

        DrawPropertiesSections(map);

        ImGui.Separator();

        if (_confirmDelete == map.Id)
        {
            ImGui.TextUnformatted($"Delete '{map.DisplayName}'?");
            ImGui.TextDisabled("Entities placed in it keep their rows and their map id.");

            if (ImGui.Button("Delete", new Vector2(100, 0)))
            {
                _pendingDelete = map;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _confirmDelete = null;
            }
        }
        else
        {
            string? blocker = maps.DeleteBlocker(map);

            ImGui.BeginDisabled(blocker != null);
            if (ImGui.Button("Delete map", new Vector2(206, 0)))
            {
                _confirmDelete = map.Id;
            }

            ImGui.EndDisabled();

            if (blocker != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(blocker);
            }
        }

        if (_cardError != null)
        {
            ImGui.TextColored(ErrorColor, _cardError);
        }

        ImGui.EndPopup();
        return true;
    }

    // Every registered IMapPropertiesSection, e.g. WoW lighting's default-light picker — this class
    // draws the heading and lets the section draw its own fields, without knowing what any of them
    // configure. Sections decide for themselves how much to offer for a map that isn't the open one:
    // only its entities are actually loaded to edit, so most will disable editing there.
    private void DrawPropertiesSections(Map map)
    {
        bool isCurrent = map.Id == _context.Maps.CurrentMap;
        foreach (IMapPropertiesSection section in _context.MapProperties.All)
        {
            ImGui.Separator();
            ImGui.TextDisabled(section.Label);
            section.Draw(_context, map, isCurrent);
        }
    }
}
