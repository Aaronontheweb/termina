// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Layout;

/// <summary>
/// Factory for creating layout containers.
/// </summary>
public static class Layouts
{
    /// <summary>
    /// Create a vertical stack layout (children arranged top to bottom).
    /// </summary>
    public static VerticalLayout Vertical(params ILayoutNode[] children) => new(children);

    /// <summary>
    /// Create a horizontal layout (children arranged left to right).
    /// </summary>
    public static HorizontalLayout Horizontal(params ILayoutNode[] children) => new(children);

    /// <summary>
    /// Create a stack layout (children overlapping, last on top).
    /// </summary>
    public static StackLayout Stack(params ILayoutNode[] children) => new(children);

    /// <summary>
    /// Create an empty node (renders nothing, takes no space).
    /// </summary>
    public static EmptyNode Empty() => new();

    /// <summary>
    /// Create a modal overlay component.
    /// </summary>
    /// <remarks>
    /// Use the focus manager to show/hide modals:
    /// <code>
    /// Focus.PushFocus(modal);  // Show modal
    /// Focus.PopFocus();        // Hide modal
    /// </code>
    /// </remarks>
    public static ModalNode Modal() => new();

    /// <summary>
    /// Create a selection list with typed items.
    /// </summary>
    /// <typeparam name="T">The type of items in the list.</typeparam>
    /// <param name="items">The items to display.</param>
    /// <param name="displaySelector">Function to convert items to display text.</param>
    public static SelectionListNode<T> SelectionList<T>(
        IEnumerable<T> items,
        Func<T, string> displaySelector) => new(items, displaySelector);

    /// <summary>
    /// Create a selection list with string items.
    /// </summary>
    /// <param name="items">The string items to display.</param>
    public static SelectionListNode<string> SelectionList(params string[] items) =>
        new(items, s => s);

    /// <summary>
    /// Create a selection list with string items from an enumerable.
    /// </summary>
    /// <param name="items">The string items to display.</param>
    public static SelectionListNode<string> SelectionList(IEnumerable<string> items) =>
        new(items, s => s);

    /// <summary>
    /// Create a file/folder picker with breadcrumb navigation and type-to-filter search.
    /// </summary>
    /// <param name="startPath">The initial directory. Defaults to <see cref="Environment.CurrentDirectory"/>.</param>
    public static FilePickerNode FilePicker(string? startPath = null) => new(startPath);

    /// <summary>
    /// Create a deferred node that delegates to a lazily-obtained node without owning it.
    /// </summary>
    /// <remarks>
    /// Use this when you need to render a node owned elsewhere (like a ViewModel) that should
    /// not be disposed when the layout changes. Useful for modals and overlays that need to
    /// be shown/hidden without being recreated.
    /// </remarks>
    /// <param name="getNode">A function that returns the node to render, or null for no content.</param>
    public static DeferredNode Deferred(Func<ILayoutNode?> getNode) => new(getNode);

    /// <summary>
    /// Create a multi-step wizard component.
    /// </summary>
    /// <typeparam name="TStep">Enum type representing the wizard steps.</typeparam>
    public static WizardNode<TStep> Wizard<TStep>() where TStep : struct, Enum => new();

    /// <summary>
    /// Create a dynamic layout node that evaluates a factory once, then only on <see cref="DynamicLayoutNode.Invalidate"/>.
    /// </summary>
    /// <remarks>
    /// For content that switches based on a key (enum, step index, tab), prefer
    /// <see cref="KeyedDynamic{TKey}(Func{TKey}, Func{TKey, ILayoutNode})"/> which provides automatic caching and state preservation.
    /// </remarks>
    /// <param name="factory">Factory that returns the current child node.</param>
    public static DynamicLayoutNode Dynamic(Func<ILayoutNode> factory) => new(factory);

    /// <summary>
    /// Create a keyed dynamic layout node that caches content by key.
    /// Navigating back to a previous key reuses the cached instance, preserving all child state.
    /// </summary>
    /// <typeparam name="TKey">The type of key used to identify content variants.</typeparam>
    /// <param name="keySelector">Function that returns the current key.</param>
    /// <param name="contentFactory">Function that creates content for a given key (called once per key).</param>
    /// <remarks>
    /// The keyed layout owns every child that <paramref name="contentFactory"/> returns.
    /// Wrap externally owned content in a <see cref="DeferredNode"/>.
    /// </remarks>
    public static KeyedDynamicLayoutNode<TKey> KeyedDynamic<TKey>(
        Func<TKey> keySelector,
        Func<TKey, ILayoutNode> contentFactory)
        where TKey : notnull => new(keySelector, contentFactory);

    /// <summary>
    /// Create a keyed dynamic layout node with an explicit cache policy.
    /// </summary>
    /// <typeparam name="TKey">The type of key that identifies content variants.</typeparam>
    /// <param name="keySelector">A function that returns the current key.</param>
    /// <param name="contentFactory">A function that creates content for a key.</param>
    /// <param name="cachePolicy">The policy that controls retention of inactive content.</param>
    /// <remarks>
    /// The keyed layout owns every child that <paramref name="contentFactory"/> returns.
    /// <see cref="KeyedDynamicCachePolicy.EvictOnKeyChange"/> requires a new child after each key change.
    /// Wrap externally owned content in a <see cref="DeferredNode"/>.
    /// </remarks>
    public static KeyedDynamicLayoutNode<TKey> KeyedDynamic<TKey>(
        Func<TKey> keySelector,
        Func<TKey, ILayoutNode> contentFactory,
        KeyedDynamicCachePolicy cachePolicy)
        where TKey : notnull => new(keySelector, contentFactory, cachePolicy);
}
