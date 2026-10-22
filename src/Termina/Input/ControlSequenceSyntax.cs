// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Classifies the bytes of an ECMA-48 control sequence (<c>CSI</c> parameters, intermediates, and final).
/// </summary>
/// <remarks>
/// A control sequence is <c>CSI</c>, then zero or more parameter bytes (0x30 to 0x3F), then zero or more
/// intermediate bytes (0x20 to 0x2F), then one final byte (0x40 to 0x7E).
/// See <see href="https://ecma-international.org/publications-and-standards/standards/ecma-48/"/>, section 5.4.
/// </remarks>
internal static class ControlSequenceSyntax
{
    private const char FirstParameterByte = '\x30';
    private const char LastParameterByte = '\x3F';
    private const char FirstIntermediateByte = '\x20';
    private const char LastIntermediateByte = '\x2F';
    private const char FirstFinalByte = '\x40';
    private const char LastFinalByte = '\x7E';

    public static bool IsParameterByte(char c) => c is >= FirstParameterByte and <= LastParameterByte;

    public static bool IsIntermediateByte(char c) => c is >= FirstIntermediateByte and <= LastIntermediateByte;

    public static bool IsFinalByte(char c) => c is >= FirstFinalByte and <= LastFinalByte;

    public static bool IsControlSequenceByte(char c) => IsParameterByte(c) || IsIntermediateByte(c) || IsFinalByte(c);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="body"/>, the text after <c>CSI</c>, is a valid control sequence
    /// that does not have its final byte yet.
    /// </summary>
    public static bool IsIncomplete(ReadOnlySpan<char> body)
    {
        var sawIntermediate = false;
        foreach (var c in body)
        {
            if (IsParameterByte(c) && !sawIntermediate)
                continue;

            if (!IsIntermediateByte(c))
                return false;

            sawIntermediate = true;
        }

        return true;
    }
}
