// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace Termina.Input;

/// <summary>
/// Decodes kitty keyboard protocol CSI-u sequences.
/// </summary>
/// <remarks>
/// See <see href="https://sw.kovidgoyal.net/kitty/keyboard-protocol/"/>. The sequence has up to three
/// fields: <c>unicode-key-code:alternate-key-codes ; modifiers:event-type ; text-as-code-points</c>.
/// </remarks>
internal static class KittyCsiUKeyboardDecoder
{
    private const char FieldSeparator = ';';
    private const char SubFieldSeparator = ':';
    private const int MaxFieldCount = 3;
    private const int KeyCodeField = 0;
    private const int ModifierField = 1;
    private const int TextField = 2;
    private const int DefaultModifierValue = 1;
    private const int PressEventType = 1;
    private const int RepeatEventType = 2;
    private const int ReleaseEventType = 3;
    private const int FirstModifierKeyCode = 57441;
    private const int LastModifierKeyCode = 57454;

    /// <summary>
    /// Attempts to parse <c>[keycode[:alternates][;[modifiers][:event][;text]]]u</c>.
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

        var fields = text[1..^1].Split(FieldSeparator);
        if (fields.Length > MaxFieldCount)
            return false;

        if (!TryParseKeyCode(fields[KeyCodeField], out var keycode))
            return false;

        var modValue = DefaultModifierValue;
        var eventType = PressEventType;
        if (fields.Length > ModifierField
            && !TryParseModifierField(fields[ModifierField], out modValue, out eventType))
            return false;

        string? associatedText = null;
        if (fields.Length > TextField && !TryDecodeAssociatedText(fields[TextField], out associatedText))
            return false;

        if (!TryMapEventType(eventType, out var phase))
            return true;

        // Modifier-key-alone events add noise without actionable ConsoleKey equivalents.
        if (keycode is >= FirstModifierKeyCode and <= LastModifierKeyCode)
            return true;

        var modifiers = ToKeyModifiers(modValue);
        var (key, fallbackText) = MapKeycodeToKeyStroke(keycode);
        var textOutput = associatedText ?? fallbackText;
        if (key == TerminaKey.None && textOutput is null)
            return true;

        result = new KeyStroke(key, modifiers, phase, textOutput, associatedText);
        return true;
    }

    private static bool TryParseKeyCode(string field, out int keycode)
    {
        var alternates = field.IndexOf(SubFieldSeparator);
        if (alternates >= 0)
            field = field[..alternates];

        return TryParseNumber(field, out keycode);
    }

    private static bool TryParseModifierField(string field, out int modValue, out int eventType)
    {
        modValue = DefaultModifierValue;
        eventType = PressEventType;

        var modPart = field;
        var separator = field.IndexOf(SubFieldSeparator);
        if (separator >= 0)
        {
            modPart = field[..separator];
            if (!TryParseNumber(field[(separator + 1)..], out eventType))
                return false;
        }

        if (modPart.Length == 0)
            return true;

        return TryParseNumber(modPart, out modValue) && modValue >= DefaultModifierValue;
    }

    private static bool TryDecodeAssociatedText(string field, out string? text)
    {
        text = null;
        if (field.Length == 0)
            return true;

        var builder = new StringBuilder();
        foreach (var part in field.Split(SubFieldSeparator))
        {
            if (!TryParseNumber(part, out var codePoint) || !Rune.IsValid(codePoint))
                return false;

            var rune = new Rune(codePoint);
            if (Rune.IsControl(rune))
                return false;

            builder.Append(rune);
        }

        text = builder.ToString();
        return true;
    }

    private static bool TryParseNumber(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static bool TryMapEventType(int eventType, out KeyEventPhase phase)
    {
        phase = eventType switch
        {
            PressEventType => KeyEventPhase.Press,
            RepeatEventType => KeyEventPhase.Repeat,
            ReleaseEventType => KeyEventPhase.Release,
            _ => KeyEventPhase.Press,
        };

        return eventType is PressEventType or RepeatEventType or ReleaseEventType;
    }

    private static KeyModifiers ToKeyModifiers(int modValue)
    {
        var modBits = modValue - 1;
        var modifiers = KeyModifiers.None;
        if ((modBits & 1) != 0) modifiers |= KeyModifiers.Shift;
        if ((modBits & 2) != 0) modifiers |= KeyModifiers.Alt;
        if ((modBits & 4) != 0) modifiers |= KeyModifiers.Control;
        return modifiers;
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
