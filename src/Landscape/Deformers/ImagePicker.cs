using System;
using System.Linq;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Picks an existing <see cref="PaintImage"/> (searchable popup, modelled on
/// <see cref="ProceduralModelPicker"/>) or creates a new one with a user-chosen id (small form popup).
/// One instance is shared by every <see cref="ImageComponentType"/> inspector draw and by
/// <see cref="ImagesWindow"/>.
/// </summary>
public sealed class ImagePicker
{
    private const string CreatePopupId = "Create Image";

    private readonly ImageSystem _system;
    private readonly ModalOperator<ImageSelectionOperation, ImageSelectionContext> _modal =
        new("SelectImage", () => new ImageSelectionOperation(), new Vector2(760, 0));

    private ImageSelectionContext? _context;

    private bool _createOpenRequested;
    private bool _createActive;
    private EditSessionManager? _createSessions;
    private CatalogEntityRegistry? _createCatalog;
    private Action<PaintImage>? _onCreated;
    private int _createId;
    private string _createName = "Image";

    public ImagePicker(ImageSystem system)
    {
        _system = system;
    }

    public void Browse(int? currentId, Action<int?> select)
    {
        _context = new ImageSelectionContext(_system, currentId, select);
        _modal.Show();
    }

    /// <summary>Opens the "id, name" form; <paramref name="onCreated"/> runs once the image is
    /// committed to the catalog (already recorded into <paramref name="sessions"/>).</summary>
    public void OpenCreate(EditSessionManager sessions, CatalogEntityRegistry catalog, Action<PaintImage> onCreated)
    {
        _createSessions = sessions;
        _createCatalog = catalog;
        _onCreated = onCreated;
        _createId = NextFreeId(catalog);
        _createName = "Image";
        _createOpenRequested = true;
    }

    public void Draw()
    {
        if (_context != null)
        {
            ModalOperationState state = _modal.Draw(_context, true, ImGuiWindowFlags.None);
            if (state is ModalOperationState.Confirmed or ModalOperationState.Cancelled)
            {
                _context = null;
            }
        }

        DrawCreatePopup();
    }

    private void DrawCreatePopup()
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
                ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), error);
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
        if (_createId <= 0)
        {
            return "Id must be positive.";
        }

        if (_createCatalog != null && _createCatalog.OfType<PaintImage>().Any(image => image.RecordId == _createId))
        {
            return $"Id {_createId} is already used.";
        }

        return null;
    }

    private void Commit()
    {
        if (_createSessions == null || _createCatalog == null || _onCreated == null)
        {
            return;
        }

        var image = new PaintImage
        {
            RecordId = _createId,
            Name = _createName.Trim().Length == 0 ? "Image" : _createName,
        };

        var command = new CreateCatalogEntityCommand(_createCatalog, image);
        command.Apply();
        _createSessions.Record(command);
        _onCreated(image);
    }

    /// <summary>Peeks the id <see cref="CatalogEntityRegistry.AssignId"/> would hand out next, without
    /// adding anything to the catalog — the form pre-fills it but lets the user type another.</summary>
    private static int NextFreeId(CatalogEntityRegistry catalog)
    {
        var probe = new PaintImage();
        catalog.AssignId(probe);
        return probe.RecordId ?? 1;
    }
}
