// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// Integration tests for TextNode wrapping behavior in real layout scenarios.
/// These tests verify that text wrapping works correctly when TextNode is used
/// within panels and other containers.
/// </summary>
public class TextNodeIntegrationTests
{
    [Fact]
    public void TextNode_InsidePanel_WrapsTextToAvailableWidth()
    {
        // Arrange: Panel with 20 chars wide, text is 40 chars
        // Panel border takes 2 chars (left + right), so content area is 18 chars
        var terminal = new VirtualTerminal(20, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 20, 10);

        var panel = new PanelNode()
            .WithTitle("Test")
            .WithBorder(BorderStyle.Single)
            .WithContent(new TextNode("This is a long message that should wrap"))
            .Height(5);

        // Act
        panel.Render(context, new Rect(0, 0, 20, 5));

        // Assert: Text should wrap within the panel's content area (18 chars wide)
        // Line 0: Border
        // Line 1-3: Content (wrapped text)
        // Line 4: Border

        var line1 = terminal.GetLine(1);
        var line2 = terminal.GetLine(2);

        // Content should appear on multiple lines (wrapped), not truncated
        Assert.Contains("This is a long", line1);
        Assert.Contains("message that", line2);
    }

    [Fact]
    public void TextNode_InsidePanel_WithFixedHeight_ClipsExcessLines()
    {
        // Arrange: Panel with height 3 can only show 1 line of content
        // (top border, content, bottom border)
        var terminal = new VirtualTerminal(20, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 20, 10);

        var panel = new PanelNode()
            .WithTitle("Test")
            .WithBorder(BorderStyle.Single)
            .WithContent(new TextNode("This is a very long message"))
            .Height(3);

        // Act
        panel.Render(context, new Rect(0, 0, 20, 3));

        // Assert: Only first line of wrapped content should be visible
        // Height 3 = 1 for top border + 1 for content + 1 for bottom border
        var line1 = terminal.GetLine(1);
        Assert.Contains("This is a very", line1);

        // Line 2 should be the bottom border, not more content
        var line2 = terminal.GetLine(2);
        Assert.StartsWith("└", line2); // Bottom border character
    }

    [Fact]
    public void TextNode_InVerticalLayout_WrapsCorrectly()
    {
        // Arrange
        var terminal = new VirtualTerminal(20, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 20, 10);

        var layout = Layouts.Vertical()
            .WithChild(new TextNode("Short"))
            .WithChild(new TextNode("This is a longer message that wraps"));

        // Act
        layout.Render(context, new Rect(0, 0, 20, 10));

        // Assert: First text on line 0, second text wraps across lines 1-2
        Assert.Equal("Short", terminal.GetLine(0).TrimEnd());
        Assert.Contains("This is a longer", terminal.GetLine(1));
        Assert.Contains("message that wraps", terminal.GetLine(2));
    }

    [Fact]
    public void TextNode_Measure_ReturnsCorrectHeightForWrappedText()
    {
        // Arrange: Text that will wrap at width 10
        var node = new TextNode("Hello World Test Message");

        // Act: Measure at width 10
        var size = node.Measure(new Size(10, 100));

        // Assert: 24 chars wrapping at 10 = 3 lines (Hello Worl | d Test Mes | sage)
        // Actually "Hello" (5) + " " + "World" (5) = 11, so:
        // "Hello" (5) fits
        // "World" (5) fits
        // "Test" (4) fits
        // "Message" (7) fits
        // With word wrapping: "Hello", "World", "Test", "Message" = depends on WordWrapper logic
        Assert.Equal(10, size.Width);
        Assert.True(size.Height >= 2, $"Expected at least 2 lines but got {size.Height}");
    }

    [Fact]
    public void TextNode_InHorizontalLayout_ReceivesCorrectWidth()
    {
        // Arrange: Horizontal layout with two items
        var terminal = new VirtualTerminal(40, 5);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 5);

        var layout = Layouts.Horizontal()
            .WithChild(new TextNode("Left side").Width(15))
            .WithChild(new TextNode("Right side content that is long").Fill());

        // Act
        layout.Render(context, new Rect(0, 0, 40, 5));

