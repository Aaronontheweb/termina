// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace Termina.Terminal;

/// <summary>
/// A Unicode text element plus the number of terminal columns it occupies.
/// </summary>
/// <param name="Text">The original text element.</param>
/// <param name="StartIndex">The UTF-16 start index in the source string.</param>
/// <param name="Length">The UTF-16 length in the source string.</param>
/// <param name="ColumnWidth">The number of terminal columns occupied by this element.</param>
public readonly record struct DisplayCell(string Text, int StartIndex, int Length, int ColumnWidth);

/// <summary>
/// Utility for measuring and manipulating text in terminal display columns.
/// </summary>
/// <remarks>
/// Terminal cells are not UTF-16 characters. CJK, Hangul, kana, fullwidth forms,
/// and emoji typically occupy two terminal columns; combining marks and variation
/// selectors occupy zero. All layout, wrapping, truncation, and cursor math should
/// go through this class instead of using <see cref="string.Length"/>.
/// </remarks>
public static class DisplayWidth
{
    /// <summary>
    /// Enumerates display cells using Unicode text elements so surrogate pairs and
    /// combining sequences are never split by column-based operations.
    /// </summary>
    public static IEnumerable<DisplayCell> EnumerateCells(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        var indexes = StringInfo.ParseCombiningCharacters(text);
        for (var i = 0; i < indexes.Length; i++)
        {
            var start = indexes[i];
            var end = i + 1 < indexes.Length ? indexes[i + 1] : text.Length;
            var element = text[start..end];
            yield return new DisplayCell(element, start, end - start, GetTextElementWidth(element));
        }
    }

    /// <summary>
    /// Returns the number of terminal display columns occupied by a string.
    /// </summary>
    public static int GetColumnCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Each printable ASCII char is one text element and one column. The scan does not allocate.
        // The cell walk below allocates one string for each text element, which costs about 16 MB
        // for a 600,000-char line.
        if (!text.AsSpan().ContainsAnyExceptInRange('\u0020', '\u007E'))
            return text.Length;

