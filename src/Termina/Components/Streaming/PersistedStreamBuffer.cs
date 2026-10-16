using Termina.Terminal;

namespace Termina.Components.Streaming;

/// <summary>
/// A streaming text buffer that retains full history with scroll support.
/// Suitable for chat messages, command output, logs that need review.
/// </summary>
/// <remarks>
/// <para>
/// Key features:
/// </para>
/// <list type="bullet">
///   <item>Full content retained and scrollable</item>
///   <item>Auto-scroll when at bottom, maintains position when scrolled up</item>
///   <item>Thread-safe for concurrent append operations</item>
///   <item>Efficient append - doesn't re-process entire buffer</item>
/// </list>
/// </remarks>
public class PersistedStreamBuffer : IStreamingTextBuffer
{
    private readonly List<StyledLine> _styledLines = [];
    private readonly object _lock = new();
    private StyledLine _currentStyledLine = new();

    // Scroll offset: 0 = bottom (newest), positive = scrolled up
    private int _scrollOffset;

    // Track if user has manually scrolled
    private bool _userScrolled;

    // Cache for wrapped line count (invalidated on content change)
    private int _cachedWrappedLineCount;
    private int _cachedWidth;
    private bool _wrappedCountDirty = true;

    /// <inheritdoc />
    public StreamMode Mode => StreamMode.Persisted;

    /// <inheritdoc />
    public int LineCount
    {
        get
        {
            lock (_lock)
            {
                // Include current partial line if non-empty
                return _styledLines.Count + (_currentStyledLine.Length > 0 ? 1 : 0);
            }
        }
    }

    /// <inheritdoc />
    public int CharacterCount
    {
        get
        {
            lock (_lock)
            {
                var count = _styledLines.Sum(l => l.Length + 1); // +1 for newlines
                count += _currentStyledLine.Length;
                return count;
            }
        }
    }

    /// <inheritdoc />
    public bool HasContent
    {
        get
        {
            lock (_lock)
            {
                return _styledLines.Count > 0 || _currentStyledLine.Length > 0;
            }
        }
    }

