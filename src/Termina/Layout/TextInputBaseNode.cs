// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Diagnostics;
using Termina.Input;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// Abstract base class for text input nodes that provides shared editing logic:
/// cursor management, selection, character/backspace/delete handling, history,
/// paste support (via <see cref="IPasteReceiver"/>), and cursor blink animation.
/// </summary>
/// <remarks>
/// Subclasses must implement <see cref="LayoutNode.Measure"/> and <see cref="LayoutNode.Render"/>
/// and may override virtual hooks (<see cref="HandleEnter"/>, <see cref="HandleUpArrow"/>,
/// <see cref="HandleDownArrow"/>, <see cref="HandleHome"/>, <see cref="HandleEnd"/>,
/// <see cref="HandlePaste"/>, <see cref="Clear"/>, <see cref="Text"/>) to customize behavior.
/// </remarks>
public abstract class TextInputBaseNode : LayoutNode, IAnimatedNode, IInvalidatingNode, IFocusable, IPasteReceiver
{
    protected readonly int _cursorBlinkMs;
    protected IDisposable? _cursorTimerSubscription;
    protected readonly Subject<Unit> _invalidated = new();
    protected readonly Subject<string> _textChanged = new();
    protected readonly Subject<string> _submitted = new();
    protected string _text = "";
    protected readonly List<CommittedSegment> _committedSegments = new();
    protected int _cursorPosition;
    protected int _selectionStart = -1;
    protected bool _cursorVisible = true;
    protected bool _hasFocus;
    protected bool _disposed;

    // Input history (null = disabled, opt-in via subclass fluent API)
    protected List<string>? _history;
    protected int _historyIndex = -1;
    protected string? _savedInput;
    protected int _maxHistoryEntries;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <summary>
    /// Observable that emits when the text value changes.
    /// </summary>
    public Observable<string> TextChanged => _textChanged;

    /// <summary>
    /// Observable that emits when the input is submitted.
    /// </summary>
    public Observable<string> Submitted => _submitted;

    /// <inheritdoc />
    public bool IsAnimating { get; protected set; }

    /// <inheritdoc />
    public bool CanFocus => true;

    /// <inheritdoc />
    public bool HasFocus => _hasFocus;

    /// <summary>
    /// Text input has low-medium priority (lower than modal and selection list).
    /// </summary>
    public int FocusPriority => 5;

    /// <summary>
    /// Gets or sets the current text value.
    /// </summary>
    public virtual string Text
    {
        get => CommittedDisplayPrefix + _text;
        set
        {
            var newValue = value ?? "";
            _committedSegments.Clear();
            if (newValue.Contains('\n'))
            {
                var lineCount = 1;
                foreach (var c in newValue)
                {
                    if (c == '\n') lineCount++;
                }

                var summary = $"[Pasted {lineCount} lines, {newValue.Length} chars] ";
                _committedSegments.Add(new CommittedSegment(summary, newValue, SegmentKind.Pasted));
                _text = "";
            }
            else
            {
                _text = newValue;
            }

            _cursorPosition = DisplayWidth.ClampToTextElementBoundary(_text, _cursorPosition);
            _selectionStart = -1;
            OnTextBufferChanged();
            _textChanged.OnNext(Text);
            _invalidated.OnNext(Unit.Default);
        }
    }

