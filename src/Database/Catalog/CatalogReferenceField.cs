using System;
using ImGuiNET;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>One reference field's worth of state for <see cref="CatalogReferenceField"/>: the label
/// (used for widget ids), which catalog it points into, the current key (null = empty — 0 for an
/// int-keyed field, null for an <c>int?</c>-keyed one), and how to write a new key. <see cref="Assign"/>
/// is a plain callback rather than a typed setter so every current variant — a tracked sheet field, a
/// scripted mutator's command, a nullable field recorded on the spot — collapses into this one widget;
/// the caller decides tracker vs. command, this widget only ever calls it with a finished value.</summary>
internal readonly record struct CatalogReference(
    string Label,
    string TargetCatalog,
    string? Key,
    Action<string?> Assign);

/// <summary>
/// The one widget every catalog reference field draws, regardless of how the field itself is stored or
/// written: display text beside the (separately drawn) numeric input, an Open button once the key
/// resolves, a New button that creates a row and points the field at it, and a Load button that opens
/// <see cref="CatalogEntityPicker"/>'s search popup. Numeric editing stays the caller's job — an
/// <c>int</c> field's drag/type/undo behaviour differs from a <c>uint</c>'s or a nullable one's — this
/// widget only draws what follows it.
///
/// One instance per field-drawing site, held for the picker's own per-frame <see cref="CatalogEntityPicker.Draw"/>
/// contract — the same lifetime a call site's own <c>CatalogEntityPicker</c> field used to have.
/// </summary>
internal sealed class CatalogReferenceField
{
    private static readonly Vector4 MissingColor = new(1.0f, 0.45f, 0.4f, 1.0f);

    private readonly CatalogEntityPicker _picker = new();

    /// <paramref name="navigate"/> is null for a site with no browser to hand navigation off to (an
    /// inspector window, rather than <see cref="CatalogBrowserWindow"/>'s own <c>DrawFields</c> call) —
    /// Open and New then fall back to opening and focusing the Catalog Browser itself.
    public void Draw(EditorContext context, CatalogReference reference, Action<string, string>? navigate)
    {
        ICatalogBrowser? target = context.ReferenceLabels.FindCatalog(reference.TargetCatalog);

        ImGui.SameLine();
        bool openResolved = DrawText(context, reference);
        if (openResolved)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Open##{reference.Label}"))
            {
                NavigateTo(context, reference.TargetCatalog, reference.Key!, navigate);
            }
        }

        // A browser that can't suggest a key (SuggestKeyAsync's default, or a read-only catalog) is
        // still offered New — clicking just no-ops, the same trade-off DrawLink's own CreateLinked
        // already made, rather than a per-frame async probe just to decide whether to show the button.
        if (target is not null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"New##{reference.Label}"))
            {
                TryCreateAndAssign(context, target, reference, navigate);
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"Load##{reference.Label}"))
        {
            if (target is not null)
            {
                _picker.Browse(context, target, reference.Key ?? string.Empty,
                    selected => reference.Assign(selected.Length == 0 ? null : selected));
            }
        }

        _picker.Draw();
    }

    // Returns whether the key resolved (so the caller knows to draw Open).
    private static bool DrawText(EditorContext context, CatalogReference reference)
    {
        if (reference.Key is not { Length: > 0 } key)
        {
            ImGui.TextDisabled("(none)");
            return false;
        }

        CatalogReferenceLabels.LabelState state = context.ReferenceLabels.TryGet(reference.TargetCatalog, key, out string text);
        switch (state)
        {
            case CatalogReferenceLabels.LabelState.Resolved:
                // Distinct from the empty-key "(none)" above: this row exists (the key resolved) but
                // has nothing better than its id — a real reference, just to an unnamed row.
                ImGui.TextDisabled(text.Length == 0 ? "(unnamed)" : text);
                return true;
            case CatalogReferenceLabels.LabelState.Missing:
                ImGui.TextColored(MissingColor, "(missing)");
                return false;
            default:
                ImGui.TextDisabled("...");
                return false;
        }
    }

    // Creates a fresh row in the target catalog, then assigns and navigates — the cross-catalog FK
    // wiring that building a linked row otherwise does by hand. Assign is a second, separate undo step
    // on top of Create's own.
    private static void TryCreateAndAssign(EditorContext context, ICatalogBrowser target, CatalogReference reference,
        Action<string, string>? navigate)
    {
        string? key = BlockingWork.Run(() => target.SuggestKeyAsync());
        if (key is null)
        {
            return;
        }

        try
        {
            target.Create(context, key);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        reference.Assign(key);
        NavigateTo(context, reference.TargetCatalog, key, navigate);
    }

    private static void NavigateTo(EditorContext context, string catalogName, string key, Action<string, string>? navigate)
    {
        if (navigate is not null)
        {
            navigate(catalogName, key);
        }
        else
        {
            context.WindowManager.OpenCatalogEntry(catalogName, key);
        }
    }
}
