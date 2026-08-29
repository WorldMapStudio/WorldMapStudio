namespace WorldMapStudio;

/// <summary>
/// Internal marker tagging a <see cref="SceneEntity"/> as the root of a <see cref="Prefab"/>
/// template. Added programmatically by <see cref="PrefabSystem"/> only — deliberately not
/// registered as an <see cref="ISceneComponentType"/>, so it never appears in the Inspector's "Add
/// component" combo and a user can never add or remove one directly.
/// </summary>
public sealed class PrefabRootComponent : SceneComponent
{
    public int PrefabId { get; set; }

    public override string TypeId => "prefab-root";

    public override string DisplayName => "Prefab Root";

    public override SceneComponent Clone() => new PrefabRootComponent { PrefabId = PrefabId };
}
