namespace WorldMapStudio;

/// <summary>
/// Internal marker tagging a <see cref="SceneEntity"/> as one of the entities of a <see cref="Prefab"/>
/// template. Added programmatically by <see cref="PrefabSystem"/> only — deliberately not
/// registered as an <see cref="ISceneComponentType"/>, so it never appears in the Inspector's "Add
/// component" combo and a user can never add or remove one directly.
/// </summary>
public sealed class PrefabTemplateComponent : SceneComponent
{
    /// <summary>The single source of truth for this component kind's id — <see cref="PrefabTemplateComponentPersistence"/>
    /// references this instead of restating it.</summary>
    public const string Kind = "prefab-template";

    public int PrefabId { get; set; }

    public override string TypeId => Kind;

    public override string DisplayName => "Prefab Template";

    public override SceneComponent Clone() => new PrefabTemplateComponent { PrefabId = PrefabId };
}
