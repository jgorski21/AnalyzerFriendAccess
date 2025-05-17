using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FriendAnalyzer : DiagnosticAnalyzer
{
    public const string Id = "FRIEND001";

    private static readonly DiagnosticDescriptor ErrorRule = new(
        Id,
        title: "Type can be used only by its friends",
        messageFormat: "'{0}' is not a friend of '{1}'",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor WarningRule = new(
        Id,
        title: ErrorRule.Title.ToString(),
        messageFormat: ErrorRule.MessageFormat.ToString(),
        category: ErrorRule.Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => [ErrorRule, WarningRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Perf-friendly cache for the current compilation
        context.RegisterCompilationStartAction(start =>
        {
            var friendAttr = start.Compilation.GetTypeByMetadataName(
                "AnalyzerFriendAccess.Friend.FriendAttribute");

            // map: annotated type → (whitelist, severity)
            var cache = new Dictionary<INamedTypeSymbol, FriendInfo>(
                SymbolEqualityComparer.Default);

            // ONE registration is enough to satisfy RS1012
            start.RegisterOperationAction(oc =>
                {
                    // Which type are we touching?
                    var target = oc.Operation switch
                    {
                        IMemberReferenceOperation m => m.Member.ContainingType,
                        IObjectCreationOperation  c => c.Constructor?.ContainingType,
                        _ => null
                    };
                    if (target is null) return;

                    if (!cache.TryGetValue(target, out var info))
                    {
                        info = FriendInfo.Build(target, friendAttr);
                        cache[target] = info;
                    }

                    if (!info.HasFriends) return;

                    var caller = oc.ContainingSymbol?.ContainingType;
                    if (caller is null ||
                        SymbolEqualityComparer.Default.Equals(caller, target) ||
                        info.Friends.Contains(caller))
                        return;

                    var rule = info.Severity == DiagnosticSeverity.Warning
                        ? WarningRule
                        : ErrorRule;

                    oc.ReportDiagnostic(
                        Diagnostic.Create(rule,
                            oc.Operation.Syntax.GetLocation(),
                            caller.Name, target.Name));
                },
                OperationKind.Invocation,
                OperationKind.MethodReference,
                OperationKind.FieldReference,
                OperationKind.PropertyReference,
                OperationKind.ObjectCreation);
        });
    }

    private readonly record struct FriendInfo(
        ImmutableHashSet<INamedTypeSymbol> Friends,
        DiagnosticSeverity Severity)
    {
        public bool HasFriends => Friends.Count != 0;

        public static FriendInfo Build(
            INamedTypeSymbol target,
            INamedTypeSymbol? attrSymbol)
        {
            if (attrSymbol is null)
                return default;

            foreach (var a in target.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(a.AttributeClass,
                        attrSymbol))
                    continue;

                var sev = DiagnosticSeverity.Error;
                var set = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(
                    SymbolEqualityComparer.Default);

                foreach (var arg in a.ConstructorArguments)
                {
                    // friend list
                    if (arg.Kind == TypedConstantKind.Array)
                    {
                        foreach (var v in arg.Values)
                            if (v.Value is INamedTypeSymbol t) set.Add(t);
                    }
                    // severity
                    else if (arg.Type?.Name == "FriendLevel" &&
                             arg.Value is int level)
                    {
                        sev = level == 1
                            ? DiagnosticSeverity.Warning
                            : DiagnosticSeverity.Error;
                    }
                }
                return new FriendInfo(set.ToImmutable(), sev);
            }
            return default;
        }
    }
}