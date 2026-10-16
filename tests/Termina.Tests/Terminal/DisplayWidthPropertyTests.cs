// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using CsCheck;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// Property-based tests for <see cref="DisplayWidth"/> (CsCheck).
/// </summary>
/// <remarks>
/// <para>
/// HOW TO READ THIS FILE. Each <c>[Fact]</c> states one rule and hands it to <c>.Sample(...)</c>.
/// <c>Sample</c> does NOT run once. It runs the body over <see cref="Iter"/> random inputs drawn
/// from a generator. If any input breaks the rule, CsCheck shrinks it to the smallest failing
/// input and prints a seed, for example: <c>Set seed: "7BaZFgowfiS9" ... (3 shrinks)</c>. To
/// reproduce that exact case, set the environment variable <c>CsCheck_Seed=7BaZFgowfiS9</c> and
/// run the one test.
/// </para>
/// <para>
/// WHY ASSERTIONS, NOT A BOOL. CsCheck accepts either a bool predicate or a body that asserts.
/// These tests assert, so a failure prints the expected and actual values, not only the input.
/// </para>
/// <para>
/// THE ORACLE. Every rule is stated against <see cref="DisplayWidth.EnumerateCells"/>, the cell
/// model that all the other methods must agree with. So the tests do not hand-write expected
/// output; they compare a method to the cell model. A "cell" is one display unit (a letter, a
/// letter with its marks, or one emoji). A "column" is one terminal column; a cell uses 0, 1, or 2
/// columns. A "boundary" is the point between two cells.
/// </para>
/// <para>
/// THE GROUPS. A: cell decomposition. B: width composition. C: left truncation. D: slicing.
/// E: index and column mapping. F: navigation and cursor safety. G: sanitization. H: width per
/// character type. I: totality (no method throws).
/// </para>
/// </remarks>
public class DisplayWidthPropertyTests
{
    private const int Iter = 10_000; // random inputs per rule
    private const char Esc = (char)0x1B;         // escape
    private const char Bel = (char)0x07;         // bell
    private const char Vs16 = (char)0xFE0F;      // emoji variation selector
    private const char Keycap = (char)0x20E3;    // combining keycap
    private const char Zwj = (char)0x200D;       // zero-width joiner
    private const char Cjk = (char)0x4E2D;       // a wide CJK character

    // Builds a string from Unicode code points. This keeps the source pure ASCII, so invisible
    // characters (combining marks, selectors, controls) never sit unreadable in the file.
    private static string Cp(params int[] codepoints) => string.Concat(codepoints.Select(char.ConvertFromUtf32));

    // ---- Generators ----
    //
    // There are two text domains, on purpose.
    //   * CellText  - built from whole cells, so boundaries are always clean. Used where a rule
    //                 needs well-formed text (equality with the cut methods, rejoin, additivity).
    //   * FuzzText  - arbitrary UTF-16 including escapes, controls, and lone surrogates. Used to
    //                 prove the methods never throw and that the width bounds always hold, even on
    //                 broken input.
    // Most rules use AnyText (the union), because each rule is stated against the cell model of the
    // actual text, so it holds for both domains.

    // Whole cells. Each item is one complete display unit, chosen to exercise every width class.
    private static readonly string[] CellPalette =
    {
        "a", "B", "7", "#", " ", "z",              // narrow (1 column)
        "中", "文", "あ", "한", "Ａ",                // wide (2 columns)
        Cp(0x65, 0x0301), Cp(0x6F, 0x0308),        // base + combining mark (1 column; the mark is 0)
        Cp(0x1F600), Cp(0x1F389), Cp(0x20000),     // supplementary (surrogate pairs)
        Cp(0x2600, 0xFE0F), Cp(0x270B, 0xFE0F),    // emoji + variation selector (2 columns)
        Cp(0x31, 0xFE0F, 0x20E3),                  // keycap sequence (2 columns)
    };

    // A text built by joining 0..8 whole cells. Boundaries are clean by construction.
    private static readonly Gen<string> CellText =
        Gen.OneOfConst(CellPalette).List[0, 8].Select(parts => string.Concat(parts));

