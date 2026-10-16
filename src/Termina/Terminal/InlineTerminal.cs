// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Rendering;

namespace Termina.Terminal;

/// <summary>
/// Buffers one layout frame and writes it into a bounded primary-buffer region.
/// </summary>
internal sealed class InlineTerminal : IAnsiTerminal
{
    private readonly IAnsiTerminal _inner;
    private readonly IInlineTerminalControl _inlineControl;
    private FrameBuffer _pendingFrame;
    private int _cursorX;
    private int _cursorY;
    private Color _currentForeground = Color.Default;
    private Color _currentBackground = Color.Default;
    private TextDecoration _currentDecoration = TextDecoration.None;
    private int _liveRows;

    public InlineTerminal(IAnsiTerminal inner, IInlineTerminalControl inlineControl)
    {
        _inner = inner;
        _inlineControl = inlineControl;
        _pendingFrame = new FrameBuffer(Width, Height);
    }

    public int Width => Math.Max(1, _inner.Width - 1);

    public int Height => Math.Max(1, _inner.Height);

    public void MoveTo(int x, int y)
    {
        _cursorX = Math.Clamp(x, 0, Width - 1);
        _cursorY = Math.Clamp(y, 0, Height - 1);
    }

    public void Write(string text)
    {
        foreach (var cell in DisplayWidth.EnumerateCells(text))
            WriteTextElement(cell.Text, cell.ColumnWidth);
    }

    public void Write(char c)
    {
        WriteTextElement(c.ToString(), DisplayWidth.GetColumnCount(c));
    }

    public void SetForeground(Color color)
    {
        _currentForeground = color;
    }

    public void SetBackground(Color color)
    {
        _currentBackground = color;
    }

    public void SetDecoration(TextDecoration decoration)
    {
        _currentDecoration = decoration;
    }

    public void ResetColors()
    {
        _currentForeground = Color.Default;
        _currentBackground = Color.Default;
        _currentDecoration = TextDecoration.None;
    }

    public void SaveCursor()
    {
    }

    public void RestoreCursor()
    {
    }

    public void SetCursorVisible(bool visible)
    {
        _inner.SetCursorVisible(visible);
    }

    public void ClearRegion(int x, int y, int width, int height)
    {
        _pendingFrame.Fill(x, y, width, height, TerminalCell.Empty);
    }

    public void ClearScreen()
    {
        EnsureFrameSize();
        _pendingFrame.Clear();
        _cursorX = 0;
        _cursorY = 0;
        ResetColors();
    }

    public void Flush()
    {
        EnsureFrameSize();
        ReplaceLiveRegion(_pendingFrame);
        _inner.Flush();
    }

    public void EnterAlternateScreen()
    {
        _inner.EnterAlternateScreen();
    }

    public void ExitAlternateScreen()
    {
        _inner.ExitAlternateScreen();
    }

    public void EnableMouse()
    {
        _inner.EnableMouse();
    }

    public void DisableMouse()
    {
        _inner.DisableMouse();
    }

    public void EnableWheelScroll()
    {
        _inner.EnableWheelScroll();
    }

    public void DisableWheelScroll()
    {
        _inner.DisableWheelScroll();
    }

    public void CopyToClipboard(string text)
    {
        _inner.CopyToClipboard(text);
    }

    public void ClearLiveRegion()
    {
        EraseLiveRegion();
        _liveRows = 0;
        _pendingFrame.Clear();
        _cursorX = 0;
        _cursorY = 0;
        _inner.ResetColors();
        _inner.Flush();
    }

    public void Commit(ILayoutNode content)
    {
        EnsureFrameSize();
        var liveFrame = new FrameBuffer(Width, Height);
        liveFrame.CopyFrom(_pendingFrame);
        var liveCursorX = _cursorX;
        var liveCursorY = _cursorY;
        var liveForeground = _currentForeground;
        var liveBackground = _currentBackground;
        var liveDecoration = _currentDecoration;

        _pendingFrame.Clear();
        _cursorX = 0;
        _cursorY = 0;
        ResetColors();

        var available = new Size(Width, Height);
        var measured = content.Measure(available);
        var contentHeight = Math.Clamp(measured.Height, 1, Height);
        var context = new RegionRenderContext(this, 0, 0, Width, contentHeight);
        content.Render(context, new Rect(0, 0, Width, contentHeight));

        var stableFrame = new FrameBuffer(Width, Height);
        stableFrame.CopyFrom(_pendingFrame);

        _pendingFrame.CopyFrom(liveFrame);
        _cursorX = liveCursorX;
        _cursorY = liveCursorY;
        _currentForeground = liveForeground;
        _currentBackground = liveBackground;
        _currentDecoration = liveDecoration;

        EraseLiveRegion();
        WriteRows(stableFrame, GetContentHeight(stableFrame));
        var nextLiveRows = GetContentHeight(liveFrame);
        WriteRows(liveFrame, nextLiveRows);
        _liveRows = nextLiveRows;
    }

