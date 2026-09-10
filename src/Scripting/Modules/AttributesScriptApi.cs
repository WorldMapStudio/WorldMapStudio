using System;
using System.Linq;

namespace WorldMapStudio;

/// <summary>A read-only snapshot of one terrain attribute's declaration, safe to hand to JS.</summary>
public sealed class AttributeDescriptor
{
    public AttributeDescriptor(TerrainAttribute attribute)
    {
        Key = attribute.Key;
        Name = attribute.Name;
        Kind = attribute.Kind.ToString();
        CellsPerChunkEdge = attribute.CellsPerChunkEdge;
        Components = attribute.Components;
        ElementWidth = attribute.ElementWidth;
        CatalogName = attribute.CatalogName;
        DefaultValue = attribute.DefaultValue;
        Seeded = attribute.Seeded;
    }

    [ScriptProperty] public string Key { get; }

    [ScriptProperty] public string Name { get; }

    [ScriptProperty] public string Kind { get; }

    [ScriptProperty] public int CellsPerChunkEdge { get; }

    [ScriptProperty] public int Components { get; }

    [ScriptProperty] public int ElementWidth { get; }

    [ScriptProperty] public string CatalogName { get; }

    [ScriptProperty] public long DefaultValue { get; }

    [ScriptProperty] public bool Seeded { get; }
}

/// <summary>
/// Authors and reads terrain attributes, exposed to JS as <c>wms.attributes</c>. A script declares an
/// attribute and binds material writes the same way a person does — there is no "set this cell" call,
/// because there is no cell store to set: attribute values are derived per build from materials and
/// channels.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class AttributesScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "attributes";

    public float Priority => 0f;

    public AttributesScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    private LandscapeSystem Landscape => _context.Landscape;

    private MapId Map => _context.Maps.CurrentMap;

    /// <summary>The open map's declared attributes.</summary>
    [ScriptFunction]
    public AttributeDescriptor[] List() =>
        Landscape.Catalog.Attributes.Select(attribute => new AttributeDescriptor(attribute)).ToArray();

    /// <summary>Declares a new attribute on the open map. <paramref name="kind"/> is one of Int, Bool,
    /// Flags, Enum, CatalogRef, Float.</summary>
    [ScriptFunction]
    public AttributeDescriptor Declare(
        string key,
        string name = "",
        string kind = "Int",
        int cellsPerChunkEdge = 1,
        int components = 1,
        int elementWidth = 32,
        string catalogName = "",
        long defaultValue = 0)
    {
        if (!Landscape.IsEnabled)
        {
            throw new InvalidOperationException("The open map has no landscape.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("An attribute needs a key.");
        }

        if (Landscape.Catalog.Attributes.Any(attribute => attribute.Key == key))
        {
            throw new InvalidOperationException($"Attribute '{key}' already exists on this map.");
        }

        if (!Enum.TryParse(kind, ignoreCase: true, out TerrainAttributeKind parsedKind))
        {
            throw new InvalidOperationException(
                $"Unknown attribute kind '{kind}'. Known: {string.Join(", ", Enum.GetNames<TerrainAttributeKind>())}.");
        }

        var attribute = new TerrainAttribute
        {
            Map = Map,
            Key = key,
            Name = name.Length > 0 ? name : key,
            Kind = parsedKind,
            CellsPerChunkEdge = Math.Max(1, cellsPerChunkEdge),
            Components = components,
            ElementWidth = elementWidth,
            CatalogName = catalogName,
            DefaultValue = unchecked((uint)defaultValue),
        };

        Record(attribute);
        return new AttributeDescriptor(attribute);
    }

    /// <summary>Adds a value/bit name to an <c>Enum</c> or <c>Flags</c> attribute.</summary>
    [ScriptFunction]
    public void AddValue(string key, long value, string name)
    {
        TerrainAttribute attribute = RequireAttribute(key);
        var row = new TerrainAttributeValue
        {
            Map = Map,
            AttributeId = attribute.RecordId ?? 0,
            Value = value,
            Name = name,
        };
        Record(row);
    }

    /// <summary>Binds a write on a material: it runs <paramref name="function"/> (an attribute-function
    /// id) with <paramref name="parameters"/> (a serialized parameter bag) to write
    /// <paramref name="attribute"/> (a key, or <c>key:swizzle</c>). Returns the new write's id.</summary>
    [ScriptFunction]
    public int Write(int materialId, string attribute, string function, string parameters = "")
    {
        if (Landscape.Catalog.Materials.All(material => material.RecordId != materialId))
        {
            throw new InvalidOperationException($"No material {materialId} on the open map.");
        }

        var write = new LandscapeMaterialAttributeWrite
        {
            Map = Map,
            MaterialId = materialId,
            Attribute = attribute,
            Function = function,
            Parameters = parameters,
        };

        Record(write);
        return write.RecordId ?? 0;
    }

    /// <summary>The values of one attribute's cell (0,0) in a built chunk — one entry per component —
    /// or null when that chunk is not loaded or nothing wrote the attribute there.</summary>
    [ScriptFunction]
    public long[]? Read(string key, int chunkX, int chunkY)
    {
        if (Landscape.ChunkIndex.OutputAt(new ChunkCoord(chunkX, chunkY)) is not { } output)
        {
            return null;
        }

        if (!output.Attributes.TryGetValue(key, out TerrainAttributeGrid grid))
        {
            return null;
        }

        var values = new long[grid.Components];
        for (int c = 0; c < grid.Components; c++)
        {
            values[c] = grid.At(0, 0, c);
        }

        return values;
    }

    /// <summary>Resolves a value against an attribute's declaration: an <c>Enum</c> case name, the
    /// space-joined names of the set bits of a <c>Flags</c> value, or the raw number otherwise.</summary>
    [ScriptFunction]
    public string ResolveName(string key, long value)
    {
        if (Landscape.Catalog.Attributes.FirstOrDefault(attribute => attribute.Key == key) is not { } attribute)
        {
            return value.ToString();
        }

        return TerrainAttributeNames.Resolve(attribute, value, _context.Catalog.OfType<TerrainAttributeValue>());
    }

    private TerrainAttribute RequireAttribute(string key) =>
        Landscape.Catalog.Attributes.FirstOrDefault(attribute => attribute.Key == key)
        ?? throw new InvalidOperationException($"No attribute '{key}' on the open map.");

    private void Record<T>(T entity)
        where T : CatalogEntity, IKeyedCatalogEntity
    {
        _context.Catalog.AssignId(entity);
        var command = new CreateCatalogEntityCommand(_context.Catalog, entity);
        command.Apply();
        _context.EditSessions.Record(command);
    }
}
