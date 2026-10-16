// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// An interactive list selection component with keyboard navigation.
/// </summary>
/// <typeparam name="T">The type of items in the list.</typeparam>
/// <remarks>
/// <para>
/// SelectionListNode provides keyboard-driven item selection with:
/// - Arrow key navigation (Up/Down)
/// - Home/End to jump to first/last item
/// - Space to toggle selection (in Multi mode)
/// - Enter to confirm selection
/// - Number keys (1-9) for quick selection
/// - Escape to cancel
/// </para>
/// <para>
/// Supports both simple string display and rich content with multiple lines
/// and styled/animated segments.
/// </para>
/// <para>
/// Optionally supports an "Other" option for custom text input.
/// </para>
/// </remarks>
public sealed class SelectionListNode<T> : IFocusable, IInvalidatingNode, IActivatableNode, ILayoutRuntimeContextAware
{
    private readonly Subject<Unit> _invalidated = new();
    private readonly Subject<IReadOnlyList<T>> _selectionConfirmed = new();
    private readonly Subject<string> _otherSelected = new();
    private readonly Subject<Unit> _cancelled = new();
    private readonly List<SelectionItem<T>> _items = new();

    private int _highlightedIndex;
    private int _scrollOffset; // Now in lines, not items
    private int _visibleRows = 10;
    private bool _isEditingOther;
    private TextInputNode? _otherInput;
    private LayoutRuntimeContext? _runtimeContext;
    private string? _otherLabel;
    private Action<string>? _otherCallback;
    private bool _hasFocus;
    private bool _disposed;

    private SelectionMode _mode = SelectionMode.Single;
    private Color _highlightForeground = Color.Black;
    private Color _highlightBackground = Color.White;
    private Color? _foreground;
    private Color? _selectedForeground;
    private bool _showNumbers = true;
    private bool _fillHeight;

    /// <summary>
    /// Creates a new SelectionListNode with the specified items and plain text display.
    /// </summary>
    /// <param name="items">The items to display.</param>
    /// <param name="displaySelector">A function to convert items to display text.</param>
    public SelectionListNode(IEnumerable<T> items, Func<T, string> displaySelector)
    {
        ArgumentNullException.ThrowIfNull(displaySelector);

        foreach (var item in items)
        {
            var selectionItem = new SelectionItem<T>(item, displaySelector(item));
            _items.Add(selectionItem);
            SubscribeToItemAnimations(selectionItem);
        }

        if (_items.Count > 0)
            _highlightedIndex = 0;
    }

    /// <summary>
    /// Creates a new SelectionListNode with rich content display supporting multiple lines
    /// and styled/animated segments.
    /// </summary>
    /// <param name="items">The items to display.</param>
    /// <param name="contentSelector">A function to convert items to rich content.</param>
    public SelectionListNode(IEnumerable<T> items, Func<T, SelectionItemContent> contentSelector)
    {
        ArgumentNullException.ThrowIfNull(contentSelector);

        foreach (var item in items)
        {
            var selectionItem = new SelectionItem<T>(item, contentSelector(item));
            _items.Add(selectionItem);
            SubscribeToItemAnimations(selectionItem);
        }

        if (_items.Count > 0)
            _highlightedIndex = 0;
    }

    private void SubscribeToItemAnimations(SelectionItem<T> item)
    {
        item.SubscribeToAnimations(_ => Invalidate());
    }

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated.AsObservable();

    /// <summary>
    /// Observable that emits the selected items when Enter is pressed.
    /// </summary>
    public Observable<IReadOnlyList<T>> SelectionConfirmed => _selectionConfirmed.AsObservable();

    /// <summary>
    /// Observable that emits the custom text when "Other" is selected and confirmed.
    /// </summary>
    public Observable<string> OtherSelected => _otherSelected.AsObservable();

    /// <summary>
    /// Observable that emits when Escape is pressed.
    /// </summary>
    public Observable<Unit> Cancelled => _cancelled.AsObservable();

