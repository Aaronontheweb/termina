// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Clipboard;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// Tests for the <see cref="PanelNode.Background"/> surface: the fill, the
/// color-reset contract, child backgrounds, and screen-bounds reporting.
/// </summary>
public class PanelNodeBackgroundTests
{
    private static readonly Color Surface = Color.FromRgb(23, 27, 34);
    private static readonly Color InnerSurface = Color.FromRgb(90, 10, 10);

    [Fact]
    public void BorderlessPanel_Background_FillsSurfaceAndContentCells()
    {
        var terminal = new VirtualTerminal(12, 4);
        var context = new RegionRenderContext(terminal, 0, 0, 12, 4);
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(Surface)
            .WithContent(Layouts.Vertical()
                .WithChild(new TextNode("First").WithForeground(Color.BrightBlue))
                .WithChild(new TextNode("Second").WithForeground(Color.BrightGreen)))
            .Width(12)
            .Height(4);

        panel.Render(context, new Rect(0, 0, 12, 4));

        AssertBackground(terminal, 0, 0, 12, 4, Surface);
    }

    [Fact]
    public void BorderlessPanel_Background_FillsScrollableContentCells()
    {
        var terminal = new VirtualTerminal(24, 8);
        var context = new RegionRenderContext(terminal, 0, 0, 24, 8);
        var detail = new ScrollableContainerNode()
            .WithScrollbar(false)
            .WithContent(new TextNode("First detail line\nSecond detail line\nThird detail line")
                .WithForeground(Color.White));
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(Surface)
            .WithContent(Layouts.Vertical()
                .WithChild(new TextNode("DETAIL").WithForeground(Color.BrightBlue))
                .WithChild(detail.Fill())
                .WithChild(new TextNode("Y copy").WithForeground(Color.Gray)))
            .Width(24)
            .Height(8);

        panel.Render(context, new Rect(0, 0, 24, 8));

        AssertBackground(terminal, 0, 0, 24, 8, Surface);
    }

    [Fact]
    public void ScrollableContainer_InsideBackgroundPanel_ReportsScreenBounds()
    {
        // The panel sits at (5,3) in the terminal. The border takes one cell,
        // so the scrollable content starts at (6,4) and is 22x6.
        var terminal = new VirtualTerminal(40, 14);
        var context = new RegionRenderContext(terminal, 5, 3, 24, 8);
        var scroll = new ScrollableContainerNode()
            .WithScrollbar(false)
            .WithContent(new TextNode("a\nb\nc\nd\ne\nf\ng\nh\ni\nj"));
        var panel = new PanelNode()
            .WithBackground(Surface)
            .WithContent(scroll)
            .Width(24)
            .Height(8);

        panel.Render(context, new Rect(0, 0, 24, 8));

        var bounds = ((IPointerScrollable)scroll).LastRenderedBounds;
        Assert.Equal(new ScreenBounds(6, 4, 22, 6), bounds);
        Assert.True(bounds.Contains(10, 6), "The wheel at a point over the scrollable must route to it.");
        Assert.False(bounds.Contains(5, 3), "The wheel over the panel border must not route to it.");
    }

    [Fact]
    public void StreamingText_InsideBackgroundPanel_ReportsScreenBounds()
    {
        var terminal = new VirtualTerminal(40, 14);
        var context = new RegionRenderContext(terminal, 5, 3, 24, 8);
        var stream = StreamingTextNode.CreateWindowed(20);
        for (var i = 0; i < 12; i++)
            stream.AppendLine($"line {i}");
        var panel = new PanelNode()
            .WithBackground(Surface)
            .WithContent(stream)
            .Width(24)
            .Height(8);

        panel.Render(context, new Rect(0, 0, 24, 8));

        var bounds = ((IPointerScrollable)stream).LastRenderedBounds;
        Assert.Equal(new ScreenBounds(6, 4, 22, 6), bounds);
        Assert.True(bounds.Contains(10, 6), "The wheel at a point over the stream must route to it.");
    }

    [Fact]
    public void BorderedPanel_TitleColorWithoutBorderColor_KeepsBackgroundOnBorder()
    {
        var terminal = new VirtualTerminal(20, 4);
        var context = new RegionRenderContext(terminal, 0, 0, 20, 4);
        var panel = new PanelNode()
            .WithTitle("T")
            .WithTitleColor(Color.BrightYellow)
            .WithBackground(Surface)
            .WithContent(new TextNode("x"));

        panel.Render(context, new Rect(0, 0, 20, 4));

        AssertBackground(terminal, 0, 0, 20, 4, Surface);
    }

    [Fact]
    public void BorderedPanel_TitleColorWithoutBorderColor_DoesNotLeakBackgroundToSibling()
    {
        var terminal = new VirtualTerminal(20, 5);
        var context = new RegionRenderContext(terminal, 0, 0, 20, 5);
        var root = Layouts.Vertical()
            .WithChild(new PanelNode()
                .WithTitle("T")
                .WithTitleColor(Color.BrightYellow)
                .WithBackground(Surface)
                .WithContent(new TextNode("x"))
                .Height(4))
            .WithChild(new TextNode("after"));

        root.Render(context, new Rect(0, 0, 20, 5));

        AssertBackground(terminal, 0, 0, 20, 4, Surface);
        AssertBackground(terminal, 0, 4, 5, 1, Color.Default);
    }

