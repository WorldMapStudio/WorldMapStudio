using System.Collections.Generic;
using System.Linq;
using Godot;

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

/// <summary>
/// Marks cells of a chunk as holes — cut out of the mesh entirely. Unlike height, this is
/// <b>write-only and order-independent</b>: an implementation may only set entries of
/// <paramref name="holes"/> to <see langword="true"/>, never clear one another material set. A hole,
/// once claimed by any surviving material, cannot be un-claimed by a different one — there is no
/// "flatten" equivalent for holes. The builder relies on this to accumulate every claim into one
/// shared buffer rather than resolving conflicts between them.
/// </summary>
public interface ILandscapeHoleFunction : ILandscapeFunction
{
    /// <summary>
    /// Sets entries of <paramref name="holes"/> to <see langword="true"/> for cells this claim wants
    /// cut out — row-major, <see cref="LandscapeEvalContext.Resolution"/> squared. Must never set an
    /// entry to <see langword="false"/>; the buffer may already carry <see langword="true"/> entries
    /// from another surviving claim.
    /// </summary>
    void Evaluate(in LandscapeEvalContext context, bool[] holes);
}

/// <summary>
/// Transforms a chunk's accumulated vertex color, sampled at the same grid as the height vertices.
/// Starts at white each build, so a chunk nothing binds this on renders unchanged — consumed in the
/// shader as a multiplier over the splatted albedo. Not restricted to a single blend mode: a function
/// receives what earlier layers built and rewrites it, the same "accumulate and transform" shape as
/// <see cref="ILandscapeHeightFunction"/>, so tint, replace and fade are all ordinary implementations.
/// </summary>
public interface ILandscapeVertexColorFunction : ILandscapeFunction
{
    /// <summary>
    /// Rewrites <paramref name="colors"/> in place — row-major, <see cref="LandscapeEvalContext.Resolution"/>
    /// squared, holding what earlier layers built. Draw order, never entity scan order.
    /// </summary>
    void Evaluate(in LandscapeEvalContext context, Color[] colors);
}

/// <summary>
/// Transforms a chunk's accumulated vertex light, sampled at the same grid as the height vertices.
/// Starts at black each build, so a chunk nothing binds this on adds nothing. Vertex light is additive
/// <b>linear</b> light reaching the surface, in the same units as the scene's ambient and direct light
/// — 1.0 is roughly one unit of full sunlight. It is modulated by the surface albedo and is not
/// attenuated by the scene's own lighting, so it stays visible at night. The common case is adding a
/// scaled color, but like height this is not restricted to addition.
/// </summary>
public interface ILandscapeVertexLightFunction : ILandscapeFunction
{
    /// <summary>
    /// Rewrites <paramref name="light"/> in place — row-major, <see cref="LandscapeEvalContext.Resolution"/>
    /// squared, holding what earlier layers built. Draw order, never entity scan order.
    /// </summary>
    void Evaluate(in LandscapeEvalContext context, Color[] light);
}

/// <summary>
/// Transforms a chunk's accumulated values for one terrain attribute. Accumulate-and-transform like
/// height, not write-only like holes: a function receives what earlier layers wrote and rewrites it,
/// so "set where covered", "set bits" and "clear bits" are all ordinary. Order is the layer's draw
/// order.
///
/// Writes go through <see cref="TerrainAttributeWriter"/> rather than a raw array so a function
/// declared against a scalar keeps working against a 3- or 4-component attribute — the swizzle on the
/// material's write decides which components a scalar write lands on. Chunk-local: the writer's cells
/// never blend across a chunk edge, and any channel a function reads is sampled with
/// <see cref="LandscapeEvalContext.ReadChannel"/> (nearest texel), never the bilinear
/// <see cref="LandscapeEvalContext.SampleChannel"/> — filtering an id turns it into a different id.
/// </summary>
public interface ILandscapeAttributeFunction : ILandscapeFunction
{
    void Evaluate(in LandscapeEvalContext context, in TerrainAttributeWriter cells);
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
