using Godot;

namespace WorldMapStudio;

/// <summary>
/// A named, saved set of scene entity templates. Catalog-backed like <see cref="ProceduralModel"/>, but
/// unlike it, nothing keeps referencing a <see cref="Prefab"/> after it is spawned — spawning clones
/// the template entities (found via <see cref="PrefabTemplateComponent"/>) into fresh, independent
/// entities. See <see cref="PrefabSystem"/> for save/spawn/delete.
/// </summary>
public sealed class Prefab : CatalogEntity, IKeyedCatalogEntity
{
    private string _name = "Prefab";

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    /// <summary>The point of the template that lands on the spawn position: the bottom-centre of the saved
    /// entities' combined bounds.</summary>
    public Vector3 Anchor { get; set; }

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;
}
