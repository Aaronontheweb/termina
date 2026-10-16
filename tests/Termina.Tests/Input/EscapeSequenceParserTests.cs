// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Input;

namespace Termina.Tests.Input;

/// <summary>
/// Tests for <see cref="EscapeSequenceParser"/> — the component that detects bracketed paste,
/// SGR mouse events, and standalone ESC keys from raw Console.ReadKey output.
/// </summary>
public class EscapeSequenceParserTests
{
    // Helper: create ConsoleKeyInfo for a regular character
    private static ConsoleKeyInfo Key(char c, ConsoleKey key = ConsoleKey.None, bool ctrl = false, bool shift = false, bool alt = false)
        => new(c, key, shift, alt, ctrl);

    private static ConsoleKeyInfo EscKey()
        => new('\x1b', ConsoleKey.Escape, false, false, false);

    // Feed a string of characters through the parser as individual keys
    private static List<IInputEvent> FeedString(EscapeSequenceParser parser, string chars)
    {
        var all = new List<IInputEvent>();
        foreach (var c in chars)
            all.AddRange(parser.Process(Key(c)));
        return all;
    }

    // Build a full SGR mouse sequence: ESC [ < button ; x ; y M
    private static IEnumerable<ConsoleKeyInfo> SgrMouseSequence(int button, int x, int y, char terminator = 'M')
    {
        yield return EscKey();
        foreach (var c in $"[<{button};{x};{y}{terminator}")
            yield return Key(c);
    }

    private static List<IInputEvent> FeedSequence(EscapeSequenceParser parser, IEnumerable<ConsoleKeyInfo> keys)
    {
        var all = new List<IInputEvent>();
        foreach (var k in keys)
            all.AddRange(parser.Process(k));
        return all;
    }

    // --- Regular key handling ---

    [Fact]
    public void RegularCharacter_EmitsKeyPressed()
    {
        var parser = new EscapeSequenceParser();
        var events = parser.Process(Key('a', ConsoleKey.A));
        var pressed = Assert.Single(events);
        var kp = Assert.IsType<KeyPressed>(pressed);
        Assert.Equal('a', kp.KeyInfo.KeyChar);
    }

    [Fact]
    public void ArrowKey_EmitsKeyPressed()
    {
        var parser = new EscapeSequenceParser();
        var events = parser.Process(Key('\0', ConsoleKey.UpArrow));
        Assert.Single(events);
        Assert.IsType<KeyPressed>(events[0]);
    }

    [Fact]
    public void ConsoleRecord_ShiftEnter_PreservesShiftModifier()
    {
        var parser = new EscapeSequenceParser();

        var pressed = Assert.IsType<KeyPressed>(Assert.Single(parser.Process(
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: true, alt: false, control: false))));

