// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Internal canonical input event produced by terminal protocol decoders before adapting
/// back to Termina's public input event surface.
/// </summary>
internal abstract record TerminaInputEvent;

/// <summary>
/// Semantic key event independent of a specific terminal byte sequence or
/// <see cref="ConsoleKeyInfo"/> transport shape.
/// </summary>
/// <param name="Key">The physical key.</param>
/// <param name="Modifiers">The modifier keys.</param>
/// <param name="Phase">The press, repeat, or release phase.</param>
/// <param name="Text">The character of the key, from the terminal text or from the key code.</param>
/// <param name="AssociatedText">The text that the terminal reports the key produces, if any.</param>
internal sealed record KeyStroke(
    TerminaKey Key,
    KeyModifiers Modifiers = KeyModifiers.None,
    KeyEventPhase Phase = KeyEventPhase.Press,
    string? Text = null,
    string? AssociatedText = null) : TerminaInputEvent;

/// <summary>
/// Text input that should be handled as text rather than as a physical key.
/// </summary>
internal sealed record TextEntered(string Text) : TerminaInputEvent;

/// <summary>
/// Semantic pointer input for mouse-compatible terminal protocols.
/// </summary>
internal sealed record PointerInput(
    PointerAction Action,
    int X,
    int Y,
    MouseButton Button,
    KeyModifiers Modifiers = KeyModifiers.None) : TerminaInputEvent;

/// <summary>
/// Bracketed paste payload as a single semantic event.
/// </summary>
internal sealed record PasteInput(string Text) : TerminaInputEvent;

/// <summary>
/// Terminal viewport resize as a semantic input event.
/// </summary>
internal sealed record TerminalResizeInput(int Width, int Height) : TerminaInputEvent;

/// <summary>
/// Terminal-originated response such as cursor position, device attributes, or focus reports.
/// This is intentionally inert in the current compatibility adapter so future decoders can
/// identify terminal replies without leaking them as user input.
/// </summary>
internal sealed record TerminalReplyInput(string Sequence) : TerminaInputEvent;
