// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

public class ScrollableContainerNodeAutoScrollTests
{
    [Fact]
    public void TailWhenAtBottom_FollowsContentThatGrowsByMoreThanTheThreshold()
    {
        var node = CreateNode(20);
        node.Measure(new Size(40, 5));
        node.ScrollToBottom();

        node.WithContent(CreateContent(50));
        node.Measure(new Size(40, 5));

        Assert.Equal(node.MaxScroll, node.ScrollOffset);
        Assert.False(node.CanScrollDown);
    }

    [Fact]
    public void TailWhenAtBottom_PreservesTheOffsetWhenTheUserScrolledUp()
    {
        var node = CreateNode(20);
        node.Measure(new Size(40, 5));
        node.ScrollToBottom();
        node.PageUp();
        var offset = node.ScrollOffset;

        node.WithContent(CreateContent(50));
        node.Measure(new Size(40, 5));

        Assert.Equal(offset, node.ScrollOffset);
        Assert.True(node.CanScrollDown);
    }

    [Fact]
    public void TailWhenAtBottom_FollowsSeveralBatchedInvalidationsWithOneMeasure()
    {
        // A batched render turns many small updates into one large height change.
        var lineCount = 20;
        var dynamic = new DynamicLayoutNode(() => CreateContent(lineCount));
        var node = CreateNode(dynamic);
        node.Measure(new Size(40, 5));
        Assert.Equal(node.MaxScroll, node.ScrollOffset);

        for (var i = 0; i < 3; i++)
        {
            lineCount += 2;
            dynamic.Invalidate();
        }

        node.Measure(new Size(40, 5));

        Assert.Equal(node.MaxScroll, node.ScrollOffset);
    }

    [Fact]
    public void TailWhenAtBottom_InvalidationDoesNotMoveAUserWhoScrolledUp()
    {
        var lineCount = 40;
        var dynamic = new DynamicLayoutNode(() => CreateContent(lineCount));
        var node = CreateNode(dynamic);
        node.Measure(new Size(40, 5));
        node.Measure(new Size(40, 5));
        node.PageUp();
        node.PageUp();
        var offset = node.ScrollOffset;

        lineCount += 1;
        dynamic.Invalidate();
        Assert.Equal(offset, node.ScrollOffset);

        node.Measure(new Size(40, 5));

        Assert.Equal(offset, node.ScrollOffset);
        Assert.True(node.CanScrollDown);
    }

    [Fact]
    public void TailWhenAtBottom_ClampsTheOffsetWhenContentShrinksBelowIt()
    {
        var node = CreateNode(50);
        node.Measure(new Size(40, 5));
        node.ScrollTo(30);

        node.WithContent(CreateContent(20));
        node.Measure(new Size(40, 5));

        Assert.InRange(node.ScrollOffset, 0, node.MaxScroll);
    }

    [Fact]
    public void TailWhenAtBottom_RendersRowsWhenContentShrinksBelowTheOffset()
    {
        var node = CreateNode(50);
        node.Measure(new Size(40, 5));
        node.ScrollTo(30);

        node.WithContent(CreateContent(20));
        node.Measure(new Size(40, 5));
        var terminal = new VirtualTerminal(40, 5);
        node.Render(new RegionRenderContext(terminal, 0, 0, 40, 5), new Rect(0, 0, 40, 5));

        Assert.Equal('L', terminal.GetChar(0, 0));
    }

    [Fact]
    public void TailWhenAtBottom_ClampsTheOffsetWhenContentClearsWhileScrolledUp()
    {
        var node = CreateNode(50);
        node.Measure(new Size(40, 5));
        node.ScrollTo(30);

        node.WithContent(CreateContent(3));
        node.Measure(new Size(40, 5));

        Assert.InRange(node.ScrollOffset, 0, node.MaxScroll);
    }

    [Fact]
    public void TailWhenAtBottom_FollowsTheTailWhenRenderedSmallerThanMeasured()
    {
        // VerticalLayout clips an overflow child, so Render can use a smaller viewport than Measure.
        var node = CreateNode(20);
        node.Measure(new Size(40, 10));
        var terminal = new VirtualTerminal(40, 10);
        node.Render(new RegionRenderContext(terminal, 0, 0, 40, 5), new Rect(0, 0, 40, 5));

        node.WithContent(CreateContent(21));
        node.Measure(new Size(40, 10));

        Assert.Equal(node.MaxScroll, node.ScrollOffset);
    }

    [Fact]
    public void TailWhenAtBottom_SnapsToTheTailWhenTheUserIsTwoRowsUp()
    {
        var node = CreateNode(20);
        node.Measure(new Size(40, 5));
        node.ScrollUp();
        node.ScrollUp();

        node.WithContent(CreateContent(60));
        node.Measure(new Size(40, 5));

        Assert.Equal(node.MaxScroll, node.ScrollOffset);
    }

    [Fact]
    public void TailWhenAtBottom_KeepsTheOffsetWhenTheUserIsThreeRowsUp()
    {
        var node = CreateNode(20);
        node.Measure(new Size(40, 5));
        node.ScrollUp();
        node.ScrollUp();
        node.ScrollUp();
        var offset = node.ScrollOffset;

        node.WithContent(CreateContent(60));
        node.Measure(new Size(40, 5));

        Assert.Equal(offset, node.ScrollOffset);
    }

    [Fact]
    public void TailWhenAtBottom_FollowsAfterTheUserScrollsBackDownLineByLine()
    {
        var node = CreateNode(20);
        node.Measure(new Size(40, 5));
        node.PageUp();
        node.PageUp();
        while (node.CanScrollDown)
        {
            node.ScrollDown();
        }

        node.WithContent(CreateContent(60));
        node.Measure(new Size(40, 5));

        Assert.Equal(node.MaxScroll, node.ScrollOffset);
    }

    private static ScrollableContainerNode CreateNode(int lineCount) =>
        CreateNode(CreateContent(lineCount));

    private static ScrollableContainerNode CreateNode(ILayoutNode content) =>
        new ScrollableContainerNode()
            .WithAutoScroll(AutoScrollPolicy.TailWhenAtBottom)
            .WithScrollbar(false)
            .WithContent(content);

    private static TextNode CreateContent(int lineCount) =>
        new(string.Join('\n', Enumerable.Range(1, lineCount).Select(index => $"Line {index}")));
}
