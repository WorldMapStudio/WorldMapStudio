namespace WorldMapStudio;

/// <summary>What kind of self-rotation a <see cref="SceneEntity"/> supports.</summary>
public enum SelfRotation
{
    /// <summary>Cannot rotate; rotating the selection only moves it.</summary>
    None,

    /// <summary>Only yaw about the world's vertical axis.</summary>
    HeightOnly,

    /// <summary>Free rotation on all axes.</summary>
    Full,
}
