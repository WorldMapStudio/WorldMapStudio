using System;
using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A rectangular footprint that adds one flat value onto every selected channel's texels inside it —
/// the same footprint as <see cref="ImageComponent"/>, but writing a constant everywhere it
/// overlaps instead of a painted bitmap, and onto as many channels as are selected instead of one.
///
/// Claims nothing itself: like the other deformers in this folder, it only writes into named channels.
/// </summary>
public sealed class TerrainValueComponent : SceneComponent, ISceneBoundsProvider, ITransformPolicy, ILandscapeDeformer
{
    /// <summary>The single source of truth for this component kind's id — <see cref="TerrainValueComponentType"/>
    /// and <see cref="TerrainValueComponentPersistence"/> both reference this instead of restating it.</summary>
    public const string Kind = "landscape-terrain-value";

    private const float BoundsHeight = 2.0f;

    private float _width = 64.0f;
    private float _height = 64.0f;
    private readonly List<string> _channels = [];

    public float Width
    {
        get => _width;
        set => _width = Mathf.Max(0.5f, value);
    }

    public float Height
    {
        get => _height;
        set => _height = Mathf.Max(0.5f, value);
    }

    public float Value { get; set; } = 0.1f;

    /// <summary>Only meaningful for a bound channel that carries more than one component — see
    /// <see cref="Rasterize"/>. Applied the same way to every such channel; a scalar-only channel in
    /// <see cref="Channels"/> uses <see cref="Value"/> instead.</summary>
    public Color ColorValue { get; set; } = Colors.White;

    public IReadOnlyList<string> Channels => _channels;

    public override string TypeId => Kind;

    public override string DisplayName => "Terrain Value";

    public SelfRotation SelfRotation => SelfRotation.HeightOnly;

    public SelfScale SelfScale => SelfScale.None;

    public bool UsesTerrainHeight => true;

    public Aabb LocalBounds => new(
        new Vector3(-Width * 0.5f, -BoundsHeight * 0.5f, -Height * 0.5f),
        new Vector3(Width, BoundsHeight, Height));

    public string DeformerKey => Entity.RecordId is { } id
        ? $"entity:{id}:terrain-value"
        : $"entity:new:{Entity.Id.Value}:terrain-value";

    public Aabb InfluenceBounds => Entity.Transform * LocalBounds;

    public override int ContentVersion
    {
        get
        {
            var hash = new HashCode();
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(Value);
            hash.Add(ColorValue);

            // Order-independent: which channels are selected matters, the order they were toggled
            // in does not, so a plain Combine over the list would invalidate on a no-op reorder.
            int channelsHash = 0;
            foreach (string channel in _channels)
            {
                channelsHash ^= channel.GetHashCode();
            }

            hash.Add(channelsHash);
            return hash.ToHashCode();
        }
    }

    public void ReplaceChannels(IEnumerable<string> channels)
    {
        _channels.Clear();
        _channels.AddRange(channels);
    }

    public override SceneComponent Clone()
    {
        var clone = new TerrainValueComponent
        {
            Width = Width,
            Height = Height,
            Value = Value,
            ColorValue = ColorValue,
        };
        clone.ReplaceChannels(_channels);
        return clone;
    }

    public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context) => [];

    public void Rasterize(in LandscapeRasterContext context)
    {
        if (_channels.Count == 0)
        {
            return;
        }

        Transform3D inverse = Entity.Transform.AffineInverse();
        float halfWidth = Width * 0.5f;
        float halfHeight = Height * 0.5f;

        foreach (string channelName in _channels)
        {
            if (context.Channel(channelName) is not { } channel || context.Writer(channelName) is not { } writer)
            {
                continue;
            }

            int resolution = channel.Resolution;
            bool writeColor = writer.Components > 1;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector3 local = inverse * context.TexelCentre(resolution, x, y);
                    if (Mathf.Abs(local.X) > halfWidth || Mathf.Abs(local.Z) > halfHeight)
                    {
                        continue;
                    }

                    if (writeColor)
                    {
                        Color current = writer.GetColor(x, y);
                        writer.SetColor(x, y, new Color(
                            Mathf.Clamp(current.R + (ColorValue.R * Value), 0.0f, 1.0f),
                            Mathf.Clamp(current.G + (ColorValue.G * Value), 0.0f, 1.0f),
                            Mathf.Clamp(current.B + (ColorValue.B * Value), 0.0f, 1.0f),
                            Mathf.Clamp(current.A + (ColorValue.A * Value), 0.0f, 1.0f)));
                    }
                    else
                    {
                        writer.Set(x, y, Mathf.Clamp(writer.Get(x, y) + Value, 0.0f, 1.0f));
                    }
                }
            }
        }
    }
}