    /// <summary>
    /// Gets or sets the cursor position as a UTF-16 index into the editable text.
    /// </summary>
    /// <remarks>
    /// The editable text is the text after the last committed segment. A multi-line paste commits the text
    /// before it, and the paste itself, as segments that the cursor cannot enter. The value is therefore not
    /// an index into <see cref="Text"/>. For example, when <see cref="Text"/> is
    /// <c>abc[Pasted 2 lines, 5 chars] def</c> and the editable text is <c> def</c>, the valid range is 0 to 4.
    /// The setter moves a position inside a surrogate pair or a grapheme cluster back to the start of
    /// that text element, and it clamps other out-of-range values to the nearest end.
    /// Like the <see cref="Text"/> setter, the setter clears the selection and invalidates the node even when the
    /// position does not change. It does not raise <see cref="TextChanged"/>.
    /// A focused node shows the cursor and restarts the blink cycle, as a key press does.
    /// Use <see cref="MoveCursorToEnd"/> to move the cursor after the last character.
    /// </remarks>
    public int CursorPosition
    {
        get => _cursorPosition;
        set
        {
            _cursorPosition = DisplayWidth.ClampToTextElementBoundary(_text, value);
            _selectionStart = -1;
            if (_hasFocus)
                RestartCursorBlink();
            _invalidated.OnNext(Unit.Default);
        }
    }

    /// <summary>
    /// Moves the cursor after the last character of the editable text and clears the selection.
    /// </summary>
    /// <remarks>
    /// Call this after you assign <see cref="Text"/> to start a pre-filled input with the cursor at the end.
    /// </remarks>
    public void MoveCursorToEnd() => CursorPosition = _text.Length;

    /// <summary>
    /// Gets or sets the placeholder text shown when empty.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// Gets or sets the foreground color.
    /// </summary>
    public Color? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the background color.
    /// </summary>
    public Color? Background { get; set; }

    /// <summary>
    /// Gets or sets the placeholder color.
    /// </summary>
    public Color PlaceholderColor { get; set; } = Color.BrightBlack;

    /// <summary>
    /// Gets or sets the cursor color.
    /// </summary>
    public Color CursorColor { get; set; } = Color.White;

    /// <summary>
    /// Gets or sets the selection background color.
    /// </summary>
    public Color SelectionColor { get; set; } = Color.Blue;

    /// <summary>
    /// Gets or sets the maximum length (0 = unlimited).
    /// </summary>
    public int MaxLength { get; set; }

    /// <summary>
    /// Gets or sets whether to mask input (password mode).
    /// </summary>
    public bool IsPassword { get; set; }

    /// <summary>
    /// Gets or sets the mask character for password mode.
    /// </summary>
    public char PasswordChar { get; set; } = '\u2022';

    /// <summary>
    /// Gets whether there is selected text.
    /// </summary>
    public bool HasSelection => _selectionStart >= 0 && _selectionStart != _cursorPosition;

    /// <summary>
    /// Gets the selected text.
    /// </summary>
    public string SelectedText
    {
        get
        {
            if (!HasSelection)
                return "";
            var start = Math.Min(_selectionStart, _cursorPosition);
            var end = Math.Max(_selectionStart, _cursorPosition);
            return _text[start..end];
        }
    }

    protected TextInputBaseNode(int cursorBlinkMs = 530)
    {
        _cursorBlinkMs = cursorBlinkMs;
    }

