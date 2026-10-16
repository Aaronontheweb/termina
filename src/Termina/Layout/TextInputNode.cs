// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// A single-line text input node with horizontal scrolling.
/// Supports optional built-in input history via <see cref="WithHistory"/>.
/// </summary>
public sealed class TextInputNode : TextInputBaseNode
{
    private int _scrollOffset;

    public TextInputNode(int cursorBlinkMs = 530)
        : base(cursorBlinkMs)
    {
        HeightConstraint = new SizeConstraint.Fixed(1);
        WidthConstraint = new SizeConstraint.Fill();
    }

    /// <summary>
    /// Set placeholder text.
    /// </summary>
    public TextInputNode WithPlaceholder(string placeholder)
    {
        Placeholder = placeholder;
        return this;
    }

    /// <summary>
    /// Set foreground color.
    /// </summary>
    public TextInputNode WithForeground(Color color)
    {
        Foreground = color;
        return this;
    }

    /// <summary>
    /// Set background color.
    /// </summary>
    public TextInputNode WithBackground(Color color)
    {
        Background = color;
        return this;
    }

    /// <summary>
    /// Set max length.
    /// </summary>
    public TextInputNode WithMaxLength(int length)
    {
        MaxLength = length;
        return this;
    }

    /// <summary>
    /// Enable password mode.
    /// </summary>
    public TextInputNode AsPassword(char maskChar = '\u2022')
    {
        IsPassword = true;
        PasswordChar = maskChar;
        return this;
    }

    /// <summary>
    /// Enable built-in input history. When enabled, Up/Down arrow keys navigate
    /// through previous submissions, and Enter auto-records non-empty text.
    /// </summary>
    /// <param name="maxEntries">Maximum history entries to keep (0 = unlimited).</param>
    public TextInputNode WithHistory(int maxEntries = 0)
    {
        EnableHistory(maxEntries);
        return this;
    }

    /// <inheritdoc />
    public override void Clear()
    {
        _scrollOffset = 0;
        base.Clear();
    }

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        var prefixWidth = DisplayWidth.GetColumnCount(CommittedDisplayPrefix);
        var width = WidthConstraint.Compute(available.Width, prefixWidth + DisplayWidth.GetColumnCount(_text) + 1, available.Width);
        return new Size(width, 1);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        if (!bounds.HasArea)
            return;

        var inputContext = context.CreateSubContext(bounds);

        if (Background.HasValue)
        {
            inputContext.SetBackground(Background.Value);
            inputContext.Fill(0, 0, bounds.Width, bounds.Height, ' ');
            inputContext.ResetColors();
        }

        var prefix = CommittedDisplayPrefix;
        var prefixWidth = DisplayWidth.GetColumnCount(prefix);
        var activeText = _text;

        if (IsPassword && activeText.Length > 0)
        {
            activeText = new string(PasswordChar, activeText.Length);
        }

        var fullDisplayText = prefix + activeText;
        var displayCursor = prefixWidth + DisplayWidth.CursorPositionToColumn(activeText, _cursorPosition);

        if (fullDisplayText.Length == 0 && !string.IsNullOrEmpty(Placeholder))
        {
            inputContext.SetForeground(PlaceholderColor);
            if (Background.HasValue)
                inputContext.SetBackground(Background.Value);
            var placeholder = DisplayWidth.GetColumnCount(Placeholder) > bounds.Width
                ? DisplayWidth.TruncateToColumns(Placeholder, bounds.Width)
                : Placeholder;
            inputContext.WriteAt(0, 0, placeholder);
            inputContext.ResetColors();

            if (_cursorVisible)
            {
                inputContext.SetBackground(CursorColor);
                inputContext.WriteAt(0, 0, ' ');
                inputContext.ResetColors();
            }
            return;
        }

        var cursorTextWidth = DisplayWidth.GetColumnCount(DisplayWidth.GetTextElementAt(activeText, _cursorPosition));

        if (displayCursor < _scrollOffset)
        {
            _scrollOffset = displayCursor;
        }
        else if (displayCursor + cursorTextWidth > _scrollOffset + bounds.Width)
        {
            _scrollOffset = displayCursor + cursorTextWidth - bounds.Width;
        }

        var visStart = _scrollOffset;
        var visEnd = _scrollOffset + bounds.Width;

