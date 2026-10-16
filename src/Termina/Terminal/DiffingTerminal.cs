// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;

namespace Termina.Terminal;

/// <summary>
/// Terminal wrapper that provides diff-based rendering to eliminate flickering.
/// Uses double-buffering: renders to a pending buffer, then only outputs changed cells.
/// </summary>
/// <remarks>
/// <para>
/// Instead of clearing the screen and redrawing everything on each frame,
/// DiffingTerminal maintains two frame buffers:
/// </para>
/// <list type="bullet">
/// <item><description><c>_currentFrame</c>: What's currently displayed on the terminal</description></item>
/// <item><description><c>_pendingFrame</c>: What we want to display (rendering target)</description></item>
/// </list>
/// <para>
/// On <see cref="Flush"/>, only cells that differ between the two buffers are output,
/// minimizing ANSI traffic and eliminating visual flicker.
/// </para>
/// </remarks>
public sealed class DiffingTerminal : IAnsiTerminal, IDisposable
{
    private readonly IAnsiTerminal _inner;
    private FrameBuffer _currentFrame;
    private FrameBuffer _pendingFrame;

    // Cached dimensions - refreshed at start of each frame to avoid repeated Console calls
    private int _cachedWidth;
    private int _cachedHeight;

    // Current rendering state (for pending buffer writes)
    private int _cursorX;
    private int _cursorY;
    private Color _currentForeground = Color.Default;
    private Color _currentBackground = Color.Default;
    private TextDecoration _currentDecoration = TextDecoration.None;

    // Saved cursor position
    private int _savedCursorX;
    private int _savedCursorY;

    // Force full refresh on next Flush (e.g., after resize)
    private bool _forceFullRefresh = true;

    // Last output state tracking for efficient ANSI emission
    private Color _lastOutputForeground = Color.Default;
    private Color _lastOutputBackground = Color.Default;
    private TextDecoration _lastOutputDecoration = TextDecoration.None;

    /// <summary>
    /// Creates a new DiffingTerminal wrapping the specified terminal.
    /// </summary>
    /// <param name="inner">The underlying terminal to write to.</param>
    public DiffingTerminal(IAnsiTerminal inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        _cachedWidth = Math.Max(1, inner.Width);
        _cachedHeight = Math.Max(1, inner.Height);

        _currentFrame = new FrameBuffer(_cachedWidth, _cachedHeight);
        _pendingFrame = new FrameBuffer(_cachedWidth, _cachedHeight);
    }

    /// <inheritdoc />
    public int Width => _cachedWidth;

    /// <inheritdoc />
    public int Height => _cachedHeight;

    /// <summary>
    /// Forces a full screen redraw on the next <see cref="Flush"/> call.
    /// Call this after terminal resize or when the terminal state may be corrupted.
    /// </summary>
    public void ForceFullRefresh()
    {
        _forceFullRefresh = true;
    }

    /// <inheritdoc />
    public void MoveTo(int x, int y)
    {
        _cursorX = Math.Clamp(x, 0, Width - 1);
        _cursorY = Math.Clamp(y, 0, Height - 1);
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        foreach (var cell in DisplayWidth.EnumerateCells(text))
        {
            WriteTextElementToBuffer(cell.Text, cell.ColumnWidth);
        }
    }

    /// <inheritdoc />
    public void Write(char c)
    {
        WriteTextElementToBuffer(c.ToString(), DisplayWidth.GetColumnCount(c));
    }

