using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A committed-state view of a scene entity for chunk invalidation. It is deliberately captured by
/// edit commands, not by live property setters, so export dirtiness only follows committed saves.
/// </summary>
public sealed record ChunkChangeSnapshot(MapId Map, Aabb Bounds, string Fingerprint)
{
    public static ChunkChangeSnapshot Capture(SceneEntity entity, Transform3D? transform = null, string? fingerprint = null)
    {
        Transform3D usedTransform = transform ?? entity.Transform;
        Aabb bounds = usedTransform * entity.LocalBounds;

        return new ChunkChangeSnapshot(
            entity.Map,
            bounds,
            fingerprint ?? FingerprintEntity(entity, usedTransform, bounds));
    }

    public static string FingerprintBytes(SceneEntity entity, byte[] bytes, params object?[] values)
    {
        var sb = new StringBuilder();
        sb.Append(entity.GetType().FullName).Append('|');
        foreach (object? value in values)
        {
            AppendValue(sb, value);
            sb.Append('|');
        }

        sb.Append(Convert.ToHexString(SHA256.HashData(bytes)));
        return Hash(sb.ToString());
    }

    private static string FingerprintEntity(SceneEntity entity, Transform3D transform, Aabb bounds)
    {
        var sb = new StringBuilder();
        sb.Append(entity.GetType().FullName).Append('|');
        AppendValue(sb, entity.Map.Value);
        AppendTransform(sb, transform);
        AppendAabb(sb, bounds);

        foreach (SceneComponent component in entity.Components.OrderBy(component => component.TypeId, StringComparer.Ordinal))
        {
            sb.Append("component=").Append(component.TypeId).Append('|');
            AppendValue(sb, component.ContentVersion);

            if (component is ISceneBoundsProvider boundsProvider)
            {
                AppendAabb(sb, boundsProvider.LocalBounds);
            }

            if (component is ILandscapeDeformer deformer)
            {
                AppendValue(sb, deformer.DeformerKey);
                AppendAabb(sb, deformer.InfluenceBounds);
            }
        }

        foreach (PropertyInfo property in entity.GetType()
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(p => p.GetIndexParameters().Length == 0)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (!CanFingerprint(property.PropertyType))
            {
                continue;
            }

            sb.Append(property.Name).Append('=');
            AppendValue(sb, property.GetValue(entity));
            sb.Append('|');
        }

        return Hash(sb.ToString());
    }

    private static bool CanFingerprint(Type type)
    {
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual.IsPrimitive ||
               actual.IsEnum ||
               actual == typeof(string) ||
               actual == typeof(decimal) ||
               actual == typeof(EntityId) ||
               actual == typeof(MapId) ||
               actual == typeof(Vector2) ||
               actual == typeof(Vector3) ||
               actual == typeof(Quaternion) ||
               actual == typeof(byte[]);
    }

    private static void AppendTransform(StringBuilder sb, Transform3D transform)
    {
        AppendVector(sb, transform.Basis.X);
        AppendVector(sb, transform.Basis.Y);
        AppendVector(sb, transform.Basis.Z);
        AppendVector(sb, transform.Origin);
    }

    private static void AppendAabb(StringBuilder sb, Aabb bounds)
    {
        AppendVector(sb, bounds.Position);
        AppendVector(sb, bounds.Size);
    }

    private static void AppendVector(StringBuilder sb, Vector3 value)
    {
        AppendValue(sb, value.X);
        AppendValue(sb, value.Y);
        AppendValue(sb, value.Z);
    }

    private static void AppendValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("<null>");
                break;
            case float f:
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                break;
            case double d:
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                sb.Append(Convert.ToHexString(SHA256.HashData(bytes)));
                break;
            default:
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }

        sb.Append(';');
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public readonly record struct ChunkChangeImpact(
    SceneEntity Entity,
    ChunkChangeSnapshot? Before,
    ChunkChangeSnapshot? After);

public interface IChunkChangeCommand : IEditCommand
{
    IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }
}