        var segColumnOffset = 0;
        foreach (var seg in _committedSegments)
        {
            var segStart = segColumnOffset;
            var segWidth = DisplayWidth.GetColumnCount(seg.DisplayText);
            var segEnd = segStart + segWidth;

            var drawStart = Math.Max(segStart, visStart);
            var drawEnd = Math.Min(segEnd, visEnd);

            if (drawStart < drawEnd)
            {
                if (seg.Kind == SegmentKind.Pasted)
                    inputContext.SetForeground(Color.BrightBlack);
                else if (Foreground.HasValue)
                    inputContext.SetForeground(Foreground.Value);
                else
                    inputContext.ResetColors();

                if (Background.HasValue)
                    inputContext.SetBackground(Background.Value);

                WriteVisibleText(inputContext, seg.DisplayText, segStart, _scrollOffset, drawStart, drawEnd);
            }

            segColumnOffset = segEnd;
        }

        var activeStart = prefixWidth;
        var activeEnd = prefixWidth + DisplayWidth.GetColumnCount(activeText);
        var activeDrawStart = Math.Max(activeStart, visStart);
        var activeDrawEnd = Math.Min(activeEnd, visEnd);

        if (activeDrawStart < activeDrawEnd)
        {
            inputContext.ResetColors();
            if (Foreground.HasValue)
                inputContext.SetForeground(Foreground.Value);
            if (Background.HasValue)
                inputContext.SetBackground(Background.Value);

            if (HasSelection)
            {
                var selStart = Math.Min(_selectionStart, _cursorPosition) + prefixWidth;
                var selEnd = Math.Max(_selectionStart, _cursorPosition) + prefixWidth;
                var activeColumn = activeStart;

                foreach (var cell in DisplayWidth.EnumerateCells(activeText))
                {
                    var cellStart = activeColumn;
                    var cellEnd = cellStart + cell.ColumnWidth;
                    activeColumn = cellEnd;

                    if (cellEnd <= activeDrawStart || cellStart >= activeDrawEnd)
                        continue;

                    if (cellStart < activeDrawStart || cellEnd > activeDrawEnd)
                        continue;

                    var textIndex = cell.StartIndex;
                    if (textIndex >= selStart - prefixWidth && textIndex < selEnd - prefixWidth)
                    {
                        inputContext.SetBackground(SelectionColor);
                    }
                    else
                    {
                        if (Background.HasValue)
                            inputContext.SetBackground(Background.Value);
                        else
                            inputContext.ResetColors();
                        if (Foreground.HasValue)
                            inputContext.SetForeground(Foreground.Value);
                    }

                    inputContext.WriteAt(cellStart - _scrollOffset, 0, cell.Text);
                }
            }
            else
            {
                WriteVisibleText(inputContext, activeText, activeStart, _scrollOffset, activeDrawStart, activeDrawEnd);
            }
        }

        inputContext.ResetColors();

        if (_cursorVisible)
        {
            var cursorX = displayCursor - _scrollOffset;
            if (cursorX >= 0 && cursorX < bounds.Width)
            {
                inputContext.SetBackground(CursorColor);
                inputContext.SetForeground(Background ?? Color.Black);
                var cursorText = _cursorPosition < activeText.Length
                    ? DisplayWidth.GetTextElementAt(activeText, _cursorPosition)
                    : " ";
                inputContext.WriteAt(cursorX, 0, cursorText);
                inputContext.ResetColors();
            }
        }
    }

    /// <summary>
    /// Resets the horizontal scroll offset in addition to base escape behavior.
    /// </summary>
    protected override void OnTextBufferChanged()
    {
        if (_text.Length == 0 || _cursorPosition == 0)
        {
            _scrollOffset = 0;
        }
        base.OnTextBufferChanged();
    }

    private static void WriteVisibleText(
        IRenderContext context,
        string text,
        int absoluteStartColumn,
        int scrollOffset,
        int drawStart,
        int drawEnd)
    {
        var column = absoluteStartColumn;
        foreach (var cell in DisplayWidth.EnumerateCells(text))
        {
            var cellStart = column;
            var cellEnd = cellStart + cell.ColumnWidth;
            column = cellEnd;

            if (cellEnd <= drawStart || cellStart >= drawEnd)
                continue;

            if (cellStart < drawStart || cellEnd > drawEnd)
                continue;

            context.WriteAt(cellStart - scrollOffset, 0, cell.Text);
        }
    }
}
