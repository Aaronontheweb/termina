// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Components.Streaming;

/// <summary>
/// Word wrapper that preserves styling across line breaks.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the plain <see cref="WordWrapper"/>, this class operates on <see cref="StyledLine"/>
/// objects and ensures that text styles are preserved when lines are wrapped.
/// </para>
/// <para>
/// Example:
/// <code>
/// var line = new StyledLine();
/// line.Append("Hello ", Color.Green);
/// line.Append("World", Color.Red);
///
/// var wrapped = StyledWordWrapper.WrapLine(line, 8);
/// // Result: two lines, "Hello" (green) and "World" (red)
/// </code>
/// </para>
/// </remarks>
public static class StyledWordWrapper
{
    /// <summary>
    /// A word split out of a styled line, together with the style of the whitespace
    /// that separated it from the previous word in the source line.
    /// </summary>
    /// <remarks>
    /// The wrapper discards whitespace when it splits a line into words, then re-inserts a
    /// single space between words that share an output line. That separator must carry the
    /// style of the original whitespace, so a background highlight stays contiguous and an
    /// unstyled gap between two styled words does not pick up a background color.
    /// </remarks>
    private readonly record struct StyledWord(StyledLine Line, TextStyle SeparatorStyle);

    /// <summary>
    /// Wraps a styled line to fit within the specified width, preserving styling.
    /// </summary>
    /// <param name="line">The line to wrap.</param>
    /// <param name="width">Maximum width per line.</param>
    /// <returns>List of wrapped styled lines.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when width is not positive.</exception>
    public static List<StyledLine> WrapLine(StyledLine line, int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");

        if (line.IsEmpty)
            return [new StyledLine()];

        // Fast path: line fits within width
        if (line.ColumnCount <= width)
            return [line.Clone()];

        var result = new List<StyledLine>();
        var currentLine = new StyledLine();
        var currentWidth = 0;

        // Split into styled words
        var words = SplitIntoStyledWords(line);

        foreach (var word in words)
        {
            var wordWidth = word.Line.ColumnCount;

            // If word itself is longer than width, break it
            if (wordWidth > width)
            {
                // Flush current line if it has content
                if (currentWidth > 0)
                {
                    result.Add(currentLine);
                    currentLine = new StyledLine();
                    currentWidth = 0;
                }

                // Break long word into rows in one pass, preserving styles. The unfinished last
                // row becomes the start of the current line.
                currentLine = BreakLongWord(word.Line, wordWidth, width, result);
                currentWidth = currentLine.ColumnCount;
            }
            else if (currentWidth == 0)
            {
                // Start of line - add word directly
                foreach (var segment in word.Line.Segments)
                {
                    currentLine.Append(segment);
                }
                currentWidth = wordWidth;
            }
            else if (currentWidth + 1 + wordWidth <= width)
            {
                // Word fits with a single-space separator. The separator carries the style of the
                // whitespace that separated the two words in the source line, so a background
                // highlight stays contiguous and an unstyled gap stays unstyled.
                currentLine.Append(new StyledSegment(" ", word.SeparatorStyle));
                foreach (var segment in word.Line.Segments)
                {
                    currentLine.Append(segment);
                }
                currentWidth += 1 + wordWidth;
            }
            else
            {
                // Word doesn't fit, start new line
                result.Add(currentLine);
                currentLine = new StyledLine();
                foreach (var segment in word.Line.Segments)
                {
                    currentLine.Append(segment);
                }
                currentWidth = wordWidth;
            }
        }

        // Flush remaining content
        if (currentWidth > 0)
        {
            result.Add(currentLine);
        }

        return result.Count > 0 ? result : [new StyledLine()];
    }

    /// <summary>
    /// Breaks a word that is wider than the available width into rows of at most
    /// <paramref name="width"/> columns, in a single pass over its text elements.
    /// </summary>
    /// <remarks>
    /// Each text element is measured once and rows are emitted as the column budget fills. This
    /// keeps the cost linear in the length of the word. Re-slicing the remainder for every row
    /// costs a walk of the whole remainder per row, which is quadratic for a long unbroken word.
    /// A zero-width element at the start of a segment is dropped, as <see cref="StyledLine.SliceByColumns"/>
    /// drops it. A wide element that does not fit a row starts the next row. When even an empty row
    /// cannot hold the next element, the rest of the word stays together as the current line.
    /// A zero-width-only segment between two segments of the same style is dropped before the
    /// neighbours join. The 0.16.3 slicer measured the joined text again, and when the join formed
    /// one text element (a flag, an emoji modifier sequence) its widths no longer matched and it
    /// could repeat text. Here the widths are the ones measured per segment.
    /// </remarks>
    /// <returns>The unfinished last row, which the caller continues to fill. It is never null.</returns>
    private static StyledLine BreakLongWord(StyledLine word, int wordWidth, int width, List<StyledLine> result)
    {
        var segments = word.Segments;
        var row = new StyledLine();
        var taken = 0;
        var remainingColumns = wordWidth;
        var emittedRow = false;

        for (var s = 0; s < segments.Count; s++)
        {
            var text = segments[s].Text;
            var style = segments[s].Style;
            var pieceStart = -1; // start of the part of this segment that belongs to the current row
            var leading = true;

            foreach (var cell in DisplayWidth.EnumerateCells(text))
            {
                // A zero-width element at the start of a segment is dropped
                if (leading && cell.ColumnWidth == 0)
                    continue;
                leading = false;

                if (taken + cell.ColumnWidth <= width)
                {
                    if (pieceStart < 0)
                        pieceStart = cell.StartIndex;
                    taken += cell.ColumnWidth;
                    continue;
                }

                // This element does not fit in the current row
                if (pieceStart >= 0)
                    row.Append(new StyledSegment(text.Substring(pieceStart, cell.StartIndex - pieceStart), style));

                if (row.IsEmpty)
                {
                    // Not even one element fits. Before the first row the word stays as it was,
                    // later the rest of the word stays together.
                    return emittedRow ? CollectRest(segments, s, cell.StartIndex) : word;
                }

                result.Add(row);
                emittedRow = true;
                remainingColumns -= taken;
                if (remainingColumns <= width)
                    return CollectRest(segments, s, cell.StartIndex);

                row = new StyledLine();
                taken = 0;
                pieceStart = -1;

                // The element starts the next row
                if (cell.ColumnWidth > width)
                    return CollectRest(segments, s, cell.StartIndex);
                pieceStart = cell.StartIndex;
                taken = cell.ColumnWidth;
            }

            if (pieceStart >= 0)
                row.Append(new StyledSegment(text.Substring(pieceStart), style));
        }

        // Unreachable for a word wider than the width: some element always overflows a row.
        return emittedRow ? row : word;
    }

