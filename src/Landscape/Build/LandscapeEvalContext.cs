using Godot;

namespace WorldMapStudio;

/// <summary>
/// Everything a function call depends on, passed in rather than held. Functions are stateless because
/// one instance serves every chunk of every parallel block build.
///
/// Positions are world positions throughout. That is not a convenience — it is what makes a shared
/// chunk edge agree: both chunks evaluate the same world point through the same global channel
/// sampling and get the same answer.
/// </summary>
public readonly struct LandscapeEvalContext
{
    public LandscapeEvalContext(
        ChunkCoord coord,
        LandscapeChannelPool channels,
        LandscapeSettings settings,
        LandscapeParameterValues values,
        int resolution)
    {
        Coord = coord;
        Channels = channels;
        Settings = settings;
        Values = values;
        Resolution = resolution;
    }

    public ChunkCoord Coord { get; }

    public LandscapeChannelPool Channels { get; }

    public LandscapeSettings Settings { get; }

    /// <summary>The material's values for the function's declared parameters.</summary>
    public LandscapeParameterValues Values { get; }

    /// <summary>Edge length of the output being written — alpha texels, or height vertices.</summary>
    public int Resolution { get; }

    public LandscapeGrid Grid => Channels.Grid;

    /// <summary>
    /// World position of output sample (x, y). Alpha texels are sampled at their centres; height
    /// vertices sit <em>on</em> the grid, including the shared row at the chunk's far edge.
    /// </summary>
    public Vector3 WorldAt(int x, int y, bool vertices)
    {
        Vector3 origin = Grid.OriginOf(Coord);
        if (vertices)
        {
            float step = Grid.ChunkSize / (Resolution - 1);
            return new Vector3(origin.X + (x * step), 0.0f, origin.Z + (y * step));
        }

        float texel = Grid.ChunkSize / Resolution;
        return new Vector3(origin.X + ((x + 0.5f) * texel), 0.0f, origin.Z + ((y + 0.5f) * texel));
    }

    /// <summary>Samples the channel a parameter is bound to, anywhere in the neighbourhood, honoring
    /// the binding's swizzle (see <see cref="LandscapeChannelBinding"/>).</summary>
    public float SampleChannel(LandscapeParameter parameter, Vector3 world) =>
        Channels.SampleScalar(Values.GetChannelBinding(parameter), world);

    /// <summary>Samples the channel a parameter is bound to as a color, honoring the binding's
    /// swizzle. What a color-consuming function (e.g. a vertex color paint) reads instead of
    /// <see cref="SampleChannel"/>.</summary>
    public Color SampleChannelColor(LandscapeParameter parameter, Vector3 world) =>
        Channels.SampleColor(Values.GetChannelBinding(parameter), world);

    public float Float(LandscapeParameter parameter) => Values.GetFloat(parameter);

    public int Int(LandscapeParameter parameter) => Values.GetInt(parameter);

    public bool Bool(LandscapeParameter parameter) => Values.GetBool(parameter);

    public Color Color(LandscapeParameter parameter) => Values.GetColor(parameter);
}
