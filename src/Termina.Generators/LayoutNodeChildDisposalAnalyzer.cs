// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Termina.Generators;

/// <summary>
/// Detects disposal of a layout node child during a content switch.
///
/// Termina layout nodes use an active/inactive lifecycle. <c>IActivatableNode.OnDeactivate()</c> pauses a
/// child and keeps it alive. <c>Dispose()</c> destroys it, so it can no longer render or handle input. A
/// container that swaps content should deactivate the old child, not dispose it. Disposal is only correct in
/// the container's own teardown (final cleanup).
///
/// To keep the false-positive rate low, the analyzer only flags <c>Dispose()</c> on a field or property that
/// this container holds — accessed on <c>this</c>/<c>base</c> — whose type implements <c>ILayoutNode</c>,
/// inside a type that derives from <c>LayoutNode</c>. It never flags disposal inside the container's own
/// teardown: a <c>Dispose()</c>/<c>DisposeAsync()</c> method, a <c>Dispose(bool)</c> method, or a finalizer.
/// Locals, method parameters, collection elements, and members of other objects are out of scope.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutNodeChildDisposalAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "TERMINA003";

    private const string Category = "Termina.Lifecycle";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Do not dispose a layout node child when switching content",
        messageFormat: "The layout node '{0}' is disposed outside this type's Dispose() method. Dispose() destroys the node, so it can no longer render or handle input. To switch content, call OnDeactivate() instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Termina layout nodes use an active/inactive lifecycle. OnDeactivate() pauses a child and keeps it alive. Dispose() destroys it. A container should deactivate a switched-out child, not dispose it. Dispose a child only in the container's own teardown (Dispose/DisposeAsync/Dispose(bool)/finalizer).",
        helpLinkUri: "https://aaronstannard.com/termina/analyzers/termina003");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var terminaContext = new TerminaContext(compilationContext.Compilation);
            if (!terminaContext.HasRequiredTypes)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeDisposeInvocation(ctx, terminaContext),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeDisposeInvocation(SyntaxNodeAnalysisContext context, TerminaContext terminaContext)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!TryGetDisposeInvocation(invocation, context.SemanticModel, context.CancellationToken, out var disposeName, out var receiverExpression))
            return;

        // The receiver must be a member of this container (accessed on `this`/`base`), not a member of
        // another object, a collection element, a local, or a parameter.
        if (!IsThisRootedMember(receiverExpression))
            return;

        var receiverSymbol = context.SemanticModel.GetSymbolInfo(receiverExpression, context.CancellationToken).Symbol;
        var receiverType = receiverSymbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };
        if (receiverType is null || !terminaContext.ImplementsLayoutNode(receiverType))
            return;

        // The enclosing type must be a container that derives from LayoutNode.
        var enclosingTypeSyntax = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingTypeSyntax is null)
            return;

        var enclosingType = context.SemanticModel.GetDeclaredSymbol(enclosingTypeSyntax, context.CancellationToken);
        if (enclosingType is null || !terminaContext.DerivesFromLayoutNode(enclosingType))
            return;

        // Disposal is correct in the container's own teardown, so skip those members.
        if (IsInsideTeardownMember(invocation))
            return;

        var diagnostic = Diagnostic.Create(Rule, disposeName.GetLocation(), receiverSymbol!.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool TryGetDisposeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SimpleNameSyntax disposeName,
        out ExpressionSyntax receiverExpression)
    {
        disposeName = null!;
        receiverExpression = null!;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            disposeName = memberAccess.Name;
            receiverExpression = memberAccess.Expression;
        }
        else if (invocation.Expression is MemberBindingExpressionSyntax memberBinding
            && invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            disposeName = memberBinding.Name;
            receiverExpression = conditionalAccess.Expression;
        }
        else
        {
            return false;
        }

        if (disposeName.Identifier.ValueText != "Dispose")
            return false;

        var method = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        return method is { Name: "Dispose", Parameters.Length: 0 };
    }

    /// <summary>
    /// True when the receiver is a member of the current instance: an unqualified field or property, or one
    /// qualified with <c>this</c> or <c>base</c>. Disposing a member of another object, a collection element,
    /// a local, or a parameter is out of scope.
    /// </summary>
    private static bool IsThisRootedMember(ExpressionSyntax receiver) => Unwrap(receiver) switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax memberAccess => memberAccess.Expression is ThisExpressionSyntax or BaseExpressionSyntax,
        _ => false
    };

    /// <summary>
    /// Strips parentheses and the null-forgiving operator from a receiver, so that <c>(_child)</c> and
    /// <c>_child!</c> classify the same as <c>_child</c>.
    /// </summary>
    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    break;
                default:
                    return expression;
            }
        }
    }

    /// <summary>
    /// True when the invocation sits inside the container's own teardown: a <c>Dispose()</c>,
    /// <c>DisposeAsync()</c>, or <c>Dispose(bool)</c> method, or a finalizer. Disposal there is correct.
    /// Lambdas and local functions are transparent; the search walks up to the containing member.
    /// </summary>
    private static bool IsInsideTeardownMember(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case DestructorDeclarationSyntax:
                    return true;
                case MethodDeclarationSyntax method:
                    return method.Identifier.ValueText is "Dispose" or "DisposeAsync";
                case ConstructorDeclarationSyntax:
                case AccessorDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private sealed class TerminaContext
    {
        private readonly INamedTypeSymbol? _iLayoutNode;
        private readonly INamedTypeSymbol? _layoutNode;

        public TerminaContext(Compilation compilation)
        {
            _iLayoutNode = compilation.GetTypeByMetadataName("Termina.Layout.ILayoutNode");
            _layoutNode = compilation.GetTypeByMetadataName("Termina.Layout.LayoutNode");
        }

        public bool HasRequiredTypes => _iLayoutNode is not null && _layoutNode is not null;

        public bool ImplementsLayoutNode(ITypeSymbol type)
        {
            if (_iLayoutNode is null)
                return false;

            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _iLayoutNode))
                return true;

            foreach (var iface in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, _iLayoutNode))
                    return true;
            }

            return false;
        }

        public bool DerivesFromLayoutNode(INamedTypeSymbol type)
        {
            if (_layoutNode is null)
                return false;

            for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, _layoutNode))
                    return true;
            }

            return false;
        }
    }
}