        var columns = 0;
        foreach (var cell in EnumerateCells(text))
            columns += cell.ColumnWidth;
        return columns;
    }

    /// <summary>
    /// Returns the number of terminal display columns occupied by a single UTF-16 character.
    /// </summary>
    public static int GetColumnCount(char c) => GetColumnCount(c.ToString());

    /// <summary>
    /// Removes terminal control sequences from user text before rendering it as printable content.
    /// </summary>
    public static string SanitizeTerminalText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        StringBuilder? sb = null;
        var segmentStart = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\u001b')
            {
                sb ??= new StringBuilder(text.Length);
                sb.Append(text, segmentStart, i - segmentStart);
                i = SkipEscapeSequence(text, i);
                segmentStart = i + 1;
            }
            else if (char.IsControl(ch))
            {
                sb ??= new StringBuilder(text.Length);
                sb.Append(text, segmentStart, i - segmentStart);
                if (ch is '\n' or '\r' or '\t')
                    sb.Append(' ');
                segmentStart = i + 1;
            }
        }

        if (sb is null)
            return text;

        sb.Append(text, segmentStart, text.Length - segmentStart);
        return sb.ToString();
    }

    /// <summary>
    /// Returns the leftmost text that fits within <paramref name="maxColumns"/> display columns.
    /// </summary>
    public static string TruncateToColumns(string text, int maxColumns)
    {
        if (string.IsNullOrEmpty(text) || maxColumns <= 0)
            return string.Empty;

        var sb = new StringBuilder();
        var columns = 0;

        foreach (var cell in EnumerateCells(text))
        {
            if (columns + cell.ColumnWidth > maxColumns)
                break;

            sb.Append(cell.Text);
            columns += cell.ColumnWidth;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the rightmost text that fits within <paramref name="maxColumns"/> display columns.
    /// </summary>
    public static string TruncateStartToColumns(string text, int maxColumns)
    {
        if (string.IsNullOrEmpty(text) || maxColumns <= 0)
            return string.Empty;

        var cells = EnumerateCells(text).ToArray();
        var columns = 0;
        var start = cells.Length;

        for (var i = cells.Length - 1; i >= 0; i--)
        {
            if (columns + cells[i].ColumnWidth > maxColumns)
                break;

            columns += cells[i].ColumnWidth;
            start = i;
        }

        return start >= cells.Length ? string.Empty : text[cells[start].StartIndex..];
    }

    /// <summary>
    /// Slices text by terminal columns. If the start falls inside a wide character,
    /// that character is skipped because terminals cannot draw half a cell pair.
    /// </summary>
    public static string SliceByColumns(string text, int startColumn, int maxColumns)
    {
        if (string.IsNullOrEmpty(text) || maxColumns <= 0)
            return string.Empty;

        startColumn = Math.Max(0, startColumn);
        var sb = new StringBuilder();
        var columns = 0;
        var taken = 0;

        foreach (var cell in EnumerateCells(text))
        {
            var nextColumns = columns + cell.ColumnWidth;
            if (nextColumns <= startColumn)
            {
                columns = nextColumns;
                continue;
            }

            if (columns < startColumn)
            {
                columns = nextColumns;
                continue;
            }

            if (taken + cell.ColumnWidth > maxColumns)
                break;

            sb.Append(cell.Text);
            taken += cell.ColumnWidth;
            columns = nextColumns;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the UTF-16 index after the longest prefix that fits within the column limit.
    /// </summary>
    public static int GetStringIndexForColumnCount(string text, int maxColumns)
    {
        if (string.IsNullOrEmpty(text) || maxColumns <= 0)
            return 0;

        var columns = 0;
        var index = 0;

        foreach (var cell in EnumerateCells(text))
        {
            if (columns + cell.ColumnWidth > maxColumns)
                break;

            columns += cell.ColumnWidth;
            index = cell.StartIndex + cell.Length;
        }

        return index;
    }

    /// <summary>
    /// Returns the display column position for a UTF-16 index in the string.
    /// </summary>
    public static int CursorPositionToColumn(string text, int charIndex)
    {
        if (string.IsNullOrEmpty(text) || charIndex <= 0)
            return 0;

        var columns = 0;
        var count = Math.Min(charIndex, text.Length);

        foreach (var cell in EnumerateCells(text))
        {
            if (cell.StartIndex + cell.Length > count)
                break;
            columns += cell.ColumnWidth;
        }

        return columns;
    }

    /// <summary>
    /// Returns a safe UTF-16 index that does not fall inside a text element.
    /// </summary>
    public static int ClampToTextElementBoundary(string text, int charIndex)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        charIndex = Math.Clamp(charIndex, 0, text.Length);
        foreach (var cell in EnumerateCells(text))
        {
            var end = cell.StartIndex + cell.Length;
            if (charIndex <= cell.StartIndex)
                return cell.StartIndex;
            if (charIndex < end)
                return cell.StartIndex;
        }

        return text.Length;
    }

    /// <summary>
    /// Returns the UTF-16 start index of the text element before <paramref name="charIndex"/>.
    /// </summary>
    public static int GetPreviousTextElementIndex(string text, int charIndex)
    {
        if (string.IsNullOrEmpty(text) || charIndex <= 0)
            return 0;

        charIndex = Math.Clamp(charIndex, 0, text.Length);
        var previous = 0;

        foreach (var cell in EnumerateCells(text))
        {
            if (cell.StartIndex >= charIndex)
                break;

            previous = cell.StartIndex;
        }

        return previous;
    }

    /// <summary>
    /// Returns the UTF-16 end index of the text element at or after <paramref name="charIndex"/>.
    /// </summary>
    public static int GetNextTextElementIndex(string text, int charIndex)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        charIndex = Math.Clamp(charIndex, 0, text.Length);
        foreach (var cell in EnumerateCells(text))
        {
            var end = cell.StartIndex + cell.Length;
            if (charIndex < end)
                return end;
        }

        return text.Length;
    }

    /// <summary>
    /// Returns the complete text element at a UTF-16 index, or a space at the end of the string.
    /// </summary>
    public static string GetTextElementAt(string text, int charIndex)
    {
        if (string.IsNullOrEmpty(text) || charIndex >= text.Length)
            return " ";

        charIndex = Math.Max(0, charIndex);
        foreach (var cell in EnumerateCells(text))
        {
            if (charIndex >= cell.StartIndex && charIndex < cell.StartIndex + cell.Length)
                return cell.Text;
        }

        return " ";
    }

    private static int GetTextElementWidth(string element)
    {
        var hasEmojiPresentationSelector = false;
        var hasEmojiCandidate = false;
        var hasKeycap = false;
        var width = 0;
        var sawWideOrEmoji = false;

        foreach (var rune in element.EnumerateRunes())
        {
            if (rune.Value == 0xFE0F)
                hasEmojiPresentationSelector = true;
            if (rune.Value == 0x20E3)
                hasKeycap = true;
            if (IsEmojiCandidate(rune))
                hasEmojiCandidate = true;

            var runeWidth = GetRuneWidth(rune);
            if (runeWidth >= 2)
                sawWideOrEmoji = true;
            width += runeWidth;
        }

        if (hasKeycap || (hasEmojiPresentationSelector && hasEmojiCandidate))
            return 2;

        return sawWideOrEmoji ? 2 : width;
    }

    private static int GetRuneWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format
            or UnicodeCategory.Control
            or UnicodeCategory.Surrogate)
            return 0;

        return IsWideOrFullwidth(rune) || IsDefaultEmojiPresentation(rune) ? 2 : 1;
    }

    private static bool IsWideOrFullwidth(Rune rune)
    {
        var code = rune.Value;

        // East Asian Wide / Fullwidth ranges, based on wcwidth-style rules.
        if (code >= 0x1100 && code <= 0x115F) return true; // Hangul Jamo init. consonants
        if (code is 0x2329 or 0x232A) return true;
        if (code >= 0x2E80 && code <= 0xA4CF) return true; // CJK, kana, bopomofo, yi
        if (code >= 0xAC00 && code <= 0xD7A3) return true; // Hangul syllables
        if (code >= 0xF900 && code <= 0xFAFF) return true; // CJK compatibility ideographs
        if (code >= 0xFE10 && code <= 0xFE19) return true;
        if (code >= 0xFE30 && code <= 0xFE6F) return true;
        if (code >= 0xFF00 && code <= 0xFF60) return true; // Fullwidth forms, not halfwidth katakana
        if (code >= 0xFFE0 && code <= 0xFFE6) return true;
        if (code >= 0x20000 && code <= 0x3FFFD) return true; // CJK extensions

        return false;
    }

    private static bool IsEmojiCandidate(Rune rune)
    {
        var code = rune.Value;
        return code is 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139
            || (code >= 0x2194 && code <= 0x21AA)
            || (code >= 0x2300 && code <= 0x23FF)
            || (code >= 0x2460 && code <= 0x24FF)
            || (code >= 0x25A0 && code <= 0x27BF)
            || (code >= 0x2900 && code <= 0x297F)
            || (code >= 0x2B00 && code <= 0x2BFF)
            || code is 0x3030 or 0x303D or 0x3297 or 0x3299
            || (code >= 0x1F000 && code <= 0x1FAFF);
    }

    private static bool IsDefaultEmojiPresentation(Rune rune)
    {
        var code = rune.Value;
        if (code >= 0x1F000 && code <= 0x1FAFF) return true;

        return code is 0x231A or 0x231B
            || (code >= 0x23E9 && code <= 0x23EC)
            || code is 0x23F0 or 0x23F3
            || (code >= 0x25FD && code <= 0x25FE)
            || code is 0x2614 or 0x2615
            || (code >= 0x2648 && code <= 0x2653)
            || code is 0x267F or 0x2693 or 0x26A1
            || (code >= 0x26AA && code <= 0x26AB)
            || (code >= 0x26BD && code <= 0x26BE)
            || (code >= 0x26C4 && code <= 0x26C5)
            || code is 0x26CE or 0x26D4 or 0x26EA
            || (code >= 0x26F2 && code <= 0x26F3)
            || code is 0x26F5 or 0x26FA or 0x26FD or 0x2705
            || (code >= 0x270A && code <= 0x270B)
            || code is 0x2728 or 0x274C or 0x274E
            || (code >= 0x2753 && code <= 0x2755)
            || code is 0x2757
            || (code >= 0x2795 && code <= 0x2797)
            || code is 0x27B0 or 0x27BF or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55;
    }

    private static int SkipEscapeSequence(string text, int escapeIndex)
    {
        if (escapeIndex + 1 >= text.Length)
            return escapeIndex;

        var introducer = text[escapeIndex + 1];
        if (introducer == '[')
        {
            for (var i = escapeIndex + 2; i < text.Length; i++)
            {
                if (text[i] >= 0x40 && text[i] <= 0x7E)
                    return i;
            }

            return text.Length - 1;
        }

        if (introducer == ']')
        {
            for (var i = escapeIndex + 2; i < text.Length; i++)
            {
                if (text[i] == '\u0007')
                    return i;
                if (text[i] == '\u001b' && i + 1 < text.Length && text[i + 1] == '\\')
                    return i + 1;
            }

            return text.Length - 1;
        }

        return escapeIndex + 1;
    }
}
