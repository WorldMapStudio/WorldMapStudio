namespace WorldMapStudio;

/// <summary>
/// Pins the pure indexing maths a terrain render batch is built on: which batch a chunk falls in,
/// where a chunk sits inside its batch, and the streaming key. Mesh and material construction calls
/// into Godot and cannot run headless, so only this side is covered.
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
}
