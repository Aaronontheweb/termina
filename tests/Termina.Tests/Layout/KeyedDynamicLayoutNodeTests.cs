// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Layout;
using Termina.Rendering;

namespace Termina.Tests.Layout;

/// <summary>
/// Tests for the KeyedDynamicLayoutNode class.
/// </summary>
public class KeyedDynamicLayoutNodeTests
{
    [Fact]
    public void KeyedDynamic_FirstMeasure_EvaluatesFactory()
    {
        var currentKey = 0;
        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            key => new TextNode($"Content {key}"));

        var size = node.Measure(new Size(80, 24));

        Assert.True(size.Width > 0);
        var children = node.GetChildNodes().ToList();
        Assert.Single(children);
        Assert.IsType<TextNode>(children[0]);
    }

    [Fact]
    public void KeyedDynamic_SameKey_ReusesInstance()
    {
        var currentKey = 0;
        var factoryCallCount = 0;
        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            key =>
            {
                factoryCallCount++;
                return new TextNode($"Content {key}");
            });

        // First evaluation
        node.Measure(new Size(80, 24));
        Assert.Equal(1, factoryCallCount);
        var firstChild = node.GetChildNodes().First();

        // Invalidate with same key — should reuse cached instance
        node.Invalidate();
        Assert.Equal(1, factoryCallCount); // factory NOT called again
        var secondChild = node.GetChildNodes().First();
        Assert.Same(firstChild, secondChild);
    }

    [Fact]
    public void KeyedDynamic_DifferentKey_CreatesAndCachesNew()
    {
        var currentKey = 0;
        var factoryCallCount = 0;
        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            key =>
            {
                factoryCallCount++;
                return new TextNode($"Content {key}");
            });

        node.Measure(new Size(80, 24));
        Assert.Equal(1, factoryCallCount);

        // Switch to different key
        currentKey = 1;
        node.Invalidate();
        Assert.Equal(2, factoryCallCount);
    }

    [Fact]
    public void KeyedDynamic_ReturnToPreviousKey_ReusesCached()
    {
        var currentKey = 0;
        var factoryCallCount = 0;
        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            key =>
            {
                factoryCallCount++;
                return new SpyLifecycleNode();
            });
        node.OnActivate();

        // Create content for key 0
        node.Measure(new Size(80, 24));
        var child0 = node.GetChildNodes().First();
        Assert.Equal(1, factoryCallCount);

        // Switch to key 1
        currentKey = 1;
        node.Invalidate();
        Assert.Equal(2, factoryCallCount);

        // Return to key 0 — should reuse cached instance
        currentKey = 0;
        node.Invalidate();
        Assert.Equal(2, factoryCallCount); // factory NOT called again
        Assert.Same(child0, node.GetChildNodes().First());
    }

    [Fact]
    public void KeyedDynamic_ChildChange_LifecycleManaged()
    {
        var currentKey = 0;
        SpyLifecycleNode? child0 = null;
        SpyLifecycleNode? child1 = null;

        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            key =>
            {
                var child = new SpyLifecycleNode();
                if (key == 0) child0 = child;
                else child1 = child;
                return child;
            });
        node.OnActivate();

        // First evaluation activates child0
        node.Measure(new Size(80, 24));
        Assert.NotNull(child0);
        Assert.Equal(1, child0!.ActivateCount);

        // Switch to key 1 — child0 deactivated, child1 activated
        currentKey = 1;
        node.Invalidate();

        Assert.Equal(1, child0.DeactivateCount);
        Assert.NotNull(child1);
        Assert.Equal(1, child1!.ActivateCount);
    }

    [Fact]
    public void KeyedDynamic_ChildInvalidation_PropagatesUpward()
    {
        var childSubject = new Subject<Unit>();
        var child = new SpyInvalidatingNode(childSubject);
        var currentKey = 0;

        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            _ => child);
        node.OnActivate();

        var parentInvalidations = 0;
        node.Invalidated.Subscribe(_ => parentInvalidations++);

        // Evaluate factory so child gets subscribed
        node.Measure(new Size(80, 24));

        childSubject.OnNext(Unit.Default);

        Assert.Equal(1, parentInvalidations);
    }

    [Fact]
    public void KeyedDynamic_Dispose_DisposesAllCachedChildren()
    {
        var currentKey = 0;
        var children = new List<SpyLifecycleNode>();

        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            key =>
            {
                var child = new SpyLifecycleNode();
                children.Add(child);
                return child;
            });

        // Create content for key 0 and 1
        node.Measure(new Size(80, 24));
        currentKey = 1;
        node.Invalidate();

        Assert.Equal(2, children.Count);

        node.Dispose();

        Assert.All(children, c => Assert.True(c.WasDisposed));
    }

    [Fact]
    public void KeyedDynamic_Invalidate_EagerlyEvaluates()
    {
        var currentKey = 0;
        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            key => new TextNode($"Content {key}"));

        // First Measure
        node.Measure(new Size(80, 24));
        var firstChild = node.GetChildNodes().First();

        // Switch key and Invalidate — child swaps immediately (before Measure/Render)
        currentKey = 1;
        node.Invalidate();
        var secondChild = node.GetChildNodes().First();

        Assert.NotSame(firstChild, secondChild);
    }

    [Fact]
    public void KeyedDynamic_FluentSizing_Works()
    {
        var node = new KeyedDynamicLayoutNode<int>(
            () => 0,
            _ => new EmptyNode());

        var result = node.Width(40).Height(10);

        Assert.IsType<SizeConstraint.Fixed>(result.WidthConstraint);
        Assert.IsType<SizeConstraint.Fixed>(result.HeightConstraint);
    }

    [Fact]
    public void EvictOnKeyChange_SameKeyPreservesTextInputState()
    {
        var node = new KeyedDynamicLayoutNode<int>(
            () => 0,
            _ => new TextInputNode(),
            KeyedDynamicCachePolicy.EvictOnKeyChange);
        node.Measure(new Size(80, 24));
        var input = Assert.IsType<TextInputNode>(node.GetChildNodes().Single());
        input.HandleInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        input.HandleInput(new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));

        node.Invalidate();

        var preservedInput = Assert.IsType<TextInputNode>(node.GetChildNodes().Single());
        Assert.Same(input, preservedInput);
        Assert.Equal("ab", preservedInput.Text);
    }

    [Fact]
    public void EvictOnKeyChange_ReturnToPriorKeyCreatesFreshContent()
    {
        var currentKey = 0;
        var factoryCallCount = 0;
        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            _ =>
            {
                factoryCallCount++;
                return new SpyLifecycleNode();
            },
            KeyedDynamicCachePolicy.EvictOnKeyChange);

        node.Measure(new Size(80, 24));
        var firstChild = node.GetChildNodes().Single();
        currentKey = 1;
        node.Invalidate();
        currentKey = 0;
        node.Invalidate();

        Assert.Equal(3, factoryCallCount);
        Assert.NotSame(firstChild, node.GetChildNodes().Single());
    }

    [Fact]
    public void EvictOnKeyChange_DisposesRetiredContentOnTheNextLayoutPass()
    {
        var currentKey = 0;
        var children = new List<SpyLifecycleNode>();
        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            _ =>
            {
                var child = new SpyLifecycleNode();
                children.Add(child);
                return child;
            },
            KeyedDynamicCachePolicy.EvictOnKeyChange);
        node.OnActivate();
        node.Measure(new Size(80, 24));

        currentKey = 1;
        node.Invalidate();

        Assert.Equal(1, children[0].DeactivateCount);
        Assert.False(children[0].WasDisposed);
        Assert.Equal(1, children[1].ActivateCount);

        node.Measure(new Size(80, 24));

        Assert.True(children[0].WasDisposed);
        Assert.False(children[1].WasDisposed);

        node.Dispose();

        Assert.True(children[1].WasDisposed);
    }

    [Fact]
    public void LayoutsFactorySupportsEvictOnKeyChangePolicy()
    {
        var node = Layouts.KeyedDynamic(
            () => 0,
            _ => new EmptyNode(),
            KeyedDynamicCachePolicy.EvictOnKeyChange);

        Assert.IsType<KeyedDynamicLayoutNode<int>>(node);
    }

    [Fact]
    public void EvictOnKeyChange_ReusedFactoryChildThrows()
    {
        var currentKey = 0;
        var sharedChild = new EmptyNode();
        var node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            _ => sharedChild,
            KeyedDynamicCachePolicy.EvictOnKeyChange);

        node.Measure(new Size(80, 24));
        currentKey = 1;

        var exception = Assert.Throws<InvalidOperationException>(node.Invalidate);

        Assert.Contains("Return a new child", exception.Message);
    }

    [Theory]
    [InlineData(KeyedDynamicCachePolicy.RetainAll)]
    [InlineData(KeyedDynamicCachePolicy.EvictOnKeyChange)]
    public void NullKeyThrowsForEveryCachePolicy(KeyedDynamicCachePolicy cachePolicy)
    {
        string? currentKey = null;
        var node = new KeyedDynamicLayoutNode<string>(
            () => currentKey!,
            _ => new EmptyNode(),
            cachePolicy);

        var exception = Assert.Throws<InvalidOperationException>(() => node.Measure(new Size(80, 24)));

        Assert.Equal("The key selector returned null.", exception.Message);
    }

    [Fact]
    public void EvictOnKeyChange_DisposalCanInvalidateWithoutMutatingTheActiveDisposalBatch()
    {
        var currentKey = 0;
        var children = new Dictionary<int, SpyLifecycleNode>();
        KeyedDynamicLayoutNode<int> node = null!;
        node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            key =>
            {
                var child = new SpyLifecycleNode(key == 0
                    ? () =>
                    {
                        currentKey = 2;
                        node.Invalidate();
                    }
                : null);
                children.Add(key, child);
                return child;
            },
            KeyedDynamicCachePolicy.EvictOnKeyChange);

        node.Measure(new Size(80, 24));
        node.Measure(new Size(80, 24));
        currentKey = 1;
        node.Invalidate();

        node.Measure(new Size(80, 24));

        Assert.True(children[0].WasDisposed);
        Assert.False(children[1].WasDisposed);
        Assert.Same(children[2], node.GetChildNodes().Single());

        node.Measure(new Size(80, 24));

        Assert.True(children[1].WasDisposed);
    }

    [Fact]
    public void EvictOnKeyChange_TeardownIgnoresReentrantInvalidationAndDisposesAllChildren()
    {
        var currentKey = 0;
        var children = new Dictionary<int, SpyLifecycleNode>();
        KeyedDynamicLayoutNode<int> node = null!;
        node = new KeyedDynamicLayoutNode<int>(
            () => currentKey,
            key =>
            {
                var child = new SpyLifecycleNode(key == 0 ? node.Invalidate : null);
                children.Add(key, child);
                return child;
            },
            KeyedDynamicCachePolicy.EvictOnKeyChange);

        node.Measure(new Size(80, 24));
        node.Measure(new Size(80, 24));
        currentKey = 1;
        node.Invalidate();

        node.Dispose();

        Assert.True(children[0].WasDisposed);
        Assert.True(children[1].WasDisposed);
    }

    [Fact]
    public void ConstructorRejectsAnUnknownCachePolicy()
    {
        var unknownPolicy = (KeyedDynamicCachePolicy)int.MaxValue;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KeyedDynamicLayoutNode<int>(() => 0, _ => new EmptyNode(), unknownPolicy));

        Assert.Equal("cachePolicy", exception.ParamName);
    }

    #region Test helpers

    private class SpyLifecycleNode : LayoutNode
    {
        private readonly Action? _onDispose;

        public SpyLifecycleNode(Action? onDispose = null)
        {
            _onDispose = onDispose;
        }

        public int ActivateCount { get; private set; }
        public int DeactivateCount { get; private set; }
        public bool WasDisposed { get; private set; }

        public override void OnActivate()
        {
            ActivateCount++;
            base.OnActivate();
        }

        public override void OnDeactivate()
        {
            DeactivateCount++;
            base.OnDeactivate();
        }

        public override void Dispose()
        {
            WasDisposed = true;
            _onDispose?.Invoke();
            base.Dispose();
        }

        public override Size Measure(Size available) => new(10, 1);
        public override void Render(IRenderContext context, Rect bounds) { }
    }

    private class SpyInvalidatingNode : LayoutNode, IInvalidatingNode
    {
        public Observable<Unit> Invalidated { get; }

        public SpyInvalidatingNode(Observable<Unit> invalidated)
        {
            Invalidated = invalidated;
        }

        public override Size Measure(Size available) => new(10, 1);
        public override void Render(IRenderContext context, Rect bounds) { }
    }

    #endregion
}
