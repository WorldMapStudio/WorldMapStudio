using Godot;

namespace WorldMapStudio;

public enum ModelBlendMode
{
    Opaque,
    AlphaCutout,
    AlphaBlend,
    Additive,
    Modulate,
    Modulate2x,
}

public sealed record ModelMaterial
{
    public string TexturePath { get; init; } = "";

    public Color AlbedoColor { get; init; } = Colors.White;

    public ModelBlendMode BlendMode { get; init; } = ModelBlendMode.Opaque;

    public bool TwoSided { get; init; }

    public bool Unlit { get; init; }

    public bool DepthWrite { get; init; } = true;

    public bool UseVertexColor { get; init; }

    public bool RepeatTexture { get; init; } = true;

    public float AlphaCutoff { get; init; } = 0.5f;

    public int SortPriority { get; init; }
}
