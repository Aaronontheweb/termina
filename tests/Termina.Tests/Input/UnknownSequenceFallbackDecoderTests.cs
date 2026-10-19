// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Input;
using Termina.Platform;

namespace Termina.Tests.Input;

public class UnknownSequenceFallbackDecoderTests
{
    [Theory]
    [InlineData("[")]
    [InlineData("[2")]
    [InlineData("[200")]
    [InlineData("[13;")]
    [InlineData("[<64;5")]
    [InlineData("[0;1;97:98:99:100:101:102:103:104:105")]
    [InlineData("[?1;2")]
    [InlineData("[1 ")]
    public void CouldLeadToRecognizedSequence_ReturnsTrueForRecognizedPrefixes(string sequence)
    {
        Assert.True(UnknownSequenceFallbackDecoder.CouldLeadToRecognizedSequence(sequence));
    }

    [Theory]
    [InlineData("[9~")]
    [InlineData("[25;5~")]
    [InlineData("[x")]
    [InlineData("[?25h")]
    [InlineData("[1\r")]
    [InlineData("[1 2")]
    [InlineData("[1\u00e9")]
    public void CouldLeadToRecognizedSequence_ReturnsFalseForUnknownCompleteOrInvalidPrefixes(string sequence)
    {
        Assert.False(UnknownSequenceFallbackDecoder.CouldLeadToRecognizedSequence(sequence));
    }

    [Fact]
    public void CouldLeadToRecognizedSequence_AcceptsParametersUpToTheLimit()
    {
        var atLimit = "[" + new string('1', UnknownSequenceFallbackDecoder.MaxSequenceLength - 1);

        Assert.True(UnknownSequenceFallbackDecoder.CouldLeadToRecognizedSequence(atLimit));
        Assert.False(UnknownSequenceFallbackDecoder.CouldLeadToRecognizedSequence(atLimit + "1"));
    }

    [Fact]
    public void Parser_FlushesSequenceThatPassesTheLimit()
    {
        var parser = new EscapeSequenceParser();
        var parameters = new string('1', UnknownSequenceFallbackDecoder.MaxSequenceLength);

        var events = Feed(parser, "\x1b[" + parameters);

        Assert.Equal(parameters.Length + 2, events.Count);
        AssertKey(events[0], ConsoleKey.Escape, '\x1b');
        AssertKey(events[1], ConsoleKey.None, '[');
        Assert.False(parser.IsBufferingEscape);
    }

    [Fact]
    public void Parser_AbortedSequence_FlushesRawKeysAndProcessesTheAbortingKey()
    {
        var parser = new EscapeSequenceParser();

        var events = Feed(parser, "\x1b[12\x1b[A");

        Assert.Equal(5, events.Count);
        AssertKey(events[0], ConsoleKey.Escape, '\x1b');
        AssertKey(events[1], ConsoleKey.None, '[');
        AssertKey(events[2], ConsoleKey.None, '1');
        AssertKey(events[3], ConsoleKey.None, '2');
        AssertKey(events[4], ConsoleKey.UpArrow, '\0');
    }

    [Fact]
    public void Parser_SequenceAbortedByEnter_KeepsTheEnterKey()
    {
        var parser = new EscapeSequenceParser();

        var events = Feed(parser, "\x1b[1\r");

        Assert.Equal(4, events.Count);
        AssertKey(events[0], ConsoleKey.Escape, '\x1b');
        AssertKey(events[1], ConsoleKey.None, '[');
        AssertKey(events[2], ConsoleKey.None, '1');
        AssertKey(events[3], ConsoleKey.Enter, '\r');
    }

    [Fact]
    public void ToRawKeyEvents_PrependsEscapeAndFlushesSequenceCharacters()
    {
        var events = UnknownSequenceFallbackDecoder.ToRawKeyEvents("[9~");

        Assert.Equal(4, events.Count);
        AssertKey(events[0], ConsoleKey.Escape, '\x1b');
        AssertKey(events[1], ConsoleKey.None, '[');
        AssertKey(events[2], ConsoleKey.None, '9');
        AssertKey(events[3], ConsoleKey.None, '~');
    }

    [Fact]
    public void AppendRawKeyEvents_AppendsWithoutClearingExistingEvents()
    {
        var events = new List<IInputEvent>
        {
            new KeyPressed(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false))
        };

        UnknownSequenceFallbackDecoder.AppendRawKeyEvents("[x", events);

        Assert.Equal(4, events.Count);
        AssertKey(events[0], ConsoleKey.X, 'x');
        AssertKey(events[1], ConsoleKey.Escape, '\x1b');
        AssertKey(events[2], ConsoleKey.None, '[');
        AssertKey(events[3], ConsoleKey.None, 'x');
    }

    [Fact]
    public void AppendRawSs3KeyEvents_FlushesEscapeOAndFinal()
    {
        var events = new List<IInputEvent>();

        UnknownSequenceFallbackDecoder.AppendRawSs3KeyEvents('Z', events);

        Assert.Equal(3, events.Count);
        AssertKey(events[0], ConsoleKey.Escape, '\x1b');
        AssertKey(events[1], ConsoleKey.O, 'O');
        AssertKey(events[2], ConsoleKey.None, 'Z');
    }

    private static List<IInputEvent> Feed(EscapeSequenceParser parser, string input)
    {
        var events = new List<IInputEvent>();
        foreach (var b in input.Select(c => (byte)c))
            events.AddRange(parser.Process(RawByteKeyMapper.ByteToKeyInfo(b)));

        return events;
    }

    private static void AssertKey(IInputEvent inputEvent, ConsoleKey key, char keyChar)
    {
        var pressed = Assert.IsType<KeyPressed>(inputEvent);
        Assert.Equal(key, pressed.KeyInfo.Key);
        Assert.Equal(keyChar, pressed.KeyInfo.KeyChar);
    }
}
