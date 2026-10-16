// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;
using Termina.Input;
using Termina.Platform;

namespace Termina.Tests.Platform;

/// <summary>
/// Tests for <see cref="RawByteKeyMapper.ByteToKeyInfo"/> and the byte → KeyInfo → EscapeSequenceParser
/// pipeline that both the Unix raw-stdin reader and the Windows raw-VT reader feed. No TTY required.
/// </summary>
public class UnixConsoleByteMappingTests
{
    [Fact]
    public void Backspace_DelByte_MapsToBackspace()
    {
        var k = RawByteKeyMapper.ByteToKeyInfo(0x7F);
        Assert.Equal(ConsoleKey.Backspace, k.Key);
        Assert.Equal('\x7F', k.KeyChar);
    }

    [Fact]
    public void Backspace_BsByte_MapsToBackspace()
    {
        var k = RawByteKeyMapper.ByteToKeyInfo(0x08);
        Assert.Equal(ConsoleKey.Backspace, k.Key);
    }

    [Fact]
    public void Tab_MapsToTab()
    {
        var k = RawByteKeyMapper.ByteToKeyInfo(0x09);
        Assert.Equal(ConsoleKey.Tab, k.Key);
    }

    [Theory]
    [InlineData((byte)0x0A)]
    [InlineData((byte)0x0D)]
    public void Enter_LfOrCr_MapsToEnter(byte b)
    {
        var k = RawByteKeyMapper.ByteToKeyInfo(b);
        Assert.Equal(ConsoleKey.Enter, k.Key);
    }

    [Fact]
    public void Escape_MapsToEscape()
    {
        var k = RawByteKeyMapper.ByteToKeyInfo(0x1B);
        Assert.Equal(ConsoleKey.Escape, k.Key);
        Assert.Equal('\x1B', k.KeyChar);
    }

    [Fact]
    public void Space_MapsToSpacebar()
    {
        var k = RawByteKeyMapper.ByteToKeyInfo(0x20);
        Assert.Equal(ConsoleKey.Spacebar, k.Key);
        Assert.Equal(' ', k.KeyChar);
    }

    [Theory]
    [InlineData((byte)0x03, ConsoleKey.C)]  // Ctrl+C
    [InlineData((byte)0x04, ConsoleKey.D)]  // Ctrl+D
    [InlineData((byte)0x1A, ConsoleKey.Z)]  // Ctrl+Z
    public void CtrlLetter_MapsToLetterWithControlModifier(byte b, ConsoleKey expectedKey)
    {
        var k = RawByteKeyMapper.ByteToKeyInfo(b);
        Assert.Equal(expectedKey, k.Key);
        Assert.True((k.Modifiers & ConsoleModifiers.Control) != 0, "Control modifier missing");
        Assert.Equal((char)b, k.KeyChar);
    }

    [Theory]
    [InlineData((byte)'a', 'a', ConsoleKey.A, false)]
    [InlineData((byte)'z', 'z', ConsoleKey.Z, false)]
    [InlineData((byte)'A', 'A', ConsoleKey.A, true)]
    [InlineData((byte)'Z', 'Z', ConsoleKey.Z, true)]
    [InlineData((byte)'0', '0', ConsoleKey.D0, false)]
    [InlineData((byte)'9', '9', ConsoleKey.D9, false)]
    public void PrintableAscii_MapsCorrectly(byte b, char expectedChar, ConsoleKey expectedKey, bool expectedShift)
    {
        var k = RawByteKeyMapper.ByteToKeyInfo(b);
        Assert.Equal(expectedChar, k.KeyChar);
        Assert.Equal(expectedKey, k.Key);
        Assert.Equal(expectedShift, (k.Modifiers & ConsoleModifiers.Shift) != 0);
        Assert.False((k.Modifiers & ConsoleModifiers.Control) != 0);
    }

