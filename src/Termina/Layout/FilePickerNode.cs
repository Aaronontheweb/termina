// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.IO.Enumeration;
using R3;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// An interactive file/folder picker with breadcrumb navigation, scrolling, and fuzzy filtering.
/// </summary>
public sealed class FilePickerNode : IFocusable, IInvalidatingNode, IActivatableNode, ILayoutRuntimeContextAware
{
    private readonly Subject<Unit> _invalidated = new();
    private readonly Subject<IReadOnlyList<string>> _selectionConfirmed = new();
    private readonly Subject<Unit> _cancelled = new();
    private readonly Subject<string> _directoryChanged = new();

    private IFileSystemProvider _fileSystem = DefaultFileSystemProvider.Instance;
    private string _currentPath;
    private List<FileSystemEntry> _entries = new();
    private List<FileSystemEntry> _filteredEntries = new();
    private readonly HashSet<string> _selectedPaths = new();

    private int _highlightedIndex;
    private int _scrollOffset;
    private int _visibleRows = 10;
    private int _effectiveRows = 10;
    private bool _hasFocus;
    private bool _disposed;
    private bool _isLoaded;

    // Filter state
    private bool _isFiltering;
    private TextInputNode? _filterInput;
    private LayoutRuntimeContext? _runtimeContext;
    private IDisposable? _filterInvalidationSub;

    // Configuration
    private FilePickerMode _mode = FilePickerMode.Files;
    private FilePickerSelectionMode _selectionMode = FilePickerSelectionMode.Single;
    private bool _showHidden;
    private string? _fileFilter;
    private bool _fillHeight;
    private Color _highlightForeground = Color.Black;
    private Color _highlightBackground = Color.White;
    private Color _directoryColor = Color.BrightCyan;
    private Color _fileColor = Color.Default;

    // ASCII icons (avoids surrogate-pair/column-width issues with emoji)
    private const string DirIcon = "[D] ";
    private const string FileIcon = "    ";
    private const string FilterPrefix = "/ ";
    private const string BreadcrumbPrefix = ">> ";

    public FilePickerNode(string? startPath = null)
    {
        _currentPath = startPath ?? Environment.CurrentDirectory;
    }

    public Observable<Unit> Invalidated => _invalidated.AsObservable();

    /// <summary>
    /// Emits selected full paths when the user confirms a selection.
    /// </summary>
    public Observable<IReadOnlyList<string>> SelectionConfirmed => _selectionConfirmed.AsObservable();

    /// <summary>
    /// Emits when the user presses Escape while browsing (an active filter is cleared instead).
    /// </summary>
    public Observable<Unit> Cancelled => _cancelled.AsObservable();

    /// <summary>
    /// Emits the new directory path when the user navigates to a different directory.
    /// The initial lazy load does not emit.
    /// </summary>
    public Observable<string> DirectoryChanged => _directoryChanged.AsObservable();

    /// <inheritdoc />
    public void SetRuntimeContext(LayoutRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _runtimeContext = context;
        if (_filterInput is not null)
            LayoutRuntimeContextInjector.Apply(_filterInput, context);
    }

    public SizeConstraint WidthConstraint => SizeConstraint.FillRemaining();

    public SizeConstraint HeightConstraint => _fillHeight ? SizeConstraint.FillRemaining() : SizeConstraint.AutoSize();

    public bool CanFocus => true;

    public bool HasFocus => _hasFocus;

    public int FocusPriority => 10;

    /// <summary>
    /// Gets the current directory path being displayed.
    /// </summary>
    public string CurrentPath => _currentPath;

    #region Fluent Configuration

    public FilePickerNode WithStartPath(string path)
    {
        _currentPath = path;
        _isLoaded = false;
        return this;
    }

    public FilePickerNode WithMode(FilePickerMode mode)
    {
        _mode = mode;
        return this;
    }

    public FilePickerNode WithSelectionMode(FilePickerSelectionMode mode)
    {
        _selectionMode = mode;
        return this;
    }

