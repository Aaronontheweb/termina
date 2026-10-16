// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using CsCheck;
using Termina.Components.Streaming;
using Termina.Terminal;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Property-based tests for <see cref="StyledLine"/> (CsCheck).
/// </summary>
/// <remarks>
/// <para>
/// HOW TO READ THIS FILE. Each <c>[Fact]</c> states one rule and hands it to <c>.Sample(...)</c>,
/// which runs the rule over many random lines. On a failure CsCheck shrinks to the smallest failing
/// input and prints a seed; reproduce it with <c>CsCheck_Seed=&lt;seed&gt;</c>.
/// </para>
/// <para>
/// THE ORACLES. A <see cref="StyledLine"/> is a run of styled segments. Two independent references
/// check it:
/// </para>
/// <list type="bullet">
/// <item><description><c>ToPlainText()</c> reduces a line to a plain string, so char-based methods
/// (<c>Substring</c>, the indexer, <c>GetStyleAt</c>) are checked against plain-string operations.</description></item>
/// <item><description>The per-character <c>(char, style)</c> sequence is the model for the
/// column-based <c>SliceByColumns</c>: a slice must be a contiguous run of that sequence. This
/// catches dropped, reordered, or restyled content across segment boundaries (the corruption class
/// of PR #351) without depending on how cells group across segments.</description></item>
/// </list>
/// <para>
/// WHY NOT COMPARE SliceByColumns TO DisplayWidth DIRECTLY. StyledLine measures each segment on its
/// own, so a combining mark in a separate segment does not merge with a base in the previous
/// segment. That is a deliberate per-segment model, not a bug, so an exact equality with the joined
/// string would falsely fail. The contiguous-run rule is the correct, robust form.
/// </para>
/// </remarks>
public class StyledLinePropertyTests
{
    private const int Iter = 5_000;

    // Builds a string from Unicode code points, so the source stays pure ASCII.
    private static string Cp(params int[] codepoints) => string.Concat(codepoints.Select(char.ConvertFromUtf32));

    // ---- Generators ----

    // A few distinct styles, so adjacent segments sometimes share a style (which must coalesce) and
    // sometimes differ.
    private static readonly TextStyle[] Styles =
    {
        TextStyle.Default,
        new(Color.Red),
        new(Color.Green, Color.Default, TextDecoration.Bold),
        new(Color.Default, Color.Blue),
    };

    // Text runs of different shapes. None starts with a combining mark or a bare selector, so
    // joining two runs never forms a new combined cell across the segment boundary. Each run is a
    // complete unit.
    private static readonly string[] TextRuns =
    {
        "a", "ab", "abc", " ", "z",
        Cp(0x4E2D), Cp(0x4E2D, 0x6587),        // 中, 中文 (wide)
        Cp(0x65, 0x0301),                      // e + combining acute (one cell)
        Cp(0x1F600), Cp(0x1F600, 0x1F389),     // emoji (surrogate pairs)
    };

    private static readonly Gen<StyledSegment> SegmentGen =
        from text in Gen.OneOfConst(TextRuns)
        from style in Gen.OneOfConst(Styles)
        select new StyledSegment(text, style);

    // A line built from 0..6 segments. The constructor coalesces adjacent same-style segments, so
    // the resulting line is already in canonical form.
    private static readonly Gen<StyledLine> LineGen =
        SegmentGen.List[0, 6].Select(segs => new StyledLine(segs));

    // A line plus a valid (start, length) for Substring.
    private static readonly Gen<(StyledLine Line, int I, int N)> LineSub =
        LineGen.SelectMany(line =>
        {
            var len = line.Length;
            return from i in Gen.Int[0, len] from n in Gen.Int[0, len] select (line, i, Math.Min(n, len - i));
        });

    // A line plus an in-range char index (only meaningful when the line is non-empty).
    private static readonly Gen<(StyledLine Line, int I)> LineIndex =
        LineGen.SelectMany(line => Gen.Int[0, Math.Max(0, line.Length - 1)].Select(i => (line, i)));

    // A line plus non-negative column arguments (StyledLine.SliceByColumns throws on negatives; that
    // is checked separately in SL10).
    private static readonly Gen<(StyledLine Line, int Start, int Max)> LineCols =
        from line in LineGen
        from start in Gen.Int[0, 30]
        from max in Gen.OneOf(Gen.Int[0, 30], Gen.Const(int.MaxValue / 2))
        select (line, start, max);

    // ---- Helpers ----

    // Expands a line to its per-character (char, style) sequence. This is the model the slice must
    // respect.
    private static List<(char Ch, TextStyle Style)> Expand(StyledLine line)
    {
        var list = new List<(char, TextStyle)>();
        foreach (var seg in line.Segments)
            foreach (var ch in seg.Text)
                list.Add((ch, seg.Style));
        return list;
    }

    // True when part is a contiguous slice of whole.
    private static bool IsContiguousSubRun(List<(char, TextStyle)> whole, List<(char, TextStyle)> part)
    {
        if (part.Count == 0)
            return true;
        for (var i = 0; i + part.Count <= whole.Count; i++)
            if (whole.Skip(i).Take(part.Count).SequenceEqual(part))
                return true;
        return false;
    }

    // The style of the segment that contains char index i, computed straight from the segments.
    private static TextStyle StyleAt(StyledLine line, int i)
    {
        var pos = 0;
        foreach (var seg in line.Segments)
        {
            if (i < pos + seg.Text.Length)
                return seg.Style;
            pos += seg.Text.Length;
        }
        throw new ArgumentOutOfRangeException(nameof(i));
    }

