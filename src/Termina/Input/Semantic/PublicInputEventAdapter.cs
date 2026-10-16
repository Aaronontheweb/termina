// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Adapts internal semantic input events back to Termina's current public input event surface.
/// This preserves existing application and component behavior while the internal pipeline evolves.
/// </summary>
internal static class PublicInputEventAdapter
{
    public static IReadOnlyList<IInputEvent> Adapt(TerminaInputEvent evt) => evt switch
    {
        KeyStroke keyStroke => AdaptKeyStroke(keyStroke),
        TextEntered textEntered => AdaptTextEntered(textEntered),
        PointerInput pointerInput => AdaptPointerInput(pointerInput),
        PasteInput pasteInput => [new PasteEvent(pasteInput.Text)],
        TerminalResizeInput resizeInput => [new ResizeEvent(resizeInput.Width, resizeInput.Height)],
        TerminalReplyInput => [],
        _ => [],
    };

    private static IReadOnlyList<IInputEvent> AdaptKeyStroke(KeyStroke keyStroke)
    {
        if (keyStroke.Phase != KeyEventPhase.Press)
            return [];

        var consoleKey = keyStroke.Key.ToConsoleKey();
        var keyChar = KeyCharFromAssociatedText(keyStroke.Text, keyStroke.Key);

        if (consoleKey == ConsoleKey.None && keyChar == '\0')
            return [];

        return [new KeyPressed(new ConsoleKeyInfo(
            keyChar,
            consoleKey,
            keyStroke.Modifiers.HasFlag(KeyModifiers.Shift),
            keyStroke.Modifiers.HasFlag(KeyModifiers.Alt),
            keyStroke.Modifiers.HasFlag(KeyModifiers.Control)))];
    }

    private static IReadOnlyList<IInputEvent> AdaptTextEntered(TextEntered textEntered)
    {
        if (string.IsNullOrEmpty(textEntered.Text))
            return [];

        var events = new List<IInputEvent>(textEntered.Text.Length);
        foreach (var c in textEntered.Text)
        {
            events.Add(new KeyPressed(new ConsoleKeyInfo(
                c,
                CharToConsoleKey(c),
                shift: c is >= 'A' and <= 'Z',
                alt: false,
                control: false)));
        }

        return events;
    }

    private static IReadOnlyList<IInputEvent> AdaptPointerInput(PointerInput pointerInput)
    {
        if (pointerInput.Action == PointerAction.Wheel)
        {
            return pointerInput.Button switch
            {
                MouseButton.WheelUp => [CreateMouseScrollEvent(+1, pointerInput)],
                MouseButton.WheelDown => [CreateMouseScrollEvent(-1, pointerInput)],
                _ => [],
            };
        }

        return [new MouseEvent(
            pointerInput.X,
            pointerInput.Y,
            pointerInput.Button,
            pointerInput.Action.ToMouseEventType(),
            pointerInput.Modifiers.ToConsoleModifiers())];
    }

    private static MouseScrollEvent CreateMouseScrollEvent(int delta, PointerInput pointerInput) => new(delta)
    {
        X = pointerInput.X > 0 ? pointerInput.X - 1 : null,
        Y = pointerInput.Y > 0 ? pointerInput.Y - 1 : null,
        Modifiers = pointerInput.Modifiers.ToConsoleModifiers(),
    };

    private static char KeyCharFromAssociatedText(string? text, TerminaKey key) =>
        string.IsNullOrEmpty(text) ? DefaultKeyChar(key) : text[0];

    private static char DefaultKeyChar(TerminaKey key) => key switch
    {
        TerminaKey.Escape => '\x1b',
        TerminaKey.Enter => '\r',
        TerminaKey.Tab => '\t',
        TerminaKey.Backspace => '\b',
        TerminaKey.Space => ' ',
        _ => '\0',
    };

    private static ConsoleKey ToConsoleKey(this TerminaKey key) => key switch
    {
        TerminaKey.None => ConsoleKey.None,
        TerminaKey.Escape => ConsoleKey.Escape,
        TerminaKey.Enter => ConsoleKey.Enter,
        TerminaKey.Tab => ConsoleKey.Tab,
        TerminaKey.Backspace => ConsoleKey.Backspace,
        TerminaKey.Space => ConsoleKey.Spacebar,
        TerminaKey.Insert => ConsoleKey.Insert,
        TerminaKey.Delete => ConsoleKey.Delete,
        TerminaKey.Home => ConsoleKey.Home,
        TerminaKey.End => ConsoleKey.End,
        TerminaKey.PageUp => ConsoleKey.PageUp,
        TerminaKey.PageDown => ConsoleKey.PageDown,
        TerminaKey.UpArrow => ConsoleKey.UpArrow,
        TerminaKey.DownArrow => ConsoleKey.DownArrow,
        TerminaKey.LeftArrow => ConsoleKey.LeftArrow,
        TerminaKey.RightArrow => ConsoleKey.RightArrow,
        >= TerminaKey.A and <= TerminaKey.Z => ConsoleKey.A + (key - TerminaKey.A),
        >= TerminaKey.D0 and <= TerminaKey.D9 => ConsoleKey.D0 + (key - TerminaKey.D0),
        >= TerminaKey.F1 and <= TerminaKey.F24 => ConsoleKey.F1 + (key - TerminaKey.F1),
        _ => ConsoleKey.None,
    };

    private static ConsoleKey CharToConsoleKey(char c) => c switch
    {
        >= 'a' and <= 'z' => ConsoleKey.A + (c - 'a'),
        >= 'A' and <= 'Z' => ConsoleKey.A + (c - 'A'),
        >= '0' and <= '9' => ConsoleKey.D0 + (c - '0'),
        ' ' => ConsoleKey.Spacebar,
        _ => ConsoleKey.None,
    };

    private static MouseEventType ToMouseEventType(this PointerAction action) => action switch
    {
        PointerAction.Press => MouseEventType.Press,
        PointerAction.Release => MouseEventType.Release,
        PointerAction.Drag => MouseEventType.Drag,
        PointerAction.Move => MouseEventType.Move,
        PointerAction.Wheel => MouseEventType.Scroll,
        _ => MouseEventType.Move,
    };
}
