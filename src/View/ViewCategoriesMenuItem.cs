using System.Collections.Generic;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Every registered <see cref="IViewCategory"/> as a checkmarked toggle under its own Group heading, in
/// the Priority order <see cref="ViewCategorySystem.All"/> already carries, plus Show All. Never names
/// a category, the same way SpawnMenu never names a spawn factory.
/// </summary>
[Subsystem(nameof(ViewMenu))]
public sealed class ViewCategoriesMenuItem : IMenuItem
{
    private readonly ViewCategorySystem _viewCategories;
    private readonly List<(IViewCategory Category, ShortcutAction Shortcut)> _categoryShortcuts = [];

    public float Priority => 0f;

    public int Section => 2;

    public ViewCategoriesMenuItem(ViewMenu menu)
    {
        _viewCategories = menu.Context.ViewCategories;
        foreach (IViewCategory category in _viewCategories.All)
        {
            string id = category.Id;
            ShortcutAction shortcut = menu.Context.Shortcuts.Register(
                $"view.show.{id}",
                "View",
                category.DisplayName,
                category.DefaultShortcut,
                () => _viewCategories.SetHidden(id, !_viewCategories.IsHidden(id)));
            _categoryShortcuts.Add((category, shortcut));
        }
    }

    public void Draw()
    {
        if (_categoryShortcuts.Count == 0)
        {
            return;
        }

        string? lastGroup = null;
        foreach ((IViewCategory category, ShortcutAction shortcut) in _categoryShortcuts)
        {
            if (category.Group != lastGroup)
            {
                lastGroup = category.Group;
                ImGui.TextDisabled(category.Group);
            }

            bool shown = !_viewCategories.IsHidden(category.Id);
            if (ImGui.MenuItem(category.DisplayName, shortcut.ShortcutLabel, ref shown))
            {
                _viewCategories.SetHidden(category.Id, !shown);
            }
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Show All"))
        {
            _viewCategories.ShowAll();
        }
    }
}
