// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using CsCheck;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// Property-based tests for the <see cref="FrameBuffer"/> diff engine (CsCheck).
/// </summary>
/// <remarks>
/// <para>
/// HOW TO READ THIS FILE. Each <c>[Fact]</c> states one rule and hands it to <c>.Sample(...)</c>,
/// which runs the rule over <see cref="Iter"/> random buffer pairs. On a failure CsCheck shrinks to
/// the smallest failing pair and prints a seed; reproduce it with <c>CsCheck_Seed=&lt;seed&gt;</c>.
/// </para>
/// <para>
/// WHAT THE DIFF ENGINE DOES. <see cref="DiffingTerminal"/> keeps a "current" buffer (what is on
/// screen) and a "pending" buffer (what we want). On each frame it calls
/// <c>pending.GetChangedRuns(current)</c> and writes only those runs, so the terminal never redraws
/// unchanged cells. Two things must always hold, or the screen drifts from the model:
/// </para>
/// <list type="number">
/// <item><description>The runs cover exactly the cells that differ, and carry the pending cells.</description></item>
/// <item><description>Writing the runs onto the current buffer reproduces the pending buffer.</description></item>
/// </list>
/// <para>
/// THE ORACLE. The tests compute the changed positions directly from the input cells (an
/// index-by-index compare), so they do not trust <see cref="FrameBuffer.GetChangedCells"/> or
/// <see cref="FrameBuffer.GetChangedRuns"/> to check each other.
/// </para>
/// </remarks>
public class FrameBufferPropertyTests
{
    private const int Iter = 2_000; // random buffer pairs per rule (each builds two grids, so heavier than the DisplayWidth suite)

    // A small palette of distinct cells. It mixes the empty cell, plain characters, two cells with
    // the same character but different styling (so equality must look past the character), a wide
    // character, and a continuation cell. A small set makes both "changed" and "unchanged" common,
    // which exercises the run grouping.
    private static readonly TerminalCell[] CellPalette =
    {
        TerminalCell.Empty,
        TerminalCell.FromChar('a'),
        TerminalCell.FromChar('b'),
        new TerminalCell('x', Color.Red, Color.Default, TextDecoration.None),
        new TerminalCell('x', Color.Green, Color.Default, TextDecoration.Bold), // same char, different style
        new TerminalCell("中", Color.Default, Color.Default, TextDecoration.None),
        TerminalCell.Continuation(Color.Default, Color.Default, TextDecoration.None),
    };

    private static readonly Gen<TerminalCell> CellGen = Gen.OneOfConst(CellPalette);

    // Two buffers of the SAME size (the diff methods require equal dimensions), each filled with
    // random cells laid out row-major. Independent fills give a mix of changed and unchanged cells.
    private static readonly Gen<(int W, int H, TerminalCell[] A, TerminalCell[] B)> TwoBuffers =
        from w in Gen.Int[1, 8]
        from h in Gen.Int[1, 6]
        from a in CellGen.Array[w * h]
        from b in CellGen.Array[w * h]
        select (w, h, a, b);

    // ---- Helpers ----

