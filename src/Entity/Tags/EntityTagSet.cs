using System;
using System.Collections;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// The tags an entity carries, as a sorted set of <see cref="EntityTagDefinition.RecordId"/> values.
/// Immutable, so a command can hold before and after sets without copying and every untagged entity
/// shares the one empty instance — the overwhelming case costs no allocation.
/// </summary>
public readonly struct EntityTagSet : IEquatable<EntityTagSet>, IEnumerable<int>
{
    // Linear below this, binary above: on the handful of tags an entity usually carries a scan beats it.
    private const int BinarySearchThreshold = 8;

    private readonly int[]? _ids;

    private EntityTagSet(int[] sortedDistinct)
    {
        _ids = sortedDistinct;
    }

    public static EntityTagSet Empty => default;

    public ReadOnlySpan<int> Ids => _ids ?? [];

    public int Count => _ids?.Length ?? 0;

    public bool IsEmpty => Count == 0;

    public static EntityTagSet From(IEnumerable<int> ids)
    {
        var sorted = new SortedSet<int>(ids);
        if (sorted.Count == 0)
        {
            return Empty;
        }

        var array = new int[sorted.Count];
        sorted.CopyTo(array);
        return new EntityTagSet(array);
    }

    public bool Contains(int tagId)
    {
        ReadOnlySpan<int> ids = Ids;
        return ids.Length <= BinarySearchThreshold ? ids.IndexOf(tagId) >= 0 : ids.BinarySearch(tagId) >= 0;
    }

    public EntityTagSet With(int tagId)
    {
        ReadOnlySpan<int> ids = Ids;
        int index = ids.BinarySearch(tagId);
        if (index >= 0)
        {
            return this;
        }

        index = ~index;
        var array = new int[ids.Length + 1];
        ids[..index].CopyTo(array);
        array[index] = tagId;
        ids[index..].CopyTo(array.AsSpan(index + 1));
        return new EntityTagSet(array);
    }

    public EntityTagSet Without(int tagId)
    {
        ReadOnlySpan<int> ids = Ids;
        int index = ids.BinarySearch(tagId);
        if (index < 0)
        {
            return this;
        }

        if (ids.Length == 1)
        {
            return Empty;
        }

        var array = new int[ids.Length - 1];
        ids[..index].CopyTo(array);
        ids[(index + 1)..].CopyTo(array.AsSpan(index));
        return new EntityTagSet(array);
    }

    /// <summary>Whether the two sets share at least one tag.</summary>
    public bool Overlaps(EntityTagSet other)
    {
        ReadOnlySpan<int> a = Ids;
        ReadOnlySpan<int> b = other.Ids;
        int i = 0;
        int j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j])
            {
                return true;
            }

            if (a[i] < b[j])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return false;
    }

    public bool Equals(EntityTagSet other) => Ids.SequenceEqual(other.Ids);

    public override bool Equals(object? obj) => obj is EntityTagSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (int id in Ids)
        {
            hash.Add(id);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(EntityTagSet left, EntityTagSet right) => left.Equals(right);

    public static bool operator !=(EntityTagSet left, EntityTagSet right) => !left.Equals(right);

    public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)(_ids ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
