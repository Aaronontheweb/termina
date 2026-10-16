// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;

namespace Termina.Terminal;

/// <summary>
/// Virtual terminal implementation for testing.
/// Captures all output to an in-memory buffer that can be inspected.
/// </summary>
public sealed class VirtualTerminal : IAnsiTerminal, IInlineTerminalControl
{
    private char[,] _buffer;
    private string[,] _textBuffer;
    private bool[,] _continuation;
    private Color[,] _foreground;
    private Color[,] _background;
    private readonly List<string> _rawOutput = new();

    private int _cursorX;
    private int _cursorY;
    private int _savedCursorX;
    private int _savedCursorY;
    private Color _currentForeground = Color.Default;
    private Color _currentBackground = Color.Default;

    /// <summary>
    /// Create a virtual terminal with the specified dimensions.
    /// </summary>
    public VirtualTerminal(int width = 80, int height = 24)
    {
        Width = width;
        Height = height;
        _buffer = new char[height, width];
        _textBuffer = new string[height, width];
        _continuation = new bool[height, width];
        _foreground = new Color[height, width];
        _background = new Color[height, width];
        Clear();
    }

    /// <inheritdoc />
    public int Width { get; private set; }

    /// <inheritdoc />
    public int Height { get; private set; }

    /// <summary>
    /// Current cursor X position (0-indexed).
    /// </summary>
    public int CursorX => _cursorX;

    /// <summary>
    /// Current cursor Y position (0-indexed).
    /// </summary>
    public int CursorY => _cursorY;

    /// <summary>
    /// Whether the cursor is visible.
    /// </summary>
    public bool CursorVisible { get; private set; } = true;

    /// <summary>
    /// Whether the terminal is in alternate screen mode.
    /// </summary>
    public bool InAlternateScreen { get; private set; }

    /// <summary>
    /// Whether mouse tracking is enabled.
    /// </summary>
    public bool MouseEnabled { get; private set; }

    /// <summary>
    /// Whether alternate-scroll (wheel-only) mode is enabled.
    /// </summary>
    public bool WheelScrollEnabled { get; private set; }

    /// <summary>
    /// Raw output strings written to the terminal (for debugging).
    /// </summary>
    public IReadOnlyList<string> RawOutput => _rawOutput;

    /// <inheritdoc />
    public void MoveTo(int x, int y)
    {
        _cursorX = Math.Clamp(x, 0, Width - 1);
        _cursorY = Math.Clamp(y, 0, Height - 1);
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        _rawOutput.Add(text);
        if (text.Contains('\u001b'))
            return;

        foreach (var cell in DisplayWidth.EnumerateCells(text))
        {
            WriteTextElement(cell.Text, cell.ColumnWidth);
        }
    }

    /// <inheritdoc />
    public void Write(char c)
    {
        _rawOutput.Add(c.ToString());
        WriteTextElement(c.ToString(), DisplayWidth.GetColumnCount(c));
    }

    private void WriteTextElement(string text, int columnWidth)
    {
        if (text == "\n")
        {
            _cursorX = 0;
            AdvanceLine();
            return;
        }

        if (text == "\r")
        {
            _cursorX = 0;
            return;
        }

        if (text == "\t")
        {
            var nextTab = ((_cursorX / 8) + 1) * 8;
            while (_cursorX < nextTab && _cursorX < Width)
                WriteTextElement(" ", 1);
            return;
        }

        if (columnWidth <= 0)
            return;

        if (_cursorX + columnWidth > Width)
        {
            _cursorX = 0;
            _cursorY++;
            if (_cursorY >= Height)
            {
                _cursorY = Height - 1;
                return;
            }
        }

        if (_cursorX < Width && _cursorY < Height)
        {
            ClearWideCellAt(_cursorX, _cursorY);

            _buffer[_cursorY, _cursorX] = text[0];
            _textBuffer[_cursorY, _cursorX] = text;
            _continuation[_cursorY, _cursorX] = false;
            _foreground[_cursorY, _cursorX] = _currentForeground;
            _background[_cursorY, _cursorX] = _currentBackground;

            if (columnWidth == 2 && _cursorX + 1 < Width)
            {
                _buffer[_cursorY, _cursorX + 1] = ' ';
                _textBuffer[_cursorY, _cursorX + 1] = " ";
                _continuation[_cursorY, _cursorX + 1] = true;
                _foreground[_cursorY, _cursorX + 1] = _currentForeground;
                _background[_cursorY, _cursorX + 1] = _currentBackground;
            }

            _cursorX += columnWidth;

            if (_cursorX >= Width)
            {
                _cursorX = 0;
                _cursorY++;
                if (_cursorY >= Height)
                {
                    _cursorY = Height - 1;
                }
            }
        }
    }

