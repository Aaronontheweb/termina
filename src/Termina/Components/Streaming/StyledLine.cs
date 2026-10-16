// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;
using Termina.Terminal;

namespace Termina.Components.Streaming;

/// <summary>
/// A line of text composed of styled segments, supporting efficient append operations
/// and substring extraction that preserves styling across segment boundaries.
/// </summary>
/// <remarks>
/// <para>
/// StyledLine automatically coalesces adjacent segments with identical styles to
/// minimize memory overhead and improve rendering performance.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var line = new StyledLine();
/// line.Append(new StyledSegment("Hello ", Color.Green));
/// line.Append(new StyledSegment("World", Color.Red));
///
/// // Get plain text
/// string plain = line.ToPlainText(); // "Hello World"
///
/// // Extract substring preserving styles
/// var sub = line.Substring(3, 5); // "lo Wo" with mixed colors
/// </code>
/// </para>
/// </remarks>
public sealed class StyledLine
{
    private readonly List<StyledSegment> _segments;
    private int _length;

    /// <summary>
    /// Creates a new empty StyledLine.
    /// </summary>
    public StyledLine()
    {
        _segments = new List<StyledSegment>();
        _length = 0;
    }

    /// <summary>
    /// Creates a new StyledLine from a collection of segments.
    /// </summary>
    /// <param name="segments">The segments to initialize with.</param>
    public StyledLine(IEnumerable<StyledSegment> segments)
    {
        _segments = new List<StyledSegment>();
        _length = 0;
        foreach (var segment in segments)
        {
            AppendInternal(segment);
        }
    }

    /// <summary>
    /// Creates a new StyledLine with a single segment.
    /// </summary>
    /// <param name="segment">The initial segment.</param>
    public StyledLine(StyledSegment segment)
    {
        _segments = new List<StyledSegment>();
        _length = 0;
        AppendInternal(segment);
    }

    /// <summary>
    /// Creates a new StyledLine with plain text (default style).
    /// </summary>
    /// <param name="text">The text content.</param>
    public StyledLine(string text) : this(new StyledSegment(text))
    {
    }

    /// <summary>
    /// The segments composing this line.
    /// </summary>
    public IReadOnlyList<StyledSegment> Segments => _segments;

    /// <summary>
    /// Total character length of the line.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Total terminal display columns occupied by the line.
    /// </summary>
    public int ColumnCount => _segments.Sum(segment => DisplayWidth.GetColumnCount(segment.Text));

    /// <summary>
    /// Returns true if the line has no content.
    /// </summary>
    public bool IsEmpty => _length == 0;

    /// <summary>
    /// Gets the number of segments in the line.
    /// </summary>
    public int SegmentCount => _segments.Count;

    /// <summary>
    /// Appends a segment to this line, coalescing with the previous segment if styles match.
    /// </summary>
    /// <param name="segment">The segment to append.</param>
    public void Append(StyledSegment segment)
    {
        AppendInternal(segment);
    }

