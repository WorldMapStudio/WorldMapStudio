namespace WorldMapStudio;

/// <summary>A semantic float token (gizmo line thickness, and the like) — same mechanics as
/// <see cref="StyleColor"/>, minus the $/@ reference grammar; a style file sets it as a plain number
/// under <c>"sizes"</c>.</summary>
public sealed class StyleSize
{
    public string Id { get; }
    public string Group { get; }
    public string Label { get; }
    public float DefaultValue { get; }

    private int _generation = -1;
    private float _value;

    public StyleSize(string id, string group, string label, float defaultValue)
    {
        Id = id;
        Group = group;
        Label = label;
        DefaultValue = defaultValue;
        StyleTokenRegistry.RegisterSize(this);
    }

    public float Value
    {
        get
        {
            if (_generation != EditorStyle.Generation)
            {
                _value = EditorStyle.Active.Sizes.TryGetValue(Id, out float value) ? value : DefaultValue;
                _generation = EditorStyle.Generation;
            }

            return _value;
        }
    }

    public static implicit operator float(StyleSize size) => size.Value;
}
