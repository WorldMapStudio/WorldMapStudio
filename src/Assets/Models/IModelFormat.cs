namespace WorldMapStudio;

/// <summary>
/// What kind of model this is, independent of which <see cref="IModelLoader"/> read it (or of there
/// being a file at all — a procedural mesh can declare itself a format and inherit everything here
/// without ever having a loader). Registered under <see cref="ModelFormatSystem"/> via
/// <c>[Subsystem(nameof(ModelFormatSystem))]</c>.
/// </summary>
public interface IModelFormat : ISubsystem
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Informational today; the seam a future file exporter would key off of.</summary>
    string Extension { get; }

    /// <summary>Which <see cref="IMeshMaterialType"/> this format's surfaces render through.</summary>
    string MaterialTypeId { get; }

    /// <summary>How placed instances of this format may rotate about themselves. Default: unrestricted.</summary>
    SelfRotation SelfRotation => SelfRotation.Full;

    /// <summary>How placed instances of this format may be scaled. Default: unrestricted per-axis.</summary>
    SelfScale SelfScale => SelfScale.PerAxis;

    /// <summary>Whether a procedural mesh may declare itself this format. False for import-only formats.</summary>
    bool CanAuthor => false;
}
