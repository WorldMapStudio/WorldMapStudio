using System;

namespace WorldMapStudio;

public static partial class ImGuiEx
{
    public static void Center(float buttonWidth, float buttonHeight, float spacing, Action<CenterContext> content)
    {
        using (var center = new CenterContext(buttonWidth, buttonHeight, spacing))
        {
            content(center);
        }
    }
}
