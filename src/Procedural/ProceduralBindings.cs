using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WorldMapStudio;

/// <summary>
/// Serialized (output, slot)-&gt;material bindings for a <see cref="ProceduralComponent"/>'s bound
/// model: each <see cref="MeshMaterialSlot"/> declared by an output is bound to either a saved
/// <see cref="MeshMaterialPreset"/> (by id) or an inline <see cref="MeshParameterValues"/> bag
/// authored just for this model. Keyed by output name so two outputs may declare identically-named
/// slots (e.g. two "surface" slots in two different formats) without colliding.
/// </summary>
public sealed class ProceduralBindings
{
    private sealed class Entry
    {
        public int? Preset { get; set; }
        public Dictionary<string, string>? Values { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries;

    public ProceduralBindings()
        : this(new Dictionary<string, Entry>(StringComparer.Ordinal))
    {
    }

    private ProceduralBindings(Dictionary<string, Entry> entries)
    {
        _entries = entries;
    }

    private static string Key(ProceduralOutputSlot output, MeshMaterialSlot slot) => $"{output.Name}/{slot.Name}";

    public int? GetPresetId(ProceduralOutputSlot output, MeshMaterialSlot slot) =>
        _entries.TryGetValue(Key(output, slot), out Entry? entry) ? entry.Preset : null;

    public MeshParameterValues? GetInlineValues(ProceduralOutputSlot output, MeshMaterialSlot slot) =>
        _entries.TryGetValue(Key(output, slot), out Entry? entry) && entry.Values != null
            ? MeshParameterValues.Parse(JsonSerializer.Serialize(entry.Values))
            : null;

    public void BindPreset(ProceduralOutputSlot output, MeshMaterialSlot slot, int presetId) =>
        _entries[Key(output, slot)] = new Entry { Preset = presetId };

    public void BindInline(ProceduralOutputSlot output, MeshMaterialSlot slot, MeshParameterValues values) =>
        _entries[Key(output, slot)] = new Entry { Values = new Dictionary<string, string>(values.Raw, StringComparer.Ordinal) };

    public void Clear(ProceduralOutputSlot output, MeshMaterialSlot slot) => _entries.Remove(Key(output, slot));

    public string Serialize() => _entries.Count == 0 ? "" : JsonSerializer.Serialize(_entries);

    public static ProceduralBindings Parse(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return new ProceduralBindings();
        }

        try
        {
            Dictionary<string, Entry>? parsed = JsonSerializer.Deserialize<Dictionary<string, Entry>>(serialized);
            return parsed == null
                ? new ProceduralBindings()
                : new ProceduralBindings(new Dictionary<string, Entry>(parsed, StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return new ProceduralBindings();
        }
    }
}
