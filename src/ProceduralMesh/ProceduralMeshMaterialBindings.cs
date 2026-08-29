using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WorldMapStudio;

/// <summary>
/// Serialized slot-&gt;material bindings for a <see cref="ProceduralMeshComponent"/>: each
/// <see cref="MeshMaterialSlot"/> is bound to either a saved <see cref="MeshMaterialPreset"/> (by id)
/// or an inline <see cref="MeshParameterValues"/> bag authored just for this component.
/// </summary>
public sealed class ProceduralMeshMaterialBindings
{
    private sealed class Entry
    {
        public int? Preset { get; set; }
        public Dictionary<string, string>? Values { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries;

    public ProceduralMeshMaterialBindings()
        : this(new Dictionary<string, Entry>(StringComparer.Ordinal))
    {
    }

    private ProceduralMeshMaterialBindings(Dictionary<string, Entry> entries)
    {
        _entries = entries;
    }

    public int? GetPresetId(MeshMaterialSlot slot) =>
        _entries.TryGetValue(slot.Name, out Entry? entry) ? entry.Preset : null;

    public MeshParameterValues? GetInlineValues(MeshMaterialSlot slot) =>
        _entries.TryGetValue(slot.Name, out Entry? entry) && entry.Values != null
            ? MeshParameterValues.Parse(JsonSerializer.Serialize(entry.Values))
            : null;

    public void BindPreset(MeshMaterialSlot slot, int presetId) =>
        _entries[slot.Name] = new Entry { Preset = presetId };

    public void BindInline(MeshMaterialSlot slot, MeshParameterValues values) =>
        _entries[slot.Name] = new Entry { Values = new Dictionary<string, string>(values.Raw, StringComparer.Ordinal) };

    public void Clear(MeshMaterialSlot slot) => _entries.Remove(slot.Name);

    public string Serialize() => _entries.Count == 0 ? "" : JsonSerializer.Serialize(_entries);

    public static ProceduralMeshMaterialBindings Parse(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return new ProceduralMeshMaterialBindings();
        }

        try
        {
            Dictionary<string, Entry>? parsed = JsonSerializer.Deserialize<Dictionary<string, Entry>>(serialized);
            return parsed == null
                ? new ProceduralMeshMaterialBindings()
                : new ProceduralMeshMaterialBindings(new Dictionary<string, Entry>(parsed, StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return new ProceduralMeshMaterialBindings();
        }
    }
}
