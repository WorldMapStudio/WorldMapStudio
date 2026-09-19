using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>Turns a tag's packed <c>0xRRGGBB</c> colour into what ImGui draws with.</summary>
public static class TagColors
{
    public static NVector4 ToVector4(int rgb, float alpha = 1.0f) => new(
        ((rgb >> 16) & 0xFF) / 255.0f,
        ((rgb >> 8) & 0xFF) / 255.0f,
        (rgb & 0xFF) / 255.0f,
        alpha);

    public static int FromVector4(NVector4 color) =>
        ((int)(color.X * 255.0f + 0.5f) << 16) | ((int)(color.Y * 255.0f + 0.5f) << 8) | (int)(color.Z * 255.0f + 0.5f);

    /// <summary>Black or white, whichever reads better on <paramref name="rgb"/>.</summary>
    public static NVector4 TextOn(int rgb)
    {
        float luminance = (0.299f * ((rgb >> 16) & 0xFF) + 0.587f * ((rgb >> 8) & 0xFF) + 0.114f * (rgb & 0xFF)) / 255.0f;
        return luminance > 0.55f ? new NVector4(0, 0, 0, 1) : new NVector4(1, 1, 1, 1);
    }
}