    /// <inheritdoc />
    public void OnFocused()
    {
        TerminaTrace.Focus.Debug(this, "OnFocused");
        _hasFocus = true;
        _cursorVisible = true;
        Start();
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public void OnBlurred()
    {
        TerminaTrace.Focus.Debug(this, "OnBlurred");
        _hasFocus = false;
        Stop();
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public void Start()
    {
        if (!IsAnimating)
        {
            IsAnimating = true;
            _cursorVisible = true;
            var ticks = Observable.Interval(TimeSpan.FromMilliseconds(_cursorBlinkMs), GetTimeProvider());
            if (GetFrameProvider() is { } frameProvider)
                ticks = ticks.ObserveOn(frameProvider);

            _cursorTimerSubscription ??= ticks
                .Subscribe(_ =>
                {
                    _cursorVisible = !_cursorVisible;
                    _invalidated.OnNext(Unit.Default);
                });
        }
    }

    /// <inheritdoc />
    public override void SetRuntimeContext(LayoutRuntimeContext context)
    {
        var restart = IsAnimating;
        if (restart)
            Stop();

        base.SetRuntimeContext(context);

        if (restart && _hasFocus)
            Start();
    }

    private TimeProvider GetTimeProvider() => RuntimeContext?.TimeProvider ?? TimeProvider.System;

    private FrameProvider? GetFrameProvider() => RuntimeContext?.RenderFrameProvider;

    /// <inheritdoc />
    public void Stop()
    {
        if (IsAnimating)
        {
            _cursorTimerSubscription?.Dispose();
            _cursorTimerSubscription = null;
            IsAnimating = false;
            _cursorVisible = false;
            _invalidated.OnNext(Unit.Default);
        }
    }

    /// <summary>
    /// Handle a key input event. Returns true if the event was handled.
    /// </summary>
    public bool HandleInput(ConsoleKeyInfo key)
    {
        if (_disposed)
        {
            TerminaTrace.Input.Debug(this, "HandleInput rejected: disposed");
            return false;
        }

        TerminaTrace.Input.Trace(this, "HandleInput: key={0}, char='{1}'", key.Key, key.KeyChar);

        // Reset cursor to visible on any input - restart the blink cycle
        RestartCursorBlink();

        var handled = key.Key switch
        {
            ConsoleKey.Backspace => HandleBackspace(key.Modifiers),
            ConsoleKey.Delete => HandleDelete(key.Modifiers),
            ConsoleKey.LeftArrow => HandleLeftArrow(key.Modifiers),
            ConsoleKey.RightArrow => HandleRightArrow(key.Modifiers),
            ConsoleKey.Home => HandleHome(key.Modifiers),
            ConsoleKey.End => HandleEnd(key.Modifiers),
            ConsoleKey.UpArrow => HandleUpArrow(),
            ConsoleKey.DownArrow => HandleDownArrow(),
            ConsoleKey.Enter => HandleEnter(key.Modifiers),
            ConsoleKey.Escape => HandleEscape(),
            _ when key.KeyChar != '\0' && !char.IsControl(key.KeyChar) => HandleCharacter(key.KeyChar, key.Modifiers),
            _ => false
        };

        TerminaTrace.Input.Trace(this, "HandleInput result: handled={0}", handled);

        if (handled)
        {
            _invalidated.OnNext(Unit.Default);
        }

        return handled;
    }

    private void RestartCursorBlink()
    {
        _cursorVisible = true;
        _cursorTimerSubscription?.Dispose();
        _cursorTimerSubscription = null;
        IsAnimating = false;
        Start();
    }

    /// <summary>
    /// Programmatically add an entry to the input history.
    /// No-op when history is not enabled.
    /// </summary>
    public void AddHistory(string entry)
    {
        if (_history is null || string.IsNullOrEmpty(entry))
            return;

        _history.Add(entry);

        if (_maxHistoryEntries > 0 && _history.Count > _maxHistoryEntries)
            _history.RemoveAt(0);
    }

    /// <summary>
    /// Clears the text and resets cursor position.
    /// Call this from ViewModel after handling submission.
    /// </summary>
    public virtual void Clear()
    {
        _committedSegments.Clear();
        _text = "";
        _cursorPosition = 0;
        _selectionStart = -1;
        OnTextBufferChanged();
        _textChanged.OnNext(Text);
        _invalidated.OnNext(Unit.Default);
    }

    /// <summary>
    /// Handles pasted text from the terminal's bracketed paste mode.
    /// </summary>
    public virtual bool HandlePaste(PasteEvent paste)
    {
        if (string.IsNullOrEmpty(paste.Content))
            return false;

        // Single-line paste: insert inline at cursor like typing
        if (!paste.Content.Contains('\n'))
        {
            if (HasSelection)
                DeleteSelection();

            _text = _text.Insert(_cursorPosition, paste.Content);
            _cursorPosition += paste.Content.Length;
            _selectionStart = -1;
            OnTextBufferChanged();
            _textChanged.OnNext(Text);
            _invalidated.OnNext(Unit.Default);
            return true;
        }

        // Multi-line paste: commit current text and add paste segment
        if (_cursorPosition >= _text.Length)
        {
            if (_text.Length > 0)
            {
                _committedSegments.Add(new CommittedSegment(_text, _text, SegmentKind.Typed));
            }
            _text = "";
        }
        else
        {
            var prefix = _text[.._cursorPosition];
            var suffix = _text[_cursorPosition..];
            if (prefix.Length > 0)
            {
                _committedSegments.Add(new CommittedSegment(prefix, prefix, SegmentKind.Typed));
            }
            _text = suffix;
        }

        var lineCount = 1;
        foreach (var c in paste.Content)
        {
            if (c == '\n') lineCount++;
        }

        var summary = $"[Pasted {lineCount} lines, {paste.Content.Length} chars] ";
        _committedSegments.Add(new CommittedSegment(summary, paste.Content, SegmentKind.Pasted));
        _cursorPosition = 0;
        _selectionStart = -1;

        OnTextBufferChanged();
        _textChanged.OnNext(Text);
        _invalidated.OnNext(Unit.Default);
        return true;
    }

    #region Concrete editing methods

    protected bool HandleCharacter(char c, ConsoleModifiers modifiers)
    {
        if (modifiers.HasFlag(ConsoleModifiers.Control) && (c == 'a' || c == 'A'))
        {
            SelectAll();
            return true;
        }

        if (modifiers.HasFlag(ConsoleModifiers.Control) && (c == 'c' || c == 'C'))
        {
            return true;
        }

        if (modifiers.HasFlag(ConsoleModifiers.Control) && (c == 'v' || c == 'V'))
        {
            return true;
        }

        var addLength = HasSelection ? 1 - SelectedText.Length : 1;
        if (MaxLength > 0 && _text.Length + addLength > MaxLength)
            return false;

        if (HasSelection)
        {
            DeleteSelection();
        }

        _text = _text.Insert(_cursorPosition, c.ToString());
        _cursorPosition++;
        OnTextBufferChanged();
        _textChanged.OnNext(Text);
        return true;
    }

    protected bool HandleBackspace(ConsoleModifiers modifiers)
    {
        if (HasSelection)
        {
            DeleteSelection();
            OnTextBufferChanged();
            _textChanged.OnNext(Text);
            return true;
        }

        if (_cursorPosition == 0)
        {
            if (_committedSegments.Count > 0)
            {
                var last = _committedSegments[^1];
                _committedSegments.RemoveAt(_committedSegments.Count - 1);
                if (last.Kind == SegmentKind.Typed)
                {
                    _text = last.DisplayText + _text;
                    _cursorPosition = last.DisplayText.Length;
                }
                OnTextBufferChanged();
                _textChanged.OnNext(Text);
                return true;
            }
            return false;
        }

        if (modifiers.HasFlag(ConsoleModifiers.Control))
        {
            var wordStart = FindWordBoundary(_cursorPosition, -1);
            _text = _text.Remove(wordStart, _cursorPosition - wordStart);
            _cursorPosition = wordStart;
        }
        else
        {
            var removeStart = DisplayWidth.GetPreviousTextElementIndex(_text, _cursorPosition);
            _text = _text.Remove(removeStart, _cursorPosition - removeStart);
            _cursorPosition = removeStart;
        }

        OnTextBufferChanged();
        _textChanged.OnNext(Text);
        return true;
    }

    protected bool HandleDelete(ConsoleModifiers modifiers)
    {
        if (HasSelection)
        {
            DeleteSelection();
            OnTextBufferChanged();
            _textChanged.OnNext(Text);
            return true;
        }

        if (_cursorPosition >= _text.Length)
            return false;

        if (modifiers.HasFlag(ConsoleModifiers.Control))
        {
            var wordEnd = FindWordBoundary(_cursorPosition, 1);
            _text = _text.Remove(_cursorPosition, wordEnd - _cursorPosition);
        }
        else
        {
            var removeEnd = DisplayWidth.GetNextTextElementIndex(_text, _cursorPosition);
            _text = _text.Remove(_cursorPosition, removeEnd - _cursorPosition);
        }

        OnTextBufferChanged();
        _textChanged.OnNext(Text);
        return true;
    }

    protected bool HandleLeftArrow(ConsoleModifiers modifiers)
    {
        var newPos = modifiers.HasFlag(ConsoleModifiers.Control)
            ? DisplayWidth.ClampToTextElementBoundary(_text, FindWordBoundary(_cursorPosition, -1))
            : DisplayWidth.GetPreviousTextElementIndex(_text, _cursorPosition);

        if (modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            if (_selectionStart < 0)
                _selectionStart = _cursorPosition;
        }
        else
        {
            _selectionStart = -1;
        }

        _cursorPosition = newPos;
        return true;
    }

    protected bool HandleRightArrow(ConsoleModifiers modifiers)
    {
        var newPos = modifiers.HasFlag(ConsoleModifiers.Control)
            ? DisplayWidth.ClampToTextElementBoundary(_text, FindWordBoundary(_cursorPosition, 1))
            : DisplayWidth.GetNextTextElementIndex(_text, _cursorPosition);

        if (modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            if (_selectionStart < 0)
                _selectionStart = _cursorPosition;
        }
        else
        {
            _selectionStart = -1;
        }

        _cursorPosition = newPos;
        return true;
    }

    protected bool HandleEscape()
    {
        if (HasSelection)
        {
            _selectionStart = -1;
            return true;
        }

        if (_committedSegments.Count > 0 || !string.IsNullOrEmpty(_text))
        {
            _committedSegments.Clear();
            _text = "";
            _cursorPosition = 0;
            _selectionStart = -1;
            OnTextBufferChanged();
            _textChanged.OnNext(Text);
            return true;
        }

        return false;
    }

    protected void SelectAll()
    {
        _selectionStart = 0;
        _cursorPosition = _text.Length;
    }

    protected void DeleteSelection()
    {
        if (!HasSelection)
            return;

        var start = Math.Min(_selectionStart, _cursorPosition);
        var end = Math.Max(_selectionStart, _cursorPosition);
        _text = _text.Remove(start, end - start);
        _cursorPosition = start;
        _selectionStart = -1;
    }

    protected int FindWordBoundary(int from, int direction)
    {
        if (direction < 0)
        {
            if (from <= 0)
                return 0;

            var pos = from - 1;
            while (pos > 0 && char.IsWhiteSpace(_text[pos]))
                pos--;
            while (pos > 0 && !char.IsWhiteSpace(_text[pos - 1]))
                pos--;
            return pos;
        }
        else
        {
            if (from >= _text.Length)
                return _text.Length;

            var pos = from;
            while (pos < _text.Length && !char.IsWhiteSpace(_text[pos]))
                pos++;
            while (pos < _text.Length && char.IsWhiteSpace(_text[pos]))
                pos++;
            return pos;
        }
    }

    #endregion

    #region Virtual hooks for subclasses

    /// <summary>
    /// Called when Enter is pressed. Override to customize submit vs newline behavior.
    /// </summary>
    /// <param name="modifiers">The modifier keys held during the Enter press.</param>
    protected virtual bool HandleEnter(ConsoleModifiers modifiers)
    {
        PerformSubmit();
        return true;
    }

    /// <summary>
    /// Called when the Up arrow key is pressed.
    /// </summary>
    protected virtual bool HandleUpArrow()
    {
        if (_history is null)
            return false;

        if (_history.Count == 0)
            return true;

        if (_historyIndex < 0)
        {
            _savedInput = SubmitContent;
            _historyIndex = _history.Count - 1;
        }
        else if (_historyIndex > 0)
        {
            _historyIndex--;
        }

        ApplyHistoryEntry(_history[_historyIndex]);
        return true;
    }

    /// <summary>
    /// Called when the Down arrow key is pressed.
    /// </summary>
    protected virtual bool HandleDownArrow()
    {
        if (_history is null)
            return false;

        if (_historyIndex < 0)
            return true;

        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            ApplyHistoryEntry(_history[_historyIndex]);
        }
        else
        {
            _historyIndex = -1;
            var saved = _savedInput ?? "";
            _savedInput = null;
            ApplyHistoryEntry(saved);
        }

        return true;
    }

    /// <summary>
    /// Called when the Home key is pressed.
    /// </summary>
    protected virtual bool HandleHome(ConsoleModifiers modifiers)
    {
        if (modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            if (_selectionStart < 0)
                _selectionStart = _cursorPosition;
        }
        else
        {
            _selectionStart = -1;
        }

        _cursorPosition = 0;
        return true;
    }

    /// <summary>
    /// Called when the End key is pressed.
    /// </summary>
    protected virtual bool HandleEnd(ConsoleModifiers modifiers)
    {
        if (modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            if (_selectionStart < 0)
                _selectionStart = _cursorPosition;
        }
        else
        {
            _selectionStart = -1;
        }

        _cursorPosition = _text.Length;
        return true;
    }

    /// <summary>
    /// Called after every mutation to <see cref="_text"/>.
    /// Override to invalidate caches (e.g., word wrap).
    /// </summary>
    protected virtual void OnTextBufferChanged()
    {
    }

    #endregion

    #region History helpers

    protected void ApplyHistoryEntry(string entry)
    {
        Text = entry;
        _cursorPosition = _text.Length;
    }

    protected void EnableHistory(int maxEntries)
    {
        _history = new List<string>();
        _maxHistoryEntries = maxEntries;
    }

    /// <summary>
    /// Performs the submit action: records to history, fires Submitted, resets history index.
    /// </summary>
    protected void PerformSubmit()
    {
        var content = SubmitContent;
        TerminaTrace.Input.Debug(this, "PerformSubmit: submitting text length={0} (segments={1})",
            content.Length, _committedSegments.Count);

        // Record to history before clearing
        if (_history is not null && !string.IsNullOrWhiteSpace(content))
        {
            _history.Add(content);
            if (_maxHistoryEntries > 0 && _history.Count > _maxHistoryEntries)
                _history.RemoveAt(0);
        }

        _historyIndex = -1;
        _savedInput = null;

        // Clear the entire input — segments, typed text, cursor, selection.
        // Without this, committed segments (paste summaries) would vanish while
        // manually typed text stayed, which is inconsistent.
        _committedSegments.Clear();
        _text = "";
        _cursorPosition = 0;
        _selectionStart = -1;
        OnTextBufferChanged();

        _submitted.OnNext(content);
        _textChanged.OnNext(Text);
    }

    #endregion

    /// <inheritdoc />
    public override void OnActivate()
    {
        if (_hasFocus)
        {
            Start();
        }
        base.OnActivate();
    }

    /// <inheritdoc />
    public override void OnDeactivate()
    {
        Stop();
        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();

        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _textChanged.OnCompleted();
        _textChanged.Dispose();
        _submitted.OnCompleted();
        _submitted.Dispose();

        base.Dispose();
    }

    protected enum SegmentKind { Typed, Pasted }
    protected sealed record CommittedSegment(string DisplayText, string SubmitText, SegmentKind Kind);

    protected string CommittedDisplayPrefix =>
        _committedSegments.Count == 0 ? "" : string.Concat(_committedSegments.Select(s => s.DisplayText));

    protected string SubmitContent =>
        string.Concat(_committedSegments.Select(s => s.SubmitText)) + _text;
}