    // A hostile UTF-16 code unit: arbitrary chars, plus specific escapes, controls, selectors, and
    // both halves of surrogate pairs (so lone, unpaired surrogates appear too).
    private static readonly Gen<char> FuzzChar = Gen.OneOf(
        Gen.Char,
        Gen.OneOfConst(Esc, '[', ']', Bel, 'm', '\n', '\t', '\r', '\0', ' ', 'a', Cjk, Vs16, Keycap, Zwj),
        Gen.OneOfConst('\uD83D', '\uDE00', '\uD800', '\uDBFF', '\uDC00', '\uDFFF'));

    // A text of 0..24 hostile code units. May contain ill-formed UTF-16.
    private static readonly Gen<string> FuzzText =
        FuzzChar.Array[0, 24].Select(chars => new string(chars));

    // Either kind of text.
    private static readonly Gen<string> AnyText = Gen.OneOf(CellText, FuzzText);

    // Column and index arguments. These deliberately include negative, zero, and int.MaxValue so
    // the guard paths (maxColumns <= 0, index past the end) are exercised too.
    private static readonly Gen<int> WidthArg = Gen.OneOf(Gen.Int[-3, 40], Gen.Const(0), Gen.Const(int.MaxValue));
    private static readonly Gen<int> IndexArg = Gen.OneOf(Gen.Int[-3, 40], Gen.Const(int.MaxValue));

    // A text paired with one of ITS OWN cell boundaries. Splitting at a real boundary never cuts a
    // surrogate pair or a combining sequence, which the additivity and rejoin rules rely on.
    private static readonly Gen<(string Text, int Boundary)> TextWithBoundary =
        AnyText.SelectMany(s =>
        {
            var bounds = Boundaries(s);
            return Gen.Int[0, bounds.Count - 1].Select(k => (s, bounds[k]));
        });

    private static readonly Gen<(string Text, int N)> TextAndWidth =
        from s in AnyText from n in WidthArg select (s, n);

    // ---- Helpers ----

    // The UTF-16 index of every cell start, plus the end of the text. These are the only indexes a
    // cursor may safely land on.
    private static List<int> Boundaries(string s)
    {
        var list = new List<int>();
        foreach (var cell in DisplayWidth.EnumerateCells(s))
            list.Add(cell.StartIndex);
        list.Add(s.Length); // the end of the text is always a boundary
        return list;
    }

    private static HashSet<int> BoundarySet(string s) => new(Boundaries(s));

    // True when the first cell of the text uses zero columns (a leading combining mark, format
    // character, or control character). SliceByColumns drops such a leading cell at column 0, but
    // TruncateToColumns keeps it. The two equality rules (D3, D4) scope this edge out.
    private static bool StartsWithZeroWidthCell(string s) =>
        s.Length > 0 && DisplayWidth.EnumerateCells(s).First().ColumnWidth == 0;

    private static List<string> CellTexts(string s) =>
        DisplayWidth.EnumerateCells(s).Select(c => c.Text).ToList();

    // True when the slice is one continuous run of whole cells taken in order from the source. This
    // is how the tests detect the corruption class of PR #351: dropping a middle cell, reordering
    // cells, or splitting a cell would all make the slice stop being a contiguous run.
    private static bool IsContiguousCellRun(string s, string slice)
    {
        var whole = CellTexts(s);
        var part = CellTexts(slice);
        if (part.Count == 0)
            return true;
        for (var i = 0; i + part.Count <= whole.Count; i++)
            if (whole.Skip(i).Take(part.Count).SequenceEqual(part))
                return true;
        return false;
    }

    // ===== A. Cell decomposition =====
    // These pin the cell model itself. Everything else is stated against it, so if these are wrong,
    // the whole suite rests on a bad foundation.

    [Fact]
    public void A1_Cells_PartitionTheText() =>
        // The cells tile the text exactly: each starts where the previous ended, none is empty, and
        // joining them rebuilds the input. This is the core "no character is lost, added, or
        // reordered" rule at the source.
        AnyText.Sample(s =>
        {
            var pos = 0;
            var cells = DisplayWidth.EnumerateCells(s).ToList();
            foreach (var c in cells)
            {
                Assert.Equal(pos, c.StartIndex);   // no gap and no overlap
                Assert.True(c.Length >= 1);        // no empty cell (would loop forever elsewhere)
                pos += c.Length;
            }
            Assert.Equal(s.Length, pos);           // the cells reach the end
            Assert.Equal(s, string.Concat(cells.Select(c => c.Text)));
        }, iter: Iter);

