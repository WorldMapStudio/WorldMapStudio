namespace WorldMapStudio.SourceGenerators;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Generates a public "InitializeSubsystems" method (and backing fields) on classes referenced
/// by [Subsystem(nameof(Parent))] attributes on other classes, e.g.
/// <code>
/// [Subsystem(nameof(MyClass))]
/// public class MySubsystem : ISubsystem
/// {
///     public float Priority => 0f;
///     public MySubsystem(MyClass parent) { }
/// }
/// </code>
/// The generated method also populates a "Subsystems" property with all subsystems of the
/// parent, ordered ascending by <see cref="ISubsystem.Priority"/>. Every class implementing
/// <see cref="ISubsystemHost"/> receives these two members, even if it hosts no subsystems, so
/// that "InitializeSubsystems();" can always be called unqualified from its constructor.
/// </summary>
[Generator]
public sealed class SubsystemGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "WorldMapStudio.SubsystemAttribute";
    private const string ISubsystemMetadataName = "WorldMapStudio.ISubsystem";
    private const string ISubsystemQualifiedName = "global::WorldMapStudio.ISubsystem";
    private const string ISubsystemHostMetadataName = "WorldMapStudio.ISubsystemHost";
    private const string ISubsystemHostQualifiedName = "global::WorldMapStudio.ISubsystemHost";
    private const string HostAttributeFullName = "WorldMapStudio.SubsystemHostAttribute";

    private static readonly SymbolDisplayFormat QualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly DiagnosticDescriptor MustImplementISubsystem = new(
        "WMS0005",
        "Subsystem must implement ISubsystem",
        "The [Subsystem] attribute on '{0}' requires the class to implement 'WorldMapStudio.ISubsystem'",
        "WorldMapStudio.Subsystem",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ArgumentMustBeNameOf = new(
        "WMS0001",
        "Subsystem parent must be specified with nameof",
        "The [Subsystem] attribute on '{0}' must specify its parent as 'nameof(ParentClass)'",
        "WorldMapStudio.Subsystem",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParentNotResolved = new(
        "WMS0002",
        "Subsystem parent could not be resolved",
        "The [Subsystem] attribute on '{0}' references '{1}', which could not be resolved to a class",
        "WorldMapStudio.Subsystem",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParentMustBePartial = new(
        "WMS0003",
        "Subsystem host must be partial",
        "'{0}' must be declared 'partial' to receive generated subsystem support",
        "WorldMapStudio.Subsystem",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParentMustNotBeNested = new(
        "WMS0004",
        "Subsystem host must not be nested",
        "'{0}' must be a top-level class to receive generated subsystem support",
        "WorldMapStudio.Subsystem",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParentMustImplementISubsystemHost = new(
        "WMS0006",
        "Subsystem host must implement ISubsystemHost",
        "'{0}' must implement 'WorldMapStudio.ISubsystemHost' to host subsystem '{1}'",
        "WorldMapStudio.Subsystem",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ContractMustBeSubsystem = new(
        "WMS0007",
        "Subsystem host contract must be an ISubsystem",
        "The [SubsystemHost] contract '{0}' on '{1}' must be a reference type that is or implements 'WorldMapStudio.ISubsystem'",
        "WorldMapStudio.Subsystem",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SubsystemMustMeetContract = new(
        "WMS0008",
        "Subsystem does not satisfy its host's contract",
        "'{0}' must implement '{1}' to be a subsystem of '{2}'",
        "WorldMapStudio.Subsystem",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var subsystemInfos = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeFullName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => GetSubsystemInfo(ctx, ct));

        context.RegisterSourceOutput(subsystemInfos, static (spc, info) =>
        {
            foreach (var diagnostic in info.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic);
            }
        });

        var hostInfos = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, ct) => GetHostInfo(ctx, ct))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!.Value);

        context.RegisterSourceOutput(hostInfos, static (spc, info) =>
        {
            foreach (var diagnostic in info.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic);
            }
        });

        var combined = subsystemInfos.Collect().Combine(hostInfos.Collect());
        context.RegisterSourceOutput(combined, static (spc, pair) => EmitAll(spc, pair.Left, pair.Right));
    }

    private static SubsystemInfo GetSubsystemInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var subsystemType = (INamedTypeSymbol)ctx.TargetSymbol;
        var compilation = ctx.SemanticModel.Compilation;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (!ImplementsInterface(subsystemType, ISubsystemMetadataName, compilation))
        {
            diagnostics.Add(Diagnostic.Create(
                MustImplementISubsystem, subsystemType.Locations.FirstOrDefault() ?? Location.None, subsystemType.Name));
        }

        var attributeSyntax = ctx.Attributes[0].ApplicationSyntaxReference?.GetSyntax(ct) as AttributeSyntax;
        var argument = attributeSyntax?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        var nameofTarget = TryGetNameofTarget(argument);

        INamedTypeSymbol? parentType = null;
        if (nameofTarget is null)
        {
            var errorNode = (SyntaxNode?)argument ?? attributeSyntax;
            var location = errorNode?.GetLocation() ?? Location.None;
            diagnostics.Add(Diagnostic.Create(ArgumentMustBeNameOf, location, subsystemType.Name));
        }
        else
        {
            var model = compilation.GetSemanticModel(nameofTarget.SyntaxTree);
            var resolvedParent = model.GetSymbolInfo(nameofTarget, ct).Symbol as INamedTypeSymbol;

            if (resolvedParent is null || resolvedParent.TypeKind != TypeKind.Class)
            {
                diagnostics.Add(Diagnostic.Create(
                    ParentNotResolved, nameofTarget.GetLocation(), subsystemType.Name, nameofTarget.ToString()));
            }
            else if (resolvedParent.ContainingType is not null)
            {
                diagnostics.Add(Diagnostic.Create(ParentMustNotBeNested, nameofTarget.GetLocation(), resolvedParent.Name));
            }
            else if (!ImplementsInterface(resolvedParent, ISubsystemHostMetadataName, compilation))
            {
                diagnostics.Add(Diagnostic.Create(
                    ParentMustImplementISubsystemHost, nameofTarget.GetLocation(), resolvedParent.Name, subsystemType.Name));
            }
            else
            {
                var isPartial = resolvedParent.DeclaringSyntaxReferences
                    .Select(r => r.GetSyntax(ct))
                    .OfType<ClassDeclarationSyntax>()
                    .Any(c => c.Modifiers.Any(SyntaxKind.PartialKeyword));

                if (!isPartial)
                {
                    diagnostics.Add(Diagnostic.Create(ParentMustBePartial, nameofTarget.GetLocation(), resolvedParent.Name));
                }
                else if (GetContract(resolvedParent, compilation) is { } contract && !Satisfies(subsystemType, contract))
                {
                    diagnostics.Add(Diagnostic.Create(
                        SubsystemMustMeetContract,
                        attributeSyntax?.GetLocation() ?? subsystemType.Locations.FirstOrDefault() ?? Location.None,
                        subsystemType.Name, contract.Name, resolvedParent.Name));
                }
                else
                {
                    parentType = resolvedParent;
                }
            }
        }

        if (diagnostics.Count > 0)
        {
            return new SubsystemInfo(null, subsystemType, diagnostics.ToImmutable());
        }

        return new SubsystemInfo(parentType, subsystemType, ImmutableArray<Diagnostic>.Empty);
    }

    private static HostInfo? GetHostInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
        if (symbol is null || !ImplementsInterface(symbol, ISubsystemHostMetadataName, ctx.SemanticModel.Compilation))
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (symbol.ContainingType is not null)
        {
            diagnostics.Add(Diagnostic.Create(ParentMustNotBeNested, classDecl.Identifier.GetLocation(), symbol.Name));
        }

        var isPartial = symbol.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(ct))
            .OfType<ClassDeclarationSyntax>()
            .Any(c => c.Modifiers.Any(SyntaxKind.PartialKeyword));

        if (!isPartial)
        {
            diagnostics.Add(Diagnostic.Create(ParentMustBePartial, classDecl.Identifier.GetLocation(), symbol.Name));
        }

        INamedTypeSymbol? contract = null;
        var contractAttribute = GetContractAttribute(symbol, ctx.SemanticModel.Compilation);
        if (contractAttribute is not null && GetContract(symbol, ctx.SemanticModel.Compilation) is { } declared)
        {
            contract = declared;
        }
        else if (contractAttribute is not null
            && contractAttribute.ApplicationSyntaxReference is { } reference
            && reference.SyntaxTree == classDecl.SyntaxTree
            && classDecl.Span.Contains(reference.Span))
        {
            var written = contractAttribute.ConstructorArguments.Length > 0
                ? contractAttribute.ConstructorArguments[0].Value?.ToString() ?? "?"
                : "?";
            diagnostics.Add(Diagnostic.Create(
                ContractMustBeSubsystem, reference.GetSyntax(ct).GetLocation(), written, symbol.Name));
        }

        var hostType = diagnostics.Count == 0 ? symbol : null;
        return new HostInfo(hostType, contract, diagnostics.ToImmutable());
    }

    private static AttributeData? GetContractAttribute(INamedTypeSymbol host, Compilation compilation)
    {
        var attributeType = compilation.GetTypeByMetadataName(HostAttributeFullName);
        return attributeType is null
            ? null
            : host.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));
    }

    // Null when the host declares no contract, or declares one that is not a valid ISubsystem type.
    private static INamedTypeSymbol? GetContract(INamedTypeSymbol host, Compilation compilation)
    {
        if (GetContractAttribute(host, compilation) is not { ConstructorArguments.Length: 1 } attribute
            || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol contract
            || !contract.IsReferenceType)
        {
            return null;
        }

        var subsystem = compilation.GetTypeByMetadataName(ISubsystemMetadataName);
        return subsystem is not null && Satisfies(contract, subsystem) ? contract : null;
    }

    private static bool Satisfies(INamedTypeSymbol type, INamedTypeSymbol contract)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, contract))
            {
                return true;
            }
        }

        return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, contract));
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string metadataName, Compilation compilation)
    {
        var interfaceType = compilation.GetTypeByMetadataName(metadataName);
        return interfaceType is not null && type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType));
    }

    private static ExpressionSyntax? TryGetNameofTarget(ExpressionSyntax? argument)
    {
        if (argument is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        if (invocation.Expression is not IdentifierNameSyntax { Identifier.Text: "nameof" })
        {
            return null;
        }

        if (invocation.ArgumentList.Arguments.Count != 1)
        {
            return null;
        }

        return invocation.ArgumentList.Arguments[0].Expression;
    }

    private static void EmitAll(SourceProductionContext spc, ImmutableArray<SubsystemInfo> subsystemInfos, ImmutableArray<HostInfo> hostInfos)
    {
        var subsystemsByParent = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        foreach (var info in subsystemInfos)
        {
            if (info.ParentType is null)
            {
                continue;
            }

            if (!subsystemsByParent.TryGetValue(info.ParentType, out var list))
            {
                list = new List<INamedTypeSymbol>();
                subsystemsByParent[info.ParentType] = list;
            }

            list.Add(info.SubsystemType);
        }

        var contractByHost = new Dictionary<INamedTypeSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var host in hostInfos)
        {
            if (host.HostType is not null && host.Contract is not null)
            {
                contractByHost[host.HostType] = host.Contract;
            }
        }

        foreach (var entry in subsystemsByParent)
        {
            contractByHost.TryGetValue(entry.Key, out var contract);
            Emit(spc, entry.Key, entry.Value.ToImmutableArray(), contract);
        }

        var emittedEmptyHosts = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var host in hostInfos)
        {
            if (host.HostType is null || subsystemsByParent.ContainsKey(host.HostType) || !emittedEmptyHosts.Add(host.HostType))
            {
                continue;
            }

            Emit(spc, host.HostType, ImmutableArray<INamedTypeSymbol>.Empty, host.Contract);
        }
    }

    private static void Emit(
        SourceProductionContext spc, INamedTypeSymbol parent, ImmutableArray<INamedTypeSymbol> subsystemsRaw, INamedTypeSymbol? contract)
    {
        var subsystems = subsystemsRaw
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>()
            .OrderBy(static s => s.ToDisplayString(QualifiedFormat), System.StringComparer.Ordinal)
            .ToImmutableArray();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine();

        if (!parent.ContainingNamespace.IsGlobalNamespace)
        {
            sb.Append("namespace ").Append(parent.ContainingNamespace.ToDisplayString()).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(AccessibilityKeyword(parent.DeclaredAccessibility)).Append("partial class ").Append(parent.Name)
            .Append(" : ").AppendLine(ISubsystemHostQualifiedName);
        sb.AppendLine("{");

        foreach (var subsystem in subsystems)
        {
            sb.Append("    public ").Append(subsystem.ToDisplayString(QualifiedFormat))
                .Append(' ').Append(subsystem.Name).AppendLine(";");
        }

        if (subsystems.Length > 0)
        {
            sb.AppendLine();
        }

        var elementType = contract is null ? ISubsystemQualifiedName : contract.ToDisplayString(QualifiedFormat);
        sb.Append("    public IEnumerable<").Append(elementType).Append("> Subsystems { get; private set; } = System.Linq.Enumerable.Empty<")
            .Append(elementType).AppendLine(">();");
        sb.AppendLine();

        if (contract is not null)
        {
            sb.Append("    IEnumerable<").Append(ISubsystemQualifiedName).Append("> ").Append(ISubsystemHostQualifiedName)
                .AppendLine(".Subsystems => Subsystems;");
            sb.AppendLine();
        }

        if (subsystems.Length > 0)
        {
            var memberNotNullArgs = subsystems.Select(static s => $"nameof({s.Name})");
            sb.Append("    [System.Diagnostics.CodeAnalysis.MemberNotNull(")
                .Append(string.Join(", ", memberNotNullArgs))
                .AppendLine(")]");
        }

        sb.AppendLine("    public void InitializeSubsystems()");
        sb.AppendLine("    {");
        foreach (var subsystem in subsystems)
        {
            sb.Append("        ").Append(subsystem.Name).Append(" = new ")
                .Append(subsystem.ToDisplayString(QualifiedFormat)).AppendLine("(this);");
        }

        if (subsystems.Length > 0)
        {
            var priority = contract is null ? "s.Priority" : $"(({ISubsystemQualifiedName})s).Priority";
            sb.Append("        Subsystems = new ").Append(elementType).Append("[] { ")
                .Append(string.Join(", ", subsystems.Select(static s => s.Name)))
                .Append(" }.OrderBy(static s => ").Append(priority).AppendLine(").ToArray();");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        var hintName = parent.ToDisplayString(QualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace('.', '_');
        spc.AddSource($"{hintName}_Subsystems.g.cs", sb.ToString());
    }

    private static string AccessibilityKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public ",
        Accessibility.Internal => "internal ",
        Accessibility.Protected => "protected ",
        Accessibility.ProtectedOrInternal => "protected internal ",
        Accessibility.ProtectedAndInternal => "private protected ",
        Accessibility.Private => "private ",
        _ => string.Empty,
    };

    private readonly struct SubsystemInfo
    {
        public SubsystemInfo(INamedTypeSymbol? parentType, INamedTypeSymbol subsystemType, ImmutableArray<Diagnostic> diagnostics)
        {
            ParentType = parentType;
            SubsystemType = subsystemType;
            Diagnostics = diagnostics;
        }

        public INamedTypeSymbol? ParentType { get; }

        public INamedTypeSymbol SubsystemType { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }

    private readonly struct HostInfo
    {
        public HostInfo(INamedTypeSymbol? hostType, INamedTypeSymbol? contract, ImmutableArray<Diagnostic> diagnostics)
        {
            HostType = hostType;
            Contract = contract;
            Diagnostics = diagnostics;
        }

        public INamedTypeSymbol? HostType { get; }

        public INamedTypeSymbol? Contract { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }
}
