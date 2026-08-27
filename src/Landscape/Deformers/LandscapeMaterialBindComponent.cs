using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public sealed record LandscapeMaterialBinding(int? LayerId, int? MaterialId);

public sealed class LandscapeMaterialBindComponent : SceneComponent, ILandscapeDeformer
{
    private readonly List<LandscapeMaterialBinding> _bindings = [];

    public int Priority { get; set; }

    public IReadOnlyList<LandscapeMaterialBinding> Bindings => _bindings;

    public override string TypeId => "landscape-material-bind";

    public override string DisplayName => "Landscape Material Bind";

    public string DeformerKey => Entity.RecordId is { } id
        ? $"entity:{id}:landscape-material-bind"
        : $"entity:new:{Entity.Id.Value}:landscape-material-bind";

    public Aabb InfluenceBounds => Entity.WorldBounds;

    public override int ContentVersion
    {
        get
        {
            var hash = new System.HashCode();
            hash.Add(Priority);
            foreach (LandscapeMaterialBinding binding in _bindings)
            {
                hash.Add(binding.LayerId);
                hash.Add(binding.MaterialId);
            }

            return hash.ToHashCode();
        }
    }

    public void ReplaceBindings(IEnumerable<LandscapeMaterialBinding> bindings)
    {
        _bindings.Clear();
        _bindings.AddRange(bindings);
    }

    public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context)
    {
        var groups = new List<LandscapeClaimGroup>();
        for (int i = 0; i < _bindings.Count; i++)
        {
            LandscapeMaterialBinding binding = _bindings[i];
            if (context.Layer(binding.LayerId) is not { } layer)
            {
                continue;
            }

            groups.Add(new LandscapeClaimGroup
            {
                Key = $"{DeformerKey}:{i}",
                Label = $"{Entity.DisplayName}: {layer.Name}",
                Priority = Priority,
                Claims = [new LandscapeClaim { Layer = layer, Material = context.Material(binding.MaterialId) }],
                Source = Entity.Id,
            });
        }

        return groups;
    }

    public void Rasterize(in LandscapeRasterContext context) { }
}
