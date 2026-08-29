using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WorldMapStudio;

/// <summary>
/// Serialized output-&gt;format bindings for a <see cref="ProceduralModel"/>: each declared
/// <see cref="ProceduralOutputSlot"/> may bind an explicit <see cref="IModelFormat"/> id, or defer to
/// <see cref="ProceduralSystem.ResolveFormatId(ProceduralModel, ProceduralOutputSlot)"/>'s fallback when
/// absent or no longer allowed.
/// </summary>
public sealed class ProceduralFormats
{
    private readonly Dictionary<string, string> _byOutput;

    public ProceduralFormats()
        : this(new Dictionary<string, string>(StringComparer.Ordinal))
    {
    }

    private ProceduralFormats(Dictionary<string, string> byOutput)
    {
        _byOutput = byOutput;
    }

    public string? Get(ProceduralOutputSlot slot) => _byOutput.TryGetValue(slot.Name, out string? id) ? id : null;

    public void Set(ProceduralOutputSlot slot, string formatId) => _byOutput[slot.Name] = formatId;

    public void Clear(ProceduralOutputSlot slot) => _byOutput.Remove(slot.Name);

    public string Serialize() => _byOutput.Count == 0 ? "" : JsonSerializer.Serialize(_byOutput);

    public static ProceduralFormats Parse(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return new ProceduralFormats();
        }

        try
        {
            Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized);
            return parsed == null
                ? new ProceduralFormats()
                : new ProceduralFormats(new Dictionary<string, string>(parsed, StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return new ProceduralFormats();
        }
    }
}
