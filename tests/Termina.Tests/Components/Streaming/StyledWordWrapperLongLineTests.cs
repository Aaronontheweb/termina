// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;
using Termina.Components.Streaming;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Tests for wrapping very long lines (issue 397). Wrapping one line must be linear in its length,
/// and the output must match the 0.16.3 algorithm, which is kept as <see cref="ReferenceStyledWordWrapper"/>.
/// </summary>
public class StyledWordWrapperLongLineTests
{
    private static readonly Color[] Palette =
    {
        Color.Default, Color.Red, Color.Green, Color.Blue, Color.BrightMagenta,
    };

    // Pieces chosen to hit every branch of the long-word breaker: ASCII, spaces, tabs, wide CJK,
    // combining marks, zero-width characters, ZWJ emoji, flags, and variation selectors.
    private static readonly string[] Pieces =
    {
        "a", "b", "c", "d", "e", "x", "Z", "1", ".", "_",
        " ", " ", "\t", "  ",
        "中", "文", "ひ", "한",
        "e\u0301", "\u0301", "\u0308", "a\u0300\u0301",
        "\u200B", "\u200D",
        "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466",
        "\U0001F600", "\U0001F1FA\U0001F1F8", "\u2764\uFE0F", "\u2764",
        "\u00A0",
    };

