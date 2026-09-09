using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Pins the pure indexing and packing maths a terrain render batch is built on: which batch a chunk
/// falls in, where a chunk sits inside its batch, the streaming key, and how a chunk's slots and
/// alpha land in the batch-wide slot-map and alpha atlas. Mesh and material construction calls into
/// Godot and cannot run headless, so only this side is covered.
/// </summary>
public static class LandscapeBatchTests
{
    [EditorTest(Category = "LandscapeBatch", Thread = TestThread.Background)]
    public static void OfGroupsNegativeChunkCoordsWithoutStraddlingZero()
    {
        const int b = 4;
        LandscapeBatchCoord zero = LandscapeBatchCoord.Of(new ChunkCoord(0, 0), b);
        LandscapeBatchCoord minusOne = LandscapeBatchCoord.Of(new ChunkCoord(-1, -1), b);

        Assert.AreNotEqual(zero, minusOne);
        Assert.AreEqual(new LandscapeBatchCoord(-1, -1), minusOne);

        // -4..-1 land in one batch on each axis; -5 is already the next one down.
        for (int c = -4; c <= -1; c++)
        {
            Assert.AreEqual(minusOne, LandscapeBatchCoord.Of(new ChunkCoord(c, c), b));
        }

        Assert.AreEqual(new LandscapeBatchCoord(-2, -2), LandscapeBatchCoord.Of(new ChunkCoord(-5, -5), b));
    }

    [EditorTest(Category = "LandscapeBatch", Thread = TestThread.Background)]
    public static void IndexOfRoundTripsAgainstOriginForEveryCell()
    {
        const int b = 4;
        foreach (LandscapeBatchCoord batch in new[] { new LandscapeBatchCoord(0, 0), new LandscapeBatchCoord(-2, 1) })
        {
            ChunkCoord origin = batch.Origin(b);
            for (int y = 0; y < b; y++)
            {
                for (int x = 0; x < b; x++)
                {
                    var coord = new ChunkCoord(origin.X + x, origin.Y + y);
                    Assert.AreEqual(batch, LandscapeBatchCoord.Of(coord, b));
                    Assert.AreEqual((y * b) + x, batch.IndexOf(coord, b));
                }
            }
        }

        Assert.AreEqual(-1, new LandscapeBatchCoord(0, 0).IndexOf(new ChunkCoord(b, 0), b));
    }

    [EditorTest(Category = "LandscapeBatch", Thread = TestThread.Background)]
    public static void KeyForDistinguishesBatchesThatDifferOnlyByMap()
    {
        var batch = new LandscapeBatchCoord(3, -2);

        Assert.AreNotEqual(batch.KeyFor(new MapId(1)), batch.KeyFor(new MapId(2)));
        Assert.AreEqual(batch.KeyFor(new MapId(1)), batch.KeyFor(new MapId(1)));
    }

    [EditorTest(Category = "LandscapeBatch", Thread = TestThread.Background)]
    public static void SlotMapResolvesEachChunkSlotToItsOwnMaterialsLayer()
    {
        const int b = 2;
        var batch = new LandscapeBatchCoord(0, 0);
        LandscapeMaterial matA = Material(1, "a");
        LandscapeMaterial matB = Material(2, "b");
        LandscapeMaterial matC = Material(3, "c");

        var chunks = new List<(ChunkCoord, LandscapeChunkOutput)>
        {
            (new ChunkCoord(0, 0), Chunk(new ChunkCoord(0, 0), 2, BaseSlot(matA), AlphaSlot(matB, [0, 0, 0, 0]))),
            (new ChunkCoord(1, 0), Chunk(new ChunkCoord(1, 0), 2, BaseSlot(matA), AlphaSlot(matC, [0, 0, 0, 0]))),
        };

        Dictionary<object, int> layerOf = LandscapeBatchMesh.BatchLayerIndices(chunks, batch, b);
        byte[] map = LandscapeBatchMesh.SlotMapBytes(chunks, batch, b, layerOf);

        int stride = LandscapeTerrainBatch.MaxSlots * 4;

        // Chunk 0 slot 1 and chunk 1 slot 1 use different materials, so different array layers.
        byte chunk0Slot1Layer = map[(0 * stride) + (1 * 4)];
        byte chunk1Slot1Layer = map[(1 * stride) + (1 * 4)];
        Assert.AreNotEqual(chunk0Slot1Layer, chunk1Slot1Layer);
        Assert.AreEqual(layerOf[2], (int)chunk0Slot1Layer);
        Assert.AreEqual(layerOf[3], (int)chunk1Slot1Layer);

        // Existing slots are flagged in alpha; anything past a chunk's 2 slots is not.
        Assert.AreEqual(255, (int)map[(0 * stride) + (0 * 4) + 3]);
        Assert.AreEqual(255, (int)map[(0 * stride) + (1 * 4) + 3]);
        Assert.AreEqual(0, (int)map[(0 * stride) + (2 * 4) + 3]);
        Assert.AreEqual(0, (int)map[(1 * stride) + (2 * 4) + 3]);
    }

    [EditorTest(Category = "LandscapeBatch", Thread = TestThread.Background)]
    public static void AlphaAtlasPlacesAChunksBytesInItsOwnTile()
    {
        const int b = 2;
        const int resolution = 2;
        var batch = new LandscapeBatchCoord(0, 0);
        LandscapeMaterial mat = Material(1, "a");

        // Only chunk (1,0) — batch index 1, so tile column 1, row 0 — carries alpha.
        var chunks = new List<(ChunkCoord, LandscapeChunkOutput)>
        {
            (new ChunkCoord(1, 0), Chunk(new ChunkCoord(1, 0), resolution, BaseSlot(mat), AlphaSlot(mat, [1, 2, 3, 4]))),
        };

        byte[][] layers = LandscapeBatchMesh.AlphaAtlasLayers(chunks, batch, b, resolution);

        Assert.AreEqual(1, layers.Length);
        int tileStride = b * resolution;
        byte[] layer = layers[0];

        // Tile at column 1, row 0: origin (2, 0) in a 4x4 layer.
        Assert.AreEqual(1, (int)layer[(0 * tileStride) + 2]);
        Assert.AreEqual(2, (int)layer[(0 * tileStride) + 3]);
        Assert.AreEqual(3, (int)layer[(1 * tileStride) + 2]);
        Assert.AreEqual(4, (int)layer[(1 * tileStride) + 3]);

        // Every texel outside that tile is untouched.
        int nonZero = 0;
        foreach (byte value in layer)
        {
            if (value != 0)
            {
                nonZero++;
            }
        }

        Assert.AreEqual(4, nonZero);
    }

    private static LandscapeMaterial Material(int recordId, string name) =>
        new() { RecordId = recordId, Name = name };

    private static LandscapeChunkLayer BaseSlot(LandscapeMaterial? material) =>
        new() { Material = material, Alpha = null };

    private static LandscapeChunkLayer AlphaSlot(LandscapeMaterial? material, byte[] alpha) =>
        new() { Material = material, Alpha = alpha };

    private static LandscapeChunkOutput Chunk(ChunkCoord coord, int alphaResolution, params LandscapeChunkLayer[] layers) =>
        new()
        {
            Coord = coord,
            HeightResolution = 2,
            Heights = new float[4],
            AlphaResolution = alphaResolution,
            Layers = layers,
            HoleResolution = 1,
            Holes = new bool[1],
            VertexColors = new Color[4],
            VertexLight = new Color[4],
        };
}
