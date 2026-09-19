namespace WorldMapStudio;

public interface ISubsystem
{
    /// <summary>Order within the host's Subsystems list. Does not affect construction order, which
    /// is alphabetical by fully qualified type name.</summary>
    public float Priority => 0f;
}
