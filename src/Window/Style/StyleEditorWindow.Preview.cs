using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>A fixed gallery of widget states — the states hardest to reach anywhere else in the
/// editor — so a style change can be eyeballed in one place.</summary>
public sealed partial class StyleEditorWindow
{
    private bool _previewCheckbox = true;
    private bool _previewCheckboxDisabled;
    private float _previewSlider = 0.4f;
    private int _previewCombo;
    private string _previewText = "Sample text";
    private bool _previewTreeOpen = true;
    private readonly float[] _previewPlot = [0.2f, 0.6f, 0.3f, 0.9f, 0.5f, 0.7f, 0.1f, 0.8f];

    private void DrawPreviewTab()
    {
        ImGui.TextUnformatted("Buttons");
        ImGui.Button("Button");
        ImGui.SameLine();
        ImGui.BeginDisabled();
        ImGui.Button("Disabled");
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.SmallButton("Small");

        ImGui.Spacing();
        ImGui.TextUnformatted("Frames");
        ImGui.Checkbox("Checkbox", ref _previewCheckbox);
        ImGui.SameLine();
        ImGui.BeginDisabled();
        ImGui.Checkbox("Disabled##preview", ref _previewCheckboxDisabled);
        ImGui.EndDisabled();
        ImGui.SliderFloat("Slider", ref _previewSlider, 0f, 1f);
        ImGui.Combo("Combo", ref _previewCombo, "Alpha\0Beta\0Gamma\0");
        ImGui.InputText("Text", ref _previewText, 128);

        ImGui.Spacing();
        ImGui.TextUnformatted("Header / Selectable");
        if (ImGui.CollapsingHeader("Collapsing Header"))
        {
            ImGui.Selectable("Selectable (normal)");
            ImGui.Selectable("Selectable (selected)", true);
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Tree");
        ImGui.SetNextItemOpen(_previewTreeOpen, ImGuiCond.Always);
        if (ImGui.TreeNode("Tree Node"))
        {
            _previewTreeOpen = true;
            ImGui.BulletText("Leaf one");
            ImGui.BulletText("Leaf two");
            ImGui.TreePop();
        }
        else
        {
            _previewTreeOpen = false;
        }

        ImGui.Spacing();
        if (ImGui.BeginTabBar("PreviewTabs"))
        {
            if (ImGui.BeginTabItem("Tab A"))
            {
                ImGui.TextUnformatted("Contents of Tab A.");
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Tab B"))
            {
                ImGui.TextUnformatted("Contents of Tab B.");
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Table");
        if (ImGui.BeginTable("PreviewTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("A");
            ImGui.TableSetupColumn("B");
            ImGui.TableSetupColumn("C");
            ImGui.TableHeadersRow();
            for (int row = 0; row < 3; row++)
            {
                ImGui.TableNextRow();
                for (int col = 0; col < 3; col++)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{row},{col}");
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Plot");
        ImGui.PlotLines("##plot", ref _previewPlot[0], _previewPlot.Length, 0, string.Empty, 0f, 1f, new Vector2(0f, 60f));
        ImGui.ProgressBar(0.65f, new Vector2(-1f, 0f), "65%");

        ImGui.Spacing();
        ImGui.TextUnformatted("Editor Color Tokens");
        foreach (StyleColor token in StyleTokenRegistry.Colors.OrderBy(static c => c.Label, System.StringComparer.Ordinal))
        {
            ImGuiEx.TextColored(token, token.Label);
        }
    }
}
