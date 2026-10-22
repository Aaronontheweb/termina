// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests;

/// <summary>
/// Tests that the scroll bound of <see cref="StreamingTextNode"/> depends on the viewport size
/// and that the terminal shows the expected cells.
/// </summary>
public class StreamingTextNodeViewportBoundTests
{
    private const char TrackChar = '░';
    private const char ThumbChar = '█';

    private static readonly ConsoleKeyInfo CtrlHome =
        new('\0', ConsoleKey.Home, shift: false, alt: false, control: true);

    private static readonly ConsoleKeyInfo PageUp =
        new('\0', ConsoleKey.PageUp, shift: false, alt: false, control: false);

    private static StreamingTextNode CreateNode(int lineCount, Func<int, string>? lineFactory = null)
    {
        var node = new StreamingTextNode(new PersistedStreamBuffer());
        var factory = lineFactory ?? (index => $"Line {index}");
        for (var i = 0; i < lineCount; i++)
            node.AppendLine(factory(i));
        return node;
    }

    private static VirtualTerminal RenderInto(StreamingTextNode node, int width, int height)
    {
        var terminal = new VirtualTerminal(width, height);
        var context = new RegionRenderContext(terminal, 0, 0, width, height);
        node.Render(context, new Rect(0, 0, width, height));
        return terminal;
    }

    private static IEnumerable<string> Rows(VirtualTerminal terminal, int count)
    {
        return Enumerable.Range(0, count).Select(y => terminal.GetLine(y).TrimEnd());
    }

    private static string WideLine(int index)
    {
        return $"L{index:D2}" + new string('x', 37);
    }