    public FilePickerNode WithShowHidden(bool show = true)
    {
        _showHidden = show;
        return this;
    }

    public FilePickerNode WithFileFilter(string? globPattern)
    {
        _fileFilter = globPattern;
        return this;
    }

    public FilePickerNode WithHighlightColors(Color foreground, Color background)
    {
        _highlightForeground = foreground;
        _highlightBackground = background;
        return this;
    }

    public FilePickerNode WithDirectoryColor(Color color)
    {
        _directoryColor = color;
        return this;
    }

    public FilePickerNode WithFileColor(Color color)
    {
        _fileColor = color;
        return this;
    }

    public FilePickerNode WithVisibleRows(int rows)
    {
        _visibleRows = Math.Max(1, rows);
        _effectiveRows = _visibleRows;
        _fillHeight = false;
        return this;
    }

    public FilePickerNode WithFillHeight(bool fill = true)
    {
        _fillHeight = fill;
        return this;
    }

    public FilePickerNode WithFileSystemProvider(IFileSystemProvider provider)
    {
        _fileSystem = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    #endregion

    #region Focus Lifecycle

    public void OnFocused()
    {
        _hasFocus = true;
        EnsureLoaded();
        Invalidate();
    }

    public void OnBlurred()
    {
        _hasFocus = false;
        if (_isFiltering)
            StopFiltering();
        Invalidate();
    }

    public void OnActivate()
    {
        EnsureLoaded();
        _filterInput?.OnActivate();
    }

    public void OnDeactivate()
    {
        _filterInput?.OnDeactivate();
    }

    #endregion

    #region Input Handling

    public bool HandleInput(ConsoleKeyInfo key)
    {
        if (_isFiltering)
            return HandleFilterInput(key);

        return HandleBrowsingInput(key);
    }

    private bool HandleBrowsingInput(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.DownArrow:
            case ConsoleKey.Home:
            case ConsoleKey.End:
                return HandleNavigationKey(key);

            case ConsoleKey.Enter:
                ActivateHighlighted();
                return true;

            case ConsoleKey.Backspace:
                NavigateUp();
                return true;

            case ConsoleKey.Escape:
                OnEscapePressed();
                return true;

            case ConsoleKey.Spacebar:
                HandleSpacebar();
                return true;

            default:
                // '/' activates filter; any printable char also activates filter + types it.
                // Alt/Ctrl chords are left for page-level bindings.
                if ((key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Control)) != 0)
                    return false;

                if (key.KeyChar == '/')
                {
                    StartFiltering("");
                    return true;
                }

                if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0')
                {
                    StartFiltering(key.KeyChar.ToString());
                    return true;
                }

                return false;
        }
    }

    private bool HandleFilterInput(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                StopFiltering();
                return true;

            case ConsoleKey.UpArrow:
            case ConsoleKey.DownArrow:
            case ConsoleKey.Home:
            case ConsoleKey.End:
                return HandleNavigationKey(key);

            case ConsoleKey.Enter:
                ActivateHighlighted();
                return true;

            default:
                if (_filterInput == null)
                    return false;

                // Backspace on an already-empty filter exits filter mode
                if (key.Key == ConsoleKey.Backspace && string.IsNullOrEmpty(_filterInput.Text))
                {
                    StopFiltering();
                    return true;
                }

                var textBefore = _filterInput.Text;
                var handled = _filterInput.HandleInput(key);
                if (handled)
                {
                    if (string.IsNullOrEmpty(_filterInput.Text))
                        StopFiltering();
                    else if (_filterInput.Text != textBefore)
                        ApplyFilter();
                }

                return handled;
        }
    }

    private bool HandleNavigationKey(ConsoleKeyInfo key)
    {
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
                _highlightedIndex = Math.Max(0, _filteredEntries.Count - 1);
                EnsureVisible();
                Invalidate();
                return true;
            default:
                return false;
        }
    }

    private void HandleSpacebar()
    {
        if (_highlightedIndex < 0 || _highlightedIndex >= _filteredEntries.Count)
            return;

        var entry = _filteredEntries[_highlightedIndex];

        if (_selectionMode == FilePickerSelectionMode.Multi)
        {
            ToggleSelection();
        }
        else
        {
            // Single mode: Space selects the item if it's selectable per mode
            if (IsSelectable(entry))
                EmitSelectionConfirmed(new[] { entry.FullPath });
        }
    }

    private void OnEscapePressed()
    {
        if (!_disposed)
            _cancelled.OnNext(Unit.Default);
    }

    #endregion

    #region Navigation

    private void EnsureLoaded()
    {
        if (!_isLoaded)
            LoadDirectory(_currentPath, emitChanged: false);
    }

    private void LoadDirectory(string path, bool emitChanged = true)
    {
        if (!_fileSystem.DirectoryExists(path))
        {
            // Latch so we don't retry filesystem I/O on every render/focus.
            // WithStartPath resets the latch to allow a reload.
            _isLoaded = true;
            return;
        }

        _currentPath = path;
        var allEntries = _fileSystem.GetEntries(path);

        _entries = new List<FileSystemEntry>(allEntries.Count);
        foreach (var e in allEntries)
        {
            if (!_showHidden && e.Name.StartsWith('.'))
                continue;
            if (!e.IsDirectory && _fileFilter != null && !MatchesGlob(e.Name, _fileFilter))
                continue;
            if (_mode == FilePickerMode.Directories && !e.IsDirectory)
                continue;
            _entries.Add(e);
        }

        _filteredEntries = new List<FileSystemEntry>(_entries);
        _highlightedIndex = 0;
        _scrollOffset = 0;
        _isLoaded = true;

        if (_isFiltering)
        {
            _isFiltering = false;
            _filterInput?.OnBlurred();
            _filterInput?.OnDeactivate();
        }

        if (emitChanged)
            EmitDirectoryChanged(_currentPath);
        Invalidate();
    }

    private void NavigateUp()
    {
        var parent = _fileSystem.GetParentDirectory(_currentPath);
        if (parent != null)
            LoadDirectory(parent);
    }

    private void ActivateHighlighted()
    {
        if (_highlightedIndex < 0 || _highlightedIndex >= _filteredEntries.Count)
            return;

        var entry = _filteredEntries[_highlightedIndex];

        if (entry.IsDirectory)
        {
            // Enter on a toggled directory confirms the selection; an untoggled
            // directory always opens, so deep navigation stays possible.
            if (_selectionMode == FilePickerSelectionMode.Multi &&
                _selectedPaths.Contains(entry.FullPath))
            {
                EmitSelectionConfirmed(_selectedPaths.ToList());
            }
            else
            {
                LoadDirectory(entry.FullPath);
            }

            return;
        }

        // File selected
        if (_selectionMode == FilePickerSelectionMode.Single)
        {
            EmitSelectionConfirmed(new[] { entry.FullPath });
        }
        else
        {
            // Confirm the toggled set plus the highlighted file, without
            // mutating the toggle state.
            var selected = _selectedPaths.ToList();
            if (!_selectedPaths.Contains(entry.FullPath))
                selected.Add(entry.FullPath);
            EmitSelectionConfirmed(selected);
        }
    }

    private void ToggleSelection()
    {
        if (_highlightedIndex < 0 || _highlightedIndex >= _filteredEntries.Count)
            return;

        var entry = _filteredEntries[_highlightedIndex];

        if (!IsSelectable(entry))
            return;

        if (!_selectedPaths.Remove(entry.FullPath))
            _selectedPaths.Add(entry.FullPath);

        MoveHighlight(1);
    }

    private bool IsSelectable(FileSystemEntry entry)
    {
        return _mode switch
        {
            FilePickerMode.Files => !entry.IsDirectory,
            FilePickerMode.Directories => entry.IsDirectory,
            FilePickerMode.All => true,
            _ => false
        };
    }

    #endregion

    #region Filtering

    private void StartFiltering(string initialText)
    {
        _isFiltering = true;
        if (_filterInput == null)
        {
            _filterInput = new TextInputNode()
                .WithPlaceholder("Type to filter...");
            if (_runtimeContext is not null)
                LayoutRuntimeContextInjector.Apply(_filterInput, _runtimeContext);

            // Bridge the embedded input's invalidations (cursor blink) to ours
            _filterInvalidationSub = _filterInput.Invalidated.Subscribe(_ => Invalidate());
        }
        _filterInput.OnActivate();
        _filterInput.OnFocused();
        _filterInput.Clear();

        // Seed with initial text if provided
        if (!string.IsNullOrEmpty(initialText))
        {
            foreach (var c in initialText)
            {
                _filterInput.HandleInput(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false));
            }
        }

        ApplyFilter();
        Invalidate();
    }

    private void StopFiltering()
    {
        _isFiltering = false;
        _filterInput?.OnBlurred();
        _filterInput?.OnDeactivate();
        _filteredEntries = new List<FileSystemEntry>(_entries);
        _highlightedIndex = Math.Min(_highlightedIndex, Math.Max(0, _filteredEntries.Count - 1));
        _scrollOffset = 0;
        EnsureVisible();
        Invalidate();
    }

    private void ApplyFilter()
    {
        var filterText = _filterInput?.Text ?? "";
        if (string.IsNullOrEmpty(filterText))
        {
            _filteredEntries = new List<FileSystemEntry>(_entries);
        }
        else
        {
            _filteredEntries = _entries
                .Where(e => e.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        _highlightedIndex = 0;
        _scrollOffset = 0;
        Invalidate();
    }

    #endregion

    #region Scrolling

    private void MoveHighlight(int delta)
    {
        if (_filteredEntries.Count == 0)
            return;

        _highlightedIndex = Math.Clamp(_highlightedIndex + delta, 0, _filteredEntries.Count - 1);
        EnsureVisible();
        Invalidate();
    }

    private void EnsureVisible()
    {
        // _effectiveRows tracks the rows actually rendered, which may be fewer
        // than the configured _visibleRows when the parent allocates less space.
        if (_highlightedIndex < _scrollOffset)
        {
            _scrollOffset = _highlightedIndex;
        }
        else if (_highlightedIndex >= _scrollOffset + _effectiveRows)
        {
            _scrollOffset = _highlightedIndex - _effectiveRows + 1;
        }
    }

    #endregion

    #region Measure & Render

    public Size Measure(Size available)
    {
        // Layout: breadcrumb (1) + optional filter (1) + file list + hints (1)
        var headerLines = 1 + (_isFiltering ? 1 : 0);
        var footerLines = 1;

        if (_fillHeight)
        {
            _visibleRows = Math.Max(1, available.Height - headerLines - footerLines);
            _effectiveRows = _visibleRows;
            return new Size(available.Width, available.Height);
        }

        // Reserve at least one row: empty lists render an "Empty directory"/"No matches" message
        var listHeight = Math.Max(1, Math.Min(_filteredEntries.Count, _visibleRows));
        var totalHeight = headerLines + listHeight + footerLines;
        return new Size(available.Width, totalHeight);
    }

    public void Render(IRenderContext context, Rect bounds)
    {
        if (!bounds.HasArea)
            return;

        // Lazy-load on first render so unfocused pickers show content
        EnsureLoaded();

        var sub = context.CreateSubContext(bounds);
        var currentRow = 0;
        var width = bounds.Width;

        // Breadcrumb
        RenderBreadcrumb(sub, currentRow, width);
        currentRow++;

        // Filter bar (if active)
        if (_isFiltering)
        {
            RenderFilterBar(sub, currentRow, width);
            currentRow++;
        }

        // Recalculate visible rows in fill mode
        var footerLines = 1;
        var availableForList = Math.Max(0, bounds.Height - currentRow - footerLines);
        if (_fillHeight)
            _visibleRows = Math.Max(1, availableForList);

        var listHeight = Math.Min(_filteredEntries.Count, Math.Min(_visibleRows, availableForList));

        // Keep scroll math in sync with what is actually rendered
        _effectiveRows = Math.Max(1, Math.Min(_visibleRows, availableForList));
        if (listHeight > 0)
            _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, _filteredEntries.Count - listHeight));

        // File list
        RenderFileList(sub, currentRow, width, listHeight);
        currentRow += Math.Max(listHeight, 1);

        // Key hints footer
        if (currentRow < bounds.Height)
            RenderFooter(sub, currentRow, width);
    }

    private void RenderBreadcrumb(IRenderContext context, int row, int width)
    {
        if (width <= 0) return;

        context.SetForeground(Color.BrightYellow);
        context.SetDecoration(TextDecoration.Bold);

        var pathDisplay = _currentPath;
        var maxPathWidth = width - DisplayWidth.GetColumnCount(BreadcrumbPrefix);
        if (maxPathWidth > 3 && DisplayWidth.GetColumnCount(pathDisplay) > maxPathWidth)
            pathDisplay = "..." + DisplayWidth.TruncateStartToColumns(pathDisplay, maxPathWidth - 3);
        else if (maxPathWidth >= 0 && DisplayWidth.GetColumnCount(pathDisplay) > maxPathWidth)
            pathDisplay = maxPathWidth > 0 ? DisplayWidth.TruncateToColumns(pathDisplay, maxPathWidth) : "";

        context.WriteAt(0, row, BreadcrumbPrefix + pathDisplay);
        context.SetDecoration(TextDecoration.None);
        context.ResetColors();
    }

    private void RenderFilterBar(IRenderContext context, int row, int width)
    {
        context.SetForeground(Color.BrightMagenta);
        context.WriteAt(0, row, FilterPrefix);

        if (_filterInput != null)
        {
            var inputX = DisplayWidth.GetColumnCount(FilterPrefix);
            var inputBounds = new Rect(inputX, row, Math.Max(1, width - inputX), 1);
            var inputContext = context.CreateSubContext(inputBounds);
            _filterInput.Render(inputContext, new Rect(0, 0, inputBounds.Width, 1));
        }

        context.ResetColors();
    }

    private void RenderFileList(IRenderContext context, int startRow, int width, int listHeight)
    {
        if (_filteredEntries.Count == 0)
        {
            context.SetForeground(Color.BrightBlack);
            context.WriteAt(0, startRow, _isFiltering ? "No matches" : "Empty directory");
            context.ResetColors();
            return;
        }

        var needsScrollbar = _filteredEntries.Count > listHeight;
        var contentWidth = needsScrollbar ? width - 1 : width;

        for (var i = 0; i < listHeight && (_scrollOffset + i) < _filteredEntries.Count; i++)
        {
            var entryIndex = _scrollOffset + i;
            var entry = _filteredEntries[entryIndex];
            var isHighlighted = entryIndex == _highlightedIndex && _hasFocus;
            var isSelected = _selectedPaths.Contains(entry.FullPath);
            var row = startRow + i;

            RenderEntry(context, entry, row, contentWidth, isHighlighted, isSelected);
        }

        if (needsScrollbar)
            RenderScrollbar(context, width - 1, startRow, listHeight);
    }

    private void RenderEntry(IRenderContext context, FileSystemEntry entry, int row, int width,
        bool isHighlighted, bool isSelected)
    {
        // Build prefix: cursor + checkbox (multi) + icon
        var cursor = isHighlighted ? "> " : "  ";
        var checkbox = "";
        if (_selectionMode == FilePickerSelectionMode.Multi)
        {
            checkbox = IsSelectable(entry) ? (isSelected ? "[x] " : "[ ] ") : "    ";
        }

        var icon = entry.IsDirectory ? DirIcon : FileIcon;
        var name = entry.IsDirectory ? entry.Name + "/" : entry.Name;

        var prefix = cursor + checkbox + icon;
        var displayText = prefix + name;

        if (width <= 0) return;

        if (DisplayWidth.GetColumnCount(displayText) > width && width > 1)
            displayText = DisplayWidth.TruncateToColumns(displayText, width - 1) + "~";
        else if (DisplayWidth.GetColumnCount(displayText) > width)
            displayText = DisplayWidth.TruncateToColumns(displayText, width);

        // Apply highlight or normal colors
        if (isHighlighted)
        {
            context.SetBackground(_highlightBackground);
            context.Fill(0, row, width, 1, ' ');
            context.SetForeground(_highlightForeground);
        }
        else if (isSelected)
        {
            context.SetForeground(Color.BrightGreen);
        }
        else
        {
            context.SetForeground(entry.IsDirectory ? _directoryColor : _fileColor);
        }

        context.WriteAt(0, row, displayText);
        context.ResetColors();
    }

    private void RenderScrollbar(IRenderContext context, int x, int startRow, int height)
    {
        var totalItems = _filteredEntries.Count;
        if (totalItems <= height)
            return;

        var thumbSize = Math.Max(1, height * height / totalItems);
        var thumbPos = _scrollOffset * (height - thumbSize) / Math.Max(1, totalItems - height);

        context.SetForeground(Color.BrightBlack);
        for (var row = 0; row < height; row++)
        {
            var isThumb = row >= thumbPos && row < thumbPos + thumbSize;
            context.WriteAt(x, startRow + row, isThumb ? '#' : '|');
        }
        context.ResetColors();
    }

    private void RenderFooter(IRenderContext context, int row, int width)
    {
        context.SetForeground(Color.BrightBlack);

        string hints;
        if (_isFiltering)
        {
            hints = "[Enter] Open  [Esc] Clear filter";
        }
        else if (_selectionMode == FilePickerSelectionMode.Multi)
        {
            hints = _selectedPaths.Count > 0
                ? $"{_selectedPaths.Count} selected  [Space] Toggle  [Enter] Open/Confirm  [Backspace] Up  [Esc] Cancel"
                : "[Space] Toggle  [Enter] Open/Select  [Backspace] Up  [/] Filter  [Esc] Cancel";
        }
        else if (_mode is FilePickerMode.Directories or FilePickerMode.All)
        {
            hints = "[Enter] Open  [Space] Select  [Backspace] Up  [/] Filter  [Esc] Cancel";
        }
        else
        {
            hints = "[Enter] Open/Select  [Backspace] Up  [/] Filter  [Esc] Cancel";
        }

        if (DisplayWidth.GetColumnCount(hints) > width)
            hints = DisplayWidth.TruncateToColumns(hints, width);

        context.WriteAt(0, row, hints);
        context.ResetColors();
    }

    #endregion

    #region Utilities

    private static bool MatchesGlob(string fileName, string pattern)
    {
        return FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true);
    }

    private void EmitDirectoryChanged(string path)
    {
        if (!_disposed)
            _directoryChanged.OnNext(path);
    }

    private void EmitSelectionConfirmed(IReadOnlyList<string> paths)
    {
        if (!_disposed)
            _selectionConfirmed.OnNext(paths);
    }

    private void Invalidate()
    {
        if (!_disposed)
            _invalidated.OnNext(Unit.Default);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_isFiltering)
        {
            _isFiltering = false;
            _filterInput?.OnBlurred();
            _filterInput?.OnDeactivate();
        }

        _filterInvalidationSub?.Dispose();

        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _selectionConfirmed.OnCompleted();
        _selectionConfirmed.Dispose();
        _cancelled.OnCompleted();
        _cancelled.Dispose();
        _directoryChanged.OnCompleted();
        _directoryChanged.Dispose();

        _filterInput?.Dispose();
    }
}
