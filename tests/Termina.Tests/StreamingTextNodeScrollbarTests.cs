// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests;

/// <summary>
/// Tests for the visual scrollbar feature on <see cref="StreamingTextNode"/>.
/// </summary>
public class StreamingTextNodeScrollbarTests
{
    private static (StreamingTextNode Node, VirtualTerminal Terminal, RegionRenderContext Context, Rect Bounds)
        SetupWithContent(int width, int height, int lineCount, bool withScrollbar = true)
    {
        var node = StreamingTextNode.Create();
        if (withScrollbar)
            node.WithScrollbar();

        for (var i = 0; i < lineCount; i++)
            node.AppendLine($"Line {i}");

        var terminal = new VirtualTerminal(width, height);
        var context = new RegionRenderContext(terminal, 0, 0, width, height);
        var bounds = new Rect(0, 0, width, height);
        return (node, terminal, context, bounds);
    }

    [Fact]
    public void Scrollbar_DrawsAtLastColumn_WhenContentExceedsViewport()
    {
        // 30 lines into a 10-row viewport → content overflows → scrollbar visible
        var (node, terminal, context, bounds) = SetupWithContent(40, 10, 30);
        node.Render(context, bounds);

        // At least one cell in the last column should contain a scrollbar character
        var foundScrollbarChar = false;
        for (var y = 0; y < 10; y++)
        {
            var c = terminal.GetChar(39, y);
            if (c == '░' || c == '█')
            {
                foundScrollbarChar = true;
                break;
            }
        }

        Assert.True(foundScrollbarChar, "Expected scrollbar character in column 39 (bounds.Width - 1)");
    }

    [Fact]
    public void Scrollbar_AutoHide_HidesScrollbar_WhenContentFitsViewport()
    {
        // Only 3 lines in a 10-row viewport → no overflow → scrollbar hidden
        var (node, terminal, context, bounds) = SetupWithContent(40, 10, 3);
        node.Render(context, bounds);

        // Last column should not contain any scrollbar characters
        for (var y = 0; y < 10; y++)
        {
            var c = terminal.GetChar(39, y);
            Assert.False(c == '░' || c == '█',
                $"Unexpected scrollbar character '{c}' at column 39, row {y} when content fits viewport");
        }
    }

    [Fact]
    public void Scrollbar_NotDrawn_WhenWithScrollbarNotCalled()
    {
        // Create without calling WithScrollbar()
        var (node, terminal, context, bounds) = SetupWithContent(40, 10, 30, withScrollbar: false);
        node.Render(context, bounds);

        // Last column should not contain scrollbar characters
        for (var y = 0; y < 10; y++)
        {
            var c = terminal.GetChar(39, y);
            Assert.False(c == '░' || c == '█',
                $"Unexpected scrollbar character at column 39, row {y} — WithScrollbar() was not called");
        }
    }

    [Fact]
    public void Scrollbar_AutoHideFalse_AlwaysDraws_EvenWhenContentFits()
    {
        var node = StreamingTextNode.Create()
            .WithScrollbar(new ScrollbarOptions(AutoHide: false));
        node.AppendLine("Only one line");

        var terminal = new VirtualTerminal(40, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 10);
        node.Render(context, new Rect(0, 0, 40, 10));

        // With AutoHide false the scrollbar column is reserved and drawn.
        // All content is visible, so the thumb fills the whole track.
        for (var y = 0; y < 10; y++)
            Assert.Equal('█', terminal.GetChar(39, y));
    }

    [Fact]
    public void Scrollbar_ThumbAtBottom_WhenScrollOffsetIsZero()
    {
        // 30 lines, 10-row viewport → buffer at bottom (scrollOffset=0)
        var (node, terminal, context, bounds) = SetupWithContent(40, 10, 30);
        node.Render(context, bounds);

        // The thumb should appear at the bottom of the scrollbar track
        // (because 0 = bottom in PersistedStreamBuffer convention)
        // The last row of the scrollbar column (y=9) should be the thumb
        Assert.Equal('█', terminal.GetChar(39, 9));
    }

    [Fact]
    public void Scrollbar_ThumbAtTop_WhenScrolledToMax()
    {
        var node = StreamingTextNode.Create().WithScrollbar();
        for (var i = 0; i < 30; i++)
            node.AppendLine($"Line {i}");

        var terminal = new VirtualTerminal(40, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 10);
        var bounds = new Rect(0, 0, 40, 10);

        // First render to populate cached viewport dimensions
        node.Render(context, bounds);

        // Scroll to the top (oldest content)
        node.ScrollUp(100, 39); // large value to reach max

        // Re-render after scrolling
        terminal.Clear();
        node.Render(context, bounds);

        // Thumb should now be at the top of the scrollbar track (y=0)
        Assert.Equal('█', terminal.GetChar(39, 0));
    }
}
