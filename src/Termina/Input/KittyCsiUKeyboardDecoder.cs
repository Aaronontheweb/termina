// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;

namespace Termina.Input;

/// <summary>
/// Decodes kitty keyboard protocol CSI-u sequences.
/// </summary>
internal static class KittyCsiUKeyboardDecoder
{
    /// <summary>
    /// Attempts to parse <c>[keycode[;modifiers[:event][;text...]]u</c>.
    /// </summary>
    /// <param name="sequence">The buffered sequence, e.g. <c>[13;5u</c>.</param>
    /// <param name="result">The resulting semantic key event, if one should be emitted.</param>
    /// <returns><c>true</c> if the sequence was parsed, whether or not an event is produced.</returns>
    public static bool TryDecode(InputSequence sequence, out KeyStroke? result)
    {
        result = null;
        var text = sequence.Text;

        if (text.Length < 3 || text[0] != '[' || text[^1] != 'u')
            return false;

        var inner = text[1..^1];
        var semicolon = inner.IndexOf(';');

        int keycode;
        var modValue = 1;
        var phase = KeyEventPhase.Press;
        string? associatedText = null;

        if (semicolon < 0)
        {
            var keyPart = inner;
            var sub = keyPart.IndexOf(':');
            if (sub >= 0)
                keyPart = keyPart[..sub];

            if (!int.TryParse(keyPart, out keycode))
                return false;
        }
        else
        {
            var keyPart = inner[..semicolon];
            var subKey = keyPart.IndexOf(':');
            if (subKey >= 0)
                keyPart = keyPart[..subKey];

            if (!int.TryParse(keyPart, out keycode))
                return false;

            var rest = inner[(semicolon + 1)..];
            var nextSemi = rest.IndexOf(';');
            if (nextSemi >= 0)
            {
                if (!TryDecodeAssociatedText(rest[(nextSemi + 1)..], out associatedText))
                    return false;
                rest = rest[..nextSemi];
            }

            var modPart = rest;
            var colon = rest.IndexOf(':');
            if (colon >= 0)
            {
                modPart = rest[..colon];
                if (!int.TryParse(rest[(colon + 1)..], out var eventType))
                    return false;

                phase = eventType switch
                {
                    1 => KeyEventPhase.Press,
                    2 => KeyEventPhase.Repeat,
                    3 => KeyEventPhase.Release,
                    _ => phase,
                };

                if (eventType is not (1 or 2 or 3))
                    return true;
            }

            if (!int.TryParse(modPart, out modValue) || modValue < 1)
                return false;
        }

        // Modifier-key-alone events add noise without actionable ConsoleKey equivalents.
        if (keycode is >= 57441 and <= 57454)
            return true;

        var modBits = modValue - 1;
        var modifiers = KeyModifiers.None;
        if ((modBits & 1) != 0) modifiers |= KeyModifiers.Shift;
        if ((modBits & 2) != 0) modifiers |= KeyModifiers.Alt;
        if ((modBits & 4) != 0) modifiers |= KeyModifiers.Control;

        var (key, fallbackText) = MapKeycodeToKeyStroke(keycode);
        var textOutput = associatedText ?? fallbackText;
        if (key == TerminaKey.None && textOutput is null)
            return true;

        result = new KeyStroke(key, modifiers, phase, textOutput);
        return true;
    }

    private static bool TryDecodeAssociatedText(string value, out string? text)
    {
        text = null;
        var builder = new StringBuilder();
        foreach (var part in value.Split(':'))
        {
            if (!int.TryParse(part, out var codePoint) || !Rune.IsValid(codePoint))
                return false;

            builder.Append(new Rune(codePoint));
        }

        text = builder.ToString();
        return text.Length > 0;
    }

    private static (TerminaKey Key, string? Text) MapKeycodeToKeyStroke(int keycode) => keycode switch
    {
        13 => (TerminaKey.Enter, "\r"),
        9 => (TerminaKey.Tab, "\t"),
        127 => (TerminaKey.Backspace, "\b"),
        27 => (TerminaKey.Escape, "\x1b"),
        >= 32 and <= 126 => (CharToTerminaKey((char)keycode), ((char)keycode).ToString()),
        57344 => (TerminaKey.Escape, null),
        57345 => (TerminaKey.Enter, null),
        57346 => (TerminaKey.Tab, null),
        57347 => (TerminaKey.Backspace, null),
        57348 => (TerminaKey.Insert, null),
        57349 => (TerminaKey.Delete, null),
        57350 => (TerminaKey.LeftArrow, null),
        57351 => (TerminaKey.RightArrow, null),
        57352 => (TerminaKey.UpArrow, null),
        57353 => (TerminaKey.DownArrow, null),
        57354 => (TerminaKey.PageUp, null),
        57355 => (TerminaKey.PageDown, null),
        57356 => (TerminaKey.Home, null),
        57357 => (TerminaKey.End, null),
        57358 => (TerminaKey.None, null),
        >= 57364 and <= 57375 => (TerminaKey.F1 + (keycode - 57364), null),
        _ => (TerminaKey.None, keycode is > 0 and < 128 ? ((char)keycode).ToString() : null)
    };

    private static TerminaKey CharToTerminaKey(char c) => c switch
    {
        >= 'a' and <= 'z' => TerminaKey.A + (c - 'a'),
        >= 'A' and <= 'Z' => TerminaKey.A + (c - 'A'),
        >= '0' and <= '9' => TerminaKey.D0 + (c - '0'),
        ' ' => TerminaKey.Space,
        _ => TerminaKey.None
    };
}
