// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// Navigation mode for grid focus.
/// </summary>
public enum GridNavigationMode
{
    /// <summary>
    /// No keyboard navigation - grid is display only.
    /// </summary>
    None,

    /// <summary>
    /// Arrow keys navigate between cells. Enter activates focused cell.
    /// </summary>
    CellNavigation,

    /// <summary>
    /// Tab routes focus to focusable children within cells.
    /// Arrow keys still navigate between cells when no child has focus.
    /// </summary>
    ChildFocusRouting
}

/// <summary>
/// A 2D grid layout container with consistent column widths and row heights across all cells.
/// </summary>
/// <remarks>
/// <para>
/// GridNode provides true 2D layout where:
/// - Column widths are consistent across all rows
/// - Row heights are consistent across all columns
/// - Optional grid lines can be rendered between cells
/// - Cells can span multiple columns and/or rows
/// - Focus navigation supports 2D movement (arrows) between cells
/// </para>
/// <para>
/// This differs from nesting HorizontalLayout inside VerticalLayout because those
/// calculate widths independently per row - GridNode ensures consistent column sizing.
/// </para>
/// </remarks>
public sealed class GridNode : LayoutNode, IFocusable, IInvalidatingNode
{
    private readonly Subject<Unit> _invalidated = new();
    private readonly Subject<(int Row, int Col)> _focusedCellChanged = new();
    private readonly Subject<(int Row, int Col, ILayoutNode? Cell)> _cellActivated = new();
    // Invalidation subscription per content node, so a cell swap can dispose exactly the displaced
    // node's subscription. Keyed by content because the subscription belongs to the node, not the cell.
    private readonly Dictionary<ILayoutNode, IDisposable> _cellSubscriptions = new();

    private ILayoutNode?[,] _cells;
    private (int ColSpan, int RowSpan)[,] _spans;
    private SizeConstraint[] _columnConstraints;
    private SizeConstraint[] _rowConstraints;

    private int _rows;
    private int _cols;
    private int _focusedRow;
    private int _focusedCol;
    private bool _hasFocus;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the navigation mode for keyboard focus.
    /// </summary>
    public GridNavigationMode NavigationMode { get; private set; } = GridNavigationMode.None;

    /// <summary>
    /// Gets or sets the border style for grid lines.
    /// </summary>
    public BorderStyle GridLines { get; private set; } = BorderStyle.None;

    /// <summary>
    /// Gets or sets the color for grid lines.
    /// </summary>
    public Color? GridLineColor { get; private set; }

    /// <summary>
    /// Gets or sets the padding inside each cell.
    /// </summary>
    public int CellPadding { get; private set; }

    /// <summary>
    /// Gets or sets the highlight color for the focused cell background.
    /// </summary>
    public Color FocusedCellBackground { get; private set; } = Color.Blue;

    /// <summary>
    /// Gets or sets the highlight color for the focused cell foreground.
    /// </summary>
    public Color FocusedCellForeground { get; private set; } = Color.White;

    /// <summary>
    /// Gets whether to show focus highlight.
    /// </summary>
    public bool ShowFocusHighlight { get; private set; } = true;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated.AsObservable();

    /// <inheritdoc />
    internal override void DisconnectChildInvalidationSubscriptions()
    {
        foreach (var subscription in _cellSubscriptions.Values)
            subscription.Dispose();

        _cellSubscriptions.Clear();
    }

    /// <summary>
    /// Observable that emits when the focused cell changes.
    /// </summary>
    public Observable<(int Row, int Col)> FocusedCellChanged => _focusedCellChanged.AsObservable();

    /// <summary>
    /// Observable that emits when a cell is activated (Enter pressed).
    /// </summary>
    public Observable<(int Row, int Col, ILayoutNode? Cell)> CellActivated => _cellActivated.AsObservable();

    /// <summary>
    /// Gets the number of rows in the grid.
    /// </summary>
    public int RowCount => _rows;

    /// <summary>
    /// Gets the number of columns in the grid.
    /// </summary>
    public int ColumnCount => _cols;

