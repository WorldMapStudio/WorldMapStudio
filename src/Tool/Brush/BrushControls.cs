using ImGuiNET;

namespace WorldMapStudio;

/// <summary>The shared toolbar controls for a <see cref="Brush"/>. A tool draws these, then its own extras.</summary>
public static class BrushControls
{
    private const float FieldWidth = 110.0f;

    /// <summary>Radius, strength, hardness and invert inline, with the rarer settings behind a "More" popup.
    /// The strength and invert labels are the tool's own words for them (opacity, erase).</summary>
    public static void Draw(Brush brush, string strengthLabel = "Strength", string invertLabel = "Invert")
    {
        float radius = brush.Radius;
        ImGui.SetNextItemWidth(FieldWidth);
        if (ImGui.DragFloat("Radius", ref radius, 0.1f, Brush.MinRadius, Brush.MaxRadius))
        {
            brush.Radius = radius;
        }

        ImGui.SameLine();

        float strength = brush.Strength;
        ImGui.SetNextItemWidth(FieldWidth);
        if (ImGui.DragFloat(strengthLabel, ref strength, 0.01f, Brush.MinStrength, Brush.MaxStrength))
        {
            brush.Strength = strength;
        }

        ImGui.SameLine();

        float hardness = brush.Hardness;
        ImGui.SetNextItemWidth(FieldWidth);
        if (ImGui.SliderFloat("Hardness", ref hardness, 0.0f, 1.0f))
        {
            brush.Hardness = hardness;
        }

        ImGui.SameLine();

        bool invert = brush.Invert;
        if (ImGui.Checkbox(invertLabel, ref invert))
        {
            brush.Invert = invert;
        }

        ImGui.SameLine();
        if (ImGui.Button("More"))
        {
            ImGui.OpenPopup("brush_more");
        }

        if (ImGui.BeginPopup("brush_more"))
        {
            DrawMore(brush);
            ImGui.EndPopup();
        }
    }

    private static void DrawMore(Brush brush)
    {
        float spacing = brush.Spacing;
        if (ImGui.SliderFloat("Spacing", ref spacing, Brush.MinSpacing, Brush.MaxSpacing))
        {
            brush.Spacing = spacing;
        }

        float rate = brush.AirbrushRate;
        if (ImGui.SliderFloat("Airbrush rate", ref rate, Brush.MinAirbrushRate, Brush.MaxAirbrushRate))
        {
            brush.AirbrushRate = rate;
        }

        bool spray = brush.Spray;
        if (ImGui.Checkbox("Spray", ref spray))
        {
            brush.Spray = spray;
        }

        if (!spray)
        {
            ImGui.BeginDisabled();
        }

        int count = brush.SprayCount;
        if (ImGui.SliderInt("Spray count", ref count, 1, Brush.MaxSprayCount))
        {
            brush.SprayCount = count;
        }

        float scatter = brush.SprayScatter;
        if (ImGui.SliderFloat("Spray scatter", ref scatter, 0.0f, 1.0f))
        {
            brush.SprayScatter = scatter;
        }

        if (!spray)
        {
            ImGui.EndDisabled();
        }
    }
}
