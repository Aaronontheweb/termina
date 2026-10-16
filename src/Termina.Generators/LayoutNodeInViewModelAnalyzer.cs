// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Termina.Generators;

/// <summary>
/// Analyzer that detects when layout node types (ILayoutNode implementations)
/// are declared as fields or properties in ReactiveViewModel-derived classes.
///
/// This is an anti-pattern because:
/// - ViewModels should contain application STATE, not UI components
/// - Layout nodes belong in Page/View classes (BuildLayout method)
/// - Mixing UI components in ViewModels breaks the MVVM separation
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutNodeInViewModelAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for layout nodes in ViewModel fields/properties.
    /// </summary>
    public const string DiagnosticId = "TERMINA002";

    private const string Category = "Termina.Architecture";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Layout nodes should not be declared in ViewModels",
        messageFormat: "'{0}' of type '{1}' is a layout node. Layout nodes should be created in the Page's BuildLayout() method, not stored in ViewModels. ViewModels should only contain application state ([Reactive] properties) and business logic.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ViewModels in the MVVM pattern should contain application state and business logic, not UI components. Layout nodes (ILayoutNode implementations) should be created in the Page class's BuildLayout() method. This separation ensures proper testability and maintains a clear boundary between UI and application logic.",
        helpLinkUri: "https://aaronstannard.com/termina/analyzers/termina002");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Use CompilationStartAction to cache symbol lookups once per compilation
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var terminaContext = new TerminaContext(compilationContext.Compilation);

            // Only run analysis if Termina types are available
            if (!terminaContext.HasTerminaInstalled)
                return;

            // Register for both property and field declarations
            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeProperty(ctx, terminaContext),
                SyntaxKind.PropertyDeclaration);

            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeField(ctx, terminaContext),
                SyntaxKind.FieldDeclaration);
        });
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context, TerminaContext terminaContext)
    {
        var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;

        // Get the property symbol
        var propertySymbol = context.SemanticModel.GetDeclaredSymbol(propertyDeclaration);
        if (propertySymbol is null)
            return;

        // Check if containing type inherits from ReactiveViewModel
        var containingType = propertySymbol.ContainingType;
        if (!terminaContext.InheritsFromReactiveViewModel(containingType))
            return;

        // Check if property type implements ILayoutNode
        var propertyType = propertySymbol.Type;
        if (!terminaContext.ImplementsILayoutNode(propertyType))
            return;

        // Report the diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            propertyDeclaration.GetLocation(),
            propertySymbol.Name,
            propertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeField(SyntaxNodeAnalysisContext context, TerminaContext terminaContext)
    {
        var fieldDeclaration = (FieldDeclarationSyntax)context.Node;

        // Get the containing type
        if (fieldDeclaration.Parent is not TypeDeclarationSyntax)
            return;

        // Process each variable in the field declaration
        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            var fieldSymbol = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
            if (fieldSymbol is null)
                continue;

            // Check if containing type inherits from ReactiveViewModel
            var containingType = fieldSymbol.ContainingType;
            if (!terminaContext.InheritsFromReactiveViewModel(containingType))
                continue;

            // Check if field type implements ILayoutNode
            var fieldType = fieldSymbol.Type;
            if (!terminaContext.ImplementsILayoutNode(fieldType))
                continue;

            // Report the diagnostic on the full field declaration for consistency with properties
            var diagnostic = Diagnostic.Create(
                Rule,
                fieldDeclaration.GetLocation(),
                fieldSymbol.Name,
                fieldType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Caches Termina symbols for efficient lookup during analysis.
    /// </summary>
    private sealed class TerminaContext
    {
        private readonly INamedTypeSymbol? _reactiveViewModelType;
        private readonly INamedTypeSymbol? _layoutNodeInterface;

        public TerminaContext(Compilation compilation)
        {
            _reactiveViewModelType = compilation.GetTypeByMetadataName("Termina.Reactive.ReactiveViewModel");
            _layoutNodeInterface = compilation.GetTypeByMetadataName("Termina.Layout.ILayoutNode");
        }

        /// <summary>
        /// Returns true if Termina types are available in this compilation.
        /// </summary>
        public bool HasTerminaInstalled => _reactiveViewModelType is not null && _layoutNodeInterface is not null;

        /// <summary>
        /// Checks if a type inherits from ReactiveViewModel using symbol equality.
        /// </summary>
        public bool InheritsFromReactiveViewModel(INamedTypeSymbol? type)
        {
            if (_reactiveViewModelType is null || type is null)
                return false;

            var current = type.BaseType;
            while (current is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, _reactiveViewModelType))
                    return true;
                current = current.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Checks if a type implements ILayoutNode interface using symbol equality.
        /// </summary>
        public bool ImplementsILayoutNode(ITypeSymbol type)
        {
            if (_layoutNodeInterface is null)
                return false;

            // Handle nullable reference types
            if (type.NullableAnnotation == NullableAnnotation.Annotated &&
                type is INamedTypeSymbol namedType)
            {
                type = namedType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            }

            // Direct check for ILayoutNode
            if (SymbolEqualityComparer.Default.Equals(type, _layoutNodeInterface))
                return true;

            // Check all interfaces (including inherited)
            foreach (var iface in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface, _layoutNodeInterface))
                    return true;
            }

            return false;
        }
    }
}