    [Fact]
    public void Equivalence_WithReference_OverRandomLinesAndEveryWidth()
    {
        const int lineCount = 300;
        var failures = new List<string>();
        var comparisons = 0;

        for (var seed = 0; seed < lineCount; seed++)
        {
            var line = GenerateLine(new Random(seed), longRuns: seed % 3 != 0);
            for (var width = 1; width <= 200; width++)
            {
                comparisons++;
                var diff = Compare(ReferenceStyledWordWrapper.WrapLine(line, width), StyledWordWrapper.WrapLine(line, width));
                if (diff is not null && failures.Count < 10)
                    failures.Add($"seed={seed} width={width}: {diff} | {Describe(line)}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} differences in {comparisons} comparisons:\n" + string.Join("\n", failures));
        Assert.Equal(lineCount * 200, comparisons);
    }

    [Fact]
    public void Equivalence_WithReference_OverShortDenseCases()
    {
        // Many short lines that stress segment boundaries at row edges, at small widths.
        var failures = new List<string>();
        for (var seed = 100_000; seed < 160_000; seed++)
        {
            var rng = new Random(seed);
            var line = GenerateLine(rng, longRuns: false, maxSegments: 4, maxPieces: 6);
            var width = rng.Next(1, 9);
            var diff = Compare(ReferenceStyledWordWrapper.WrapLine(line, width), StyledWordWrapper.WrapLine(line, width));
            if (diff is not null && failures.Count < 10)
                failures.Add($"seed={seed} width={width}: {diff} | {Describe(line)}");
        }

        Assert.True(failures.Count == 0, $"{failures.Count}+ differences:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(30_000)]
    [InlineData(100_000)]
    public void UnbrokenLine_WrapsIntoFullRows(int length)
    {
        var line = new StyledLine();
        // Split at a point that is not a multiple of the width, so one row holds both styles.
        var split = length / 2 + 7;
        line.Append(new string('a', split), Color.Green);
        line.Append(new string('b', length - split), Color.Red);

        var rows = StyledWordWrapper.WrapLine(line, 120);

        Assert.Equal((length + 119) / 120, rows.Count);
        Assert.All(rows.Take(rows.Count - 1), r => Assert.Equal(120, r.ColumnCount));
        Assert.Equal(line.ToPlainText(), string.Concat(rows.Select(r => r.ToPlainText())));

        // The style change survives in the row that crosses it.
        var crossing = rows[split / 120];
        Assert.Equal(2, crossing.SegmentCount);
        Assert.Equal(Color.Green, crossing.Segments[0].Style.Foreground);
        Assert.Equal(Color.Red, crossing.Segments[1].Style.Foreground);
    }

    [Fact]
    public void WideCharacterAtRowEdge_StartsNextRow()
    {
        var rows = StyledWordWrapper.WrapLine(new StyledLine(new string('中', 10)), 5);

        Assert.Equal(new[] { "中中", "中中", "中中", "中中", "中中" }, rows.Select(r => r.ToPlainText()));
    }

    [Fact]
    public void WideCharacterWiderThanWidth_KeepsRestTogether()
    {
        var line = new StyledLine("中中中");
        var expected = ReferenceStyledWordWrapper.WrapLine(line, 1);

        Assert.Null(Compare(expected, StyledWordWrapper.WrapLine(line, 1)));
    }

    [Fact]
    public void ZwjEmojiSequence_IsNeverSplit()
    {
        const string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";
        var rows = StyledWordWrapper.WrapLine(new StyledLine(string.Concat(Enumerable.Repeat(family, 50))), 7);

        Assert.All(rows, r => Assert.Equal(0, r.ToPlainText().Length % family.Length));
        Assert.Equal(string.Concat(Enumerable.Repeat(family, 50)), string.Concat(rows.Select(r => r.ToPlainText())));
    }

    [Fact]
    public void StreamingTextNode_RendersHugeUnbrokenLine()
    {
        var node = StreamingTextNode.Create();
        node.AppendLine(new string('a', 100_000));
        node.AppendLine("tail");

        var terminal = new VirtualTerminal(120, 10);
        node.Render(new RegionRenderContext(terminal, 0, 0, 120, 10), new Rect(0, 0, 120, 10));

        // The viewport shows the end of the buffer: the last rows of the long line, then the next line.
        Assert.Equal('t', terminal.GetChar(0, 9));
        Assert.Equal('a', terminal.GetChar(0, 8));
        Assert.Equal('a', terminal.GetChar(39, 8));
        Assert.Equal(' ', terminal.GetChar(40, 8)); // 100,000 = 833 * 120 + 40
    }

    [Fact]
    public void WrapLine_AllocationScalesLinearlyWithLength()
    {
        // Bytes allocated on this thread are deterministic and do not depend on machine speed.
        // Linear work gives a ratio near 4 for a 4x longer line. Quadratic work gives about 16.
        // The 0.16.3 algorithm allocated several hundred MB for 30,000 characters.
        const int small = 10_000;
        const int large = 40_000;

        double ratio = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var smallBytes = MeasureAllocatedBytes(small);
            var largeBytes = MeasureAllocatedBytes(large);
            ratio = (double)largeBytes / smallBytes;
            if (ratio < 8)
                return;
        }

        Assert.Fail($"Wrapping 4x more text allocated {ratio:F1}x more memory. Expected about 4x for linear work.");
    }

    private static long MeasureAllocatedBytes(int length)
    {
        var line = new StyledLine();
        line.Append(new string('a', length / 2), Color.Green);
        line.Append(new string('b', length - length / 2), Color.Red);

        StyledWordWrapper.WrapLine(line, 120); // warm up the JIT so its allocations are not counted

        var before = GC.GetAllocatedBytesForCurrentThread();
        StyledWordWrapper.WrapLine(line, 120);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static StyledLine GenerateLine(Random rng, bool longRuns, int maxSegments = 5, int maxPieces = 30)
    {
        var line = new StyledLine();
        var segments = rng.Next(0, maxSegments + 1);
        for (var s = 0; s < segments; s++)
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

            if (sb.Length == 0) continue;
            var style = new TextStyle(
                Palette[rng.Next(Palette.Length)],
                Palette[rng.Next(Palette.Length)],
                rng.Next(3) == 0 ? TextDecoration.Bold : TextDecoration.None);
            line.Append(new StyledSegment(sb.ToString(), style));
        }
        return line;
    }

    // Compares exact output: row count, segment count, text, and style of every segment.
    private static string? Compare(List<StyledLine> expected, List<StyledLine> actual)
    {
        if (expected.Count != actual.Count)
            return $"row count {expected.Count} vs {actual.Count}";

        for (var r = 0; r < expected.Count; r++)
        {
            var e = expected[r].Segments;
            var a = actual[r].Segments;
            if (e.Count != a.Count)
                return $"row {r}: segment count {e.Count} vs {a.Count}: «{expected[r]}» vs «{actual[r]}»";

            for (var s = 0; s < e.Count; s++)
            {
                if (e[s].Text != a[s].Text || !e[s].Style.Equals(a[s].Style))
                    return $"row {r} segment {s}: «{e[s].Text}» vs «{a[s].Text}»";
            }
        }
        return null;
    }

    private static string Describe(StyledLine line) =>
        "[" + string.Join(", ", line.Segments.Select(s => $"\"{s.Text.Replace("\t", "\\t")}\"({s.Style.Foreground}/{s.Style.Background}/{s.Style.Decoration})")) + "]";
}
