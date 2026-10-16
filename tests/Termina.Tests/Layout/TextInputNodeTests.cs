// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Input;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

using Microsoft.Extensions.Time.Testing;
using R3;
namespace Termina.Tests.Layout;

/// <summary>
/// Tests for the TextInputNode layout component.
/// </summary>
public class TextInputNodeTests : IDisposable
{
    private readonly TextInputNode _node;

    public TextInputNodeTests()
    {
        _node = new TextInputNode();
    }

    public void Dispose()
    {
        _node.Dispose();
    }

    [Fact]
    public void HandleInput_UpArrow_ReturnsFalse_ForViewModelToHandle()
    {
        // Up arrow should return false so ViewModel can handle history
        var handled = _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.False(handled);
    }

    [Fact]
    public void HandleInput_DownArrow_ReturnsFalse_ForViewModelToHandle()
    {
        // Down arrow should return false so ViewModel can handle history
        var handled = _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.False(handled);
    }

    [Fact]
    public void HandleInput_Enter_ClearsTextAfterSubmit()
    {
        // Type some text
        TypeText("test text");

        string? submitted = null;
        _node.Submitted.Subscribe(t => submitted = t);

        // Press Enter — submits and clears
        _node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal("test text", submitted);
        Assert.Equal("", _node.Text);
    }

    [Fact]
    public void Clear_ResetsTextAndCursor()
    {
        // Type some text
        TypeText("test text");

        // Clear
        _node.Clear();

        // Text should be cleared
        Assert.Equal("", _node.Text);
    }

    [Fact]
    public void Submitted_EventFired_OnEnter()
    {
        string? submittedText = null;
        _node.Submitted.Subscribe(text => submittedText = text);

        TypeText("test submission");
        _node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal("test submission", submittedText);
    }

    [Fact]
    public void TextChanged_EventFired_OnCharacterInput()
    {
        var changeCount = 0;
        _node.TextChanged.Subscribe(_ => changeCount++);

        TypeText("abc");

        Assert.Equal(3, changeCount);
    }

    [Fact]
    public void TextChanged_EventFired_OnClear()
    {
        TypeText("test");

        string? changedText = null;
        _node.TextChanged.Subscribe(text => changedText = text);

        _node.Clear();

        Assert.Equal("", changedText);
    }