    private void WriteTextElementToBuffer(string text, int columnWidth)
    {
        if (_cursorX < 0 || _cursorX >= Width || _cursorY < 0 || _cursorY >= Height)
            return;

        // Handle special characters
        switch (text)
        {
            case "\n":
                _cursorY++;
                _cursorX = 0;
                return;
            case "\r":
                _cursorX = 0;
                return;
            case "\t":
                // Tab to next 8-column boundary
                var nextTab = ((_cursorX / 8) + 1) * 8;
                while (_cursorX < nextTab && _cursorX < Width)
                {
                    WriteTextElementToBuffer(" ", 1);
                }
                return;
        }

        if (columnWidth <= 0)
            return;

        if (_cursorX + columnWidth > Width)
        {
            _cursorX = 0;
            _cursorY++;
            if (_cursorY >= Height)
                return;
        }

        // Write the cell to pending buffer
        ClearWideCellAt(_cursorX, _cursorY);

        var cell = new TerminalCell(text, _currentForeground, _currentBackground, _currentDecoration);
        _pendingFrame.TrySet(_cursorX, _cursorY, cell);

        if (columnWidth == 2 && _cursorX + 1 < Width)
        {
            _pendingFrame.TrySet(_cursorX + 1, _cursorY,
                TerminalCell.Continuation(_currentForeground, _currentBackground, _currentDecoration));
        }

        // Advance cursor
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

    /// <inheritdoc />
    public void SetForeground(Color color)
    {
        _currentForeground = color;
    }

    /// <inheritdoc />
    public void SetBackground(Color color)
    {
        _currentBackground = color;
    }

    /// <summary>
    /// Sets the text decoration for subsequent writes.
    /// </summary>
    public void SetDecoration(TextDecoration decoration)
    {
        _currentDecoration = decoration;
    }

    /// <inheritdoc />
    public void ResetColors()
    {
        _currentForeground = Color.Default;
        _currentBackground = Color.Default;
        _currentDecoration = TextDecoration.None;
    }

    /// <inheritdoc />
    public void SaveCursor()
    {
        _savedCursorX = _cursorX;
        _savedCursorY = _cursorY;
    }

    /// <inheritdoc />
    public void RestoreCursor()
    {
        _cursorX = _savedCursorX;
        _cursorY = _savedCursorY;
    }

    /// <inheritdoc />
    public void SetCursorVisible(bool visible)
    {
        // Pass through to inner terminal immediately
        _inner.SetCursorVisible(visible);
    }

    /// <inheritdoc />
    public void ClearRegion(int x, int y, int width, int height)
    {
        // Clear region in pending buffer (not actual terminal)
        _pendingFrame.Fill(x, y, width, height, TerminalCell.Empty);
    }

    /// <inheritdoc />
    public void ClearScreen()
    {
        // Clear pending buffer only - DO NOT emit ANSI clear
        // This is the key to eliminating flickering!
        _pendingFrame.Clear();
        _cursorX = 0;
        _cursorY = 0;
    }

    /// <inheritdoc />
    public void Flush()
    {
        // Handle resize if dimensions changed
        HandleResize();

        if (_forceFullRefresh)
        {
            FlushFull();
            _forceFullRefresh = false;
        }
        else
        {
            FlushDiff();
        }

        // Copy pending to current (pending is now what's on screen)
        _currentFrame.CopyFrom(_pendingFrame);

        // Flush the inner terminal
        _inner.Flush();
    }

    private void HandleResize()
    {
        // Refresh cached dimensions from actual console (only place we call inner.Width/Height)
        var newWidth = _inner.Width;
        var newHeight = _inner.Height;

        // Skip resize if dimensions are invalid (e.g., headless CI environment)
        if (newWidth <= 0 || newHeight <= 0)
            return;

        // Update cache
        _cachedWidth = newWidth;
        _cachedHeight = newHeight;

        if (newWidth != _pendingFrame.Width || newHeight != _pendingFrame.Height)
        {
            _pendingFrame.Resize(newWidth, newHeight);
            _currentFrame.Resize(newWidth, newHeight);
            _forceFullRefresh = true;
        }
    }

    /// <summary>
    /// Output the entire pending buffer (used for first frame or after resize).
    /// </summary>
    private void FlushFull()
    {
        // Clear the actual terminal first for a clean slate
        _inner.ClearScreen();

        // Reset output state
        _lastOutputForeground = Color.Default;
        _lastOutputBackground = Color.Default;
        _lastOutputDecoration = TextDecoration.None;

        for (var y = 0; y < _pendingFrame.Height; y++)
        {
            _inner.MoveTo(0, y);

            for (var x = 0; x < _pendingFrame.Width; x++)
            {
                var cell = _pendingFrame[x, y];
                if (cell.IsContinuation)
                    continue;
                EmitStyleChanges(cell);
                _inner.Write(cell.Text);
            }
        }

        _inner.ResetColors();
    }

    /// <summary>
    /// Output only changed cells by diffing pending vs current buffer.
    /// </summary>
    private void FlushDiff()
    {
        // Reset output state tracking
        _lastOutputForeground = Color.Default;
        _lastOutputBackground = Color.Default;
        _lastOutputDecoration = TextDecoration.None;

        // Use GetChangedRuns for efficient output (groups consecutive changes)
        foreach (var (y, startX, cells) in _pendingFrame.GetChangedRuns(_currentFrame))
        {
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell.IsContinuation)
                    continue;

                _inner.MoveTo(startX + i, y);
                EmitStyleChanges(cell);
                _inner.Write(cell.Text);
            }
        }

