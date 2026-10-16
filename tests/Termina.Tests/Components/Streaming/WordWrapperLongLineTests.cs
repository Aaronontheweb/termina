// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Tests for wrapping very long lines with the plain <see cref="WordWrapper"/> (issue 397). The output
/// must match the 0.16.3 algorithm, kept as <see cref="ReferenceWordWrapper"/>, and the cost must be
/// linear in the length of the line.
/// </summary>
public class WordWrapperLongLineTests
{
    [Fact]
    public void Equivalence_WithReference_OverRandomLinesAndEveryWidth()
    {
        const int lineCount = 300;
        var failures = new List<string>();
        var comparisons = 0;

        for (var seed = 0; seed < lineCount; seed++)
        {
            var text = WrapTestText.Generate(new Random(seed), longRuns: seed % 3 != 0);
            for (var width = 1; width <= 200; width++)
            {
                comparisons++;
                var expected = ReferenceWordWrapper.WrapLine(text, width);
                var actual = WordWrapper.WrapLine(text, width);
                if (!expected.SequenceEqual(actual) && failures.Count < 10)
                    failures.Add($"seed={seed} width={width}: «{string.Join("│", expected)}» vs «{string.Join("│", actual)}»");
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
            var text = WrapTestText.Generate(rng, longRuns: false, maxPieces: 8);
            var width = rng.Next(1, 9);
            var expected = ReferenceWordWrapper.WrapLine(text, width);
            var actual = WordWrapper.WrapLine(text, width);
            if (!expected.SequenceEqual(actual) && failures.Count < 10)
                failures.Add($"seed={seed} width={width}: «{string.Join("│", expected)}» vs «{string.Join("│", actual)}»");
        }

        Assert.True(failures.Count == 0, $"{failures.Count}+ differences:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(100_000)]
    public void UnbrokenLine_WrapsIntoFullRows(int length)
    {
        var rows = WordWrapper.WrapLine(new string('a', length), 120);

        Assert.Equal((length + 119) / 120, rows.Count);
        Assert.All(rows.Take(rows.Count - 1), r => Assert.Equal(120, r.Length));
        Assert.Equal(new string('a', length), string.Concat(rows));
    }

    [Fact]
    public void WideCharactersAtRowEdge_StartTheNextRow()
    {
        var rows = WordWrapper.WrapLine(new string('中', 10), 5);

        Assert.Equal(new[] { "中中", "中中", "中中", "中中", "中中" }, rows);
    }

    [Fact]
    public void WrapLine_AllocationScalesLinearlyWithLength()
    {
        WrapTestText.AssertLinearAllocation(
            length => { var text = new string('a', length); return () => WordWrapper.WrapLine(text, 120); },
            10_000, 40_000, "WordWrapper.WrapLine, unbroken");
        WrapTestText.AssertLinearAllocation(
            length => { var text = new string('中', length / 2); return () => WordWrapper.WrapLine(text, 120); },
            10_000, 40_000, "WordWrapper.WrapLine, wide");
    }
}
