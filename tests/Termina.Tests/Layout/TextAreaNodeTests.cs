// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Time.Testing;
using R3;
using Termina.Input;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// Tests for the <see cref="TextAreaNode"/> multi-line text input component.
/// </summary>
public class TextAreaNodeTests : IDisposable
{
    private readonly TextAreaNode _node;

    public TextAreaNodeTests()
    {
        _node = new TextAreaNode();
    }

    public void Dispose()
    {
        _node.Dispose();
    }

    #region Basic Input

    [Fact]
    public void TypeText_SetsTextProperty()
    {
        TypeText("hello world");
        Assert.Equal("hello world", _node.Text);
    }

    [Fact]
    public void TypeText_FiresTextChanged()
    {
        var changeCount = 0;
        _node.TextChanged.Subscribe(_ => changeCount++);

        TypeText("abc");

        Assert.Equal(3, changeCount);
    }

    [Fact]
    public void Clear_ResetsTextAndCursor()
    {
        TypeText("hello");
        _node.Clear();
        Assert.Equal("", _node.Text);
    }

    [Fact]
    public void TextSetter_MultiLineContent_ShowsSummary()
    {
        // Multi-line content via Text setter shows summary (same as TextInputNode)
        _node.Text = "line1\nline2\nline3";
        Assert.Equal("[Pasted 3 lines, 17 chars] ", _node.Text);
    }

    [Fact]
    public void Backspace_DeletesCharacter()
    {
        TypeText("hello");
        PressBackspace();
        Assert.Equal("hell", _node.Text);
    }

    [Fact]
    public void Delete_DeletesCharacterAhead()
    {
        TypeText("hello");
        PressHome();
        PressDelete();
        Assert.Equal("ello", _node.Text);
    }

    [Fact]
    public void Escape_ClearsText()
    {
        TypeText("hello");
        PressEscape();
        Assert.Equal("", _node.Text);
    }

    #endregion

    #region Newline Insertion (Ctrl+Enter and Alt+Enter)

    [Fact]
    public void CtrlEnter_InsertsNewline()
    {
        TypeText("hello");
        InsertNewline();
        TypeText("world");

        Assert.Equal("hello\nworld", _node.Text);
    }

    [Fact]
    public void CtrlEnter_FiresTextChanged_NotSubmitted()
    {
        var submittedCount = 0;
        string? lastTextChanged = null;
        _node.Submitted.Subscribe(_ => submittedCount++);
        _node.TextChanged.Subscribe(t => lastTextChanged = t);

        TypeText("hello");
        InsertNewline();

        Assert.Equal(0, submittedCount);
        Assert.Equal("hello\n", lastTextChanged);
    }

    [Fact]
    public void CtrlEnter_AtMiddle_InsertsNewlineAtCursor()
    {
        TypeText("helloworld");
        // Move cursor left 5 times (between "hello" and "world")
        for (var i = 0; i < 5; i++)
            PressLeftArrow();

        InsertNewline();

        Assert.Equal("hello\nworld", _node.Text);
    }

    [Fact]
    public void MultipleCtrlEnters_InsertMultipleNewlines()
    {
        TypeText("a");
        InsertNewline();
        TypeText("b");
        InsertNewline();
        TypeText("c");

        Assert.Equal("a\nb\nc", _node.Text);
    }

    [Fact]
    public void DoubleNewline_ThenType_CursorStaysOnCorrectLine()
    {
        // Regression: inserting two consecutive newlines followed by typing should
        // place all typed characters on the third line — the cursor must not jump up.
        TypeText("hello");
        InsertNewline();
        InsertNewline();

        // At this point text is "hello\n\n" with cursor on the 3rd visual line.
        // Measure to trigger line computation.
        _node.Measure(new Size(80, 10));

        TypeText("world");
        Assert.Equal("hello\n\nworld", _node.Text);

        // Measure again — should be exactly 3 visual lines
        var size = _node.Measure(new Size(80, 10));
        Assert.Equal(3, size.Height);
    }

    [Fact]
    public void AltEnter_InsertsNewline()
    {
        TypeText("hello");
        PressAltEnter();
        TypeText("world");

        Assert.Equal("hello\nworld", _node.Text);
    }

