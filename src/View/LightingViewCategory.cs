using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Turning lighting off has to mean no lighting, not just no light markers: this category hides
/// every entity carrying an <see cref="IEnvironmentSource"/> component through the ordinary
/// mechanism every other category uses, and <see cref="EnvironmentRenderer"/> separately pulls
/// <see cref="CategoryId"/>'s hidden state to drop the viewport to its flat grey default — a render
/// feature may be gated this way only because lighting's whole point is a render effect. Core, and
/// defined over the component interface rather than any concrete light type, so a plugin's own
/// environment source falls under it without this needing to name it. Replaces
/// <c>ViewSettings.UseEnvironmentLighting</c> rather than sitting beside it, inheriting its Alt+M
/// shortcut.
/// </summary>
[Subsystem(nameof(ViewCategorySystem))]
public sealed class LightingViewCategory : IViewCategory
{
    public const string CategoryId = "view.lighting";

    public string Id => CategoryId;

    public string DisplayName => "Lighting";

    public string Group => "Lighting";

    public KeyboardShortcut DefaultShortcut => new(ImGuiKey.M, ShortcutModifiers.Alt);

    public LightingViewCategory(ViewCategorySystem system)
    {
    }

    public bool Includes(SceneEntity entity) => entity.ComponentsOf<IEnvironmentSource>().Any();
}
