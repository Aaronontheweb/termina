// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Input;

namespace Termina.Tests.Input;

public class PublicInputEventAdapterTests
{
    [Fact]
    public void KeyStrokePress_AdaptsToKeyPressed()
    {
        var events = PublicInputEventAdapter.Adapt(new KeyStroke(
            TerminaKey.PageUp,
            KeyModifiers.Control | KeyModifiers.Shift));

        var pressed = Assert.IsType<KeyPressed>(Assert.Single(events));
        Assert.Equal(ConsoleKey.PageUp, pressed.KeyInfo.Key);
        Assert.Equal('\0', pressed.KeyInfo.KeyChar);
        Assert.True(pressed.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control));
        Assert.True(pressed.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));
    }

    [Fact]
    public void KeyStrokeNonPress_DoesNotEmitPublicKeyEvent()
    {
        var repeatEvents = PublicInputEventAdapter.Adapt(new KeyStroke(TerminaKey.Enter, Phase: KeyEventPhase.Repeat));
        var releaseEvents = PublicInputEventAdapter.Adapt(new KeyStroke(TerminaKey.Enter, Phase: KeyEventPhase.Release));

        Assert.Empty(repeatEvents);
        Assert.Empty(releaseEvents);
    }

    [Fact]
    public void KeyStroke_WithAssociatedText_UsesFirstTextCharacterAsKeyChar()
    {
        var events = PublicInputEventAdapter.Adapt(new KeyStroke(
            TerminaKey.Enter,
            KeyModifiers.Control,
            Text: "\r"));

        var pressed = Assert.IsType<KeyPressed>(Assert.Single(events));
        Assert.Equal(ConsoleKey.Enter, pressed.KeyInfo.Key);
        Assert.Equal('\r', pressed.KeyInfo.KeyChar);
        Assert.True(pressed.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    [Fact]
    public void KeyStroke_ControlKeysUseConsoleCompatibleDefaultKeyChars()
    {
        var cases = new[]
        {
            (Key: TerminaKey.Escape, ConsoleKey: ConsoleKey.Escape, KeyChar: '\x1b'),
            (Key: TerminaKey.Enter, ConsoleKey: ConsoleKey.Enter, KeyChar: '\r'),
            (Key: TerminaKey.Tab, ConsoleKey: ConsoleKey.Tab, KeyChar: '\t'),
            (Key: TerminaKey.Backspace, ConsoleKey: ConsoleKey.Backspace, KeyChar: '\b'),
            (Key: TerminaKey.Space, ConsoleKey: ConsoleKey.Spacebar, KeyChar: ' '),
        };

        foreach (var testCase in cases)
        {
            var events = PublicInputEventAdapter.Adapt(new KeyStroke(testCase.Key));

            var pressed = Assert.IsType<KeyPressed>(Assert.Single(events));
            Assert.Equal(testCase.ConsoleKey, pressed.KeyInfo.Key);
            Assert.Equal(testCase.KeyChar, pressed.KeyInfo.KeyChar);
        }
    }

    [Fact]
    public void KeyStroke_CanRepresentModifierBearingPrintableKey()
    {
        var events = PublicInputEventAdapter.Adapt(new KeyStroke(
            TerminaKey.A,
            KeyModifiers.Control,
            Text: "\x01"));

        var pressed = Assert.IsType<KeyPressed>(Assert.Single(events));
        Assert.Equal(ConsoleKey.A, pressed.KeyInfo.Key);
        Assert.Equal('\x01', pressed.KeyInfo.KeyChar);
        Assert.True(pressed.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    [Fact]
    public void KeyStroke_AdaptsDigitKeyIdentity()
    {
        var events = PublicInputEventAdapter.Adapt(new KeyStroke(TerminaKey.D7, Text: "7"));

        var pressed = Assert.IsType<KeyPressed>(Assert.Single(events));
        Assert.Equal(ConsoleKey.D7, pressed.KeyInfo.Key);
        Assert.Equal('7', pressed.KeyInfo.KeyChar);
    }

    [Fact]
    public void TextEntered_AdaptsEachCharacterToKeyPressed()
    {
        var events = PublicInputEventAdapter.Adapt(new TextEntered("Az1!"));

        Assert.Equal(4, events.Count);
        AssertKey(events[0], ConsoleKey.A, 'A', ConsoleModifiers.Shift);
        AssertKey(events[1], ConsoleKey.Z, 'z', default);
        AssertKey(events[2], ConsoleKey.D1, '1', default);
        AssertKey(events[3], ConsoleKey.None, '!', default);
    }

    [Theory]
    [InlineData(MouseButton.WheelUp, +1)]
    [InlineData(MouseButton.WheelDown, -1)]
    public void PointerWheel_AdaptsToMouseScrollEvent(MouseButton button, int expectedDelta)
    {
        var events = PublicInputEventAdapter.Adapt(new PointerInput(
            PointerAction.Wheel,
            X: 3,
            Y: 4,
            button,
            KeyModifiers.Control));

        var scroll = Assert.IsType<MouseScrollEvent>(Assert.Single(events));
        Assert.Equal(expectedDelta, scroll.Delta);
        Assert.Equal(2, scroll.X);
        Assert.Equal(3, scroll.Y);
        Assert.Equal(ConsoleModifiers.Control, scroll.Modifiers);
    }

    [Fact]
    public void PointerPress_AdaptsToMouseEvent()
    {
        var events = PublicInputEventAdapter.Adapt(new PointerInput(
            PointerAction.Press,
            X: 3,
            Y: 4,
            MouseButton.Left,
            KeyModifiers.Alt));

        var mouse = Assert.IsType<MouseEvent>(Assert.Single(events));
        Assert.Equal(3, mouse.X);
        Assert.Equal(4, mouse.Y);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(MouseEventType.Press, mouse.EventType);
        Assert.Equal(ConsoleModifiers.Alt, mouse.Modifiers);
    }

    [Fact]
    public void PasteInput_AdaptsToPasteEvent()
    {
        var events = PublicInputEventAdapter.Adapt(new PasteInput("line 1\nline 2"));

        var paste = Assert.IsType<PasteEvent>(Assert.Single(events));
        Assert.Equal("line 1\nline 2", paste.Content);
    }

    [Fact]
    public void TerminalResizeInput_AdaptsToResizeEvent()
    {
        var events = PublicInputEventAdapter.Adapt(new TerminalResizeInput(120, 40));

        var resize = Assert.IsType<ResizeEvent>(Assert.Single(events));
        Assert.Equal(120, resize.Width);
        Assert.Equal(40, resize.Height);
    }

    [Fact]
    public void TerminalReplyInput_DoesNotEmitPublicEvent()
    {
        var events = PublicInputEventAdapter.Adapt(new TerminalReplyInput("[12;40R"));

        Assert.Empty(events);
    }

    private static void AssertKey(
        IInputEvent evt,
        ConsoleKey key,
        char keyChar,
        ConsoleModifiers modifiers)
    {
        var pressed = Assert.IsType<KeyPressed>(evt);
        Assert.Equal(key, pressed.KeyInfo.Key);
        Assert.Equal(keyChar, pressed.KeyInfo.KeyChar);
        Assert.Equal(modifiers, pressed.KeyInfo.Modifiers);
    }
}