    [Fact]
    public void A2_CellWidth_IsZeroOneOrTwo() =>
        // A terminal cell is 0, 1, or 2 columns. A value outside that range means a glyph that
        // cannot be placed, so later column math (wrapping, cursor) would be wrong.
        AnyText.Sample(s =>
        {
            foreach (var c in DisplayWidth.EnumerateCells(s))
                Assert.InRange(c.ColumnWidth, 0, 2);
        }, iter: Iter);

    [Fact]
    public void A3_SumOfCells_EqualsColumnCount() =>
        // GetColumnCount must equal the sum of the per-cell widths. This ties the single public
        // width number to the cell model, so the two can never drift apart.
        AnyText.Sample(s =>
            Assert.Equal(DisplayWidth.GetColumnCount(s), DisplayWidth.EnumerateCells(s).Sum(c => c.ColumnWidth)),
            iter: Iter);

    [Fact]
    public void A4_CharWidth_MatchesStringWidth() =>
        // The char overload is only a convenience. It must agree with measuring the one-char string,
        // so callers get the same answer either way.
        Gen.Char.Sample(c =>
            Assert.Equal(DisplayWidth.GetColumnCount(c.ToString()), DisplayWidth.GetColumnCount(c)),
            iter: Iter);

    [Fact]
    public void A5_AsciiText_MatchesTheCellWalk() =>
        // GetColumnCount skips the cell walk for printable ASCII. Text near the edges of that range
        // (controls, DEL, Latin-1, combining marks, keycap parts) must give the same count as the walk.
        Gen.OneOf(
                Gen.Char['\u0020', '\u007E'],
                Gen.OneOfConst('\u001F', '\u007F', '\u0080', '\u00A0', '\u00E9', '\u0301', '\n', '\t', Vs16, Keycap, Cjk))
            .Array[0, 40]
            .Select(chars => new string(chars))
            .Sample(s =>
                Assert.Equal(DisplayWidth.EnumerateCells(s).Sum(c => c.ColumnWidth), DisplayWidth.GetColumnCount(s)),
                iter: Iter);

    // ===== B. Width composition =====

    [Fact]
    public void B1_Width_IsAdditiveAtABoundary() =>
        // Cut at a boundary; the two halves' widths sum to the whole. We split only at a real cell
        // boundary on purpose: cutting through a surrogate pair or an "emoji + selector" cell would
        // break additivity (0 + 0 vs 2, or 1 + 0 vs 2), because those halves measure differently
        // apart than together. That is a property of Unicode, not a bug, so the generator avoids it.
        TextWithBoundary.Sample(t =>
        {
            var (s, i) = t;
            Assert.Equal(
                DisplayWidth.GetColumnCount(s),
                DisplayWidth.GetColumnCount(s[..i]) + DisplayWidth.GetColumnCount(s[i..]));
        }, iter: Iter);

    // ===== C. Left truncation =====

    [Fact]
    public void C1_Truncate_DoesNotExceedWidth() =>
        // The kept left part never spills past N columns. Math.Max(0, n) accounts for a negative N,
        // which the method treats as zero.
        TextAndWidth.Sample(t =>
        {
            var (s, n) = t;
            Assert.True(DisplayWidth.GetColumnCount(DisplayWidth.TruncateToColumns(s, n)) <= Math.Max(0, n));
        }, iter: Iter);

    [Fact]
    public void C2_Truncate_IsAPrefixAndMatchesIndex() =>
        // The result is a genuine prefix (never re-ordered text), and it agrees with the index the
        // method reports for the same width. Tying the two together stops them drifting apart.
        TextAndWidth.Sample(t =>
        {
            var (s, n) = t;
            var kept = DisplayWidth.TruncateToColumns(s, n);
            Assert.StartsWith(kept, s, StringComparison.Ordinal);
            Assert.Equal(s[..DisplayWidth.GetStringIndexForColumnCount(s, n)], kept);
        }, iter: Iter);

    [Fact]
    public void C3_Truncate_IsIdempotent() =>
        // Cutting an already-cut result to the same width must be a no-op. If it were not, repeated
        // layout passes could keep shrinking the same text.
        TextAndWidth.Sample(t =>
        {
            var (s, n) = t;
            var once = DisplayWidth.TruncateToColumns(s, n);
            Assert.Equal(once, DisplayWidth.TruncateToColumns(once, n));
        }, iter: Iter);

