using Godot;

namespace WorldMapStudio;

/// <summary>
/// Pins how a binding's swizzle reduces a channel to one number. The pool reads a single stored
/// component directly where it can and widens to a Color only where it must, and the two paths have
/// to agree on every combination of channel width and swizzle — a disagreement shows up as a splat
/// slot sampling the wrong texture, which looks like a bad import rather than a sampling bug.
/// </summary>
public static class LandscapeChannelPoolTests
{
    private const float ChunkSize = 64.0f;

    private static readonly ChunkCoord Origin = new(0, 0);

    [EditorTest(Category = "LandscapeChannelPool", Thread = TestThread.Background)]
    public static void SwizzleReadsTheNamedComponentOfAColorChannel()
    {
        LandscapeChannelPool pool = Filled(components: 4, [0.1f, 0.2f, 0.3f, 0.4f]);

        Assert.AreApproximatelyEqual(0.1f, Read(pool, LandscapeSwizzle.Native), 0.0001);
        Assert.AreApproximatelyEqual(0.1f, Read(pool, LandscapeSwizzle.R), 0.0001);
        Assert.AreApproximatelyEqual(0.2f, Read(pool, LandscapeSwizzle.G), 0.0001);
        Assert.AreApproximatelyEqual(0.3f, Read(pool, LandscapeSwizzle.B), 0.0001);
        Assert.AreApproximatelyEqual(0.4f, Read(pool, LandscapeSwizzle.A), 0.0001);
    }

    [EditorTest(Category = "LandscapeChannelPool", Thread = TestThread.Background)]
    public static void EverySwizzleOfAScalarChannelReadsItsOneValue()
    {
        LandscapeChannelPool pool = Filled(components: 1, [0.75f]);

        Assert.AreApproximatelyEqual(0.75f, Read(pool, LandscapeSwizzle.Native), 0.0001);
        Assert.AreApproximatelyEqual(0.75f, Read(pool, LandscapeSwizzle.R), 0.0001);
        Assert.AreApproximatelyEqual(0.75f, Read(pool, LandscapeSwizzle.G), 0.0001);
        Assert.AreApproximatelyEqual(0.75f, Read(pool, LandscapeSwizzle.A), 0.0001);
        Assert.AreApproximatelyEqual(0.75f, Read(pool, LandscapeSwizzle.Luminance), 0.0001);
    }

    [EditorTest(Category = "LandscapeChannelPool", Thread = TestThread.Background)]
    public static void AlphaOfAThreeComponentChannelIsOpaque()
    {
        LandscapeChannelPool pool = Filled(components: 3, [0.1f, 0.2f, 0.3f]);

        Assert.AreApproximatelyEqual(1.0f, Read(pool, LandscapeSwizzle.A), 0.0001);
        Assert.AreApproximatelyEqual(0.2f, Read(pool, LandscapeSwizzle.G), 0.0001);
    }

    [EditorTest(Category = "LandscapeChannelPool", Thread = TestThread.Background)]
    public static void LuminanceWeightsTheStoredRgb()
    {
        LandscapeChannelPool pool = Filled(components: 4, [1.0f, 0.0f, 0.0f, 0.0f]);

        Assert.AreApproximatelyEqual(0.299f, Read(pool, LandscapeSwizzle.Luminance), 0.0001);
        Assert.AreApproximatelyEqual(0.299f, Read(pool, LandscapeSwizzle.Rgb), 0.0001);
    }

    [EditorTest(Category = "LandscapeChannelPool", Thread = TestThread.Background)]
    public static void ColorReadWidensPerTheChannelsOwnWidth()
    {
        Color scalar = Filled(components: 1, [0.5f]).SampleColor(Binding(LandscapeSwizzle.Native), Centre);
        Assert.AreApproximatelyEqual(0.5f, scalar.A, 0.0001, "a scalar channel replicates into every component");

        Color rgb = Filled(components: 3, [0.1f, 0.2f, 0.3f]).SampleColor(Binding(LandscapeSwizzle.Native), Centre);
        Assert.AreApproximatelyEqual(1.0f, rgb.A, 0.0001, "a 3-component channel has no stored alpha");
        Assert.AreApproximatelyEqual(0.3f, rgb.B, 0.0001);
    }

    [EditorTest(Category = "LandscapeChannelPool", Thread = TestThread.Background)]
    public static void PositionsOutsideTheBlockReadAsZero()
    {
        LandscapeChannelPool pool = Filled(components: 4, [0.1f, 0.2f, 0.3f, 0.4f]);
        var outside = new Vector3(ChunkSize * 4.0f, 0.0f, ChunkSize * 4.0f);

        Assert.AreApproximatelyEqual(0.0f, pool.SampleScalar(Binding(LandscapeSwizzle.G), outside), 0.0001);
    }

    [EditorTest(Category = "LandscapeChannelPool", Thread = TestThread.Background)]
    public static void UnknownChannelReadsAsZero()
    {
        LandscapeChannelPool pool = Filled(components: 4, [0.1f, 0.2f, 0.3f, 0.4f]);

        Assert.AreApproximatelyEqual(0.0f, pool.SampleScalar(new LandscapeChannelBinding("absent", LandscapeSwizzle.R), Centre), 0.0001);
    }

    private static Vector3 Centre => new(ChunkSize * 0.5f, 0.0f, ChunkSize * 0.5f);

    private static LandscapeChannelBinding Binding(LandscapeSwizzle swizzle) => new("mask", swizzle);

    private static float Read(LandscapeChannelPool pool, LandscapeSwizzle swizzle) =>
        pool.SampleScalar(Binding(swizzle), Centre);

    // A single chunk whose every texel holds the same value, so a sample anywhere inside it reads that
    // value back whatever the bilinear weights work out to.
    private static LandscapeChannelPool Filled(int components, float[] texel)
    {
        var channel = new LandscapeChannel { Name = "mask", RecordId = 1, Resolution = 8, Components = components };
        var settings = new LandscapeSettings { ChunkWorldSize = ChunkSize };
        var pool = new LandscapeChannelPool(settings, [channel]);

        float[] buffer = pool.Buffer(channel.Name, Origin)!;
        for (int i = 0; i < buffer.Length; i += components)
        {
            texel.CopyTo(buffer, i);
        }

        return pool;
    }
}