    /// <inheritdoc />
    public bool HasContentOnCurrentLine
    {
        get
        {
            lock (_lock)
            {
                return _currentStyledLine.Length > 0;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether auto-scroll is enabled.
    /// When true (default), view scrolls to bottom on new content if not manually scrolled.
    /// </summary>
    public bool AutoScroll { get; set; } = true;

    /// <summary>
    /// Gets the current scroll offset (0 = bottom).
    /// </summary>
    public int ScrollOffset
    {
        get
        {
            lock (_lock)
            {
                return _scrollOffset;
            }
        }
    }

    /// <summary>
    /// Gets whether the user has manually scrolled away from the bottom.
    /// </summary>
    public bool IsScrolledUp
    {
        get
        {
            lock (_lock)
            {
                return _userScrolled && _scrollOffset > 0;
            }
        }
    }

    /// <inheritdoc />
    public void Append(string text)
    {
        Append(text, TextStyle.Default);
    }

    /// <inheritdoc />
    public void Append(string text, TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (_lock)
        {
            var pendingText = new System.Text.StringBuilder();

            foreach (var c in text)
            {
                if (c == '\n')
                {
                    // Flush pending text to current line
                    if (pendingText.Length > 0)
                    {
                        _currentStyledLine.Append(new StyledSegment(pendingText.ToString(), style));
                        pendingText.Clear();
                    }

                    // Finalize current line
                    _styledLines.Add(_currentStyledLine);
                    _currentStyledLine = new StyledLine();
                }
                else if (c != '\r') // Ignore carriage returns
                {
                    pendingText.Append(c);
                }
            }

            // Append remaining text to current line
            if (pendingText.Length > 0)
            {
                _currentStyledLine.Append(new StyledSegment(pendingText.ToString(), style));
            }

            _wrappedCountDirty = true;

            // Auto-scroll to bottom if enabled and user hasn't scrolled
            if (AutoScroll && !_userScrolled)
            {
                _scrollOffset = 0;
            }
        }
    }

    /// <inheritdoc />
    public void Append(string text, Color foreground)
    {
        Append(text, new TextStyle(foreground));
    }

    /// <inheritdoc />
    public void Append(StyledSegment segment)
    {
        Append(segment.Text, segment.Style);
    }

    /// <inheritdoc />
    public void AppendLine(string line)
    {
        Append(line + "\n");
    }

    /// <inheritdoc />
    public void AppendLine(string line, TextStyle style)
    {
        Append(line + "\n", style);
    }

    /// <inheritdoc />
    public void AppendLine(string line, Color foreground)
    {
        Append(line + "\n", new TextStyle(foreground));
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _styledLines.Clear();
            _currentStyledLine = new StyledLine();
            _scrollOffset = 0;
            _userScrolled = false;
            _wrappedCountDirty = true;
        }
    }

    /// <summary>
    /// Scrolls up by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll (default 1).</param>
    /// <param name="viewportWidth">Width for word wrapping calculation.</param>
    public void ScrollUp(int lines = 1, int viewportWidth = 80)
    {
        ScrollUp(lines, viewportWidth, viewportHeight: 1);
    }

    /// <summary>
    /// Scrolls up without moving the oldest content above the viewport.
    /// </summary>
    /// <param name="lines">Number of lines to scroll.</param>
    /// <param name="viewportWidth">Width for word wrapping calculation.</param>
    /// <param name="viewportHeight">Number of visible rows.</param>
    public void ScrollUp(int lines, int viewportWidth, int viewportHeight)
    {
        lock (_lock)
        {
            _userScrolled = true;
            var maxScroll = GetMaxScrollOffset(viewportWidth, viewportHeight);
            _scrollOffset = Math.Min(_scrollOffset + lines, maxScroll);
        }
    }

    /// <summary>
    /// Scrolls down by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll (default 1).</param>
    public void ScrollDown(int lines = 1)
    {
        lock (_lock)
        {
            _scrollOffset = Math.Max(0, _scrollOffset - lines);

            // If scrolled back to bottom, re-enable auto-scroll
            if (_scrollOffset == 0)
            {
                _userScrolled = false;
            }
        }
    }

    /// <summary>
    /// Scrolls to the bottom (newest content).
    /// </summary>
    public void ScrollToBottom()
    {
        lock (_lock)
        {
            _scrollOffset = 0;
            _userScrolled = false;
        }
    }

    /// <summary>
    /// Scrolls to the top (oldest content).
    /// </summary>
    /// <param name="viewportWidth">Width for word wrapping calculation.</param>
    public void ScrollToTop(int viewportWidth = 80)
    {
        ScrollToTop(viewportWidth, viewportHeight: 1);
    }

    /// <summary>
    /// Scrolls to the oldest viewport without moving content above it.
    /// </summary>
    /// <param name="viewportWidth">Width for word wrapping calculation.</param>
    /// <param name="viewportHeight">Number of visible rows.</param>
    public void ScrollToTop(int viewportWidth, int viewportHeight)
    {
        lock (_lock)
        {
            _userScrolled = true;
            _scrollOffset = GetMaxScrollOffset(viewportWidth, viewportHeight);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetVisibleLines(int viewportHeight, int viewportWidth)
    {
        // Delegate to styled version and convert to plain text
        return GetVisibleStyledLines(viewportHeight, viewportWidth)
            .Select(l => l.ToPlainText())
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<StyledLine> GetVisibleStyledLines(int viewportHeight, int viewportWidth)
    {
        if (viewportHeight <= 0 || viewportWidth <= 0)
            return [];

        lock (_lock)
        {
            var allLines = GetAllStyledLinesInternal().ToList();
            var wrappedLines = StyledWordWrapper.WrapLines(allLines, viewportWidth);

            if (wrappedLines.Count == 0)
                return [];

            // Calculate visible range from bottom with scroll offset
            var totalWrapped = wrappedLines.Count;
            var bottomIndex = totalWrapped - _scrollOffset;
            var topIndex = Math.Max(0, bottomIndex - viewportHeight);
            bottomIndex = Math.Min(totalWrapped, topIndex + viewportHeight);

            return wrappedLines.Skip(topIndex).Take(bottomIndex - topIndex).ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllLines()
    {
        return GetAllStyledLines().Select(l => l.ToPlainText()).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<StyledLine> GetAllStyledLines()
    {
        lock (_lock)
        {
            return GetAllStyledLinesInternal().ToList();
        }
    }

    /// <summary>
    /// Gets the total number of wrapped lines for the given width.
    /// </summary>
    public int GetWrappedLineCount(int viewportWidth)
    {
        lock (_lock)
        {
            if (_wrappedCountDirty || _cachedWidth != viewportWidth)
            {
                var allLines = GetAllStyledLinesInternal().ToList();
                _cachedWrappedLineCount = StyledWordWrapper.CalculateTotalWrappedLineCount(allLines, viewportWidth);
                _cachedWidth = viewportWidth;
                _wrappedCountDirty = false;
            }
            return _cachedWrappedLineCount;
        }
    }

    /// <summary>
    /// Returns the maximum scroll offset for the given viewport width.
    /// At maximum offset, the oldest content is visible at the bottom of the viewport.
    /// </summary>
    /// <param name="viewportWidth">
    /// The width of the content area in columns, used for word-wrap calculations.
    /// This should be the content width excluding any prefix or scrollbar columns.
    /// </param>
    public int GetMaxScrollOffset(int viewportWidth)
    {
        return GetMaxScrollOffset(viewportWidth, viewportHeight: 1);
    }

    /// <summary>
    /// Returns the maximum scroll offset that keeps the oldest content at the top of the viewport.
    /// </summary>
    /// <param name="viewportWidth">Width for word wrapping calculation.</param>
    /// <param name="viewportHeight">Number of visible rows.</param>
    public int GetMaxScrollOffset(int viewportWidth, int viewportHeight)
    {
        var totalWrapped = GetWrappedLineCount(viewportWidth);
        return Math.Max(0, totalWrapped - Math.Max(1, viewportHeight));
    }

    private IEnumerable<StyledLine> GetAllStyledLinesInternal()
    {
        foreach (var line in _styledLines)
        {
            yield return line;
        }

        // Include current partial line if non-empty
        if (_currentStyledLine.Length > 0)
        {
            yield return _currentStyledLine;
        }
    }
}