    [Fact]
    public void C4_Truncate_KeepsAllWhenItFits() =>
        // If the text already fits, truncation must not remove anything. Guarded to N >= 1 because
        // TruncateToColumns returns "" for N <= 0 by design (the maxColumns <= 0 guard), even for
        // zero-width text.
        TextAndWidth.Sample(t =>
        {
            var (s, n) = t;
            if (n >= 1 && DisplayWidth.GetColumnCount(s) <= n)
                Assert.Equal(s, DisplayWidth.TruncateToColumns(s, n));
        }, iter: Iter);

    [Fact]
    public void C5_Truncate_IsMaximal() =>
        // Truncation keeps as much as possible: the first cell it dropped would have pushed the
        // width past N. This catches an "off by one early" bug that C1 alone would miss (returning
        // less than fits still satisfies the bound). Guarded to N >= 1 to avoid the N <= 0 guard,
        // and skipped when the whole text fits (there is no dropped cell then). The dropped cell has
        // width >= 1 because zero-width cells are always kept.
        TextAndWidth.Sample(t =>
        {
            var (s, n) = t;
            if (n < 1)
                return;
            var idx = DisplayWidth.GetStringIndexForColumnCount(s, n);
            if (idx >= s.Length)
                return;
            var kept = DisplayWidth.GetColumnCount(s[..idx]);
            var next = DisplayWidth.EnumerateCells(s[idx..]).First();
            Assert.True(kept + next.ColumnWidth > n);
        }, iter: Iter);

    // ===== D. Slicing =====

    [Fact]
    public void D1_Slice_DoesNotExceedWidth() =>
        // A middle slice never spills past N columns, for any start (including negative or past the
        // end, which yield an empty slice).
        (from s in AnyText from start in IndexArg from n in WidthArg select (s, start, n)).Sample(t =>
        {
            var (s, start, n) = t;
            Assert.True(DisplayWidth.GetColumnCount(DisplayWidth.SliceByColumns(s, start, n)) <= Math.Max(0, n));
        }, iter: Iter);

    [Fact]
    public void D2_Slice_IsContiguousCellRun() =>
        // A slice is one continuous run of whole cells, in order. Dropping a middle cell, reordering
        // cells, or splitting a cell would all fail this. This is the exact corruption class that
        // PR #351 fixed, now stated as a universal rule over random input.
        (from s in AnyText from start in IndexArg from n in WidthArg select (s, start, n)).Sample(t =>
        {
            var (s, start, n) = t;
            var slice = DisplayWidth.SliceByColumns(s, start, n);
            Assert.True(IsContiguousCellRun(s, slice));
        }, iter: Iter);

    [Fact]
    public void D3_SliceFromZero_EqualsTruncate() =>
        // Slicing from column 0 is the same as truncating from the left. It ties two methods that
        // must agree at the start.
        // Known divergence: when the text begins with a zero-width cell, SliceByColumns drops it
        // while TruncateToColumns keeps it. That edge is scoped out and left for a separate decision.
        TextAndWidth.Sample(t =>
        {
            var (s, n) = t;
            if (StartsWithZeroWidthCell(s))
                return;
            Assert.Equal(DisplayWidth.TruncateToColumns(s, n), DisplayWidth.SliceByColumns(s, 0, n));
        }, iter: Iter);

    [Fact]
    public void D4_Slices_RejoinAtABoundary() =>
        // Cut the text into a left slice and a right slice at a boundary, then join them: you get the
        // original back. No cell is lost or duplicated at the seam. kCols is a column boundary, so no
        // wide cell straddles the cut.
        TextWithBoundary.Sample(t =>
        {
            var (s, k) = t;
            if (StartsWithZeroWidthCell(s))
                return; // a leading zero-width cell is dropped by the left slice (see D3)
            var kCols = DisplayWidth.GetColumnCount(s[..k]);
            if (kCols < 1)
                return; // a zero-column left part meets the maxColumns <= 0 guard (a separate case)
            var left = DisplayWidth.SliceByColumns(s, 0, kCols);
            var right = DisplayWidth.SliceByColumns(s, kCols, int.MaxValue / 2);
            Assert.Equal(s, left + right);
        }, iter: Iter);

