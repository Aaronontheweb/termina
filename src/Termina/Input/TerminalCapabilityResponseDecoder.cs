// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Decodes terminal responses which close the Kitty keyboard capability probe.
/// </summary>
internal static class TerminalCapabilityResponseDecoder
{
    public static bool CouldBeResponse(InputSequence sequence)
    {
        var text = sequence.Text;
        if (!text.StartsWith("[?", StringComparison.Ordinal) || text.Length > 32)
            return false;

        return ContainsOnlyParameters(text.AsSpan(2));
    }

    public static bool TryDecodeKeyboardFlags(InputSequence sequence, out int flags)
    {
        flags = 0;
        var text = sequence.Text;
        return text.Length >= 4
               && text.StartsWith("[?", StringComparison.Ordinal)
               && text[^1] == 'u'
               && int.TryParse(text.AsSpan(2, text.Length - 3), out flags);
    }

    public static bool IsPrimaryDeviceAttributes(InputSequence sequence)
    {
        var text = sequence.Text;
        if (text.Length < 3
            || !text.StartsWith("[?", StringComparison.Ordinal)
            || text[^1] != 'c')
            return false;

        var attributes = text.AsSpan(2, text.Length - 3);
        return ContainsOnlyParameters(attributes);
    }

    private static bool ContainsOnlyParameters(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character) && character != ';')
                return false;
        }

        return true;
    }
}

internal sealed record KittyKeyboardFlagsReported(int Flags) : IInputEvent;

internal sealed record PrimaryDeviceAttributesReported : IInputEvent;
