// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Rendering;
using Termina.Terminal;
using R3;

namespace Termina.Tests.Rendering;

/// <summary>
/// Tests for the TextInput component (v2).
/// </summary>
public class TextInputTests
{
    #region Constructor and Properties Tests

    [Fact]
    public void Constructor_DefaultValues()
    {
        var input = new TextInput();

        Assert.Equal(string.Empty, input.Text);
        Assert.Equal(string.Empty, input.Label);
        Assert.Equal(string.Empty, input.Placeholder);
        Assert.False(input.IsFocused);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void Text_SetAndGet()
    {
        var input = new TextInput { Text = "Hello" };

        Assert.Equal("Hello", input.Text);
    }

    [Fact]
    public void Text_SetNull_BecomesEmpty()
    {
        var input = new TextInput { Text = "Hello" };
        input.Text = null!;

        Assert.Equal(string.Empty, input.Text);
    }

    [Fact]
    public void Label_SetAndGet()
    {
        var input = new TextInput { Label = "Name" };

        Assert.Equal("Name", input.Label);
    }

    [Fact]
    public void Placeholder_SetAndGet()
    {
        var input = new TextInput { Placeholder = "Enter text..." };

        Assert.Equal("Enter text...", input.Placeholder);
    }

    [Fact]
    public void CursorPosition_ClampedToTextLength()
    {
        var input = new TextInput { Text = "Hello" };
        input.CursorPosition = 100;

        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void CursorPosition_ClampedToZero()
    {
        var input = new TextInput { Text = "Hello" };
        input.CursorPosition = -5;

        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void Text_WhenSetShorter_ClampsCursorPosition()
    {
        var input = new TextInput { Text = "Hello World" };
        input.CursorPosition = 10;
        input.Text = "Hi";

        Assert.Equal(2, input.CursorPosition);
    }

    #endregion

    #region HandleKey Tests

    [Fact]
    public void HandleKey_WhenNotFocused_ReturnsFalse()
    {
        var input = new TextInput { IsFocused = false };
        var key = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);

        var handled = input.HandleKey(key);

        Assert.False(handled);
    }

    [Fact]
    public void HandleKey_PrintableChar_InsertsAtCursor()
    {
        var input = new TextInput { IsFocused = true };
        var key = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);

        var handled = input.HandleKey(key);

        Assert.True(handled);
        Assert.Equal("a", input.Text);
        Assert.Equal(1, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_CjkPrintableChar_InsertsAtCursor()
    {
        var input = new TextInput { IsFocused = true };
        var key = new ConsoleKeyInfo('你', (ConsoleKey)0, false, false, false);

        var handled = input.HandleKey(key);

        Assert.True(handled);
        Assert.Equal("你", input.Text);
        Assert.Equal(1, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_MultipleChars_InsertsInOrder()
    {
        var input = new TextInput { IsFocused = true };

        input.HandleKey(new ConsoleKeyInfo('H', ConsoleKey.H, false, false, false));
        input.HandleKey(new ConsoleKeyInfo('i', ConsoleKey.I, false, false, false));

        Assert.Equal("Hi", input.Text);
        Assert.Equal(2, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_InsertInMiddle()
    {
        var input = new TextInput { IsFocused = true, Text = "Hllo" };
        input.CursorPosition = 1;

        input.HandleKey(new ConsoleKeyInfo('e', ConsoleKey.E, false, false, false));

        Assert.Equal("Hello", input.Text);
        Assert.Equal(2, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_Backspace_DeletesCharBeforeCursor()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 5;

        var handled = input.HandleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        Assert.True(handled);
        Assert.Equal("Hell", input.Text);
        Assert.Equal(4, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_Backspace_AtStartDoesNothing()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 0;

        input.HandleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        Assert.Equal("Hello", input.Text);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_Delete_DeletesCharAtCursor()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 0;

        var handled = input.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));

        Assert.True(handled);
        Assert.Equal("ello", input.Text);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_Delete_AtEndDoesNothing()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 5;

        input.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));

        Assert.Equal("Hello", input.Text);
        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_LeftArrow_MovesCursorLeft()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 3;

        var handled = input.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));

        Assert.True(handled);
        Assert.Equal(2, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_LeftArrow_AtStartDoesNothing()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 0;

        input.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));

        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_RightArrow_MovesCursorRight()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 2;

        var handled = input.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));

        Assert.True(handled);
        Assert.Equal(3, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_RightArrow_SkipsWholeEmojiTextElement()
    {
        var input = new TextInput { IsFocused = true, Text = "😀A" };
        input.CursorPosition = 0;

        input.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        input.HandleKey(new ConsoleKeyInfo('X', (ConsoleKey)0, false, false, false));

        Assert.Equal("😀XA", input.Text);
    }

    [Fact]
    public void HandleKey_Backspace_DeletesWholeEmojiTextElement()
    {
        var input = new TextInput { IsFocused = true, Text = "😀A" };
        input.CursorPosition = 2;

        input.HandleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        Assert.Equal("A", input.Text);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_RightArrow_AtEndDoesNothing()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 5;

        input.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));

        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_Home_MovesCursorToStart()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 3;

        var handled = input.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));

        Assert.True(handled);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_End_MovesCursorToEnd()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        input.CursorPosition = 0;

        var handled = input.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));

        Assert.True(handled);
        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void HandleKey_Enter_RaisesOnSubmit()
    {
        var input = new TextInput { IsFocused = true, Text = "Hello" };
        string? submittedText = null;
        input.Submitted.Subscribe(text => submittedText = text);

        var handled = input.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.True(handled);
        Assert.Equal("Hello", submittedText);
    }

    [Fact]
    public void HandleKey_NonPrintable_ReturnsFalse()
    {
        var input = new TextInput { IsFocused = true };
        var key = new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false);

        var handled = input.HandleKey(key);

        Assert.False(handled);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_ResetsTextAndCursor()
    {
        var input = new TextInput { Text = "Hello", IsFocused = true };
        input.CursorPosition = 3;

        input.Clear();

        Assert.Equal(string.Empty, input.Text);
        Assert.Equal(0, input.CursorPosition);
    }

    #endregion

    #region Render Tests

    [Fact]
    public void Render_EmptyUnfocused_RendersNothing()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 1);
        var input = new TextInput();

        input.Render(context);

        // Empty unfocused input renders empty or minimal content
        Assert.Equal("", terminal.GetLine(0).TrimEnd());
    }

    [Fact]
    public void Render_WithText_RendersText()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 1);
        var input = new TextInput { Text = "Hello" };

        input.Render(context);

        Assert.True(terminal.Contains("Hello"));
    }

    [Fact]
    public void Render_WithLabel_RendersLabelAndText()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 1);
        var input = new TextInput { Label = "Name", Text = "John" };

        input.Render(context);

        Assert.True(terminal.Contains("Name:"));
        Assert.True(terminal.Contains("John"));
    }

    [Fact]
    public void Render_FocusedWithCursor_ShowsCursor()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 1);
        var input = new TextInput { Text = "Hello", IsFocused = true };
        input.CursorPosition = 2;

        input.Render(context);

        // The text should be rendered
        Assert.True(terminal.Contains("Hello"));
    }

    [Fact]
    public void Render_WithPlaceholder_WhenEmpty_ShowsPlaceholder()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 1);
        var input = new TextInput { Placeholder = "Enter name..." };

        input.Render(context);

        Assert.True(terminal.Contains("Enter name..."));
    }

    [Fact]
    public void Render_WithPlaceholder_WhenHasText_HidesPlaceholder()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 1);
        var input = new TextInput { Placeholder = "Enter name...", Text = "John" };

        input.Render(context);

        Assert.True(terminal.Contains("John"));
        Assert.False(terminal.Contains("Enter name..."));
    }

    [Fact]
    public void Render_TruncatesToWidth()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);
        var input = new TextInput { Text = "Hello World This Is Long" };

        input.Render(context);

        var line = terminal.GetLine(0);
        Assert.True(line.Length <= 10);
    }

    #endregion

    #region Measure Tests

    [Fact]
    public void Measure_EmptyInput_ReturnsMinimumSize()
    {
        var input = new TextInput();

        var (width, height) = input.Measure(100, 100);

        Assert.Equal(1, height);
        Assert.True(width >= 1);
    }

    [Fact]
    public void Measure_WithText_ReturnsTextWidth()
    {
        var input = new TextInput { Text = "Hello" };

        var (width, height) = input.Measure(100, 100);

        Assert.Equal(1, height);
        Assert.True(width >= 5);
    }

    [Fact]
    public void Measure_WithLabel_IncludesLabelWidth()
    {
        var input = new TextInput { Label = "Name", Text = "John" };

        var (width, height) = input.Measure(100, 100);

        // Label "Name: " (6) + "John" (4) = 10 minimum
        Assert.True(width >= 10);
    }

    [Fact]
    public void Measure_ClampsToAvailableWidth()
    {
        var input = new TextInput { Text = "Hello World" };

        var (width, height) = input.Measure(5, 1);

        Assert.Equal(5, width);
        Assert.Equal(1, height);
    }

    #endregion

    #region Focus Change Tests

    [Fact]
    public void Focus_WhenGained_MarksDirty()
    {
        var input = new TextInput();
        var dirtyCount = 0;
        input.Dirty.Subscribe(_ => dirtyCount++);

        input.IsFocused = true;

        Assert.True(dirtyCount > 0);
    }

    [Fact]
    public void Focus_WhenLost_MarksDirty()
    {
        var input = new TextInput { IsFocused = true };
        var dirtyCount = 0;
        input.Dirty.Subscribe(_ => dirtyCount++);

        input.IsFocused = false;

        Assert.True(dirtyCount > 0);
    }

    #endregion
}
