// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;
using Termina.Terminal;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Tests for styled text functionality in PersistedStreamBuffer.
/// </summary>
public class PersistedStreamBufferStyledTests
{
    [Fact]
    public void Append_StyledSegment_AddsToBuffer()
    {
        var buffer = new PersistedStreamBuffer();
        var segment = new StyledSegment("Hello", new TextStyle(Color.Red));

        buffer.Append(segment);

        var lines = buffer.GetAllStyledLines();
        Assert.Single(lines);
        Assert.Equal("Hello", lines[0].ToPlainText());
    }

    [Fact]
    public void Append_StyledSegment_PreservesStyle()
    {
        var buffer = new PersistedStreamBuffer();
        var style = new TextStyle(Color.Green, Color.Blue, TextDecoration.Bold);
        buffer.Append(new StyledSegment("Hello", style));

        var lines = buffer.GetAllStyledLines();
        var segment = lines[0].Segments[0];

        Assert.Equal(Color.Green, segment.Style.Foreground);
        Assert.Equal(Color.Blue, segment.Style.Background);
        Assert.Equal(TextDecoration.Bold, segment.Style.Decoration);
    }

    [Fact]
    public void Append_TextWithStyle_AddsStyledSegment()
    {
        var buffer = new PersistedStreamBuffer();
        var style = new TextStyle(Color.Yellow);

        buffer.Append("Hello", style);

        var lines = buffer.GetAllStyledLines();
        Assert.Equal(Color.Yellow, lines[0].Segments[0].Style.Foreground);
    }

    [Fact]
    public void Append_TextWithForeground_AddsColoredSegment()
    {
        var buffer = new PersistedStreamBuffer();

        buffer.Append("Hello", Color.Cyan);

        var lines = buffer.GetAllStyledLines();
        Assert.Equal(Color.Cyan, lines[0].Segments[0].Style.Foreground);
    }

    [Fact]
    public void AppendLine_WithStyle_CreatesNewLine()
    {
        var buffer = new PersistedStreamBuffer();
        var style = new TextStyle(Color.Red);

        buffer.AppendLine("Hello", style);
        buffer.AppendLine("World", style);

        var lines = buffer.GetAllStyledLines();
        Assert.Equal(2, lines.Count);
        Assert.Equal("Hello", lines[0].ToPlainText());
        Assert.Equal("World", lines[1].ToPlainText());
    }

    [Fact]
    public void AppendLine_WithForeground_CreatesColoredLine()
    {
        var buffer = new PersistedStreamBuffer();

        buffer.AppendLine("Hello", Color.Magenta);

        var lines = buffer.GetAllStyledLines();
        Assert.Equal(Color.Magenta, lines[0].Segments[0].Style.Foreground);
    }

    [Fact]
    public void GetVisibleStyledLines_ReturnsWrappedLines()
    {
        var buffer = new PersistedStreamBuffer();
        buffer.AppendLine("Hello World Test");

        // Width of 6 should wrap the line
        var visible = buffer.GetVisibleStyledLines(10, 6);

        Assert.True(visible.Count >= 2);
        Assert.Equal("Hello", visible[0].ToPlainText());
    }

    [Fact]
    public void GetVisibleStyledLines_PreservesStylesAcrossWrap()
    {
        var buffer = new PersistedStreamBuffer();
        buffer.Append(new StyledSegment("Hello World", new TextStyle(Color.Red)));
        buffer.AppendLine("");

        var visible = buffer.GetVisibleStyledLines(10, 6);

        // Both wrapped lines should have red color
        Assert.Equal(Color.Red, visible[0].Segments[0].Style.Foreground);
        if (visible.Count > 1)
        {
            Assert.Equal(Color.Red, visible[1].Segments[0].Style.Foreground);
        }
    }

    [Fact]
    public void GetVisibleLines_BackwardCompatible_ReturnsPlainText()
    {
        var buffer = new PersistedStreamBuffer();
        buffer.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));
        buffer.AppendLine("");

        var lines = buffer.GetVisibleLines(10, 80);

        Assert.Contains("Hello", lines);
    }

    [Fact]
    public void MultipleStyledAppends_OnSameLine_Coalesce()
    {
        var buffer = new PersistedStreamBuffer();
        var style = new TextStyle(Color.Green);

        buffer.Append(new StyledSegment("Hello", style));
        buffer.Append(new StyledSegment(" World", style));
        buffer.AppendLine("");

        var lines = buffer.GetAllStyledLines();
        // Should have coalesced into single segment
        Assert.Single(lines[0].Segments);
        Assert.Equal("Hello World", lines[0].ToPlainText());
    }

    [Fact]
    public void MultipleStyledAppends_DifferentStyles_NoCoalesce()
    {
        var buffer = new PersistedStreamBuffer();

        buffer.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));
        buffer.Append(new StyledSegment(" World", new TextStyle(Color.Blue)));
        buffer.AppendLine("");

        var lines = buffer.GetAllStyledLines();
        Assert.Equal(2, lines[0].Segments.Count);
    }

    [Fact]
    public void Clear_RemovesAllStyledContent()
    {
        var buffer = new PersistedStreamBuffer();
        buffer.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));
        buffer.AppendLine("");

        buffer.Clear();

        var lines = buffer.GetAllStyledLines();
        Assert.Empty(lines);
    }

    [Fact]
    public void AppendWithNewline_InText_SplitsLine()
    {
        var buffer = new PersistedStreamBuffer();
        buffer.Append(new StyledSegment("Hello\nWorld", new TextStyle(Color.Red)));
        buffer.AppendLine("");

        var lines = buffer.GetAllStyledLines();
        Assert.True(lines.Count >= 2);
        Assert.Equal("Hello", lines[0].ToPlainText());
        Assert.Equal("World", lines[1].ToPlainText());
    }

    [Fact]
    public void Scrolling_WithStyledContent_Works()
    {
        var buffer = new PersistedStreamBuffer();
        for (int i = 0; i < 20; i++)
        {
            buffer.AppendLine($"Line {i}", new TextStyle(Color.Green));
        }

        // Scroll up
        buffer.ScrollUp(5, 80, 10);

        var visible = buffer.GetVisibleStyledLines(10, 80);
        // Should see earlier lines after scrolling up
        Assert.True(visible.Count > 0);
    }
}