    [Fact]
    public void AltEnter_DoesNotSubmit()
    {
        var submittedCount = 0;
        _node.Submitted.Subscribe(_ => submittedCount++);

        TypeText("hello");
        PressAltEnter();

        Assert.Equal(0, submittedCount);
        Assert.Equal("hello\n", _node.Text);
    }

    [Fact]
    public void AltEnter_ViaEscapeSequenceParser_InsertsNewline()
    {
        // End-to-end test: ESC + Enter through EscapeSequenceParser → TextAreaNode
        // This simulates what actually happens on a real terminal.
        var parser = new EscapeSequenceParser();
        using var node = new TextAreaNode();

        // Type "hello"
        foreach (var c in "hello")
            node.HandleInput(new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false));

        // Simulate Alt+Enter via ESC prefix (how real terminals send it)
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)));
        events.AddRange(parser.Process(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));

        // Feed parsed events into the node
        foreach (var evt in events)
        {
            if (evt is KeyPressed kp)
                node.HandleInput(kp.KeyInfo);
        }

        // Type "world"
        foreach (var c in "world")
            node.HandleInput(new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false));

        Assert.Equal("hello\nworld", node.Text);
    }

    [Fact]
    public void CsiU_CtrlEnter_ViaEscapeSequenceParser_InsertsNewline()
    {
        // End-to-end test: CSI u Ctrl+Enter (ESC[13;5u) through parser → TextAreaNode
        // This simulates kitty keyboard protocol on terminals that support it.
        var parser = new EscapeSequenceParser();
        using var node = new TextAreaNode();

        // Type "hello"
        foreach (var c in "hello")
            node.HandleInput(new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false));

        // Simulate CSI u Ctrl+Enter: ESC [ 1 3 ; 5 u
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)));
        foreach (var c in "[13;5u")
            events.AddRange(parser.Process(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false)));

        // Feed parsed events into the node
        foreach (var evt in events)
        {
            if (evt is KeyPressed kp)
                node.HandleInput(kp.KeyInfo);
        }

        // Type "world"
        foreach (var c in "world")
            node.HandleInput(new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false));

        Assert.Equal("hello\nworld", node.Text);
    }

    [Fact]
    public void BareEnter_Submits_DoesNotInsertNewline()
    {
        string? submitted = null;
        _node.Submitted.Subscribe(t => submitted = t);

        TypeText("hello");
        Submit(); // bare Enter

        Assert.Equal("hello", submitted);
    }

    #endregion

    #region Submit (Enter)

    [Fact]
    public void Enter_SubmitsContent()
    {
        string? submitted = null;
        _node.Submitted.Subscribe(t => submitted = t);

        TypeText("line1");
        InsertNewline();
        TypeText("line2");

        Submit();

        Assert.Equal("line1\nline2", submitted);
    }

    [Fact]
    public void Enter_ClearsInputAfterSubmit()
    {
        TypeText("hello world");
        Submit();

        Assert.Equal("", _node.Text);
    }

    [Fact]
    public void Enter_ClearsPasteAndTextAfterSubmit()
    {
        // Paste + type + submit should clear everything
        _node.HandlePaste(new PasteEvent("pasted\ncontent"));
        TypeText("typed");
        Submit();

        Assert.Equal("", _node.Text);
    }

    [Fact]
    public void Enter_SubmitsFullMultiLineContent()
    {
        string? submitted = null;
        _node.Submitted.Subscribe(t => submitted = t);

        TypeText("first");
        InsertNewline();
        TypeText("second");
        InsertNewline();
        TypeText("third");

        Submit();

        Assert.Equal("first\nsecond\nthird", submitted);
    }

    [Fact]
    public void CustomNewlineModifier_ShiftEnter()
    {
        using var node = new TextAreaNode().WithNewlineModifier(ConsoleModifiers.Shift);
        string? submitted = null;
        node.Submitted.Subscribe(t => submitted = t);

        TypeText(node, "test");
        // Shift+Enter should insert newline, bare Enter should submit
        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, true, false, false));
        Assert.Null(submitted); // Shift+Enter inserted newline, didn't submit
        Assert.Equal("test\n", node.Text);

        // Bare Enter submits
        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        Assert.Equal("test\n", submitted);
    }

    #endregion

    #region Up/Down Visual Line Navigation

    [Fact]
    public void UpArrow_WithContent_NavigatesToPreviousVisualLine()
    {
        TypeText("line1");
        InsertNewline();
        TypeText("line2");

        // Cursor is at end of "line2" — pressing Up should go to "line1"
        PressUpArrow();

        // Type a character to verify cursor position
        TypeText("X");
        Assert.Equal("line1X\nline2", _node.Text);
    }

    [Fact]
    public void DownArrow_WithContent_NavigatesToNextVisualLine()
    {
        TypeText("line1");
        InsertNewline();
        TypeText("line2");

        // Go to top
        PressUpArrow();
        PressHome();

        // Down arrow should go to start of line2
        PressDownArrow();
        TypeText("X");
        Assert.Equal("line1\nXline2", _node.Text);
    }

    [Fact]
    public void UpArrow_AtTopLine_ConsumesKey()
    {
        TypeText("only one line");
        var handled = _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.True(handled);
    }

    [Fact]
    public void DownArrow_AtBottomLine_ConsumesKey()
    {
        TypeText("only one line");
        var handled = _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.True(handled);
    }

    [Fact]
    public void UpArrow_ClampsColumnToShorterLine()
    {
        TypeText("short");
        InsertNewline();
        TypeText("much longer line");

        // Cursor is at col 16 of "much longer line"
        // Up should go to col 5 (end of "short")
        PressUpArrow();
        TypeText("X");
        Assert.Equal("shortX\nmuch longer line", _node.Text);
    }

    #endregion

    #region Up/Down History (when empty)

    [Fact]
    public void UpArrow_WhenEmpty_NoHistory_ReturnsFalse()
    {
        // No history enabled, text empty
        var handled = _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.False(handled);
    }

    [Fact]
    public void DownArrow_WhenEmpty_NoHistory_ReturnsFalse()
    {
        var handled = _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.False(handled);
    }

    [Fact]
    public void UpArrow_WhenEmpty_WithHistory_RecallsEntry()
    {
        using var node = new TextAreaNode().WithHistory();

        TypeText(node, "previous entry");
        Submit(node);
        node.Clear();

        PressUpArrow(node);
        Assert.Equal("previous entry", node.Text);
    }

    [Fact]
    public void DownArrow_WhenEmpty_WithHistory_ReturnsTrue()
    {
        using var node = new TextAreaNode().WithHistory();

        TypeText(node, "entry");
        Submit(node);
        node.Clear();

        // Down arrow with empty text and history enabled — should consume the key
        var handled = node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.True(handled);
    }

    #endregion

    #region Home/End

    [Fact]
    public void Home_GoesToStartOfVisualLine()
    {
        TypeText("line1");
        InsertNewline();
        TypeText("line2");

        // Cursor at end of line2
        PressHome();
        TypeText("X");
        Assert.Equal("line1\nXline2", _node.Text);
    }

    [Fact]
    public void End_GoesToEndOfVisualLine()
    {
        TypeText("line1");
        InsertNewline();
        TypeText("line2");

        // Go to start of line2, then End
        PressHome();
        PressEnd();
        TypeText("X");
        Assert.Equal("line1\nline2X", _node.Text);
    }

    [Fact]
    public void CtrlHome_GoesToDocumentStart()
    {
        TypeText("line1");
        InsertNewline();
        TypeText("line2");

        // Ctrl+Home — go to document start
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, true));
        TypeText("X");
        Assert.Equal("Xline1\nline2", _node.Text);
    }

    [Fact]
    public void CtrlEnd_GoesToDocumentEnd()
    {
        TypeText("line1");
        InsertNewline();
        TypeText("line2");

        // Go to document start first
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, true));

        // Ctrl+End — go to document end
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, true));
        TypeText("X");
        Assert.Equal("line1\nline2X", _node.Text);
    }

    #endregion

    #region Word Wrap

    [Fact]
    public void WordWrap_WrapsAtWidth()
    {
        _node.Text = "hello world";

        // Measure with width 7 — should wrap "hello" and "world" to separate lines
        var size = _node.Measure(new Size(7, 10));

        Assert.Equal(7, size.Width);
        Assert.True(size.Height >= 2, $"Expected height >= 2, got {size.Height}");
    }

    [Fact]
    public void WordWrap_Disabled_DoesNotWrap()
    {
        using var node = new TextAreaNode().WithWordWrap(false);
        node.Text = "hello world";

        var size = node.Measure(new Size(7, 10));

        // Without word wrap, single logical line
        Assert.Equal(1, size.Height);
    }

    [Fact]
    public void WordWrap_ReWrapsOnWidthChange()
    {
        _node.Text = "hello world foo";

        // At width 10, "hello" and "world foo" might be on two lines
        var size1 = _node.Measure(new Size(10, 20));
        // At width 5, each word gets its own line
        var size2 = _node.Measure(new Size(5, 20));

        Assert.True(size2.Height >= size1.Height,
            $"Narrower width should produce more lines: {size2.Height} >= {size1.Height}");
    }

    #endregion

    #region Vertical Scroll

    [Fact]
    public void VerticalScroll_CursorStaysVisible()
    {
        // Type many lines in a small viewport
        for (var i = 0; i < 15; i++)
        {
            TypeText($"line{i}");
            InsertNewline();
        }

        // Measure with small height — should still work without error
        var size = _node.Measure(new Size(80, 5));
        Assert.True(size.Height <= 5);
    }

    #endregion

    #region Paste

    [Fact]
    public void Paste_SingleLine_InsertsInline()
    {
        TypeText("hello ");
        _node.HandlePaste(new PasteEvent("world"));
        Assert.Equal("hello world", _node.Text);
    }

    [Fact]
    public void Paste_MultiLine_ShowsSummary()
    {
        // Multi-line paste shows summary (same behavior as TextInputNode)
        _node.HandlePaste(new PasteEvent("line1\nline2\nline3"));
        Assert.Contains("[Pasted 3 lines, 17 chars]", _node.Text);
    }

    [Fact]
    public void Paste_MultiLine_CreatesSummary()
    {
        _node.HandlePaste(new PasteEvent("hello\nworld"));
        Assert.Contains("[Pasted 2 lines, 11 chars]", _node.Text);
    }

    [Fact]
    public void Paste_MultiLine_ThenSubmit_ReturnsRawContent()
    {
        string? submitted = null;
        _node.Submitted.Subscribe(t => submitted = t);

        _node.HandlePaste(new PasteEvent("first\nsecond"));
        Submit();

        // SubmitContent returns the original paste content, not the summary
        Assert.Equal("first\nsecond", submitted);
    }

    [Fact]
    public void Paste_MultiLine_AtCursorPosition_CommitsPrefix()
    {
        TypeText("helloworld");
        // Move cursor left 5 times to position between "hello" and "world"
        for (var i = 0; i < 5; i++)
            PressLeftArrow();

        _node.HandlePaste(new PasteEvent("brave\nnew"));
        // "hello" becomes a Typed committed segment, paste becomes Pasted summary, "world" remains as _text
        Assert.Contains("hello", _node.Text);
        Assert.Contains("[Pasted 2 lines, 9 chars]", _node.Text);
        Assert.Contains("world", _node.Text);
    }

    [Fact]
    public void Paste_WithSelection_ReplacesSelection()
    {
        TypeText("hello world");
        // Select "world" — Home, then Shift+End would select all, instead use select all
        _node.HandleInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, true)); // Ctrl+A
        _node.HandlePaste(new PasteEvent("replaced"));
        Assert.Equal("replaced", _node.Text);
    }

    [Fact]
    public void Paste_Empty_ReturnsFalse()
    {
        var result = _node.HandlePaste(new PasteEvent(""));
        Assert.False(result);
    }

    #endregion

    #region Measure

    [Fact]
    public void Measure_SingleLine_ReturnsHeight1()
    {
        TypeText("hello");
        var size = _node.Measure(new Size(80, 10));
        Assert.Equal(1, size.Height);
    }

    [Fact]
    public void Measure_MultipleLines_ReturnsLineCount()
    {
        TypeText("line1");
        InsertNewline();
        TypeText("line2");
        InsertNewline();
        TypeText("line3");

        var size = _node.Measure(new Size(80, 10));
        Assert.Equal(3, size.Height);
    }

    [Fact]
    public void Measure_TrailingNewline_CorrectLineCount()
    {
        // "hello\n" should be 2 lines (text line + empty line after newline)
        TypeText("hello");
        InsertNewline();

        var size = _node.Measure(new Size(80, 10));
        Assert.Equal(2, size.Height);
    }

    [Fact]
    public void Measure_TwoTrailingNewlines_CorrectLineCount()
    {
        // "hello\n\n" should be 3 lines, not 4
        TypeText("hello");
        InsertNewline();
        InsertNewline();

        var size = _node.Measure(new Size(80, 10));
        Assert.Equal(3, size.Height);
    }

    [Fact]
    public void Measure_ClampedByMaxHeight()
    {
        using var node = new TextAreaNode().WithMaxHeight(3);

        TypeText(node, "a");
        InsertNewline(node);
        TypeText(node, "b");
        InsertNewline(node);
        TypeText(node, "c");
        InsertNewline(node);
        TypeText(node, "d");
        InsertNewline(node);
        TypeText(node, "e");

        var size = node.Measure(new Size(80, 20));
        Assert.Equal(3, size.Height);
    }

    [Fact]
    public void Measure_EmptyText_ReturnsMinHeight()
    {
        var size = _node.Measure(new Size(80, 10));
        Assert.True(size.Height >= 1);
    }

    #endregion

    #region MaxLines

    [Fact]
    public void MaxLines_PreventsExcessiveNewlines()
    {
        using var node = new TextAreaNode().WithMaxLines(3);

        TypeText(node, "line1");
        InsertNewline(node);
        TypeText(node, "line2");
        InsertNewline(node);
        TypeText(node, "line3");
        InsertNewline(node); // This should be consumed but NOT insert a newline

        Assert.Equal("line1\nline2\nline3", node.Text);
    }

    [Fact]
    public void MaxLines_StillAllowsTyping()
    {
        using var node = new TextAreaNode().WithMaxLines(2);

        TypeText(node, "line1");
        InsertNewline(node);
        TypeText(node, "line2");
        InsertNewline(node); // At limit, consumed
        TypeText(node, "more text"); // Should still append to current line

        Assert.Equal("line1\nline2more text", node.Text);
    }

    [Fact]
    public void MaxLines_CtrlEnterConsumed_ReturnsTrue()
    {
        using var node = new TextAreaNode().WithMaxLines(1);

        TypeText(node, "only line");
        // Ctrl+Enter inserts newline — but at max lines, should be consumed without inserting
        var handled = node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, control: true));

        Assert.True(handled);
        Assert.Equal("only line", node.Text);
    }

    #endregion

    #region Cursor Blink with FakeTimeProvider

    [Fact]
    public void CursorBlink_TogglesWithTimeProvider()
    {
        var timeProvider = new FakeTimeProvider();
        using var node = new TextAreaNode(cursorBlinkMs: 100);
        LayoutRuntimeContextInjector.Apply(
            node,
            new LayoutRuntimeContext(new TestFrameProvider(), timeProvider, () => { }));

        node.OnFocused();
        Assert.True(node.IsAnimating);

        // Advance time to trigger blink
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));

        // Node should have invalidated (cursor toggled)
        // We verify by checking IsAnimating is still true (timer running)
        Assert.True(node.IsAnimating);
    }

    private sealed class TestFrameProvider : FrameProvider
    {
        public override long GetFrameCount() => 0;

        public override void Register(IFrameRunnerWorkItem callback)
        {
        }
    }

    #endregion

    #region Selection

    [Fact]
    public void SelectAll_ThenType_ReplacesText()
    {
        TypeText("hello world");
        _node.HandleInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, true)); // Ctrl+A
        TypeText("new text");
        Assert.Equal("new text", _node.Text);
    }

    [Fact]
    public void ShiftArrow_CreatesSelection()
    {
        TypeText("hello");
        // Shift+Left x3 selects "llo"
        for (var i = 0; i < 3; i++)
            _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, true, false, false));

        Assert.True(_node.HasSelection);
        Assert.Equal("llo", _node.SelectedText);
    }

    #endregion

    #region Rendering

    [Fact]
    public void Render_DoesNotThrow_WithContent()
    {
        TypeText("line1");
        InsertNewline();
        TypeText("line2");

        var context = new TestRenderContext(80, 10);
        _node.Measure(new Size(80, 10));
        _node.Render(context, new Rect(0, 0, 80, 10));
    }

    [Fact]
    public void Render_ShowsPlaceholder_WhenEmpty()
    {
        using var node = new TextAreaNode().WithPlaceholder("Type here...");
        node.OnFocused(); // Need focus for cursor to show

        var context = new TestRenderContext(80, 10);
        node.Measure(new Size(80, 10));
        node.Render(context, new Rect(0, 0, 80, 10));

        // Cursor overwrites first char at position 0, so check for "ype here..."
        var row0 = context.GetContent(0);
        Assert.Contains("ype here...", row0);
    }

    #endregion

    #region Cursor Placement

    [Fact]
    public void TextSetter_KeepsCursorPosition()
    {
        _node.Text = "abc";
        TypeText("x");

        Assert.Equal("xabc", _node.Text);
    }

    [Fact]
    public void MoveCursorToEnd_AfterSeedingText_AppendsTypedCharacter()
    {
        _node.Text = "seed";
        _node.MoveCursorToEnd();

        TypeText("x");

        Assert.Equal("seedx", _node.Text);
    }

    [Fact]
    public void CursorPosition_MultiLineText_InsertsOnRequestedLine()
    {
        TypeText("ab");
        InsertNewline();
        TypeText("cd");

        _node.CursorPosition = 4;
        TypeText("X");
        Assert.Equal("ab\ncXd", _node.Text);

        _node.MoveCursorToEnd();
        TypeText("Z");
        Assert.Equal("ab\ncXdZ", _node.Text);
    }

    [Fact]
    public void CursorPosition_InsideTextElement_MovesToElementStart()
    {
        _node.Text = "a\U0001F44D\U0001F3FDb";

        _node.CursorPosition = 3;

        Assert.Equal(1, _node.CursorPosition);
    }

    [Fact]
    public void CursorPosition_ClearsSelectionAndDoesNotEmitTextChanged()
    {
        TypeText("abcd");
        _node.HandleInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, true));
        Assert.True(_node.HasSelection);
        var changeCount = 0;
        _node.TextChanged.Subscribe(_ => changeCount++);

        _node.CursorPosition = 1;

        Assert.False(_node.HasSelection);
        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void CursorPosition_EmitsInvalidated()
    {
        _node.Text = "abcd";
        var invalidations = 0;
        _node.Invalidated.Subscribe(_ => invalidations++);

        _node.MoveCursorToEnd();

        Assert.Equal(1, invalidations);
    }

    [Fact]
    public void CursorPosition_IndexesEditableTextAfterPastedSegment()
    {
        _node.Text = "line1\nline2";
        Assert.StartsWith("[Pasted", _node.Text);

        _node.CursorPosition = 50;
        Assert.Equal(0, _node.CursorPosition);

        _node.MoveCursorToEnd();
        TypeText("x");

        Assert.EndsWith("x", _node.Text);
    }

    [Fact]
    public void Render_AfterMoveCursorToEnd_ShowsLastLineWithCursor()
    {
        // 25 characters wrap to rows of 10, 10 and 5 columns. A two-row viewport must show the last two rows.
        var terminal = new VirtualTerminal(10, 2);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 2);
        _node.Text = "abcdefghijklmnopqrstuvwxy";
        _node.MoveCursorToEnd();

        _node.Measure(new Size(10, 2));
        _node.Render(context, new Rect(0, 0, 10, 2));

        Assert.Equal("klmnopqrst", terminal.GetLine(0));
        Assert.Equal("uvwxy", terminal.GetLine(1).TrimEnd());
        Assert.Equal(_node.CursorColor, terminal.GetBackground(5, 1));
    }

    #endregion

    #region Helper Methods

    private void TypeText(string text) => TypeText(_node, text);

    private static void TypeText(TextAreaNode node, string text)
    {
        foreach (var c in text)
        {
            node.HandleInput(new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false));
        }
    }

    /// <summary>
    /// Bare Enter — submits (default TextAreaNode behavior).
    /// </summary>
    private void PressEnter() => PressEnter(_node);

    private static void PressEnter(TextAreaNode node)
    {
        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    /// <summary>
    /// Ctrl+Enter — inserts a newline (default TextAreaNode behavior).
    /// </summary>
    private void PressCtrlEnter() => PressCtrlEnter(_node);

    private static void PressCtrlEnter(TextAreaNode node)
    {
        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, control: true));
    }

    /// <summary>
    /// Alt+Enter — also inserts a newline (universal fallback for terminals without kitty protocol).
    /// </summary>
    private void PressAltEnter() => PressAltEnter(_node);

    private static void PressAltEnter(TextAreaNode node)
    {
        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, alt: true, false));
    }

    /// <summary>Semantic alias: inserts a newline (Ctrl+Enter).</summary>
    private void InsertNewline() => PressCtrlEnter();

    private static void InsertNewline(TextAreaNode node) => PressCtrlEnter(node);

    /// <summary>Semantic alias: submits (bare Enter).</summary>
    private void Submit() => PressEnter();

    private static void Submit(TextAreaNode node) => PressEnter(node);

    private void PressBackspace()
    {
        _node.HandleInput(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
    }

    private void PressDelete()
    {
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));
    }

    private void PressHome()
    {
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
    }

    private void PressEnd()
    {
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
    }

    private void PressEscape()
    {
        _node.HandleInput(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
    }

    private void PressUpArrow() => PressUpArrow(_node);

    private static void PressUpArrow(TextAreaNode node)
    {
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
    }

    private void PressDownArrow() => PressDownArrow(_node);

    private static void PressDownArrow(TextAreaNode node)
    {
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
    }

    private void PressLeftArrow()
    {
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
    }

    #endregion

    /// <summary>
    /// Minimal render context for testing that captures written content.
    /// </summary>
    private sealed class TestRenderContext : IRenderContext
    {
        private readonly Dictionary<(int X, int Y), char> _chars = new();
        private readonly int _width;
        private readonly int _height;

        public TestRenderContext(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public int Width => _width;
        public int Height => _height;

        public void WriteAt(int x, int y, char c)
        {
            _chars[(x, y)] = c;
        }

        public void WriteAt(int x, int y, string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                _chars[(x + i, y)] = text[i];
            }
        }

        public void SetForeground(Color color) { }
        public void SetBackground(Color color) { }
        public void ResetColors() { }
        public void SetDecoration(TextDecoration decoration) { }
        public void ApplyStyle(TextStyle style) { }
        public void Fill(int x, int y, int width, int height, char c = ' ') { }
        public void Clear() { }

        public IRenderContext CreateSubContext(Rect bounds)
        {
            return new SubContext(this, bounds);
        }

        /// <summary>
        /// Get the text content of a specific row.
        /// </summary>
        public string GetContent(int row)
        {
            var chars = new char[_width];
            for (var i = 0; i < _width; i++)
            {
                chars[i] = _chars.TryGetValue((i, row), out var c) ? c : ' ';
            }
            return new string(chars).TrimEnd();
        }

        private sealed class SubContext : IRenderContext
        {
            private readonly TestRenderContext _parent;
            private readonly Rect _bounds;

            public SubContext(TestRenderContext parent, Rect bounds)
            {
                _parent = parent;
                _bounds = bounds;
            }

            public int Width => _bounds.Width;
            public int Height => _bounds.Height;

            public void WriteAt(int x, int y, char c) => _parent.WriteAt(_bounds.X + x, _bounds.Y + y, c);
            public void WriteAt(int x, int y, string text) => _parent.WriteAt(_bounds.X + x, _bounds.Y + y, text);
            public void SetForeground(Color color) => _parent.SetForeground(color);
            public void SetBackground(Color color) => _parent.SetBackground(color);
            public void ResetColors() => _parent.ResetColors();
            public void SetDecoration(TextDecoration decoration) { }
            public void ApplyStyle(TextStyle style) { }
            public void Fill(int x, int y, int width, int height, char c = ' ') { }
            public void Clear() { }
            public IRenderContext CreateSubContext(Rect bounds) =>
                new SubContext(_parent, new Rect(_bounds.X + bounds.X, _bounds.Y + bounds.Y, bounds.Width, bounds.Height));
        }
    }
}