    [Fact]
    public void D5_Slice_DropsTheStraddlingWideCellAtStart() =>
        // A terminal cannot draw half of a wide glyph. So when the start column lands inside a wide
        // cell, that one cell is dropped and the slice begins at the next boundary. This test starts
        // one column inside the first wide cell and checks the slice is exactly the cells after it.
        CellText.Sample(s =>
        {
            var cells = DisplayWidth.EnumerateCells(s).ToList();
            var col = 0;
            var wideIndex = -1;
            var wideCol = 0;
            for (var k = 0; k < cells.Count; k++)
            {
                if (cells[k].ColumnWidth == 2)
                {
                    wideIndex = k;
                    wideCol = col;
                    break;
                }
                col += cells[k].ColumnWidth;
            }
            if (wideIndex < 0)
                return; // this sample has no wide cell to straddle
            var startInside = wideCol + 1; // a column inside the wide cell
            var slice = DisplayWidth.SliceByColumns(s, startInside, int.MaxValue / 2);
            var expected = string.Concat(cells.Skip(wideIndex + 1).Select(c => c.Text));
            Assert.Equal(expected, slice);
        }, iter: Iter);

    [Fact]
    public void D6_TruncateStart_IsSuffixWithinWidthAndMaximal() =>
        // The right-hand cut keeps a real suffix that fits in N columns and keeps as much as
        // possible (the next cell to the left would overflow). Guarded like C5.
        TextAndWidth.Sample(t =>
        {
            var (s, n) = t;
            var kept = DisplayWidth.TruncateStartToColumns(s, n);
            Assert.True(DisplayWidth.GetColumnCount(kept) <= Math.Max(0, n));
            Assert.EndsWith(kept, s, StringComparison.Ordinal);
            if (n >= 1 && kept.Length < s.Length)
            {
                var leftPart = s[..^kept.Length];
                var prev = DisplayWidth.EnumerateCells(leftPart).Last();
                Assert.True(DisplayWidth.GetColumnCount(kept) + prev.ColumnWidth > n);
            }
        }, iter: Iter);

    // ===== E. Index and column mapping =====
    // These cover the two directions of "where is the cursor": a UTF-16 index and a display column.

    [Fact]
    public void E1_IndexForColumns_TiesToTruncateAndBoundary() =>
        // The index that fits N columns equals the length of the left cut, sits on a cell boundary,
        // and its prefix fits N. This keeps the index method, the cut method, and the cell model
        // consistent with one another.
        TextAndWidth.Sample(t =>
        {
            var (s, n) = t;
            var idx = DisplayWidth.GetStringIndexForColumnCount(s, n);
            Assert.Equal(DisplayWidth.TruncateToColumns(s, n).Length, idx);
            Assert.Contains(idx, BoundarySet(s));
            Assert.True(DisplayWidth.GetColumnCount(s[..idx]) <= Math.Max(0, n));
        }, iter: Iter);

    [Fact]
    public void E2_CursorColumn_IsMonotonic() =>
        // Moving the index forward never lowers the column. A non-monotonic mapping would make the
        // cursor jump backward on screen while moving forward in the text.
        (from s in AnyText from i in IndexArg from j in IndexArg select (s, i, j)).Sample(t =>
        {
            var (s, i, j) = t;
            if (i > j)
                (i, j) = (j, i);
            Assert.True(DisplayWidth.CursorPositionToColumn(s, i) <= DisplayWidth.CursorPositionToColumn(s, j));
        }, iter: Iter);

    [Fact]
    public void E3_CursorColumn_Endpoints() =>
        // The start is column 0, the end is the full width, and an index past the end clamps to the
        // full width (it does not run off).
        AnyText.Sample(s =>
        {
            Assert.Equal(0, DisplayWidth.CursorPositionToColumn(s, 0));
            Assert.Equal(DisplayWidth.GetColumnCount(s), DisplayWidth.CursorPositionToColumn(s, s.Length));
            Assert.Equal(DisplayWidth.GetColumnCount(s), DisplayWidth.CursorPositionToColumn(s, s.Length + 5));
        }, iter: Iter);

    [Fact]
    public void E4_CursorColumn_AtBoundary_EqualsPrefixWidth() =>
        // At a boundary, the column is exactly the width of the text before it. This is the precise
        // form of the mapping (an index inside a wide cell floors to that cell's start column).
        TextWithBoundary.Sample(t =>
        {
            var (s, i) = t;
            Assert.Equal(DisplayWidth.GetColumnCount(s[..i]), DisplayWidth.CursorPositionToColumn(s, i));
        }, iter: Iter);

    // ===== F. Navigation and boundary safety =====
    // The rule behind this whole group: a cursor must never land inside a glyph. If it did, an edit
    // would split a surrogate pair or a combining sequence and corrupt the text.