    [Fact]
    public void CtrlHome_BeforeFirstRender_UsesTheSuppliedViewportHeight()
    {
        var node = CreateNode(30);

        node.HandleInput(CtrlHome, viewportHeight: 10, viewportWidth: 40);
        var terminal = RenderInto(node, 40, 10);

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"Line {i}"), Rows(terminal, 10));
    }

    [Fact]
    public void CtrlHome_AfterTheViewportShrinks_UsesTheSuppliedViewportHeight()
    {
        var node = CreateNode(30);
        RenderInto(node, 40, 20);

        node.HandleInput(CtrlHome, viewportHeight: 10, viewportWidth: 40);
        var terminal = RenderInto(node, 40, 10);

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"Line {i}"), Rows(terminal, 10));
    }

    [Fact]
    public void PageUp_BeforeFirstRender_ReachesTheOldestContent()
    {
        var node = CreateNode(30);

        for (var i = 0; i < 5; i++)
            node.HandleInput(PageUp, viewportHeight: 10, viewportWidth: 40);
        var terminal = RenderInto(node, 40, 10);

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"Line {i}"), Rows(terminal, 10));
    }

    [Fact]
    public void PageUp_AfterTheViewportShrinks_ReachesTheOldestContent()
    {
        var node = CreateNode(30);
        RenderInto(node, 40, 20);

        for (var i = 0; i < 5; i++)
            node.HandleInput(PageUp, viewportHeight: 10, viewportWidth: 40);
        var terminal = RenderInto(node, 40, 10);

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"Line {i}"), Rows(terminal, 10));
    }

    [Fact]
    public void ScrollUp_WithoutWidth_UsesTheMeasuredWidthOfWrappedContent()
    {
        var node = CreateNode(15, WideLine);
        RenderInto(node, 20, 20);
        Assert.True(node.CanScrollUp);

        node.ScrollUp(100);
        var terminal = RenderInto(node, 20, 20);

        Assert.False(node.CanScrollUp);
        Assert.StartsWith("L00", terminal.GetLine(0));
    }

    [Fact]
    public void ScrollUp_WithoutWidth_StopsWhenTheOldestContentFillsTheViewport()
    {
        var node = CreateNode(30);
        RenderInto(node, 40, 10);

        node.ScrollUp(100);

        Assert.False(node.CanScrollUp);
        Assert.Equal(20, ((PersistedStreamBuffer)node.Buffer).ScrollOffset);
    }

    [Fact]
    public void ForcedScrollbar_DrawsAFullThumb_WhenContentFitsTheViewport()
    {
        var node = CreateNode(3).WithScrollbar(new ScrollbarOptions(AutoHide: false));

        var terminal = RenderInto(node, 40, 10);

        for (var y = 0; y < 10; y++)
            Assert.Equal(ThumbChar, terminal.GetChar(39, y));
    }

    [Fact]
    public void ForcedScrollbar_DrawsAFullThumb_WhenContentExactlyFillsTheViewport()
    {
        var node = CreateNode(10).WithScrollbar(new ScrollbarOptions(AutoHide: false));

        var terminal = RenderInto(node, 40, 10);

        for (var y = 0; y < 10; y++)
            Assert.Equal(ThumbChar, terminal.GetChar(39, y));
        Assert.Equal("Line 0", terminal.GetLine(0)[..39].TrimEnd());
    }

    [Fact]
    public void Scrollbar_ThumbIsAtTheTop_WhenScrolledToTheOldestViewport()
    {
        var node = CreateNode(30).WithScrollbar();
        RenderInto(node, 40, 10);

        node.ScrollUp(100);
        var terminal = RenderInto(node, 40, 10);

        Assert.Equal(ThumbChar, terminal.GetChar(39, 0));
        Assert.Equal(TrackChar, terminal.GetChar(39, 9));
    }

    [Fact]
    public void Render_AfterClear_ShowsTheNewContentFromTheTop()
    {
        var node = CreateNode(30);
        RenderInto(node, 40, 10);
        node.ScrollUp(100);

        node.Clear();
        node.AppendLine("Fresh 0");
        node.AppendLine("Fresh 1");
        var terminal = RenderInto(node, 40, 10);

        Assert.Equal(["Fresh 0", "Fresh 1"], Rows(terminal, 2));
        Assert.Equal(0, ((PersistedStreamBuffer)node.Buffer).ScrollOffset);
        Assert.False(node.CanScrollUp);
    }

    [Fact]
    public void Render_ClampsTheOffset_WhenAWiderViewportReflowsTheLines()
    {
        var node = CreateNode(10, WideLine);
        RenderInto(node, 20, 10);
        node.HandleInput(CtrlHome, viewportHeight: 10, viewportWidth: 20);
        var buffer = (PersistedStreamBuffer)node.Buffer;
        Assert.Equal(10, buffer.ScrollOffset);

        var terminal = RenderInto(node, 40, 10);

        Assert.Equal(0, buffer.ScrollOffset);
        Assert.Equal(Enumerable.Range(0, 10).Select(WideLine), Rows(terminal, 10));
    }

    [Fact]
    public void ScrollUp_ReachesTheOldestContent_AfterANarrowerViewportReflowsTheLines()
    {
        var node = CreateNode(10, WideLine);
        RenderInto(node, 40, 10);
        RenderInto(node, 20, 10);

        node.ScrollUp(100);
        var terminal = RenderInto(node, 20, 10);

        Assert.Equal(10, ((PersistedStreamBuffer)node.Buffer).ScrollOffset);
        Assert.StartsWith("L00", terminal.GetLine(0));
    }

    [Fact]
    public void ViewportHeightOfOne_ScrollsToTheOldestRow()
    {
        var node = CreateNode(30);
        RenderInto(node, 40, 1);

        node.ScrollUp(100);
        var terminal = RenderInto(node, 40, 1);

        Assert.Equal(29, ((PersistedStreamBuffer)node.Buffer).ScrollOffset);
        Assert.Equal("Line 0", terminal.GetLine(0).TrimEnd());
    }

    [Fact]
    public void ViewportHeightOfZero_IsTreatedAsOneRow()
    {
        var node = CreateNode(30);

        node.HandleInput(CtrlHome, viewportHeight: 0, viewportWidth: 40);
        var terminal = RenderInto(node, 40, 1);

        Assert.Equal("Line 0", terminal.GetLine(0).TrimEnd());
    }

    [Fact]
    public void Buffer_GetMaxScrollOffset_TreatsAViewportHeightOfZeroAsOneRow()
    {
        var buffer = new PersistedStreamBuffer();
        for (var i = 0; i < 5; i++)
            buffer.AppendLine($"Line {i}");

        Assert.Equal(4, buffer.GetMaxScrollOffset(40, 0));
        Assert.Equal(4, buffer.GetMaxScrollOffset(40, 1));
        Assert.Equal(0, buffer.GetMaxScrollOffset(40, 5));
    }

    [Fact]
    public void Buffer_ClampScrollOffset_KeepsTheUserScrolledState()
    {
        var buffer = new PersistedStreamBuffer();
        for (var i = 0; i < 30; i++)
            buffer.AppendLine($"Line {i}");
        buffer.ScrollUp(29, 40, 1);

        buffer.ClampScrollOffset(40, 10);

        Assert.Equal(20, buffer.ScrollOffset);
        Assert.True(buffer.IsScrolledUp);
    }

    [Fact]
    public void Buffer_ClampScrollOffset_LeavesAnOffsetInsideTheRangeUnchanged()
    {
        var buffer = new PersistedStreamBuffer();
        for (var i = 0; i < 30; i++)
            buffer.AppendLine($"Line {i}");
        buffer.ScrollUp(5, 40, 10);

        buffer.ClampScrollOffset(40, 10);

        Assert.Equal(5, buffer.ScrollOffset);
    }
}