    [Fact]
    public void HandleInput_LeftArrow_MovesCursor()
    {
        TypeText("hello");

        // Move left
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));

        // Type at cursor position
        _node.HandleInput(new ConsoleKeyInfo('X', (ConsoleKey)0, false, false, false));

        Assert.Equal("helXlo", _node.Text);
    }

    [Fact]
    public void HandleInput_RightArrow_MovesCursor()
    {
        TypeText("hello");

        // Move to start
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));

        // Move right twice
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));

        // Type at cursor position
        _node.HandleInput(new ConsoleKeyInfo('X', (ConsoleKey)0, false, false, false));

        Assert.Equal("heXllo", _node.Text);
    }

    [Fact]
    public void HandleInput_RightArrow_SkipsWholeEmojiTextElement()
    {
        _node.HandlePaste(new PasteEvent("😀A"));
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));

        _node.HandleInput(new ConsoleKeyInfo('X', (ConsoleKey)0, false, false, false));

        Assert.Equal("😀XA", _node.Text);
    }

    [Fact]
    public void HandleInput_Backspace_DeletesWholeEmojiTextElement()
    {
        _node.HandlePaste(new PasteEvent("😀A"));
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));

        _node.HandleInput(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        Assert.Equal("A", _node.Text);
    }

    [Fact]
    public void HandleInput_Home_MovesCursorToStart()
    {
        TypeText("hello");
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        _node.HandleInput(new ConsoleKeyInfo('X', (ConsoleKey)0, false, false, false));

        Assert.Equal("Xhello", _node.Text);
    }

    [Fact]
    public void HandleInput_End_MovesCursorToEnd()
    {
        TypeText("hello");
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        _node.HandleInput(new ConsoleKeyInfo('X', (ConsoleKey)0, false, false, false));

        Assert.Equal("helloX", _node.Text);
    }

    [Fact]
    public void HandleInput_Backspace_DeletesCharacter()
    {
        TypeText("hello");
        _node.HandleInput(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        Assert.Equal("hell", _node.Text);
    }

    [Fact]
    public void HandleInput_Delete_DeletesCharacterAhead()
    {
        TypeText("hello");
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));

        Assert.Equal("ello", _node.Text);
    }

    [Fact]
    public void HandleInput_Escape_ClearsText()
    {
        TypeText("hello");
        _node.HandleInput(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));

        Assert.Equal("", _node.Text);
    }

    #region History Tests

    [Fact]
    public void History_Disabled_UpArrow_ReturnsFalse()
    {
        // Default node has no history — Up should return false
        var handled = _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.False(handled);
    }

    [Fact]
    public void History_Disabled_DownArrow_ReturnsFalse()
    {
        var handled = _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.False(handled);
    }

    [Fact]
    public void History_Enabled_UpArrow_ReturnsTrue()
    {
        using var node = new TextInputNode().WithHistory();
        var handled = node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.True(handled);
    }

    [Fact]
    public void History_Enabled_DownArrow_ReturnsTrue()
    {
        using var node = new TextInputNode().WithHistory();
        var handled = node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.True(handled);
    }

    [Fact]
    public void History_RecallsLastSubmission()
    {
        using var node = new TextInputNode().WithHistory();

        TypeText(node, "first");
        PressEnter(node);
        node.Clear();

        TypeText(node, "second");
        PressEnter(node);
        node.Clear();

        // Press Up — should recall "second"
        PressUp(node);
        Assert.Equal("second", node.Text);
    }

    [Fact]
    public void History_NavigatesMultipleEntries()
    {
        using var node = new TextInputNode().WithHistory();

        TypeText(node, "first");
        PressEnter(node);
        node.Clear();

        TypeText(node, "second");
        PressEnter(node);
        node.Clear();

        TypeText(node, "third");
        PressEnter(node);
        node.Clear();

        // Navigate backward through all entries
        PressUp(node);
        Assert.Equal("third", node.Text);

        PressUp(node);
        Assert.Equal("second", node.Text);

        PressUp(node);
        Assert.Equal("first", node.Text);

        // At the beginning — stays on first
        PressUp(node);
        Assert.Equal("first", node.Text);
    }

    [Fact]
    public void History_DownRestoresSavedInput()
    {
        using var node = new TextInputNode().WithHistory();

        TypeText(node, "submitted");
        PressEnter(node);
        node.Clear();

        // Type partial text, then navigate history
        TypeText(node, "in-progress");
        PressUp(node);
        Assert.Equal("submitted", node.Text);

        // Press Down — should restore in-progress text
        PressDown(node);
        Assert.Equal("in-progress", node.Text);
    }

    [Fact]
    public void CancelHistoryNavigation_RestoresSavedInput()
    {
        using var node = new TextInputNode().WithHistory();
        node.AddHistory("submitted");
        TypeText(node, "in-progress");

        PressUp(node);

        Assert.True(node.CancelHistoryNavigation());
        Assert.Equal("in-progress", node.Text);
    }

    [Fact]
    public void CancelHistoryNavigation_ResetsNavigation()
    {
        using var node = new TextInputNode().WithHistory();
        node.AddHistory("first");
        node.AddHistory("second");
        TypeText(node, "draft");

        PressUp(node);
        PressUp(node);
        node.CancelHistoryNavigation();
        PressDown(node);

        Assert.Equal("draft", node.Text);
    }

    [Fact]
    public void CancelHistoryNavigation_WithoutNavigation_ReturnsFalse()
    {
        using var node = new TextInputNode().WithHistory();
        TypeText(node, "draft");

        Assert.False(node.CancelHistoryNavigation());
        Assert.Equal("draft", node.Text);
    }

    [Fact]
    public void History_MaxEntries_EvictsOldest()
    {
        using var node = new TextInputNode().WithHistory(maxEntries: 2);

        TypeText(node, "first");
        PressEnter(node);
        node.Clear();

        TypeText(node, "second");
        PressEnter(node);
        node.Clear();

        TypeText(node, "third");
        PressEnter(node);
        node.Clear();

        // Should only have "second" and "third" (first was evicted)
        PressUp(node);
        Assert.Equal("third", node.Text);

        PressUp(node);
        Assert.Equal("second", node.Text);

        // At the beginning — stays on second (first was evicted)
        PressUp(node);
        Assert.Equal("second", node.Text);
    }

    [Fact]
    public void History_EmptySubmissionsNotAdded()
    {
        using var node = new TextInputNode().WithHistory();

        // Submit empty string
        PressEnter(node);
        node.Clear();

        TypeText(node, "real entry");
        PressEnter(node);
        node.Clear();

        // Up should recall "real entry" (empty was not added)
        PressUp(node);
        Assert.Equal("real entry", node.Text);

        // Up again — should stay on "real entry" (only one entry)
        PressUp(node);
        Assert.Equal("real entry", node.Text);
    }

    [Fact]
    public void History_EnterResetsHistoryIndex()
    {
        using var node = new TextInputNode().WithHistory();

        TypeText(node, "first");
        PressEnter(node);
        node.Clear();

        TypeText(node, "second");
        PressEnter(node);
        node.Clear();

        // Navigate to "first"
        PressUp(node);
        PressUp(node);
        Assert.Equal("first", node.Text);

        // Submit "first" again (Enter resets index)
        PressEnter(node);
        node.Clear();

        // Up should now recall the most recent entry ("first" — just submitted)
        PressUp(node);
        Assert.Equal("first", node.Text);
    }

    [Fact]
    public void AddHistory_AddsEntry()
    {
        using var node = new TextInputNode().WithHistory();

        node.AddHistory("programmatic entry");

        PressUp(node);
        Assert.Equal("programmatic entry", node.Text);
    }

    [Fact]
    public void AddHistory_NoOp_WhenDisabled()
    {
        // Default node — history disabled
        _node.AddHistory("should be ignored");

        // Up arrow still returns false (no history)
        var handled = _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.False(handled);
    }

    [Fact]
    public void AddHistory_IgnoresEmptyStrings()
    {
        using var node = new TextInputNode().WithHistory();

        node.AddHistory("");
        node.AddHistory("real");

        PressUp(node);
        Assert.Equal("real", node.Text);

        // Only one entry
        PressUp(node);
        Assert.Equal("real", node.Text);
    }

    [Fact]
    public void AddHistory_RespectsMaxEntries()
    {
        using var node = new TextInputNode().WithHistory(maxEntries: 2);

        node.AddHistory("first");
        node.AddHistory("second");
        node.AddHistory("third");

        PressUp(node);
        Assert.Equal("third", node.Text);

        PressUp(node);
        Assert.Equal("second", node.Text);

        // first was evicted
        PressUp(node);
        Assert.Equal("second", node.Text);
    }

    [Fact]
    public void History_PasteContentRecalledAsCondensed()
    {
        using var node = new TextInputNode().WithHistory();

        // Simulate a paste
        node.HandlePaste(new Termina.Input.PasteEvent("line1\nline2\nline3"));
        PressEnter(node);
        node.Clear();

        // Recall — should show condensed form (Text setter handles multi-line)
        PressUp(node);
        Assert.Contains("[Pasted", node.Text);
    }

    #endregion

    #region Cursor placement

    private const string ThumbsUpMedium = "\U0001F44D\U0001F3FD";

    [Fact]
    public void TextSetter_KeepsCursorPosition()
    {
        // Two-way bindings write Text back while the user edits, so the setter must not move the cursor.
        _node.Text = "abc";
        TypeText("x");
        Assert.Equal("xabc", _node.Text);

        TypeText("y");
        _node.Text = "xyabcdef";
        TypeText("z");

        Assert.Equal("xyzabcdef", _node.Text);
    }

    [Fact]
    public void MoveCursorToEnd_AfterSeedingText_AppendsTypedCharacter()
    {
        _node.Text = "http://localhost:11434";
        _node.MoveCursorToEnd();

        TypeText("x");

        Assert.Equal("http://localhost:11434x", _node.Text);
    }

    [Fact]
    public void CursorPosition_InsertsAtRequestedIndex()
    {
        _node.Text = "abcd";
        _node.CursorPosition = 2;

        TypeText("X");

        Assert.Equal("abXcd", _node.Text);
        Assert.Equal(3, _node.CursorPosition);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(100, 4)]
    [InlineData(int.MaxValue, 4)]
    public void CursorPosition_ClampsToTextRange(int requested, int expected)
    {
        _node.Text = "abcd";

        _node.CursorPosition = requested;

        Assert.Equal(expected, _node.CursorPosition);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)] // inside the surrogate pair
    [InlineData(3, 1)] // between the base emoji and its skin tone modifier
    [InlineData(4, 1)] // inside the skin tone modifier surrogate pair
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    public void CursorPosition_InsideTextElement_MovesToElementStart(int requested, int expected)
    {
        _node.Text = "a" + ThumbsUpMedium + "b";

        _node.CursorPosition = requested;

        Assert.Equal(expected, _node.CursorPosition);
    }

    [Fact]
    public void CursorPosition_ClearsSelection()
    {
        _node.Text = "abcd";
        _node.HandleInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, true));
        Assert.True(_node.HasSelection);

        _node.CursorPosition = 1;

        Assert.False(_node.HasSelection);
        Assert.Equal("", _node.SelectedText);
    }

    [Fact]
    public void MoveCursorToEnd_ClearsSelection()
    {
        _node.Text = "abcd";
        _node.HandleInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, true));

        _node.MoveCursorToEnd();

        Assert.False(_node.HasSelection);
        Assert.Equal(4, _node.CursorPosition);
    }

    [Fact]
    public void CursorPosition_DoesNotEmitTextChanged()
    {
        _node.Text = "abcd";
        var changeCount = 0;
        _node.TextChanged.Subscribe(_ => changeCount++);

        _node.CursorPosition = 2;
        _node.MoveCursorToEnd();

        Assert.Equal(0, changeCount);
        Assert.Equal("abcd", _node.Text);
    }

    [Fact]
    public void CursorPosition_EmitsInvalidated()
    {
        _node.Text = "abcd";
        var invalidations = 0;
        _node.Invalidated.Subscribe(_ => invalidations++);

        _node.CursorPosition = 2;
        Assert.Equal(1, invalidations);

        _node.MoveCursorToEnd();
        Assert.Equal(2, invalidations);
    }

    [Fact]
    public void CursorPosition_SameValue_StillInvalidatesAndClearsSelection()
    {
        // Matches the Text setter, which invalidates and clears the selection even when nothing changes.
        _node.Text = "abcd";
        _node.MoveCursorToEnd();
        _node.HandleInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, true));
        Assert.True(_node.HasSelection);
        var invalidations = 0;
        _node.Invalidated.Subscribe(_ => invalidations++);

        _node.CursorPosition = _node.CursorPosition;

        Assert.False(_node.HasSelection);
        Assert.Equal(1, invalidations);
    }

    [Fact]
    public void MoveCursorToEnd_WhenFocusedInBlinkOffPhase_ShowsCursorImmediately()
    {
        var timeProvider = new FakeTimeProvider();
        using var frameProvider = new TerminaRenderFrameProvider(() => { }, timeProvider, TimeSpan.FromMilliseconds(10));
        using var node = new TextInputNode(cursorBlinkMs: 100);
        LayoutRuntimeContextInjector.Apply(node, new LayoutRuntimeContext(frameProvider, timeProvider, () => { }));
        node.Text = "ab";
        node.OnFocused();
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        frameProvider.AdvanceFrame();
        var terminal = new VirtualTerminal(10, 1);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);
        node.Render(context, new Rect(0, 0, 10, 1));
        Assert.NotEqual(node.CursorColor, terminal.GetBackground(0, 0));

        node.MoveCursorToEnd();
        node.Render(context, new Rect(0, 0, 10, 1));

        Assert.Equal(node.CursorColor, terminal.GetBackground(2, 0));
    }

    [Fact]
    public void CursorPosition_WhenUnfocused_DoesNotStartAnimation()
    {
        _node.Text = "abcd";

        _node.MoveCursorToEnd();

        Assert.False(_node.IsAnimating);
    }

    [Fact]
    public void CursorPosition_WorksBeforeFocusAndAttach()
    {
        Assert.False(_node.HasFocus);

        _node.Text = "seed";
        _node.MoveCursorToEnd();

        Assert.False(_node.HasFocus);
        Assert.Equal(4, _node.CursorPosition);
    }

    [Fact]
    public void CursorPosition_IndexesEditableTextAfterPastedSegment()
    {
        // A newline in Text becomes a committed paste summary and leaves the editable text empty.
        _node.Text = "line1\nline2";
        Assert.StartsWith("[Pasted", _node.Text);

        _node.CursorPosition = 50;
        Assert.Equal(0, _node.CursorPosition);

        _node.MoveCursorToEnd();
        TypeText("x");

        Assert.EndsWith("x", _node.Text);
        Assert.StartsWith("[Pasted", _node.Text);
    }

    [Fact]
    public void CursorPosition_AfterPastedSegment_UsesTypedTextLength()
    {
        _node.HandlePaste(new PasteEvent("line1\nline2"));
        TypeText("abc");
        Assert.Equal(3, _node.CursorPosition);

        _node.CursorPosition = 1;
        TypeText("X");
        Assert.EndsWith("aXbc", _node.Text);

        _node.MoveCursorToEnd();
        TypeText("Z");
        Assert.EndsWith("aXbcZ", _node.Text);
    }

    [Fact]
    public void Render_AfterMoveCursorToEnd_ShowsEndOfLongTextWithCursor()
    {
        const int width = 10;
        var terminal = new VirtualTerminal(width, 1);
        var context = new RegionRenderContext(terminal, 0, 0, width, 1);
        _node.Text = "http://localhost:11434";
        _node.MoveCursorToEnd();

        _node.Render(context, new Rect(0, 0, width, 1));

        Assert.Equal("ost:11434", terminal.GetLine(0).TrimEnd());
        Assert.Equal(_node.CursorColor, terminal.GetBackground(width - 1, 0));
    }

    #endregion

    private void TypeText(string text)
    {
        TypeText(_node, text);
    }

    private static void TypeText(TextInputNode node, string text)
    {
        foreach (var c in text)
        {
            node.HandleInput(new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false));
        }
    }

    private static void PressEnter(TextInputNode node)
    {
        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    private static void PressUp(TextInputNode node)
    {
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
    }

    private static void PressDown(TextInputNode node)
    {
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
    }

    [Fact]
    public void ScrollOffset_ResetsToZero_AfterSubmit()
    {
        // Submit triggers OnTextBufferChanged with empty text and cursor 0, which should reset scroll offset.
        SetScrollOffset(_node, 10);
        TypeText(_node, "test");
        PressEnter(_node);

        Assert.Equal(0, GetScrollOffset(_node));
    }

    [Fact]
    public void ScrollOffset_ResetsToZero_AfterClear()
    {
        // Clear() explicitly resets scroll offset, and OnTextBufferChanged also does it as a safety net.
        SetScrollOffset(_node, 10);
        TypeText(_node, "test");
        _node.Clear();

        Assert.Equal(0, GetScrollOffset(_node));
    }

    [Fact]
    public void ScrollOffset_Preserved_WhenTypingFromHomeWithTextNonEmpty()
    {
        // Home moves cursor to index 0 without changing text; typing after Home is normal editing.
        // It should not zero out scroll offset.
        TypeText(_node, "test");
        SetScrollOffset(_node, 10);
        _node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        TypeText(_node, "X");

        Assert.Equal("Xtest", _node.Text);
        Assert.Equal(10, GetScrollOffset(_node));
    }

    private static int GetScrollOffset(TextInputNode node)
    {
        var field = typeof(TextInputNode).GetField("_scrollOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        return (int)field.GetValue(node)!;
    }

    private static void SetScrollOffset(TextInputNode node, int value)
    {
        var field = typeof(TextInputNode).GetField("_scrollOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(node, value);
    }
}
