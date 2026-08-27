using Godot;

namespace WorldMapStudio;

public abstract class SceneComponent
{
    public SceneEntity? Owner { get; internal set; }

    public abstract string TypeId { get; }

    public abstract string DisplayName { get; }

    public virtual int ContentVersion => 0;

    protected SceneEntity Entity => Owner ?? throw new System.InvalidOperationException("Component is not attached to an entity.");
}

public interface ISceneBoundsProvider
{
    Aabb LocalBounds { get; }
}

public interface ISceneNodeComponent
{
    Node3D? BuildNode();
}

public interface ITransformPolicy
{
    SelfRotation SelfRotation { get; }

    bool UsesTerrainHeight { get; }
}

public interface IDrawingTargetComponent
{
    int Width { get; }

    int Height { get; }

    float WorldSizeX { get; }

    float WorldSizeZ { get; }

    System.ReadOnlySpan<byte> Pixels { get; }

    bool Paint(Vector3 local, float radius, float opacity, bool erase);

    byte[] CopyPixels();

    void ReplacePixels(byte[] pixels);

    void Resize(int width, int height);

    void LoadPixels(int width, int height, byte[] pixels);
}