    /// <summary>
    /// Gets the currently focused row.
    /// </summary>
    public int FocusedRow => _focusedRow;

    /// <summary>
    /// Gets the currently focused column.
    /// </summary>
    public int FocusedColumn => _focusedCol;

    /// <summary>
    /// Creates a new grid with the specified dimensions.
    /// </summary>
    /// <param name="rows">Number of rows.</param>
    /// <param name="cols">Number of columns.</param>
    public GridNode(int rows = 0, int cols = 0)
    {
        _rows = rows;
        _cols = cols;
        _cells = new ILayoutNode?[rows, cols];
        _spans = new (int, int)[rows, cols];
        _columnConstraints = new SizeConstraint[cols];
        _rowConstraints = new SizeConstraint[rows];

        // Initialize all spans to 1,1 and constraints to Auto
        for (var r = 0; r < rows; r++)
        {
            _rowConstraints[r] = new SizeConstraint.Auto();
            for (var c = 0; c < cols; c++)
            {
                _spans[r, c] = (1, 1);
                if (r == 0)
                    _columnConstraints[c] = new SizeConstraint.Auto();
            }
        }

        // Default to fill available space
        HeightConstraint = new SizeConstraint.Auto();
        WidthConstraint = new SizeConstraint.Fill();
    }

    #region Fluent Configuration

    /// <summary>
    /// Set column width constraints.
    /// </summary>
    public GridNode WithColumnWidths(params SizeConstraint[] constraints)
    {
        EnsureColumns(constraints.Length);
        for (var i = 0; i < constraints.Length && i < _cols; i++)
        {
            _columnConstraints[i] = constraints[i];
        }
        return this;
    }

    /// <summary>
    /// Set row height constraints.
    /// </summary>
    public GridNode WithRowHeights(params SizeConstraint[] constraints)
    {
        EnsureRows(constraints.Length);
        for (var i = 0; i < constraints.Length && i < _rows; i++)
        {
            _rowConstraints[i] = constraints[i];
        }
        return this;
    }

    /// <summary>
    /// Set grid line style.
    /// </summary>
    public GridNode WithGridLines(BorderStyle style)
    {
        GridLines = style;
        return this;
    }

    /// <summary>
    /// Set grid line color.
    /// </summary>
    public GridNode WithGridLineColor(Color color)
    {
        GridLineColor = color;
        return this;
    }

    /// <summary>
    /// Set cell padding.
    /// </summary>
    public GridNode WithCellPadding(int padding)
    {
        CellPadding = padding;
        return this;
    }

    /// <summary>
    /// Set navigation mode.
    /// </summary>
    public GridNode WithNavigationMode(GridNavigationMode mode)
    {
        NavigationMode = mode;
        return this;
    }

    /// <summary>
    /// Configure focus highlight colors.
    /// </summary>
    public GridNode WithFocusHighlight(Color background, Color foreground)
    {
        FocusedCellBackground = background;
        FocusedCellForeground = foreground;
        ShowFocusHighlight = true;
        return this;
    }

    /// <summary>
    /// Disable focus highlight.
    /// </summary>
    public GridNode WithoutFocusHighlight()
    {
        ShowFocusHighlight = false;
        return this;
    }

    /// <summary>
    /// Set a cell's content.
    /// </summary>
    /// <param name="row">Row index (0-based).</param>
    /// <param name="col">Column index (0-based).</param>
    /// <param name="content">The content node to place in the cell.</param>
    /// <param name="colSpan">Number of columns to span (default 1).</param>
    /// <param name="rowSpan">Number of rows to span (default 1).</param>
    public GridNode SetCell(int row, int col, ILayoutNode? content, int colSpan = 1, int rowSpan = 1)
    {
        EnsureRows(row + rowSpan);
        EnsureColumns(col + colSpan);

        // Remove whatever is currently in the target cell: deactivate its content (do not dispose — a
        // swapped-out node may be reused) and dispose that cell's own invalidation subscription.
        DisplaceCell(row, col);

        _cells[row, col] = content;
        _spans[row, col] = (colSpan, rowSpan);

        // Mark spanned cells as occupied (null content, 0 span indicates part of another cell).
        for (var r = row; r < row + rowSpan; r++)
        {
            for (var c = col; c < col + colSpan; c++)
            {
                if (r != row || c != col)
                {
                    DisplaceCell(r, c);
                    _spans[r, c] = (0, 0); // 0,0 indicates this is part of a span
                }
            }
        }

        // Subscribe to the new content's invalidation, tracked by content so a later swap can remove it.
        if (content is IInvalidatingNode invalidating)
        {
            _cellSubscriptions[content] =
                invalidating.Invalidated.Subscribe(_ => _invalidated.OnNext(Unit.Default));
        }

        return this;
    }