    /// <inheritdoc />
    public void SetRuntimeContext(LayoutRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _runtimeContext = context;
        if (_otherInput is not null)
            LayoutRuntimeContextInjector.Apply(_otherInput, context);
    }

    /// <inheritdoc />
    public SizeConstraint WidthConstraint => SizeConstraint.FillRemaining();

    /// <inheritdoc />
    public SizeConstraint HeightConstraint => _fillHeight ? SizeConstraint.FillRemaining() : SizeConstraint.AutoSize();

    /// <inheritdoc />
    public bool CanFocus => true;

    /// <inheritdoc />
    public bool HasFocus => _hasFocus;

    /// <summary>
    /// Selection list has medium priority (lower than modal).
    /// </summary>
    public int FocusPriority => 10;

    /// <summary>
    /// Gets the currently highlighted item.
    /// </summary>
    public SelectionItem<T>? HighlightedItem =>
        _highlightedIndex >= 0 && _highlightedIndex < _items.Count ? _items[_highlightedIndex] : null;

    /// <summary>
    /// Gets all selected items.
    /// </summary>
    public IReadOnlyList<T> SelectedItems =>
        _items.Where(i => i.IsSelected && !i.IsOther).Select(i => i.Value).ToList();

    /// <summary>
    /// Gets the items in the list.
    /// </summary>
    public IReadOnlyList<SelectionItem<T>> Items => _items;

    /// <summary>
    /// Gets the total number of lines across all items.
    /// </summary>
    private int TotalLineCount => _items.Sum(i => i.LineCount);

    /// <summary>
    /// Sets the selection mode (Single or Multi).
    /// </summary>
    public SelectionListNode<T> WithMode(SelectionMode mode)
    {
        _mode = mode;
        return this;
    }

    /// <summary>
    /// Sets the highlight colors for the selected row.
    /// </summary>
    public SelectionListNode<T> WithHighlightColors(Color foreground, Color background)
    {
        _highlightForeground = foreground;
        _highlightBackground = background;
        return this;
    }

    /// <summary>
    /// Sets the initially highlighted item by index. Useful for pre-selecting an item
    /// when re-entering a previously configured view (e.g. a wizard edit flow).
    /// </summary>
    /// <param name="index">
    /// The item index to highlight. Clamped to the valid range; ignored when the list
    /// is empty.
    /// </param>
    /// <remarks>
    /// The highlighted item is scrolled into view using the current visible-row count,
    /// so call this after <see cref="WithVisibleRows"/> when both are used. In
    /// <see cref="WithFillHeight"/> mode the row count is not known until layout, so the
    /// scroll offset is computed against the default; the highlight still becomes visible
    /// once the user navigates, which re-runs the scroll math against the real height.
    /// </remarks>
    public SelectionListNode<T> WithHighlightedIndex(int index)
    {
        if (_items.Count > 0)
        {
            _highlightedIndex = Math.Clamp(index, 0, _items.Count - 1);
            EnsureVisible();
        }

        return this;
    }

    /// <summary>
    /// Sets the default foreground color.
    /// </summary>
    public SelectionListNode<T> WithForeground(Color color)
    {
        _foreground = color;
        return this;
    }

    /// <summary>
    /// Sets the color for selected (checked) items.
    /// </summary>
    public SelectionListNode<T> WithSelectedForeground(Color color)
    {
        _selectedForeground = color;
        return this;
    }

