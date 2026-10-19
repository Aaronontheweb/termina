// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Handles incomplete and unrecognized CSI sequences without leaving the parser stuck.
/// </summary>
internal static class UnknownSequenceFallbackDecoder
{
    /// <summary>
    /// The maximum length of a buffered sequence, including the leading <c>[</c>.
    /// </summary>
    /// <remarks>
    /// A kitty CSI-u sequence with associated text has one colon-separated code point for each character,
    /// so input method editor (IME) text can make it long. The limit allows more than 100 code points of
    /// associated text and stops a stray <c>ESC [</c> from buffering input without limit.
    /// </remarks>
    public const int MaxSequenceLength = 1024;

    /// <summary>
    /// Returns <c>true</c> if the accumulated bracket sequence could still lead to a recognized
    /// escape sequence.
    /// </summary>
    /// <remarks>
    /// A sequence that has its final byte, and that no decoder recognized, is complete and unknown.
    /// It is flushed as raw <see cref="KeyPressed"/> events rather than keeping the parser stuck in
    /// <c>InBracketSequence</c>.
    /// </remarks>
    public static bool CouldLeadToRecognizedSequence(InputSequence sequence)
    {
        var text = sequence.Text;

        if (BracketedPasteDecoder.CouldBeStartSequence(sequence))
            return true;

        if (text.Length > MaxSequenceLength || text.Length == 0 || text[0] != '[')
            return false;

        return ControlSequenceSyntax.IsIncomplete(text.AsSpan(1));
    }

    public static IReadOnlyList<IInputEvent> ToRawKeyEvents(string sequence)
    {
        var results = new List<IInputEvent>(sequence.Length + 1);
        AppendRawKeyEvents(sequence, results);
        return results;
    }

    public static void AppendRawKeyEvents(string sequence, List<IInputEvent> results)
    {
        results.Add(new KeyPressed(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)));
        foreach (var c in sequence)
            results.Add(new KeyPressed(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false)));
    }

    public static void AppendRawSs3KeyEvents(char final, List<IInputEvent> results)
    {
        results.Add(new KeyPressed(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)));
        results.Add(new KeyPressed(new ConsoleKeyInfo('O', ConsoleKey.O, false, false, false)));
        results.Add(new KeyPressed(new ConsoleKeyInfo(final, ConsoleKey.None, false, false, false)));
    }
}