    private void ClearWideCellAt(int x, int y)
    {
        if (x > 0 && _continuation[y, x])
        {
            _buffer[y, x - 1] = ' ';
            _textBuffer[y, x - 1] = " ";
            _continuation[y, x - 1] = false;
            _foreground[y, x - 1] = Color.Default;
            _background[y, x - 1] = Color.Default;
        }

        if (x + 1 < Width && _continuation[y, x + 1])
        {
            _buffer[y, x + 1] = ' ';
            _textBuffer[y, x + 1] = " ";
            _continuation[y, x + 1] = false;
            _foreground[y, x + 1] = Color.Default;
            _background[y, x + 1] = Color.Default;
        }
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

    /// <inheritdoc />
    public void ResetColors()
    {
        _currentForeground = Color.Default;
        _currentBackground = Color.Default;
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
    public void MoveCursorUp(int rows)
    {
        if (rows > 0)
            _cursorY = Math.Max(0, _cursorY - rows);
    }

    /// <inheritdoc />
    public void MoveCursorDown(int rows)
    {
        if (rows > 0)
            _cursorY = Math.Min(Height - 1, _cursorY + rows);
    }

    /// <inheritdoc />
    public void MoveCursorToLineStart()
    {
        _cursorX = 0;
    }

    /// <inheritdoc />
    public void EraseLine()
    {
        ClearRegion(0, _cursorY, Width, 1);
    }

    /// <inheritdoc />
    public void WriteLineBreak()
    {
        _rawOutput.Add("\r\n");
        _cursorX = 0;
        AdvanceLine();
    }

    private void AdvanceLine()
    {
        _cursorY++;
        if (_cursorY < Height)
            return;

        for (var y = 1; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                _buffer[y - 1, x] = _buffer[y, x];
                _textBuffer[y - 1, x] = _textBuffer[y, x];
                _continuation[y - 1, x] = _continuation[y, x];
                _foreground[y - 1, x] = _foreground[y, x];
                _background[y - 1, x] = _background[y, x];
            }
        }

        ClearRegion(0, Height - 1, Width, 1);
        _cursorY = Height - 1;
    }

    /// <inheritdoc />
    public void SetCursorVisible(bool visible)
    {
        CursorVisible = visible;
    }

    /// <inheritdoc />
    public void ClearRegion(int x, int y, int width, int height)
    {
        for (var row = y; row < y + height && row < Height; row++)
        {
            for (var col = x; col < x + width && col < Width; col++)
            {
                if (row >= 0 && col >= 0)
                {
                    _buffer[row, col] = ' ';
                    _textBuffer[row, col] = " ";
                    _continuation[row, col] = false;
                    _foreground[row, col] = Color.Default;
                    _background[row, col] = Color.Default;
                }
            }
        }
    }

    /// <inheritdoc />
    public void ClearScreen()
    {
        Clear();
        MoveTo(0, 0);
    }

    /// <inheritdoc />
    public void Flush()
    {
        // No-op for virtual terminal - everything is already in buffer
    }

    /// <inheritdoc />
    public void EnterAlternateScreen()
    {
        InAlternateScreen = true;
    }

    /// <inheritdoc />
    public void ExitAlternateScreen()
    {
        InAlternateScreen = false;
    }

    /// <inheritdoc />
    public void EnableMouse()
    {
        MouseEnabled = true;
    }

    /// <inheritdoc />
    public void DisableMouse()
    {
        MouseEnabled = false;
    }

    /// <inheritdoc />
    public void EnableWheelScroll()
    {
        WheelScrollEnabled = true;
    }

    /// <inheritdoc />
    public void DisableWheelScroll()
    {
        WheelScrollEnabled = false;
    }

    /// <inheritdoc />
    public void CopyToClipboard(string text)
    {
        _rawOutput.Add(AnsiCodes.Osc52Clipboard(text));
    }

    /// <summary>
    /// Clear the entire buffer.
    /// </summary>
    public void Clear()
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                _buffer[y, x] = ' ';
                _textBuffer[y, x] = " ";
                _continuation[y, x] = false;
                _foreground[y, x] = Color.Default;
                _background[y, x] = Color.Default;
            }
        }
    }

    /// <summary>
    /// Resize the terminal (for testing resize events).
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        var newBuffer = new char[newHeight, newWidth];
        var newTextBuffer = new string[newHeight, newWidth];
        var newContinuation = new bool[newHeight, newWidth];
        var newForeground = new Color[newHeight, newWidth];
        var newBackground = new Color[newHeight, newWidth];

        // Initialize with spaces
        for (var y = 0; y < newHeight; y++)
        {
            for (var x = 0; x < newWidth; x++)
            {
                newBuffer[y, x] = ' ';
                newTextBuffer[y, x] = " ";
                newContinuation[y, x] = false;
                newForeground[y, x] = Color.Default;
                newBackground[y, x] = Color.Default;
            }
        }

        // Copy existing content
        var copyHeight = Math.Min(Height, newHeight);
        var copyWidth = Math.Min(Width, newWidth);
        for (var y = 0; y < copyHeight; y++)
        {
            for (var x = 0; x < copyWidth; x++)
            {
                newBuffer[y, x] = _buffer[y, x];
                newTextBuffer[y, x] = _textBuffer[y, x];
                newContinuation[y, x] = _continuation[y, x];
                newForeground[y, x] = _foreground[y, x];
                newBackground[y, x] = _background[y, x];
            }
        }

        _buffer = newBuffer;
        _textBuffer = newTextBuffer;
        _continuation = newContinuation;
        _foreground = newForeground;
        _background = newBackground;

        // Update dimensions.
        Width = newWidth;
        Height = newHeight;

        // Clamp cursor
        _cursorX = Math.Min(_cursorX, newWidth - 1);
        _cursorY = Math.Min(_cursorY, newHeight - 1);
    }

    // Query methods for testing assertions

    /// <summary>
    /// Get the character at the specified position.
    /// </summary>
    public char GetChar(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return ' ';
        return _buffer[y, x];
    }

    /// <summary>
    /// Get the foreground color at the specified position.
    /// </summary>
    public Color GetForeground(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return Color.Default;
        return _foreground[y, x];
    }

    /// <summary>
    /// Get the background color at the specified position.
    /// </summary>
    public Color GetBackground(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return Color.Default;
        return _background[y, x];
    }

    /// <summary>
    /// Get a single line from the buffer.
    /// </summary>
    public string GetLine(int y)
    {
        if (y < 0 || y >= Height)
            return string.Empty;

        var sb = new StringBuilder(Width);
        for (var x = 0; x < Width; x++)
        {
            if (_continuation[y, x])
                continue;
            sb.Append(_textBuffer[y, x]);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Get text from a rectangular region.
    /// </summary>
    public string GetRegion(int x, int y, int width, int height)
    {
        var sb = new StringBuilder();
        for (var row = y; row < y + height && row < Height; row++)
        {
            if (row > y) sb.AppendLine();
            for (var col = x; col < x + width && col < Width; col++)
            {
                if (row >= 0 && col >= 0)
                {
                    if (_continuation[row, col])
                        continue;
                    sb.Append(_textBuffer[row, col]);
                }
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Get all lines as a string array.
    /// </summary>
    public string[] GetAllLines()
    {
        var lines = new string[Height];
        for (var y = 0; y < Height; y++)
        {
            lines[y] = GetLine(y);
        }
        return lines;
    }

    /// <summary>
    /// Check if the buffer contains the specified text anywhere.
    /// </summary>
    public bool Contains(string text)
    {
        var fullText = string.Join("\n", GetAllLines());
        return fullText.Contains(text);
    }

    /// <summary>
    /// Get a snapshot of the entire screen as a string.
    /// </summary>
    public override string ToString()
    {
        return string.Join("\n", GetAllLines());
    }
}