    private void WriteTextElement(string text, int columnWidth)
    {
        switch (text)
        {
            case "\n":
                _cursorX = 0;
                _cursorY++;
                return;
            case "\r":
                _cursorX = 0;
                return;
            case "\t":
                var nextTab = ((_cursorX / 8) + 1) * 8;
                while (_cursorX < nextTab && _cursorX < Width)
                    WriteTextElement(" ", 1);
                return;
        }

        if (columnWidth <= 0 || _cursorY >= Height)
            return;

        if (_cursorX + columnWidth > Width)
        {
            _cursorX = 0;
            _cursorY++;
            if (_cursorY >= Height)
                return;
        }

        ClearWideCellAt(_cursorX, _cursorY);
        _pendingFrame.TrySet(
            _cursorX,
            _cursorY,
            new TerminalCell(
                text,
                _currentForeground,
                _currentBackground,
                _currentDecoration));

        if (columnWidth == 2 && _cursorX + 1 < Width)
        {
            _pendingFrame.TrySet(
                _cursorX + 1,
                _cursorY,
                TerminalCell.Continuation(
                    _currentForeground,
                    _currentBackground,
                    _currentDecoration));
        }

        _cursorX += columnWidth;
        if (_cursorX >= Width)
        {
            _cursorX = 0;
            _cursorY++;
        }
    }

    private void ClearWideCellAt(int x, int y)
    {
        if (x > 0 && _pendingFrame.GetSafe(x, y).IsContinuation)
            _pendingFrame.TrySet(x - 1, y, TerminalCell.Empty);

        if (x + 1 < Width && _pendingFrame.GetSafe(x + 1, y).IsContinuation)
            _pendingFrame.TrySet(x + 1, y, TerminalCell.Empty);
    }

    private void EnsureFrameSize()
    {
        if (_pendingFrame.Width == Width && _pendingFrame.Height == Height)
            return;

        _pendingFrame = new FrameBuffer(Width, Height);
        _cursorX = 0;
        _cursorY = 0;
    }

    private void ReplaceLiveRegion(FrameBuffer frame)
    {
        EraseLiveRegion();

        var nextLiveRows = GetContentHeight(frame);
        WriteRows(frame, nextLiveRows);
        _liveRows = nextLiveRows;
    }

    private void EraseLiveRegion()
    {
        if (_liveRows == 0)
            return;

        _inlineControl.MoveCursorUp(_liveRows);
        for (var row = 0; row < _liveRows; row++)
        {
            _inlineControl.MoveCursorToLineStart();
            _inlineControl.EraseLine();
            _inlineControl.MoveCursorDown(1);
        }

        _inlineControl.MoveCursorUp(_liveRows);
        _inlineControl.MoveCursorToLineStart();
    }

    private void WriteRows(FrameBuffer frame, int rowCount)
    {
        var lastStyle = TerminalCell.Empty;
        for (var y = 0; y < rowCount; y++)
        {
            var lastColumn = GetContentWidth(frame, y);
            for (var x = 0; x < lastColumn; x++)
            {
                var cell = frame[x, y];
                if (cell.IsContinuation)
                    continue;

                EmitStyleChanges(cell, lastStyle);
                _inner.Write(cell.Text);
                lastStyle = cell;
            }

            _inner.ResetColors();
            lastStyle = TerminalCell.Empty;
            _inlineControl.WriteLineBreak();
        }
    }

    private void EmitStyleChanges(TerminalCell cell, TerminalCell previous)
    {
        if (cell.Decoration != previous.Decoration)
        {
            _inner.ResetColors();
            EmitDecoration(cell.Decoration);
            previous = TerminalCell.Empty;
        }

        if (cell.Foreground != previous.Foreground)
            _inner.SetForeground(cell.Foreground);

        if (cell.Background != previous.Background)
            _inner.SetBackground(cell.Background);
    }

    private void EmitDecoration(TextDecoration decoration)
    {
        if (decoration.HasFlag(TextDecoration.Bold))
            _inner.Write(AnsiCodes.Bold);
        if (decoration.HasFlag(TextDecoration.Dim))
            _inner.Write(AnsiCodes.Dim);
        if (decoration.HasFlag(TextDecoration.Italic))
            _inner.Write(AnsiCodes.Italic);
        if (decoration.HasFlag(TextDecoration.Underline))
            _inner.Write(AnsiCodes.Underline);
        if (decoration.HasFlag(TextDecoration.Strikethrough))
            _inner.Write(AnsiCodes.Strikethrough);
    }

    private static int GetContentHeight(FrameBuffer frame)
    {
        for (var y = frame.Height - 1; y >= 0; y--)
        {
            if (GetContentWidth(frame, y) > 0)
                return y + 1;
        }

        return 0;
    }

    private static int GetContentWidth(FrameBuffer frame, int y)
    {
        for (var x = frame.Width - 1; x >= 0; x--)
        {
            if (frame[x, y] != TerminalCell.Empty)
                return x + 1;
        }

        return 0;
    }
}