    [Fact]
    public void Grid_FocusedCell_InsideBackgroundPanel_KeepsFocusBackgroundUnderText()
    {
        var terminal = new VirtualTerminal(10, 1);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);
        var grid = new GridNode(1, 1)
            .WithNavigationMode(GridNavigationMode.CellNavigation)
            .WithFocusHighlight(Color.Blue, Color.White);
        grid.SetCell(0, 0, new TextNode("abc"));
        grid.OnFocused();
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(Surface)
            .WithContent(grid)
            .Width(10)
            .Height(1);

        panel.Render(context, new Rect(0, 0, 10, 1));

        AssertBackground(terminal, 0, 0, 3, 1, Color.Blue);
    }

    [Fact]
    public void Child_SelectsBackgroundBeforeCreatingSubContext_KeepsItsBackground()
    {
        var terminal = new VirtualTerminal(10, 1);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(Surface)
            .WithContent(new SelectThenSubContextNode(Color.Red))
            .Width(10)
            .Height(1);

        panel.Render(context, new Rect(0, 0, 10, 1));

        AssertBackground(terminal, 0, 0, 4, 1, Color.Red);
        AssertBackground(terminal, 4, 0, 6, 1, Surface);
    }

    [Fact]
    public void TextNode_WithOwnBackground_KeepsItAndSurfaceResumesAfter()
    {
        var terminal = new VirtualTerminal(10, 3);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 3);
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(Surface)
            .WithContent(Layouts.Vertical()
                .WithChild(new TextNode("Hot").WithBackground(Color.Red))
                .WithChild(new TextNode("Cool").WithForeground(Color.White)))
            .Width(10)
            .Height(3);

        panel.Render(context, new Rect(0, 0, 10, 3));

        AssertBackground(terminal, 0, 0, 3, 1, Color.Red);
        AssertBackground(terminal, 0, 1, 10, 2, Surface);
    }

    [Fact]
    public void CopyableText_InsideBackgroundPanel_TextCellsKeepSurface()
    {
        var terminal = new VirtualTerminal(24, 3);
        var context = new RegionRenderContext(terminal, 0, 0, 24, 3);
        var node = new CopyableTextNode(new NoClipboard(), "hello");
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(Surface)
            .WithContent(node)
            .Width(24)
            .Height(3);

        panel.Render(context, new Rect(0, 0, 24, 3));

        AssertBackground(terminal, 0, 0, 24, 3, Surface);
    }

    [Fact]
    public void Child_ResetsColorsThenWritesAgain_KeepsSurface()
    {
        var terminal = new VirtualTerminal(10, 1);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(Surface)
            .WithContent(new ResetThenWriteNode())
            .Width(10)
            .Height(1);

        panel.Render(context, new Rect(0, 0, 10, 1));

        // The cells at x=3 and x=4 are written after ResetColors.
        AssertBackground(terminal, 0, 0, 10, 1, Surface);
    }

    [Fact]
    public void Sibling_AfterBackgroundPanel_HasDefaultBackground()
    {
        var terminal = new VirtualTerminal(10, 2);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 2);
        var root = Layouts.Vertical()
            .WithChild(new PanelNode()
                .WithBorder(BorderStyle.None)
                .WithBackground(Surface)
                .WithContent(new TextNode("a"))
                .Height(1))
            .WithChild(new TextNode("b"));

        root.Render(context, new Rect(0, 0, 10, 2));

        AssertBackground(terminal, 0, 0, 10, 1, Surface);
        AssertBackground(terminal, 0, 1, 1, 1, Color.Default);
    }

    [Fact]
    public void NestedBackgroundPanels_InnerSurfaceThenOuterSurfaceResumes()
    {
        var terminal = new VirtualTerminal(10, 4);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 4);
        var innerPanel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(InnerSurface)
            .WithContent(new TextNode("in"))
            .Height(1);
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(Surface)
            .WithContent(Layouts.Vertical()
                .WithChild(innerPanel)
                .WithChild(new TextNode("out")))
            .Width(10)
            .Height(4);

        panel.Render(context, new Rect(0, 0, 10, 4));

        AssertBackground(terminal, 0, 0, 10, 1, InnerSurface);
        AssertBackground(terminal, 0, 1, 10, 3, Surface);
    }

    private static void AssertBackground(VirtualTerminal terminal, int x, int y, int width, int height, Color expected)
    {
        var mismatches = new List<string>();
        for (var row = y; row < y + height; row++)
        for (var column = x; column < x + width; column++)
        {
            var actual = terminal.GetBackground(column, row);
            if (actual != expected)
                mismatches.Add($"({column},{row})={actual}");
        }

        Assert.True(
            mismatches.Count == 0,
            $"Expected background {expected} in all cells. Mismatches: {string.Join(' ', mismatches.Take(8))}");
    }

    private sealed class NoClipboard : IClipboardService
    {
        public bool Copy(string text) => true;
    }

    private sealed class ResetThenWriteNode : LayoutNode
    {
        public override Size Measure(Size available) => new(available.Width, 1);

        public override void Render(IRenderContext context, Rect bounds)
        {
            context.SetForeground(Color.Red);
            context.WriteAt(0, 0, "ab");
            context.ResetColors();
            context.WriteAt(3, 0, "cd");
        }
    }

    private sealed class SelectThenSubContextNode(Color background) : LayoutNode
    {
        public override Size Measure(Size available) => new(available.Width, 1);

        public override void Render(IRenderContext context, Rect bounds)
        {
            // Same order as GridNode: select the background, then create the child context.
            context.SetBackground(background);
            var child = context.CreateSubContext(new Rect(0, 0, 4, 1));
            child.WriteAt(0, 0, "abcd");
        }
    }
}
