namespace WorldMapStudio;

/// <summary>
/// A named, saved scene entity template. Catalog-backed like <see cref="ProceduralModel"/>, but
/// unlike it, nothing keeps referencing a <see cref="Prefab"/> after it is spawned — spawning clones
/// the template's <see cref="SceneEntity"/> subtree (found via <see cref="PrefabRootComponent"/>)
/// into fresh, independent entities. See <see cref="PrefabSystem"/> for save/spawn/delete.
/// </summary>
public sealed class Prefab : CatalogEntity, IKeyedCatalogEntity
{
    private string _name = "Prefab";

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;
}
