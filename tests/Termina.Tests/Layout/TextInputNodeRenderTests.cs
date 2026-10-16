// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Input;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// Rendering tests for <see cref="TextInputNode"/> that inspect the produced cell colors
/// via <see cref="VirtualTerminal"/>.
/// </summary>
public class TextInputNodeRenderTests
{
    private const int Width = 20;

    private static (VirtualTerminal Terminal, RegionRenderContext Context) NewSurface()
    {
        var terminal = new VirtualTerminal(Width, 1);
        var context = new RegionRenderContext(terminal, 0, 0, Width, 1);
        return (terminal, context);
    }

    [Fact]
    public void Render_PlaceholderWithBackground_AppliesConfiguredBackground()
    {
        // Regression: the placeholder used to render against the terminal default background
        // even when a Background was configured, leaving a visual seam in styled input panels.
        var bg = Color.FromRgb(0x33, 0x38, 0x42);
        using var node = new TextInputNode()
            .WithBackground(bg)
            .WithPlaceholder("type here");

        var (terminal, context) = NewSurface();
        node.Render(context, new Rect(0, 0, Width, 1));

        // Placeholder glyph cell — x >= 1 so we never collide with the cursor cell at (0,0).
        Assert.Equal(bg, terminal.GetBackground(3, 0));

        // Trailing empty cell beyond the placeholder text also carries the configured bg
        // (the up-front Fill covers the whole input region, not just the glyph cells).
        Assert.Equal(bg, terminal.GetBackground(15, 0));
    }

    [Fact]
    public void Render_PlaceholderWithoutBackground_LeavesCellsDefault()
    {
        // No-regression: without a configured Background, cells stay the terminal default.
        using var node = new TextInputNode().WithPlaceholder("type here");

        var (terminal, context) = NewSurface();
        node.Render(context, new Rect(0, 0, Width, 1));

        Assert.Equal(Color.Default, terminal.GetBackground(3, 0));
        Assert.Equal(Color.Default, terminal.GetBackground(15, 0));
    }

    [Fact]
    public void Render_CjkText_PositionsCursorByDisplayColumns()
    {
        using var node = new TextInputNode();
        node.HandlePaste(new PasteEvent("你好A"));

        var (terminal, context) = NewSurface();
        node.Render(context, new Rect(0, 0, 6, 1));

        Assert.Equal('你', terminal.GetChar(0, 0));
        Assert.Equal('好', terminal.GetChar(2, 0));
        Assert.Equal('A', terminal.GetChar(4, 0));
        Assert.Equal(node.CursorColor, terminal.GetBackground(5, 0));
    }

    [Fact]
    public void Render_CjkCursorAtViewportEdge_RemainsVisible()
    {
        using var node = new TextInputNode();
        node.HandlePaste(new PasteEvent("你好"));
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));

        var terminal = new VirtualTerminal(3, 1);
        var context = new RegionRenderContext(terminal, 0, 0, 3, 1);

        node.Render(context, new Rect(0, 0, 3, 1));

        Assert.Equal(node.CursorColor, terminal.GetBackground(2, 0));
    }
}
