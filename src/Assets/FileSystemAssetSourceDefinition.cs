using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(AppSystems))]
public sealed class FileSystemAssetSourceDefinition : IAssetSourceDefinition
{
    private const uint PathMaxLength = 512;

    public FileSystemAssetSourceDefinition(AppSystems app)
    {
    }

    public string Type => AssetSourceType.FileSystem;

    public string Label => "Filesystem";

    public string DefaultIdPrefix => "textures";

    public string DefaultNamePrefix => "Textures";

    public void DrawSettings(AssetSourceSettings source)
    {
        string root = source.RootPath;
        if (ImGui.InputText("Root path", ref root, PathMaxLength))
        {
            source.RootPath = root;
        }

        bool lowercase = source.GetFlag(FileSystemAssetProvider.LowercasePathsKey);
        if (ImGui.Checkbox("Lowercase paths", ref lowercase))
        {
            source.SetFlag(FileSystemAssetProvider.LowercasePathsKey, lowercase);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("The tree is stored lowercase; requested relative paths are folded before resolving.");
        }
    }
}