    /// <summary>
    /// Sets whether to show number prefixes (1-9) for quick selection.
    /// </summary>
    public SelectionListNode<T> WithShowNumbers(bool show = true)
    {
        _showNumbers = show;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of visible rows before scrolling.
    /// </summary>
    public SelectionListNode<T> WithVisibleRows(int rows)
    {
        _visibleRows = Math.Max(1, rows);
        _fillHeight = false;
        return this;
    }

    /// <summary>
    /// Enables fill-height mode: the list expands to fill the available vertical space,
    /// using available.Height / bounds.Height as the visible row count.
    /// This overrides any previously set WithVisibleRows value.
    /// </summary>
    /// <param name="fill">
    /// When <c>true</c> (the default), the list fills its parent's available height.
    /// When <c>false</c>, fill mode is disabled and the current <see cref="WithVisibleRows"/>
    /// count is used.
    /// </param>
    public SelectionListNode<T> WithFillHeight(bool fill = true)
    {
        _fillHeight = fill;
        return this;
    }

    /// <summary>
    /// Adds an "Other..." option that allows custom text input.
    /// </summary>
    /// <param name="label">The label for the "Other" option (default: "Other...").</param>
    /// <param name="onOther">Callback when custom text is submitted.</param>
    public SelectionListNode<T> WithOtherOption(string label = "Other...", Action<string>? onOther = null)
    {
        _otherLabel = label;
        _otherCallback = onOther;

        // Add Other as a special item at the end
        // We use default(T)! as the value since it won't be used
        var otherItem = new SelectionItem<T>(default!, label, isOther: true);
        _items.Add(otherItem);

        return this;
    }

    /// <inheritdoc />
    public void OnFocused()
    {
        _hasFocus = true;
        Invalidate();
    }

    /// <inheritdoc />
    public void OnBlurred()
    {
        _hasFocus = false;
        _isEditingOther = false;
        Invalidate();
    }

    /// <inheritdoc />
    public bool HandleInput(ConsoleKeyInfo key)
    {
        // If editing "Other" text, forward to text input
        if (_isEditingOther && _otherInput != null)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                var text = _otherInput.Text;
                _isEditingOther = false;
                _otherCallback?.Invoke(text);
                _otherSelected.OnNext(text);
                Invalidate();
                return true;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                _isEditingOther = false;
                Invalidate();
                return true;
            }

            return _otherInput.HandleInput(key);
        }

        // Handle navigation and selection
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                MoveHighlight(-1);
                return true;

            case ConsoleKey.DownArrow:
                MoveHighlight(1);
                return true;

            case ConsoleKey.Home:
                _highlightedIndex = 0;
                EnsureVisible();
                Invalidate();
                return true;

            case ConsoleKey.End:
                _highlightedIndex = _items.Count - 1;
                EnsureVisible();
                Invalidate();
                return true;

            case ConsoleKey.Spacebar:
                ToggleSelection();
                return true;

            case ConsoleKey.Enter:
                ConfirmSelection();
                return true;

            case ConsoleKey.Escape:
                _cancelled.OnNext(Unit.Default);
                return true;

            // Number keys for quick selection (1-9)
            case >= ConsoleKey.D1 and <= ConsoleKey.D9 when _showNumbers:
                var index = key.Key - ConsoleKey.D1;
                if (index < _items.Count)
                {
                    _highlightedIndex = index;
                    var selectedItem = _items[index];

                    // If selecting "Other", start text input immediately
                    if (selectedItem.IsOther)
                    {
                        StartOtherInput();
                    }
                    else if (_mode == SelectionMode.Single)
                    {
                        // In single mode, number key confirms immediately
                        SelectItem(index);
                        ConfirmSelection();
                    }
                    else
                    {
                        // In multi mode, number key toggles selection
                        ToggleSelection();
                    }
                }
                return true;

