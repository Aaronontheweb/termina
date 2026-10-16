// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using System.Runtime.CompilerServices;
using Termina.Rendering;

namespace Termina.Layout;

/// <summary>
/// Defines how a keyed dynamic layout retains inactive content.
/// </summary>
public enum KeyedDynamicCachePolicy
{
    /// <summary>
    /// Retain one child for every visited key until the keyed layout is disposed.
    /// A return to a key reuses the same child and its state.
    /// </summary>
    RetainAll,

    /// <summary>
    /// Evict the active child when the selected key changes.
    /// A return to a prior key creates a new child with fresh state.
    /// </summary>
    EvictOnKeyChange
}

/// <summary>
/// A dynamic layout node that preserves content while a key stays active.
/// Its cache policy controls retention of inactive content.
/// </summary>
/// <typeparam name="TKey">The type of key used to identify content variants.</typeparam>
/// <remarks>
/// The default policy retains a child for every visited key.
/// <see cref="KeyedDynamicCachePolicy.EvictOnKeyChange"/> creates a new child after each key transition.
/// Use <see cref="Layouts.KeyedDynamic{TKey}(Func{TKey}, Func{TKey, ILayoutNode})"/> to create instances.
/// </remarks>
public sealed class KeyedDynamicLayoutNode<TKey> : LayoutNode, IInvalidatingNode
    where TKey : notnull
{
    private sealed class SeenChildMarker
    {
        public static SeenChildMarker Instance { get; } = new();

        private SeenChildMarker()
        {
        }
    }

    private readonly Func<TKey> _keySelector;
    private readonly Func<TKey, ILayoutNode> _contentFactory;
    private readonly KeyedDynamicCachePolicy _cachePolicy;
    private readonly Dictionary<TKey, ILayoutNode> _cache = new();

    // Weak keys let retired children be collected after disposal. The set still rejects an instance
    // that the factory retains and returns again.
    private readonly ConditionalWeakTable<ILayoutNode, SeenChildMarker> _seenEvictionChildren = new();
    private readonly List<ILayoutNode> _retiredChildren = [];
    private readonly Subject<Unit> _invalidated = new();
    private IDisposable? _childInvalidationSubscription;
    private ILayoutNode _currentChild;
    private TKey? _currentKey;
    private bool _hasCurrentKey;
    private bool _isActive;
    private bool _needsEvaluation = true;
    private bool _isEvaluating;
    private bool _reevaluationRequested;
    private bool _disposed;

    // Bounds the settle loop when the key selector or content factory invalidates its own node on every
    // pass (never converges).
    internal const int MaxReevaluationPasses = 8;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <summary>
    /// Create a keyed dynamic layout node.
    /// </summary>
    /// <param name="keySelector">Function that returns the current key.</param>
    /// <param name="contentFactory">Function that creates content for a given key (called once per key).</param>
    public KeyedDynamicLayoutNode(Func<TKey> keySelector, Func<TKey, ILayoutNode> contentFactory)
        : this(keySelector, contentFactory, KeyedDynamicCachePolicy.RetainAll)
    {
    }

    /// <summary>
    /// Create a keyed dynamic layout node with an explicit cache policy.
    /// </summary>
    /// <param name="keySelector">Function that returns the current key.</param>
    /// <param name="contentFactory">Function that creates content for a key.</param>
    /// <param name="cachePolicy">The policy that controls retention of inactive content.</param>
    /// <remarks>
    /// <see cref="KeyedDynamicCachePolicy.EvictOnKeyChange"/> transfers ownership of each returned child
    /// to this layout. The factory must return a new child after each key change.
    /// Wrap externally owned content in a <see cref="DeferredNode"/>.
    /// </remarks>
    public KeyedDynamicLayoutNode(
        Func<TKey> keySelector,
        Func<TKey, ILayoutNode> contentFactory,
        KeyedDynamicCachePolicy cachePolicy)
    {
        if (!Enum.IsDefined(cachePolicy))
            throw new ArgumentOutOfRangeException(nameof(cachePolicy), cachePolicy, "The cache policy is not valid.");

        _keySelector = keySelector;
        _contentFactory = contentFactory;
        _cachePolicy = cachePolicy;
        _currentChild = new EmptyNode();
    }

    /// <summary>
    /// Signal that the key may have changed.
    /// Eagerly re-evaluates so the new child is immediately available
    /// for tree traversal (e.g., focus propagation).
    /// </summary>
    public void Invalidate()
    {
        if (_disposed)
            return;

        _needsEvaluation = true;

        // A re-entrant call from inside the key selector or content factory: request another pass of the
        // current settle loop instead of recursing, and do not notify the parent mid-evaluation.
        if (_isEvaluating)
        {
            _reevaluationRequested = true;
            return;
        }

        EvaluateFactory();
        _invalidated.OnNext(Unit.Default);
    }

    /// <summary>
    /// Evaluate the key selector, look up or create content, and swap child if changed.
    /// No-op when clean (not invalidated). Re-runs until the selector/factory stops requesting
    /// re-evaluation (convergence), bounded by <see cref="MaxReevaluationPasses"/>, so a selector or
    /// factory that invalidates its own node self-heals instead of recursing without end (#159).
    /// </summary>
    private void EvaluateFactory()
    {
        if (!_needsEvaluation)
            return;

        // Re-entrant evaluation (invalidated during a Measure/Render pass): defer, do not recurse.
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
                // The selector/factory invalidated its own node on every pass and never converged. Keep the
                // last child rather than hang, and surface the mistake in debug builds.
                System.Diagnostics.Debug.WriteLine(
                    $"KeyedDynamicLayoutNode did not converge after {MaxReevaluationPasses} passes; the key " +
                    "selector or content factory invalidates its own node on every evaluation. Move the side " +
                    "effect out of the selector/factory.");
            }
        }
        finally
        {
            _isEvaluating = false;
        }
    }

    /// <summary>
    /// Evaluate the key selector once, look up or create content, and swap the child if it changed.
    /// </summary>
    private void EvaluateFactoryOnce()
    {
        _needsEvaluation = false;
        var key = _keySelector();
        if (key is null)
            throw new InvalidOperationException("The key selector returned null.");

        if (_cachePolicy == KeyedDynamicCachePolicy.EvictOnKeyChange
            && _hasCurrentKey
            && EqualityComparer<TKey>.Default.Equals(key, _currentKey))
            return;

        ILayoutNode newChild;
        if (_cachePolicy == KeyedDynamicCachePolicy.RetainAll)
        {
            if (_cache.TryGetValue(key, out var cachedChild))
            {
                newChild = cachedChild;
            }
            else
            {
                newChild = _contentFactory(key)
                    ?? throw new InvalidOperationException("The content factory returned null.");
                _cache[key] = newChild;
            }
        }
        else
        {
            newChild = _contentFactory(key)
                ?? throw new InvalidOperationException("The content factory returned null.");
            if (_seenEvictionChildren.TryGetValue(newChild, out _))
            {
                throw new InvalidOperationException(
                    "The content factory reused a child with EvictOnKeyChange. Return a new child after each key change.");
            }

            _seenEvictionChildren.Add(newChild, SeenChildMarker.Instance);
        }

        _currentKey = key;
        _hasCurrentKey = true;

        // Same instance — no lifecycle churn needed
        if (ReferenceEquals(newChild, _currentChild))
            return;

        // Deactivate old child
        if (_isActive && _currentChild is IActivatableNode oldLayoutNode)
        {
            oldLayoutNode.OnDeactivate();
        }

        // Invalidate can run inside the old child's input callback. Defer disposal until the next
        // layout pass, after input dispatch completes.
        if (_cachePolicy == KeyedDynamicCachePolicy.EvictOnKeyChange)
            _retiredChildren.Add(_currentChild);

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
        DisposeRetiredChildren();
        EvaluateFactory();

        var childSize = _currentChild.Measure(available);

        var width = WidthConstraint.Compute(available.Width, childSize.Width, available.Width);
        var height = HeightConstraint.Compute(available.Height, childSize.Height, available.Height);

        return new Size(width, height);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        DisposeRetiredChildren();
        EvaluateFactory();
        _currentChild.Render(context, bounds);
    }

    private void DisposeRetiredChildren()
    {
        if (_retiredChildren.Count == 0)
            return;

        var retiredChildren = _retiredChildren.ToArray();
        _retiredChildren.Clear();

        foreach (var child in retiredChildren)
        {
            if (!ReferenceEquals(child, _currentChild))
                child.Dispose();
        }
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
        if (_disposed)
            return;

        _disposed = true;
        _childInvalidationSubscription?.Dispose();

        DisposeRetiredChildren();

        // Check if _currentChild is in the cache (it won't be if factory was never evaluated)
        var currentChildInCache = _cache.Values.Any(c => ReferenceEquals(c, _currentChild));

        // Dispose ALL cached children
        foreach (var child in _cache.Values)
        {
            child.Dispose();
        }
        _cache.Clear();

        // Dispose the initial EmptyNode if it was never replaced by a cached entry
        if (!currentChildInCache)
        {
            _currentChild.Dispose();
        }

        _invalidated.OnCompleted();
        _invalidated.Dispose();

        base.Dispose();
    }
}
