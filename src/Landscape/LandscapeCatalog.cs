using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>How badly a catalog issue breaks things.</summary>
public enum LandscapeIssueSeverity
{
    /// <summary>Works, but is probably not what the user meant.</summary>
    Warning,

    /// <summary>The catalog cannot be resolved into chunks while this stands.</summary>
    Error,
}

/// <summary>One thing wrong with the authored catalog, shown while editing it.</summary>
public readonly record struct LandscapeIssue(LandscapeIssueSeverity Severity, string Message)
{
    public override string ToString() => $"{Severity}: {Message}";
}

/// <summary>
/// A read-only view over the loaded landscape catalog, and the rules it has to satisfy before the
/// resolver can use it. Validation lives here rather than in the editing window so the builder can
/// refuse a broken catalog outright instead of producing nonsense chunks from it.
/// </summary>
public sealed class LandscapeCatalog
{
    public LandscapeCatalog(
        IReadOnlyList<LandscapeChannel> channels,
        IReadOnlyList<LandscapeLayer> layers,
        IReadOnlyList<LandscapeMaterial> materials,
        LandscapeFunctions? functions = null,
        IReadOnlyList<TerrainAttribute>? attributes = null,
        IReadOnlyList<LandscapeMaterialAttributeWrite>? attributeWrites = null)
    {
        Channels = channels;
        Layers = layers;
        Materials = materials;
        Functions = functions;
        Attributes = attributes ?? [];
        AttributeWrites = attributeWrites ?? [];

        foreach (LandscapeMaterialAttributeWrite write in AttributeWrites)
        {
            if (!_writesByMaterial.TryGetValue(write.MaterialId, out List<LandscapeMaterialAttributeWrite>? list))
            {
                list = [];
                _writesByMaterial[write.MaterialId] = list;
            }

            list.Add(write);
        }
    }

    private readonly Dictionary<int, List<LandscapeMaterialAttributeWrite>> _writesByMaterial = [];

    public IReadOnlyList<LandscapeChannel> Channels { get; }

    public IReadOnlyList<LandscapeLayer> Layers { get; }

    public IReadOnlyList<LandscapeMaterial> Materials { get; }

    /// <summary>The terrain attributes this map declares — the open-ended output kinds a material can
    /// write, an exporter reads and the debug overlay renders. Sits with <see cref="Channels"/> /
    /// <see cref="Layers"/> / <see cref="Materials"/>; declares no storage of its own.</summary>
    public IReadOnlyList<TerrainAttribute> Attributes { get; }

    /// <summary>Every material's terrain-attribute writes, flat. Use <see cref="AttributeWritesOf"/> to
    /// get one material's, in the order they were authored.</summary>
    public IReadOnlyList<LandscapeMaterialAttributeWrite> AttributeWrites { get; }

    /// <summary>The attribute writes declared on <paramref name="material"/>, or empty.</summary>
    public IReadOnlyList<LandscapeMaterialAttributeWrite> AttributeWritesOf(LandscapeMaterial material) =>
        material.RecordId is { } id && _writesByMaterial.TryGetValue(id, out List<LandscapeMaterialAttributeWrite>? list)
            ? list
            : [];

    /// <summary>Whether <paramref name="material"/> writes any terrain attribute — the resolver routes
    /// on this, matching <c>DeformsHeight</c> / <c>CutsHole</c> / <c>PaintsVertexColor</c>.</summary>
    public bool WritesAttributes(LandscapeMaterial material) => AttributeWritesOf(material).Count > 0;

    /// <summary>The function registry material bindings resolve against, or null to skip those checks.</summary>
    public LandscapeFunctions? Functions { get; }

