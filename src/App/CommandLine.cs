#nullable enable
using System;
using Godot;

namespace WorldMapStudio;

/// <summary>Reads the user arguments passed after <c>--</c> on the Godot command line, in either
/// <c>--flag value</c> or <c>--flag=value</c> form.</summary>
public static class CommandLine
{
    /// <summary>The value given for <paramref name="flag"/>, or null when it was not passed.</summary>
    public static string? Value(string flag) => Value(OS.GetCmdlineUserArgs(), flag);

    public static string? Value(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(flag + "=", StringComparison.Ordinal))
            {
                return arg[(flag.Length + 1)..];
            }

            if (arg == flag && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
