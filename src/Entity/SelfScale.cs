namespace WorldMapStudio;

/// <summary>What kind of self-scaling a <see cref="SceneEntity"/> supports.</summary>
public enum SelfScale
{
    /// <summary>Cannot be scaled; always 1:1:1.</summary>
    None,

    /// <summary>Scales uniformly across all three axes together.</summary>
    Uniform,

    /// <summary>Free scaling per axis.</summary>
    PerAxis,
}
