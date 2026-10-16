// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Rendering;

namespace Termina.Layout;

/// <summary>
/// A layout node that updates its content based on an observable stream.
/// When the observable emits, the child node is replaced and invalidation is signaled.
/// </summary>
public sealed class ReactiveLayoutNode : LayoutNode, IInvalidatingNode
{
    private readonly Observable<ILayoutNode> _source;
    private IDisposable? _subscription;
    private IDisposable? _childInvalidationSubscription;
    private readonly Subject<Unit> _invalidated = new();
    private ILayoutNode _currentChild;
    private Size _lastMeasuredSize;
    private bool _isActive = false;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <summary>
    /// Create a reactive layout node from an observable of layout nodes.
    /// </summary>
    public ReactiveLayoutNode(Observable<ILayoutNode> source, ILayoutNode? initialChild = null)
    {
        _source = source;
        _currentChild = initialChild ?? new EmptyNode();
        SubscribeToChildInvalidation(_currentChild);

        _subscription = source.Subscribe(node =>
            {
                // Deactivate old child instead of disposing (active/inactive pattern)
                if (_currentChild is IActivatableNode oldChild)
                {
                    oldChild.OnDeactivate();
                }
                _currentChild = node;
                ApplyRuntimeContextToChild(node);
                SubscribeToChildInvalidation(node);

                // Activate the new child if we're currently active
                if (_isActive && node is IActivatableNode newChildNode)
                {
                    newChildNode.OnActivate();
                }

                RuntimeContext?.NotifyLayoutStructureChanged();
                _invalidated.OnNext(Unit.Default);
            });
    }

    /// <summary>
    /// Subscribe to a child's invalidation events and propagate them upward.
    /// </summary>
    private void SubscribeToChildInvalidation(ILayoutNode child)
    {
        // Dispose previous child invalidation subscription
        _childInvalidationSubscription?.Dispose();
        _childInvalidationSubscription = null;

        if (child is IInvalidatingNode invalidating)
        {
            _childInvalidationSubscription = invalidating.Invalidated.Subscribe(_ => _invalidated.OnNext(Unit.Default));
        }
    }

    /// <inheritdoc />
    internal override IEnumerable<ILayoutNode> GetChildNodes() => [_currentChild];

    /// <inheritdoc />
    internal override void DisconnectChildInvalidationSubscriptions()
    {
        _childInvalidationSubscription?.Dispose();
        _childInvalidationSubscription = null;
    }

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        var childSize = _currentChild.Measure(available);

        // Apply our own constraints
        var width = WidthConstraint.Compute(available.Width, childSize.Width, available.Width);
        var height = HeightConstraint.Compute(available.Height, childSize.Height, available.Height);