    [Fact]
    public void F1_Clamp_LandsOnBoundary() =>
        // Clamp turns any index, even a negative or out-of-range one, into a safe boundary.
        (from s in AnyText from i in IndexArg select (s, i)).Sample(t =>
        {
            var (s, i) = t;
            Assert.Contains(DisplayWidth.ClampToTextElementBoundary(s, i), BoundarySet(s));
        }, iter: Iter);

    [Fact]
    public void F2_NextAndPrevious_LandOnBoundaries() =>
        // Both step methods always return a boundary, so repeated left/right moves keep the cursor
        // valid no matter where it starts.
        (from s in AnyText from i in IndexArg select (s, i)).Sample(t =>
        {
            var (s, i) = t;
            var set = BoundarySet(s);
            Assert.Contains(DisplayWidth.GetNextTextElementIndex(s, i), set);
            Assert.Contains(DisplayWidth.GetPreviousTextElementIndex(s, i), set);
        }, iter: Iter);

    [Fact]
    public void F3_NextAndPrevious_DirectionAndFixpoints() =>
        // Next moves forward and stops at the end; Previous moves back and stops at the start. The
        // fixpoints at the ends stop a held arrow key from running off the text.
        (from s in AnyText from i in Gen.Int[0, 40] select (s, Math.Min(i, s.Length))).Sample(t =>
        {
            var (s, i) = t;
            Assert.True(DisplayWidth.GetNextTextElementIndex(s, i) >= i);
            Assert.True(DisplayWidth.GetPreviousTextElementIndex(s, i) <= i);
            Assert.Equal(s.Length, DisplayWidth.GetNextTextElementIndex(s, s.Length));
            Assert.Equal(0, DisplayWidth.GetPreviousTextElementIndex(s, 0));
        }, iter: Iter);

    [Fact]
    public void F4_Next_VisitsAllBoundariesInOrder() =>
        // Stepping Next from the start visits every boundary once, in order; Previous from the end
        // mirrors it. The "must make progress" checks also prove the stepping cannot loop forever.
        AnyText.Sample(s =>
        {
            var ends = DisplayWidth.EnumerateCells(s).Select(c => c.StartIndex + c.Length).ToList();
            var forward = new List<int>();
            var idx = 0;
            while (idx < s.Length)
            {
                var next = DisplayWidth.GetNextTextElementIndex(s, idx);
                Assert.True(next > idx); // Next must make progress
                idx = next;
                forward.Add(idx);
            }
            Assert.Equal(ends, forward);

            var starts = DisplayWidth.EnumerateCells(s).Select(c => c.StartIndex).Reverse().ToList();
            var back = new List<int>();
            idx = s.Length;
            while (idx > 0)
            {
                var prev = DisplayWidth.GetPreviousTextElementIndex(s, idx);
                Assert.True(prev < idx); // Previous must make progress
                idx = prev;
                back.Add(idx);
            }
            Assert.Equal(starts, back);
        }, iter: Iter);

    [Fact]
    public void F5_TextElementAt_ReturnsContainingCell() =>
        // The glyph under the cursor is the whole cell that holds the index (not a half cell). Past
        // the end it is a space, which is what a terminal draws for an empty cursor cell.
        (from s in AnyText from i in IndexArg select (s, i)).Sample(t =>
        {
            var (s, i) = t;
            var got = DisplayWidth.GetTextElementAt(s, i);
            if (s.Length == 0 || i >= s.Length)
            {
                Assert.Equal(" ", got);
            }
            else
            {
                var ci = Math.Max(0, i);
                var cell = DisplayWidth.EnumerateCells(s).First(c => ci >= c.StartIndex && ci < c.StartIndex + c.Length);
                Assert.Equal(cell.Text, got);
            }
        }, iter: Iter);

    // ===== G. Sanitization =====

    [Fact]
    public void G1_Sanitize_RemovesControlAndEscape() =>
        // Cleaning user text must leave no escape and no control character, so raw input cannot move
        // the cursor or recolor the screen when it is drawn as content.
        FuzzText.Sample(s =>
        {
            var clean = DisplayWidth.SanitizeTerminalText(s);
            Assert.False(clean.Contains(Esc));
            Assert.All(clean, ch => Assert.False(char.IsControl(ch)));
        }, iter: Iter);

