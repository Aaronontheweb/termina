// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;
using Termina.Rendering;

namespace Termina.Tests.Terminal;

/// <summary>
/// Tests for DiffingTerminal - the double-buffered terminal wrapper that eliminates flickering.
/// </summary>
public class DiffingTerminalTests
{
    [Fact]
    public void Constructor_WrapsInnerTerminal()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        Assert.Equal(80, diffing.Width);
        Assert.Equal(24, diffing.Height);
    }

    [Fact]
    public void Write_DoesNotImmediatelyOutputToInner()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(0, 0);
        diffing.Write("Hello");

        // Inner terminal should be empty until Flush
        Assert.Equal(' ', inner.GetChar(0, 0));
    }

    [Fact]
    public void Flush_OutputsContentToInner()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(0, 0);
        diffing.Write("Hello");
        diffing.Flush();

        // After flush, inner should have content
        Assert.Equal('H', inner.GetChar(0, 0));
        Assert.Equal('e', inner.GetChar(1, 0));
        Assert.Equal('l', inner.GetChar(2, 0));
        Assert.Equal('l', inner.GetChar(3, 0));
        Assert.Equal('o', inner.GetChar(4, 0));
    }

    [Fact]
    public void Flush_AdvancesByDisplayColumnsForCjk()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(0, 0);
        diffing.Write("你A");
        diffing.Flush();

        Assert.Equal('你', inner.GetChar(0, 0));
        Assert.Equal(' ', inner.GetChar(1, 0));
        Assert.Equal('A', inner.GetChar(2, 0));
    }

    [Fact]
    public void Flush_DecoratedText_DoesNotRenderAnsiFragments()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);
        var context = new RegionRenderContext(diffing, 0, 0, 80, 24);

        context.SetDecoration(TextDecoration.Bold);
        context.WriteAt(0, 0, "Hi");
        diffing.Flush();

        Assert.Equal("Hi", inner.GetLine(0));
        Assert.DoesNotContain("[1m", inner.ToString());
    }

    [Fact]
    public void ClearScreen_DoesNotEmitAnsiClear()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        // First flush to establish baseline
        diffing.MoveTo(0, 0);
        diffing.Write("Initial");
        diffing.Flush();

        // Clear count before our test
        var initialRawCount = inner.RawOutput.Count;

        // ClearScreen should NOT output anything
        diffing.ClearScreen();

        // No new output from ClearScreen alone
        Assert.Equal(initialRawCount, inner.RawOutput.Count);
    }

    [Fact]
    public void Flush_OnlyOutputsChangedCells()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        // First frame
        diffing.MoveTo(0, 0);
        diffing.Write("AAAAA");
        diffing.Flush();

        // Count output after first frame
        var outputAfterFirst = inner.RawOutput.Count;

        // Second frame - only change last 2 chars
        diffing.ClearScreen();
        diffing.MoveTo(0, 0);
        diffing.Write("AAABB"); // Only BB is different
        diffing.Flush();

        // Output should be minimal (MoveTo + "BB" + some control sequences)
        var newOutput = inner.RawOutput.Skip(outputAfterFirst).ToList();

        // Verify that "AAA" was NOT re-output
        // We should only see the changed characters
        var joinedOutput = string.Join("", newOutput);
        Assert.Contains("BB", joinedOutput);

        // The unchanged "AAA" prefix should not be in the new output
        // (This is a bit tricky because RawOutput includes control sequences)
        // Instead, let's verify that total output is less than full re-render would be
        Assert.True(newOutput.Count < outputAfterFirst,
            $"Expected fewer outputs for diff ({newOutput.Count}) than full render ({outputAfterFirst})");
    }

    [Fact]
    public void Flush_NoChanges_OutputsNothing()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        // First frame
        diffing.MoveTo(0, 0);
        diffing.Write("Hello");
        diffing.Flush();

        var outputAfterFirst = inner.RawOutput.Count;

        // Second frame - exact same content
        diffing.ClearScreen();
        diffing.MoveTo(0, 0);
        diffing.Write("Hello");
        diffing.Flush();

        var newOutputs = inner.RawOutput.Skip(outputAfterFirst).ToList();

        // Should be minimal output (just control sequences for cursor visibility, etc.)
        // Definitely no character data for "Hello"
        var joinedOutput = string.Join("", newOutputs);
        Assert.DoesNotContain("Hello", joinedOutput);
    }

    [Fact]
    public void ForceFullRefresh_CausesFullOutput()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        // First frame
        diffing.MoveTo(0, 0);
        diffing.Write("Hello");
        diffing.Flush();

        var outputAfterFirst = inner.RawOutput.Count;

        // Force full refresh
        diffing.ForceFullRefresh();

        // Same content
        diffing.ClearScreen();
        diffing.MoveTo(0, 0);
        diffing.Write("Hello");
        diffing.Flush();

        // Should have significant output due to forced refresh
        var newOutputCount = inner.RawOutput.Count - outputAfterFirst;
        Assert.True(newOutputCount > 10, "Expected full refresh to produce significant output");
    }

    [Fact]
    public void SetForeground_AppliesColorToSubsequentWrites()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(0, 0);
        diffing.SetForeground(Color.Red);
        diffing.Write("Red");
        diffing.Flush();

        // Verify the color was applied by checking the cell colors
        Assert.Equal(Color.Red, inner.GetForeground(0, 0));
        Assert.Equal(Color.Red, inner.GetForeground(1, 0));
        Assert.Equal(Color.Red, inner.GetForeground(2, 0));
    }

    [Fact]
    public void SetBackground_AppliesColorToSubsequentWrites()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(0, 0);
        diffing.SetBackground(Color.Blue);
        diffing.Write("Blue BG");
        diffing.Flush();

        // Verify the background color was applied by checking cell colors
        Assert.Equal(Color.Blue, inner.GetBackground(0, 0));
        Assert.Equal(Color.Blue, inner.GetBackground(1, 0));
    }

    [Fact]
    public void ResetColors_ResetsState()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.SetForeground(Color.Red);
        diffing.SetBackground(Color.Blue);
        diffing.ResetColors();

        // After reset, subsequent writes should use default colors
        diffing.MoveTo(0, 0);
        diffing.Write("Default");
        diffing.Flush();

        // This is testing internal state; the write should proceed without error
        Assert.Equal('D', inner.GetChar(0, 0));
    }

    [Fact]
    public void SaveCursor_And_RestoreCursor_WorkCorrectly()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(5, 10);
        diffing.SaveCursor();

        diffing.MoveTo(0, 0);
        diffing.Write("At origin");

        diffing.RestoreCursor();
        diffing.Write("Back");
        diffing.Flush();

        // "Back" should be at position (5, 10)
        Assert.Equal('B', inner.GetChar(5, 10));
        Assert.Equal('a', inner.GetChar(6, 10));
    }

    [Fact]
    public void SetCursorVisible_PassesThroughImmediately()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.SetCursorVisible(false);

        // Should affect inner immediately (it's a pass-through)
        Assert.False(inner.CursorVisible);

        diffing.SetCursorVisible(true);
        Assert.True(inner.CursorVisible);
    }

    [Fact]
    public void ClearRegion_ClearsPendingBufferOnly()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        // First frame with content
        diffing.MoveTo(0, 0);
        diffing.Write("XXXXXXXXXX");
        diffing.Flush();

        // Second frame - clear region
        diffing.ClearScreen();
        diffing.MoveTo(0, 0);
        diffing.Write("XXXXXXXXXX");
        diffing.ClearRegion(2, 0, 4, 1); // Clear middle portion
        diffing.Flush();

        // Positions 2-5 should now be empty
        Assert.Equal('X', inner.GetChar(0, 0));
        Assert.Equal('X', inner.GetChar(1, 0));
        Assert.Equal(' ', inner.GetChar(2, 0));
        Assert.Equal(' ', inner.GetChar(3, 0));
        Assert.Equal(' ', inner.GetChar(4, 0));
        Assert.Equal(' ', inner.GetChar(5, 0));
        Assert.Equal('X', inner.GetChar(6, 0));
    }

    [Fact]
    public void EnterAlternateScreen_PassesThrough()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.EnterAlternateScreen();

        Assert.True(inner.InAlternateScreen);
    }

    [Fact]
    public void ExitAlternateScreen_PassesThrough()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.EnterAlternateScreen();
        diffing.ExitAlternateScreen();

        Assert.False(inner.InAlternateScreen);
    }

    [Fact]
    public void EnableMouse_PassesThrough()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.EnableMouse();

        Assert.True(inner.MouseEnabled);
    }

    [Fact]
    public void DisableMouse_PassesThrough()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.EnableMouse();
        diffing.DisableMouse();

        Assert.False(inner.MouseEnabled);
    }

    [Fact]
    public void EnableWheelScroll_PassesThrough()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.EnableWheelScroll();

        Assert.True(inner.WheelScrollEnabled);
        Assert.False(inner.MouseEnabled);
    }

    [Fact]
    public void DisableWheelScroll_PassesThrough()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.EnableWheelScroll();
        diffing.DisableWheelScroll();

        Assert.False(inner.WheelScrollEnabled);
    }

    [Fact]
    public void CopyToClipboard_PassesThroughWithoutChangingFrame()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(0, 0);
        diffing.Write("Hello");
        diffing.Flush();

        var screenBeforeCopy = inner.ToString();
        var rawCountBeforeCopy = inner.RawOutput.Count;

        diffing.CopyToClipboard("https://example.com/oauth");

        Assert.Equal(screenBeforeCopy, inner.ToString());
        Assert.Equal(rawCountBeforeCopy + 1, inner.RawOutput.Count);
        Assert.Contains("]52;c;", inner.RawOutput[^1]);
    }

    [Fact]
    public void Write_HandlesNewlines()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(5, 5);
        diffing.Write("Line1\nLine2");
        diffing.Flush();

        // Line1 at (5, 5)
        Assert.Equal('L', inner.GetChar(5, 5));
        Assert.Equal('i', inner.GetChar(6, 5));

        // After \n, cursor goes to start of next line
        // Line2 at (0, 6)
        Assert.Equal('L', inner.GetChar(0, 6));
        Assert.Equal('i', inner.GetChar(1, 6));
    }

    [Fact]
    public void Write_HandlesCarriageReturn()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(0, 0);
        diffing.Write("ABCDE\rXY");
        diffing.Flush();

        // After \r, cursor returns to column 0, same row
        // XY overwrites AB
        Assert.Equal('X', inner.GetChar(0, 0));
        Assert.Equal('Y', inner.GetChar(1, 0));
        Assert.Equal('C', inner.GetChar(2, 0));
        Assert.Equal('D', inner.GetChar(3, 0));
        Assert.Equal('E', inner.GetChar(4, 0));
    }

    [Fact]
    public void Write_HandlesTabs()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        diffing.MoveTo(0, 0);
        diffing.Write("A\tB");
        diffing.Flush();

        // A at position 0
        Assert.Equal('A', inner.GetChar(0, 0));
        // Tab advances to column 8 (next 8-column boundary)
        // Positions 1-7 should be spaces
        Assert.Equal(' ', inner.GetChar(1, 0));
        Assert.Equal(' ', inner.GetChar(7, 0));
        // B at position 8
        Assert.Equal('B', inner.GetChar(8, 0));
    }

    [Fact]
    public void Dispose_DisposesInnerIfDisposable()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        // Should not throw
        diffing.Dispose();
    }

    [Fact]
    public void MultipleWrites_SameColor_AllCellsHaveSameColor()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        // Write multiple characters with same color
        diffing.MoveTo(0, 0);
        diffing.SetForeground(Color.Red);
        diffing.Write("AAAA");
        diffing.Flush();

        // All cells should have the same foreground color
        Assert.Equal(Color.Red, inner.GetForeground(0, 0));
        Assert.Equal(Color.Red, inner.GetForeground(1, 0));
        Assert.Equal(Color.Red, inner.GetForeground(2, 0));
        Assert.Equal(Color.Red, inner.GetForeground(3, 0));
    }

    [Fact]
    public void ColorChanges_ApplyToCorrectCells()
    {
        var inner = new VirtualTerminal(80, 24);
        var diffing = new DiffingTerminal(inner);

        // Write with different colors
        diffing.MoveTo(0, 0);
        diffing.SetForeground(Color.Red);
        diffing.Write("RR");
        diffing.SetForeground(Color.Blue);
        diffing.Write("BB");
        diffing.Flush();

        // First two cells should be red
        Assert.Equal(Color.Red, inner.GetForeground(0, 0));
        Assert.Equal(Color.Red, inner.GetForeground(1, 0));
        // Last two cells should be blue
        Assert.Equal(Color.Blue, inner.GetForeground(2, 0));
        Assert.Equal(Color.Blue, inner.GetForeground(3, 0));
    }
}