            default:
                return false;
        }
    }

    private void MoveHighlight(int delta)
    {
        if (_items.Count == 0)
            return;

        _highlightedIndex = Math.Clamp(_highlightedIndex + delta, 0, _items.Count - 1);
        EnsureVisible();
        Invalidate();
    }

    /// <summary>
    /// Gets the starting line offset for a given item index.
    /// </summary>
    private int GetItemLineOffset(int itemIndex)
    {
        var offset = 0;
        for (var i = 0; i < itemIndex && i < _items.Count; i++)
        {
            offset += _items[i].LineCount;
        }
        return offset;
    }

    private void EnsureVisible()
    {
        var itemLineStart = GetItemLineOffset(_highlightedIndex);
        var itemLineEnd = itemLineStart + (_highlightedIndex < _items.Count ? _items[_highlightedIndex].LineCount : 1);

        if (itemLineStart < _scrollOffset)
        {
            _scrollOffset = itemLineStart;
        }
        else if (itemLineEnd > _scrollOffset + _visibleRows)
        {
            _scrollOffset = itemLineEnd - _visibleRows;
        }
    }

    private void ToggleSelection()
    {
        if (_highlightedIndex < 0 || _highlightedIndex >= _items.Count)
            return;

        var item = _items[_highlightedIndex];

        if (item.IsOther)
        {
            // Start editing "Other" text
            StartOtherInput();
            return;
        }

        if (_mode == SelectionMode.Single)
        {
            // Clear other selections
            foreach (var i in _items)
                i.IsSelected = false;
        }

        item.IsSelected = !item.IsSelected;
        Invalidate();
    }

    private void SelectItem(int index)
    {
        if (index < 0 || index >= _items.Count)
            return;

        if (_mode == SelectionMode.Single)
        {
            foreach (var i in _items)
                i.IsSelected = false;
        }

        _items[index].IsSelected = true;
        Invalidate();
    }

    private void StartOtherInput()
    {
        _otherInput ??= new TextInputNode()
            .WithPlaceholder("Enter custom value...");
        if (_runtimeContext is not null)
            LayoutRuntimeContextInjector.Apply(_otherInput, _runtimeContext);

        _otherInput.Clear();
        _isEditingOther = true;
        Invalidate();
    }

    private void ConfirmSelection()
    {
        if (_highlightedIndex >= 0 && _highlightedIndex < _items.Count)
        {
            var item = _items[_highlightedIndex];

            if (item.IsOther)
            {
                StartOtherInput();
                return;
            }

            // In single mode, always select the highlighted item on Enter
            if (_mode == SelectionMode.Single)
            {
                // Clear other selections first
                foreach (var i in _items)
                    i.IsSelected = false;
                item.IsSelected = true;
            }
        }

        var selected = _items
            .Where(i => i.IsSelected && !i.IsOther)
            .Select(i => i.Value)
            .ToList();

        _selectionConfirmed.OnNext(selected);
    }

    /// <inheritdoc />
    public Size Measure(Size available)
    {
        var totalLines = TotalLineCount;

        // In fill mode, the visible-row count is dictated by the space the parent
        // allocates. Render() re-syncs this from bounds.Height (the authoritative
        // value) so EnsureVisible()'s scroll math stays correct between renders.
        if (_fillHeight)
            _visibleRows = Math.Max(1, available.Height);

        var height = Math.Min(totalLines, _visibleRows);

        // Calculate width based on content
        var maxItemWidth = _items.Count > 0
            ? _items.Max(i => GetItemDisplayWidth(i, _items.IndexOf(i)))
            : 10;

        var width = Math.Min(maxItemWidth + 2, available.Width);

        return new Size(width, height);
    }

    private int GetItemDisplayWidth(SelectionItem<T> item, int index)
    {
        var prefix = GetItemPrefix(item, index);

        // Find the maximum width across all lines
        var maxLineWidth = 0;
        foreach (var line in item.Content.Lines)
        {
            var lineWidth = line.Sum(s => DisplayWidth.GetColumnCount(s.GetCurrentSegment().Text));
            maxLineWidth = Math.Max(maxLineWidth, lineWidth);
        }

        return DisplayWidth.GetColumnCount(prefix) + maxLineWidth;
    }

    private string GetItemPrefix(SelectionItem<T> item, int index)
    {
        var parts = new List<string>();

        // Number prefix (display numbers for all items; keyboard shortcuts still limited to 1-9)
        if (_showNumbers)
        {
            parts.Add($"{index + 1}.");
        }

        // Checkbox for multi-select
        if (_mode == SelectionMode.Multi && !item.IsOther)
        {
            parts.Add(item.IsSelected ? "[x]" : "[ ]");
        }
        else if (_mode == SelectionMode.Single && item.IsSelected && !item.IsOther)
        {
            parts.Add("●");
        }

        return parts.Count > 0 ? string.Join(" ", parts) + " " : "";
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, Rect bounds)
    {
        if (!bounds.HasArea || _items.Count == 0)
            return;

        // Create a sub-context for this node's bounds so all coordinates are relative
        var subContext = context.CreateSubContext(bounds);

        // In fill mode, bounds.Height is the authoritative visible-row count. Sync it
        // here so EnsureVisible()'s scroll math is correct even if Render() runs without
        // a preceding Measure() (e.g. as a Fill child, which VerticalLayout.Render does
        // not re-measure).
        if (_fillHeight)
            _visibleRows = Math.Max(1, bounds.Height);

        var visibleLines = Math.Min(_visibleRows, bounds.Height);
        var needsScrollbar = TotalLineCount > visibleLines;
        var contentWidth = needsScrollbar ? bounds.Width - 1 : bounds.Width;

        var currentLine = 0;
        var currentItemLineOffset = 0;

        for (var itemIndex = 0; itemIndex < _items.Count && currentLine < visibleLines; itemIndex++)
        {
            var item = _items[itemIndex];
            var itemLineCount = item.LineCount;
            var itemEndOffset = currentItemLineOffset + itemLineCount;

            // Skip items entirely before the scroll offset
            if (itemEndOffset <= _scrollOffset)
            {
                currentItemLineOffset = itemEndOffset;
                continue;
            }

            // Calculate which lines of this item to render
            var itemLineStart = Math.Max(0, _scrollOffset - currentItemLineOffset);
            var renderRow = currentLine;

            // If this is the "Other" item and we're editing, render the text input instead
            if (item.IsOther && _isEditingOther && _otherInput != null)
            {
                RenderOtherInput(subContext, renderRow, contentWidth, itemIndex);
                currentLine++;
            }
            else
            {
                var isHighlighted = itemIndex == _highlightedIndex && _hasFocus && !_isEditingOther;

                // Render each visible line of this item
                for (var lineIndex = itemLineStart; lineIndex < itemLineCount && currentLine < visibleLines; lineIndex++)
                {
                    RenderItemLine(subContext, item, itemIndex, lineIndex, currentLine, contentWidth, isHighlighted);
                    currentLine++;
                }
            }

            currentItemLineOffset = itemEndOffset;
        }

        // Render scrollbar if needed
        if (needsScrollbar)
        {
            RenderScrollbar(subContext, bounds.Width - 1, visibleLines);
        }
    }

    private void RenderOtherInput(IRenderContext context, int row, int width, int itemIndex)
    {
        if (_otherInput == null)
            return;

        // Render the number prefix (e.g., "4. " or "10. ") in dimmed color
        var prefix = _showNumbers ? $"{itemIndex + 1}. " : "";
        if (prefix.Length > 0)
        {
            context.SetForeground(Color.BrightBlack);
            context.WriteAt(0, row, prefix);
            context.ResetColors();
        }

        // Render the text input after the prefix
        var inputX = DisplayWidth.GetColumnCount(prefix);
        var inputWidth = Math.Max(1, width - inputX);
        var inputBounds = new Rect(inputX, row, inputWidth, 1);
        var inputContext = context.CreateSubContext(inputBounds);
        var innerBounds = new Rect(0, 0, inputWidth, 1);
        _otherInput.Render(inputContext, innerBounds);
    }

    private void RenderItemLine(IRenderContext context, SelectionItem<T> item, int itemIndex,
        int lineIndex, int row, int width, bool isHighlighted)
    {
        // Get the line content
        var lines = item.Content.Lines;
        if (lineIndex >= lines.Count)
            return;

        var lineSegments = lines[lineIndex];

        // Calculate prefix - only show on first line
        var itemPrefix = GetItemPrefix(item, itemIndex);
        var prefix = lineIndex == 0 ? itemPrefix : new string(' ', DisplayWidth.GetColumnCount(itemPrefix));

        // Set background for highlighted items
        if (isHighlighted)
        {
            context.SetBackground(_highlightBackground);
            // Fill the entire row with background color
            context.Fill(0, row, width, 1, ' ');
        }

        // Render prefix
        if (prefix.Length > 0)
        {
            if (isHighlighted)
            {
                context.SetForeground(_highlightForeground);
            }
            else if (lineIndex > 0)
            {
                // Indent continuation lines with dimmed color
                context.SetForeground(Color.BrightBlack);
            }
            else if (item.IsSelected && _selectedForeground.HasValue)
            {
                context.SetForeground(_selectedForeground.Value);
            }
            else if (_foreground.HasValue)
            {
                context.SetForeground(_foreground.Value);
            }

            context.WriteAt(0, row, prefix);
        }

        // Render segments
        var xPos = DisplayWidth.GetColumnCount(prefix);
        foreach (var segment in lineSegments)
        {
            if (xPos >= width)
                break;

            var styledSegment = segment.GetCurrentSegment();
            var text = styledSegment.Text;
            var style = styledSegment.Style;

            // Truncate if needed
            var remainingWidth = width - xPos;
            if (DisplayWidth.GetColumnCount(text) > remainingWidth)
            {
                text = remainingWidth > 1
                    ? DisplayWidth.TruncateToColumns(text, remainingWidth - 1) + "…"
                    : "…";
            }

            // Apply colors - highlight overrides segment colors for foreground
            if (isHighlighted)
            {
                context.SetForeground(_highlightForeground);
                context.SetBackground(_highlightBackground);
            }
            else
            {
                // Apply segment style, falling back to item/list defaults
                var fg = style.HasForeground ? style.Foreground :
                    (item.IsSelected && _selectedForeground.HasValue ? _selectedForeground.Value :
                    (_foreground ?? Color.Default));
                var bg = style.HasBackground ? style.Background : Color.Default;

                context.SetForeground(fg);
                if (style.HasBackground)
                {
                    context.SetBackground(bg);
                }
            }

            // Apply decoration if present
            if (style.HasDecoration)
            {
                context.SetDecoration(style.Decoration);
            }

            context.WriteAt(xPos, row, text);
            xPos += DisplayWidth.GetColumnCount(text);

            // Reset decoration after each segment
            if (style.HasDecoration)
            {
                context.SetDecoration(TextDecoration.None);
            }
        }

        context.ResetColors();
    }

    private void RenderScrollbar(IRenderContext context, int x, int height)
    {
        var totalLines = TotalLineCount;
        if (totalLines <= height)
            return;

        var thumbSize = Math.Max(1, height * height / totalLines);
        var thumbPos = _scrollOffset * (height - thumbSize) / (totalLines - height);

        context.SetForeground(Color.BrightBlack);

        for (var row = 0; row < height; row++)
        {
            var isThumb = row >= thumbPos && row < thumbPos + thumbSize;
            context.WriteAt(x, row, isThumb ? '█' : '░');
        }

        context.ResetColors();
    }

    private void Invalidate()
    {
        if (!_disposed)
            _invalidated.OnNext(Unit.Default);
    }

    /// <summary>
    /// Called when the node becomes active (page navigated to).
    /// Activates the embedded TextInputNode if it exists.
    /// </summary>
    public void OnActivate()
    {
        // Activate the embedded "Other" input if it exists
        _otherInput?.OnActivate();
    }

    /// <summary>
    /// Called when the node becomes inactive (navigating away from page).
    /// Deactivates the embedded TextInputNode to conserve resources.
    /// </summary>
    public void OnDeactivate()
    {
        // Deactivate the embedded "Other" input if it exists
        _otherInput?.OnDeactivate();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _selectionConfirmed.OnCompleted();
        _selectionConfirmed.Dispose();
        _otherSelected.OnCompleted();
        _otherSelected.Dispose();
        _cancelled.OnCompleted();
        _cancelled.Dispose();

        _otherInput?.Dispose();

        // Dispose all items (which will dispose their content and animation subscriptions)
        foreach (var item in _items)
        {
            item.Dispose();
        }
    }
}
