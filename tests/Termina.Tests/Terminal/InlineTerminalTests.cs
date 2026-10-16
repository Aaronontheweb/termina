// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Terminal;
using Termina.Layout;

namespace Termina.Tests.Terminal;

public sealed class InlineTerminalTests
{
    [Fact]
    public void Flush_replaces_only_the_owned_live_region()
    {
        var inner = new VirtualTerminal(20, 10);
        inner.Write("stable");
        inner.WriteLineBreak();
        var terminal = new InlineTerminal(inner, inner);

        terminal.ClearScreen();
        terminal.MoveTo(0, 0);
        terminal.Write("first");
        terminal.Flush();

        terminal.ClearScreen();
        terminal.MoveTo(0, 0);
        terminal.Write("second");
        terminal.Flush();

        Assert.Equal("stable", inner.GetLine(0));
        Assert.Equal("second", inner.GetLine(1));
        Assert.Equal(string.Empty, inner.GetLine(2));
        Assert.False(inner.InAlternateScreen);
    }

    [Fact]
    public void Flush_preserves_parallel_rows_and_wide_text()
    {
        var inner = new VirtualTerminal(20, 10);
        var terminal = new InlineTerminal(inner, inner);

        terminal.ClearScreen();
        terminal.MoveTo(0, 0);
        terminal.Write("A: active");
        terminal.MoveTo(0, 1);
        terminal.Write("B: 😀 done");
        terminal.Flush();

        Assert.Equal("A: active", inner.GetLine(0));
        Assert.Equal("B: 😀 done", inner.GetLine(1));
    }

    [Fact]
    public void ClearLiveRegion_removes_live_rows_and_keeps_stable_rows()
    {
        var inner = new VirtualTerminal(20, 10);
        inner.Write("stable");
        inner.WriteLineBreak();
        var terminal = new InlineTerminal(inner, inner);

        terminal.ClearScreen();
        terminal.Write("active");
        terminal.Flush();
        terminal.ClearLiveRegion();

        Assert.Equal("stable", inner.GetLine(0));
        Assert.Equal(string.Empty, inner.GetLine(1));
        Assert.Equal(0, inner.CursorX);
        Assert.Equal(1, inner.CursorY);
    }

    [Fact]
    public void Width_reserves_the_last_column_to_prevent_implicit_wrap()
    {
        var inner = new VirtualTerminal(20, 10);
        var terminal = new InlineTerminal(inner, inner);

        Assert.Equal(19, terminal.Width);
        Assert.Equal(10, terminal.Height);
    }

    [Fact]
    public void Commit_places_stable_content_above_the_live_region()
    {
        var inner = new VirtualTerminal(20, 10);
        var terminal = new InlineTerminal(inner, inner);
        terminal.ClearScreen();
        terminal.Write("active one");
        terminal.Flush();

        terminal.Commit(new TextNode("settled"));
        terminal.ClearScreen();
        terminal.Write("active two");
        terminal.Flush();

        Assert.Equal("settled", inner.GetLine(0));
        Assert.Equal("active two", inner.GetLine(1));
        Assert.Equal(string.Empty, inner.GetLine(2));
    }

    [Fact]
    public void Sequential_commits_keep_their_order_above_parallel_live_rows()
    {
        var inner = new VirtualTerminal(24, 12);
        var terminal = new InlineTerminal(inner, inner);
        terminal.ClearScreen();
        terminal.MoveTo(0, 0);
        terminal.Write("tool A: active");
        terminal.MoveTo(0, 1);
        terminal.Write("tool B: active");
        terminal.Flush();

        terminal.Commit(new TextNode("tool B: done"));
        terminal.ClearScreen();
        terminal.Write("tool A: active");
        terminal.Flush();
        terminal.Commit(new TextNode("tool A: done"));
        terminal.ClearScreen();
        terminal.Write("composer");
        terminal.Flush();

        Assert.Equal("tool B: done", inner.GetLine(0));
        Assert.Equal("tool A: done", inner.GetLine(1));
        Assert.Equal("composer", inner.GetLine(2));
    }

    [Theory]
    [InlineData(12, 8)]
    [InlineData(30, 14)]
    public void Resize_keeps_stable_content_and_replaces_the_live_region(int width, int height)
    {
        var inner = new VirtualTerminal(20, 10);
        inner.Write("stable");
        inner.WriteLineBreak();
        var terminal = new InlineTerminal(inner, inner);
        terminal.ClearScreen();
        terminal.Write("old live");
        terminal.Flush();

        inner.Resize(width, height);
        terminal.ClearScreen();
        terminal.Write("new live");
        terminal.Flush();

        Assert.Equal("stable", inner.GetLine(0));
        Assert.Equal("new live", inner.GetLine(1));
        Assert.Equal(width - 1, terminal.Width);
    }
}