    /// <summary>
    /// Changes whenever anything in the catalog that affects a build changes.
    ///
    /// Registry membership is not enough to notice an edit: changing a material's height amount or a
    /// layer's draw order mutates an object in place, so nothing about the collection moves and a
    /// chunk would happily keep whatever it was built with. Computed rather than cached, because the
    /// catalog holds live entities that are edited underneath it.
    /// </summary>
    public int ContentVersion
    {
        get
        {
            var hash = new System.HashCode();

            foreach (LandscapeChannel channel in Channels)
            {
                // The name is part of it: channels are bound by name, so renaming one rebinds it.
                hash.Add(channel.Name);
                hash.Add(channel.Resolution);
                hash.Add(channel.BitDepth);
                hash.Add(channel.Components);
            }

            foreach (TerrainAttribute attribute in Attributes)
            {
                // The key is part of it: writes bind by key, so renaming one rebinds it.
                hash.Add(attribute.Key);
                hash.Add(attribute.CellsPerChunkEdge);
                hash.Add(attribute.Components);
                hash.Add(attribute.ElementWidth);
                hash.Add(attribute.Kind);
                hash.Add(attribute.DefaultValue);
            }

            foreach (LandscapeMaterialAttributeWrite write in AttributeWrites)
            {
                hash.Add(write.MaterialId);
                hash.Add(write.Attribute);
                hash.Add(write.Function);
                hash.Add(write.Parameters);
            }

            foreach (LandscapeLayer layer in Layers)
            {
                hash.Add(layer.RecordId);
                hash.Add(layer.IsBase);
                hash.Add(layer.Priority);
                hash.Add(layer.DrawOrder);
            }

            foreach (LandscapeMaterial material in Materials)
            {
                hash.Add(material.RecordId);
                hash.Add(material.TexturePath);
                hash.Add(material.AlphaFunction);
                hash.Add(material.AlphaParameters);
                hash.Add(material.HeightFunction);
                hash.Add(material.HeightParameters);
                hash.Add(material.HoleFunction);
                hash.Add(material.HoleParameters);
                hash.Add(material.VertexColorFunction);
                hash.Add(material.VertexColorParameters);
                hash.Add(material.VertexLightFunction);
                hash.Add(material.VertexLightParameters);
                hash.Add(material.SurfaceEffectId);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// The largest distance any bound function samples outside its chunk. This is what sizes the
    /// builder's halo and bounds the dirty set when an entity changes.
    /// </summary>
    public float MaxSampleRadius
    {
        get
        {
            float radius = 0.0f;
            foreach (LandscapeMaterial material in Materials)
            {
                foreach (ILandscapeFunction function in BoundFunctions(material))
                {
                    radius = System.Math.Max(radius, function.MaxSampleRadius);
                }
            }

            return radius;
        }
    }

    /// <summary>The functions a material binds, skipping ids nothing currently provides.</summary>
    public IEnumerable<ILandscapeFunction> BoundFunctions(LandscapeMaterial material)
    {
        if (Functions == null)
        {
            yield break;
        }

        if (Functions.Find(material.AlphaFunction) is { } alpha)
        {
            yield return alpha;
        }

        if (Functions.Find(material.HeightFunction) is { } height)
        {
            yield return height;
        }

        if (Functions.Find(material.HoleFunction) is { } hole)
        {
            yield return hole;
        }

        if (Functions.Find(material.VertexColorFunction) is { } vertexColor)
        {
            yield return vertexColor;
        }

        if (Functions.Find(material.VertexLightFunction) is { } vertexLight)
        {
            yield return vertexLight;
        }
    }

    /// <summary>Layers in draw order — compositing order for whatever paints, evaluation order for
    /// whatever deforms.</summary>
    public IEnumerable<LandscapeLayer> LayersInOrder => Layers.OrderBy(layer => layer.DrawOrder);

    /// <summary>Everything wrong with the catalog, worst first. Empty means it is usable.</summary>
    public IReadOnlyList<LandscapeIssue> Validate()
    {
        var issues = new List<LandscapeIssue>();

        ValidateChannels(issues);
        ValidateLayers(issues);
        ValidateMaterials(issues);

        return issues.OrderByDescending(issue => issue.Severity).ToList();
    }

    private void ValidateChannels(List<LandscapeIssue> issues)
    {
        CheckNames(issues, Channels.Select(channel => channel.Name), "channel");

        foreach (LandscapeChannel channel in Channels)
        {
            // A channel binding serializes as "name:swizzle" (see LandscapeChannelBinding) — a colon
            // in the name itself would make that unparseable.
            if (channel.Name.Contains(':'))
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                    $"Channel '{channel.Name}' has a ':' in its name, which a channel binding uses as a separator."));
            }

            if (channel.Resolution < 1)
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                    $"Channel '{channel.Name}' needs a resolution of at least 1."));
            }

            if (channel.BitDepth is not (8 or 16 or 32))
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                    $"Channel '{channel.Name}' has bit depth {channel.BitDepth}; expected 8, 16 or 32."));
            }

            if (channel.Components is not (1 or 3 or 4))
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                    $"Channel '{channel.Name}' has {channel.Components} components; expected 1 (scalar), 3 (RGB) or 4 (RGBA)."));
            }
        }
    }

    private void ValidateLayers(List<LandscapeIssue> issues)
    {
        CheckNames(issues, Layers.Select(layer => layer.Name), "layer");

        // Unique draw orders are what make "adjacent in draw order" well-defined, which the resolver's
        // merge step depends on: two layers sharing an order have no defined compositing sequence.
        foreach (IGrouping<int, LandscapeLayer> group in Layers.GroupBy(layer => layer.DrawOrder).Where(g => g.Count() > 1))
        {
            issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                $"Draw order {group.Key} is used by {string.Join(", ", group.Select(layer => $"'{layer.Name}'"))}; orders must be unique."));
        }

        // Whether a layer composites at all depends on the material bound to it, per entity and per
        // chunk, so "sorts below the base" is a build-time problem rather than a catalog rule.
        if (Layers.Count > 0 && Layers.All(layer => !layer.IsBase))
        {
            issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Warning,
                "No layer is marked as a base, so every chunk depends on the map having a fallback material."));
        }
    }

    private void ValidateMaterials(List<LandscapeIssue> issues)
    {
        CheckNames(issues, Materials.Select(material => material.Name), "material");

        foreach (LandscapeMaterial material in Materials)
        {
            // Which half a material needs depends on the layer it is bound to, and that binding is
            // made per entity and per chunk — so the only thing checkable here is that it does
            // something at all. The builder reports a material missing the half its layer needed.
            if (!material.PaintsTexture && !material.DeformsHeight && !material.CutsHole &&
                !material.PaintsVertexColor && !material.PaintsVertexLight)
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Warning,
                    $"Material '{material.Name}' has no alpha, height, hole, vertex color or vertex light function, so it does nothing."));
            }

            if (material.PaintsTexture && material.TexturePath.Length == 0)
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Warning,
                    $"Material '{material.Name}' paints a texture layer but has no texture assigned."));
            }

            ValidateBinding(issues, material, material.AlphaFunction, material.AlphaParameters, "alpha");
            ValidateBinding(issues, material, material.HeightFunction, material.HeightParameters, "height");
            ValidateBinding(issues, material, material.HoleFunction, material.HoleParameters, "hole");
            ValidateBinding(issues, material, material.VertexColorFunction, material.VertexColorParameters, "vertexcolor");
            ValidateBinding(issues, material, material.VertexLightFunction, material.VertexLightParameters, "vertexlight");
        }
    }

    // A material names its functions by id and its channels by name, so both can dangle: a function
    // whose plugin is not loaded, or a channel that was renamed or deleted out from under it.
    private void ValidateBinding(
        List<LandscapeIssue> issues,
        LandscapeMaterial material,
        string functionId,
        string serializedValues,
        string role)
    {
        if (Functions == null || functionId.Length == 0)
        {
            return;
        }

        ILandscapeFunction? function = Functions.Find(functionId);
        if (function == null)
        {
            issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                $"Material '{material.Name}' binds {role} function '{functionId}', which nothing provides."));
            return;
        }

        LandscapeParameterValues values = LandscapeParameterValues.Parse(serializedValues);
        foreach (LandscapeParameter parameter in function.Parameters.Where(p => p.Kind == LandscapeParameterKind.Channel))
        {
            LandscapeChannelBinding binding = values.GetChannelBinding(parameter);
            if (binding.IsEmpty)
            {
                if (!parameter.Optional)
                {
                    issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                        $"Material '{material.Name}' leaves {role} channel '{parameter.DisplayName}' unbound."));
                }

                continue;
            }

            if (Channels.FirstOrDefault(existing => existing.Name == binding.Channel) is not { } bound)
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                    $"Material '{material.Name}' binds {role} channel '{parameter.DisplayName}' to '{binding.Channel}', which does not exist."));
                continue;
            }

            // A swizzle can always bridge a scalar parameter to a color channel or vice versa (see
            // LandscapeChannelBinding), so this is never wrong — just possibly not what the author
            // meant, e.g. picking "rgb" for a parameter that only ever reads one number back out.
            if (parameter.ChannelFormat == LandscapeChannelFormat.Scalar &&
                binding.Swizzle is LandscapeSwizzle.Rgb or LandscapeSwizzle.Rgba)
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Warning,
                    $"Material '{material.Name}' binds {role} channel '{parameter.DisplayName}' with swizzle " +
                    $"'{LandscapeChannelBinding.SuffixOf(binding.Swizzle)}', but the parameter only reads a single " +
                    "value — it will see luminance instead."));
            }

            if (bound.Components == 1 && binding.Swizzle is LandscapeSwizzle.G or LandscapeSwizzle.B or LandscapeSwizzle.A)
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Warning,
                    $"Material '{material.Name}' binds {role} channel '{parameter.DisplayName}' to '{binding.Channel}' " +
                    $"with swizzle '{LandscapeChannelBinding.SuffixOf(binding.Swizzle)}', but that channel is scalar " +
                    "and has no such component."));
            }
        }
    }

    private static void CheckNames(List<LandscapeIssue> issues, IEnumerable<string> names, string kind)
    {
        var seen = new List<string>();
        foreach (string name in names)
        {
            if (name.Trim().Length == 0)
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error, $"A {kind} has no name."));
                continue;
            }

            // Names are how entities and materials refer to catalog items in the inspector, so a
            // duplicate makes a reference ambiguous to the user even where the id is not.
            if (seen.Contains(name))
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error, $"Two {kind}s are named '{name}'."));
            }
            else
            {
                seen.Add(name);
            }
        }
    }
}
