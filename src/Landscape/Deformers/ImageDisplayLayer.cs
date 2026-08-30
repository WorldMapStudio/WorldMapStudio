using Godot;

namespace WorldMapStudio;

/// <summary>How an <see cref="ImageComponent"/> bound to a given <see cref="ImageDisplayLayer"/> is
/// previewed/interacted with in the editor viewport. Purely a viewport concern — never feeds
/// <see cref="ImageComponent.ContentVersion"/> or chunk-export dirtiness.</summary>
public enum ImageDisplayMode
{
    /// <summary>No preview — the image paints its landscape channel with nothing extra to see.</summary>
    None,

    /// <summary>A decal on the terrain fading from transparent to <see cref="ImageDisplayLayer.OverlayColor"/>
    /// as the image's pixel values rise.</summary>
    LandscapeOverlay,

    /// <summary>A flat, paintable quad showing the image's texture, which the Paint tool can target
    /// directly instead of projecting through the terrain.</summary>
    Object,
}

/// <summary>
/// A named, saved preview preset an <see cref="ImageComponent"/> placement can opt into. Catalog-backed
/// like <see cref="PaintImage"/>, so many placements can share one display treatment and changing it
/// from any of them (or a window, or a script) updates every placement's viewport representation.
/// </summary>
public sealed class ImageDisplayLayer : CatalogEntity, IKeyedCatalogEntity
{
    private string _name = "Display Layer";
    private ImageDisplayMode _displayMode = ImageDisplayMode.None;
    private Color _overlayColor = new(1.0f, 0.35f, 0.1f, 1.0f);

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            Revision++;
        }
    }

    public ImageDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode == value)
            {
                return;
            }

            _displayMode = value;
            Revision++;
        }
    }

    /// <summary>Only meaningful in <see cref="ImageDisplayMode.LandscapeOverlay"/> — the color the
    /// decal fades toward as an image's pixel values rise from 0.</summary>
    public Color OverlayColor
    {
        get => _overlayColor;
        set
        {
            if (_overlayColor == value)
            {
                return;
            }

            _overlayColor = value;
            Revision++;
        }
    }

    /// <summary>Bumped by every setter. What <see cref="ImageComponent.NeedsRefresh"/> compares
    /// against to notice a display layer changed under it — see <see cref="ImageSystem.Update"/>.</summary>
    public int Revision { get; private set; }

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;
}
