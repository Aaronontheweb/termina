// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Termina.Generators;

/// <summary>
/// Detects broad invalidation of a dynamic layout node whose factory creates a new stateful layout node.
///
/// DynamicLayoutNode runs its factory again on Invalidate(). If the factory creates a new stateful control
/// (for example ScrollableContainerNode), the control loses state such as scroll offset, selected index,
/// input text, or cursor position. Prefer to reuse the stateful node instance, invalidate a narrower child
/// node, or use KeyedDynamicLayoutNode when switching by state.
///
/// The analyzer treats a `??=` (or `?? new ...`) fallback as safe, because that pattern creates the node
/// one time and then reuses it. It follows a private helper method one level deep. It does not do full
/// data-flow analysis, so a manual cache inside a helper can still produce a warning.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatefulLayoutNodeRecreationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "TERMINA004";

    private const string Category = "Termina.State";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Do not recreate a stateful node in a dynamic layout factory",
        messageFormat: "The dynamic layout field '{0}' creates a new {1} in its factory. A call to Invalidate() on '{0}' runs the factory again and resets the {1} state, for example the scroll position. Reuse the {1} instance, invalidate a smaller child node, or use KeyedDynamicLayoutNode.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Some layout nodes such as ScrollableContainerNode own UI state. A DynamicLayoutNode factory runs again on Invalidate(). If the factory creates the stateful node again, the node loses its state. Create the stateful node one time and reuse the instance.",
        helpLinkUri: "https://aaronstannard.com/termina/analyzers/termina004");

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
                ctx => AnalyzeInvalidateInvocation(ctx, terminaContext),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvalidateInvocation(SyntaxNodeAnalysisContext context, TerminaContext terminaContext)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!TryGetInvalidateInvocationReceiver(invocation, context.SemanticModel, context.CancellationToken, out var invalidateName, out var receiver))
            return;

        if (receiver is null || !terminaContext.IsDynamicLayoutNode(receiver.Type))
            return;

        var containingTypeSyntax = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (containingTypeSyntax is null)
            return;

        var containingType = context.SemanticModel.GetDeclaredSymbol(containingTypeSyntax, context.CancellationToken);
        if (containingType is null)
            return;

        var recreatedType = FindStatefulNodeRecreatedByDynamicFactory(
            containingTypeSyntax,
            receiver,
            containingType,
            context.SemanticModel,
            terminaContext,
            context.CancellationToken);

        if (recreatedType is null)
            return;

        var diagnostic = Diagnostic.Create(
            Rule,
            invalidateName.GetLocation(),
            receiver.Name,
            recreatedType.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool TryGetInvalidateInvocationReceiver(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SimpleNameSyntax invalidateName,
        out IFieldSymbol? receiver)
    {
        invalidateName = null!;
        receiver = null;

        ExpressionSyntax? receiverExpression;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            invalidateName = memberAccess.Name;
            receiverExpression = memberAccess.Expression;
        }
        else if (invocation.Expression is MemberBindingExpressionSyntax memberBinding)
        {
            invalidateName = memberBinding.Name;
            if (invocation.Parent is not ConditionalAccessExpressionSyntax conditionalAccess)
                return false;

            receiverExpression = conditionalAccess.Expression;
        }
        else
        {
            return false;
        }

        if (invalidateName.Identifier.ValueText != "Invalidate")
            return false;

        var method = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        if (method is null || method.Name != "Invalidate" || method.Parameters.Length != 0)
            return false;

        receiver = semanticModel.GetSymbolInfo(receiverExpression, cancellationToken).Symbol as IFieldSymbol;
        return receiver is not null;
    }

    /// <summary>
    /// Finds the assignment that gives <paramref name="dynamicLayoutField"/> a new DynamicLayoutNode, then
    /// checks whether that node's factory creates a stateful layout node. Returns the created type, or null.
    /// </summary>
    private static ITypeSymbol? FindStatefulNodeRecreatedByDynamicFactory(
        TypeDeclarationSyntax containingTypeSyntax,
        IFieldSymbol dynamicLayoutField,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        TerminaContext terminaContext,
        CancellationToken cancellationToken)
    {
        foreach (var assignment in containingTypeSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var assignedField = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol as IFieldSymbol;
            if (!SymbolEqualityComparer.Default.Equals(assignedField, dynamicLayoutField))
                continue;

            if (assignment.Right is not ObjectCreationExpressionSyntax creation)
                continue;

            var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
            if (createdType is null || !terminaContext.IsDynamicLayoutNode(createdType))
                continue;

            var factory = creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            if (factory is null)
                continue;

            var recreatedType = FindStatefulRecreationInFactory(factory, containingType, semanticModel, terminaContext, cancellationToken);
            if (recreatedType is not null)
                return recreatedType;
        }

        return null;
    }

    /// <summary>
    /// Looks for a stateful node that the factory creates on every run: a direct construction in the factory
    /// body, or a construction inside a private helper the factory calls (one level deep).
    /// </summary>
    private static ITypeSymbol? FindStatefulRecreationInFactory(
        ExpressionSyntax factoryExpression,
        INamedTypeSymbol containingType,
        SemanticModel semanticModel,
        TerminaContext terminaContext,
        CancellationToken cancellationToken)
    {
        var body = GetFactoryOrMethodBody(factoryExpression);
        if (body is null)
            return null;

        var direct = FindStatefulConstruction(body, semanticModel, terminaContext, cancellationToken);
        if (direct is not null)
            return direct;

        foreach (var invocation in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
                continue;

            if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, containingType))
                continue;

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var node = reference.GetSyntax(cancellationToken);
                if (node.SyntaxTree != semanticModel.SyntaxTree)
                    continue;

                var helperBody = GetFactoryOrMethodBody(node);
                if (helperBody is null)
                    continue;

                var viaHelper = FindStatefulConstruction(helperBody, semanticModel, terminaContext, cancellationToken);
                if (viaHelper is not null)
                    return viaHelper;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the type of the first stateful layout node that <paramref name="body"/> constructs, or null.
    /// A construction on the right of a `??=` or `??` fallback is safe (create once, then reuse) and is skipped.
    /// </summary>
    private static ITypeSymbol? FindStatefulConstruction(
        SyntaxNode body,
        SemanticModel semanticModel,
        TerminaContext terminaContext,
        CancellationToken cancellationToken)
    {
        foreach (var creation in body.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsPreservedByCoalesce(creation, body))
                continue;

            var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
            if (createdType is not null && terminaContext.IsStatefulLayoutNode(createdType))
                return createdType;
        }

        return null;
    }

    /// <summary>
    /// True when the construction is the fallback of a `??=` assignment or a `??` expression. That pattern
    /// keeps the existing instance, so the construction runs one time only.
    /// </summary>
    private static bool IsPreservedByCoalesce(ObjectCreationExpressionSyntax creation, SyntaxNode boundary)
    {
        SyntaxNode current = creation;
        while (current != boundary && current.Parent is { } parent)
        {
            if (parent is AssignmentExpressionSyntax assign
                && assign.IsKind(SyntaxKind.CoalesceAssignmentExpression)
                && assign.Right == current)
                return true;

            if (parent is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.CoalesceExpression)
                && binary.Right == current)
                return true;

            current = parent;
        }

        return false;
    }

    private static SyntaxNode? GetFactoryOrMethodBody(SyntaxNode node) => node switch
    {
        ParenthesizedLambdaExpressionSyntax lambda => (SyntaxNode?)lambda.Body,
        SimpleLambdaExpressionSyntax lambda => lambda.Body,
        AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.Body,
        MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
        LocalFunctionStatementSyntax localFunction => (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody,
        _ => null
    };

    private sealed class TerminaContext
    {
        private readonly INamedTypeSymbol? _dynamicLayoutNode;
        private readonly INamedTypeSymbol? _scrollableContainerNode;
        private readonly INamedTypeSymbol? _selectionListNode;
        private readonly INamedTypeSymbol? _textInputNode;
        private readonly INamedTypeSymbol? _streamingTextNode;

        public TerminaContext(Compilation compilation)
        {
            _dynamicLayoutNode = compilation.GetTypeByMetadataName("Termina.Layout.DynamicLayoutNode");
            _scrollableContainerNode = compilation.GetTypeByMetadataName("Termina.Layout.ScrollableContainerNode");
            _selectionListNode = compilation.GetTypeByMetadataName("Termina.Layout.SelectionListNode`1");
            _textInputNode = compilation.GetTypeByMetadataName("Termina.Layout.TextInputNode");
            _streamingTextNode = compilation.GetTypeByMetadataName("Termina.Layout.StreamingTextNode");
        }

        public bool HasRequiredTypes => _dynamicLayoutNode is not null;

        public bool IsDynamicLayoutNode(ITypeSymbol type)
        {
            return _dynamicLayoutNode is not null
                && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _dynamicLayoutNode);
        }

        public bool IsStatefulLayoutNode(ITypeSymbol type)
        {
            var original = type.OriginalDefinition;
            return SymbolEquals(original, _scrollableContainerNode)
                || SymbolEquals(original, _selectionListNode)
                || SymbolEquals(original, _textInputNode)
                || SymbolEquals(original, _streamingTextNode);
        }

        private static bool SymbolEquals(ISymbol? left, ISymbol? right)
            => left is not null && right is not null && SymbolEqualityComparer.Default.Equals(left, right);
    }
}
