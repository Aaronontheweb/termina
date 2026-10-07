// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;

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

    private static ScrollableContainerNode CreateNode(int lineCount) =>
        new ScrollableContainerNode()
            .WithAutoScroll(AutoScrollPolicy.TailWhenAtBottom)
            .WithScrollbar(false)
            .WithContent(CreateContent(lineCount));

    private static TextNode CreateContent(int lineCount) =>
        new(string.Join('\n', Enumerable.Range(1, lineCount).Select(index => $"Line {index}")));
}
