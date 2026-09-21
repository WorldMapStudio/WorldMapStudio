using System;
using Godot;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Draws a <see cref="SubViewport"/> as an ImGui image sized to <c>size</c>. The resize is deferred until
/// after the ImGui draw, so the image never samples a reallocated, not-yet-rendered texture.
/// </summary>
public static class SubViewportImage
{
    public static void Draw(SubViewport viewport, NVector2 size)
    {
        Vector2I target = new(Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y));
        if (viewport.Size != target)
        {
            GodotImGui.AfterRender(() =>
            {
                if (GodotObject.IsInstanceValid(viewport))
                {
                    viewport.Size = target;
                }
            });
        }

        ImGui.Image((IntPtr)viewport.GetTexture().GetRid().Id, size);
    }
}
