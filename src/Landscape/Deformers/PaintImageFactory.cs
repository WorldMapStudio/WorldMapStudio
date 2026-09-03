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
/// Only currently resident chunks are re-staged on every commit — the "re-persist everything the entity
/// currently holds" shape <see cref="EditorCatalogFactory{TEntity,TRecord}"/> uses for a single row,
/// spread across many rows here, but scoped to residency rather than the whole image: a stored-but-not-
/// resident chunk (evicted, or never loaded on a canvas too large to ever be fully resident) is left
/// alone in storage — see <see cref="PaintImage.RemovedSincePersist"/> for how a genuine deletion is
/// told apart from that.
///
/// <see cref="LoadAllAsync"/> loads every image's chunk coordinate manifest, but only loads chunk pixel
/// data up front for an image small enough that doing so is still cheap; a larger one starts with no
/// resident chunks and relies on <see cref="ImageResidencySystem"/> to bring them in as needed.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class PaintImageFactory : ICatalogEntityFactory
{
    // Above this many resident bytes, an image loads its coordinate manifest only and relies on
    // ImageResidencySystem to bring chunks in as a streamed-in placement needs them, instead of paying
    // for every chunk up front. Below it, an image loads fully and instantly, exactly as it did before
    // residency existed — and stays the common case, since a default-sized image is one chunk.
    private const long EagerLoadBudgetBytes = 64L * 1024 * 1024;

    private readonly EditorStorage _storage;

    public PaintImageFactory(EditorStorage storage)
    {
        _storage = storage;
    }

    public float Priority => 0.0f;

    public Type EntityType => typeof(PaintImage);

    public bool Handles(IEntity entity) => entity is PaintImage;

    public void Configure(ModelBuilder model)
    {
        model.Entity<PaintImageRecord>(entity =>
        {
            entity.ToTable("images");
            entity.HasKey(record => record.Id);

            // The editor assigns catalog ids so entities can reference each other before a commit.
            entity.Property(record => record.Id).ValueGeneratedNever();
        });

        model.Entity<ImageChunkRecord>(entity =>
        {
            entity.ToTable("image_chunks");
            entity.HasKey(record => new { record.ImageId, record.ChunkX, record.ChunkY });
        });
    }

    public async Task<IReadOnlyList<CatalogEntity>> LoadAllAsync()
    {
        await using EditorDbContext context = _storage.CreateContext();
        List<PaintImageRecord> headers = await context.Images.AsNoTracking().ToListAsync().ConfigureAwait(false);

        // Coordinates only — cheap even for an image with a huge number of chunks, since it never
        // touches the Pixels column. Every image needs its manifest regardless of eager/lazy: it is
        // what IsStored (and so residency's reload path) reads.
        ILookup<int, ImageChunkCoord> manifestByImage = (await context.ImageChunks.AsNoTracking()
                .Select(row => new { row.ImageId, row.ChunkX, row.ChunkY })
                .ToListAsync().ConfigureAwait(false))
            .ToLookup(row => row.ImageId, row => new ImageChunkCoord(row.ChunkX, row.ChunkY));

        var entities = new List<PaintImage>();
        var eagerImageIds = new List<int>();
        foreach (PaintImageRecord header in headers)
        {
            var entity = new PaintImage { RecordId = header.Id, Name = header.Name };
            entity.ConfigureNew(header.Width, header.Height, header.ChunkSize, header.Components, (PaintImagePixelFormat)header.PixelFormat);

            List<ImageChunkCoord> coords = manifestByImage[header.Id].ToList();
            entity.LoadManifest(coords);
            entity.IsSaved = true;
            entities.Add(entity);

            if ((long)coords.Count * entity.ChunkByteSize <= EagerLoadBudgetBytes)
            {
                eagerImageIds.Add(header.Id);
            }
        }

        if (eagerImageIds.Count > 0)
        {
            List<ImageChunkRecord> blobs = await context.ImageChunks.AsNoTracking()
                .Where(row => eagerImageIds.Contains(row.ImageId))
                .ToListAsync().ConfigureAwait(false);
            ILookup<int, ImageChunkRecord> blobsByImage = blobs.ToLookup(row => row.ImageId);

            foreach (PaintImage entity in entities)
            {
                int imageId = entity.RecordId ?? 0;
                if (!eagerImageIds.Contains(imageId))
                {
                    continue;
                }

                entity.LoadChunks(blobsByImage[imageId].Select(row =>
                    (new ImageChunkCoord(row.ChunkX, row.ChunkY), ImageChunkCodec.Decode(row.Format, row.Pixels, entity.ChunkSize, entity.Stride))));
            }
        }

        return entities;
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
            Components = image.Components,
            PixelFormat = (int)image.Format,
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

        // Deletes are driven by RemovedSincePersist — coordinates an edit explicitly emptied out —
        // never by "absent from ChunkCoords" on its own: a chunk ImageResidencySystem merely evicted
        // is also absent from ChunkCoords, and deleting its (perfectly intact) row would be data loss
        // the user never asked for. Intersected with previouslyPersisted so a removal that was never
        // actually saved does not stage a pointless delete.
        var toDelete = new HashSet<ImageChunkCoord>(image.RemovedSincePersist.Where(previouslyPersisted.Contains));
        foreach (ImageChunkCoord coord in toDelete)
        {
            db.ImageChunks.Remove(new ImageChunkRecord { ImageId = imageId, ChunkX = coord.X, ChunkY = coord.Y });
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

            // Reconciles the manifest incrementally (current joins it, toDelete leaves it) rather than
            // overwriting it with `current` — a stored-but-not-resident chunk this commit never
            // touched (because it was evicted, or simply because this image's canvas is too large to
            // ever be fully resident) must stay recorded as persisted, or the next commit would try to
            // INSERT a row that already exists.
            image.CommitChunkPersistence(current, toDelete);
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
}
