// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Input;
using Termina.Layout;
using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// <see cref="ColumnCountCache"/> keeps the column count of the last string. It must give the count
/// of the current string after every change, and it must spare the cell walk of an unchanged string.
/// The ASCII fast path of <see cref="DisplayWidth.GetColumnCount(string)"/> already spares the walk
/// for ASCII text, so these tests use non-ASCII text.
/// </summary>
public class ColumnCountCacheTests
{
    // Wide CJK, an emoji with a skin tone modifier, a combining mark, and a precomposed Latin-1 char.
    private const string Chunk = "你好\U0001F44D\U0001F3FDééab";

    public static readonly TheoryData<string, string> Changes = new()
    {
        // The new text has the same length and a different count.
        { "ab", "你好" },
        { "你好", "ab" },
        // The new text is longer and wider.
        { "é", "你好\U0001F44D\U0001F3FDé" },
        // The new text is shorter and narrower.
        { "你好你好", "é" },
        // A combining mark changes the length and not the count.
        { "e", "é" },
        // The text becomes empty.
        { "你", "" },
        // The text keeps its length and one char changes the width of the whole text.
        { "éx", "ex́" },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Get_returns_the_count_of_the_current_string_after_a_change(string first, string second)
    {
        var cache = new ColumnCountCache();

        Assert.Equal(DisplayWidth.GetColumnCount(first), cache.Get(first));
        Assert.Equal(DisplayWidth.GetColumnCount(second), cache.Get(second));
        Assert.Equal(DisplayWidth.GetColumnCount(first), cache.Get(first));
    }

    [Fact]
    public void Get_returns_the_same_count_for_an_equal_string_in_a_new_reference()
    {
        var cache = new ColumnCountCache();
        var first = string.Concat(Enumerable.Repeat(Chunk, 100));
        var second = string.Concat(Enumerable.Repeat(Chunk, 100));
        Assert.NotSame(first, second);

        Assert.Equal(DisplayWidth.GetColumnCount(first), cache.Get(first));
        Assert.Equal(DisplayWidth.GetColumnCount(first), cache.Get(second));
    }

    [Fact]
    public void Get_of_an_unchanged_non_ASCII_string_does_not_walk_the_string_again()
    {
        var cache = new ColumnCountCache();
        var text = string.Concat(Enumerable.Repeat(Chunk, 20_000));
        cache.Get(text);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var columns = cache.Get(text);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(DisplayWidth.GetColumnCount(text), columns);
        // The cell walk allocates one string for each text element, which is more than 1 MB here.
        Assert.True(allocated < 1024, $"Get allocated {allocated} bytes for an unchanged string.");
    }

    public static readonly TheoryData<string[]> TextSequences = new()
    {
        new[] { "hello", "你好", "ab", "\U0001F44D\U0001F3FD", "é", "" },
        new[] { "你好", "ab", "你好", "abcd" },
    };

    [Theory]
    [MemberData(nameof(TextSequences))]
    public void Measure_of_an_auto_width_text_area_follows_each_text_change(string[] texts)
    {
        var available = new Size(200, 10);
        var node = new TextAreaNode();
        node.WidthAuto(1, 200);

        foreach (var text in texts)
        {
            node.Text = text;
            var fresh = new TextAreaNode();
            fresh.WidthAuto(1, 200);
            fresh.Text = text;

            // Auto width is the column count of the text plus one for the cursor, so a stale
            // count shows as a wrong width.
            Assert.Equal(DisplayWidth.GetColumnCount(text) + 1, node.Measure(available).Width);
            Assert.Equal(fresh.Measure(available), node.Measure(available));
        }
    }

    [Fact]
    public void Measure_of_an_auto_width_text_area_follows_typed_wide_characters()
    {
        var available = new Size(200, 10);
        var node = new TextAreaNode();
        node.WidthAuto(1, 200);
        Assert.Equal(1, node.Measure(available).Width);

        foreach (var c in "a你好b")
        {
            node.HandleInput(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
            Assert.Equal(DisplayWidth.GetColumnCount(node.Text) + 1, node.Measure(available).Width);
        }
    }

    [Fact]
    public void Measure_of_an_unchanged_long_non_ASCII_line_does_not_walk_the_line_again()
    {
        var node = new TextAreaNode();
        node.Text = string.Concat(Enumerable.Repeat(Chunk, 20_000));
        node.MoveCursorToEnd();
        var available = new Size(80, 24);
        node.Measure(available);
        node.Measure(available);

        var before = GC.GetAllocatedBytesForCurrentThread();
        node.Measure(available);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // The ASCII fast path does not apply to this text. One cell walk of it allocates more than 1 MB.
        Assert.True(allocated < 64 * 1024, $"Measure allocated {allocated} bytes for an unchanged line.");
    }

    [Fact]
    public void Measure_of_an_equal_non_ASCII_text_in_a_new_string_does_not_walk_the_line_again()
    {
        var node = new TextAreaNode();
        node.Text = string.Concat(Enumerable.Repeat(Chunk, 20_000));
        var available = new Size(80, 24);
        node.Measure(available);

        node.Text = string.Concat(Enumerable.Repeat(Chunk, 20_000));
        node.Measure(available);

        var before = GC.GetAllocatedBytesForCurrentThread();
        node.Measure(available);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 64 * 1024, $"Measure allocated {allocated} bytes for an equal text.");
    }
}
