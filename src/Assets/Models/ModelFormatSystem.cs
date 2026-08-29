using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>Registry of <see cref="IModelFormat"/>s, mirroring <see cref="ProceduralMeshSystem"/>'s shape.</summary>
public sealed partial class ModelFormatSystem : ISubsystemHost
{
    private readonly Dictionary<string, IModelFormat> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];

    public ModelFormatSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
        DiscoverFrom(Subsystems.OfType<IModelFormat>());
    }

    public EditorContext Context { get; }

    public IReadOnlyList<IModelFormat> All { get; private set; } = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public int Version { get; private set; }

    public IModelFormat? Find(string id) =>
        id.Length > 0 && _byId.TryGetValue(id, out IModelFormat? format) ? format : null;

    public void DiscoverFrom(IEnumerable<IModelFormat> formats)
    {
        _byId.Clear();
        _warnings.Clear();

        foreach (IModelFormat format in formats)
        {
            Register(format);
        }

        All = _byId.Values.OrderBy(format => format.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        Version++;

        foreach (string warning in _warnings)
        {
            GD.PushWarning($"[ModelFormat] Format skipped - {warning}");
        }
    }

    private void Register(IModelFormat format)
    {
        if (format.Id.Trim().Length == 0)
        {
            _warnings.Add($"{format.GetType().Name}: has an empty Id.");
            return;
        }

        if (_byId.TryGetValue(format.Id, out IModelFormat? existing))
        {
            _warnings.Add($"{format.GetType().Name}: id '{format.Id}' is already used by {existing.GetType().Name}.");
            return;
        }

        _byId[format.Id] = format;
    }
}