    /// <summary>
    /// Copies the rest of a word, from a text element in one segment to the end, dropping a
    /// zero-width element at the start of each later segment.
    /// </summary>
    private static StyledLine CollectRest(IReadOnlyList<StyledSegment> segments, int segmentIndex, int startIndex)
    {
        var rest = new StyledLine();
        rest.Append(new StyledSegment(segments[segmentIndex].Text.Substring(startIndex), segments[segmentIndex].Style));

        for (var s = segmentIndex + 1; s < segments.Count; s++)
        {
            foreach (var cell in DisplayWidth.EnumerateCells(segments[s].Text))
            {
                if (cell.ColumnWidth == 0)
                    continue;

                rest.Append(new StyledSegment(segments[s].Text.Substring(cell.StartIndex), segments[s].Style));
                break;
            }
        }

        return rest;
    }

    /// <summary>
    /// Wraps multiple styled lines.
    /// </summary>
    /// <param name="lines">Lines to wrap.</param>
    /// <param name="width">Maximum width per line.</param>
    /// <returns>All wrapped styled lines.</returns>
    public static List<StyledLine> WrapLines(IEnumerable<StyledLine> lines, int width)
    {
        var result = new List<StyledLine>();
        foreach (var line in lines)
        {
            result.AddRange(WrapLine(line, width));
        }
        return result;
    }

    /// <summary>
    /// Calculates how many wrapped lines a styled line will produce.
    /// </summary>
    /// <param name="line">Line to measure.</param>
    /// <param name="width">Width for wrapping.</param>
    /// <returns>Number of wrapped lines.</returns>
    public static int CalculateWrappedLineCount(StyledLine line, int width)
    {
        return WrapLine(line, width).Count;
    }

    /// <summary>
    /// Calculates total wrapped line count for multiple styled lines.
    /// </summary>
    public static int CalculateTotalWrappedLineCount(IEnumerable<StyledLine> lines, int width)
    {
        return lines.Sum(line => CalculateWrappedLineCount(line, width));
    }

    /// <summary>
    /// Splits a styled line into styled words, preserving styling for each character.
    /// </summary>
    /// <remarks>
    /// This handles the complex case where a word may span multiple segments with
    /// different styles. Each resulting "word" is a StyledLine that may contain
    /// multiple segments, together with the style of the whitespace that preceded it.
    /// </remarks>
    private static List<StyledWord> SplitIntoStyledWords(StyledLine line)
    {
        var words = new List<StyledWord>();
        var currentWord = new StyledLine();

        // Style of the whitespace that precedes the word currently being built.
        // The first word has no preceding whitespace, so its separator style is never used.
        var separatorStyle = TextStyle.Default;

        foreach (var segment in line.Segments)
        {
            var currentSegmentStart = 0;

            for (var i = 0; i < segment.Length; i++)
            {
                var c = segment.Text[i];

                if (char.IsWhiteSpace(c))
                {
                    // Flush any accumulated text from this segment to current word
                    if (i > currentSegmentStart)
                    {
                        var text = segment.Text.Substring(currentSegmentStart, i - currentSegmentStart);
                        currentWord.Append(new StyledSegment(text, segment.Style));
                    }

                    // If we have a word, add it to results
                    if (!currentWord.IsEmpty)
                    {
                        words.Add(new StyledWord(currentWord, separatorStyle));
                        currentWord = new StyledLine();
                    }

                    // The style of this whitespace separates the previous word from the next one.
                    separatorStyle = segment.Style;
                    currentSegmentStart = i + 1;
                }
            }

            // Flush remaining text from this segment
            if (currentSegmentStart < segment.Length)
            {
                var text = segment.Text.Substring(currentSegmentStart);
                currentWord.Append(new StyledSegment(text, segment.Style));
            }
        }

        // Add final word if any
        if (!currentWord.IsEmpty)
        {
            words.Add(new StyledWord(currentWord, separatorStyle));
        }

        return words;
    }
}
