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
        int resolution,
        bool cellCentres = false)
    {
        Coord = coord;
        Channels = channels;
        Settings = settings;
        Values = values;
        Resolution = cellCentres ? resolution - 1 : resolution;
        CellCentres = cellCentres;
    }

    /// <summary>Whether vertex positions are the cell centres of the height grid rather than its corners.</summary>
    public bool CellCentres { get; }

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
    /// vertices sit <em>on</em> the grid, including the shared row at the chunk's far edge — or, for a
    /// cell-centre context, at the centre of each cell.
    /// </summary>
    public Vector3 WorldAt(int x, int y, bool vertices)
    {
        Vector3 origin = Grid.OriginOf(Coord);
        if (vertices && CellCentres)
        {
            float cell = Grid.ChunkSize / Resolution;
            return new Vector3(origin.X + ((x + 0.5f) * cell), 0.0f, origin.Z + ((y + 0.5f) * cell));
        }

        if (vertices)
        {
            float step = Grid.ChunkSize / (Resolution - 1);
            return new Vector3(origin.X + (x * step), 0.0f, origin.Z + (y * step));
        }

        float texel = Grid.ChunkSize / Resolution;
        return new Vector3(origin.X + ((x + 0.5f) * texel), 0.0f, origin.Z + ((y + 0.5f) * texel));
    }

    /// <summary>What a channel parameter is bound to. Resolving it means parsing the material's
    /// stored value, so a function sampling per texel reads it once before its loop rather than on
    /// every sample.</summary>
    public LandscapeChannelBinding ChannelBinding(LandscapeParameter parameter) =>
        Values.GetChannelBinding(parameter);

    /// <summary>Samples the channel a parameter is bound to, anywhere in the neighbourhood, honoring
    /// the binding's swizzle (see <see cref="LandscapeChannelBinding"/>).</summary>
    public float SampleChannel(LandscapeParameter parameter, Vector3 world) =>
        Channels.SampleScalar(Values.GetChannelBinding(parameter), world);

    /// <inheritdoc cref="SampleChannel(LandscapeParameter, Vector3)"/>
    public float SampleChannel(in LandscapeChannelBinding binding, Vector3 world) =>
        Channels.SampleScalar(binding, world);

    /// <summary>Samples the channel a parameter is bound to as a color, honoring the binding's
    /// swizzle. What a color-consuming function (e.g. a vertex color paint) reads instead of
    /// <see cref="SampleChannel"/>.</summary>
    public Color SampleChannelColor(LandscapeParameter parameter, Vector3 world) =>
        Channels.SampleColor(Values.GetChannelBinding(parameter), world);

    /// <inheritdoc cref="SampleChannelColor(LandscapeParameter, Vector3)"/>
    public Color SampleChannelColor(in LandscapeChannelBinding binding, Vector3 world) =>
        Channels.SampleColor(binding, world);

    /// <summary>Reads the channel a parameter is bound to at the single nearest texel — no bilinear
    /// blend — honoring the binding's swizzle. What a terrain-attribute function reading a discrete id
    /// must use instead of <see cref="SampleChannel"/>, which would filter the id into a different one
    /// across a texel or chunk edge.</summary>
    public float ReadChannel(LandscapeParameter parameter, Vector3 world) =>
        Channels.ReadScalarNearest(Values.GetChannelBinding(parameter), world);

    /// <inheritdoc cref="ReadChannel(LandscapeParameter, Vector3)"/>
    public float ReadChannel(in LandscapeChannelBinding binding, Vector3 world) =>
        Channels.ReadScalarNearest(binding, world);

    public float Float(LandscapeParameter parameter) => Values.GetFloat(parameter);

    public int Int(LandscapeParameter parameter) => Values.GetInt(parameter);

    public uint UInt(LandscapeParameter parameter) => Values.GetUInt(parameter);

    public bool Bool(LandscapeParameter parameter) => Values.GetBool(parameter);

    public Color Color(LandscapeParameter parameter) => Values.GetColor(parameter);
}
