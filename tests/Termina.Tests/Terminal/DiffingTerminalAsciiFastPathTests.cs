// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Text;
using CsCheck;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// Tests for the printable-ASCII fast path in <see cref="DiffingTerminal.Write(string)"/>.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IS UNDER TEST. <c>Write(string)</c> takes a fast path when every char of the text is
/// printable ASCII (0x20 to 0x7E). The fast path skips the grapheme parse. All other text takes
/// the general loop, <c>WriteTextElements</c>. Both paths end in the same
/// <c>WriteTextElementToBuffer</c>, so the fast path is correct only if each printable ASCII char is
/// one text element of width 1 and if no other char takes the fast path.
/// </para>
/// <para>
/// THE ORACLE. The property test drives two <see cref="DiffingTerminal"/> instances through the same
/// operations. The first terminal uses <c>Write</c> (with the fast path). The second terminal calls
/// <c>WriteTextElements</c> directly, so it never takes the fast path. After each operation the test
/// compares the cursor and each cell of the pending frame and the current frame. After each flush it
/// also compares the ANSI-level output that the diff sent to the inner terminal.
/// </para>
/// <para>
/// REPRODUCE A FAILURE. CsCheck shrinks to the smallest failing scenario and prints a seed. Set the
/// environment variable <c>CsCheck_Seed=&lt;seed&gt;</c> to run that case again.
/// </para>
/// </remarks>
public class DiffingTerminalAsciiFastPathTests
{
    private const int Iter = 100_000; // scenarios; each has up to 25 operations on a terminal of at most 12x5

    // The tests read private state of DiffingTerminal (cursor and frames) because the type has no
    // public accessor for it. A renamed field fails the type initializer with a clear message.
    private static readonly FieldInfo PendingField = Field("_pendingFrame");
    private static readonly FieldInfo CurrentField = Field("_currentFrame");
    private static readonly FieldInfo CursorXField = Field("_cursorX");
    private static readonly FieldInfo CursorYField = Field("_cursorY");

