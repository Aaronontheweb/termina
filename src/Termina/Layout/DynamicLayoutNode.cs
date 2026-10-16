// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Rendering;

namespace Termina.Layout;

/// <summary>
/// A layout node that evaluates a factory function once, then re-evaluates only when
/// <see cref="Invalidate"/> is called. Uses reference equality to detect child changes —
/// same instance means no lifecycle churn.
/// </summary>
/// <remarks>
/// For content that switches based on a key (enum, step index, tab), prefer
/// <see cref="KeyedDynamicLayoutNode{TKey}"/> which provides automatic caching and state preservation.
/// </remarks>
public sealed class DynamicLayoutNode : LayoutNode, IInvalidatingNode
{
    private readonly Func<ILayoutNode> _factory;
    private readonly Subject<Unit> _invalidated = new();
    private IDisposable? _childInvalidationSubscription;
    private ILayoutNode _currentChild;
    private bool _isActive;
    private bool _needsEvaluation = true;
    private bool _isEvaluating;
    private bool _reevaluationRequested;

    // Bounds the settle loop when a factory invalidates its own node on every pass (never converges).
    internal const int MaxReevaluationPasses = 8;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <summary>
    /// Create a dynamic layout node that evaluates a factory once, then only on <see cref="Invalidate"/>.
    /// </summary>
    /// <param name="factory">Factory function that returns the current child node.</param>
    public DynamicLayoutNode(Func<ILayoutNode> factory)
    {
        _factory = factory;
        _currentChild = new EmptyNode();
    }

    /// <summary>
    /// Signal that the factory output may have changed.
    /// Eagerly re-evaluates the factory so the new child is immediately available
    /// for tree traversal (e.g., focus propagation).
    /// </summary>
    public void Invalidate()
    {
        _needsEvaluation = true;

        // A re-entrant call from inside the factory: request another pass of the current settle loop
        // instead of recursing, and do not notify the parent mid-evaluation.
        if (_isEvaluating)
        {
            _reevaluationRequested = true;
            return;
        }

        EvaluateFactory();
        _invalidated.OnNext(Unit.Default);
    }

    /// <summary>
    /// Evaluate the factory and update the child if it changed (by reference).
    /// No-op when clean (not invalidated). Re-runs the factory until it stops requesting re-evaluation
    /// (convergence), bounded by <see cref="MaxReevaluationPasses"/>, so a factory that invalidates its
    /// own node self-heals instead of blanking the content or recursing without end (#159).
    /// </summary>
    private void EvaluateFactory()
    {
        if (!_needsEvaluation)
            return;

        // Re-entrant evaluation (the factory invalidated during a Measure/Render pass): defer, do not recurse.
        if (_isEvaluating)
        {
            _reevaluationRequested = true;
            return;
        }

        _isEvaluating = true;
        try
        {
            var passes = 0;
            do
            {
                _reevaluationRequested = false;
                EvaluateFactoryOnce();
            }
            while (_reevaluationRequested && ++passes < MaxReevaluationPasses);

            if (_reevaluationRequested)
            {
                // The factory invalidated its own node on every pass and never converged. Keep the last
                // child rather than hang, and surface the mistake in debug builds.
                System.Diagnostics.Debug.WriteLine(
                    $"DynamicLayoutNode factory did not converge after {MaxReevaluationPasses} passes; " +
                    "it invalidates its own node on every evaluation. Move the side effect out of the factory.");
            }
        }
        finally
        {
            _isEvaluating = false;
        }
    }

    /// <summary>
    /// Evaluate the factory once and swap the child if it changed (by reference).
    /// </summary>
    private void EvaluateFactoryOnce()
    {
        _needsEvaluation = false;
        var newChild = _factory();

        // Same instance — no lifecycle churn needed
        if (ReferenceEquals(newChild, _currentChild))
            return;

        // Deactivate old child (active/inactive pattern — don't dispose)
        if (_isActive && _currentChild is IActivatableNode oldLayoutNode)
        {
            oldLayoutNode.OnDeactivate();
        }

        _currentChild = newChild;
        ApplyRuntimeContextToChild(newChild);
        SubscribeToChildInvalidation(newChild);

        // Activate new child if we're currently active
        if (_isActive && newChild is IActivatableNode newLayoutNode)
        {
            newLayoutNode.OnActivate();
        }

        RuntimeContext?.NotifyLayoutStructureChanged();
    }

    /// <summary>
    /// Subscribe to a child's invalidation events and propagate them upward.
    /// </summary>
    private void SubscribeToChildInvalidation(ILayoutNode child)
    {
        _childInvalidationSubscription?.Dispose();
        _childInvalidationSubscription = null;

        if (child is IInvalidatingNode invalidating)
        {
            _childInvalidationSubscription = invalidating.Invalidated
                .Subscribe(_ => _invalidated.OnNext(Unit.Default));
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
        EvaluateFactory();

        var childSize = _currentChild.Measure(available);

        var width = WidthConstraint.Compute(available.Width, childSize.Width, available.Width);
        var height = HeightConstraint.Compute(available.Height, childSize.Height, available.Height);

        return new Size(width, height);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        EvaluateFactory();
        _currentChild.Render(context, bounds);
    }

    /// <inheritdoc />
    public override void OnActivate()
    {
        _isActive = true;
        _needsEvaluation = true;

        // Re-subscribe to current child's invalidation events
        SubscribeToChildInvalidation(_currentChild);

        // Activate current child
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

        // Deactivate current child
        if (_currentChild is IActivatableNode childNode)
        {
            childNode.OnDeactivate();
        }

        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _childInvalidationSubscription?.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _currentChild.Dispose();
        base.Dispose();
    }
}
