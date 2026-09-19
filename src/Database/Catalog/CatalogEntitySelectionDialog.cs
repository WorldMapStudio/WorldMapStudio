using System;
using System.Collections.Generic;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Body of <see cref="CatalogEntityPicker"/>'s popup: chrome (title, view toggle, Clear/Select/Cancel
/// footer) around whichever <see cref="ICatalogSearchView"/> session the catalog is currently showing —
/// the <see cref="CatalogSearchPurpose.Pick"/> counterpart of <c>CatalogBrowserWindow</c>'s own
/// chrome-around-a-session shape.
/// </summary>
public sealed class CatalogEntitySelectionDialog : IModalDialog<CatalogEntityPickerContext>
{
    // Selected label + separator + Clear/Select/Cancel row, reserved out of the session's own drawing
    // area so the footer never gets pushed off a small (List-sized) popup.
    private const float FooterHeight = 76.0f;

    private ICatalogSearchView? _view;
    private ICatalogSearchViewSession? _session;
    private string _selectedKey = "";
    private bool _confirmRequested;

    public ModalDialogState Draw(CatalogEntityPickerContext context)
    {
        EnsureSession(context);

        ImGui.Text($"Select {context.Catalog.CatalogName}");
        ImGui.Separator();

        DrawViewToggle(context);

        Vector2 available = ImGui.GetContentRegionAvail();
        var sessionAvailable = new Vector2(available.X, MathF.Max(120.0f, available.Y - FooterHeight));

        _confirmRequested = false;
        _session!.Draw(sessionAvailable);

        if (_confirmRequested)
        {
            context.Select(_selectedKey);
            return ModalDialogState.Confirmed;
        }

        string label = _selectedKey.Length == 0 ? "(none)" : _session.LabelFor(_selectedKey) ?? _selectedKey;
        ImGui.TextDisabled($"Selected: {label}");

        ImGui.Separator();
        if (ImGui.Button("Clear", new Vector2(120, 0)))
        {
            context.Select("");
            return ModalDialogState.Confirmed;
        }

        ImGui.SameLine();
        bool canSelect = _selectedKey.Length > 0;
        ImGui.BeginDisabled(!canSelect);
        if (ImGui.Button("Select", new Vector2(120, 0)))
        {
            context.Select(_selectedKey);
            return ModalDialogState.Confirmed;
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            return ModalDialogState.Cancelled;
        }

        return ModalDialogState.Running;
    }

    public void OnClose() => _session?.Dispose();

    // Hidden with one view, matching CatalogBrowserWindow's own toggle.
    private void DrawViewToggle(CatalogEntityPickerContext context)
    {
        IReadOnlyList<ICatalogSearchView> views = context.Context.CatalogSearchViews.For(context.Catalog);
        if (views.Count <= 1)
        {
            return;
        }

        for (int i = 0; i < views.Count; i++)
        {
            ICatalogSearchView candidate = views[i];
            if (i > 0)
            {
                ImGui.SameLine();
            }

            bool selected = candidate == _view;
            if (ImGui.RadioButton(candidate.ViewName, selected) && !selected)
            {
                context.Context.CatalogSearchViews.SetPreferred(context.Catalog, candidate);
                SwitchView(context, candidate);
            }
        }
    }

    private void EnsureSession(CatalogEntityPickerContext context)
    {
        if (_session is not null)
        {
            return;
        }

        _selectedKey = context.CurrentKey;
        SwitchView(context, context.Context.CatalogSearchViews.Preferred(context.Catalog));
    }

    // Grows or shrinks the popup to match the new view's own preferred size — the reason
    // CatalogEntityPicker's own ModalDialogHost is resizable rather than auto-fit.
    private void SwitchView(CatalogEntityPickerContext context, ICatalogSearchView view)
    {
        string filter = _session?.Filter ?? string.Empty;
        _session?.Dispose();

        var host = new CatalogSearchViewHost(
            context.Context, context.Catalog, CatalogSearchPurpose.Pick, filter,
            SelectedKey: () => _selectedKey,
            Highlight: key => _selectedKey = key,
            Activate: key =>
            {
                _selectedKey = key;
                _confirmRequested = true;
            });

        _view = view;
        _session = view.CreateSession(host);
        ImGui.SetWindowSize(view.PreferredPickerSize);
    }
}
