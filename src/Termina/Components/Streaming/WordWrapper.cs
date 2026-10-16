using Termina.Terminal;

namespace Termina.Components.Streaming;

/// <summary>
/// Utility class for word-wrapping text to fit within a specified width.
/// </summary>
public static class WordWrapper
{
    /// <summary>
    /// Wraps a single line of text to fit within the specified width.
    /// </summary>
    /// <param name="text">Text to wrap.</param>
    /// <param name="width">Maximum width per line.</param>
    /// <returns>List of wrapped lines.</returns>
    public static List<string> WrapLine(string text, int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");

        if (string.IsNullOrEmpty(text))
            return [""];

        var result = new List<string>();

        // Handle text that's shorter than width
        if (DisplayWidth.GetColumnCount(text) <= width)
        {
            result.Add(text);
            return result;
        }

        var currentLine = new System.Text.StringBuilder();
        var words = SplitIntoWords(text);

        foreach (var word in words)
        {
            var wordWidth = DisplayWidth.GetColumnCount(word);

            // If word itself is longer than width, break it
            if (wordWidth > width)
            {
                // Flush current line if it has content
                if (currentLine.Length > 0)
                {
                    result.Add(currentLine.ToString());
                    currentLine.Clear();
                }

                // Break long word into rows in one pass; the unfinished last row continues the line
                currentLine.Append(BreakLongWord(word, wordWidth, width, result));
            }
            else if (currentLine.Length == 0)
            {
                // Start of line
                currentLine.Append(word);
            }
            else if (DisplayWidth.GetColumnCount(currentLine.ToString()) + 1 + wordWidth <= width)
            {
                // Word fits with space
                currentLine.Append(' ');
                currentLine.Append(word);
            }
            else
            {
                // Word doesn't fit, start new line
                result.Add(currentLine.ToString());
                currentLine.Clear();
                currentLine.Append(word);
            }
        }

        // Flush remaining content
        if (currentLine.Length > 0)
        {
            result.Add(currentLine.ToString());
        }

        // A whitespace-only line that is wider than the width produces no words. Return one blank
        // line so the result matches the empty-string case and the styled wrapper.
        return result.Count > 0 ? result : [""];
    }

    /// <summary>
    /// Wraps multiple lines of text.
    /// </summary>
    /// <param name="lines">Lines to wrap.</param>
    /// <param name="width">Maximum width per line.</param>
    /// <returns>All wrapped lines.</returns>
    public static List<string> WrapLines(IEnumerable<string> lines, int width)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            result.AddRange(WrapLine(line, width));
        }
        return result;
    }

    /// <summary>
    /// Calculates how many wrapped lines a piece of text will produce.
    /// </summary>
    /// <param name="text">Text to measure.</param>
    /// <param name="width">Width for wrapping.</param>
    /// <returns>Number of wrapped lines.</returns>
    public static int CalculateWrappedLineCount(string text, int width)
    {
        return WrapLine(text, width).Count;
    }

    /// <summary>
    /// Calculates total wrapped line count for multiple lines.
    /// </summary>
    public static int CalculateTotalWrappedLineCount(IEnumerable<string> lines, int width)
    {
        return lines.Sum(line => CalculateWrappedLineCount(line, width));
    }

    /// <summary>
    /// Breaks a word wider than <paramref name="width"/> into rows in a single pass over its text
    /// elements. Re-measuring the remainder for every row would be quadratic in the word length.
    /// </summary>
    /// <returns>The unfinished last row. It is longer than the width only when one text element is.</returns>
    private static string BreakLongWord(string word, int wordWidth, int width, List<string> result)
    {
        var rowStart = 0;
        var taken = 0;
        var remainingColumns = wordWidth;

        foreach (var cell in DisplayWidth.EnumerateCells(word))
        {
            if (taken + cell.ColumnWidth <= width)
            {
                taken += cell.ColumnWidth;
                continue;
            }

            // Not even one element fits: the rest of the word stays together
            if (cell.StartIndex == rowStart)
                break;

            result.Add(word.Substring(rowStart, cell.StartIndex - rowStart));
            remainingColumns -= taken;
            rowStart = cell.StartIndex;
            taken = 0;

            if (remainingColumns <= width || cell.ColumnWidth > width)
                break;

            taken = cell.ColumnWidth;
        }

        return word.Substring(rowStart);
    }

    private static List<string> SplitIntoWords(string text)
    {
        var words = new List<string>();
        var currentWord = new System.Text.StringBuilder();

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (currentWord.Length > 0)
                {
                    words.Add(currentWord.ToString());
                    currentWord.Clear();
                }
                // Preserve multiple spaces as empty words? Or collapse?
                // For now, collapse whitespace
            }
            else
            {
                currentWord.Append(c);
            }
        }

        if (currentWord.Length > 0)
        {
            words.Add(currentWord.ToString());
        }

        return words;
    }
}
