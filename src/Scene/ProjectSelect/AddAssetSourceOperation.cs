using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class AddAssetSourceOperation : IModalOperation<IList<AssetSourceSettings>>
{
    private AssetSourceType _type = AssetSourceType.FileSystem;
    private string _id = "";
    private string _name = "";
    private string _rootPath = "";
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

        if (ImGui.BeginCombo("Type", Label(_type)))
        {
            foreach (AssetSourceType type in System.Enum.GetValues<AssetSourceType>())
            {
                if (ImGui.Selectable(Label(type), type == _type))
                {
                    _type = type;
                    ResetDefaults(sources);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(220.0f);
        ImGui.InputText("Source id", ref _id, AssetSourceId.MaxLength);
        ImGui.SetNextItemWidth(220.0f);
        ImGui.InputText("Name", ref _name, 128);

        if (_type == AssetSourceType.FileSystem)
        {
            ImGui.SetNextItemWidth(300.0f);
            ImGui.InputText("Root path", ref _rootPath, 512);
        }

        ImGui.TextDisabled("Texture paths use this as source_id::relative/path.png.");

        if (_error != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _error);
        }

        ImGui.Separator();

        if (ImGui.Button("Add", new Vector2(120, 0)))
        {
            _error = AssetSourceId.Validate(_id.Trim(), sources, null);
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
        _id = _type switch
        {
            AssetSourceType.FileSystem => AssetSourceId.Unique("textures", sources.Select(source => source.Id)),
            _ => AssetSourceId.Unique("assets", sources.Select(source => source.Id)),
        };
        _name = _type switch
        {
            AssetSourceType.FileSystem => AssetSourceEditor.UniqueName("Textures", sources.Select(source => source.Name)),
            _ => AssetSourceEditor.UniqueName("Assets", sources.Select(source => source.Name)),
        };
        _rootPath = "";
    }

    private AssetSourceSettings CreateSource() => _type switch
    {
        AssetSourceType.FileSystem => new AssetSourceSettings
        {
            Id = _id.Trim(),
            Type = AssetSourceType.FileSystem,
            Name = _name.Trim().Length == 0 ? _id.Trim() : _name.Trim(),
            RootPath = _rootPath.Trim(),
        },
        _ => new AssetSourceSettings
        {
            Id = _id.Trim(),
            Type = _type,
            Name = _name.Trim().Length == 0 ? _id.Trim() : _name.Trim(),
        },
    };

    private static string Label(AssetSourceType type) => type switch
    {
        AssetSourceType.FileSystem => "Filesystem",
        _ => type.ToString(),
    };
}
