using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class ImageTests
{
    private const string MaskChannel = "paint";

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Painting_changes_the_bitmap_and_content_version()
    {
        EditorContext context = NewContext("__wms_image_paint_test__");
        PaintImage image = NewImage(context, id: 1);
        image.Resize(32, 32);

        var entity = new SceneEntity();
        var target = new ImageComponent(context.Images) { ImageId = image.RecordId };
        entity.AddComponent(target);
        int beforeVersion = target.ContentVersion;

        bool changed = target.Paint(Vector3.Zero, 6.0f, 1.0f, erase: false);

        Assert.IsTrue(changed);
        Assert.IsTrue(image.Pixels.ToArray().Any(pixel => pixel > 0));
        Assert.AreNotEqual(beforeVersion, target.ContentVersion);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Painting_across_a_chunk_seam_matches_an_unchunked_stroke()
    {
        var chunked = new PaintImage();
        chunked.ConfigureNew(64, 64, chunkSize: 16);

        var unchunked = new PaintImage();
        unchunked.ConfigureNew(64, 64, chunkSize: 64);

        // Centered on the (16, 16) chunk corner, so the stamp spans four chunks in the chunked image.
        const float u = 16.0f / 64.0f;
        const float v = 16.0f / 64.0f;
        const float radius = 12.0f / 64.0f;

        bool changedChunked = chunked.Paint(u, v, radius, radius, 0.8f, erase: false);
        bool changedUnchunked = unchunked.Paint(u, v, radius, radius, 0.8f, erase: false);

        Assert.IsTrue(changedChunked);
        Assert.IsTrue(changedUnchunked);
        Assert.IsTrue(chunked.CopyPixels().SequenceEqual(unchunked.CopyPixels()),
            "a stroke spanning several chunks should paint identically to the same stroke on a single-chunk image");
        Assert.Greater(chunked.ChunkCount, 1, "the stroke should have touched more than one chunk");
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Erasing_an_untouched_chunk_leaves_it_absent()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);

        bool changed = image.Paint(0.25f, 0.25f, 0.1f, 0.1f, 1.0f, erase: true);

        Assert.IsFalse(changed);
        Assert.AreEqual(0, image.ChunkCount);
        Assert.IsTrue(image.CopyPixels().All(pixel => pixel == 0));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Painting_materializes_only_the_chunks_the_stroke_touches()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16); // a 4x4 grid of chunks

        // A small stamp well inside the top-left chunk, nowhere near a boundary.
        image.Paint(4.0f / 64.0f, 4.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false);

        Assert.AreEqual(1, image.ChunkCount);
        Assert.IsTrue(image.ChunkCoords.Contains(new ImageChunkCoord(0, 0)));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Loading_pixels_skips_chunks_that_are_entirely_zero()
    {
        var image = new PaintImage();
        image.ConfigureNew(32, 32, chunkSize: 16); // a 2x2 grid of chunks

        var dense = new byte[32 * 32];
        dense[(20 * 32) + 20] = 200; // lands in chunk (1, 1)

        image.ReplacePixels(dense);

        Assert.AreEqual(1, image.ChunkCount);
        Assert.IsTrue(image.ChunkCoords.Contains(new ImageChunkCoord(1, 1)));
        Assert.IsTrue(image.CopyPixels().SequenceEqual(dense));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Sampler_matches_dense_pixels_across_a_chunk_boundary()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.Paint(0.5f, 0.5f, 0.2f, 0.2f, 1.0f, erase: false);

        byte[] dense = image.CopyPixels();
        ImageSampler sampler = image.CreateSampler();

        // Exact texel centers on both sides of a chunk seam (x/y = 16), so the comparison is exact
        // rather than approximate through bilinear interpolation.
        foreach (int x in new[] { 15, 16, 31, 32 })
        {
            foreach (int y in new[] { 15, 16, 31, 32 })
            {
                float u = (x + 0.5f) / 64.0f;
                float v = (y + 0.5f) / 64.0f;
                float expected = dense[(y * 64) + x] / 255.0f;
                Assert.AreApproximatelyEqual(expected, sampler.Sample(u, v), 1e-3, $"mismatch at ({x},{y})");
            }
        }
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Chunk_codec_round_trips_both_a_sparse_and_a_dense_buffer()
    {
        var sparse = new byte[64 * 64]; // mostly zero, like a real paint mask — should favor deflate
        sparse[100] = 200;
        sparse[101] = 210;

        var random = new Random(1234);
        var dense = new byte[64 * 64];
        random.NextBytes(dense); // incompressible — deflate would only grow it, so raw should win

        (byte sparseFormat, byte[] sparseBytes) = ImageChunkCodec.Encode(sparse);
        (byte denseFormat, byte[] denseBytes) = ImageChunkCodec.Encode(dense);

        Assert.AreEqual(ImageChunkCodec.FormatDeflate, sparseFormat);
        Assert.IsTrue(sparseBytes.Length < sparse.Length, "a mostly-zero chunk should compress smaller than raw");
        Assert.AreEqual(ImageChunkCodec.FormatRaw, denseFormat);

        Assert.IsTrue(ImageChunkCodec.Decode(sparseFormat, sparseBytes, 64).SequenceEqual(sparse));
        Assert.IsTrue(ImageChunkCodec.Decode(denseFormat, denseBytes, 64).SequenceEqual(dense));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Persisted_chunk_coords_follow_load_and_commit_but_not_a_bare_edit()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);

        // Simulate a factory load: two chunks already in storage.
        image.LoadChunks(
        [
            (new ImageChunkCoord(0, 0), new byte[16 * 16]),
            (new ImageChunkCoord(1, 1), new byte[16 * 16]),
        ]);

        Assert.AreEqual(2, image.PersistedChunkCoords.Count);
        Assert.AreEqual(2, image.ChunkCount);

        // Painting a third chunk changes what's live in memory but must not retroactively change what
        // the factory believes is already on disk — that only moves once a commit's Stage writeback
        // runs, which is what lets Stage tell "needs an update" from "needs an insert".
        image.Paint(40.0f / 64.0f, 40.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false);

        Assert.AreEqual(3, image.ChunkCount);
        Assert.AreEqual(2, image.PersistedChunkCoords.Count);

        image.SetPersistedChunkCoords(image.ChunkCoords);
        Assert.AreEqual(3, image.PersistedChunkCoords.Count);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Painting_a_stored_but_not_resident_chunk_is_refused_without_data_loss()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.LoadManifest([new ImageChunkCoord(0, 0)]); // storage has this chunk, but it is not loaded

        bool changed = image.Paint(4.0f / 64.0f, 4.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false);

        Assert.IsFalse(changed, "painting a chunk that is stored but not resident must not fabricate a zero buffer over it");
        Assert.AreEqual(0, image.ChunkCount);
        Assert.AreEqual(1, image.PersistedChunkCoords.Count);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Publishing_loaded_chunks_bumps_only_view_revision_and_unblocks_painting()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.LoadManifest([new ImageChunkCoord(0, 0)]);

        int contentBefore = image.ContentRevision;
        int viewBefore = image.ViewRevision;

        image.PublishLoadedChunks([(new ImageChunkCoord(0, 0), new byte[16 * 16])]);

        Assert.AreEqual(contentBefore, image.ContentRevision, "loading a chunk from storage is not a content edit");
        Assert.Greater(image.ViewRevision, viewBefore);

        bool changed = image.Paint(4.0f / 64.0f, 4.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false);
        Assert.IsTrue(changed, "the chunk is now resident, so painting should work normally");
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Evicting_a_dirty_chunk_is_refused()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.Paint(4.0f / 64.0f, 4.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false);

        var coord = new ImageChunkCoord(0, 0);
        Assert.IsTrue(image.IsDirty(coord));

        image.EvictChunk(coord);

        Assert.IsTrue(image.IsResident(coord), "a chunk with unsaved edits must never be evicted");
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Evicting_a_clean_chunk_leaves_it_stored_but_not_resident()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.LoadChunks([(new ImageChunkCoord(0, 0), new byte[16 * 16])]);

        var coord = new ImageChunkCoord(0, 0);
        Assert.IsFalse(image.IsDirty(coord));

        int viewBefore = image.ViewRevision;
        image.EvictChunk(coord);

        Assert.IsFalse(image.IsResident(coord));
        Assert.IsTrue(image.IsStored(coord), "eviction only drops the in-memory copy, not the storage manifest");
        Assert.Greater(image.ViewRevision, viewBefore);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void ChunksNeededFor_intersects_footprint_with_region_and_adds_headroom()
    {
        EditorContext context = NewContext("__wms_image_chunks_needed_test__");
        PaintImage image = NewImage(context, id: 1);
        image.ConfigureNew(64, 64, chunkSize: 16); // a 4x4 grid of chunks

        var entity = new SceneEntity();
        var component = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            WorldSizeX = 64.0f,
            WorldSizeZ = 64.0f,
        };
        entity.AddComponent(component);

        // Covers world X/Z in [-40, -8] — the near-corner quarter of the placement's [-32, 32] footprint.
        var region = new Aabb(new Vector3(-40.0f, -1000.0f, -40.0f), new Vector3(32.0f, 2000.0f, 32.0f));

        ImageChunkRect? rect = component.ChunksNeededFor(region);

        Assert.IsNotNull(rect);
        Assert.AreEqual(0, rect!.Value.MinX);
        Assert.AreEqual(0, rect.Value.MinY);
        Assert.AreEqual(2, rect.Value.MaxX);
        Assert.AreEqual(2, rect.Value.MaxY);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void ChunksNeededFor_returns_null_when_the_region_misses_the_footprint()
    {
        EditorContext context = NewContext("__wms_image_chunks_needed_miss_test__");
        PaintImage image = NewImage(context, id: 1);

        var entity = new SceneEntity();
        var component = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            WorldSizeX = 64.0f,
            WorldSizeZ = 64.0f,
        };
        entity.AddComponent(component);

        var farRegion = new Aabb(new Vector3(1000.0f, -10.0f, 1000.0f), new Vector3(10.0f, 20.0f, 10.0f));

        Assert.IsNull(component.ChunksNeededFor(farRegion));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Stroke_tracking_records_state_before_the_whole_stroke_not_each_dab()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 64); // a single chunk, simplest case

        image.BeginStroke();
        image.Paint(0.5f, 0.5f, 0.1f, 0.1f, 0.5f, erase: false);
        image.Paint(0.5f, 0.5f, 0.1f, 0.1f, 0.5f, erase: false); // second dab, same spot, darker still
        var edits = image.EndStroke();

        Assert.AreEqual(1, edits.Count);
        Assert.IsNull(edits[0].Before, "the chunk did not exist before the stroke started");
        Assert.IsNotNull(edits[0].After);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Stroke_tracking_omits_chunks_touched_but_left_unchanged()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);

        image.BeginStroke();
        bool changed = image.Paint(0.9f, 0.9f, 0.001f, 0.001f, 1.0f, erase: true); // erasing nothing: a no-op
        var edits = image.EndStroke();

        Assert.IsFalse(changed);
        Assert.AreEqual(0, edits.Count);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void PaintImageChunksCommand_apply_and_revert_round_trip_a_stroke_with_narrowed_bounds()
    {
        EditorContext context = NewContext("__wms_paint_chunks_command_test__");
        PaintImage image = NewImage(context, id: 1);
        image.ConfigureNew(64, 64, chunkSize: 16); // a 4x4 grid of chunks

        var entity = new SceneEntity();
        var component = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            WorldSizeX = 64.0f,
            WorldSizeZ = 64.0f,
        };
        entity.AddComponent(component);
        context.Scene.Add(entity);

        image.BeginStroke();
        image.Paint(4.0f / 64.0f, 4.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false);
        var edits = image.EndStroke();

        Assert.AreEqual(1, edits.Count);
        ImageChunkCoord coord = edits[0].Coord;
        Assert.IsNull(edits[0].Before);
        Assert.IsTrue(image.IsResident(coord));

        var command = new PaintImageChunksCommand(image, edits, component.AffectedEntities, "Paint Test");

        Assert.AreEqual(1, command.ChunkImpacts.Count);
        ChunkChangeImpact impact = command.ChunkImpacts[0];
        Assert.IsNull(impact.Before);
        Assert.IsNotNull(impact.After);
        Assert.AreApproximatelyEqual(16.0, impact.After!.Bounds.Size.X, 1e-4,
            "bounds should be narrowed to the one touched 16px chunk, not the whole 64-wide footprint");

        command.Revert();
        Assert.IsFalse(image.IsResident(coord), "reverting should remove a chunk that did not exist before the stroke");

        command.Apply();
        Assert.IsTrue(image.IsResident(coord));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Marking_chunks_clean_after_commit_allows_eviction()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.Paint(4.0f / 64.0f, 4.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false);

        var coord = new ImageChunkCoord(0, 0);
        Assert.IsTrue(image.IsDirty(coord));

        image.EvictChunk(coord);
        Assert.IsTrue(image.IsResident(coord), "still dirty, so eviction should still refuse it");

        image.MarkChunksClean([coord]);
        Assert.IsFalse(image.IsDirty(coord));

        image.EvictChunk(coord);
        Assert.IsFalse(image.IsResident(coord), "now clean, eviction should succeed");
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Evicting_a_chunk_does_not_mark_it_for_deletion()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.LoadChunks([(new ImageChunkCoord(0, 0), new byte[16 * 16])]);

        var coord = new ImageChunkCoord(0, 0);
        image.EvictChunk(coord);

        Assert.IsFalse(image.IsResident(coord));
        Assert.IsTrue(image.IsStored(coord), "eviction must not make Stage think this chunk's content is gone");
        Assert.IsFalse(image.RemovedSincePersist.Contains(coord));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Erasing_a_chunk_back_to_all_zero_prunes_it_and_marks_it_removed()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        var fullyPainted = new byte[16 * 16];
        Array.Fill(fullyPainted, (byte)255);
        image.LoadChunks([(new ImageChunkCoord(0, 0), fullyPainted)]);

        var coord = new ImageChunkCoord(0, 0);
        Assert.IsTrue(image.IsResident(coord));
        Assert.IsTrue(image.IsStored(coord));

        // A radius far larger than the canvas guarantees ~full weight everywhere in the chunk, so a
        // single erase pass reduces every pixel to zero.
        bool changed = image.Paint(4.0f / 64.0f, 4.0f / 64.0f, 10.0f, 10.0f, 1.0f, erase: true);

        Assert.IsTrue(changed);
        Assert.IsFalse(image.IsResident(coord), "a chunk erased back to all-zero should be pruned, not kept resident-but-empty");
        Assert.IsTrue(image.RemovedSincePersist.Contains(coord));
        Assert.IsFalse(image.IsStored(coord), "IsStored should reflect the pending removal even before commit");
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Committing_after_an_eviction_keeps_the_evicted_chunk_in_the_manifest()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.LoadChunks(
        [
            (new ImageChunkCoord(0, 0), new byte[16 * 16]),
            (new ImageChunkCoord(1, 1), new byte[16 * 16]),
        ]);

        image.EvictChunk(new ImageChunkCoord(1, 1)); // now stored-but-not-resident

        // Simulate a commit staging only what is currently resident (chunk (1,1) is not, having just
        // been evicted) — the bug this guards against overwrote the manifest with exactly this set.
        var current = new List<ImageChunkCoord> { new(0, 0) };
        image.CommitChunkPersistence(current, deleted: []);

        Assert.IsTrue(image.IsStored(new ImageChunkCoord(1, 1)), "an evicted chunk must stay in the manifest across an unrelated commit");
        Assert.IsFalse(image.IsResident(new ImageChunkCoord(1, 1)));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void ResizeCanvas_shrinking_drops_out_of_bounds_chunks_and_marks_them_removed()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16); // a 4x4 grid of chunks
        image.LoadChunks(
        [
            (new ImageChunkCoord(0, 0), new byte[16 * 16]),
            (new ImageChunkCoord(3, 3), new byte[16 * 16]),
        ]);

        IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> dropped = image.ResizeCanvas(32, 32); // now a 2x2 grid

        Assert.AreEqual(1, dropped.Count);
        Assert.AreEqual(new ImageChunkCoord(3, 3), dropped[0].Coord);
        Assert.IsFalse(image.IsResident(new ImageChunkCoord(3, 3)));
        Assert.IsTrue(image.RemovedSincePersist.Contains(new ImageChunkCoord(3, 3)));
        Assert.IsTrue(image.IsResident(new ImageChunkCoord(0, 0)), "a chunk still within the new bounds should survive untouched");
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void ResizeCanvas_extending_does_not_drop_anything()
    {
        var image = new PaintImage();
        image.ConfigureNew(32, 32, chunkSize: 16);
        image.LoadChunks([(new ImageChunkCoord(0, 0), new byte[16 * 16])]);

        IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> dropped = image.ResizeCanvas(64, 64);

        Assert.AreEqual(0, dropped.Count);
        Assert.IsTrue(image.IsResident(new ImageChunkCoord(0, 0)));
        Assert.AreEqual(4, image.ChunksX);
        Assert.AreEqual(4, image.ChunksY);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void ClearAll_drops_every_resident_chunk()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.LoadChunks(
        [
            (new ImageChunkCoord(0, 0), new byte[16 * 16]),
            (new ImageChunkCoord(1, 1), new byte[16 * 16]),
        ]);

        IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> cleared = image.ClearAll();

        Assert.AreEqual(2, cleared.Count);
        Assert.AreEqual(0, image.ChunkCount);
        Assert.IsTrue(image.RemovedSincePersist.Contains(new ImageChunkCoord(0, 0)));
        Assert.IsTrue(image.RemovedSincePersist.Contains(new ImageChunkCoord(1, 1)));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void ResizeImageCanvasCommand_revert_restores_dimensions_and_dropped_chunks()
    {
        EditorContext context = NewContext("__wms_resize_canvas_command_test__");
        PaintImage image = NewImage(context, id: 1);
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.LoadChunks([(new ImageChunkCoord(3, 3), new byte[16 * 16])]);

        var entity = new SceneEntity();
        var component = new ImageComponent(context.Images) { ImageId = image.RecordId, WorldSizeX = 64.0f, WorldSizeZ = 64.0f };
        entity.AddComponent(component);
        context.Scene.Add(entity);

        var command = new ResizeImageCanvasCommand(image, 32, 32, component.AffectedEntities, "Resize Test");

        Assert.AreEqual(32, image.Width);
        Assert.IsFalse(image.IsResident(new ImageChunkCoord(3, 3)));

        command.Revert();

        Assert.AreEqual(64, image.Width);
        Assert.IsTrue(image.IsResident(new ImageChunkCoord(3, 3)), "shrinking then reverting should restore the dropped chunk");

        command.Apply();

        Assert.AreEqual(32, image.Width);
        Assert.IsFalse(image.IsResident(new ImageChunkCoord(3, 3)));
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void A_100k_canvas_can_be_configured_and_painted_without_a_dense_allocation()
    {
        var image = new PaintImage();
        image.ConfigureNew(100_000, 100_000, chunkSize: 512);

        Assert.AreEqual(196, image.ChunksX);
        Assert.AreEqual(196, image.ChunksY);

        bool changed = image.Paint(0.5f, 0.5f, 0.001f, 0.001f, 1.0f, erase: false);

        Assert.IsTrue(changed);
        Assert.AreEqual(1, image.ChunkCount, "painting a small dot should only materialize one chunk on a huge canvas");
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Images_keep_authored_height_but_only_yaw_rotation()
    {
        EditorContext context = NewContext("__wms_image_transform_test__");
        var target = new SceneEntity();
        target.AddComponent(new ImageComponent(context.Images));
        target.Transform = new Transform3D(
            Basis.FromEuler(new Vector3(0.35f, 0.7f, -0.2f)),
            new Vector3(12.0f, 99.0f, 24.0f));

        // Height has no bearing on the terrain projection (Rasterize/Paint never read local.Y), so
        // it's free — unlike rotation, where only yaw actually changes the projected footprint.
        Assert.AreApproximatelyEqual(99.0, target.Transform.Origin.Y, 1e-5);
        Assert.AreApproximatelyEqual(0.0, target.Transform.Basis.X.Y, 1e-5);
        Assert.AreApproximatelyEqual(1.0, target.Transform.Basis.Y.Y, 1e-5);
        Assert.AreApproximatelyEqual(0.0, target.Transform.Basis.Z.Y, 1e-5);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Display_layer_setters_bump_revision()
    {
        var layer = new ImageDisplayLayer();
        int revision0 = layer.Revision;

        layer.Name = "Roads";
        Assert.Greater(layer.Revision, revision0);

        int revision1 = layer.Revision;
        layer.DisplayMode = ImageDisplayMode.LandscapeOverlay;
        Assert.Greater(layer.Revision, revision1);

        int revision2 = layer.Revision;
        layer.BaseColor = new Color(0.1f, 0.2f, 0.3f, 0.0f);
        Assert.Greater(layer.Revision, revision2);

        int revision3 = layer.Revision;
        layer.FullColor = new Color(0.1f, 0.2f, 0.3f, 1.0f);
        Assert.Greater(layer.Revision, revision3);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void None_display_mode_builds_no_node()
    {
        EditorContext context = NewContext("__wms_image_display_none_test__");
        PaintImage image = NewImage(context, id: 1);
        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.None };
        context.Catalog.Add(layer);

        var target = new ImageComponent(context.Images) { ImageId = image.RecordId, DisplayLayerId = layer.RecordId };
        Assert.IsNull(target.BuildNode());
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void LandscapeOverlay_display_mode_builds_no_decals_until_something_is_painted()
    {
        EditorContext context = NewContext("__wms_image_display_overlay_empty_test__");
        PaintImage image = NewImage(context, id: 1);
        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.LandscapeOverlay };
        context.Catalog.Add(layer);

        var target = new ImageComponent(context.Images) { ImageId = image.RecordId, DisplayLayerId = layer.RecordId };

        Node3D? root = target.BuildNode();
        Assert.IsNotNull(root);
        Assert.AreEqual(0, root!.GetChildCount(), "no chunk is resident yet, so there is nothing to project");
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void LandscapeOverlay_display_mode_builds_a_decal_per_painted_chunk_sized_to_its_share_of_the_footprint()
    {
        EditorContext context = NewContext("__wms_image_display_overlay_test__");
        PaintImage image = NewImage(context, id: 1); // default 256x256, chunk size 256 -> exactly one chunk
        image.Paint(0.5f, 0.5f, 0.1f, 0.1f, 1.0f, erase: false);
        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.LandscapeOverlay };
        context.Catalog.Add(layer);

        var target = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            DisplayLayerId = layer.RecordId,
            WorldSizeX = 32.0f,
            WorldSizeZ = 48.0f,
        };

        Node3D? root = target.BuildNode();
        Assert.IsNotNull(root);
        Assert.AreEqual(1, root!.GetChildCount(), "the whole image is one chunk, so painting it materializes exactly one");
        var decal = root.GetChild(0) as Decal;
        Assert.IsNotNull(decal);
        Assert.AreApproximatelyEqual(32.0, decal!.Size.X, 1e-4);
        Assert.AreApproximatelyEqual(48.0, decal.Size.Z, 1e-4);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Object_display_mode_always_builds_a_paintable_backdrop_sized_to_the_footprint()
    {
        EditorContext context = NewContext("__wms_image_display_object_test__");
        PaintImage image = NewImage(context, id: 1);
        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.Object };
        context.Catalog.Add(layer);

        var target = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            DisplayLayerId = layer.RecordId,
            WorldSizeX = 20.0f,
            WorldSizeZ = 10.0f,
        };

        // Unpainted: still gets a full-footprint backdrop, since it is what the Paint tool ray-tests
        // against to let a user start painting in the first place.
        Node3D? root = target.BuildNode();
        Assert.IsNotNull(root);
        var backdrop = root!.GetNodeOrNull<MeshInstance3D>("Backdrop");
        Assert.IsNotNull(backdrop);
        var plane = backdrop!.Mesh as PlaneMesh;
        Assert.IsNotNull(plane);
        Assert.AreApproximatelyEqual(20.0, plane!.Size.X, 1e-5);
        Assert.AreApproximatelyEqual(10.0, plane.Size.Y, 1e-5);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void LandscapeOverlay_display_mode_builds_one_decal_per_resident_chunk_on_a_multi_chunk_image()
    {
        EditorContext context = NewContext("__wms_image_display_overlay_multi_test__");
        PaintImage image = NewImage(context, id: 1);
        image.ConfigureNew(64, 64, chunkSize: 16); // a 4x4 grid of chunks
        image.Paint(4.0f / 64.0f, 4.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false); // chunk (0,0)
        image.Paint(60.0f / 64.0f, 60.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false); // chunk (3,3)

        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.LandscapeOverlay };
        context.Catalog.Add(layer);

        var target = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            DisplayLayerId = layer.RecordId,
            WorldSizeX = 64.0f,
            WorldSizeZ = 64.0f,
        };

        Node3D? root = target.BuildNode();
        Assert.IsNotNull(root);
        Assert.AreEqual(2, root!.GetChildCount(), "one decal per painted chunk, not one for the whole canvas");
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void ChunkGrayscale_texture_is_sized_to_chunk_size_not_canvas_size()
    {
        var image = new PaintImage();
        image.ConfigureNew(64, 64, chunkSize: 16);
        image.Paint(4.0f / 64.0f, 4.0f / 64.0f, 2.0f / 64.0f, 2.0f / 64.0f, 1.0f, erase: false);

        ImageTexture texture = PaintImageTextures.ChunkGrayscale(image, new ImageChunkCoord(0, 0));

        Assert.AreEqual(16, texture.GetWidth());
        Assert.AreEqual(16, texture.GetHeight());
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Overview_texture_is_capped_regardless_of_canvas_size()
    {
        var image = new PaintImage();
        image.ConfigureNew(4096, 4096, chunkSize: 512);

        ImageTexture overview = PaintImageTextures.Overview(image, maxSize: 256);

        Assert.AreEqual(256, overview.GetWidth());
        Assert.AreEqual(256, overview.GetHeight());
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void System_update_refreshes_every_placement_when_the_shared_display_layer_changes()
    {
        EditorContext context = NewContext("__wms_image_display_update_test__");
        PaintImage image = NewImage(context, id: 1);
        var layer = new ImageDisplayLayer { RecordId = 1, DisplayMode = ImageDisplayMode.None };
        context.Catalog.Add(layer);

        var entityA = new SceneEntity();
        var entityB = new SceneEntity();
        entityA.AddComponent(new ImageComponent(context.Images) { ImageId = image.RecordId, DisplayLayerId = layer.RecordId });
        entityB.AddComponent(new ImageComponent(context.Images) { ImageId = image.RecordId, DisplayLayerId = layer.RecordId });
        context.Scene.Add(entityA);
        context.Scene.Add(entityB);
        entityA.CreateRepresentation(context.Root);
        entityB.CreateRepresentation(context.Root);

        var componentA = entityA.Component<ImageComponent>()!;
        var componentB = entityB.Component<ImageComponent>()!;
        context.Images.Update();
        Assert.IsFalse(componentA.NeedsRefresh);
        Assert.IsFalse(componentB.NeedsRefresh);

        layer.DisplayMode = ImageDisplayMode.Object;

        Assert.IsTrue(componentA.NeedsRefresh);
        Assert.IsTrue(componentB.NeedsRefresh);

        context.Images.Update();
        Assert.IsFalse(componentA.NeedsRefresh, "the guarded sweep should have refreshed every placement sharing the layer");
        Assert.IsFalse(componentB.NeedsRefresh);
    }

    [EditorTest(Category = "Image", Thread = TestThread.Main)]
    public static void Rasterizing_an_image_feeds_the_landscape_channel()
    {
        var functions = new LandscapeFunctions();
        functions.Discover(typeof(ChannelMaskAlpha).Assembly);

        var alphaValues = new LandscapeParameterValues();
        alphaValues.Set(ChannelMaskAlpha.Mask, MaskChannel);
        alphaValues.Set(ChannelMaskAlpha.Threshold, 0.1f);
        alphaValues.Set(ChannelMaskAlpha.Softness, 0.0f);

        var material = new LandscapeMaterial
        {
            Name = "painted",
            RecordId = 1,
            TexturePath = "res://painted.png",
            AlphaFunction = "builtin.alpha.channel_mask",
            AlphaParameters = alphaValues.Serialize(),
        };

        var channel = new LandscapeChannel { Name = MaskChannel, RecordId = 1, Resolution = 32 };
        var baseLayer = new LandscapeLayer { Name = "base", RecordId = 1, DrawOrder = 0, IsBase = true };
        var paintLayer = new LandscapeLayer { Name = "paint", RecordId = 2, DrawOrder = 1 };
        var settings = new LandscapeSettings
        {
            ChunkWorldSize = 64.0f,
            ChunkHeightResolution = 9,
            ChunkAlphaResolution = 32,
            TextureLimit = 4,
            FallbackMaterialId = 1,
        };

        var catalog = new LandscapeCatalog([channel], [baseLayer, paintLayer], [material], functions);
        EditorContext context = NewContext("__wms_image_rasterize_test__");
        PaintImage image = NewImage(context, id: 1);
        var entity = new SceneEntity();
        var target = new ImageComponent(context.Images)
        {
            ImageId = image.RecordId,
            Channel = MaskChannel,
            WorldSizeX = 64.0f,
            WorldSizeZ = 64.0f,
        };
        var bind = new LandscapeMaterialBindComponent();
        bind.ReplaceBindings([new LandscapeMaterialBinding(paintLayer.RecordId, material.RecordId)]);
        entity.AddComponent(target);
        entity.AddComponent(bind);
        target.Paint(Vector3.Zero, 12.0f, 1.0f, erase: false);

        LandscapeChunkOutput output = new LandscapeBuilder(settings, catalog, functions)
            .BuildOne(new ChunkCoord(0, 0), entity.Components.OfType<ILandscapeDeformer>().ToList());

        Assert.AreEqual(2, output.Layers.Count);
        Assert.IsTrue(output.Layers[1].Alpha!.Any(alpha => alpha > 200), "painted pixels should become alpha");
        Assert.IsTrue(output.Layers[1].Alpha!.Any(alpha => alpha == 0), "unpainted pixels should stay transparent");
    }

    private static EditorContext NewContext(string name) =>
        new(new Node3D(), new Project { Name = name });

    private static PaintImage NewImage(EditorContext context, int id)
    {
        var image = new PaintImage { RecordId = id, Name = $"Image {id}" };
        context.Catalog.Add(image);
        return image;
    }
}
