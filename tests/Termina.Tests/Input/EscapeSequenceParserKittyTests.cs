// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Input;

namespace Termina.Tests.Input;

/// <summary>
/// Tests for the kitty-keyboard-protocol additions to <see cref="EscapeSequenceParser"/>:
/// CSI second-form arrow / function keys (<c>CSI 1;&lt;mods&gt; [ABCDFHPQRS]</c>), event-type
/// subfield handling, kitty PUA functional keycodes in CSI-u form, modifier-alone swallowing,
    /// and the <see cref="EscapeSequenceParser.KittyReportAllKeysVisible"/> mode-flag interaction with
/// bare <c>CSI A/B</c> / <c>SS3 OA/B</c> wheel routing.
/// </summary>
public class EscapeSequenceParserKittyTests
{
    private static ConsoleKeyInfo Key(char c) => new(c, ConsoleKey.None, false, false, false);
    private static ConsoleKeyInfo EscKey() => new('\x1b', ConsoleKey.Escape, false, false, false);

    private static List<IInputEvent> FeedString(EscapeSequenceParser parser, string s)
    {
        var all = new List<IInputEvent>();
        foreach (var c in s)
            all.AddRange(parser.Process(c == '\x1b' ? EscKey() : Key(c)));
        return all;
    }

    // --- Bare CSI arrow routing depends on KittyReportAllKeysVisible ---