    /// <summary>
    /// Add a row with the specified cells.
    /// </summary>
    public GridNode AddRow(params ILayoutNode?[] cells)
    {
        var rowIndex = _rows;
        EnsureRows(rowIndex + 1);
        EnsureColumns(cells.Length);

        for (var c = 0; c < cells.Length; c++)
        {
            SetCell(rowIndex, c, cells[c]);
        }

        return this;
    }

    /// <summary>
    /// Define columns (ensures column count and sets constraints).
    /// </summary>
    public GridNode WithColumns(params SizeConstraint[] constraints)
    {
        EnsureColumns(constraints.Length);
        return WithColumnWidths(constraints);
    }

    /// <summary>
    /// Define rows (ensures row count and sets constraints).
    /// </summary>
    public GridNode WithRows(params SizeConstraint[] constraints)
    {
        EnsureRows(constraints.Length);
        return WithRowHeights(constraints);
    }

    #endregion

    /// <summary>
    /// Removes the content currently occupying a cell: disposes that cell's invalidation subscription and
    /// deactivates the content (active/inactive pattern), then clears the cell. The content is not disposed
    /// here — a swapped-out node may still be reused; disposal happens only in <see cref="Dispose"/>.
    /// </summary>
    private void DisplaceCell(int row, int col)
    {
        var content = _cells[row, col];
        if (content is null)
            return;

        if (_cellSubscriptions.Remove(content, out var subscription))
            subscription.Dispose();

        if (content is IActivatableNode activatable)
            activatable.OnDeactivate();

        _cells[row, col] = null;
    }

    #region Grid Resizing

    private void EnsureRows(int minRows)
    {
        if (minRows <= _rows) return;

        var newCells = new ILayoutNode?[minRows, _cols];
        var newSpans = new (int, int)[minRows, _cols];
        var newRowConstraints = new SizeConstraint[minRows];

        // Copy existing data
        for (var r = 0; r < _rows; r++)
        {
            newRowConstraints[r] = _rowConstraints[r];
            for (var c = 0; c < _cols; c++)
            {
                newCells[r, c] = _cells[r, c];
                newSpans[r, c] = _spans[r, c];
            }
        }

        // Initialize new rows
        for (var r = _rows; r < minRows; r++)
        {
            newRowConstraints[r] = new SizeConstraint.Auto();
            for (var c = 0; c < _cols; c++)
            {
                newSpans[r, c] = (1, 1);
            }
        }

        _cells = newCells;
        _spans = newSpans;
        _rowConstraints = newRowConstraints;
        _rows = minRows;
    }

