// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Time.Testing;
using R3;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;

namespace Termina.Tests.Layout;

public class LayoutRuntimeContextTests
{
    [Fact]
    public void Apply_SuppliesContextBeforeActivation()
    {
        var context = CreateContext();
        var node = new RecordingNode();

        LayoutRuntimeContextInjector.Apply(node, context);
        node.OnActivate();

        Assert.Same(context, node.Context);
        Assert.True(node.HadContextOnActivate);
    }

    [Fact]
    public void Apply_PropagatesContextToNestedChildren()
    {
        var context = CreateContext();
        var child = new RecordingNode();
        using var root = Layouts.Vertical()
            .WithChild(Layouts.Horizontal().WithChild(child));

        LayoutRuntimeContextInjector.Apply(root, context);

        Assert.Same(context, child.Context);
    }

    [Fact]
    public void DynamicLayoutNode_AppliesContextToFactoryCreatedChild()
    {
        var context = CreateContext();
        var child = new RecordingNode();
        using var node = new DynamicLayoutNode(() => child);

        LayoutRuntimeContextInjector.Apply(node, context);
        node.Measure(new Size(10, 10));

        Assert.Same(context, child.Context);
    }

    [Fact]
    public void ReactiveLayoutNode_AfterReactivation_NotifiesStructuralChange()
    {
        var structureChanges = 0;
        var timeProvider = new FakeTimeProvider();
        var context = new LayoutRuntimeContext(
            new TestFrameProvider(),
            timeProvider,
            () => { },
            () => structureChanges++);
        var source = new Subject<ILayoutNode>();
        using var node = new ReactiveLayoutNode(source);
        LayoutRuntimeContextInjector.Apply(node, context);

        node.OnDeactivate();
        node.OnActivate();
        source.OnNext(new TextNode("replacement"));

        Assert.Equal(1, structureChanges);
    }

    [Fact]
    public void GenericReactiveLayoutNode_AfterReactivation_NotifiesStructuralChange()
    {
        var structureChanges = 0;
        var timeProvider = new FakeTimeProvider();
        var context = new LayoutRuntimeContext(
            new TestFrameProvider(),
            timeProvider,
            () => { },
            () => structureChanges++);
        var source = new Subject<int>();
        using var node = new ReactiveLayoutNode<int>(source, value => new TextNode(value.ToString()));
        LayoutRuntimeContextInjector.Apply(node, context);

        node.OnDeactivate();
        node.OnActivate();
        source.OnNext(1);

        Assert.Equal(1, structureChanges);
    }

    [Fact]
    public void ContainerNode_AppliesContextToChildAddedAfterContext()
    {
        var context = CreateContext();
        var container = new TestContainerNode();
        var child = new RecordingNode();

        LayoutRuntimeContextInjector.Apply(container, context);
        container.Add(child);

        Assert.Same(context, child.Context);
    }

    [Fact]
    public void SpinnerNode_UsesRuntimeContextFrameProviderWithoutConstructorArgument()
    {
        var timeProvider = new FakeTimeProvider();
        var frameRequests = 0;
        using var frameProvider = new TerminaRenderFrameProvider(
            () => frameRequests++,
            timeProvider,
            TimeSpan.FromMilliseconds(10));
        var context = new LayoutRuntimeContext(frameProvider, timeProvider, () => { });
        using var spinner = new SpinnerNode(intervalMs: 80);
        var invalidations = 0;
        using var subscription = spinner.Invalidated.Subscribe(_ => invalidations++);

        LayoutRuntimeContextInjector.Apply(spinner, context);
        spinner.OnActivate();
        timeProvider.Advance(TimeSpan.FromMilliseconds(80));

        Assert.Equal(0, invalidations);
        Assert.Equal(1, frameRequests);

        frameProvider.AdvanceFrame();

        Assert.Equal(1, invalidations);
    }

    private static LayoutRuntimeContext CreateContext()
    {
        var timeProvider = new FakeTimeProvider();
        var frameProvider = new TestFrameProvider();
        return new LayoutRuntimeContext(frameProvider, timeProvider, () => { });
    }

    private sealed class RecordingNode : LayoutNode
    {
        public LayoutRuntimeContext? Context => RuntimeContext;

        public bool HadContextOnActivate { get; private set; }

        public override void OnActivate()
        {
            HadContextOnActivate = RuntimeContext is not null;
            base.OnActivate();
        }

        public override Size Measure(Size available) => Size.Zero;

        public override void Render(IRenderContext context, Rect bounds)
        {
        }
    }

    private sealed class TestContainerNode : ContainerNode
    {
        public void Add(ILayoutNode child) => AddChild(child);

        public override Size Measure(Size available) => Size.Zero;

        public override void Render(IRenderContext context, Rect bounds)
        {
        }
    }

    private sealed class TestFrameProvider : FrameProvider
    {
        public override long GetFrameCount() => 0;

        public override void Register(IFrameRunnerWorkItem callback)
        {
        }
    }
}
