using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Owns the loaded <see cref="PaintImage"/> catalog. Unlike <see cref="ProceduralSystem"/>, there is
/// no per-frame <see cref="ISceneNodeComponent"/> to rebuild — an <see cref="ImageComponent"/> is a
/// pure <see cref="ILandscapeDeformer"/> with no representation node — so this is just catalog lookup
/// and usage-counting, no build cache and no <c>Update()</c> loop.
/// </summary>
public sealed class ImageSystem
{
    public ImageSystem(EditorContext context)
    {
        Context = context;
    }

    public EditorContext Context { get; }

    /// <summary>The loaded image catalog. Membership comes from <see cref="EditorContext.Catalog"/>.</summary>
    public IEnumerable<PaintImage> Images => Context.Catalog.OfType<PaintImage>();

    public PaintImage? FindImage(int? id) =>
        id is int value ? Images.FirstOrDefault(image => image.RecordId == value) : null;

    /// <summary>How many loaded scene entities currently reference this image — what the picker and
    /// the images window show so an edit or delete does not surprise the user.</summary>
    public int UsageCount(int imageId) =>
        Context.Scene.Entities.Count(entity => entity.Component<ImageComponent>()?.ImageId == imageId);

    /// <summary>Loads the image catalog whole, replacing what is loaded. Called after the migration
    /// gate, like <see cref="ProceduralSystem.LoadCatalog"/>.</summary>
    public void LoadCatalog() => Context.Database.LoadCatalog<PaintImage>();
}