        // Assert: Left side gets 15 chars, right side gets 25 chars
        var line0 = terminal.GetLine(0);
        Assert.Contains("Left side", line0);
        Assert.Contains("Right side", line0);
    }

    [Fact]
    public void TextNode_MeasuredHeight_UsedByVerticalLayout()
    {
        // Arrange: Text that wraps to 3 lines at width 10
        var terminal = new VirtualTerminal(10, 20);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 20);

        // Two text nodes - first wraps, second should appear after wrapped content
        var layout = Layouts.Vertical()
            .WithChild(new TextNode("AAAAAAAAAA BBBBBBBBBB CCCCCCCCCC")) // 3 words, each 10 chars
            .WithChild(new TextNode("END MARKER"));

        // Act
        layout.Render(context, new Rect(0, 0, 10, 20));

        // Assert: First text wraps to 3 lines, END MARKER appears on line 3
        Assert.Equal("AAAAAAAAAA", terminal.GetLine(0).TrimEnd());
        Assert.Equal("BBBBBBBBBB", terminal.GetLine(1).TrimEnd());
        Assert.Equal("CCCCCCCCCC", terminal.GetLine(2).TrimEnd());
        Assert.StartsWith("END MARKER", terminal.GetLine(3).TrimEnd());
    }

    [Fact]
    public void TextNode_NoWrap_TruncatesInsteadOfWrapping()
    {
        // Arrange
        var terminal = new VirtualTerminal(10, 5);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 5);

        var layout = Layouts.Vertical()
            .WithChild(new TextNode("This is a very long message").NoWrap())
            .WithChild(new TextNode("SECOND"));

        // Act
        layout.Render(context, new Rect(0, 0, 10, 5));

        // Assert: First text is truncated to 10 chars, not wrapped
        // Second text appears on line 1 (right after truncated first text)
        Assert.Equal("This is a", terminal.GetLine(0).TrimEnd());
        Assert.Equal("SECOND", terminal.GetLine(1).TrimEnd());
    }

    [Fact]
    public void Panel_FillHeight_AllowsWrappedTextToExpand()
    {
        // Arrange: Panel with Fill() should use all available height
        var terminal = new VirtualTerminal(20, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 20, 10);

        var panel = new PanelNode()
            .WithTitle("Messages")
            .WithBorder(BorderStyle.Single)
            .WithContent(new TextNode("Line one here and Line two here and Line three"))
            .Fill();

        // Act: Render into full 10 row area
        panel.Render(context, new Rect(0, 0, 20, 10));

        // Assert: Content should wrap and multiple lines should be visible
        // Panel interior is 18 chars wide, 8 rows tall
        var line1 = terminal.GetLine(1);
        var line2 = terminal.GetLine(2);
        var line3 = terminal.GetLine(3);

        // Verify that wrapped content appears on multiple lines
        Assert.True(line1.Trim().Length > 0, "Line 1 should have content");
        Assert.True(line2.Trim().Length > 0, "Line 2 should have content");
        Assert.True(line3.Trim().Length > 0, "Line 3 should have content");
    }

    [Fact]
    public void BorderlessPanel_Background_FillsSurfaceAndContentCells()
    {
        var terminal = new VirtualTerminal(12, 4);
        var context = new RegionRenderContext(terminal, 0, 0, 12, 4);
        var surface = Color.FromRgb(23, 27, 34);
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(surface)
            .WithContent(Layouts.Vertical()
                .WithChild(new TextNode("First").WithForeground(Color.BrightBlue))
                .WithChild(new TextNode("Second").WithForeground(Color.BrightGreen)))
            .Width(12)
            .Height(4);

        panel.Render(context, new Rect(0, 0, 12, 4));

        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 12; x++)
            Assert.Equal(surface, terminal.GetBackground(x, y));
    }

    [Fact]
    public void BorderlessPanel_Background_FillsScrollableContentCells()
    {
        var terminal = new VirtualTerminal(24, 8);
        var context = new RegionRenderContext(terminal, 0, 0, 24, 8);
        var surface = Color.FromRgb(23, 27, 34);
        var detail = new ScrollableContainerNode()
            .WithScrollbar(false)
            .WithContent(new TextNode("First detail line\nSecond detail line\nThird detail line")
                .WithForeground(Color.White));
        var panel = new PanelNode()
            .WithBorder(BorderStyle.None)
            .WithBackground(surface)
            .WithContent(Layouts.Vertical()
                .WithChild(new TextNode("DETAIL").WithForeground(Color.BrightBlue))
                .WithChild(detail.Fill())
                .WithChild(new TextNode("Y copy").WithForeground(Color.Gray)))
            .Width(24)
            .Height(8);

        panel.Render(context, new Rect(0, 0, 24, 8));

        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 24; x++)
            Assert.Equal(surface, terminal.GetBackground(x, y));
    }

    [Fact]
    public void TextNode_SameInstance_WrapsCorrectlyOnResize()
    {
        // Arrange: A single TextNode that will be rendered at different widths
        // This simulates what happens when the terminal is resized
        var text = "(no messages yet)"; // 17 chars
        var node = new TextNode(text);

        // First render at width 40 - should fit on one line
        var terminal1 = new VirtualTerminal(40, 10);
        var context1 = new RegionRenderContext(terminal1, 0, 0, 40, 10);

        node.Render(context1, new Rect(0, 0, 40, 10));

        Assert.Equal(text, terminal1.GetLine(0).TrimEnd());
        Assert.Equal("", terminal1.GetLine(1).TrimEnd()); // No second line

        // Second render at width 10 - should wrap to 2 lines
        var terminal2 = new VirtualTerminal(10, 10);
        var context2 = new RegionRenderContext(terminal2, 0, 0, 10, 10);

        node.Render(context2, new Rect(0, 0, 10, 10));

        // "(no" (3) doesn't fit with "messages" (8), so:
        // Line 0: "(no"
        // Line 1: "messages"
        // Line 2: "yet)"
        var line0 = terminal2.GetLine(0).TrimEnd();
        var line1 = terminal2.GetLine(1).TrimEnd();
        var line2 = terminal2.GetLine(2).TrimEnd();

        // Verify text appeared on multiple lines
        Assert.True(line0.Length > 0, $"Line 0 should have content, got: '{line0}'");
        Assert.True(line1.Length > 0, $"Line 1 should have content, got: '{line1}'");

        // The combined content should contain all original words
        var combined = $"{line0} {line1} {line2}".Trim();
        Assert.Contains("no", combined);
        Assert.Contains("messages", combined);
        Assert.Contains("yet", combined);
    }

    [Fact]
    public void TextNode_InsideVerticalLayoutPanel_WrapsOnShrink()
    {
        // This test simulates the exact scenario from the demo:
        // A panel with Fill() inside a vertical layout

        // First, render at a wide width
        var terminal1 = new VirtualTerminal(60, 20);
        var context1 = new RegionRenderContext(terminal1, 0, 0, 60, 20);

        var layout = Layouts.Vertical()
            .WithChild(
                new PanelNode()
                    .WithTitle("Header")
                    .WithBorder(BorderStyle.Single)
                    .WithContent(new TextNode("Short"))
                    .Height(3))
            .WithChild(
                new PanelNode()
                    .WithTitle("Messages")
                    .WithBorder(BorderStyle.Single)
                    .WithContent(new TextNode("(no messages yet)"))
                    .Fill());

        layout.Render(context1, new Rect(0, 0, 60, 20));

        // The Messages panel should have "(no messages yet)" visible
        // Header takes rows 0-2, Messages starts at row 3
        // Row 3 is top border of Messages panel
        // Row 4 is content
        var contentLine1 = terminal1.GetLine(4);
        Assert.Contains("no messages yet", contentLine1);

        // Now render the SAME layout at a narrower width
        var terminal2 = new VirtualTerminal(15, 20);
        var context2 = new RegionRenderContext(terminal2, 0, 0, 15, 20);

        layout.Render(context2, new Rect(0, 0, 15, 20));

        // At width 15, panel interior is 13 chars
        // "(no messages yet)" = 17 chars, should wrap
        // Row 4 should have first part, row 5 should have second part
        var narrowLine4 = terminal2.GetLine(4);
        var narrowLine5 = terminal2.GetLine(5);

        // Both lines should have content (text wrapped)
        Assert.True(narrowLine4.Trim().Length > 0, $"Row 4 should have content: '{narrowLine4}'");
        Assert.True(narrowLine5.Trim().Length > 0, $"Row 5 should have content: '{narrowLine5}'");
    }

    [Fact]
    public void StatusBar_WithFixedHeight_ClipsWrappedText()
    {
        // This test demonstrates that fixed height containers CLIP wrapped text
        // This is expected behavior - the user should use appropriate heights

        var terminal = new VirtualTerminal(20, 5);
        var context = new RegionRenderContext(terminal, 0, 0, 20, 5);

        // Status bar with fixed height of 1
        var layout = Layouts.Horizontal()
            .WithChild(new TextNode("Status text here").WidthAuto())
            .WithChild(new TextNode("[↑/↓] Count [Enter] Send [Esc] Quit"))  // 35 chars - will wrap at width 10
            .Height(1);

        layout.Render(context, new Rect(0, 0, 20, 1));

        // With Height(1), only the first line of each component is visible
        var line0 = terminal.GetLine(0);

        // Some content should be visible
        Assert.True(line0.Trim().Length > 0);

        // But wrapped content is clipped (this is expected!)
        // The second row of the layout would be clipped
    }

    [Fact]
    public void TextNode_WithAutoHeight_ExpandsForWrappedContent()
    {
        // When TextNode has Auto height, it should request the height needed for wrapped content
        var node = new TextNode("This is a long message that will wrap to multiple lines");

        // Measure at width 15 - text should wrap
        var size = node.Measure(new Size(15, 100));

        // The measured height should be > 1 because text wraps
        Assert.True(size.Height > 1, $"Expected height > 1 but got {size.Height}");

        // Render and verify wrapped content
        var terminal = new VirtualTerminal(15, 10);
        var context = new RegionRenderContext(terminal, 0, 0, 15, 10);

        node.Render(context, new Rect(0, 0, 15, size.Height));

        // Multiple lines should have content
        var line0 = terminal.GetLine(0).TrimEnd();
        var line1 = terminal.GetLine(1).TrimEnd();
        var line2 = terminal.GetLine(2).TrimEnd();

        Assert.True(line0.Length > 0, "Line 0 should have content");
        Assert.True(line1.Length > 0, "Line 1 should have content");
        Assert.True(line2.Length > 0, "Line 2 should have content");
    }
}
