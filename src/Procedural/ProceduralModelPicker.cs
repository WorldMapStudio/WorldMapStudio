using System;
using System.Linq;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Picks an existing <see cref="ProceduralModel"/> (searchable popup, modelled on
/// <see cref="ModelAssetPicker"/>) or creates a new one with a user-chosen id (small form popup).
/// One instance is shared by every <see cref="ProceduralComponentType"/> inspector draw and by
/// <see cref="ProceduralModelsWindow"/>.
/// </summary>
public sealed class ProceduralModelPicker
{
    private const string CreatePopupId = "Create Procedural Model";

    private readonly ProceduralSystem _system;
    private readonly Node _previewOwner;
    private readonly ModalOperator<ProceduralModelSelectionOperation, ProceduralModelSelectionContext> _modal =
        new("SelectProceduralModel", () => new ProceduralModelSelectionOperation(), new Vector2(940, 0));

    private ProceduralModelSelectionContext? _context;

    private bool _createOpenRequested;
    private bool _createActive;
    private EditSessionManager? _createSessions;
    private CatalogEntityRegistry? _createCatalog;
    private Action<ProceduralModel>? _onCreated;
    private int _createId;
    private string _createName = "Model";
    private string _createFunctionId = "";

    public ProceduralModelPicker(ProceduralSystem system, Node previewOwner)
    {
        _system = system;
        _previewOwner = previewOwner;
    }

    public void Browse(int? currentId, Action<int?> select)
    {
        _context = new ProceduralModelSelectionContext(_system, _previewOwner, currentId, select);
        _modal.Show();
    }

    /// <summary>Opens the "id, name, function" form; <paramref name="onCreated"/> runs once the model
    /// is committed to the catalog (already recorded into <paramref name="sessions"/>).</summary>
    public void OpenCreate(EditSessionManager sessions, CatalogEntityRegistry catalog, Action<ProceduralModel> onCreated)
    {
        _createSessions = sessions;
        _createCatalog = catalog;
        _onCreated = onCreated;
        _createId = NextFreeId(catalog);
        _createName = "Model";
        _createFunctionId = _system.All.FirstOrDefault()?.Id ?? "";
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
            DrawFunctionCombo();

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

    private void DrawFunctionCombo()
    {
        IProceduralFunction? bound = _system.Find(_createFunctionId);
        string label = bound?.DisplayName ?? "(none)";
        if (!ImGui.BeginCombo("Function", label))
        {
            return;
        }

        foreach (IProceduralFunction function in _system.All)
        {
            if (ImGui.Selectable(function.DisplayName, function.Id == _createFunctionId))
            {
                _createFunctionId = function.Id;
            }
        }

        ImGui.EndCombo();
    }

    private string? ValidationError()
    {
        if (_createId <= 0)
        {
            return "Id must be positive.";
        }

        if (_createCatalog != null && _createCatalog.OfType<ProceduralModel>().Any(model => model.RecordId == _createId))
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

        var model = new ProceduralModel
        {
            RecordId = _createId,
            Name = _createName.Trim().Length == 0 ? "Model" : _createName,
            FunctionId = _createFunctionId,
        };
        model.ReplaceNetwork(DefaultNetwork());

        var command = new CreateCatalogEntityCommand(_createCatalog, model);
        command.Apply();
        _createSessions.Record(command);
        _onCreated(model);
    }

    /// <summary>Peeks the id <see cref="CatalogEntityRegistry.AssignId{TEntity}"/> would hand out next —
    /// the form pre-fills it but lets the user type another.</summary>
    private static int NextFreeId(CatalogEntityRegistry catalog) => catalog.PeekNextId<ProceduralModel>();

    /// <summary>A single edge to start from, the same seed <c>ProceduralComponentType.Create</c> used
    /// to give before models existed separately from components. Also what <see cref="ProceduralModelFactory.Create"/>
    /// seeds a script- or link-created model with, so every creation path starts from the same shape.</summary>
    internal static VertexNetwork DefaultNetwork()
    {
        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(-1.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);
        return network;
    }
}
