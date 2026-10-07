// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Input;

namespace Termina.Tests.Input;

public class KittyCsiUKeyboardDecoderTests
{
    [Theory]
    [InlineData("[13;5u", ConsoleKey.Enter, "\r", false, false, true)]
    [InlineData("[13;2u", ConsoleKey.Enter, "\r", true, false, false)]
    [InlineData("[13;6u", ConsoleKey.Enter, "\r", true, false, true)]
    [InlineData("[13u", ConsoleKey.Enter, "\r", false, false, false)]
    [InlineData("[97;2;65u", ConsoleKey.A, "A", true, false, false)]
    [InlineData("[49;2;33u", ConsoleKey.D1, "!", true, false, false)]
    [InlineData("[0;1;229u", ConsoleKey.None, "å", false, false, false)]
    [InlineData("[0;1;72:105u", ConsoleKey.None, "Hi", false, false, false)]
    public void TryDecode_StandardSequence_ReturnsKeyStroke(
        string sequence,
        ConsoleKey expectedKey,
        string expectedText,
        bool shift,
        bool alt,
        bool ctrl)
    {
        var decoded = KittyCsiUKeyboardDecoder.TryDecode(sequence, out var keyStroke);

        Assert.True(decoded);
        Assert.NotNull(keyStroke);
        Assert.Equal(ToTerminaKey(expectedKey), keyStroke!.Key);
        Assert.Equal(expectedText, keyStroke.Text);
        Assert.Equal(KeyEventPhase.Press, keyStroke.Phase);
        Assert.Equal(shift, keyStroke.Modifiers.HasFlag(KeyModifiers.Shift));
        Assert.Equal(alt, keyStroke.Modifiers.HasFlag(KeyModifiers.Alt));
        Assert.Equal(ctrl, keyStroke.Modifiers.HasFlag(KeyModifiers.Control));
    }

    [Theory]
    [InlineData("[57352u", ConsoleKey.UpArrow, false)]
    [InlineData("[57352;2u", ConsoleKey.UpArrow, true)]
    [InlineData("[57368u", ConsoleKey.F5, false)]
    [InlineData("[57348u", ConsoleKey.Insert, false)]
    public void TryDecode_PuaFunctionalKey_ReturnsKeyStroke(
        string sequence,
        ConsoleKey expectedKey,
        bool shift)
    {
        var decoded = KittyCsiUKeyboardDecoder.TryDecode(sequence, out var keyStroke);

        Assert.True(decoded);
        Assert.NotNull(keyStroke);
        Assert.Equal(ToTerminaKey(expectedKey), keyStroke!.Key);
        Assert.Null(keyStroke.Text);
        Assert.Equal(KeyEventPhase.Press, keyStroke.Phase);
        Assert.Equal(shift, keyStroke.Modifiers.HasFlag(KeyModifiers.Shift));
    }

    [Theory]
    [InlineData("[57441u")]
    [InlineData("[57448u")]
    [InlineData("[57441;1:3u")]
    [InlineData("[20000u")]
    public void TryDecode_SwallowedSequence_ReturnsTrueWithoutEvent(string sequence)
    {
        var decoded = KittyCsiUKeyboardDecoder.TryDecode(sequence, out var keyStroke);

        Assert.True(decoded);
        Assert.Null(keyStroke);
    }

    [Fact]
    public void TryDecode_ReleaseEvent_ReturnsKeyStrokeWithReleasePhase()
    {
        var decoded = KittyCsiUKeyboardDecoder.TryDecode("[97;1:3u", out var keyStroke);

        Assert.True(decoded);
        Assert.NotNull(keyStroke);
        Assert.Equal(TerminaKey.A, keyStroke!.Key);
        Assert.Equal("a", keyStroke.Text);
        Assert.Equal(KeyEventPhase.Release, keyStroke.Phase);
    }

    [Theory]
    [InlineData("[bad;1u")]
    [InlineData("[13;0u")]
    [InlineData("[13;badu")]
    [InlineData("[13;1:badu")]
    [InlineData("[13;1x")]
    [InlineData("13;1u")]
    public void TryDecode_MalformedSequence_ReturnsFalse(string sequence)
    {
        var decoded = KittyCsiUKeyboardDecoder.TryDecode(sequence, out var keyStroke);

        Assert.False(decoded);
        Assert.Null(keyStroke);
    }

    private static TerminaKey ToTerminaKey(ConsoleKey key) => key switch
    {
        ConsoleKey.Escape => TerminaKey.Escape,
        ConsoleKey.Enter => TerminaKey.Enter,
        ConsoleKey.Tab => TerminaKey.Tab,
        ConsoleKey.Backspace => TerminaKey.Backspace,
        ConsoleKey.Spacebar => TerminaKey.Space,
        ConsoleKey.Insert => TerminaKey.Insert,
        ConsoleKey.Delete => TerminaKey.Delete,
        ConsoleKey.Home => TerminaKey.Home,
        ConsoleKey.End => TerminaKey.End,
        ConsoleKey.PageUp => TerminaKey.PageUp,
        ConsoleKey.PageDown => TerminaKey.PageDown,
        ConsoleKey.UpArrow => TerminaKey.UpArrow,
        ConsoleKey.DownArrow => TerminaKey.DownArrow,
        ConsoleKey.LeftArrow => TerminaKey.LeftArrow,
        ConsoleKey.RightArrow => TerminaKey.RightArrow,
        >= ConsoleKey.A and <= ConsoleKey.Z => TerminaKey.A + (key - ConsoleKey.A),
        >= ConsoleKey.D0 and <= ConsoleKey.D9 => TerminaKey.D0 + (key - ConsoleKey.D0),
        >= ConsoleKey.F1 and <= ConsoleKey.F12 => TerminaKey.F1 + (key - ConsoleKey.F1),
        _ => TerminaKey.None,
    };
}
