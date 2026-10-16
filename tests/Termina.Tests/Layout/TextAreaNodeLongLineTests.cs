// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Tests.Components.Streaming;

namespace Termina.Tests.Layout;

/// <summary>
/// Tests for wrapping very long lines in <see cref="TextAreaNode"/> (issue 397). The rows must match
/// the 0.16.3 algorithm, kept as <see cref="ReferenceTextAreaWrap"/>, and the cost must be linear in
/// the length of the line. The wrap runs after every edit.
/// </summary>
public class TextAreaNodeLongLineTests
{
    private static List<TextAreaNode.WrappedLine> Wrap(bool reference, string text, int start, int length, int width)
    {
        var rows = new List<TextAreaNode.WrappedLine>();
        if (reference)
            ReferenceTextAreaWrap.WrapLogicalLine(rows, text, start, length, width);
        else
            TextAreaNode.WrapLogicalLine(rows, text, start, length, width);
        return rows;
    }

    private static string Describe(List<TextAreaNode.WrappedLine> rows) =>
        string.Join(" ", rows.Select(r => $"({r.TextStartIndex},{r.Length},{r.ColumnCount})"));

    [Fact]
    public void Equivalence_WithReference_OverRandomLinesAndEveryWidth()
    {
        const int lineCount = 300;
        var failures = new List<string>();
        var comparisons = 0;

        for (var seed = 0; seed < lineCount; seed++)
        {
            var rng = new Random(seed);
            var line = WrapTestText.Generate(rng, longRuns: seed % 3 != 0);
            // The line sits inside a larger text, as a logical line does in the text area.
            var prefix = "ab\n";
            var text = prefix + line + "\nzz";
            for (var width = 1; width <= 200; width++)
            {
                comparisons++;
                var expected = Wrap(true, text, prefix.Length, line.Length, width);
                var actual = Wrap(false, text, prefix.Length, line.Length, width);
                if (!expected.SequenceEqual(actual) && failures.Count < 10)
                    failures.Add($"seed={seed} width={width}: {Describe(expected)} vs {Describe(actual)}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count}+ differences in {comparisons} comparisons:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Equivalence_WithReference_OverShortDenseCases()
    {
        var failures = new List<string>();
        for (var seed = 100_000; seed < 160_000; seed++)
        {
            var rng = new Random(seed);
            var line = WrapTestText.Generate(rng, longRuns: false, maxPieces: 8);
            var width = rng.Next(1, 9);
            var expected = Wrap(true, line, 0, line.Length, width);
            var actual = Wrap(false, line, 0, line.Length, width);
            if (!expected.SequenceEqual(actual) && failures.Count < 10)
                failures.Add($"seed={seed} width={width} text={line.Replace("\t", "\\t")}: {Describe(expected)} vs {Describe(actual)}");
        }

        Assert.True(failures.Count == 0, $"{failures.Count}+ differences:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Equivalence_WithReference_WhenAWindowCutsATextElement()
    {
        // Long runs of combining marks and ZWJ emoji make the lookahead window end inside one text
        // element, which the wrapper must not trust.
        foreach (var unit in new[] { "é́́", "\U0001F468‍\U0001F469‍\U0001F467", "\U0001F1FA\U0001F1F8" })
        {
            var line = string.Concat(Enumerable.Repeat(unit, 200)) + " " + string.Concat(Enumerable.Repeat(unit, 150));
            for (var width = 1; width <= 80; width++)
                Assert.Equal(Describe(Wrap(true, line, 0, line.Length, width)), Describe(Wrap(false, line, 0, line.Length, width)));
        }
    }

    [Fact]
    public void LongLines_WrapIntoFullRows()
    {
        var unbroken = new string('a', 100_000);
        var rows = Wrap(false, unbroken, 0, unbroken.Length, 120);
        Assert.Equal((100_000 + 119) / 120, rows.Count);
        Assert.All(rows.Take(rows.Count - 1), r => Assert.Equal(120, r.ColumnCount));

        // Words of 9 letters and a space.
        var words = string.Concat(Enumerable.Repeat("abcdefghi ", 10_000));
        rows = Wrap(false, words, 0, words.Length, 120);
        Assert.All(rows, r => Assert.True(r.ColumnCount <= 120));
        Assert.Equal((100_000 + 119) / 120, rows.Count); // 12 words of 10 characters fill a row exactly
        Assert.All(rows.Take(rows.Count - 1), r => Assert.Equal(120, r.Length));
    }

    [Fact]
    public void Wrap_AllocationScalesLinearlyWithLength()
    {
        WrapTestText.AssertLinearAllocation(
            length => { var text = new string('a', length); return () => Wrap(false, text, 0, text.Length, 120); },
            10_000, 40_000, "TextAreaNode wrap, unbroken");
        WrapTestText.AssertLinearAllocation(
            length => { var text = string.Concat(Enumerable.Repeat("abcdefghi ", length / 10)); return () => Wrap(false, text, 0, text.Length, 120); },
            10_000, 40_000, "TextAreaNode wrap, words");
    }
}
