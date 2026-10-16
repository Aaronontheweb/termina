// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Rendering;

/// <summary>
/// Tests for RegionRenderContext - coordinate translation and clipping.
/// </summary>
public class RegionRenderContextTests
{
    [Fact]
    public void WriteAt_TranslatesCoordinates()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 10, 5, 20, 10);

        context.WriteAt(0, 0, "Hello");

        Assert.Equal("Hello", terminal.GetRegion(10, 5, 5, 1));
    }

    [Fact]
    public void WriteAt_ClipsToRegionWidth()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 5, 1);

        context.WriteAt(0, 0, "Hello World");

        // Should only write "Hello" (5 chars)
        Assert.Equal("Hello", terminal.GetLine(0));
    }

    [Fact]
    public void WriteAt_ClipsStartingOutsideRegion()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);

        context.WriteAt(-2, 0, "Hello");

        // First 2 chars skipped, "llo" written starting at 0
        Assert.Equal("llo", terminal.GetRegion(0, 0, 3, 1));
    }

    [Fact]
    public void WriteAt_YOutOfRange_DoesNothing()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 5);

        context.WriteAt(0, 10, "Hidden");

        Assert.False(terminal.Contains("Hidden"));
    }

    [Fact]
    public void WriteAt_XOutOfRange_DoesNothing()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 5);

        context.WriteAt(20, 0, "Hidden");

        Assert.False(terminal.Contains("Hidden"));
    }

    [Fact]
    public void WriteAt_Char_TranslatesCoordinates()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 15, 10, 20, 10);

        context.WriteAt(5, 3, 'X');

        Assert.Equal('X', terminal.GetChar(20, 13));
    }

    [Fact]
    public void WriteAt_Char_OutOfRange_DoesNothing()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 5);

        context.WriteAt(-1, 0, 'X');
        context.WriteAt(10, 0, 'Y');
        context.WriteAt(0, -1, 'Z');
        context.WriteAt(0, 5, 'W');

        Assert.False(terminal.Contains("X"));
        Assert.False(terminal.Contains("Y"));
        Assert.False(terminal.Contains("Z"));
        Assert.False(terminal.Contains("W"));
    }

    [Fact]
    public void WriteAt_WideCharThatDoesNotFit_DoesNotOverflowRegion()
    {
        var terminal = new VirtualTerminal(5, 1);
        var context = new RegionRenderContext(terminal, 0, 0, 1, 1);

        context.WriteAt(0, 0, '你');

        Assert.Equal(' ', terminal.GetChar(0, 0));
        Assert.Equal(' ', terminal.GetChar(1, 0));
    }

    [Fact]
    public void WriteAt_TextWithNewline_DoesNotMoveOutsideRegionRow()
    {
        var terminal = new VirtualTerminal(10, 2);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);

        context.WriteAt(0, 0, "A\nB");

        Assert.Equal("A B", terminal.GetLine(0));
        Assert.Equal("", terminal.GetLine(1));
    }

    [Fact]
    public void WriteAt_TextWithAnsiEscape_DoesNotExecuteOrRenderEscapeFragment()
    {
        var terminal = new VirtualTerminal(10, 1);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);

        context.WriteAt(0, 0, "\u001b[1mHi");

        Assert.Equal("Hi", terminal.GetLine(0));
    }

    [Fact]
    public void Width_ReturnsRegionWidth()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 10, 5, 20, 10);

        Assert.Equal(20, context.Width);
    }

    [Fact]
    public void Height_ReturnsRegionHeight()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 10, 5, 20, 10);

        Assert.Equal(10, context.Height);
    }

    [Fact]
    public void SetForeground_SetsTerminalForeground()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 5);

        context.SetForeground(Color.Red);
        context.WriteAt(0, 0, "X");

        Assert.Equal(Color.Red, terminal.GetForeground(0, 0));
    }

    [Fact]
    public void SetBackground_SetsTerminalBackground()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 5);

        context.SetBackground(Color.Blue);
        context.WriteAt(0, 0, "X");

        Assert.Equal(Color.Blue, terminal.GetBackground(0, 0));
    }

    [Fact]
    public void ResetColors_ResetsTerminalColors()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 5);

        context.SetForeground(Color.Red);
        context.SetBackground(Color.Blue);
        context.ResetColors();
        context.WriteAt(0, 0, "X");

        Assert.Equal(Color.Default, terminal.GetForeground(0, 0));
        Assert.Equal(Color.Default, terminal.GetBackground(0, 0));
    }

    [Fact]
    public void Fill_FillsRectangle()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 10, 5, 20, 10);

        context.Fill(0, 0, 5, 3, '*');

        for (var y = 5; y < 8; y++)
        {
            for (var x = 10; x < 15; x++)
            {
                Assert.Equal('*', terminal.GetChar(x, y));
            }
        }
    }

    [Fact]
    public void Fill_ClipsToRegion()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 5, 5);

        // Fill larger than region
        context.Fill(0, 0, 100, 100, 'X');

        // Should be clipped to 5x5
        Assert.Equal('X', terminal.GetChar(4, 4));
        Assert.Equal(' ', terminal.GetChar(5, 0)); // Outside region
    }

    [Fact]
    public void Clear_FillsWithSpaces()
    {
        var terminal = new VirtualTerminal(80, 24);
        terminal.MoveTo(10, 5);
        terminal.Write("XXXXXXXXX");

        var context = new RegionRenderContext(terminal, 10, 5, 5, 1);
        context.Clear();

        // First 5 X's should be replaced with spaces
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(' ', terminal.GetChar(10 + i, 5));
        }
        // Remaining X's untouched
        Assert.Equal('X', terminal.GetChar(15, 5));
    }
}
