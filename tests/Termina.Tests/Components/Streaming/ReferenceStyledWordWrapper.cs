// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;
using Termina.Terminal;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Frozen copy of the <see cref="StyledWordWrapper.WrapLine"/> algorithm as it shipped in 0.16.3.
/// </summary>
/// <remarks>
/// This is the reference for the equivalence tests. Do not "improve" it. It re-slices the remainder
/// of a long word for every wrapped row, so it is quadratic in the length of an unbroken word. The
/// production wrapper must produce the same output in linear time.
/// </remarks>
internal static class ReferenceStyledWordWrapper
{
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

                // Break long word into chunks while preserving styles
                var remaining = word.Line;
                while (remaining.ColumnCount > width)
                {
                    var chunk = remaining.SliceByColumns(0, width);
                    if (chunk.IsEmpty)
                        break;

                    result.Add(chunk);
                    remaining = remaining.SliceByColumns(chunk.ColumnCount, int.MaxValue / 2);
                }

                // Remainder becomes start of new line
                if (!remaining.IsEmpty)
                {
                    currentLine = remaining;
                    currentWidth = remaining.ColumnCount;
                }
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
