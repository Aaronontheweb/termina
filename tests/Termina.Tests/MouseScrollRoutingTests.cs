// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;
using Termina.Input;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests;

/// <summary>
/// Tests for mouse wheel scroll support via <see cref="IScrollable"/> and <see cref="MouseScrollEvent"/>.
/// </summary>
public class MouseScrollRoutingTests
{
    [Fact]
    public void StreamingTextNode_ImplementsIScrollable()
    {
        var node = StreamingTextNode.Create();
        Assert.IsAssignableFrom<IScrollable>(node);
    }

    [Fact]
    public void CanScrollUp_False_BeforeFirstRender_WithNoContent()
    {
        var node = StreamingTextNode.Create();
        Assert.False(node.CanScrollUp);
    }

    [Fact]
    public void CanScrollDown_False_WhenAtBottom()
    {
        var node = StreamingTextNode.Create();
        for (var i = 0; i < 30; i++)
            node.AppendLine($"Line {i}");

        var terminal = new VirtualTerminal(40, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 10);
        node.Render(context, new Rect(0, 0, 40, 10));

        // At the bottom (scrollOffset=0) → cannot scroll further down
        Assert.False(node.CanScrollDown);
    }

    [Fact]
    public void CanScrollUp_True_WhenContentExceedsViewport()
    {
        var node = StreamingTextNode.Create();
        for (var i = 0; i < 30; i++)
            node.AppendLine($"Line {i}");

        var terminal = new VirtualTerminal(40, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 10);
        node.Render(context, new Rect(0, 0, 40, 10));

        // 30 lines in a 10-row viewport → can scroll up to older content
        Assert.True(node.CanScrollUp);
    }

    [Fact]
    public void CanScrollDown_True_AfterScrollingUp()
    {
        var node = StreamingTextNode.Create();
        for (var i = 0; i < 30; i++)
            node.AppendLine($"Line {i}");

        var terminal = new VirtualTerminal(40, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 10);
        node.Render(context, new Rect(0, 0, 40, 10));

        // Scroll up first
        node.ScrollUp(5, 40);
        node.Render(context, new Rect(0, 0, 40, 10));

        // After scrolling up, can scroll back down
        Assert.True(node.CanScrollDown);
    }

    [Fact]
    public void ScrollUp_StopsWhenTheOldestContentFillsTheViewport()
    {
        var buffer = new PersistedStreamBuffer();
        var node = new StreamingTextNode(buffer);
        for (var i = 0; i < 30; i++)
            node.AppendLine($"Line {i}");

        var terminal = new VirtualTerminal(40, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 10);
        node.Render(context, new Rect(0, 0, 40, 10));

        ((IScrollable)node).ScrollUp(100);

        Assert.Equal(20, buffer.ScrollOffset);
        Assert.False(node.CanScrollUp);
        Assert.Equal(
            Enumerable.Range(0, 10).Select(index => $"Line {index}"),
            buffer.GetVisibleLines(10, 40));
    }

    [Fact]
    public void Render_ClampsAnExistingOffsetAfterTheViewportGetsTaller()
    {
        var buffer = new PersistedStreamBuffer();
        var node = new StreamingTextNode(buffer);
        for (var i = 0; i < 30; i++)
            node.AppendLine($"Line {i}");

        var terminal = new VirtualTerminal(40, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 10);
        node.Render(context, new Rect(0, 0, 40, 1));
        ((IScrollable)node).ScrollUp(100);
        Assert.Equal(29, buffer.ScrollOffset);

        node.Render(context, new Rect(0, 0, 40, 10));

        Assert.Equal(20, buffer.ScrollOffset);
        Assert.Equal(
            Enumerable.Range(0, 10).Select(index => $"Line {index}"),
            buffer.GetVisibleLines(10, 40));
    }

    [Fact]
    public void IScrollable_ScrollUp_UsesLastViewportWidth()
    {
        var node = StreamingTextNode.Create();
        for (var i = 0; i < 30; i++)
            node.AppendLine($"Line {i}");

        var terminal = new VirtualTerminal(40, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 10);
        node.Render(context, new Rect(0, 0, 40, 10));

        // ScrollUp via IScrollable interface
        IScrollable scrollable = node;
        scrollable.ScrollUp(3);

        // Verify we scrolled (CanScrollDown should now be true)
        node.Render(context, new Rect(0, 0, 40, 10));
        Assert.True(node.CanScrollDown);
    }

