// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// Tests for VirtualTerminal - the in-memory terminal implementation for testing.
/// </summary>
public class VirtualTerminalTests
{
    [Fact]
    public void Constructor_DefaultDimensions_Is80x24()
    {
        var terminal = new VirtualTerminal();

        Assert.Equal(80, terminal.Width);
        Assert.Equal(24, terminal.Height);
    }

    [Fact]
    public void Constructor_CustomDimensions_AreRespected()
    {
        var terminal = new VirtualTerminal(120, 40);

        Assert.Equal(120, terminal.Width);
        Assert.Equal(40, terminal.Height);
    }

    [Fact]
    public void Write_String_WritesToBufferAtCursorPosition()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(5, 0);

        terminal.Write("Hello");

        Assert.Equal("Hello", terminal.GetRegion(5, 0, 5, 1));
    }

    [Fact]
    public void Write_Emoji_PreservesWholeTextElementInLines()
    {
        var terminal = new VirtualTerminal(10, 2);

        terminal.Write("😀A");

        Assert.Equal("😀A", terminal.GetLine(0));
        Assert.True(terminal.Contains("😀"));
        Assert.Equal('A', terminal.GetChar(2, 0));
    }

    [Fact]
    public void Write_CombiningSequence_PreservesWholeTextElementInLines()
    {
        var terminal = new VirtualTerminal(10, 2);

        terminal.Write("e\u0301A");

        Assert.Equal("e\u0301A", terminal.GetLine(0));
        Assert.True(terminal.Contains("e\u0301"));
        Assert.Equal('A', terminal.GetChar(1, 0));
    }

    [Fact]
    public void Write_Char_WritesToBufferAtCursorPosition()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(0, 0);

        terminal.Write('X');

        Assert.Equal('X', terminal.GetChar(0, 0));
    }

    [Fact]
    public void Write_AdvancesCursor()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(0, 0);

        terminal.Write("ABC");

        Assert.Equal(3, terminal.CursorX);
        Assert.Equal(0, terminal.CursorY);
    }

    [Fact]
    public void Write_WrapsAtLineEnd()
    {
        var terminal = new VirtualTerminal(10, 5);
        terminal.MoveTo(8, 0);

        terminal.Write("ABCD");

        // AB at end of first line, CD at start of second line
        Assert.Equal('A', terminal.GetChar(8, 0));
        Assert.Equal('B', terminal.GetChar(9, 0));
        Assert.Equal('C', terminal.GetChar(0, 1));
        Assert.Equal('D', terminal.GetChar(1, 1));
    }

    [Fact]
    public void Write_NewlineResetsXAndAdvancesY()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(0, 0);

        terminal.Write("Line1\nLine2");

        Assert.Equal("Line1", terminal.GetLine(0));
        Assert.Equal("Line2", terminal.GetLine(1));
    }

    [Fact]
    public void Write_CarriageReturnResetsXOnly()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(5, 2);

        terminal.Write("\r");

        Assert.Equal(0, terminal.CursorX);
        Assert.Equal(2, terminal.CursorY);
    }

    [Fact]
    public void MoveTo_SetsCursorPosition()
    {
        var terminal = new VirtualTerminal();

        terminal.MoveTo(10, 5);

        Assert.Equal(10, terminal.CursorX);
        Assert.Equal(5, terminal.CursorY);
    }

    [Fact]
    public void MoveTo_ClampsToValidRange()
    {
        var terminal = new VirtualTerminal(80, 24);

        terminal.MoveTo(-5, -10);
        Assert.Equal(0, terminal.CursorX);
        Assert.Equal(0, terminal.CursorY);

        terminal.MoveTo(100, 50);
        Assert.Equal(79, terminal.CursorX);
        Assert.Equal(23, terminal.CursorY);
    }

    [Fact]
    public void SetForeground_AffectsSubsequentWrites()
    {
        var terminal = new VirtualTerminal();
        terminal.SetForeground(Color.Red);
        terminal.MoveTo(0, 0);

        terminal.Write("X");

        Assert.Equal(Color.Red, terminal.GetForeground(0, 0));
    }

    [Fact]
    public void SetBackground_AffectsSubsequentWrites()
    {
        var terminal = new VirtualTerminal();
        terminal.SetBackground(Color.Blue);
        terminal.MoveTo(0, 0);

        terminal.Write("X");

        Assert.Equal(Color.Blue, terminal.GetBackground(0, 0));
    }

    [Fact]
    public void ResetColors_SetsColorsToDefault()
    {
        var terminal = new VirtualTerminal();
        terminal.SetForeground(Color.Red);
        terminal.SetBackground(Color.Blue);

        terminal.ResetColors();
        terminal.MoveTo(0, 0);
        terminal.Write("X");

        Assert.Equal(Color.Default, terminal.GetForeground(0, 0));
        Assert.Equal(Color.Default, terminal.GetBackground(0, 0));
    }

    [Fact]
    public void SaveCursor_RestoreCursor_PreservesPosition()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(10, 5);
        terminal.SaveCursor();

        terminal.MoveTo(0, 0);
        terminal.RestoreCursor();

        Assert.Equal(10, terminal.CursorX);
        Assert.Equal(5, terminal.CursorY);
    }

    [Fact]
    public void SetCursorVisible_UpdatesProperty()
    {
        var terminal = new VirtualTerminal();
        Assert.True(terminal.CursorVisible); // default

        terminal.SetCursorVisible(false);
        Assert.False(terminal.CursorVisible);

        terminal.SetCursorVisible(true);
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public void ClearRegion_FillsWithSpaces()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(0, 0);
        terminal.Write("XXXXXXXXXX");

        terminal.ClearRegion(2, 0, 5, 1);

        Assert.Equal('X', terminal.GetChar(0, 0));
        Assert.Equal('X', terminal.GetChar(1, 0));
        Assert.Equal(' ', terminal.GetChar(2, 0));
        Assert.Equal(' ', terminal.GetChar(6, 0));
        Assert.Equal('X', terminal.GetChar(7, 0));
    }

    [Fact]
    public void ClearRegion_ResetsColors()
    {
        var terminal = new VirtualTerminal();
        terminal.SetForeground(Color.Red);
        terminal.SetBackground(Color.Blue);
        terminal.MoveTo(0, 0);
        terminal.Write("XXXXX");

        terminal.ClearRegion(0, 0, 5, 1);

        Assert.Equal(Color.Default, terminal.GetForeground(0, 0));
        Assert.Equal(Color.Default, terminal.GetBackground(0, 0));
    }

    [Fact]
    public void ClearScreen_ClearsEntireBuffer()
    {
        var terminal = new VirtualTerminal(10, 5);
        terminal.MoveTo(0, 0);
        terminal.Write("Line 1");
        terminal.MoveTo(0, 1);
        terminal.Write("Line 2");

        terminal.ClearScreen();

        Assert.Equal("", terminal.GetLine(0));
        Assert.Equal("", terminal.GetLine(1));
        Assert.Equal(0, terminal.CursorX);
        Assert.Equal(0, terminal.CursorY);
    }

    [Fact]
    public void EnterAlternateScreen_SetsFlag()
    {
        var terminal = new VirtualTerminal();
        Assert.False(terminal.InAlternateScreen);

        terminal.EnterAlternateScreen();

        Assert.True(terminal.InAlternateScreen);
    }

    [Fact]
    public void ExitAlternateScreen_ClearsFlag()
    {
        var terminal = new VirtualTerminal();
        terminal.EnterAlternateScreen();

        terminal.ExitAlternateScreen();

        Assert.False(terminal.InAlternateScreen);
    }

    [Fact]
    public void EnableMouse_SetsFlag()
    {
        var terminal = new VirtualTerminal();
        Assert.False(terminal.MouseEnabled);

        terminal.EnableMouse();

        Assert.True(terminal.MouseEnabled);
    }

    [Fact]
    public void DisableMouse_ClearsFlag()
    {
        var terminal = new VirtualTerminal();
        terminal.EnableMouse();

        terminal.DisableMouse();

        Assert.False(terminal.MouseEnabled);
    }

    [Fact]
    public void EnableWheelScroll_SetsFlag()
    {
        var terminal = new VirtualTerminal();
        Assert.False(terminal.WheelScrollEnabled);

        terminal.EnableWheelScroll();

        Assert.True(terminal.WheelScrollEnabled);
        Assert.False(terminal.MouseEnabled);
    }

    [Fact]
    public void DisableWheelScroll_ClearsFlag()
    {
        var terminal = new VirtualTerminal();
        terminal.EnableWheelScroll();

        terminal.DisableWheelScroll();

        Assert.False(terminal.WheelScrollEnabled);
    }

    [Fact]
    public void GetLine_ReturnsCorrectContent()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(0, 0);
        terminal.Write("Hello World");

        Assert.Equal("Hello World", terminal.GetLine(0));
    }

    [Fact]
    public void GetLine_OutOfRange_ReturnsEmpty()
    {
        var terminal = new VirtualTerminal(80, 24);

        Assert.Equal("", terminal.GetLine(-1));
        Assert.Equal("", terminal.GetLine(100));
    }

    [Fact]
    public void GetRegion_ReturnsMultipleLines()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(0, 0);
        terminal.Write("ABC");
        terminal.MoveTo(0, 1);
        terminal.Write("DEF");
        terminal.MoveTo(0, 2);
        terminal.Write("GHI");

        var region = terminal.GetRegion(0, 0, 3, 3);

        Assert.Contains("ABC", region);
        Assert.Contains("DEF", region);
        Assert.Contains("GHI", region);
    }

    [Fact]
    public void Contains_FindsText()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(5, 5);
        terminal.Write("SearchMe");

        Assert.True(terminal.Contains("SearchMe"));
        Assert.False(terminal.Contains("NotFound"));
    }

    [Fact]
    public void GetAllLines_ReturnsAllLines()
    {
        var terminal = new VirtualTerminal(80, 5);
        terminal.MoveTo(0, 0);
        terminal.Write("Line0");
        terminal.MoveTo(0, 2);
        terminal.Write("Line2");

        var lines = terminal.GetAllLines();

        Assert.Equal(5, lines.Length);
        Assert.Equal("Line0", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal("Line2", lines[2]);
    }

    [Fact]
    public void ToString_ReturnsFullScreen()
    {
        var terminal = new VirtualTerminal(10, 3);
        terminal.MoveTo(0, 0);
        terminal.Write("Hello");
        terminal.MoveTo(0, 2);
        terminal.Write("World");

        var output = terminal.ToString();

        Assert.Contains("Hello", output);
        Assert.Contains("World", output);
    }

    [Fact]
    public void Resize_PreservesExistingContent()
    {
        var terminal = new VirtualTerminal(10, 5);
        terminal.MoveTo(0, 0);
        terminal.Write("Keep");

        terminal.Resize(20, 10);

        Assert.Equal(20, terminal.Width);
        Assert.Equal(10, terminal.Height);
        Assert.Equal("Keep", terminal.GetRegion(0, 0, 4, 1));
    }

    [Fact]
    public void Resize_ClampsCursor()
    {
        var terminal = new VirtualTerminal(20, 20);
        terminal.MoveTo(15, 15);

        terminal.Resize(10, 10);

        Assert.Equal(9, terminal.CursorX);
        Assert.Equal(9, terminal.CursorY);
    }

    [Fact]
    public void RawOutput_CapturesAllWrites()
    {
        var terminal = new VirtualTerminal();
        terminal.Write("First");
        terminal.Write("Second");
        terminal.Write('X');

        Assert.Equal(3, terminal.RawOutput.Count);
        Assert.Equal("First", terminal.RawOutput[0]);
        Assert.Equal("Second", terminal.RawOutput[1]);
        Assert.Equal("X", terminal.RawOutput[2]);
    }

    [Fact]
    public void Clear_DoesNotResetCursor()
    {
        var terminal = new VirtualTerminal();
        terminal.MoveTo(10, 5);
        terminal.Write("Text");

        terminal.Clear();

        // Clear only clears buffer, not cursor
        Assert.Equal(' ', terminal.GetChar(10, 5));
    }

    [Fact]
    public void CopyToClipboard_CapturesOsc52SequenceInRawOutput()
    {
        var terminal = new VirtualTerminal();

        terminal.CopyToClipboard("Hello");

        Assert.Single(terminal.RawOutput);
        Assert.Equal("\x1b]52;c;SGVsbG8=\x07", terminal.RawOutput[0]);
    }

    [Fact]
    public void Flush_IsNoOp()
    {
        var terminal = new VirtualTerminal();
        terminal.Write("Test");

        // Should not throw
        terminal.Flush();

        // Content should still be there
        Assert.True(terminal.Contains("Test"));
    }

    [Fact]
    public void GetChar_OutOfRange_ReturnsSpace()
    {
        var terminal = new VirtualTerminal(80, 24);

        Assert.Equal(' ', terminal.GetChar(-1, 0));
        Assert.Equal(' ', terminal.GetChar(0, -1));
        Assert.Equal(' ', terminal.GetChar(100, 0));
        Assert.Equal(' ', terminal.GetChar(0, 100));
    }

    [Fact]
    public void GetForeground_OutOfRange_ReturnsDefault()
    {
        var terminal = new VirtualTerminal(80, 24);

        Assert.Equal(Color.Default, terminal.GetForeground(-1, 0));
        Assert.Equal(Color.Default, terminal.GetForeground(100, 0));
    }

    [Fact]
    public void GetBackground_OutOfRange_ReturnsDefault()
    {
        var terminal = new VirtualTerminal(80, 24);

        Assert.Equal(Color.Default, terminal.GetBackground(-1, 0));
        Assert.Equal(Color.Default, terminal.GetBackground(100, 0));
    }
}