    private void EnsureColumns(int minCols)
    {
        if (minCols <= _cols) return;

        var newCells = new ILayoutNode?[_rows, minCols];
        var newSpans = new (int, int)[_rows, minCols];
        var newColConstraints = new SizeConstraint[minCols];

        // Copy existing data
        for (var c = 0; c < _cols; c++)
        {
            newColConstraints[c] = _columnConstraints[c];
        }
        for (var r = 0; r < _rows; r++)
        {
            for (var c = 0; c < _cols; c++)
            {
                newCells[r, c] = _cells[r, c];
                newSpans[r, c] = _spans[r, c];
            }
        }

        // Initialize new columns
        for (var c = _cols; c < minCols; c++)
        {
            newColConstraints[c] = new SizeConstraint.Auto();
            for (var r = 0; r < _rows; r++)
            {
                newSpans[r, c] = (1, 1);
            }
        }

        _cells = newCells;
        _spans = newSpans;
        _columnConstraints = newColConstraints;
        _cols = minCols;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        if (_rows == 0 || _cols == 0)
            return Size.Zero;

        var hasGridLines = GridLines != BorderStyle.None;
        var gridLineWidth = hasGridLines ? 1 : 0;

        // Calculate available space for content (subtract grid lines)
        var totalGridLinesH = gridLineWidth * (_cols + 1); // left + between + right
        var totalGridLinesV = gridLineWidth * (_rows + 1); // top + between + bottom
        var contentWidth = Math.Max(0, available.Width - totalGridLinesH);
        var contentHeight = Math.Max(0, available.Height - totalGridLinesV);

        // Solve column widths
        var columnWidths = SolveConstraints(_columnConstraints, contentWidth, MeasureColumnContent);

        // Solve row heights
        var rowHeights = SolveConstraints(_rowConstraints, contentHeight, (index, maxSize) => MeasureRowContent(index, maxSize, columnWidths));

        var totalWidth = columnWidths.Sum() + totalGridLinesH;
        var totalHeight = rowHeights.Sum() + totalGridLinesV;

        return new Size(
            WidthConstraint.Compute(available.Width, totalWidth, available.Width),
            HeightConstraint.Compute(available.Height, totalHeight, available.Height));
    }

    private int MeasureColumnContent(int colIndex, int maxWidth)
    {
        var maxContent = 0;
        for (var r = 0; r < _rows; r++)
        {
            var cell = _cells[r, colIndex];
            var (colSpan, _) = _spans[r, colIndex];

            // Only measure cells that start here and don't span
            if (cell != null && colSpan == 1)
            {
                var cellSize = cell.Measure(new Size(maxWidth, int.MaxValue));
                maxContent = Math.Max(maxContent, cellSize.Width + CellPadding * 2);
            }
        }
        return maxContent;
    }

    private int MeasureRowContent(int rowIndex, int maxHeight, int[] columnWidths)
    {
        var maxContent = 0;
        for (var c = 0; c < _cols; c++)
        {
            var cell = _cells[rowIndex, c];
            var (colSpan, rowSpan) = _spans[rowIndex, c];

            // Only measure cells that start here and don't span vertically
            if (cell != null && rowSpan == 1)
            {
                // Calculate available width (sum of spanned columns)
                var cellWidth = 0;
                for (var i = 0; i < colSpan && c + i < _cols; i++)
                    cellWidth += columnWidths[c + i];

                var cellSize = cell.Measure(new Size(cellWidth - CellPadding * 2, maxHeight));
                maxContent = Math.Max(maxContent, cellSize.Height + CellPadding * 2);
            }
        }
        return maxContent;
    }

