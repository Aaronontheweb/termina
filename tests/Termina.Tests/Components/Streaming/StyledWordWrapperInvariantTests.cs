// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;
using Termina.Terminal;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Invariant and oracle checks for <see cref="StyledWordWrapper"/>.
/// </summary>
/// <remarks>
/// These checks run over many randomly generated inputs from fixed seeds, so failures are
/// deterministic and reproducible. Each generated case must satisfy every property. A failure
/// message prints the seed, the width, and the input, so you can reproduce the case exactly.
/// This is a zero-dependency stand-in for a property-based testing library. It also acts as a
/// differential oracle against the independent plain <see cref="WordWrapper"/>.
/// </remarks>
public class StyledWordWrapperInvariantTests
{
    private const int Iterations = 50000;

    private static readonly Color[] Palette =
    {
        Color.Default, Color.Red, Color.Green, Color.Blue, Color.BrightMagenta, Color.BrightGreen,
    };

    private static readonly TextDecoration[] Decorations =
    {
        TextDecoration.None, TextDecoration.Bold, TextDecoration.Underline,
    };

    [Fact]
    public void Invariants_HoldOverManyRandomInputs()
    {
        var counts = new Dictionary<string, int>();
        var examples = new List<string>();

        void Record(string property, string detail)
        {
            counts[property] = counts.GetValueOrDefault(property) + 1;
            if (examples.Count < 12)
                examples.Add($"[{property}] {detail}");
        }

        for (var seed = 0; seed < Iterations; seed++)
        {
            var rng = new Random(seed);
            var line = GenerateLine(rng);
            var width = rng.Next(2, 31); // width >= 2 keeps a 2-column glyph sliceable

            List<StyledLine> wrapped;
            try
            {
                wrapped = StyledWordWrapper.WrapLine(line, width);
            }
            catch (Exception ex)
            {
                Record("THROW", $"{ex.GetType().Name}: {ex.Message} | seed={seed} width={width} input={Describe(line)}");
                continue;
            }

            var context = $"seed={seed} width={width} input={Describe(line)} wrapped={Describe(wrapped)}";

            if (CheckContentAndStylePreserved(line, wrapped) is { } p1) Record("P1-content-style", $"{p1} | {context}");
            if (CheckWidthBound(wrapped, width) is { } p2) Record("P2-width-bound", $"{p2} | {context}");
            if (CheckMaximalPacking(line, wrapped, width) is { } p3) Record("P3-maximal-packing", $"{p3} | {context}");
            if (CheckMatchesPlainOracle(line, wrapped, width) is { } d1) Record("D1-plain-oracle", $"{d1} | {context}");
        }

        if (counts.Count > 0)
        {
            var summary = string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            Assert.Fail($"Invariant/oracle failures over {Iterations} inputs.\nCounts: {summary}\n\n{string.Join("\n", examples)}");
        }
    }

    // P1: no non-whitespace glyph is lost, added, reordered, or restyled.
    private static string? CheckContentAndStylePreserved(StyledLine input, List<StyledLine> wrapped)
    {
        var expected = NonWhitespaceGlyphs(input.Segments);
        var actual = NonWhitespaceGlyphs(wrapped.SelectMany(l => l.Segments));
        return expected.SequenceEqual(actual)
            ? null
            : $"content/style changed: expected {expected.Count} glyphs, got {actual.Count}";
    }

    // P2: every output line fits within the width.
    private static string? CheckWidthBound(List<StyledLine> wrapped, int width)
    {
        foreach (var l in wrapped)
            if (l.ColumnCount > width)
                return $"line width {l.ColumnCount} exceeds {width}";
        return null;
    }

    // P3: greedy packing is maximal. When no source word is wider than the width, the first word
    // of a line must not fit at the end of the previous line.
    private static string? CheckMaximalPacking(StyledLine input, List<StyledLine> wrapped, int width)
    {
        var words = input.ToPlainText().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;
        if (words.Max(w => DisplayWidth.GetColumnCount(w)) > width) return null; // long-word breaking is exempt

        for (var i = 0; i + 1 < wrapped.Count; i++)
        {
            var prevWidth = wrapped[i].ColumnCount;
            var nextFirst = wrapped[i + 1].ToPlainText().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (nextFirst is null) continue;
            var nextFirstWidth = DisplayWidth.GetColumnCount(nextFirst);
            if (prevWidth + 1 + nextFirstWidth <= width)
                return $"line {i} (width {prevWidth}) could still take next word (width {nextFirstWidth})";
        }
        return null;
    }

    // D1: the styled wrapper must agree with the independent plain-string wrapper on where breaks land.
    private static string? CheckMatchesPlainOracle(StyledLine input, List<StyledLine> wrapped, int width)
    {
        var styled = wrapped.Select(l => l.ToPlainText()).ToList();
        var plain = WordWrapper.WrapLine(input.ToPlainText(), width);
        return styled.SequenceEqual(plain)
            ? null
            : $"oracle mismatch: styled={FormatLines(styled)} plain={FormatLines(plain)}";
    }

    private static List<(char Ch, TextStyle Style)> NonWhitespaceGlyphs(IEnumerable<StyledSegment> segments)
    {
        var result = new List<(char, TextStyle)>();
        foreach (var seg in segments)
            foreach (var c in seg.Text)
                if (!char.IsWhiteSpace(c))
                    result.Add((c, seg.Style));
        return result;
    }

    private static StyledLine GenerateLine(Random rng)
    {
        var line = new StyledLine();
        var segmentCount = rng.Next(0, 6);
        for (var s = 0; s < segmentCount; s++)
        {
            var text = GenerateText(rng);
            if (text.Length == 0) continue;
            var style = new TextStyle(Pick(rng, Palette), Pick(rng, Palette), Pick(rng, Decorations));
            line.Append(new StyledSegment(text, style));
        }
        return line;
    }

    private static string GenerateText(Random rng)
    {
        var len = rng.Next(0, 9);
        var sb = new System.Text.StringBuilder(len);
        for (var i = 0; i < len; i++)
        {
            var r = rng.Next(0, 100);
            if (r < 55) sb.Append((char)('a' + rng.Next(0, 5)));       // letters a-e
            else if (r < 85) sb.Append(' ');                           // spaces: word boundaries and runs
            else sb.Append(rng.Next(0, 2) == 0 ? '中' : '文'); // wide (2-column) BMP glyphs
        }
        return sb.ToString();
    }

    private static T Pick<T>(Random rng, T[] items) => items[rng.Next(0, items.Length)];

    private static string Describe(StyledLine line) =>
        "[" + string.Join(", ", line.Segments.Select(s => $"\"{s.Text}\"({s.Style.Foreground}/{s.Style.Background}/{s.Style.Decoration})")) + "]";

    private static string Describe(List<StyledLine> lines) => FormatLines(lines.Select(l => l.ToPlainText()));

    private static string FormatLines(IEnumerable<string> lines) => "«" + string.Join("│", lines) + "»";
}
