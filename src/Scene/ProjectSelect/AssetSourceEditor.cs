using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class AssetSourceEditor
{
    private const uint IdMaxLength = AssetSourceId.MaxLength;
    private const uint NameMaxLength = 128;

    private readonly ModalDialogHost<AddAssetSourceDialog, IList<AssetSourceSettings>> _addModal =
        new("AddAssetSource", () => new AddAssetSourceDialog(), new Vector2(320, 0));

    public void Draw(IList<AssetSourceSettings> sources)
    {
        if (ImGui.Button("Add source"))
        {
            _addModal.Show();
        }

        ImGui.Separator();

        if (sources.Count == 0)
        {
            ImGui.TextDisabled("No asset sources configured.");
            _addModal.Draw(sources, true, ImGuiWindowFlags.None);
            return;
        }

        for (int i = 0; i < sources.Count; i++)
        {
            AssetSourceSettings source = sources[i];
            ImGui.PushID(i);

            bool open = ImGui.CollapsingHeader($"{source.Name} ({AssetSourceTypeRegistry.Label(source.Type)})##source", ImGuiTreeNodeFlags.DefaultOpen);
            if (open)
            {
                bool enabled = source.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                {
                    source.Enabled = enabled;
                }

                string name = source.Name;
                if (ImGui.InputText("Name", ref name, NameMaxLength))
                {
                    source.Name = name;
                }

                string id = source.Id;
                if (ImGui.InputText("Source id", ref id, IdMaxLength))
                {
                    source.Id = id.Trim();
                }

                if (AssetSourceId.Validate(source.Id, sources, source) is { } idError)
                {
                    ImGuiEx.TextColored(CommonColors.Error, idError);
                }

                if (AssetSourceTypeRegistry.Find(source.Type) is { } definition)
                {
                    definition.DrawSettings(source);
                }
                else
                {
                    ImGui.TextDisabled("No editor is registered for this asset source type.");
                }

                if (ImGui.SmallButton("Remove"))
                {
                    sources.RemoveAt(i);
                    ImGui.PopID();
                    return;
                }
            }

            ImGui.PopID();
        }

        _addModal.Draw(sources, true, ImGuiWindowFlags.None);
    }

    internal static string UniqueName(string prefix, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken);
        for (int i = 1; ; i++)
        {
            string candidate = $"{prefix} {i}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