    private int[] SolveConstraints(SizeConstraint[] constraints, int available, Func<int, int, int> measureContent)
    {
        var sizes = new int[constraints.Length];
        var remaining = available;
        var fillCount = 0;
        var totalWeight = 0;

        // First pass: fixed, percent, and auto
        for (var i = 0; i < constraints.Length; i++)
        {
            var constraint = constraints[i];
            switch (constraint)
            {
                case SizeConstraint.Fixed fix:
                    sizes[i] = Math.Min(fix.Value, remaining);
                    remaining -= sizes[i];
                    break;

                case SizeConstraint.Percent pct:
                    sizes[i] = available * pct.Value / 100;
                    remaining -= sizes[i];
                    break;

                case SizeConstraint.Auto auto:
                    var content = measureContent(i, remaining);
                    sizes[i] = auto.Compute(remaining, content, remaining);
                    remaining -= sizes[i];
                    break;

                case SizeConstraint.Fill fill:
                    fillCount++;
                    totalWeight += fill.Weight;
                    break;
            }
        }

        // Second pass: distribute remaining to fills
        if (fillCount > 0 && remaining > 0)
        {
            for (var i = 0; i < constraints.Length; i++)
            {
                if (constraints[i] is SizeConstraint.Fill fill)
                {
                    sizes[i] = remaining * fill.Weight / totalWeight;
                }
            }
        }

        return sizes;
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        if (_rows == 0 || _cols == 0 || !bounds.HasArea)
            return;

        var hasGridLines = GridLines != BorderStyle.None;
        var gridLineWidth = hasGridLines ? 1 : 0;

        // Calculate content area
        var totalGridLinesH = gridLineWidth * (_cols + 1);
        var totalGridLinesV = gridLineWidth * (_rows + 1);
        var contentWidth = Math.Max(0, bounds.Width - totalGridLinesH);
        var contentHeight = Math.Max(0, bounds.Height - totalGridLinesV);

        // Solve sizes
        var columnWidths = SolveConstraints(_columnConstraints, contentWidth, MeasureColumnContent);
        var rowHeights = SolveConstraints(_rowConstraints, contentHeight, (index, maxSize) => MeasureRowContent(index, maxSize, columnWidths));

        // Create sub-context for grid
        var gridContext = context.CreateSubContext(bounds);

        // Render grid lines if enabled
        if (hasGridLines)
        {
            RenderGridLines(gridContext, bounds.Width, bounds.Height, columnWidths, rowHeights);
        }

        // Render cells
        var y = gridLineWidth;
        for (var r = 0; r < _rows; r++)
        {
            var x = gridLineWidth;
            for (var c = 0; c < _cols; c++)
            {
                var cell = _cells[r, c];
                var (colSpan, rowSpan) = _spans[r, c];

                // Skip cells that are part of a span (marked with 0,0)
                if (colSpan == 0 && rowSpan == 0)
                {
                    x += columnWidths[c] + gridLineWidth;
                    continue;
                }

                // Calculate cell bounds (including spanned area)
                var cellWidth = 0;
                var cellHeight = 0;
                for (var i = 0; i < colSpan && c + i < _cols; i++)
                {
                    cellWidth += columnWidths[c + i];
                    if (i > 0) cellWidth += gridLineWidth; // Include grid line space in span
                }
                for (var i = 0; i < rowSpan && r + i < _rows; i++)
                {
                    cellHeight += rowHeights[r + i];
                    if (i > 0) cellHeight += gridLineWidth;
                }

                if (cell != null && cellWidth > 0 && cellHeight > 0)
                {
                    // Apply padding
                    var innerX = x + CellPadding;
                    var innerY = y + CellPadding;
                    var innerWidth = Math.Max(0, cellWidth - CellPadding * 2);
                    var innerHeight = Math.Max(0, cellHeight - CellPadding * 2);

                    // Highlight focused cell
                    var isFocused = _hasFocus && ShowFocusHighlight && r == _focusedRow && c == _focusedCol;
                    if (isFocused && NavigationMode != GridNavigationMode.None)
                    {
                        gridContext.SetBackground(FocusedCellBackground);
                        gridContext.SetForeground(FocusedCellForeground);

                        // Fill the cell background
                        for (var fillY = y; fillY < y + cellHeight && fillY < bounds.Height; fillY++)
                        {
                            gridContext.WriteAt(x, fillY, new string(' ', Math.Min(cellWidth, bounds.Width - x)));
                        }
                    }

                    if (innerWidth > 0 && innerHeight > 0)
                    {
                        var cellBounds = new Rect(innerX, innerY, innerWidth, innerHeight);
                        var cellContext = gridContext.CreateSubContext(cellBounds);
                        cell.Render(cellContext, new Rect(0, 0, innerWidth, innerHeight));
                    }

                    if (isFocused)
                    {
                        gridContext.ResetColors();
                    }
                }

                x += columnWidths[c] + gridLineWidth;
            }
            y += rowHeights[r] + gridLineWidth;
        }
    }