        Assert.Equal(ConsoleKey.Enter, pressed.KeyInfo.Key);
        Assert.True(pressed.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));
    }

    // --- Mouse scroll up (button 64) ---

    [Fact]
    public void SgrScrollUp_EmitsMouseScrollEventPositive()
    {
        var parser = new EscapeSequenceParser();
        var events = FeedSequence(parser, SgrMouseSequence(64, 5, 10));

        var scroll = Assert.Single(events);
        var mse = Assert.IsType<MouseScrollEvent>(scroll);
        Assert.Equal(+1, mse.Delta);
        Assert.Equal(4, mse.X);
        Assert.Equal(9, mse.Y);
    }

    [Fact]
    public void SgrScrollUp_WithLargeCoordinates_EmitsMouseScrollEvent()
    {
        var parser = new EscapeSequenceParser();
        var events = FeedSequence(parser, SgrMouseSequence(64, 200, 50));

        var scroll = Assert.Single(events);
        Assert.IsType<MouseScrollEvent>(scroll);
        Assert.Equal(+1, ((MouseScrollEvent)scroll).Delta);
    }

    // --- Mouse scroll down (button 65) ---

    [Fact]
    public void SgrScrollDown_EmitsMouseScrollEventNegative()
    {
        var parser = new EscapeSequenceParser();
        var events = FeedSequence(parser, SgrMouseSequence(65, 5, 10));

        var scroll = Assert.Single(events);
        var mse = Assert.IsType<MouseScrollEvent>(scroll);
        Assert.Equal(-1, mse.Delta);
    }

    // --- Mouse click events silently consumed ---

    [Fact]
    public void CsiArrowUp_EmitsKeyPressedBeforeDeckmConfirmed()
    {
        // Before DECCKM is confirmed (no SS3 arrow key seen yet), CSI A/B are treated
        // as keyboard arrows — the parser cannot safely assume the terminal honored DECCKM.
        var parser = new EscapeSequenceParser();
        var events = FeedSequence(parser, new[] { EscKey(), Key('['), Key('A') });

        var press = Assert.Single(events);
        var kp = Assert.IsType<KeyPressed>(press);
        Assert.Equal(ConsoleKey.UpArrow, kp.KeyInfo.Key);
    }

    [Fact]
    public void CsiArrowDown_EmitsKeyPressedBeforeDeckmConfirmed()
    {
        var parser = new EscapeSequenceParser();
        var events = FeedSequence(parser, new[] { EscKey(), Key('['), Key('B') });

        var press = Assert.Single(events);
        var kp = Assert.IsType<KeyPressed>(press);
        Assert.Equal(ConsoleKey.DownArrow, kp.KeyInfo.Key);
    }

    [Theory]
    [InlineData('A', ConsoleKey.UpArrow)]
    [InlineData('B', ConsoleKey.DownArrow)]
    [InlineData('C', ConsoleKey.RightArrow)]
    [InlineData('D', ConsoleKey.LeftArrow)]
    [InlineData('H', ConsoleKey.Home)]
    [InlineData('F', ConsoleKey.End)]
    public void Ss3Arrow_EmitsKeyPressed_WithMatchingConsoleKey(char terminator, ConsoleKey expected)
    {
        // Under raw-stdin input (UnixConsole), real arrow keys arrive as the SS3 sequence
        // ESC O A/B/C/D when the terminal is in cursor-key application mode (DECCKM, ?1h).
        // The parser must turn them back into a single KeyPressed so existing keyboard
        // handlers see a normal arrow keypress — NOT a MouseScrollEvent.
        var parser = new EscapeSequenceParser();
        var events = FeedSequence(parser, new[] { EscKey(), Key('O'), Key(terminator) });

        var ev = Assert.Single(events);
        var kp = Assert.IsType<KeyPressed>(ev);
        Assert.Equal(expected, kp.KeyInfo.Key);
    }

    [Fact]
    public void UnknownSs3Sequence_FlushesAsRawKeys_AndUnsticksParser()
    {
        var parser = new EscapeSequenceParser();

        var events = FeedSequence(parser, new[] { EscKey(), Key('O'), Key('Z') });

        Assert.False(parser.IsBufferingEscape);
        Assert.Equal(3, events.Count);
        Assert.Equal("\x1bOZ", new string(events.Select(e => ((KeyPressed)e).KeyInfo.KeyChar).ToArray()));
    }

    [Fact]
    public void SgrMouseClickPress_IsSilentlyConsumed()
    {
        var parser = new EscapeSequenceParser();
        // Button 0 + 'M' = left button press
        var events = FeedSequence(parser, SgrMouseSequence(0, 5, 10, 'M'));
        Assert.Empty(events);
    }

    [Fact]
    public void SgrMouseClickRelease_IsSilentlyConsumed()
    {
        var parser = new EscapeSequenceParser();
        // Button 0 + 'm' = left button release
        var events = FeedSequence(parser, SgrMouseSequence(0, 5, 10, 'm'));
        Assert.Empty(events);
    }

    [Fact]
    public void SgrMouseRightClick_IsSilentlyConsumed()
    {
        var parser = new EscapeSequenceParser();
        var events = FeedSequence(parser, SgrMouseSequence(2, 10, 20, 'M'));
        Assert.Empty(events);
    }

    [Fact]
    public void MultipleClicks_AllSilentlyConsumed()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        // Press + release
        events.AddRange(FeedSequence(parser, SgrMouseSequence(0, 5, 10, 'M')));
        events.AddRange(FeedSequence(parser, SgrMouseSequence(0, 5, 10, 'm')));
        Assert.Empty(events);
    }

    [Fact]
    public void ClickThenScroll_ClickConsumed_ScrollEmitted()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(FeedSequence(parser, SgrMouseSequence(0, 5, 10, 'M'))); // click
        events.AddRange(FeedSequence(parser, SgrMouseSequence(64, 5, 10)));      // scroll up

        var single = Assert.Single(events);
        var mse = Assert.IsType<MouseScrollEvent>(single);
        Assert.Equal(+1, mse.Delta);
    }

    // --- Parser state: not buffering after processing a complete sequence ---

    [Fact]
    public void AfterMouseEvent_ParserNotBuffering()
    {
        var parser = new EscapeSequenceParser();
        FeedSequence(parser, SgrMouseSequence(0, 5, 10, 'M'));
        Assert.False(parser.IsBufferingEscape);
    }

    [Fact]
    public void AfterScrollEvent_ParserNotBuffering()
    {
        var parser = new EscapeSequenceParser();
        FeedSequence(parser, SgrMouseSequence(64, 5, 10));
        Assert.False(parser.IsBufferingEscape);
    }

    // --- ESC timeout ---

    [Fact]
    public void StandaloneEsc_IsBuffering_UntilTimeout()
    {
        var parser = new EscapeSequenceParser();
        parser.Process(EscKey());

        // Before timeout: buffering, CheckEscapeTimeout returns null
        Assert.True(parser.IsBufferingEscape);
        Assert.Null(parser.CheckEscapeTimeout());
    }

    [Fact]
    public void StandaloneEsc_AfterTimeout_EmitsKeyPressed()
    {
        var tick = 0L;
        var parser = new EscapeSequenceParser(() => tick);

        parser.Process(EscKey());
        Assert.True(parser.IsBufferingEscape);

        tick += 60; // advance 60ms — past the 50ms threshold
        var flushed = parser.CheckEscapeTimeout();

        Assert.NotNull(flushed);
        Assert.Equal(ConsoleKey.Escape, flushed!.KeyInfo.Key);
        Assert.False(parser.IsBufferingEscape);
    }

    [Fact]
    public void StandaloneEsc_BeforeTimeout_CheckReturnsNull()
    {
        var tick = 0L;
        var parser = new EscapeSequenceParser(() => tick);

        parser.Process(EscKey());
        tick += 30; // only 30ms — below the 50ms threshold

        Assert.Null(parser.CheckEscapeTimeout());
        Assert.True(parser.IsBufferingEscape);
    }

    // --- Esc followed by non-bracket flushes both ---

    [Fact]
    public void EscThenNonBracket_FlushesEscAndProcessesKey()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        events.AddRange(parser.Process(Key('x', ConsoleKey.X)));

        Assert.Equal(2, events.Count);
        Assert.Equal(ConsoleKey.Escape, ((KeyPressed)events[0]).KeyInfo.Key);
        Assert.Equal('x', ((KeyPressed)events[1]).KeyInfo.KeyChar);
    }

    // --- CSI u (kitty keyboard protocol) ---

    [Fact]
    public void CsiU_CtrlEnter_EmitsKeyPressedWithCtrlModifier()
    {
        // ESC[13;5u = Enter (keycode 13) with Ctrl modifier (5 = 1 + 4)
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        foreach (var c in "[13;5u")
            events.AddRange(parser.Process(Key(c)));

        var kp = Assert.Single(events);
        var pressed = Assert.IsType<KeyPressed>(kp);
        Assert.Equal(ConsoleKey.Enter, pressed.KeyInfo.Key);
        Assert.Equal('\r', pressed.KeyInfo.KeyChar);
        Assert.True((pressed.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0,
            "CSI u sequence ESC[13;5u should produce Ctrl modifier");
    }

    [Fact]
    public void CsiU_ShiftEnter_EmitsKeyPressedWithShiftModifier()
    {
        // ESC[13;2u = Enter (keycode 13) with Shift modifier (2 = 1 + 1)
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        foreach (var c in "[13;2u")
            events.AddRange(parser.Process(Key(c)));

        var kp = Assert.Single(events);
        var pressed = Assert.IsType<KeyPressed>(kp);
        Assert.Equal(ConsoleKey.Enter, pressed.KeyInfo.Key);
        Assert.True((pressed.KeyInfo.Modifiers & ConsoleModifiers.Shift) != 0);
    }

    [Fact]
    public void CsiU_CtrlShiftEnter_EmitsKeyPressedWithBothModifiers()
    {
        // ESC[13;6u = Enter with Ctrl+Shift (6 = 1 + 1 + 4)
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        foreach (var c in "[13;6u")
            events.AddRange(parser.Process(Key(c)));

        var kp = Assert.Single(events);
        var pressed = Assert.IsType<KeyPressed>(kp);
        Assert.Equal(ConsoleKey.Enter, pressed.KeyInfo.Key);
        Assert.True((pressed.KeyInfo.Modifiers & ConsoleModifiers.Control) != 0);
        Assert.True((pressed.KeyInfo.Modifiers & ConsoleModifiers.Shift) != 0);
    }

    [Fact]
    public void CsiU_PlainEnter_EmitsUnmodifiedEnter()
    {
        // ESC[13u = Enter with no modifiers (modifier field absent = 1)
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        foreach (var c in "[13u")
            events.AddRange(parser.Process(Key(c)));

        var kp = Assert.Single(events);
        var pressed = Assert.IsType<KeyPressed>(kp);
        Assert.Equal(ConsoleKey.Enter, pressed.KeyInfo.Key);
        Assert.Equal(default(ConsoleModifiers), pressed.KeyInfo.Modifiers);
    }

    [Fact]
    public void KittyKeyboardFlagResponse_EmitsCapabilityReport()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, "[?9u"));

        var report = Assert.IsType<KittyKeyboardFlagsReported>(Assert.Single(events));
        Assert.Equal(9, report.Flags);
        Assert.False(parser.IsBufferingEscape);
    }

    [Fact]
    public void PrimaryDeviceAttributesResponse_ClosesCapabilityProbe()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, "[?1;2c"));

        Assert.IsType<PrimaryDeviceAttributesReported>(Assert.Single(events));
        Assert.False(parser.IsBufferingEscape);
    }

    [Fact]
    public void CsiU_CtrlEnter_InsertsNewlineInTextAreaNode()
    {
        // End-to-end: CSI u Ctrl+Enter should insert a newline in a TextAreaNode
        var parser = new EscapeSequenceParser();
        using var node = new Termina.Layout.TextAreaNode();

        // Type "hello"
        foreach (var c in "hello")
            node.HandleInput(new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false));

        // Simulate Ctrl+Enter via CSI u sequence
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        foreach (var c in "[13;5u")
            events.AddRange(parser.Process(Key(c)));

        // Feed the parsed event into the node
        foreach (var evt in events)
        {
            if (evt is KeyPressed kp)
                node.HandleInput(kp.KeyInfo);
        }

        // Type "world"
        foreach (var c in "world")
            node.HandleInput(new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false));

        Assert.Equal("hello\nworld", node.Text);
    }

    // --- Alt+Enter (ESC + Enter) ---

    [Fact]
    public void EscThenEnter_EmitsAltEnter()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        events.AddRange(parser.Process(Key('\r', ConsoleKey.Enter)));

        var kp = Assert.Single(events);
        var pressed = Assert.IsType<KeyPressed>(kp);
        Assert.Equal(ConsoleKey.Enter, pressed.KeyInfo.Key);
        Assert.True(pressed.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt),
            "ESC + Enter should be interpreted as Alt+Enter");
        Assert.False(parser.IsBufferingEscape);
    }

    [Fact]
    public void EscThenLinefeed_EmitsAltEnter()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(EscKey()));
        events.AddRange(parser.Process(Key('\n', ConsoleKey.Enter)));

        var kp = Assert.Single(events);
        var pressed = Assert.IsType<KeyPressed>(kp);
        Assert.Equal(ConsoleKey.Enter, pressed.KeyInfo.Key);
        Assert.True(pressed.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt));
    }

    // --- Bracketed paste ---

    [Fact]
    public void BracketedPaste_EmitsPasteEvent()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();

        // Start: ESC[200~
        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, "[200~"));

        // Paste content
        events.AddRange(FeedString(parser, "hello world"));

        // End: ESC[201~
        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, "[201~"));

        var paste = Assert.Single(events);
        var pe = Assert.IsType<PasteEvent>(paste);
        Assert.Equal("hello world", pe.Content);
    }

    [Fact]
    public void BracketedPaste_WithNewlines_PreservesContent()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();

        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, "[200~"));
        events.AddRange(FeedString(parser, "line1\nline2"));
        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, "[201~"));

        var pe = Assert.IsType<PasteEvent>(Assert.Single(events));
        Assert.Equal("line1\nline2", pe.Content);
    }

    // --- Normal sequence followed by keys works ---

    [Fact]
    public void AfterConsumedMouseEvent_RegularKeysWorkNormally()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();

        // Mouse click (consumed)
        events.AddRange(FeedSequence(parser, SgrMouseSequence(0, 5, 10, 'M')));
        events.AddRange(FeedSequence(parser, SgrMouseSequence(0, 5, 10, 'm')));

        // Regular key after
        events.AddRange(parser.Process(Key('a', ConsoleKey.A)));

        var single = Assert.Single(events);
        Assert.Equal('a', ((KeyPressed)single).KeyInfo.KeyChar);
    }

    // --- Legacy CSI tilde key sequences ---

    [Theory]
    [InlineData("[1~", ConsoleKey.Home)]
    [InlineData("[2~", ConsoleKey.Insert)]
    [InlineData("[3~", ConsoleKey.Delete)]
    [InlineData("[4~", ConsoleKey.End)]
    [InlineData("[5~", ConsoleKey.PageUp)]
    [InlineData("[6~", ConsoleKey.PageDown)]
    [InlineData("[7~", ConsoleKey.Home)]
    [InlineData("[8~", ConsoleKey.End)]
    [InlineData("[11~", ConsoleKey.F1)]
    [InlineData("[12~", ConsoleKey.F2)]
    [InlineData("[13~", ConsoleKey.F3)]
    [InlineData("[14~", ConsoleKey.F4)]
    [InlineData("[15~", ConsoleKey.F5)]
    [InlineData("[17~", ConsoleKey.F6)]
    [InlineData("[18~", ConsoleKey.F7)]
    [InlineData("[19~", ConsoleKey.F8)]
    [InlineData("[20~", ConsoleKey.F9)]
    [InlineData("[21~", ConsoleKey.F10)]
    [InlineData("[23~", ConsoleKey.F11)]
    [InlineData("[24~", ConsoleKey.F12)]
    public void LegacyCsiTilde_EmitsSemanticKeyPressed(string seq, ConsoleKey expected)
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();

        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, seq));

        Assert.False(parser.IsBufferingEscape);

        var press = Assert.IsType<KeyPressed>(Assert.Single(events));
        Assert.Equal(expected, press.KeyInfo.Key);
        Assert.Equal('\0', press.KeyInfo.KeyChar);
        Assert.Equal((ConsoleModifiers)0, press.KeyInfo.Modifiers);
    }

    [Theory]
    [InlineData("[5;2~", true, false, false)]
    [InlineData("[5;3~", false, true, false)]
    [InlineData("[5;5~", false, false, true)]
    [InlineData("[5;8~", true, true, true)]
    public void LegacyCsiTilde_WithModifiers_EmitsSemanticKeyPressed(string seq, bool shift, bool alt, bool ctrl)
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();

        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, seq));

        Assert.False(parser.IsBufferingEscape);

        var press = Assert.IsType<KeyPressed>(Assert.Single(events));
        Assert.Equal(ConsoleKey.PageUp, press.KeyInfo.Key);
        Assert.Equal(shift, press.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));
        Assert.Equal(alt, press.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt));
        Assert.Equal(ctrl, press.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    [Theory]
    [InlineData("[9~")]
    [InlineData("[25;5~")]
    public void UnknownCsiTilde_FlushesAsRawKeys_AndUnsticksParser(string seq)
    {
        // Regression from #232: complete-but-unknown tilde sequences must not leave the parser
        // stuck in InBracketSequence and swallowing subsequent input.
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();

        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, seq));

        Assert.False(parser.IsBufferingEscape);
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.IsType<KeyPressed>(e));
        var keyChars = events.Select(e => ((KeyPressed)e).KeyInfo.KeyChar).ToArray();
        Assert.Equal($"\x1b{seq}".ToCharArray(), keyChars);
    }

    [Fact]
    public void MouseWheel_AfterLegacyCsiTilde_StillParses()
    {
        // The reported #232 symptom: [5~ used to leave the parser stuck, causing later mouse
        // wheel events to be swallowed. Semantic decoding must keep the parser unstuck too.
        var parser = new EscapeSequenceParser();

        var keyEvents = new List<IInputEvent>();
        keyEvents.AddRange(parser.Process(EscKey()));
        keyEvents.AddRange(FeedString(parser, "[5~"));

        Assert.False(parser.IsBufferingEscape);
        Assert.Equal(ConsoleKey.PageUp, Assert.IsType<KeyPressed>(Assert.Single(keyEvents)).KeyInfo.Key);

        var events = FeedSequence(parser, SgrMouseSequence(64, 5, 10));
        var scroll = Assert.Single(events);
        Assert.Equal(+1, Assert.IsType<MouseScrollEvent>(scroll).Delta);
    }

    [Fact]
    public void CsiZ_Backtab_EmitsTabWithShift()
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();

        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, "[Z"));

        Assert.False(parser.IsBufferingEscape);

        var press = Assert.IsType<KeyPressed>(Assert.Single(events));
        Assert.Equal(ConsoleKey.Tab, press.KeyInfo.Key);
        Assert.True(press.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));
    }

    [Fact]
    public void BracketedPaste_StillWorks_WithTildeFlushGuard()
    {
        // Guards against the tilde-flush returning false for the paste start "[200~" or end
        // "[201~": paste must still round-trip into a single PasteEvent.
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();

        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, "[200~"));
        events.AddRange(FeedString(parser, "pasted!"));
        events.AddRange(parser.Process(EscKey()));
        events.AddRange(FeedString(parser, "[201~"));

        var pe = Assert.IsType<PasteEvent>(Assert.Single(events));
        Assert.Equal("pasted!", pe.Content);
    }
}
