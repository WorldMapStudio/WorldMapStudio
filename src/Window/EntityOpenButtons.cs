using ImGuiNET;

namespace WorldMapStudio;

/// <summary>Draws one button per <see cref="IEntityOpener"/> that can show an entity, on one row.</summary>
public static class EntityOpenButtons
{
    public static void Draw(EditorContext context, IEntity entity)
    {
        bool any = false;
        foreach (IEntityOpener opener in context.WindowManager.OpenersFor(entity))
        {
            if (any)
            {
                ImGui.SameLine();
            }

            any = true;
            if (ImGui.Button(opener.OpenLabel))
            {
                opener.Open(entity);
            }
        }

        if (any)
        {
            ImGui.Spacing();
        }
    }
}