    [Fact]
    public void HighBitByte_PassedThroughVerbatim()
    {
        var k = RawByteKeyMapper.ByteToKeyInfo(0xC3);
        Assert.Equal('\u00C3', k.KeyChar);
        Assert.Equal(ConsoleKey.None, k.Key);
    }

    /// <summary>
    /// CSI arrow forms fed byte-by-byte through the parser emit as keyboard arrows
    /// until DECCKM compliance is confirmed (by observing an SS3 arrow key).
    /// </summary>
    [Theory]
    [InlineData('A', ConsoleKey.UpArrow)]
    [InlineData('B', ConsoleKey.DownArrow)]
    public void RawBytes_CsiArrow_ParsesAsKeyPressedBeforeDeckmConfirmed(char letter, ConsoleKey expectedKey)
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(RawByteKeyMapper.ByteToKeyInfo(0x1B)));
        events.AddRange(parser.Process(RawByteKeyMapper.ByteToKeyInfo((byte)'[')));
        events.AddRange(parser.Process(RawByteKeyMapper.ByteToKeyInfo((byte)letter)));

        var press = Assert.Single(events);
        var kp = Assert.IsType<KeyPressed>(press);
        Assert.Equal(expectedKey, kp.KeyInfo.Key);
    }

    /// <summary>
    /// SS3 arrow forms (real keyboard arrows under DECCKM) fed byte-by-byte must come out
    /// as <see cref="KeyPressed"/> events — NOT as scroll events.
    /// </summary>
    [Theory]
    [InlineData('A', ConsoleKey.UpArrow)]
    [InlineData('B', ConsoleKey.DownArrow)]
    [InlineData('C', ConsoleKey.RightArrow)]
    [InlineData('D', ConsoleKey.LeftArrow)]
    public void RawBytes_Ss3Arrow_ParsesAsKeyPressed(char letter, ConsoleKey expectedKey)
    {
        var parser = new EscapeSequenceParser();
        var events = new List<IInputEvent>();
        events.AddRange(parser.Process(RawByteKeyMapper.ByteToKeyInfo(0x1B)));
        events.AddRange(parser.Process(RawByteKeyMapper.ByteToKeyInfo((byte)'O')));
        events.AddRange(parser.Process(RawByteKeyMapper.ByteToKeyInfo((byte)letter)));

        var evt = Assert.Single(events);
        var kp = Assert.IsType<KeyPressed>(evt);
        Assert.Equal(expectedKey, kp.KeyInfo.Key);
    }

    [Fact]
    public void RawBytes_CsiUShiftEnter_PreservesShiftModifier()
    {
        var parser = new EscapeSequenceParser();
        var events = "\x1b[13;2u"u8.ToArray()
            .SelectMany(value => parser.Process(RawByteKeyMapper.ByteToKeyInfo(value)))
            .ToList();

        var pressed = Assert.IsType<KeyPressed>(Assert.Single(events));
        Assert.Equal(ConsoleKey.Enter, pressed.KeyInfo.Key);
        Assert.True(pressed.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));
    }

    /// <summary>
    /// UTF-8 multi-byte sequences must reassemble into one <see cref="ConsoleKeyInfo"/> per
    /// codepoint when fed through the same decoding logic the reader thread uses.
    /// </summary>
    [Theory]
    [InlineData("é")]     // 2-byte UTF-8
    [InlineData("€")]     // 3-byte UTF-8
    [InlineData("🎉")]    // 4-byte UTF-8 → surrogate pair (2 chars)
    public void Utf8MultiByte_DecodesToCodepoint(string input)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var oneByte = new byte[1];
        var chars = new char[2];
        var emitted = new List<char>();

        foreach (var b in Encoding.UTF8.GetBytes(input))
        {
            if (b < 0x80)
            {
                emitted.Add((char)b);
            }
            else
            {
                oneByte[0] = b;
                var count = decoder.GetChars(oneByte, 0, 1, chars, 0);
                for (var i = 0; i < count; i++) emitted.Add(chars[i]);
            }
        }

        Assert.Equal(input, new string(emitted.ToArray()));
    }
}