    [Fact]
    public void BareCsiUp_WhenKittyInactive_EmitsKeyPressedBeforeDeckmConfirmed()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = false };
        var events = FeedString(parser, "\x1b[A");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.UpArrow, press.KeyInfo.Key);
    }

    [Fact]
    public void BareCsiUp_WhenKittyActive_EmitsKeyPressedUpArrow()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[A");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.UpArrow, press.KeyInfo.Key);
    }

    [Fact]
    public void BareCsiDown_WhenKittyActive_EmitsKeyPressedDownArrow()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[B");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.DownArrow, press.KeyInfo.Key);
    }

    [Theory]
    [InlineData('C', ConsoleKey.RightArrow)]
    [InlineData('D', ConsoleKey.LeftArrow)]
    [InlineData('H', ConsoleKey.Home)]
    [InlineData('F', ConsoleKey.End)]
    [InlineData('P', ConsoleKey.F1)]
    [InlineData('Q', ConsoleKey.F2)]
    [InlineData('R', ConsoleKey.F3)]
    [InlineData('S', ConsoleKey.F4)]
    public void BareCsi_KittyActive_AllFunctionalFinals(char final, ConsoleKey expected)
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, $"\x1b[{final}");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(expected, press.KeyInfo.Key);
    }

    // --- SS3 routing inverts when kitty active (wheel under ?1007h+DECCKM) ---

    [Fact]
    public void Ss3OA_WhenKittyInactive_EmitsKeyPressedUpArrow()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = false };
        var events = FeedString(parser, "\x1bOA");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.UpArrow, press.KeyInfo.Key);
    }

    [Fact]
    public void Ss3OA_WhenKittyActive_EmitsMouseScrollUp()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1bOA");
        Assert.Single(events);
        var scroll = Assert.IsType<MouseScrollEvent>(events[0]);
        Assert.Equal(+1, scroll.Delta);
    }

    [Fact]
    public void Ss3OB_WhenKittyActive_EmitsMouseScrollDown()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1bOB");
        Assert.Single(events);
        var scroll = Assert.IsType<MouseScrollEvent>(events[0]);
        Assert.Equal(-1, scroll.Delta);
    }

    [Fact]
    public void Ss3OC_WhenKittyActive_StillEmitsRightArrow()
    {
        // Wheel has no horizontal axis — SS3 OC/OD remain real arrows even with kitty active.
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1bOC");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.RightArrow, press.KeyInfo.Key);
    }

    // --- Kitty "second form" CSI 1;<mods> [ABCDFHPQRS] ---

    [Fact]
    public void SecondForm_ShiftUp_EmitsUpArrowWithShift()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[1;2A");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.UpArrow, press.KeyInfo.Key);
        Assert.True((press.KeyInfo.Modifiers & ConsoleModifiers.Shift) != 0);
        Assert.Equal((ConsoleModifiers)0, press.KeyInfo.Modifiers & ~ConsoleModifiers.Shift);
    }

    [Fact]
    public void SecondForm_CtrlDown_EmitsDownArrowWithControl()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[1;5B");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.DownArrow, press.KeyInfo.Key);
        Assert.True((press.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0);
    }

    [Fact]
    public void SecondForm_AltShiftCtrl_AllModifiersCombined()
    {
        // 1 + shift(1) + alt(2) + ctrl(4) = 8
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[1;8A");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.UpArrow, press.KeyInfo.Key);
        Assert.True((press.KeyInfo.Modifiers & ConsoleModifiers.Shift) != 0);
        Assert.True((press.KeyInfo.Modifiers & ConsoleModifiers.Alt) != 0);
        Assert.True((press.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0);
    }

    [Fact]
    public void SecondForm_ReleaseEvent_IsSwallowed()
    {
        // CSI 1;<mods>:3 X  — event type 3 = release
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[1;1:3A");
        Assert.Empty(events);
    }

    [Fact]
    public void SecondForm_RepeatEvent_IsSwallowed()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[1;1:2A");
        Assert.Empty(events);
    }

    [Fact]
    public void SecondForm_ExplicitPressEvent_IsEmitted()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[1;1:1A");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.UpArrow, press.KeyInfo.Key);
    }

    // --- CSI u with kitty PUA functional keycodes ---

    [Fact]
    public void CsiU_PuaUpArrow_NoMods_EmitsUpArrow()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[57352u");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.UpArrow, press.KeyInfo.Key);
    }

    [Fact]
    public void CsiU_PuaUpArrow_WithShift_EmitsUpArrowShift()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[57352;2u");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.UpArrow, press.KeyInfo.Key);
        Assert.True((press.KeyInfo.Modifiers & ConsoleModifiers.Shift) != 0);
    }

    [Fact]
    public void CsiU_PuaF5_EmitsF5()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[57368u"); // 57364 + 4 = F5
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.F5, press.KeyInfo.Key);
    }

    [Fact]
    public void CsiU_PuaModifierAlone_LeftShift_IsSwallowed()
    {
        // Kitty emits 57441 for the Left Shift key when report_all_keys is on.
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[57441u");
        Assert.Empty(events);
    }

    [Fact]
    public void CsiU_PuaModifierAlone_RightControl_IsSwallowed()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[57448u");
        Assert.Empty(events);
    }

    [Fact]
    public void CsiU_PuaModifierAlone_Release_IsSwallowed()
    {
        // 57441 release with event type 3 — exercises both the modifier-alone and
        // release-event swallowing paths.
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[57441;1:3u");
        Assert.Empty(events);
    }

    [Fact]
    public void CsiU_AsciiKey_ReleaseEvent_IsSwallowed()
    {
        // CSI 97;1:3 u → 'a' release. Should NOT emit.
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[97;1:3u");
        Assert.Empty(events);
    }

    [Fact]
    public void CsiU_AsciiKey_WithTextSubfield_IsParsed()
    {
        // CSI 97;2;65 u → 'A' (shift+a, associated text 65='A').
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        var events = FeedString(parser, "\x1b[97;2;65u");
        Assert.Single(events);
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.A, press.KeyInfo.Key);
        Assert.Equal('A', press.KeyInfo.KeyChar);
        Assert.True((press.KeyInfo.Modifiers & ConsoleModifiers.Shift) != 0);
    }

    // --- CSI arrows are keyboard keys until DECCKM is confirmed ---

    [Fact]
    public void ExistingBehavior_BareCsiB_KittyInactive_EmitsKeyPressedBeforeDeckmConfirmed()
    {
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = false };
        var events = FeedString(parser, "\x1b[B");
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.DownArrow, press.KeyInfo.Key);
    }

    [Fact]
    public void BareCsiB_AfterSs3Arrow_EmitsScrollDown()
    {
        // Once an SS3 arrow key arrives (proving DECCKM is honored), CSI A/B become wheel.
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = false };
        FeedString(parser, "\x1bOA"); // SS3 Up — confirms DECCKM
        var events = FeedString(parser, "\x1b[B");
        var scroll = Assert.IsType<MouseScrollEvent>(events[0]);
        Assert.Equal(-1, scroll.Delta);
    }

    [Fact]
    public void BareCsiB_AfterSs3FKey_StillEmitsKeyPressed()
    {
        // SS3 F1-F4 use SS3 encoding as a VT220 legacy independent of DECCKM.
        // They must not false-positive the DECCKM detection.
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = false };
        FeedString(parser, "\x1bOP"); // SS3 F1 — should NOT confirm DECCKM
        var events = FeedString(parser, "\x1b[B");
        var press = Assert.IsType<KeyPressed>(events[0]);
        Assert.Equal(ConsoleKey.DownArrow, press.KeyInfo.Key);
    }
}
