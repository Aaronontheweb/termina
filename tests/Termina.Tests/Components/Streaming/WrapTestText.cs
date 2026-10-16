// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Generates plain text for the wrapper equivalence tests, from fixed seeds.
/// </summary>
internal static class WrapTestText
{
    // ASCII, spaces, tabs, CJK, combining marks, zero-width characters, ZWJ emoji, flags, modifiers,
    // Hangul jamo and spacing marks.
    private static readonly string[] Pieces =
    {
        "a", "b", "c", "d", "e", "x", "Z", "1", ".", "_",
        " ", " ", " ", "\t", "  ",
        "中", "文", "ひ", "한",
        "é", "́", "̈", "à́",
        "​", "‍",
        "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466",
        "\U0001F600", "\U0001F1FA\U0001F1F8", "❤️", "❤",
        " ",
        "\U0001F1FA", "\U0001F1F8", "\U0001F44D", "\U0001F3FB", "\U0001F468", "\U0001F469",
        "ᄀ", "ᅡ", "ः",
    };

    public static string Generate(Random rng, bool longRuns, int maxPieces = 30)
    {
        var sb = new StringBuilder();
        var pieces = rng.Next(0, maxPieces + 1);
        for (var i = 0; i < pieces; i++)
        {
            var piece = Pieces[rng.Next(Pieces.Length)];
            // Unbroken runs of one piece force the long-word path at many widths.
            var repeat = longRuns && rng.Next(10) == 0 ? rng.Next(20, 120) : 1;
            for (var r = 0; r < repeat; r++)
                sb.Append(piece);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Bytes allocated on this thread by one call, after a warm-up call. Deterministic and independent
    /// of machine speed.
    /// </summary>
    public static long MeasureAllocatedBytes(Action action)
    {
        action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// Asserts that 4x more input allocates less than 8x more memory. Linear work gives about 4,
    /// quadratic work about 16. Retries to absorb noise from other allocations.
    /// </summary>
    public static void AssertLinearAllocation(Func<int, Action> actionForLength, int small, int large, string what)
    {
        double ratio = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ratio = (double)MeasureAllocatedBytes(actionForLength(large)) / MeasureAllocatedBytes(actionForLength(small));
            if (ratio < 8)
                return;
        }

        Assert.Fail($"{what}: 4x more text allocated {ratio:F1}x more memory. Expected about 4x for linear work.");
    }
}