        _inner.ResetColors();
        _lastOutputForeground = Color.Default;
        _lastOutputBackground = Color.Default;
        _lastOutputDecoration = TextDecoration.None;
    }

    /// <summary>
    /// Emit ANSI sequences for style changes if needed.
    /// </summary>
    private void EmitStyleChanges(TerminalCell cell)
    {
        // Check if decoration changed
        if (cell.Decoration != _lastOutputDecoration)
        {
            // Reset all decorations first, then apply new ones
            // This is simpler than tracking individual decoration bits
            _inner.ResetColors();
            _lastOutputForeground = Color.Default;
            _lastOutputBackground = Color.Default;

            EmitDecoration(cell.Decoration);
            _lastOutputDecoration = cell.Decoration;
        }

        // Check foreground
        if (cell.Foreground != _lastOutputForeground)
        {
            _inner.SetForeground(cell.Foreground);
            _lastOutputForeground = cell.Foreground;
        }

        // Check background
        if (cell.Background != _lastOutputBackground)
        {
            _inner.SetBackground(cell.Background);
            _lastOutputBackground = cell.Background;
        }
    }

    /// <summary>
    /// Emit ANSI codes for text decoration.
    /// </summary>
    private void EmitDecoration(TextDecoration decoration)
    {
        if (decoration == TextDecoration.None)
            return;

        // Build and emit decoration codes
        var sb = new StringBuilder();

        if ((decoration & TextDecoration.Bold) != 0)
            sb.Append(AnsiCodes.Bold);

        if ((decoration & TextDecoration.Dim) != 0)
            sb.Append(AnsiCodes.Dim);

        if ((decoration & TextDecoration.Italic) != 0)
            sb.Append(AnsiCodes.Italic);

        if ((decoration & TextDecoration.Underline) != 0)
            sb.Append(AnsiCodes.Underline);

        if ((decoration & TextDecoration.Strikethrough) != 0)
            sb.Append(AnsiCodes.Strikethrough);

        if (sb.Length > 0)
        {
            _inner.Write(sb.ToString());
        }
    }

    // Pass-through methods that don't affect buffering

    /// <inheritdoc />
    public void EnterAlternateScreen()
    {
        _inner.EnterAlternateScreen();
        _forceFullRefresh = true;
    }

    /// <inheritdoc />
    public void ExitAlternateScreen()
    {
        _inner.ExitAlternateScreen();
    }

    /// <inheritdoc />
    public void EnableMouse()
    {
        _inner.EnableMouse();
    }

    /// <inheritdoc />
    public void DisableMouse()
    {
        _inner.DisableMouse();
    }

    /// <inheritdoc />
    public void EnableWheelScroll()
    {
        _inner.EnableWheelScroll();
    }

    /// <inheritdoc />
    public void DisableWheelScroll()
    {
        _inner.DisableWheelScroll();
    }

    /// <inheritdoc />
    public void CopyToClipboard(string text)
    {
        _inner.CopyToClipboard(text);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
