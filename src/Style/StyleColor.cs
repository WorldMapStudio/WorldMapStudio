using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// An editor-level color a call site actually asks for — "text.error", "gizmo.axis.x" — as opposed to
/// the raw <c>ImGuiCol</c> entries. Declared as a <c>static readonly</c> field on a
/// <see cref="StyleTokensAttribute"/> class and self-registers with <see cref="StyleTokenRegistry"/> on
/// construction. <see cref="Value"/>/<see cref="U32"/> are cached per <see cref="EditorStyle.Generation"/>,
/// so a call site is as cheap as today's literal <c>new Vector4(...)</c>.
/// </summary>
public sealed class StyleColor
{
    public string Id { get; }
    public string Group { get; }
    public string Label { get; }

    /// <summary>The value used when no style in the active chain overrides this token. May itself be
    /// a reference, e.g. a "warning" variant defaulting to <c>"@text.warning"</c>.</summary>
    public StyleColorValue Default { get; }

    private int _generation = -1;
    private Vector4 _value;
    private uint _u32;

    public StyleColor(string id, string group, string label, string defaultValue)
    {
        Id = id;
        Group = group;
        Label = label;
        Default = StyleColorValue.FromCode(defaultValue);
        StyleTokenRegistry.RegisterColor(this);
    }

    public Vector4 Value
    {
        get
        {
            Refresh();
            return _value;
        }
    }

    public uint U32
    {
        get
        {
            Refresh();
            return _u32;
        }
    }

    private void Refresh()
    {
        if (_generation == EditorStyle.Generation)
        {
            return;
        }

        _value = EditorStyle.Active.Tokens.TryGetValue(Id, out Vector4 value) ? value : new Vector4(1f, 0f, 1f, 1f);
        _u32 = ImGui.GetColorU32(_value);
        _generation = EditorStyle.Generation;
    }

    public static implicit operator Vector4(StyleColor color) => color.Value;
}
