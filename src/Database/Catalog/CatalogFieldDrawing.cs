using System;
using System.Linq;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Shared <see cref="ICatalogBrowser.DrawFields"/> field widgets — the same handful of "drag/input this
/// typed value, track it for undo" shapes a catalog's own <c>DrawFields</c> would otherwise grow one
/// private copy of per catalog. Pulled out once, generic over the entity type.
/// </summary>
internal static class CatalogFieldDrawing
{
    // Drawn as a plain id field plus an Open link rather than a searchable Pick widget: the common
    // case this is used for is a lazily-loaded catalog (never loaded whole), so there is no in-memory
    // list to pick from — CatalogBrowserWindow's own search box is the tool for finding an id you don't
    // already know. A "Pick" widget elsewhere for a small, eagerly loaded catalog can still be cheap.
    public static void DrawLink<TEntity>(
        EditorContext context, FieldEditTracker tracker, TEntity entity, string label, int current,
        Action<int> set, string targetCatalogName, Action<string, string> navigate)
        where TEntity : CatalogEntity
    {
        int value = current;
        ImGui.SetNextItemWidth(160.0f);
        if (ImGui.InputInt(label, ref value))
        {
            set(value);
        }

        tracker.Track(context.EditSessions, entity, label, current, set);

        if (current != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Open##{label}"))
            {
                navigate(targetCatalogName, current.ToString());
            }
        }
        else if (FindBrowser(context, targetCatalogName) is { } target)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"New##{label}"))
            {
                CreateLinked(context, entity, label, set, targetCatalogName, target, navigate);
            }
        }
    }

    private static ICatalogBrowser? FindBrowser(EditorContext context, string catalogName) =>
        context.Database.Storages.SelectMany(storage => storage.CatalogBrowsers)
            .FirstOrDefault(browser => browser.CatalogName == catalogName);

    // Creates a fresh row in the linked catalog, points this field at it as one further undo step on
    // top of the create, and jumps there — the cross-catalog FK wiring that building a linked row
    // otherwise does by hand.
    private static void CreateLinked<TEntity>(
        EditorContext context, TEntity entity, string label, Action<int> set,
        string targetCatalogName, ICatalogBrowser target, Action<string, string> navigate)
        where TEntity : CatalogEntity
    {
        string? key = BlockingWork.Run(() => target.SuggestKeyAsync());
        if (key is null || !int.TryParse(key, out int newId))
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

        var link = new SetFieldCommand<int>(entity, label, set, 0, newId);
        link.Apply();
        context.EditSessions.Record(link);
        navigate(targetCatalogName, key);
    }

    public static void DrawText<TEntity>(
        EditorContext context, FieldEditTracker tracker, TEntity entity, string label, string current,
        uint maxLength, Action<string> set)
        where TEntity : CatalogEntity
    {
        string value = current;
        if (ImGui.InputText(label, ref value, maxLength))
        {
            set(value);
        }

        tracker.Track(context.EditSessions, entity, label, current, set);
    }

    // For a long free-text column (a description-variable block, a spell description) where a
    // single-line field would scroll away out of sight.
    public static void DrawTextMultiline<TEntity>(
        EditorContext context, FieldEditTracker tracker, TEntity entity, string label, string current,
        uint maxLength, Action<string> set, float height = 90.0f)
        where TEntity : CatalogEntity
    {
        string value = current;
        if (ImGui.InputTextMultiline(label, ref value, maxLength, new Vector2(0.0f, height)))
        {
            set(value);
        }

        tracker.Track(context.EditSessions, entity, label, current, set);
    }

    // min/max null draws the plain unclamped ImGui.DragFloat overload; both set draws the clamped one.
    public static void DrawFloat<TEntity>(
        EditorContext context, FieldEditTracker tracker, TEntity entity, string label, float current,
        Action<float> set, float speed = 0.01f, float? min = null, float? max = null, float? width = null)
        where TEntity : CatalogEntity
    {
        float value = current;
        if (width is { } itemWidth)
        {
            ImGui.SetNextItemWidth(itemWidth);
        }

        bool changed = min is { } lo && max is { } hi
            ? ImGui.DragFloat(label, ref value, speed, lo, hi)
            : ImGui.DragFloat(label, ref value, speed);

        if (changed)
        {
            set(value);
        }

        tracker.Track(context.EditSessions, entity, label, current, set);
    }

    public static void DrawInt<TEntity>(
        EditorContext context, FieldEditTracker tracker, TEntity entity, string label, int current, Action<int> set)
        where TEntity : CatalogEntity
    {
        int value = current;
        if (ImGui.InputInt(label, ref value))
        {
            set(value);
        }

        tracker.Track(context.EditSessions, entity, label, current, set);
    }

    public static void DrawByte<TEntity>(
        EditorContext context, FieldEditTracker tracker, TEntity entity, string label, byte current, Action<byte> set)
        where TEntity : CatalogEntity
    {
        int value = current;
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.DragInt(label, ref value, 1.0f, byte.MinValue, byte.MaxValue))
        {
            set((byte)value);
        }

        tracker.Track(context.EditSessions, entity, label, current, set);
    }

    public static void DrawSByte<TEntity>(
        EditorContext context, FieldEditTracker tracker, TEntity entity, string label, sbyte current, Action<sbyte> set)
        where TEntity : CatalogEntity
    {
        int value = current;
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.DragInt(label, ref value, 1.0f, sbyte.MinValue, sbyte.MaxValue))
        {
            set((sbyte)value);
        }

        tracker.Track(context.EditSessions, entity, label, current, set);
    }

    public static void DrawUShort<TEntity>(
        EditorContext context, FieldEditTracker tracker, TEntity entity, string label, ushort current, Action<ushort> set)
        where TEntity : CatalogEntity
    {
        int value = current;
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.DragInt(label, ref value, 1.0f, ushort.MinValue, ushort.MaxValue))
        {
            set((ushort)value);
        }

        tracker.Track(context.EditSessions, entity, label, current, set);
    }

    // int.MaxValue rather than uint.MaxValue: DragInt's own range is a signed int. Values in real data
    // stay well under this.
    public static void DrawUInt<TEntity>(
        EditorContext context, FieldEditTracker tracker, TEntity entity, string label, uint current, Action<uint> set)
        where TEntity : CatalogEntity
    {
        int value = (int)Math.Min(current, int.MaxValue);
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.DragInt(label, ref value, 1.0f, 0, int.MaxValue))
        {
            set((uint)Math.Max(0, value));
        }

        tracker.Track(context.EditSessions, entity, label, current, set);
    }
}