    private void RenderGridLines(IRenderContext context, int width, int height, int[] columnWidths, int[] rowHeights)
    {
        var chars = GetGridChars(GridLines);

        if (GridLineColor.HasValue)
            context.SetForeground(GridLineColor.Value);

        // Calculate positions
        var colPositions = new int[_cols + 1];
        colPositions[0] = 0;
        for (var c = 0; c < _cols; c++)
        {
            colPositions[c + 1] = colPositions[c] + 1 + columnWidths[c];
        }

        var rowPositions = new int[_rows + 1];
        rowPositions[0] = 0;
        for (var r = 0; r < _rows; r++)
        {
            rowPositions[r + 1] = rowPositions[r] + 1 + rowHeights[r];
        }

        // Draw horizontal lines
        for (var r = 0; r <= _rows; r++)
        {
            var y = rowPositions[r];
            if (y >= height) break;

            for (var c = 0; c <= _cols; c++)
            {
                var x = colPositions[c];
                if (x >= width) break;

                // Draw intersection
                char intersection;
                if (r == 0 && c == 0) intersection = chars.TopLeft;
                else if (r == 0 && c == _cols) intersection = chars.TopRight;
                else if (r == _rows && c == 0) intersection = chars.BottomLeft;
                else if (r == _rows && c == _cols) intersection = chars.BottomRight;
                else if (r == 0) intersection = chars.TopTee;
                else if (r == _rows) intersection = chars.BottomTee;
                else if (c == 0) intersection = chars.LeftTee;
                else if (c == _cols) intersection = chars.RightTee;
                else intersection = chars.Cross;

                context.WriteAt(x, y, intersection.ToString());

                // Draw horizontal segment after intersection (except for last column)
                if (c < _cols)
                {
                    var segmentWidth = columnWidths[c];
                    if (segmentWidth > 0 && x + 1 < width)
                    {
                        var segment = new string(chars.Horizontal, Math.Min(segmentWidth, width - x - 1));
                        context.WriteAt(x + 1, y, segment);
                    }
                }
            }
        }

        // Draw vertical lines (between horizontal lines)
        for (var r = 0; r < _rows; r++)
        {
            var startY = rowPositions[r] + 1;
            var segmentHeight = rowHeights[r];

            for (var c = 0; c <= _cols; c++)
            {
                var x = colPositions[c];
                if (x >= width) break;

                for (var dy = 0; dy < segmentHeight && startY + dy < height; dy++)
                {
                    context.WriteAt(x, startY + dy, chars.Vertical.ToString());
                }
            }
        }

        if (GridLineColor.HasValue)
            context.ResetColors();
    }

    private static GridChars GetGridChars(BorderStyle style) => style switch
    {
        BorderStyle.Single => new GridChars('┌', '┐', '└', '┘', '─', '│', '┬', '┴', '├', '┤', '┼'),
        BorderStyle.Double => new GridChars('╔', '╗', '╚', '╝', '═', '║', '╦', '╩', '╠', '╣', '╬'),
        BorderStyle.Rounded => new GridChars('╭', '╮', '╰', '╯', '─', '│', '┬', '┴', '├', '┤', '┼'),
        BorderStyle.Ascii => new GridChars('+', '+', '+', '+', '-', '|', '+', '+', '+', '+', '+'),
        _ => new GridChars(' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ')
    };

    private readonly record struct GridChars(
        char TopLeft, char TopRight, char BottomLeft, char BottomRight,
        char Horizontal, char Vertical,
        char TopTee, char BottomTee, char LeftTee, char RightTee, char Cross);

    #endregion

    #region IFocusable

    /// <inheritdoc />
    public bool CanFocus => NavigationMode != GridNavigationMode.None;

    /// <inheritdoc />
    public bool HasFocus => _hasFocus;

    /// <inheritdoc />
    public int FocusPriority => 10;

