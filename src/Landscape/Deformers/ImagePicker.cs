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
    private readonly ModalOperator<DiskImageSourceSelectionOperation, DiskImageSourceSelectionContext> _diskModal =
        new("SelectDiskImageSource", () => new DiskImageSourceSelectionOperation(), new Vector2(760, 0));

    private ImageSelectionContext? _context;
    private DiskImageSourceSelectionContext? _diskContext;

    private bool _createOpenRequested;
    private bool _createActive;
    private EditSessionManager? _createSessions;
    private CatalogEntityRegistry? _createCatalog;
    private Action<PaintImage>? _onCreated;
    private int _createId;
    private string _createName = "Image";
    private int _createChunkSize = 256;
    private int _createChunksX = 1;
    private int _createChunksY = 1;
    private int _createComponents = 1;
    private PaintImagePixelFormat _createFormat = PaintImagePixelFormat.Byte;
    private PaintImageStorageKind _createStorageKind = PaintImageStorageKind.Database;
    private DiskImageSourceResult? _createDiskSource;

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
        _createChunkSize = 256;
        _createChunksX = 1;
        _createChunksY = 1;
        _createComponents = 1;
        _createFormat = PaintImagePixelFormat.Byte;
        _createStorageKind = PaintImageStorageKind.Database;
        _createDiskSource = null;
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
            ImGui.Separator();
            DrawStorageCombo();

            if (_createStorageKind == PaintImageStorageKind.Disk)
            {
                DrawDiskSourceRow();
            }
            else
            {
                DrawFormatCombo();
                DrawPrecisionCombo();
                ImGui.InputInt("Chunk Size (px)", ref _createChunkSize);
                ImGui.InputInt("Image Width (chunks)", ref _createChunksX);
                ImGui.InputInt("Image Height (chunks)", ref _createChunksY);
                ImGui.TextDisabled($"= {ClampedPixelSize(_createChunksX, _createChunkSize)} x {ClampedPixelSize(_createChunksY, _createChunkSize)} px total");
                ImGui.TextDisabled("Fixed once created — see .godot/ImageChunkPlan.md for why.");
            }

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

            // Drawn from inside the create popup's content so its OpenPopup/BeginPopupModal nest one
            // level deeper rather than replacing the create popup on the stack.
            if (_diskContext != null)
            {
                ModalOperationState diskState = _diskModal.Draw(_diskContext, true, ImGuiWindowFlags.None);
                if (diskState is ModalOperationState.Confirmed or ModalOperationState.Cancelled)
                {
                    _diskContext = null;
                }
            }
        });

        if (!open)
        {
            _createActive = false;
        }
    }

    private static string ComponentsLabel(int components) => components switch
    {
        3 => "RGB",
        4 => "RGBA",
        _ => "Scalar",
    };

    private static string StorageLabel(PaintImageStorageKind kind) => kind switch
    {
        PaintImageStorageKind.Disk => "Disk (PNG / EXR files)",
        _ => "Database",
    };

    private void DrawStorageCombo()
    {
        if (ImGui.BeginCombo("Storage", StorageLabel(_createStorageKind)))
        {
            foreach (PaintImageStorageKind candidate in new[] { PaintImageStorageKind.Database, PaintImageStorageKind.Disk })
            {
                if (ImGui.Selectable(StorageLabel(candidate), candidate == _createStorageKind))
                {
                    _createStorageKind = candidate;
                }
            }

            ImGui.EndCombo();
        }
    }

    /// <summary>Disk storage takes its geometry from the file(s) picked, not the form fields — the
    /// browse modal probes them and drops the result into <see cref="_createDiskSource"/>.</summary>
    private void DrawDiskSourceRow()
    {
        if (ImGui.Button("Browse disk source..."))
        {
            _diskContext = new DiskImageSourceSelectionContext(_system.Context.Assets, result => _createDiskSource = result);
            _diskModal.Show();
        }

        if (_createDiskSource is not { } source)
        {
            ImGui.TextDisabled("No file or tile folder selected.");
            return;
        }

        ImGui.TextDisabled(source.IsTiled ? $"Tiles: {source.Path}/  ({source.TilePattern})" : $"File: {source.Path}");
        ImGui.TextDisabled(
            $"{source.Width} x {source.Height} px · chunk {source.ChunkSize} · {ComponentsLabel(source.Components)}" +
            (source.Format == PaintImagePixelFormat.Float32 ? " · f32" : ""));
    }

    private void DrawFormatCombo()
    {
        if (ImGui.BeginCombo("Format", ComponentsLabel(_createComponents)))
        {
            foreach (int candidate in new[] { 1, 3, 4 })
            {
                if (ImGui.Selectable(ComponentsLabel(candidate), candidate == _createComponents))
                {
                    _createComponents = candidate;
                }
            }

            ImGui.EndCombo();
        }
    }

    private static string PrecisionLabel(PaintImagePixelFormat format) => format switch
    {
        PaintImagePixelFormat.Float32 => "Float32 (heightmap)",
        _ => "Byte",
    };

    /// <summary>Only meaningful on a Scalar image — see <see cref="PaintImage.ConfigureNew"/> — so this
    /// disables itself and snaps back to Byte the moment "Format" leaves Scalar, rather than letting the
    /// user set up a combination <see cref="PaintImage.ConfigureNew"/> would silently downgrade anyway.</summary>
    private void DrawPrecisionCombo()
    {
        bool available = _createComponents == 1;
        if (!available)
        {
            _createFormat = PaintImagePixelFormat.Byte;
            ImGui.BeginDisabled();
        }

        if (ImGui.BeginCombo("Precision", PrecisionLabel(_createFormat)))
        {
            foreach (PaintImagePixelFormat candidate in new[] { PaintImagePixelFormat.Byte, PaintImagePixelFormat.Float32 })
            {
                if (ImGui.Selectable(PrecisionLabel(candidate), candidate == _createFormat))
                {
                    _createFormat = candidate;
                }
            }

            ImGui.EndCombo();
        }

        if (!available)
        {
            ImGui.EndDisabled();
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

        if (_createStorageKind == PaintImageStorageKind.Disk)
        {
            return _createDiskSource == null ? "Pick a disk file or tile folder." : null;
        }

        if (_createChunkSize <= 0)
        {
            return "Chunk size must be positive.";
        }

        if (_createChunksX <= 0 || _createChunksY <= 0)
        {
            return "Image width and height (in chunks) must be positive.";
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

        if (_createStorageKind == PaintImageStorageKind.Disk && _createDiskSource is { } disk)
        {
            image.ConfigureNew(disk.Width, disk.Height, disk.ChunkSize, disk.Components, disk.Format);
            image.ConfigureDiskSource(disk.SourceId, disk.Path, disk.IsTiled ? disk.TilePattern : "");
        }
        else
        {
            image.ConfigureNew(
                ClampedPixelSize(_createChunksX, _createChunkSize),
                ClampedPixelSize(_createChunksY, _createChunkSize),
                _createChunkSize,
                _createComponents,
                _createFormat);
        }

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

    // long multiplication first so a user typing an absurd chunk count cannot overflow int before the
    // clamp gets a chance to catch it.
    private static int ClampedPixelSize(int chunks, int chunkSize) =>
        (int)Math.Clamp((long)Math.Max(0, chunks) * Math.Max(0, chunkSize), 1, PaintImage.MaxDimension);
}
