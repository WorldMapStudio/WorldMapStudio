namespace WorldMapStudio;

/// <summary>
/// Read/write, by-cell view over one terrain attribute's buffer for one chunk while
/// <see cref="ILandscapeAttributeFunction.Evaluate"/> runs — the counterpart of
/// <see cref="LandscapeChannelWriter"/> on the attribute side. Values are held as <see cref="uint"/>
/// per component, interleaved (<c>[cell0.c0, cell0.c1, …]</c>); the packing to
/// <see cref="TerrainAttributeGrid"/>'s width-sized bytes happens once, afterwards.
///
/// The writer carries the material write's <see cref="LandscapeSwizzle"/>, so a function never asks
/// which components it is addressing: <see cref="Set"/>/<see cref="Get"/> act on the swizzled
/// component(s), and a <see cref="LandscapeSwizzle.Native"/> write broadcasts to every component —
/// the same rule <see cref="LandscapeChannelWriter.Set"/> follows, so a scalar function pointed at a
/// wide attribute still produces a sensible result. <see cref="SetAt"/>/<see cref="GetAt"/> reach one
/// explicit component regardless of the swizzle.
///
/// A value that does not fit the attribute's declared element width is masked to it and raises
/// <see cref="Overflowed"/>, which <c>Evaluate</c>'s caller turns into one reported problem rather
/// than a silent truncation.
/// </summary>
public readonly struct TerrainAttributeWriter
{
    private readonly uint[] _cells;
    private readonly int _elementWidth;
    private readonly LandscapeSwizzle _swizzle;

    // A one-element flag rather than a struct field, so a write through this readonly struct can still
    // record that it overran the declared width.
    private readonly bool[] _overflow;

    public TerrainAttributeWriter(
        uint[] cells, int resolution, int components, int elementWidth, LandscapeSwizzle swizzle, bool[] overflow)
    {
        _cells = cells;
        Resolution = resolution;
        Components = components;
        _elementWidth = elementWidth;
        _swizzle = swizzle;
        _overflow = overflow;
    }

    /// <summary>Cells along a chunk edge.</summary>
    public int Resolution { get; }

    /// <summary>Values stored per cell — see <see cref="TerrainAttribute.Components"/>.</summary>
    public int Components { get; }

    /// <summary>Whether any write so far did not fit <see cref="TerrainAttribute.ElementWidth"/>.</summary>
    public bool Overflowed => _overflow[0];

    private int IndexOf(int x, int y, int component) => (((y * Resolution) + x) * Components) + component;

    /// <summary>The first component this write's swizzle addresses.</summary>
    public uint Get(int x, int y) => _cells[IndexOf(x, y, FirstComponent())];

    /// <summary>Component <paramref name="component"/> of cell (x, y), ignoring the swizzle.</summary>
    public uint GetAt(int x, int y, int component) => _cells[IndexOf(x, y, component)];

    /// <summary>Writes <paramref name="value"/> into every component this write's swizzle addresses.</summary>
    public void Set(int x, int y, uint value)
    {
        for (int c = 0; c < Components; c++)
        {
            if (Addresses(c))
            {
                Store(x, y, c, value);
            }
        }
    }

    /// <summary>Writes <paramref name="value"/> into one explicit component, ignoring the swizzle.</summary>
    public void SetAt(int x, int y, int component, uint value) => Store(x, y, component, value);

    private void Store(int x, int y, int component, uint value)
    {
        if (_elementWidth < 32)
        {
            uint max = (1u << _elementWidth) - 1u;
            if (value > max)
            {
                _overflow[0] = true;
                value &= max;
            }
        }

        _cells[IndexOf(x, y, component)] = value;
    }

    private int FirstComponent()
    {
        for (int c = 0; c < Components; c++)
        {
            if (Addresses(c))
            {
                return c;
            }
        }

        return 0;
    }

    private bool Addresses(int component) => _swizzle switch
    {
        LandscapeSwizzle.Native or LandscapeSwizzle.Rgba => true,
        LandscapeSwizzle.Rgb => component < 3,
        LandscapeSwizzle.R => component == 0,
        LandscapeSwizzle.G => component == 1,
        LandscapeSwizzle.B => component == 2,
        LandscapeSwizzle.A => component == 3,
        _ => true,
    };
}
