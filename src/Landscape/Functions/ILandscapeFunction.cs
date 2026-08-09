using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// A compiled landscape function: the code half of a material, turning channel data into an alpha
/// layer or into height. Functions are discovered by reflection at startup rather than registered
/// with <c>[Subsystem]</c>, so they can later live in an assembly that is swapped at runtime.
///
/// Functions are <b>stateless</b>. Everything a call depends on arrives as arguments — parameter
/// values from the material, channels from the builder — because chunk builds run in parallel and a
/// single instance serves all of them.
///
/// Evaluation itself arrives with the builder (Phase 6). What is declared here is the contract the
/// builder and the editor need first: what the function reads, what it takes, how far it reaches,
/// and which version of it produced a given result.
/// </summary>
public interface ILandscapeFunction
{
    /// <summary>
    /// Stable identifier stored on materials. Changing it orphans every material bound to it, so it
    /// is deliberately not the type name.
    /// </summary>
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    /// <summary>
    /// Bumped whenever the implementation changes in a way that alters its output. Part of a chunk's
    /// dependency record, so a rebuild can tell that the code — not the data — moved underneath it.
    /// </summary>
    int Version { get; }

    /// <summary>
    /// How far outside the chunk this function samples, in world units. Zero means chunk-local.
    /// This is what bounds the builder's halo and the dirty set: without a declared limit, an entity
    /// edit would invalidate the whole map.
    /// </summary>
    float MaxSampleRadius { get; }

    /// <summary>What the material must supply, including which channels to read and write.</summary>
    IReadOnlyList<LandscapeParameter> Parameters { get; }
}

/// <summary>Produces one texture layer's alpha for a chunk from the channels a material points it at.</summary>
public interface ILandscapeAlphaFunction : ILandscapeFunction
{
    /// <summary>
    /// Writes coverage in 0..1 into <paramref name="alpha"/>, which is
    /// <see cref="LandscapeEvalContext.Resolution"/> squared and row-major. Runs during output
    /// evaluation, so channels — including neighbouring chunks' — are final and may be sampled freely.
    /// </summary>
    void Evaluate(in LandscapeEvalContext context, float[] alpha);
}

/// <summary>
/// Transforms a chunk's accumulated height. Not restricted to adding: a function receives the height
/// built so far and rewrites it, so flatten, max and blend are ordinary implementations. Order is the
/// layer's draw order, never entity scan order.
/// </summary>
public interface ILandscapeHeightFunction : ILandscapeFunction
{
    /// <summary>
    /// Rewrites <paramref name="heights"/> in place — world units, row-major,
    /// <see cref="LandscapeEvalContext.Resolution"/> squared, holding what earlier layers built.
    /// </summary>
    void Evaluate(in LandscapeEvalContext context, float[] heights);
}

/// <summary>Shared helpers over a function's declared parameters.</summary>
public static class LandscapeFunctionExtensions
{
    public static LandscapeParameter? Parameter(this ILandscapeFunction function, string name) =>
        function.Parameters.FirstOrDefault(parameter => parameter.Name == name);

    /// <summary>The channel parameters this function reads from.</summary>
    public static IEnumerable<LandscapeParameter> ReadChannels(this ILandscapeFunction function) =>
        function.Parameters.Where(p => p.Kind == LandscapeParameterKind.Channel && p.Access == LandscapeChannelAccess.Read);

    /// <summary>The channel parameters this function writes to.</summary>
    public static IEnumerable<LandscapeParameter> WriteChannels(this ILandscapeFunction function) =>
        function.Parameters.Where(p => p.Kind == LandscapeParameterKind.Channel && p.Access == LandscapeChannelAccess.Write);
}