    // ===== Structure =====

    [Fact]
    public void SL1_Segments_AreCoalescedAndNonEmpty() =>
        // The constructor leaves the line in canonical form: no empty segment, and no two adjacent
        // segments share a style (they would have been merged). Length is the sum of segment
        // lengths. This is what lets the other rules trust the segment layout.
        LineGen.Sample(line =>
        {
            var segs = line.Segments;
            for (var i = 0; i < segs.Count; i++)
            {
                Assert.True(segs[i].Text.Length >= 1);                 // no empty segment
                if (i > 0)
                    Assert.False(segs[i].Style.Equals(segs[i - 1].Style)); // adjacent segments differ
            }
            Assert.Equal(segs.Sum(s => s.Text.Length), line.Length);
        }, iter: Iter);

    [Fact]
    public void SL2_PlainText_ConcatenatesSegments() =>
        // ToPlainText is exactly the segment texts joined, and Length matches that string. This ties
        // the plain-text oracle to the segment content.
        LineGen.Sample(line =>
        {
            Assert.Equal(string.Concat(line.Segments.Select(s => s.Text)), line.ToPlainText());
            Assert.Equal(line.ToPlainText().Length, line.Length);
        }, iter: Iter);

    [Fact]
    public void SL8_ConstructionIsCanonical() =>
        // Rebuilding a line from its own segments yields the same segments. Construction is
        // idempotent, so the coalesced form is a fixed point.
        LineGen.Sample(line =>
            Assert.True(new StyledLine(line.Segments).Segments.SequenceEqual(line.Segments)),
            iter: Iter);

    [Fact]
    public void SL9_Clone_EqualsOriginal() =>
        // A clone has the same text, length, and segments as the original.
        LineGen.Sample(line =>
        {
            var clone = line.Clone();
            Assert.Equal(line.ToPlainText(), clone.ToPlainText());
            Assert.Equal(line.Length, clone.Length);
            Assert.True(line.Segments.SequenceEqual(clone.Segments));
        }, iter: Iter);

    // ===== Substring (char-based) =====

    [Fact]
    public void SL3_Substring_MatchesPlainAndPreservesStyle() =>
        // Substring is char-based, so its plain text equals the plain-string substring, and every
        // character keeps the style it had in the original. This is the cross-segment conservation
        // rule for Substring.
        LineSub.Sample(t =>
        {
            var (line, i, n) = t;
            var sub = line.Substring(i, n);
            Assert.Equal(line.ToPlainText().Substring(i, n), sub.ToPlainText());
            for (var j = 0; j < n; j++)
                Assert.Equal(line.GetStyleAt(i + j), sub.GetStyleAt(j));
        }, iter: Iter);

    [Fact]
    public void SL4_SubstringToEnd_MatchesPlain() =>
        // The Substring(start) overload equals the plain-string substring to the end.
        LineGen.SelectMany(line => Gen.Int[0, line.Length].Select(start => (line, start))).Sample(t =>
        {
            var (line, start) = t;
            Assert.Equal(line.ToPlainText().Substring(start), line.Substring(start).ToPlainText());
        }, iter: Iter);

    // ===== SliceByColumns (column-based) =====

    [Fact]
    public void SL5_SliceByColumns_IsContiguousSubRunWithinWidth() =>
        // A column slice is a contiguous run of the original (char, style) pairs (nothing dropped in
        // the middle, reordered, or restyled), and it fits within the requested width. This is the
        // StyledLine-level guard for the PR #351 corruption class.
        LineCols.Sample(t =>
        {
            var (line, start, max) = t;
            var slice = line.SliceByColumns(start, max);
            Assert.True(IsContiguousSubRun(Expand(line), Expand(slice)));
            Assert.True(DisplayWidth.GetColumnCount(slice.ToPlainText()) <= max);
        }, iter: Iter);

    // ===== Indexer and GetStyleAt =====

    [Fact]
    public void SL6_Indexer_MatchesPlain() =>
        // The char indexer returns the same character as the plain string at that index.
        LineIndex.Sample(t =>
        {
            var (line, i) = t;
            if (line.Length == 0)
                return;
            Assert.Equal(line.ToPlainText()[i], line[i]);
        }, iter: Iter);

    [Fact]
    public void SL7_GetStyleAt_MatchesContainingSegment() =>
        // GetStyleAt returns the style of the segment that contains the index.
        LineIndex.Sample(t =>
        {
            var (line, i) = t;
            if (line.Length == 0)
                return;
            Assert.Equal(StyleAt(line, i), line.GetStyleAt(i));
        }, iter: Iter);

    // ===== Bounds =====

    [Fact]
    public void SL10_OutOfRange_Throws() =>
        // Out-of-range access throws rather than reading past the ends or returning junk. Substring
        // rejects a negative start, a negative length, or a range past the end. The indexer and
        // GetStyleAt reject any index outside [0, Length). SliceByColumns rejects negative
        // arguments. A small iteration count is enough; the behavior does not depend on content.
        LineGen.Sample(line =>
        {
            var len = line.Length;
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = line.Substring(-1, 0); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = line.Substring(0, -1); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = line.Substring(0, len + 1); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = line[-1]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = line[len]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = line.GetStyleAt(-1); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = line.GetStyleAt(len); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = line.SliceByColumns(-1, 5); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = line.SliceByColumns(0, -1); });
        }, iter: 500);
}
