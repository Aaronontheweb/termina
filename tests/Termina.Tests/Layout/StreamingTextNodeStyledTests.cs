// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Components.Streaming;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

/// <summary>
/// Tests for styled text functionality in StreamingTextNode.
/// </summary>
public class StreamingTextNodeStyledTests
{
    [Fact]
    public void Append_WithForeground_SetsColor()
    {
        var node = StreamingTextNode.Create();

        node.Append("Hello", foreground: Color.Red);

        var lines = node.Buffer.GetAllStyledLines();
        Assert.Single(lines);
        Assert.Equal(Color.Red, lines[0].Segments[0].Style.Foreground);
    }

    [Fact]
    public void Append_WithBackground_SetsColor()
    {
        var node = StreamingTextNode.Create();

        node.Append("Hello", background: Color.Blue);

        var lines = node.Buffer.GetAllStyledLines();
        Assert.Equal(Color.Blue, lines[0].Segments[0].Style.Background);
    }

    [Fact]
    public void Append_WithDecoration_SetsDecoration()
    {
        var node = StreamingTextNode.Create();

        node.Append("Hello", decoration: TextDecoration.Bold);

        var lines = node.Buffer.GetAllStyledLines();
        Assert.Equal(TextDecoration.Bold, lines[0].Segments[0].Style.Decoration);
    }

    [Fact]
    public void Append_WithMultipleDecorations_CombinesFlags()
    {
        var node = StreamingTextNode.Create();

        node.Append("Hello", decoration: TextDecoration.Bold | TextDecoration.Underline);

        var lines = node.Buffer.GetAllStyledLines();
        var dec = lines[0].Segments[0].Style.Decoration;
        Assert.True(dec.HasFlag(TextDecoration.Bold));
        Assert.True(dec.HasFlag(TextDecoration.Underline));
    }

    [Fact]
    public void Append_StyledSegment_AddsSegment()
    {
        var node = StreamingTextNode.Create();
        var style = new TextStyle(Color.Green, Color.Default, TextDecoration.Italic);
        var segment = new StyledSegment("Hello", style);

        node.Append(segment);

        var lines = node.Buffer.GetAllStyledLines();
        Assert.Equal(Color.Green, lines[0].Segments[0].Style.Foreground);
        Assert.Equal(TextDecoration.Italic, lines[0].Segments[0].Style.Decoration);
    }

    [Fact]
    public void AppendLine_WithForeground_SetsColorAndNewline()
    {
        var node = StreamingTextNode.Create();

        node.AppendLine("Hello", foreground: Color.Yellow);
        node.AppendLine("World", foreground: Color.Cyan);

        var lines = node.Buffer.GetAllStyledLines();
        Assert.Equal(2, lines.Count);
        Assert.Equal(Color.Yellow, lines[0].Segments[0].Style.Foreground);
        Assert.Equal(Color.Cyan, lines[1].Segments[0].Style.Foreground);
    }

    [Fact]
    public void Render_AppliesStylesToOutput()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 24);
        var node = StreamingTextNode.Create();

        node.AppendLine("Hello", foreground: Color.Red);

        node.Render(context, new Rect(0, 0, 80, 10));

        // Verify text was rendered
        Assert.True(terminal.Contains("Hello"));
        // Verify color was applied
        Assert.Equal(Color.Red, terminal.GetForeground(0, 0));
    }

    [Fact]
    public void Render_MultiplStyledSegments_AppliesEachStyle()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 24);
        var node = StreamingTextNode.Create();

        node.Append("Red", foreground: Color.Red);
        node.Append(" Blue", foreground: Color.Blue);
        node.AppendLine("");

        node.Render(context, new Rect(0, 0, 80, 10));

        Assert.Equal(Color.Red, terminal.GetForeground(0, 0));
        Assert.Equal(Color.Blue, terminal.GetForeground(4, 0)); // " Blue" starts at position 3
    }

    [Fact]
    public void Render_WrapsCjkChatTextByDisplayColumns()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 24);
        var node = StreamingTextNode.Create();

        node.AppendLine("你是谁");

        node.Render(context, new Rect(0, 0, 4, 3));

        Assert.Equal("你是", terminal.GetLine(0));
        Assert.Equal("谁", terminal.GetLine(1));
    }

    [Fact]
    public void Render_AdvancesStyledSegmentsByDisplayColumns()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 24);
        var node = StreamingTextNode.Create();

        node.Append("你", foreground: Color.Red);
        node.AppendLine("A", foreground: Color.Blue);

        node.Render(context, new Rect(0, 0, 10, 1));

        Assert.Equal('你', terminal.GetChar(0, 0));
        Assert.Equal('A', terminal.GetChar(2, 0));
        Assert.Equal(Color.Blue, terminal.GetForeground(2, 0));
    }

    [Fact]
    public void Render_WithNodeForeground_UsesAsFallback()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 24);
        var node = StreamingTextNode.Create()
            .WithForeground(Color.Green);

        node.AppendLine("Hello"); // No specific color

        node.Render(context, new Rect(0, 0, 80, 10));

        Assert.Equal(Color.Green, terminal.GetForeground(0, 0));
    }

    [Fact]
    public void Render_SegmentStyleOverridesNodeStyle()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 24);
        var node = StreamingTextNode.Create()
            .WithForeground(Color.Green); // Node default

        node.AppendLine("Hello", foreground: Color.Red); // Override with red

        node.Render(context, new Rect(0, 0, 80, 10));

        Assert.Equal(Color.Red, terminal.GetForeground(0, 0));
    }

    [Fact]
    public void Invalidated_FiresOnStyledAppend()
    {
        var node = StreamingTextNode.Create();
        var fired = false;
        node.Invalidated.Subscribe(_ => fired = true);

        node.Append("Hello", foreground: Color.Red);

        Assert.True(fired);
    }

    [Fact]
    public void ContentChanged_FiresOnStyledAppend()
    {
        var node = StreamingTextNode.Create();
        var fired = false;
        node.ContentChanged.Subscribe(_ => fired = true);

        node.Append("Hello", foreground: Color.Red);

        Assert.True(fired);
    }

    [Fact]
    public void Clear_RemovesAllStyledContent()
    {
        var node = StreamingTextNode.Create();
        node.Append("Hello", foreground: Color.Red);

        node.Clear();

        var lines = node.Buffer.GetAllStyledLines();
        Assert.Empty(lines);
    }

    [Fact]
    public void WithPrefix_RendersPrefix()
    {
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 80, 24);
        var node = StreamingTextNode.Create()
            .WithPrefix("> ", Color.Gray);

        node.AppendLine("Hello");

        node.Render(context, new Rect(0, 0, 80, 10));

        // Should see prefix followed by content
        Assert.True(terminal.Contains(">"));
        Assert.True(terminal.Contains("Hello"));
    }

    [Fact]
    public void Create_ReturnsPersistedBuffer()
    {
        var node = StreamingTextNode.Create();

        Assert.IsType<PersistedStreamBuffer>(node.Buffer);
    }

    [Fact]
    public void CreateWindowed_ReturnsWindowedBuffer()
    {
        var node = StreamingTextNode.CreateWindowed(50);

        Assert.IsType<WindowedStreamBuffer>(node.Buffer);
    }

    [Fact]
    public void FluentMethods_ReturnSameInstance()
    {
        var node = StreamingTextNode.Create();

        var result = node
            .WithForeground(Color.Red)
            .WithBackground(Color.Blue)
            .WithPrefix(">> ", Color.Gray);

        Assert.Same(node, result);
    }
}
