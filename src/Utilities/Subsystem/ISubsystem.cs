namespace WorldMapStudio;

public interface ISubsystem
{
    public float Priority { get; }
    public void Update() {}
}
