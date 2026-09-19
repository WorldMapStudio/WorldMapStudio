using System;
using System.Linq;
using System.Threading.Tasks;
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
    private readonly ModalDialogHost<ImageSelectionDialog, ImageSelectionContext> _modal =
        new("SelectImage", () => new ImageSelectionDialog(), new Vector2(760, 0));

    private ImageSelectionContext? _context;

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
    private Task<DiskImageSourceResult?>? _createDiskProbe;
    private string _createDiskPattern = PaintImage.DefaultDiskTilePattern;

    // The folder the last "tile folder" pick chose, so editing the pattern can re-probe it; null once
    // a single file was picked instead. Kept separately from _createDiskSource, which is the probe
    // result rather than the raw pick.
    private string? _createDiskFolder;

    // Remembered across opens so the native dialog starts where the user last was.
    private string _diskStartDirectory = "";

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
        _createDiskProbe = null;
        _createDiskFolder = null;
        _createDiskPattern = PaintImage.DefaultDiskTilePattern;
        _createOpenRequested = true;
    }

    public void Draw()
    {
        if (_context != null)
        {
            ModalDialogState state = _modal.Draw(_context, true, ImGuiWindowFlags.None);
            if (state is ModalDialogState.Confirmed or ModalDialogState.Cancelled)
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
                ImGui.TextDisabled($"= {ImageSystem.ClampedPixelSize(_createChunksX, _createChunkSize)} x {ImageSystem.ClampedPixelSize(_createChunksY, _createChunkSize)} px total");
                ImGui.TextDisabled("Fixed once created — see .godot/ImageChunkPlan.md for why.");
            }

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

    /// <summary>Disk storage takes its geometry from the file(s) the OS picker chose, not the form
    /// fields. <see cref="DiskImageProbe"/> reads the pick on a background task and the result lands in
    /// <see cref="_createDiskSource"/>.</summary>
    private void DrawDiskSourceRow()
    {
        ImGui.SetNextItemWidth(220.0f);
        if (ImGui.InputTextWithHint("Tile pattern", PaintImage.DefaultDiskTilePattern, ref _createDiskPattern, 64)
            && _createDiskFolder is { } folder)
        {
            _createDiskSource = null;
            _createDiskProbe = DiskImageProbe.ProbeFolderAsync(folder, _createDiskPattern);
        }

        if (ImGui.Button("Choose image file..."))
        {
            NativeFileDialog.PickFile("Choose image file", ["*.png,*.exr ; Images (PNG, EXR)"], _diskStartDirectory, path =>
            {
                if (path == null)
                {
                    return;
                }

                _diskStartDirectory = System.IO.Path.GetDirectoryName(path) ?? "";
                _createDiskFolder = null;
                _createDiskSource = null;
                _createDiskProbe = DiskImageProbe.ProbeFileAsync(path);
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("Choose tile folder..."))
        {
            NativeFileDialog.PickDirectory("Choose tile folder", _diskStartDirectory, path =>
            {
                if (path == null)
                {
                    return;
                }

                _diskStartDirectory = path;
                _createDiskFolder = path;
                _createDiskSource = null;
                _createDiskProbe = DiskImageProbe.ProbeFolderAsync(path, _createDiskPattern);
            });
        }

        if (_createDiskProbe is { IsCompleted: true } done)
        {
            _createDiskSource = done.IsCompletedSuccessfully ? done.Result : null;
            _createDiskProbe = null;
        }

        if (_createDiskProbe != null)
        {
            ImGui.TextDisabled("Reading picked source...");
            return;
        }

        if (_createDiskSource is not { } source)
        {
            ImGui.TextDisabled("No source chosen yet, or the last pick could not be read.");
            return;
        }

        ImGui.TextDisabled(source.IsTiled ? $"Tiles: {source.Path}  ({source.TilePattern})" : $"File: {source.Path}");
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
        if (_system.ValidateImageId(_createId) is { } idError)
        {
            return idError;
        }

        if (_createStorageKind == PaintImageStorageKind.Disk)
        {
            if (_createDiskProbe != null)
            {
                return "Reading the picked source...";
            }

            return _createDiskSource == null ? "Choose a valid image file or tile folder." : null;
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

        NewImageSpec spec = _createStorageKind == PaintImageStorageKind.Disk && _createDiskSource is { } disk
            ? new NewImageSpec
            {
                RecordId = _createId,
                Name = _createName,
                Width = disk.Width,
                Height = disk.Height,
                ChunkSize = disk.ChunkSize,
                Components = disk.Components,
                Format = disk.Format,
                DiskPath = disk.Path,
                DiskTilePattern = disk.IsTiled ? disk.TilePattern : "",
            }
            : new NewImageSpec
            {
                RecordId = _createId,
                Name = _createName,
                Width = ImageSystem.ClampedPixelSize(_createChunksX, _createChunkSize),
                Height = ImageSystem.ClampedPixelSize(_createChunksY, _createChunkSize),
                ChunkSize = _createChunkSize,
                Components = _createComponents,
                Format = _createFormat,
            };

        CreateCatalogEntityCommand command = _system.BuildCreateCommand(spec, out PaintImage image);
        command.Apply();
        _createSessions.Record(command);
        _onCreated(image);
    }

    /// <summary>Peeks the id <see cref="CatalogEntityRegistry.AssignId{TEntity}"/> would hand out next —
    /// the form pre-fills it but lets the user type another.</summary>
    private static int NextFreeId(CatalogEntityRegistry catalog) => catalog.PeekNextId<PaintImage>();

}
