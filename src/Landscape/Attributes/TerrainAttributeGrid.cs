using System;

namespace WorldMapStudio;

/// <summary>
/// One terrain attribute's evaluated values for one chunk: a <see cref="CellsPerEdge"/>-squared grid
/// of cells, <see cref="Components"/> unsigned integers each, held little-endian and interleaved in
/// <see cref="Bytes"/> at <see cref="ElementWidth"/> bits per component (<c>[cell0.c0, cell0.c1, …,
/// cell1.c0, …]</c>).
///
/// Every accessor returns <see cref="uint"/> whatever the width, so an exporter and every function
/// read it the same way and never switch on <see cref="ElementWidth"/>. A <see cref="TerrainAttributeKind.Float"/>
/// attribute's consumer reinterprets those bits itself.
/// </summary>
public readonly struct TerrainAttributeGrid
{
    private readonly byte[] _bytes;

    public TerrainAttributeGrid(int cellsPerEdge, int components, int elementWidth, byte[] bytes)
    {
        CellsPerEdge = cellsPerEdge;
        Components = components;
        ElementWidth = elementWidth;
        _bytes = bytes;
    }

    public int CellsPerEdge { get; }

    public int Components { get; }

    /// <summary>Bits per component — 8, 16 or 32.</summary>
    public int ElementWidth { get; }

    /// <summary>The raw interleaved buffer, <see cref="CellsPerEdge"/>² × <see cref="Components"/> ×
    /// <c>ElementWidth / 8</c> bytes.</summary>
    public byte[] Bytes => _bytes;

    /// <summary>The first component of cell (x, y).</summary>
    public uint At(int x, int y) => At(x, y, 0);

    /// <summary>Component <paramref name="component"/> of cell (x, y).</summary>
    public uint At(int x, int y, int component)
    {
        int stride = ElementWidth / 8;
        int offset = (((y * CellsPerEdge) + x) * Components + component) * stride;
        return ElementWidth switch
        {
            8 => _bytes[offset],
            16 => (uint)(_bytes[offset] | (_bytes[offset + 1] << 8)),
            _ => (uint)(_bytes[offset]
                | (_bytes[offset + 1] << 8)
                | (_bytes[offset + 2] << 16)
                | (_bytes[offset + 3] << 24)),
        };
    }

    /// <summary>The lone value of the 1-cell, 1-component attribute most declarations are.</summary>
    public uint Single => At(0, 0, 0);

    /// <summary>An all-<paramref name="defaultValue"/> grid — what an attribute no surviving claim
    /// wrote reads as.</summary>
    public static TerrainAttributeGrid Filled(int cellsPerEdge, int components, int elementWidth, uint defaultValue)
    {
        int stride = elementWidth / 8;
        var bytes = new byte[cellsPerEdge * cellsPerEdge * components * stride];
        if (defaultValue != 0)
        {
            for (int i = 0; i < cellsPerEdge * cellsPerEdge * components; i++)
            {
                WriteInto(bytes, i * stride, elementWidth, defaultValue);
            }
        }

        return new TerrainAttributeGrid(cellsPerEdge, components, elementWidth, bytes);
    }

    /// <summary>Packs an interleaved <c>uint</c>-per-component buffer — what
    /// <see cref="TerrainAttributeWriter"/> fills — into a width-sized grid.</summary>
    public static TerrainAttributeGrid FromComponents(int cellsPerEdge, int components, int elementWidth, uint[] values)
    {
        int stride = elementWidth / 8;
        var bytes = new byte[values.Length * stride];
        for (int i = 0; i < values.Length; i++)
        {
            WriteInto(bytes, i * stride, elementWidth, values[i]);
        }

        return new TerrainAttributeGrid(cellsPerEdge, components, elementWidth, bytes);
    }

    internal static void WriteInto(byte[] bytes, int offset, int elementWidth, uint value)
    {
        switch (elementWidth)
        {
            case 8:
                bytes[offset] = (byte)value;
                break;
            case 16:
                bytes[offset] = (byte)value;
                bytes[offset + 1] = (byte)(value >> 8);
                break;
            default:
                bytes[offset] = (byte)value;
                bytes[offset + 1] = (byte)(value >> 8);
                bytes[offset + 2] = (byte)(value >> 16);
                bytes[offset + 3] = (byte)(value >> 24);
                break;
        }
    }
}
