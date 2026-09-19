using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>The numeric <c>ImGuiStyle</c> fields a style file may set under <c>"vars"</c>. Bools and
/// the two <c>ImGuiDir</c> placement fields are deliberately left out — they aren't themeable colors
/// or sizes. Only rounding, padding, spacing, border sizes and alpha are exposed.</summary>
public static class StyleVarFields
{
    public sealed class Field
    {
        public required string Id { get; init; }
        public required bool IsVector { get; init; }
        public required Func<ImGuiStylePtr, StyleVarValue> Get { get; init; }
        public required Action<ImGuiStylePtr, StyleVarValue> Set { get; init; }
    }

    public static readonly IReadOnlyList<Field> All =
    [
        Scalar("Alpha", s => s.Alpha, (s, v) => s.Alpha = v),
        Scalar("DisabledAlpha", s => s.DisabledAlpha, (s, v) => s.DisabledAlpha = v),
        Vec("WindowPadding", s => s.WindowPadding, (s, v) => s.WindowPadding = v),
        Scalar("WindowRounding", s => s.WindowRounding, (s, v) => s.WindowRounding = v),
        Scalar("WindowBorderSize", s => s.WindowBorderSize, (s, v) => s.WindowBorderSize = v),
        Vec("WindowMinSize", s => s.WindowMinSize, (s, v) => s.WindowMinSize = v),
        Vec("WindowTitleAlign", s => s.WindowTitleAlign, (s, v) => s.WindowTitleAlign = v),
        Scalar("ChildRounding", s => s.ChildRounding, (s, v) => s.ChildRounding = v),
        Scalar("ChildBorderSize", s => s.ChildBorderSize, (s, v) => s.ChildBorderSize = v),
        Scalar("PopupRounding", s => s.PopupRounding, (s, v) => s.PopupRounding = v),
        Scalar("PopupBorderSize", s => s.PopupBorderSize, (s, v) => s.PopupBorderSize = v),
        Vec("FramePadding", s => s.FramePadding, (s, v) => s.FramePadding = v),
        Scalar("FrameRounding", s => s.FrameRounding, (s, v) => s.FrameRounding = v),
        Scalar("FrameBorderSize", s => s.FrameBorderSize, (s, v) => s.FrameBorderSize = v),
        Vec("ItemSpacing", s => s.ItemSpacing, (s, v) => s.ItemSpacing = v),
        Vec("ItemInnerSpacing", s => s.ItemInnerSpacing, (s, v) => s.ItemInnerSpacing = v),
        Vec("CellPadding", s => s.CellPadding, (s, v) => s.CellPadding = v),
        Vec("TouchExtraPadding", s => s.TouchExtraPadding, (s, v) => s.TouchExtraPadding = v),
        Scalar("IndentSpacing", s => s.IndentSpacing, (s, v) => s.IndentSpacing = v),
        Scalar("ColumnsMinSpacing", s => s.ColumnsMinSpacing, (s, v) => s.ColumnsMinSpacing = v),
        Scalar("ScrollbarSize", s => s.ScrollbarSize, (s, v) => s.ScrollbarSize = v),
        Scalar("ScrollbarRounding", s => s.ScrollbarRounding, (s, v) => s.ScrollbarRounding = v),
        Scalar("GrabMinSize", s => s.GrabMinSize, (s, v) => s.GrabMinSize = v),
        Scalar("GrabRounding", s => s.GrabRounding, (s, v) => s.GrabRounding = v),
        Scalar("TabRounding", s => s.TabRounding, (s, v) => s.TabRounding = v),
        Scalar("TabBorderSize", s => s.TabBorderSize, (s, v) => s.TabBorderSize = v),
        Vec("ButtonTextAlign", s => s.ButtonTextAlign, (s, v) => s.ButtonTextAlign = v),
        Vec("SelectableTextAlign", s => s.SelectableTextAlign, (s, v) => s.SelectableTextAlign = v),
        Vec("DisplayWindowPadding", s => s.DisplayWindowPadding, (s, v) => s.DisplayWindowPadding = v),
        Vec("DisplaySafeAreaPadding", s => s.DisplaySafeAreaPadding, (s, v) => s.DisplaySafeAreaPadding = v),
        Scalar("MouseCursorScale", s => s.MouseCursorScale, (s, v) => s.MouseCursorScale = v),
        Scalar("CurveTessellationTol", s => s.CurveTessellationTol, (s, v) => s.CurveTessellationTol = v),
        Scalar("CircleTessellationMaxError", s => s.CircleTessellationMaxError, (s, v) => s.CircleTessellationMaxError = v),
    ];

    private static readonly Dictionary<string, Field> ById = All.ToDictionary(f => f.Id, StringComparer.Ordinal);

    public static Field? Find(string id) => ById.GetValueOrDefault(id);

    private static Field Scalar(string id, Func<ImGuiStylePtr, float> get, Action<ImGuiStylePtr, float> set) => new()
    {
        Id = id,
        IsVector = false,
        Get = s => StyleVarValue.Scalar(get(s)),
        Set = (s, v) => set(s, v.AsFloat()),
    };

    private static Field Vec(string id, Func<ImGuiStylePtr, System.Numerics.Vector2> get, Action<ImGuiStylePtr, System.Numerics.Vector2> set) => new()
    {
        Id = id,
        IsVector = true,
        Get = s => StyleVarValue.Vector(get(s)),
        Set = (s, v) => set(s, v.AsVector2()),
    };
}