        _lastMeasuredSize = new Size(width, height);
        return _lastMeasuredSize;
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        _currentChild.Render(context, bounds);
    }

    /// <inheritdoc />
    public override void OnActivate()
    {
        _isActive = true;

        // If subscription was disposed during deactivation, recreate it
        if (_subscription == null)
        {
            _subscription = _source.Subscribe(node =>
                {
                    // Deactivate old child instead of disposing (active/inactive pattern)
                    if (_currentChild is IActivatableNode oldChild)
                    {
                        oldChild.OnDeactivate();
                    }
                    _currentChild = node;
                    ApplyRuntimeContextToChild(node);
                    SubscribeToChildInvalidation(node);

                    // Activate the new child if we're currently active
                    if (_isActive && node is IActivatableNode newChildNode)
                    {
                        newChildNode.OnActivate();
                    }

                    RuntimeContext?.NotifyLayoutStructureChanged();
                    _invalidated.OnNext(Unit.Default);
                });
        }

        // Re-subscribe to current child's invalidation events (might have been disposed)
        SubscribeToChildInvalidation(_currentChild);

        // Activate current child if it's a LayoutNode
        if (_currentChild is IActivatableNode childNode)
        {
            childNode.OnActivate();
        }

        base.OnActivate();
    }

    /// <inheritdoc />
    public override void OnDeactivate()
    {
        _isActive = false;

        // Deactivate current child if it's a LayoutNode
        if (_currentChild is IActivatableNode childNode)
        {
            childNode.OnDeactivate();
        }

        // Dispose subscription to pause updates
        _subscription?.Dispose();
        _subscription = null;

        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _subscription?.Dispose();
        _childInvalidationSubscription?.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _currentChild.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// A layout node that updates based on an observable of values, transformed to layout nodes.
/// </summary>
public sealed class ReactiveLayoutNode<T> : LayoutNode, IInvalidatingNode
{
    private readonly Observable<T> _source;
    private readonly Func<T, ILayoutNode> _transform;
    private IDisposable? _subscription;
    private IDisposable? _childInvalidationSubscription;
    private readonly Subject<Unit> _invalidated = new();
    private ILayoutNode _currentChild;
    private bool _isActive = false;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <summary>
    /// Create a reactive layout node from an observable with a transform function.
    /// </summary>
    public ReactiveLayoutNode(Observable<T> source, Func<T, ILayoutNode> transform)
    {
        _source = source;
        _transform = transform;
        _currentChild = new EmptyNode();

        _subscription = source.Subscribe(value =>
            {
                // Deactivate old child instead of disposing (active/inactive pattern)
                if (_currentChild is IActivatableNode oldChild)
                {
                    oldChild.OnDeactivate();
                }
                _currentChild = _transform(value);
                ApplyRuntimeContextToChild(_currentChild);
                SubscribeToChildInvalidation(_currentChild);

                // Activate the new child if we're currently active
                if (_isActive && _currentChild is IActivatableNode newChildNode)
                {
                    newChildNode.OnActivate();
                }

                RuntimeContext?.NotifyLayoutStructureChanged();
                _invalidated.OnNext(Unit.Default);
            });
    }

    /// <summary>
    /// Subscribe to a child's invalidation events and propagate them upward.
    /// </summary>
    private void SubscribeToChildInvalidation(ILayoutNode child)
    {
        // Dispose previous child invalidation subscription
        _childInvalidationSubscription?.Dispose();
        _childInvalidationSubscription = null;

        if (child is IInvalidatingNode invalidating)
        {
            _childInvalidationSubscription = invalidating.Invalidated.Subscribe(_ => _invalidated.OnNext(Unit.Default));
        }
    }

    /// <inheritdoc />
    internal override IEnumerable<ILayoutNode> GetChildNodes() => [_currentChild];

    /// <inheritdoc />
    internal override void DisconnectChildInvalidationSubscriptions()
    {
        _childInvalidationSubscription?.Dispose();
        _childInvalidationSubscription = null;
    }

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        var childSize = _currentChild.Measure(available);

        var width = WidthConstraint.Compute(available.Width, childSize.Width, available.Width);
        var height = HeightConstraint.Compute(available.Height, childSize.Height, available.Height);

        return new Size(width, height);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        _currentChild.Render(context, bounds);
    }

    /// <inheritdoc />
    public override void OnActivate()
    {
        _isActive = true;

        // If subscription was disposed during deactivation, recreate it
        if (_subscription == null)
        {
            _subscription = _source.Subscribe(value =>
                {
                    // Deactivate old child instead of disposing (active/inactive pattern)
                    if (_currentChild is IActivatableNode oldChild)
                    {
                        oldChild.OnDeactivate();
                    }
                    _currentChild = _transform(value);
                    ApplyRuntimeContextToChild(_currentChild);
                    SubscribeToChildInvalidation(_currentChild);

                    // Activate the new child if we're currently active
                    if (_isActive && _currentChild is IActivatableNode newChildNode)
                    {
                        newChildNode.OnActivate();
                    }

                    RuntimeContext?.NotifyLayoutStructureChanged();
                    _invalidated.OnNext(Unit.Default);
                });
        }

        // Re-subscribe to current child's invalidation events (might have been disposed)
        SubscribeToChildInvalidation(_currentChild);

        // Activate current child if it's a LayoutNode
        if (_currentChild is IActivatableNode childNode)
        {
            childNode.OnActivate();
        }

        base.OnActivate();
    }

    /// <inheritdoc />
    public override void OnDeactivate()
    {
        _isActive = false;

        // Deactivate current child if it's a LayoutNode
        if (_currentChild is IActivatableNode childNode)
        {
            childNode.OnDeactivate();
        }

        // Dispose subscription to pause updates
        _subscription?.Dispose();
        _subscription = null;

        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _subscription?.Dispose();
        _childInvalidationSubscription?.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _currentChild.Dispose();
        base.Dispose();
    }
}
