using Godot;

namespace WorldMapStudio;

/// <summary>
/// Pins <see cref="ChannelVertexColorPaint"/>'s <c>Exponent</c>/<c>Scale</c> decode: defaults must
/// reproduce a plain painted color unchanged, and a non-default pair must decode an encoded multiplier
/// the way an imported MCCV byte needs to (see the WoW plugin's vertex color import fix).
/// </summary>
public static class ChannelVertexFunctionsTests
{
    private const float ChunkSize = 64.0f;
    private static readonly ChunkCoord Origin = new(0, 0);

    // A 3x3 evaluation grid puts vertex (1,1) exactly at the chunk centre — clear of the bilinear
    // sampler's chunk-edge halo, the same reason LandscapeChannelPoolTests samples its Centre point.
    private const int Resolution = 3;
    private const int CentreIndex = (1 * Resolution) + 1;

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void Default_exponent_and_scale_reproduce_the_sampled_color()
    {
        Color[] colors = Evaluate(sourceValue: 0.498f, exponent: 1.0f, scale: 1.0f);

        Assert.AreApproximatelyEqual(0.498, colors[CentreIndex].R, 0.001);
        Assert.AreApproximatelyEqual(0.498, colors[CentreIndex].G, 0.001);
        Assert.AreApproximatelyEqual(0.498, colors[CentreIndex].B, 0.001);
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void Exponent_and_scale_decode_an_mccv_style_neutral_byte()
    {
        // 0x7F / 255 ~= 0.498, MCCV's own mid-grey neutral. pow(0.498, 2) * 4 ~= 0.992 — just under
        // white, matching the on-disk byte's near-but-not-quite-neutral value.
        Color[] colors = Evaluate(sourceValue: 0.498f, exponent: 2.0f, scale: 4.0f);

        Assert.AreApproximatelyEqual(0.992, colors[CentreIndex].R, 0.005);
    }

    [EditorTest(Category = "LandscapeFunctions", Thread = TestThread.Background)]
    public static void Exponent_and_scale_decode_a_fully_bright_byte_above_one()
    {
        Color[] colors = Evaluate(sourceValue: 1.0f, exponent: 2.0f, scale: 4.0f);

        Assert.AreApproximatelyEqual(4.0, colors[CentreIndex].R, 0.001,
            "the whole point of the decode: a bright source must survive above 1.0");
    }

    private static Color[] Evaluate(float sourceValue, float exponent, float scale)
    {
        var channel = new LandscapeChannel { Name = "source", RecordId = 1, Resolution = 8, Components = 4 };
        var settings = new LandscapeSettings { ChunkWorldSize = ChunkSize };
        var pool = new LandscapeChannelPool(settings, [channel]);

        float[] buffer = pool.Buffer(channel.Name, Origin)!;
        for (int i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = sourceValue;
            buffer[i + 1] = sourceValue;
            buffer[i + 2] = sourceValue;
            buffer[i + 3] = 1.0f;
        }

        var values = new LandscapeParameterValues();
        values.Set(ChannelVertexColorPaint.Source, "source");
        values.Set(ChannelVertexColorPaint.Exponent, exponent);
        values.Set(ChannelVertexColorPaint.Scale, scale);

        var context = new LandscapeEvalContext(Origin, pool, settings, values, Resolution);
        var colors = new Color[Resolution * Resolution];
        System.Array.Fill(colors, Colors.White);

        new ChannelVertexColorPaint().Evaluate(context, colors);
        return colors;
    }
}
