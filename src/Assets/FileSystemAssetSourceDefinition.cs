using ImGuiNET;

namespace WorldMapStudio;

public sealed class FileSystemAssetSourceDefinition : IAssetSourceDefinition
{
    private const uint PathMaxLength = 512;

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
    }
}
