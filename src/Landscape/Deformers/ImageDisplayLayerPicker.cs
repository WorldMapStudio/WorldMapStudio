using System;
using System.Linq;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Creates a new <see cref="ImageDisplayLayer"/> with a user-chosen id (small form popup). Unlike
/// <see cref="ImagePicker"/> there is no browse/search modal — display layers are a small,
/// hand-authored list, so <see cref="ImageComponentType"/> just offers them in a plain combo box and
/// only needs this for the "New" button. Shared with <see cref="ImageDisplayLayersWindow"/>'s
/// "Add layer" button.
/// </summary>
public sealed class ImageDisplayLayerPicker
{
    private const string CreatePopupId = "Create Image Display Layer";

    private readonly ImageSystem _system;

    private bool _createOpenRequested;
    private bool _createActive;
    private EditSessionManager? _createSessions;
    private CatalogEntityRegistry? _createCatalog;
    private Action<ImageDisplayLayer>? _onCreated;
    private int _createId;
    private string _createName = "Display Layer";

    public ImageDisplayLayerPicker(ImageSystem system)
    {
        _system = system;
    }

    /// <summary>Opens the "id, name" form; <paramref name="onCreated"/> runs once the layer is
    /// committed to the catalog (already recorded into <paramref name="sessions"/>).</summary>
    public void OpenCreate(EditSessionManager sessions, CatalogEntityRegistry catalog, Action<ImageDisplayLayer> onCreated)
    {
        _createSessions = sessions;
        _createCatalog = catalog;
        _onCreated = onCreated;
        _createId = NextFreeId(catalog);
        _createName = "Display Layer";
        _createOpenRequested = true;
    }

    public void Draw()
    {
        if (_createOpenRequested)
        {
            ImGui.OpenPopup(CreatePopupId);
            _createOpenRequested = false;
            _createActive = true;
        }

        if (!_createActive)
        {
            return;
        }

        bool open = true;
        ImGuiEx.PopupModal(CreatePopupId, true, ref open, ImGuiWindowFlags.AlwaysAutoResize, () =>
        {
            ImGui.InputInt("Id", ref _createId);
            ImGui.InputText("Name", ref _createName, 128);

            string? error = ValidationError();
            if (error != null)
            {
                ImGuiEx.TextColored(CommonColors.Error, error);
            }

            if (error != null)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Create", new Vector2(120, 0)))
            {
                Commit();
                open = false;
            }

            if (error != null)
            {
                ImGui.EndDisabled();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                open = false;
            }
        });

        if (!open)
        {
            _createActive = false;
        }
    }

    private string? ValidationError()
    {
        return _system.ValidateLayerId(_createId);
    }

    private void Commit()
    {
        if (_createSessions == null || _createCatalog == null || _onCreated == null)
        {
            return;
        }

        CreateCatalogEntityCommand command = _system.BuildCreateLayerCommand(_createId, _createName, out ImageDisplayLayer layer);
        command.Apply();
        _createSessions.Record(command);
        _onCreated(layer);
    }

    /// <summary>Peeks the id <see cref="CatalogEntityRegistry.AssignId{TEntity}"/> would hand out next —
    /// the form pre-fills it but lets the user type another.</summary>
    private static int NextFreeId(CatalogEntityRegistry catalog) => catalog.PeekNextId<ImageDisplayLayer>();
}
