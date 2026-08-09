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
        IReadOnlyList<LandscapeTextureMaterial> materials)
    {
        Channels = channels;
        Layers = layers;
        Materials = materials;
    }

    public IReadOnlyList<LandscapeChannel> Channels { get; }

    public IReadOnlyList<LandscapeLayer> Layers { get; }

    public IReadOnlyList<LandscapeTextureMaterial> Materials { get; }

    /// <summary>Texture layers in the order they composite, base first.</summary>
    public IEnumerable<LandscapeLayer> TextureLayersInOrder =>
        Layers.Where(layer => layer.UsesTextureSlot).OrderBy(layer => layer.DrawOrder);

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

        foreach (LandscapeLayer layer in Layers.Where(layer => layer.IsBase && !layer.UsesTextureSlot))
        {
            issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                $"Layer '{layer.Name}' is a height layer flagged as base; only texture layers can be a base."));
        }

        // A base layer is the opaque bottom of the chunk, so anything compositing below one would be
        // painted over and silently lost.
        List<LandscapeLayer> textureLayers = TextureLayersInOrder.ToList();
        int lastBase = textureLayers.FindLastIndex(layer => layer.IsBase);
        if (lastBase >= 0)
        {
            foreach (LandscapeLayer covered in textureLayers.Take(lastBase).Where(layer => !layer.IsBase))
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                    $"Layer '{covered.Name}' draws below the base layer '{textureLayers[lastBase].Name}' and would never be visible."));
            }
        }
        else if (textureLayers.Count > 0)
        {
            issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Warning,
                "No layer is marked as a base, so every chunk falls back to the map's fallback material."));
        }
    }

    private void ValidateMaterials(List<LandscapeIssue> issues)
    {
        CheckNames(issues, Materials.Select(material => material.Name), "material");

        foreach (LandscapeTextureMaterial material in Materials)
        {
            if (material.AlphaFunction.Length == 0)
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Error,
                    $"Material '{material.Name}' has no alpha function, so nothing decides where it shows."));
            }

            if (material.TexturePath.Length == 0)
            {
                issues.Add(new LandscapeIssue(LandscapeIssueSeverity.Warning,
                    $"Material '{material.Name}' has no texture assigned."));
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
