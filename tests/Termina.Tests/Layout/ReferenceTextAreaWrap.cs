// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// Frozen copy of the <c>TextAreaNode</c> row wrapper as it shipped in 0.16.3. It is the reference
/// for the equivalence tests. Do not "improve" it. It copies and measures the remainder of the
/// line for every row, so it is quadratic in the line length.
/// </summary>
internal static class ReferenceTextAreaWrap
{
    internal static void WrapLogicalLine(
        List<TextAreaNode.WrappedLine> result, string text, int lineStart, int lineLength, int width)
    {
        var pos = lineStart;
        var end = lineStart + lineLength;

        while (pos < end)
        {
            var remainingText = text[pos..end];
            var remainingWidth = DisplayWidth.GetColumnCount(remainingText);
            if (remainingWidth <= width)
            {
                result.Add(new TextAreaNode.WrappedLine(pos, end - pos, remainingWidth));
                break;
            }

            // Try to break at a word boundary
            var breakOffset = DisplayWidth.GetStringIndexForColumnCount(remainingText, width);
            if (breakOffset == 0)
                breakOffset = DisplayWidth.EnumerateCells(remainingText).First().Length;

            var breakPos = pos + breakOffset;
            var wordBreak = breakPos;

            while (wordBreak > pos && !char.IsWhiteSpace(text[wordBreak - 1]))
                wordBreak--;

            if (wordBreak == pos)
                wordBreak = breakPos;

            var wrappedLength = wordBreak - pos;
            var wrappedText = text.Substring(pos, wrappedLength);
            result.Add(new TextAreaNode.WrappedLine(pos, wrappedLength, DisplayWidth.GetColumnCount(wrappedText)));
            pos = wordBreak;

            // Skip leading whitespace on the new line
            while (pos < end && text[pos] == ' ')
                pos++;
        }
    }
}