    [Fact]
    public void IScrollable_ScrollDown_DecreasesScrollOffset()
    {
        var node = StreamingTextNode.Create();
        for (var i = 0; i < 30; i++)
            node.AppendLine($"Line {i}");

        var terminal = new VirtualTerminal(40, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 10);
        node.Render(context, new Rect(0, 0, 40, 10));

        // Scroll up then back down via interface
        IScrollable scrollable = node;
        scrollable.ScrollUp(5);
        node.Render(context, new Rect(0, 0, 40, 10));
        Assert.True(node.CanScrollDown);

        scrollable.ScrollDown(5);
        node.Render(context, new Rect(0, 0, 40, 10));
        Assert.False(node.CanScrollDown);
    }

    [Fact]
    public void MouseScrollEvent_PositiveDelta_MeansScrollUp()
    {
        // Verify that positive Delta represents scroll up (toward older content)
        var evt = new MouseScrollEvent(+1);
        Assert.True(evt.Delta > 0);
    }

    [Fact]
    public void MouseScrollEvent_NegativeDelta_MeansScrollDown()
    {
        var evt = new MouseScrollEvent(-1);
        Assert.True(evt.Delta < 0);
    }

    [Fact]
    public void MouseScrollEvent_RoutesToScrollableUnderPointer()
    {
        var (app, top, bottom) = CreateScrollApplication();
        try
        {
            var topOffset = top.ScrollOffset;
            var bottomOffset = bottom.ScrollOffset;

            InvokeProcessEvent(app, new MouseScrollEvent(+1) { X = 2, Y = 2 });

            Assert.Equal(topOffset - 3, top.ScrollOffset);
            Assert.Equal(bottomOffset, bottom.ScrollOffset);
        }
        finally
        {
            app.Dispose();
        }
    }

    [Fact]
    public void LegacyMouseScroll_RoutesToScrollableUnderPointer()
    {
        var (app, top, bottom) = CreateScrollApplication();
        try
        {
            var topOffset = top.ScrollOffset;
            var bottomOffset = bottom.ScrollOffset;

            InvokeProcessEvent(app, new MouseEvent(
                X: 2,
                Y: 7,
                MouseButton.WheelUp,
                MouseEventType.Scroll));

            Assert.Equal(topOffset, top.ScrollOffset);
            Assert.Equal(bottomOffset - 3, bottom.ScrollOffset);
        }
        finally
        {
            app.Dispose();
        }
    }

    [Fact]
    public void ScrollableContainer_UsesMeasuredViewportThroughIScrollable()
    {
        var (app, top, _) = CreateScrollApplication();
        try
        {
            var offset = top.ScrollOffset;

            ((IScrollable)top).ScrollUp(4);

            Assert.Equal(offset - 4, top.ScrollOffset);
        }
        finally
        {
            app.Dispose();
        }
    }

    private static (TerminaApplication App, ScrollableContainerNode Top, ScrollableContainerNode Bottom)
        CreateScrollApplication()
    {
        var top = CreateScrollable("Top");
        var bottom = CreateScrollable("Bottom");
        ScrollPage.Root = Layouts.Vertical()
            .WithChild(top.Height(5))
            .WithChild(bottom.Height(5));

        var app = new TerminaApplication(new VirtualTerminal(20, 10), new TestServiceProvider());
        app.RegisterRoute<ScrollPage, ScrollViewModel>("/scroll");
        app.NavigateTo("/scroll");
        InvokeRenderCurrentPage(app);
        return (app, top, bottom);
    }

    private static ScrollableContainerNode CreateScrollable(string prefix) =>
        new ScrollableContainerNode()
            .WithContent(new TextNode(string.Join('\n',
                Enumerable.Range(0, 20).Select(index => $"{prefix} {index}"))));

    private static void InvokeProcessEvent(TerminaApplication app, object evt)
    {
        var method = typeof(TerminaApplication).GetMethod(
            "ProcessEvent",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        method!.Invoke(app, [evt]);
    }

    private static void InvokeRenderCurrentPage(TerminaApplication app)
    {
        var method = typeof(TerminaApplication).GetMethod(
            "RenderCurrentPage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        method!.Invoke(app, []);
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IEnumerable<IInputSource>)
            ? Array.Empty<IInputSource>()
            : null;
    }

    private sealed class ScrollPage : ReactivePage<ScrollViewModel>
    {
        public static ILayoutNode Root { get; set; } = new EmptyNode();

        public override ILayoutNode BuildLayout() => Root;
    }

    private sealed class ScrollViewModel : ReactiveViewModel
    {
    }
}
