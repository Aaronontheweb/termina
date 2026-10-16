// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Rendering;

namespace Termina.Layout;

/// <summary>
/// Base class for layout nodes providing common functionality.
/// </summary>
public abstract class LayoutNode : ILayoutNode, IActivatableNode, ILayoutRuntimeContextAware
{
    private SizeConstraint _widthConstraint = new SizeConstraint.Auto();
    private SizeConstraint _heightConstraint = new SizeConstraint.Auto();

    /// <summary>
    /// Runtime services supplied by the owning Termina application.
    /// Available after the layout tree is built and before activation.
    /// </summary>
    protected LayoutRuntimeContext? RuntimeContext { get; private set; }

    /// <inheritdoc />
    public SizeConstraint WidthConstraint
    {
        get => _widthConstraint;
        protected set => _widthConstraint = value;
    }

    /// <inheritdoc />
    public SizeConstraint HeightConstraint
    {
        get => _heightConstraint;
        protected set => _heightConstraint = value;
    }

    /// <inheritdoc />
    public abstract Size Measure(Size available);

    /// <inheritdoc />
    public abstract void Render(IRenderContext context, Rect bounds);

    /// <inheritdoc />
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public virtual void SetRuntimeContext(LayoutRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeContext = context;
    }

    /// <summary>
    /// Applies this node's runtime context to a child created after the initial layout tree walk.
    /// </summary>
    protected void ApplyRuntimeContextToChild(ILayoutNode child)
    {
        if (RuntimeContext is { } context)
            LayoutRuntimeContextInjector.Apply(child, context);
    }

    /// <summary>
    /// Called when the node becomes active (page navigated to).
    /// Override to resume subscriptions, start timers, and restore active state.
    /// </summary>
    /// <remarks>
    /// Default implementation does nothing. Override in derived classes that need
    /// to resume resource-consuming operations when the page becomes active.
    /// </remarks>
    public virtual void OnActivate()
    {
        // Default: no action needed
    }

    /// <summary>
    /// Called when the node becomes inactive (navigating away from page).
    /// Override to pause subscriptions, stop timers, but preserve state.
    /// </summary>
    /// <remarks>
    /// Default implementation does nothing. Override in derived classes that need
    /// to pause resource-consuming operations when the page becomes inactive.
    /// </remarks>
    public virtual void OnDeactivate()
    {
        // Default: no action needed
    }

    /// <summary>
    /// Gets the child nodes of this layout node for tree traversal.
    /// Override in nodes that wrap or contain children.
    /// </summary>
    internal virtual IEnumerable<ILayoutNode> GetChildNodes() => [];

    /// <summary>
    /// Disconnect subscriptions from this node to its children without
    /// disposing or mutating the children themselves.
    /// </summary>
    internal virtual void DisconnectChildInvalidationSubscriptions()
    {
    }

    /// <summary>
    /// Set width to a fixed value.
    /// </summary>
    public LayoutNode Width(int value)
    {
        _widthConstraint = new SizeConstraint.Fixed(value);
        return this;
    }

    /// <summary>
    /// Set width to fill remaining space.
    /// </summary>
    public LayoutNode WidthFill(int weight = 1)
    {
        _widthConstraint = new SizeConstraint.Fill { Weight = weight };
        return this;
    }

    /// <summary>
    /// Set width to auto-size based on content.
    /// </summary>
    public LayoutNode WidthAuto(int min = 0, int max = int.MaxValue)
    {
        _widthConstraint = new SizeConstraint.Auto { Min = min, Max = max };
        return this;
    }

    /// <summary>
    /// Set width to a percentage.
    /// </summary>
    public LayoutNode WidthPercent(int value)
    {
        _widthConstraint = new SizeConstraint.Percent(value);
        return this;
    }

    /// <summary>
    /// Set height to a fixed value.
    /// </summary>
    public LayoutNode Height(int value)
    {
        _heightConstraint = new SizeConstraint.Fixed(value);
        return this;
    }

    /// <summary>
    /// Set height to fill remaining space.
    /// </summary>
    public LayoutNode Fill(int weight = 1)
    {
        _heightConstraint = new SizeConstraint.Fill { Weight = weight };
        return this;
    }

    /// <summary>
    /// Set height to auto-size based on content.
    /// </summary>
    public LayoutNode HeightAuto(int min = 0, int max = int.MaxValue)
    {
        _heightConstraint = new SizeConstraint.Auto { Min = min, Max = max };
        return this;
    }

    /// <summary>
    /// Set height to a percentage.
    /// </summary>
    public LayoutNode HeightPercent(int value)
    {
        _heightConstraint = new SizeConstraint.Percent(value);
        return this;
    }
}

/// <summary>
/// Base class for container nodes that arrange children.
/// </summary>
public abstract class ContainerNode : LayoutNode, IContainerNode, IInvalidatingNode
{
    private readonly List<ILayoutNode> _children = new();
    private readonly List<IDisposable> _childInvalidationSubscriptions = new();
    private readonly Subject<Unit> _invalidated = new();
    private int _disposeState;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <inheritdoc />
    public IReadOnlyList<ILayoutNode> Children => _children;

    /// <inheritdoc />
    internal override IEnumerable<ILayoutNode> GetChildNodes() => _children;

    /// <summary>
    /// Add a child node.
    /// </summary>
    protected void AddChild(ILayoutNode child)
    {
        ApplyRuntimeContextToChild(child);
        _children.Add(child);
        SubscribeToChildInvalidation(child);
    }

    /// <summary>
    /// Add multiple child nodes.
    /// </summary>
    protected void AddChildren(IEnumerable<ILayoutNode> children)
    {
        foreach (var child in children)
        {
            ApplyRuntimeContextToChild(child);
            _children.Add(child);
            SubscribeToChildInvalidation(child);
        }
    }

    /// <summary>
    /// Subscribe to a child's invalidation events and propagate them upward.
    /// </summary>
    private void SubscribeToChildInvalidation(ILayoutNode child)
    {
        if (child is IInvalidatingNode invalidating)
        {
            var subscription = invalidating.Invalidated.Subscribe(_ => _invalidated.OnNext(Unit.Default));
            _childInvalidationSubscriptions.Add(subscription);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        // Layout teardown can race with a render pass that retires the same container.
        // Claim disposal atomically before completing the R3 subject; Subject.Dispose()
        // deliberately throws if OnCompleted() is attempted a second time.
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        // Dispose child invalidation subscriptions
        foreach (var subscription in _childInvalidationSubscriptions)
        {
            subscription.Dispose();
        }
        _childInvalidationSubscriptions.Clear();

        // Complete and dispose our invalidation subject
        _invalidated.OnCompleted();
        _invalidated.Dispose();

        // Dispose children
        foreach (var child in _children)
        {
            child.Dispose();
        }
        base.Dispose();
    }

    /// <inheritdoc />
    internal override void DisconnectChildInvalidationSubscriptions()
    {
        foreach (var subscription in _childInvalidationSubscriptions)
        {
            subscription.Dispose();
        }

        _childInvalidationSubscriptions.Clear();
    }

    /// <summary>
    /// Activates this container and all children.
    /// </summary>
    public override void OnActivate()
    {
        foreach (var child in _children)
        {
            if (child is IActivatableNode node)
                node.OnActivate();
        }
        base.OnActivate();
    }

    /// <summary>
    /// Deactivates this container and all children.
    /// </summary>
    public override void OnDeactivate()
    {
        foreach (var child in _children)
        {
            if (child is IActivatableNode node)
                node.OnDeactivate();
        }
        base.OnDeactivate();
    }
}
