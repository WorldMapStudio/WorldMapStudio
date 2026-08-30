using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Maps <see cref="PaintImage"/> to and from the Editor storage's <c>images</c> header table and
/// <c>image_chunks</c> table. Implements <see cref="ICatalogEntityFactory"/> directly rather than going
/// through <see cref="EditorCatalogFactory{TEntity,TRecord}"/> — that base assumes one row per entity,
/// but a chunked image is a header row plus one row per non-empty chunk, and only those chunks are ever
/// written. See <c>.godot/ImageChunkPlan.md</c> for the storage design this implements.
///
/// A whole image's current chunk set is re-staged on every commit — the same "re-persist everything the
/// entity currently holds" shape <see cref="EditorCatalogFactory{TEntity,TRecord}"/> uses for a single
/// row, just spread across many rows here. Only touched chunks round-trip once per-chunk undo (a later
/// phase) exists to know what changed.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class PaintImageFactory : ICatalogEntityFactory
{
    private readonly EditorStorage _storage;

    public PaintImageFactory(EditorStorage storage)
    {
        _storage = storage;
    }

    public float Priority => 0.0f;

    public Type EntityType => typeof(PaintImage);

    public bool Handles(IEntity entity) => entity is PaintImage;

    public async Task<IReadOnlyList<CatalogEntity>> LoadAllAsync()
    {
        await using EditorDbContext context = _storage.CreateContext();
        List<PaintImageRecord> headers = await context.Images.AsNoTracking().ToListAsync().ConfigureAwait(false);
        List<ImageChunkRecord> chunkRows = await context.ImageChunks.AsNoTracking().ToListAsync().ConfigureAwait(false);
        ILookup<int, ImageChunkRecord> chunksByImage = chunkRows.ToLookup(row => row.ImageId);

        return headers.Select(header => (CatalogEntity)ToEntity(header, chunksByImage[header.Id])).ToList();
    }

    public Action Stage(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        var image = (PaintImage)entity;
        int imageId = image.RecordId ?? 0;

        var header = new PaintImageRecord
        {
            Id = imageId,
            Name = image.Name,
            Width = image.Width,
            Height = image.Height,
            ChunkSize = image.ChunkSize,
        };

        // Ids are assigned at creation, so having one no longer means a row exists — IsSaved is what
        // separates an insert from an update, exactly as EditorCatalogFactory does for a single row.
        if (image.IsSaved)
        {
            db.Images.Update(header);
        }
        else
        {
            db.Images.Add(header);
        }

        var current = new HashSet<ImageChunkCoord>(image.ChunkCoords);
        var previouslyPersisted = new HashSet<ImageChunkCoord>(image.PersistedChunkCoords);

        foreach (ImageChunkCoord coord in previouslyPersisted)
        {
            if (!current.Contains(coord))
            {
                db.ImageChunks.Remove(new ImageChunkRecord { ImageId = imageId, ChunkX = coord.X, ChunkY = coord.Y });
            }
        }

        foreach (ImageChunkCoord coord in current)
        {
            byte[] pixels = image.CopyChunkBytes(coord)
                ?? throw new InvalidOperationException($"Image #{imageId} chunk {coord} is in ChunkCoords but has no data.");
            (byte format, byte[] bytes) = ImageChunkCodec.Encode(pixels);
            var row = new ImageChunkRecord
            {
                ImageId = imageId,
                ChunkX = coord.X,
                ChunkY = coord.Y,
                Format = format,
                Pixels = bytes,
            };

            if (previouslyPersisted.Contains(coord))
            {
                db.ImageChunks.Update(row);
            }
            else
            {
                db.ImageChunks.Add(row);
            }
        }

        return () =>
        {
            image.IsSaved = true;
            image.SetPersistedChunkCoords(current);

            // Every chunk just staged now matches storage — clears the way for ImageResidencySystem
            // to evict it again once nothing needs it resident. Without this, a chunk that was ever
            // painted would stay dirty (and so pinned in memory) forever, even after being saved.
            image.MarkChunksClean(current);
        };
    }

    public void StageDelete(DbContext context, IEntity entity)
    {
        var db = (EditorDbContext)context;
        var image = (PaintImage)entity;

        // A never-saved entity has no rows to remove; its id was only ever an in-memory reference.
        if (!image.IsSaved || image.RecordId is not int id)
        {
            return;
        }

        db.Images.Remove(new PaintImageRecord { Id = id });
        foreach (ImageChunkCoord coord in image.PersistedChunkCoords)
        {
            db.ImageChunks.Remove(new ImageChunkRecord { ImageId = id, ChunkX = coord.X, ChunkY = coord.Y });
        }

        image.IsSaved = false;
    }

    private static PaintImage ToEntity(PaintImageRecord header, IEnumerable<ImageChunkRecord> chunkRows)
    {
        var entity = new PaintImage { RecordId = header.Id, Name = header.Name };
        entity.ConfigureNew(header.Width, header.Height, header.ChunkSize);
        entity.LoadChunks(chunkRows.Select(row =>
            (new ImageChunkCoord(row.ChunkX, row.ChunkY), ImageChunkCodec.Decode(row.Format, row.Pixels, header.ChunkSize))));
        entity.IsSaved = true;
        return entity;
    }
}