    private static FieldInfo Field(string name) =>
        typeof(DiffingTerminal).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"DiffingTerminal has no field '{name}'. Update this test.");

    private static (int X, int Y) Cursor(DiffingTerminal t) =>
        ((int)CursorXField.GetValue(t)!, (int)CursorYField.GetValue(t)!);

    private static TerminalCell Pending(DiffingTerminal t, int x, int y) =>
        ((FrameBuffer)PendingField.GetValue(t)!).GetSafe(x, y);

    // ===== Deterministic cases: each kills a specific planted bug in the fast path =====

    [Fact]
    public void Write_DelBetweenAsciiChars_IsDroppedAndTakesNoColumn()
    {
        // DEL (0x7F) has width 0. If the fast path range included it, DEL would become a visible cell.
        var t = new DiffingTerminal(new VirtualTerminal(10, 2));

        t.Write("ab\u007Fcd");

        Assert.Equal("a", Pending(t, 0, 0).Text);
        Assert.Equal("b", Pending(t, 1, 0).Text);
        Assert.Equal("c", Pending(t, 2, 0).Text);
        Assert.Equal("d", Pending(t, 3, 0).Text);
        Assert.Equal((4, 0), Cursor(t));
    }

    [Fact]
    public void Write_SpacesOverOldText_ReplaceEveryCellWithTheCurrentBackground()
    {
        // A panel fill writes spaces over old text. A space must write a cell and not only move the cursor.
        var t = new DiffingTerminal(new VirtualTerminal(10, 2));
        t.Write("xxxx");
        t.MoveTo(0, 0);
        t.SetBackground(Color.Red);

        t.Write("    ");

        for (var x = 0; x < 4; x++)
        {
            var cell = Pending(t, x, 0);
            Assert.Equal(" ", cell.Text);
            Assert.Equal(Color.Red, cell.Background);
        }

        Assert.Equal((4, 0), Cursor(t));
    }

    [Fact]
    public void Write_AsciiAroundWideChar_PlacesEachCell()
    {
        var t = new DiffingTerminal(new VirtualTerminal(20, 2));

        t.Write("abc中def");

        Assert.Equal(["a", "b", "c", "中"], Enumerable.Range(0, 4).Select(x => Pending(t, x, 0).Text));
        Assert.True(Pending(t, 4, 0).IsContinuation);
        Assert.Equal(["d", "e", "f"], Enumerable.Range(5, 3).Select(x => Pending(t, x, 0).Text));
        Assert.Equal((8, 0), Cursor(t));
    }

    [Fact]
    public void Write_BaseCharWithCombiningMark_IsOneCell()
    {
        var t = new DiffingTerminal(new VirtualTerminal(20, 2));

        t.Write("é");

        Assert.Equal("é", Pending(t, 0, 0).Text);
        Assert.Equal(" ", Pending(t, 1, 0).Text);
        Assert.Equal((1, 0), Cursor(t));
    }

    [Fact]
    public void Write_ZwjFamilyBetweenAsciiChars_IsOneWideCell()
    {
        const string family = "\U0001F468‍\U0001F469‍\U0001F467";
        var t = new DiffingTerminal(new VirtualTerminal(20, 2));

        t.Write("x" + family + "y");

        Assert.Equal("x", Pending(t, 0, 0).Text);
        Assert.Equal(family, Pending(t, 1, 0).Text);
        Assert.True(Pending(t, 2, 0).IsContinuation);
        Assert.Equal("y", Pending(t, 3, 0).Text);
        Assert.Equal((4, 0), Cursor(t));
    }

    [Fact]
    public void Write_AsciiPastTheRightEdge_WrapsToTheNextRow()
    {
        var t = new DiffingTerminal(new VirtualTerminal(5, 3));

        t.Write("abcdefgh");

        Assert.Equal("abcde", string.Concat(Enumerable.Range(0, 5).Select(x => Pending(t, x, 0).Text)));
        Assert.Equal("fgh  ", string.Concat(Enumerable.Range(0, 5).Select(x => Pending(t, x, 1).Text)));
        Assert.Equal((3, 1), Cursor(t));
    }

    [Fact]
    public void Write_AsciiThatEndsAtTheBottomRightCell_ParksTheCursorBelowTheScreen()
    {
        var t = new DiffingTerminal(new VirtualTerminal(5, 2));
        t.MoveTo(0, 1);

        t.Write("abcde");

        Assert.Equal("abcde", string.Concat(Enumerable.Range(0, 5).Select(x => Pending(t, x, 1).Text)));
        Assert.Equal((0, 2), Cursor(t));

        // The screen is full. A further write must change nothing.
        t.Write("zz");

        Assert.Equal("abcde", string.Concat(Enumerable.Range(0, 5).Select(x => Pending(t, x, 1).Text)));
        Assert.Equal(" ", Pending(t, 0, 0).Text);
        Assert.Equal((0, 2), Cursor(t));
    }

    [Fact]
    public void Write_AsciiThatOverrunsTheBottomRightCell_DropsTheExtraChars()
    {
        var t = new DiffingTerminal(new VirtualTerminal(5, 2));
        t.MoveTo(3, 1);

        t.Write("abcdefg");

        Assert.Equal("   ab", string.Concat(Enumerable.Range(0, 5).Select(x => Pending(t, x, 1).Text)));
        Assert.Equal((0, 2), Cursor(t));
    }

    [Fact]
    public void Write_TabAwayFromTheRightEdge_MatchesTheGeneralLoop()
    {
        // A tab is outside the fast path range. This pins the case that the property test must leave out.
        var fast = new DiffingTerminal(new VirtualTerminal(40, 3));
        var loop = new DiffingTerminal(new VirtualTerminal(40, 3));

        fast.Write("a\tb");
        loop.WriteTextElements("a\tb");

        Assert.Equal(Cursor(loop), Cursor(fast));
        Assert.Equal(Pending(loop, 8, 0).Text, Pending(fast, 8, 0).Text);
        Assert.Equal((9, 0), Cursor(fast));
    }

    // ===== Property: Write (fast path) matches the general loop =====

    // Fragments at the boundaries of the fast path: marks, joiners, wide chars, emoji, controls, and
    // DEL. No fragment holds a tab (see TextGen).
    private static readonly string[] Fragments =
    [
        "a", "e", "Z", " ", "~", "!", "#", "*", "0", "hello", "    ",
        "́", "é", "̀́",                         // combining marks
        "‍", "\U0001F468‍\U0001F469‍\U0001F467",      // ZWJ, ZWJ family
        "❤️", "️", "#️⃣", "⃣",         // variation selector, keycap
        "中", "文", "Ａ",                                 // wide
        "\U0001F600", "\uD83D", "\uDE00",                             // emoji, lone surrogates
        "\r", "\n", "\r\n", "\u001B", "\u001B[31m",                   // CR, LF, ESC
        "\u007F", "\0", "\u0007", "\u0085", " ", "é",       // DEL, NUL, BEL, NEL, NBSP, e-acute
    ];

    // Printable ASCII only: the text that takes the fast path.
    private static readonly Gen<string> AsciiText =
        Gen.Char[' ', '~'].Array[0, 14].Select(chars => new string(chars));

    // Everything else. A tab is left out on purpose. Write("\t") never returns when the tab reaches
    // the right edge: the tab loop in WriteTextElementToBuffer wraps to the next row and spins. This is
    // older code (#61) and it hangs on the fast path and on the general loop alike. It is a separate bug
    // and it is not part of this PR. Add '\t' to this generator when that bug is fixed.
    private static readonly Gen<string> MixedText = Gen.OneOf(
        Gen.OneOfConst(Fragments).Array[1, 6].Select(parts => string.Concat(parts)),
        Gen.Char.Where(c => c != '\t').Array[0, 6].Select(chars => new string(chars)));

    private static readonly Gen<string> TextGen = Gen.Frequency((6, AsciiText), (4, MixedText));

    private abstract record Op;

    // One or more consecutive writes. Several parts model split writes, for example a base char in
    // one write and its combining mark in the next.
    private sealed record WriteOp(string[] Parts) : Op;
    private sealed record MoveOp(int X, int Y) : Op;
    private sealed record ForegroundOp(int Index) : Op;
    private sealed record BackgroundOp(int Index) : Op;
    private sealed record DecorationOp(int Index) : Op;
    private sealed record ResetOp : Op;
    private sealed record FlushOp : Op;
    private sealed record ClearOp : Op;
    private sealed record SaveOp : Op;
    private sealed record RestoreOp : Op;

    private static readonly Color[] Colors = [Color.Default, Color.Red, Color.FromRgb(23, 27, 34), Color.BrightBlue];
    private static readonly TextDecoration[] Decorations = [TextDecoration.None, TextDecoration.Bold, TextDecoration.Underline];

    private static readonly Gen<Op> OpGen = Gen.Frequency(
        (10, TextGen.Array[1, 4].Select(parts => (Op)new WriteOp(parts))),
        (2, Gen.Select(Gen.Int[-1, 14], Gen.Int[-1, 6], (x, y) => (Op)new MoveOp(x, y))), // includes out of range
        (1, Gen.Int[0, Colors.Length - 1].Select(i => (Op)new ForegroundOp(i))),
        (1, Gen.Int[0, Colors.Length - 1].Select(i => (Op)new BackgroundOp(i))),
        (1, Gen.Int[0, Decorations.Length - 1].Select(i => (Op)new DecorationOp(i))),
        (1, Gen.Const((Op)new ResetOp())),
        (1, Gen.Const((Op)new FlushOp())),
        (1, Gen.Const((Op)new ClearOp())),
        (1, Gen.Const((Op)new SaveOp())),
        (1, Gen.Const((Op)new RestoreOp())));

    // Small terminals so that wraps, right-edge crossings, and the bottom-right cell are common.
    private static readonly Gen<(int Width, int Height, Op[] Ops)> ScenarioGen =
        from w in Gen.Int[1, 12]
        from h in Gen.Int[1, 5]
        from ops in OpGen.Array[1, 25]
        select (w, h, ops);

    private static void Apply(DiffingTerminal t, Op op, bool useFastPath)
    {
        switch (op)
        {
            case WriteOp write:
                foreach (var part in write.Parts)
                {
                    if (useFastPath)
                        t.Write(part);
                    else
                        t.WriteTextElements(part);
                }

                break;
            case MoveOp m: t.MoveTo(m.X, m.Y); break;
            case ForegroundOp f: t.SetForeground(Colors[f.Index]); break;
            case BackgroundOp b: t.SetBackground(Colors[b.Index]); break;
            case DecorationOp d: t.SetDecoration(Decorations[d.Index]); break;
            case ResetOp: t.ResetColors(); break;
            case FlushOp: t.Flush(); break;
            case ClearOp: t.ClearScreen(); break;
            case SaveOp: t.SaveCursor(); break;
            case RestoreOp: t.RestoreCursor(); break;
        }
    }

    private static string? FirstDifference(
        DiffingTerminal fast, DiffingTerminal loop, RecordingTerminal fastOutput, RecordingTerminal loopOutput, bool compareOutput)
    {
        if (Cursor(fast) != Cursor(loop))
            return $"cursor fast={Cursor(fast)} loop={Cursor(loop)}";

        foreach (var field in new[] { PendingField, CurrentField })
        {
            var fastFrame = (FrameBuffer)field.GetValue(fast)!;
            var loopFrame = (FrameBuffer)field.GetValue(loop)!;
            for (var y = 0; y < fastFrame.Height; y++)
            for (var x = 0; x < fastFrame.Width; x++)
            {
                if (!fastFrame.GetSafe(x, y).Equals(loopFrame.GetSafe(x, y)))
                    return $"{field.Name} cell ({x},{y}) fast={fastFrame.GetSafe(x, y)} loop={loopFrame.GetSafe(x, y)}";
            }
        }

        if (compareOutput && fastOutput.Log.ToString() != loopOutput.Log.ToString())
            return $"inner output differs\nfast: {fastOutput.Log}\nloop: {loopOutput.Log}";

        return null;
    }

    private static string Describe((int Width, int Height, Op[] Ops) scenario) =>
        $"{scenario.Width}x{scenario.Height}: " + string.Join(" | ", scenario.Ops.Select(op => op is WriteOp w
            ? "Write(" + string.Join(", ", w.Parts.Select(Escape)) + ")"
            : op.ToString()));

    private static string Escape(string text) =>
        "\"" + string.Concat(text.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}")) + "\"";

    [Fact]
    public void Write_MatchesTheGeneralLoop_ForGeneratedOperationSequences()
    {
        ScenarioGen.Sample(scenario =>
        {
            var fastOutput = new RecordingTerminal(scenario.Width, scenario.Height);
            var loopOutput = new RecordingTerminal(scenario.Width, scenario.Height);
            var fast = new DiffingTerminal(fastOutput);
            var loop = new DiffingTerminal(loopOutput);

            for (var i = 0; i < scenario.Ops.Length; i++)
            {
                var op = scenario.Ops[i];
                Apply(fast, op, useFastPath: true);
                Apply(loop, op, useFastPath: false);

                var difference = FirstDifference(fast, loop, fastOutput, loopOutput, compareOutput: op is FlushOp);
                if (difference is not null)
                    throw new Exception($"after op {i} ({op}): {difference}\n{Describe(scenario)}");
            }

            fast.Flush();
            loop.Flush();
            var final = FirstDifference(fast, loop, fastOutput, loopOutput, compareOutput: true);
            if (final is not null)
                throw new Exception($"after the final flush: {final}\n{Describe(scenario)}");
        }, iter: Iter);
    }

    // The same rule for fixed inputs that sit at the boundaries, each as one write and as split writes
    // ('|' separates the writes), on terminals where the input wraps or crosses the right edge.
    [Theory]
    [InlineData("abc中def")]
    [InlineData("éx")]
    [InlineData("e|́")]
    [InlineData("ab|‍\U0001F469")]
    [InlineData("x\U0001F468‍\U0001F469‍\U0001F467y")]
    [InlineData("x|❤️|y")]
    [InlineData("a\rc\nd\u001B[31me")]
    [InlineData("ab\u007Fcd")]
    [InlineData("abcdefghijklmnop")]
    [InlineData("abcd中")]
    [InlineData("abcde|中|fg")]
    public void Write_MatchesTheGeneralLoop_ForBoundaryInputs(string writes)
    {
        foreach (var (width, height) in new[] { (5, 3), (6, 3), (80, 2) })
        {
            var fastOutput = new RecordingTerminal(width, height);
            var loopOutput = new RecordingTerminal(width, height);
            var fast = new DiffingTerminal(fastOutput);
            var loop = new DiffingTerminal(loopOutput);
            fast.SetBackground(Color.Red);
            loop.SetBackground(Color.Red);

            foreach (var part in writes.Split('|'))
            {
                fast.Write(part);
                loop.WriteTextElements(part);
            }

            fast.Flush();
            loop.Flush();

            Assert.Null(FirstDifference(fast, loop, fastOutput, loopOutput, compareOutput: true));
        }
    }

    // Records each call that the DiffingTerminal sends to its inner terminal, so a test can compare
    // the exact output of two diffs.
    private sealed class RecordingTerminal(int width, int height) : IAnsiTerminal
    {
        public StringBuilder Log { get; } = new();
        public int Width => width;
        public int Height => height;
        public void MoveTo(int x, int y) => Log.Append($"M{x},{y};");
        public void Write(string text) => Log.Append($"W[{text}];");
        public void Write(char c) => Log.Append($"C[{c}];");
        public void SetForeground(Color color) => Log.Append($"F{color};");
        public void SetBackground(Color color) => Log.Append($"B{color};");
        public void ResetColors() => Log.Append("R;");
        public void SaveCursor() => Log.Append("S;");
        public void RestoreCursor() => Log.Append("T;");
        public void SetCursorVisible(bool visible) => Log.Append($"V{visible};");
        public void ClearRegion(int x, int y, int w, int h) => Log.Append($"CR{x},{y},{w},{h};");
        public void ClearScreen() => Log.Append("CS;");
        public void Flush() => Log.Append("FL;");
        public void EnterAlternateScreen() { }
        public void ExitAlternateScreen() { }
        public void EnableMouse() { }
        public void DisableMouse() { }
        public void EnableWheelScroll() { }
        public void DisableWheelScroll() { }
        public void CopyToClipboard(string text) { }
    }
}
