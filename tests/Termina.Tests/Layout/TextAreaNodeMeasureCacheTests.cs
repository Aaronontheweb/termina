// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Input;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// <see cref="TextAreaNode.Measure"/> runs on every frame. It must not walk a long line again while
/// the text stays the same, and it must give the same size as a new node after each edit.
/// </summary>
public class TextAreaNodeMeasureCacheTests
{
    private const int LongLine = 600_000;

    [Fact]
    public void Measure_of_an_unchanged_long_line_does_not_walk_the_line_again()
    {
        var node = new TextAreaNode();
        node.Text = new string('a', LongLine);
        node.MoveCursorToEnd();
        var available = new Size(80, 24);
        node.Measure(available);
        node.Measure(available);

        var before = GC.GetAllocatedBytesForCurrentThread();
        node.Measure(available);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // One walk of the line allocates more than 10 MB (one string for each text element).
        Assert.True(allocated < 64 * 1024, $"Measure allocated {allocated} bytes for an unchanged line.");
    }

    [Fact]
    public void Measure_of_an_equal_text_in_a_new_string_does_not_walk_the_line_again()
    {
        var node = new TextAreaNode();
        node.Text = new string('a', LongLine);
        var available = new Size(80, 24);
        node.Measure(available);

        // An app that puts refused text back assigns an equal string. The wrap runs again, but
        // the measure afterwards must not count the columns again.
        node.Text = new string('a', LongLine);
        node.Measure(available);

        var before = GC.GetAllocatedBytesForCurrentThread();
        node.Measure(available);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 64 * 1024, $"Measure allocated {allocated} bytes for an equal text.");
    }

    public static readonly TheoryData<string> Edits = new()
    {
        "type",
        "type-wide",
        "backspace",
        "set-text",
        "paste-line",
        "paste-lines",
        "paste-then-type",
        "clear",
    };

    [Theory]
    [MemberData(nameof(Edits))]
    public void Measure_and_render_after_an_edit_match_a_new_node(string edit)
    {
        var available = new Size(40, 10);
        var cached = CreateNode();
        cached.Measure(available);
        Render(cached, available);
        Apply(cached, edit);

        var fresh = CreateNode();
        Apply(fresh, edit);

        Assert.Equal(fresh.Measure(available), cached.Measure(available));
        Assert.Equal(Render(fresh, available), Render(cached, available));
    }

    private static TextAreaNode CreateNode()
    {
        var node = new TextAreaNode();
        // Auto width uses the content width, so a stale column count changes the measured size.
        node.WidthAuto(1, 40);
        node.Text = "hello";
        node.MoveCursorToEnd();
        return node;
    }

    private static void Apply(TextAreaNode node, string edit)
    {
        switch (edit)
        {
            case "type":
                Type(node, "abc");
                break;
            case "type-wide":
                Type(node, "你好");
                break;
            case "backspace":
                node.HandleInput(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
                break;
            case "set-text":
                node.Text = "a different text";
                break;
            case "paste-line":
                node.HandlePaste(new PasteEvent(" pasted"));
                break;
            case "paste-lines":
                node.HandlePaste(new PasteEvent("one\ntwo\nthree"));
                break;
            case "paste-then-type":
                node.HandlePaste(new PasteEvent("one\ntwo"));
                Type(node, "xy");
                break;
            case "clear":
                node.Clear();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit), edit, null);
        }
    }

    private static void Type(TextAreaNode node, string text)
    {
        foreach (var c in text)
            node.HandleInput(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
    }

    private static string Render(TextAreaNode node, Size available)
    {
        var size = node.Measure(available);
        var terminal = new VirtualTerminal(available.Width, available.Height);
        node.Render(
            new RegionRenderContext(terminal, 0, 0, available.Width, available.Height),
            new Rect(0, 0, size.Width, size.Height));
        return terminal.ToString();
    }
}
