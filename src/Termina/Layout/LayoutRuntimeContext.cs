// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;

namespace Termina.Layout;

/// <summary>
/// Runtime services supplied by the owning Termina application to layout nodes.
/// </summary>
public sealed class LayoutRuntimeContext
{
    /// <summary>
    /// Creates a new layout runtime context.
    /// </summary>
    public LayoutRuntimeContext(
        FrameProvider renderFrameProvider,
        TimeProvider timeProvider,
        Action requestRedraw)
        : this(renderFrameProvider, timeProvider, requestRedraw, static () => { })
    {
    }

    /// <summary>
    /// Creates a new layout runtime context with a structural-change callback.
    /// </summary>
    public LayoutRuntimeContext(
        FrameProvider renderFrameProvider,
        TimeProvider timeProvider,
        Action requestRedraw,
        Action notifyLayoutStructureChanged)
    {
        ArgumentNullException.ThrowIfNull(renderFrameProvider);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(requestRedraw);
        ArgumentNullException.ThrowIfNull(notifyLayoutStructureChanged);

        RenderFrameProvider = renderFrameProvider;
        TimeProvider = timeProvider;
        RequestRedraw = requestRedraw;
        NotifyLayoutStructureChanged = notifyLayoutStructureChanged;
    }

    /// <summary>
    /// R3 frame provider bound to the owning Termina application render loop.
    /// </summary>
    public FrameProvider RenderFrameProvider { get; }

    /// <summary>
    /// Time provider configured for the owning Termina application.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// Requests a redraw from the owning Termina application.
    /// </summary>
    public Action RequestRedraw { get; }

    /// <summary>
    /// Notifies the owning page that the set of child nodes changed.
    /// </summary>
    public Action NotifyLayoutStructureChanged { get; }
}

/// <summary>
/// Implemented by layout nodes that receive runtime services from the owning Termina application.
/// </summary>
public interface ILayoutRuntimeContextAware
{
    /// <summary>
    /// Supplies runtime services before the node is activated.
    /// </summary>
    void SetRuntimeContext(LayoutRuntimeContext context);
}

internal static class LayoutRuntimeContextInjector
{
    public static void Apply(ILayoutNode root, LayoutRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(context);

        var visited = new HashSet<ILayoutNode>();
        var stack = new Stack<ILayoutNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
                continue;

            if (current is ILayoutRuntimeContextAware aware)
                aware.SetRuntimeContext(context);

            foreach (var child in GetChildNodes(current))
                stack.Push(child);
        }
    }

    internal static IEnumerable<ILayoutNode> GetChildNodes(ILayoutNode node) => node switch
    {
        LayoutNode layoutNode => layoutNode.GetChildNodes(),
        IContainerNode containerNode => containerNode.Children,
        _ => []
    };
}
