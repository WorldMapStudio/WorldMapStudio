using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

public static class AssetSourceId
{
    public const uint MaxLength = 64;

    public static bool IsValid(string id) =>
        Validate(id, [], null) == null;

    public static string? Validate(string id, IEnumerable<AssetSourceSettings> sources, AssetSourceSettings? editing)
    {
        if (id.Length == 0)
        {
            return "Source id is required.";
        }

        if (id.Length > MaxLength)
        {
            return $"Source id must be {MaxLength} characters or fewer.";
        }

        foreach (char c in id)
        {
            if (!IsAllowed(c))
            {
                return "Use only letters, numbers, '-', '_' or '.'.";
            }
        }

        if (sources.Any(source => !ReferenceEquals(source, editing) && source.Id == id))
        {
            return "Another source already uses this id.";
        }

        return null;
    }

    public static string Unique(string prefix, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken);
        if (used.Add(prefix))
        {
            return prefix;
        }

        for (int i = 2; ; i++)
        {
            string candidate = $"{prefix}{i}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool IsAllowed(char c) =>
        c is >= 'a' and <= 'z'
        || c is >= 'A' and <= 'Z'
        || c is >= '0' and <= '9'
        || c is '-' or '_' or '.';
}