    /// <summary>
    /// Appends plain text with default style.
    /// </summary>
    /// <param name="text">The text to append.</param>
    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        AppendInternal(new StyledSegment(text));
    }

    /// <summary>
    /// Appends text with the specified foreground color.
    /// </summary>
    /// <param name="text">The text to append.</param>
    /// <param name="foreground">The foreground color.</param>
    public void Append(string text, Color foreground)
    {
        if (string.IsNullOrEmpty(text)) return;
        AppendInternal(new StyledSegment(text, foreground));
    }

    /// <summary>
    /// Appends text with the specified style.
    /// </summary>
    /// <param name="text">The text to append.</param>
    /// <param name="style">The style to apply.</param>
    public void Append(string text, TextStyle style)
    {
        if (string.IsNullOrEmpty(text)) return;
        AppendInternal(new StyledSegment(text, style));
    }

    private void AppendInternal(StyledSegment segment)
    {
        if (segment.IsEmpty) return;

        // Coalesce adjacent segments with identical styles
        if (_segments.Count > 0)
        {
            var last = _segments[^1];
            if (last.Style.Equals(segment.Style))
            {
                _segments[^1] = new StyledSegment(last.Text + segment.Text, last.Style);
                _length += segment.Length;
                return;
            }
        }

        _segments.Add(segment);
        _length += segment.Length;
    }

    /// <summary>
    /// Extracts a substring of this line, preserving styling across segment boundaries.
    /// </summary>
    /// <param name="startIndex">The starting character index.</param>
    /// <param name="length">The number of characters to extract.</param>
    /// <returns>A new StyledLine containing the substring with preserved styles.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when startIndex or length are out of range.
    /// </exception>
    public StyledLine Substring(int startIndex, int length)
    {
        if (startIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index cannot be negative.");
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
        if (startIndex + length > _length)
            throw new ArgumentOutOfRangeException(nameof(length), "Substring extends beyond the end of the line.");

        if (length == 0)
            return new StyledLine();

        var result = new StyledLine();
        var currentIndex = 0;

        foreach (var segment in _segments)
        {
            // Skip segments entirely before our range
            if (currentIndex + segment.Length <= startIndex)
            {
                currentIndex += segment.Length;
                continue;
            }

            // Stop if we're past our range
            if (currentIndex >= startIndex + length)
                break;

            // Calculate the portion of this segment we need
            var segmentStart = Math.Max(0, startIndex - currentIndex);
            var segmentEnd = Math.Min(segment.Length, startIndex + length - currentIndex);
            var takeLength = segmentEnd - segmentStart;

            if (takeLength > 0)
            {
                var subText = segment.Text.Substring(segmentStart, takeLength);
                result.AppendInternal(new StyledSegment(subText, segment.Style));
            }

            currentIndex += segment.Length;
        }

        return result;
    }

    /// <summary>
    /// Extracts a substring from the specified index to the end of the line.
    /// </summary>
    /// <param name="startIndex">The starting character index.</param>
    /// <returns>A new StyledLine containing the substring with preserved styles.</returns>
    public StyledLine Substring(int startIndex)
    {
        if (startIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index cannot be negative.");
        if (startIndex >= _length)
            return new StyledLine();

        return Substring(startIndex, _length - startIndex);
    }

    /// <summary>
    /// Extracts a column-based slice while preserving styling across segment boundaries.
    /// </summary>
    public StyledLine SliceByColumns(int startColumn, int maxColumns)
    {
        if (startColumn < 0)
            throw new ArgumentOutOfRangeException(nameof(startColumn), "Start column cannot be negative.");
        if (maxColumns < 0)
            throw new ArgumentOutOfRangeException(nameof(maxColumns), "Column length cannot be negative.");
        if (maxColumns == 0)
            return new StyledLine();

        var result = new StyledLine();
        var currentColumn = 0;
        var taken = 0;

        foreach (var segment in _segments)
        {
            var segmentColumns = DisplayWidth.GetColumnCount(segment.Text);
            if (currentColumn + segmentColumns <= startColumn)
            {
                currentColumn += segmentColumns;
                continue;
            }

            var localStart = Math.Max(0, startColumn - currentColumn);
            var remainingBudget = maxColumns - taken;

            // Take the whole tail of this segment from localStart, then compare it to the budget.
            // This tells us whether the budget ends inside this segment or the segment ends first.
            var fullTail = DisplayWidth.SliceByColumns(segment.Text, localStart, int.MaxValue);
            var fullTailColumns = DisplayWidth.GetColumnCount(fullTail);
            var budgetEndsInsideSegment = fullTailColumns > remainingBudget;

            var localText = budgetEndsInsideSegment
                ? DisplayWidth.SliceByColumns(segment.Text, localStart, remainingBudget)
                : fullTail;

            if (localText.Length > 0)
            {
                result.AppendInternal(new StyledSegment(localText, segment.Style));
                taken += DisplayWidth.GetColumnCount(localText);
            }

            // Stop when the budget ends inside this segment. A wide glyph that straddles the budget
            // boundary is skipped, so the slice is shorter than the budget. The slice must be a
            // contiguous column range, so no later segment can join it.
            if (budgetEndsInsideSegment || taken >= maxColumns)
                break;

            currentColumn += segmentColumns;
        }

        return result;
    }

    /// <summary>
    /// Gets the plain text content without styling.
    /// </summary>
    /// <returns>The concatenated text of all segments.</returns>
    public string ToPlainText()
    {
        if (_segments.Count == 0)
            return string.Empty;

        if (_segments.Count == 1)
            return _segments[0].Text;

        var sb = new StringBuilder(_length);
        foreach (var segment in _segments)
        {
            sb.Append(segment.Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Creates a deep copy of this line.
    /// </summary>
    /// <returns>A new StyledLine with the same content.</returns>
    public StyledLine Clone()
    {
        var clone = new StyledLine();
        foreach (var segment in _segments)
        {
            clone._segments.Add(segment);
        }
        clone._length = _length;
        return clone;
    }

    /// <summary>
    /// Clears all content from the line.
    /// </summary>
    public void Clear()
    {
        _segments.Clear();
        _length = 0;
    }

    /// <summary>
    /// Gets the character at the specified index.
    /// </summary>
    /// <param name="index">The character index.</param>
    /// <returns>The character at the specified position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public char this[int index]
    {
        get
        {
            if (index < 0 || index >= _length)
                throw new ArgumentOutOfRangeException(nameof(index));

            var currentIndex = 0;
            foreach (var segment in _segments)
            {
                if (index < currentIndex + segment.Length)
                {
                    return segment.Text[index - currentIndex];
                }
                currentIndex += segment.Length;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>
    /// Gets the style at the specified character index.
    /// </summary>
    /// <param name="index">The character index.</param>
    /// <returns>The style at the specified position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public TextStyle GetStyleAt(int index)
    {
        if (index < 0 || index >= _length)
            throw new ArgumentOutOfRangeException(nameof(index));

        var currentIndex = 0;
        foreach (var segment in _segments)
        {
            if (index < currentIndex + segment.Length)
            {
                return segment.Style;
            }
            currentIndex += segment.Length;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>
    /// Creates an empty StyledLine.
    /// </summary>
    public static StyledLine Empty => new();

    /// <inheritdoc />
    public override string ToString() => ToPlainText();
}
