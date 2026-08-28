using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

public sealed partial class ProceduralMeshSystem : ISubsystemHost
{
    private readonly Dictionary<string, IProceduralMeshFunction> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];

    public ProceduralMeshSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
        DiscoverFrom(Subsystems.OfType<IProceduralMeshFunction>());
    }

    public EditorContext Context { get; }

    public IReadOnlyList<IProceduralMeshFunction> All { get; private set; } = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public int Version { get; private set; }

    public IProceduralMeshFunction? Find(string id) =>
        id.Length > 0 && _byId.TryGetValue(id, out IProceduralMeshFunction? function) ? function : null;

    public ProceduralMeshOutput Build(ProceduralMeshComponent component)
    {
        var builder = new ProceduralMeshOutputBuilder();
        if (Find(component.FunctionId) is not { } function)
        {
            return builder.Build();
        }

        function.Build(new ProceduralMeshBuildContext(
            component.Network,
            ProceduralMeshParameterValues.Parse(component.Parameters),
            Context.Assets), builder);
        return builder.Build();
    }

    public void DiscoverFrom(IEnumerable<IProceduralMeshFunction> functions)
    {
        _byId.Clear();
        _warnings.Clear();

        foreach (IProceduralMeshFunction function in functions)
        {
            Register(function);
        }

        All = _byId.Values.OrderBy(function => function.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        Version++;

        foreach (string warning in _warnings)
        {
            GD.PushWarning($"[ProceduralMesh] Function skipped - {warning}");
        }
    }

    private void Register(IProceduralMeshFunction function)
    {
        if (function.Id.Trim().Length == 0)
        {
            _warnings.Add($"{function.GetType().Name}: has an empty Id.");
            return;
        }

        if (_byId.TryGetValue(function.Id, out IProceduralMeshFunction? existing))
        {
            _warnings.Add($"{function.GetType().Name}: id '{function.Id}' is already used by {existing.GetType().Name}.");
            return;
        }

        if (DuplicateParameter(function) is { } duplicate)
        {
            _warnings.Add($"{function.GetType().Name}: declares parameter '{duplicate}' twice.");
            return;
        }

        _byId[function.Id] = function;
    }

    private static string? DuplicateParameter(IProceduralMeshFunction function)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProceduralMeshParameter parameter in function.Parameters)
        {
            if (!seen.Add(parameter.Name))
            {
                return parameter.Name;
            }
        }

        return null;
    }
}