    private static FrameBuffer Build(int w, int h, TerminalCell[] cells)
    {
        var fb = new FrameBuffer(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                fb.TrySet(x, y, cells[y * w + x]);
        return fb;
    }

    // The changed positions computed straight from the two cell arrays. This is the oracle the tests
    // compare against.
    private static HashSet<(int X, int Y)> ExpectedChanges(int w, int h, TerminalCell[] a, TerminalCell[] b)
    {
        var set = new HashSet<(int, int)>();
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (!a[y * w + x].Equals(b[y * w + x]))
                    set.Add((x, y));
        return set;
    }

    [Fact]
    public void ChangedCells_MatchARawCompare() =>
        // GetChangedCells reports exactly the positions that differ between the two buffers, no more
        // and no fewer, checked against a plain index-by-index compare.
        TwoBuffers.Sample(t =>
        {
            var (w, h, a, b) = t;
            var pending = Build(w, h, b);
            var current = Build(w, h, a);
            var reported = pending.GetChangedCells(current).ToHashSet();
            Assert.Equal(ExpectedChanges(w, h, a, b), reported);
        }, iter: Iter);

    [Fact]
    public void ChangedRuns_CoverExactlyTheChangedCells_AndCarryPending() =>
        // The runs cover the same positions as the raw compare, they carry the pending cells, and
        // they are maximal (two runs on one row never touch, so nothing was split needlessly).
        TwoBuffers.Sample(t =>
        {
            var (w, h, a, b) = t;
            var current = Build(w, h, a);
            var pending = Build(w, h, b);
            var runs = pending.GetChangedRuns(current).ToList();

            // (1) The runs cover exactly the changed positions.
            var runPositions = runs
                .SelectMany(r => Enumerable.Range(0, r.Cells.Length).Select(i => (r.StartX + i, r.Y)))
                .ToHashSet();
            Assert.Equal(ExpectedChanges(w, h, a, b), runPositions);

            // (2) Each run carries the pending cell at each position.
            foreach (var r in runs)
                for (var i = 0; i < r.Cells.Length; i++)
                    Assert.Equal(pending[r.StartX + i, r.Y], r.Cells[i]);

            // (3) Runs on a row are maximal: an unchanged cell separates any two of them.
            foreach (var group in runs.GroupBy(r => r.Y))
            {
                var ordered = group.OrderBy(r => r.StartX).ToList();
                for (var i = 1; i < ordered.Count; i++)
                    Assert.True(ordered[i].StartX > ordered[i - 1].StartX + ordered[i - 1].Cells.Length);
            }
        }, iter: Iter);

    [Fact]
    public void ApplyingRuns_ToCurrent_ReproducesPending() =>
        // The core contract DiffingTerminal relies on: start from the current buffer, write only the
        // changed runs, and you get the pending buffer back, cell for cell. A dropped or misplaced
        // cell in the diff would leave a stale cell here.
        TwoBuffers.Sample(t =>
        {
            var (w, h, a, b) = t;
            var current = Build(w, h, a);
            var pending = Build(w, h, b);

            var applied = new FrameBuffer(w, h);
            applied.CopyFrom(current);
            foreach (var (y, startX, cells) in pending.GetChangedRuns(current))
                for (var i = 0; i < cells.Length; i++)
                    applied.TrySet(startX + i, y, cells[i]);

            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    Assert.Equal(pending[x, y], applied[x, y]);
        }, iter: Iter);

    [Fact]
    public void IdenticalBuffers_ProduceNoChanges() =>
        // If nothing changed, the diff must be empty. A false positive here would redraw the whole
        // screen every frame and bring back the flicker the diff engine removes.
        (from w in Gen.Int[1, 8] from h in Gen.Int[1, 6] from a in CellGen.Array[w * h] select (w, h, a)).Sample(t =>
        {
            var (w, h, a) = t;
            var buf = Build(w, h, a);
            var copy = new FrameBuffer(w, h);
            copy.CopyFrom(buf);
            Assert.Empty(buf.GetChangedCells(copy));
            Assert.Empty(buf.GetChangedRuns(copy));
        }, iter: Iter);

    [Fact]
    public void MismatchedDimensions_Throw() =>
        // Diffing buffers of different sizes is a programming error and must throw, not read out of
        // bounds. Both methods are iterators, so the throw happens on enumeration.
        (from w1 in Gen.Int[1, 6]
         from h1 in Gen.Int[1, 6]
         from w2 in Gen.Int[1, 6]
         from h2 in Gen.Int[1, 6]
         select (w1, h1, w2, h2))
        .Where(t => t.w1 != t.w2 || t.h1 != t.h2)
        .Sample(t =>
        {
            var (w1, h1, w2, h2) = t;
            var a = new FrameBuffer(w1, h1);
            var b = new FrameBuffer(w2, h2);
            Assert.Throws<ArgumentException>(() => a.GetChangedCells(b).ToList());
            Assert.Throws<ArgumentException>(() => a.GetChangedRuns(b).ToList());
        }, iter: Iter);
}
