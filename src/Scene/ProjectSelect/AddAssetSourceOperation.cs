using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class AddAssetSourceOperation : IModalOperation<IList<AssetSourceSettings>>
{
    private IAssetSourceDefinition? _definition;
    private string _id = "";
    private string _name = "";
    private AssetSourceSettings _draft = new();
    private bool _initialized;
    private string? _error;

    public ModalOperationState Draw(IList<AssetSourceSettings> sources)
    {
        if (!_initialized)
        {
            ResetDefaults(sources);
        }

        ImGui.Text("Add Asset Source");
        ImGui.Separator();

        _definition ??= AssetSourceTypeRegistry.Definitions.FirstOrDefault();
        if (ImGui.BeginCombo("Type", _definition?.Label ?? "No registered source types"))
        {
            foreach (IAssetSourceDefinition definition in AssetSourceTypeRegistry.Definitions)
            {
                if (ImGui.Selectable(definition.Label, definition.Type == _definition?.Type))
                {
                    _definition = definition;
                    ResetDefaults(sources);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(220.0f);
        ImGui.InputText("Source id", ref _id, AssetSourceId.MaxLength);
        ImGui.SetNextItemWidth(220.0f);
        ImGui.InputText("Name", ref _name, 128);

        _definition?.DrawSettings(_draft);

        ImGui.TextDisabled("Assets are referenced by their relative path.");

        if (_error != null)
        {
            ImGuiEx.TextColored(CommonColors.Error, _error);
        }

        ImGui.Separator();

        if (ImGui.Button("Add", new Vector2(120, 0)))
        {
            _error = _definition == null ? "No asset source type is registered." : AssetSourceId.Validate(_id.Trim(), sources, null);
            if (_error == null)
            {
                sources.Add(CreateSource());
                return ModalOperationState.Confirmed;
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            return ModalOperationState.Cancelled;
        }

        return ModalOperationState.Running;
    }

    private void ResetDefaults(IList<AssetSourceSettings> sources)
    {
        _initialized = true;
        _error = null;
        _definition ??= AssetSourceTypeRegistry.Definitions.FirstOrDefault(definition => definition.Type == AssetSourceType.FileSystem) ??
            AssetSourceTypeRegistry.Definitions.FirstOrDefault();

        _id = AssetSourceId.Unique(_definition?.DefaultIdPrefix ?? "assets", sources.Select(source => source.Id));
        _name = AssetSourceEditor.UniqueName(_definition?.DefaultNamePrefix ?? "Assets", sources.Select(source => source.Name));
        _draft = new AssetSourceSettings
        {
            Type = _definition?.Type ?? AssetSourceType.FileSystem,
        };
    }

    private AssetSourceSettings CreateSource() => new()
    {
        Id = _id.Trim(),
        Type = _definition?.Type ?? _draft.Type,
        Name = _name.Trim().Length == 0 ? _id.Trim() : _name.Trim(),
        RootPath = _draft.RootPath.Trim(),
        Properties = new Dictionary<string, string>(_draft.Properties),
    };
}
