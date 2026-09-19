using System.Linq;

namespace WorldMapStudio;

public static class PaintImagePixelTests
{
    private static PaintImage NewImage()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16, components: 4);
        return image;
    }

    private static byte[] Rect(int width, int height, byte seed)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(seed + i);
        }

        return pixels;
    }

    [EditorTest(Category = "Image Pixels", Thread = TestThread.Background)]
    public static void A_written_rectangle_reads_back_across_chunk_seams()
    {
        PaintImage image = NewImage();
        byte[] pixels = Rect(20, 9, seed: 3);
        int revision = image.ContentRevision;

        Assert.IsTrue(image.WritePixels(10, 12, 20, 9, pixels));

        Assert.IsTrue(image.ReadPixels(10, 12, 20, 9).SequenceEqual(pixels));
        Assert.Greater(image.ContentRevision, revision);
        Assert.AreEqual(0, image.ReadPixels(0, 0, 4, 4).Count(value => value != 0));
    }

    [EditorTest(Category = "Image Pixels", Thread = TestThread.Background)]
    public static void A_write_joins_the_stroke_and_snapshots_each_chunk_before_it_changes()
    {
        PaintImage image = NewImage();
        image.WritePixels(0, 0, 8, 8, Rect(8, 8, seed: 1));

        image.BeginStroke();
        image.WritePixels(4, 4, 24, 4, Rect(24, 4, seed: 9));
        var edits = image.EndStroke();

        Assert.AreEqual(2, edits.Count, "the write touched two chunks");
        var existing = edits.Single(edit => edit.Coord.X == 0);
        var created = edits.Single(edit => edit.Coord.X == 1);
        Assert.IsNotNull(existing.Before);
        Assert.IsNull(created.Before, "a chunk that did not exist has no before-state");
        Assert.IsNotNull(created.After);
    }

    [EditorTest(Category = "Image Pixels", Thread = TestThread.Background)]
    public static void Writing_zeros_over_an_absent_chunk_creates_nothing_and_over_paint_removes_it()
    {
        PaintImage image = NewImage();

        Assert.IsFalse(image.WritePixels(0, 0, 16, 16, new byte[16 * 16 * 4]));
        Assert.AreEqual(0, image.ChunkCount);

        image.WritePixels(0, 0, 16, 16, Rect(16, 16, seed: 5));
        Assert.AreEqual(1, image.ChunkCount);

        Assert.IsTrue(image.WritePixels(0, 0, 16, 16, new byte[16 * 16 * 4]));
        Assert.AreEqual(0, image.ChunkCount, "an all-zero chunk is absent again");
    }

    [EditorTest(Category = "Image Pixels", Thread = TestThread.Background)]
    public static void A_stored_chunk_that_is_not_resident_refuses_the_write()
    {
        PaintImage image = NewImage();
        image.LoadManifest([new ImageChunkCoord(1, 0)]);

        Assert.IsFalse(image.CanEdit(new ImageChunkCoord(1, 0)));
        Assert.IsTrue(image.CanEdit(new ImageChunkCoord(0, 0)));
        Assert.IsFalse(image.CanEditRegion(10, 0, 12, 4));
        Assert.IsFalse(image.WritePixels(10, 0, 12, 4, Rect(12, 4, seed: 2)));
        Assert.AreEqual(0, image.ChunkCount, "nothing was written, not even to the resident side");
    }

    [EditorTest(Category = "Image Pixels", Thread = TestThread.Background)]
    public static void A_rectangle_outside_the_image_is_rejected()
    {
        PaintImage image = NewImage();

        Assert.IsFalse(image.WritePixels(60, 60, 8, 8, Rect(8, 8, seed: 1)));
        Assert.IsFalse(image.CanEditRegion(-1, 0, 4, 4));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => image.ReadPixels(60, 60, 8, 8));
    }
}