    [Fact]
    public void G2_Sanitize_IsIdempotentAndKeepsCleanText() =>
        // Cleaning twice is the same as cleaning once, and already-clean text is returned unchanged.
        // Note we do NOT test that width stays the same: cleaning turns a newline, tab, or return
        // (0 columns) into a space (1 column), so width can grow. That is correct behavior.
        FuzzText.Sample(s =>
        {
            var once = DisplayWidth.SanitizeTerminalText(s);
            Assert.Equal(once, DisplayWidth.SanitizeTerminalText(once));
            if (s.All(ch => ch != Esc && !char.IsControl(ch)))
                Assert.Equal(s, once);
        }, iter: Iter);

    // ===== H. Width classification =====
    // These pin the width tables (the range checks in IsWideOrFullwidth and the emoji rules). They
    // sample whole ranges, so a mutated range boundary is very likely to be caught.

    [Fact]
    public void H1_WideRanges_UseTwoColumns() =>
        // Characters from the East Asian wide ranges use 2 columns.
        Gen.OneOf(
            Gen.Int[0x4E00, 0x9FFF],   // CJK unified ideographs
            Gen.Int[0x3041, 0x3096],   // hiragana
            Gen.Int[0xAC00, 0xD7A3],   // Hangul syllables
            Gen.Int[0xFF21, 0xFF3A])   // fullwidth Latin capitals
        .Sample(cp => Assert.Equal(2, DisplayWidth.GetColumnCount(((char)cp).ToString())), iter: Iter);

    [Fact]
    public void H2_AsciiPrintable_UsesOneColumn() =>
        // A normal ASCII character (space through tilde) uses 1 column.
        Gen.Int[0x20, 0x7E].Sample(cp => Assert.Equal(1, DisplayWidth.GetColumnCount(((char)cp).ToString())), iter: Iter);

    [Fact]
    public void H3_ZeroWidthCategories_UseZeroColumns() =>
        // A combining mark, a format character, or a control character uses 0 columns.
        // A lone surrogate is deliberately NOT here: string enumeration replaces it with U+FFFD,
        // which measures as 1 column, so it is not zero-width in practice.
        Gen.OneOf(
            Gen.Int[0x0300, 0x036F],                        // combining marks
            Gen.OneOfConst(0x200B, 0x200D, 0x2060, 0xFEFF), // format characters
            Gen.Int[0x0000, 0x001F])                        // C0 control characters
        .Sample(cp => Assert.Equal(0, DisplayWidth.GetColumnCount(((char)cp).ToString())), iter: Iter);

    [Fact]
    public void H4_EmojiSequences_UseTwoColumns() =>
        // A default-presentation emoji, an emoji forced wide by a variation selector, and a keycap
        // all use 2 columns.
        Gen.OneOfConst(Cp(0x1F600), Cp(0x1F389), Cp(0x2600, 0xFE0F), Cp(0x270B, 0xFE0F), Cp(0x31, 0xFE0F, 0x20E3), Cp(0x20000))
        .Sample(e => Assert.Equal(2, DisplayWidth.GetColumnCount(e)), iter: Iter);

    // ===== I. Totality =====

    [Fact]
    public void I1_NoMethodThrows_OnAnyInput() =>
        // No method throws for any text (including ill-formed UTF-16) or any int argument (negative,
        // zero, or int.MaxValue). The body simply calls everything; the rule is that nothing throws.
        (from s in AnyText from a in IndexArg from b in WidthArg select (s, a, b)).Sample(t =>
        {
            var (s, a, b) = t;
            _ = DisplayWidth.GetColumnCount(s);
            _ = DisplayWidth.EnumerateCells(s).ToList();
            _ = DisplayWidth.SanitizeTerminalText(s);
            _ = DisplayWidth.TruncateToColumns(s, b);
            _ = DisplayWidth.TruncateStartToColumns(s, b);
            _ = DisplayWidth.SliceByColumns(s, a, b);
            _ = DisplayWidth.GetStringIndexForColumnCount(s, b);
            _ = DisplayWidth.CursorPositionToColumn(s, a);
            _ = DisplayWidth.ClampToTextElementBoundary(s, a);
            _ = DisplayWidth.GetPreviousTextElementIndex(s, a);
            _ = DisplayWidth.GetNextTextElementIndex(s, a);
            _ = DisplayWidth.GetTextElementAt(s, a);
        }, iter: Iter);
}