    /// <inheritdoc />
    public void OnFocused()
    {
        _hasFocus = true;
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public void OnBlurred()
    {
        _hasFocus = false;
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public bool HandleInput(ConsoleKeyInfo key)
    {
        if (NavigationMode == GridNavigationMode.None)
            return false;

        var oldRow = _focusedRow;
        var oldCol = _focusedCol;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                MoveFocus(-1, 0);
                break;

            case ConsoleKey.DownArrow:
                MoveFocus(1, 0);
                break;

            case ConsoleKey.LeftArrow:
                MoveFocus(0, -1);
                break;

            case ConsoleKey.RightArrow:
                MoveFocus(0, 1);
                break;

            case ConsoleKey.Home:
                _focusedRow = 0;
                _focusedCol = 0;
                break;

            case ConsoleKey.End:
                _focusedRow = Math.Max(0, _rows - 1);
                _focusedCol = Math.Max(0, _cols - 1);
                break;

            case ConsoleKey.Enter:
                var cell = _cells[_focusedRow, _focusedCol];
                _cellActivated.OnNext((_focusedRow, _focusedCol, cell));
                return true;

            case ConsoleKey.Tab:
                // In ChildFocusRouting mode, Tab might route to child
                if (NavigationMode == GridNavigationMode.ChildFocusRouting)
                {
                    var currentCell = _cells[_focusedRow, _focusedCol];
                    if (currentCell is IFocusable focusable && focusable.CanFocus)
                    {
                        // Let the cell handle it
                        return focusable.HandleInput(key);
                    }
                }
                return false; // Let parent handle Tab for focus cycling

            default:
                // Try to pass to focused cell in ChildFocusRouting mode
                if (NavigationMode == GridNavigationMode.ChildFocusRouting)
                {
                    var currentCell = _cells[_focusedRow, _focusedCol];
                    if (currentCell is IFocusable focusable && focusable.HasFocus)
                    {
                        return focusable.HandleInput(key);
                    }
                }
                return false;
        }

        if (oldRow != _focusedRow || oldCol != _focusedCol)
        {
            _focusedCellChanged.OnNext((_focusedRow, _focusedCol));
            _invalidated.OnNext(Unit.Default);
        }

        return true;
    }

    private void MoveFocus(int rowDelta, int colDelta)
    {
        var newRow = _focusedRow + rowDelta;
        var newCol = _focusedCol + colDelta;

        // Clamp to valid range
        newRow = Math.Clamp(newRow, 0, Math.Max(0, _rows - 1));
        newCol = Math.Clamp(newCol, 0, Math.Max(0, _cols - 1));

        // Skip cells that are part of a span (find the span owner)
        while (newRow >= 0 && newRow < _rows && newCol >= 0 && newCol < _cols)
        {
            var (colSpan, rowSpan) = _spans[newRow, newCol];
            if (colSpan == 0 && rowSpan == 0)
            {
                // This cell is part of a span, keep moving
                newRow += rowDelta;
                newCol += colDelta;

                // If we can't move further, stop
                if (newRow < 0 || newRow >= _rows || newCol < 0 || newCol >= _cols)
                {
                    newRow = _focusedRow;
                    newCol = _focusedCol;
                    break;
                }
            }
            else
            {
                break;
            }
        }

        _focusedRow = Math.Clamp(newRow, 0, Math.Max(0, _rows - 1));
        _focusedCol = Math.Clamp(newCol, 0, Math.Max(0, _cols - 1));
    }

    #endregion

    #region Lifecycle

    /// <inheritdoc />
    public override void OnActivate()
    {
        for (var r = 0; r < _rows; r++)
        {
            for (var c = 0; c < _cols; c++)
            {
                if (_cells[r, c] is IActivatableNode node)
                    node.OnActivate();
            }
        }
        base.OnActivate();
    }

    /// <inheritdoc />
    public override void OnDeactivate()
    {
        for (var r = 0; r < _rows; r++)
        {
            for (var c = 0; c < _cols; c++)
            {
                if (_cells[r, c] is IActivatableNode node)
                    node.OnDeactivate();
            }
        }
        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var subscription in _cellSubscriptions.Values)
            subscription.Dispose();
        _cellSubscriptions.Clear();

        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _focusedCellChanged.OnCompleted();
        _focusedCellChanged.Dispose();
        _cellActivated.OnCompleted();
        _cellActivated.Dispose();

        for (var r = 0; r < _rows; r++)
        {
            for (var c = 0; c < _cols; c++)
            {
                _cells[r, c]?.Dispose();
            }
        }

        base.Dispose();
    }

    #endregion
}
