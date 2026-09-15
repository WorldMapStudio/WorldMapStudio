namespace WorldMapStudio;

public enum StyleSource
{
    BuiltIn,
    Shipped,
    User,
}

/// <summary>A read-only summary of one available style — what the picker combo and <c>wms.style.list()</c> show.</summary>
public sealed class StyleDescriptor
{
    public required string Name { get; init; }
    public required StyleSource Source { get; init; }
    public string? Extends { get; init; }
    public bool ReadOnly => Source != StyleSource.User;
}
