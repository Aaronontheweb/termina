// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Terminal;

/// <summary>
/// A 2D buffer of terminal cells for double-buffering and diff-based rendering.
/// </summary>
/// <remarks>
/// The frame buffer stores a snapshot of the terminal screen state. By maintaining
/// two buffers (current and pending), we can diff them to determine which cells
/// have changed and only emit ANSI sequences for those cells.
/// </remarks>
public sealed class FrameBuffer
{
    private TerminalCell[,] _cells;

    /// <summary>
    /// The width of the buffer in characters.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// The height of the buffer in lines.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Creates a new frame buffer with the specified dimensions.
    /// All cells are initialized to empty (space with default colors).
    /// </summary>
    public FrameBuffer(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive");

        Width = width;
        Height = height;
        _cells = new TerminalCell[height, width];
        Clear();
    }

    /// <summary>
    /// Gets or sets the cell at the specified position.
    /// </summary>
    /// <param name="x">Column index (0-based).</param>
    /// <param name="y">Row index (0-based).</param>
    public ref TerminalCell this[int x, int y]
    {
        get
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                throw new ArgumentOutOfRangeException($"Position ({x}, {y}) is outside buffer bounds ({Width}x{Height})");
            return ref _cells[y, x];
        }
    }

    /// <summary>
    /// Gets the cell at the specified position, or <see cref="TerminalCell.Empty"/> if out of bounds.
    /// </summary>
    public TerminalCell GetSafe(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return TerminalCell.Empty;
        return _cells[y, x];
    }

    /// <summary>
    /// Sets the cell at the specified position if within bounds.
    /// </summary>
    /// <returns>True if the cell was set, false if out of bounds.</returns>
    public bool TrySet(int x, int y, TerminalCell cell)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;
        _cells[y, x] = cell;
        return true;
    }

    /// <summary>
    /// Clears all cells to empty (space with default colors).
    /// </summary>
    public void Clear()
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                _cells[y, x] = TerminalCell.Empty;
            }
        }
    }

    /// <summary>
    /// Fills a rectangular region with the specified cell.
    /// </summary>
    public void Fill(int startX, int startY, int width, int height, TerminalCell cell)
    {
        var endX = Math.Min(startX + width, Width);
        var endY = Math.Min(startY + height, Height);
        startX = Math.Max(0, startX);
        startY = Math.Max(0, startY);

        for (var y = startY; y < endY; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                _cells[y, x] = cell;
            }
        }
    }

    /// <summary>
    /// Resizes the buffer to new dimensions.
    /// Existing content in the overlapping area is preserved.
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth), "Width must be positive");
        if (newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newHeight), "Height must be positive");

        if (newWidth == Width && newHeight == Height)
            return;

        var newCells = new TerminalCell[newHeight, newWidth];

        // Initialize new buffer with empty cells
        for (var y = 0; y < newHeight; y++)
        {
            for (var x = 0; x < newWidth; x++)
            {
                newCells[y, x] = TerminalCell.Empty;
            }
        }

        // Copy overlapping content from old buffer
        var copyWidth = Math.Min(Width, newWidth);
        var copyHeight = Math.Min(Height, newHeight);

        for (var y = 0; y < copyHeight; y++)
        {
            for (var x = 0; x < copyWidth; x++)
            {
                newCells[y, x] = _cells[y, x];
            }
        }

        _cells = newCells;
        Width = newWidth;
        Height = newHeight;
    }

    /// <summary>
    /// Copies all cells from another buffer into this buffer.
    /// Buffers must have the same dimensions.
    /// </summary>
    public void CopyFrom(FrameBuffer source)
    {
        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException($"Source buffer dimensions ({source.Width}x{source.Height}) don't match this buffer ({Width}x{Height})");

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                _cells[y, x] = source._cells[y, x];
            }
        }
    }

    /// <summary>
    /// Compares this buffer to another and returns the positions of changed cells.
    /// </summary>
    /// <param name="other">The buffer to compare against.</param>
    /// <returns>An enumerable of (X, Y) positions where cells differ.</returns>
    public IEnumerable<(int X, int Y)> GetChangedCells(FrameBuffer other)
    {
        if (other.Width != Width || other.Height != Height)
            throw new ArgumentException($"Buffer dimensions don't match: ({Width}x{Height}) vs ({other.Width}x{other.Height})");

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (!_cells[y, x].Equals(other._cells[y, x]))
                {
                    yield return (x, y);
                }
            }
        }
    }

    /// <summary>
    /// Compares this buffer to another and returns changed cells grouped by row
    /// as contiguous runs. This is more efficient for ANSI output since we can
    /// minimize cursor movement.
    /// </summary>
    /// <param name="other">The buffer to compare against.</param>
    /// <returns>An enumerable of (Y, StartX, Cells) representing contiguous runs of changed cells on each row.</returns>
    public IEnumerable<(int Y, int StartX, TerminalCell[] Cells)> GetChangedRuns(FrameBuffer other)
    {
        if (other.Width != Width || other.Height != Height)
            throw new ArgumentException($"Buffer dimensions don't match: ({Width}x{Height}) vs ({other.Width}x{other.Height})");

        for (var y = 0; y < Height; y++)
        {
            int? runStart = null;
            var runCells = new List<TerminalCell>();

            for (var x = 0; x < Width; x++)
            {
                var thisCell = _cells[y, x];
                var otherCell = other._cells[y, x];
                var changed = !thisCell.Equals(otherCell);

                if (changed)
                {
                    runStart ??= x;
                    runCells.Add(thisCell);
                }
                else if (runStart.HasValue)
                {
                    // End of run - yield it
                    yield return (y, runStart.Value, runCells.ToArray());
                    runStart = null;
                    runCells.Clear();
                }
            }

            // Yield final run if we ended in the middle of one
            if (runStart.HasValue)
            {
                yield return (y, runStart.Value, runCells.ToArray());
            }
        }
    }

    /// <summary>
    /// Gets the content of a row as a string (characters only, no styling).
    /// </summary>
    public string GetRowText(int y)
    {
        if (y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        var sb = new System.Text.StringBuilder(Width);
        for (var x = 0; x < Width; x++)
        {
            if (_cells[y, x].IsContinuation)
                continue;
            sb.Append(_cells[y, x].Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Gets all content as a multi-line string (characters only, no styling).
    /// </summary>
    public string GetAllText()
    {
        var lines = new string[Height];
        for (var y = 0; y < Height; y++)
        {
            lines[y] = GetRowText(y);
        }
        return string.Join(Environment.NewLine, lines);
    }
}
