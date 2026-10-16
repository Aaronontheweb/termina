// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;
using Termina.Terminal;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Tests for the StyledLine class.
/// </summary>
public class StyledLineTests
{
    [Fact]
    public void Constructor_CreatesEmptyLine()
    {
        var line = new StyledLine();

        Assert.Equal(0, line.Length);
        Assert.Empty(line.Segments);
    }

    [Fact]
    public void Append_SingleSegment_AddsToLine()
    {
        var line = new StyledLine();
        var segment = new StyledSegment("Hello", new TextStyle(Color.Red));

        line.Append(segment);

        Assert.Equal(5, line.Length);
        Assert.Single(line.Segments);
        Assert.Equal("Hello", line.Segments[0].Text);
    }

    [Fact]
    public void Append_MultipleSegments_AddsAll()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello ", new TextStyle(Color.Red)));
        line.Append(new StyledSegment("World", new TextStyle(Color.Blue)));

        Assert.Equal(11, line.Length);
        Assert.Equal(2, line.Segments.Count);
    }

    [Fact]
    public void Append_SameStyleSegments_Coalesces()
    {
        var line = new StyledLine();
        var style = new TextStyle(Color.Green);
        line.Append(new StyledSegment("Hello", style));
        line.Append(new StyledSegment(" World", style));

        Assert.Equal(11, line.Length);
        Assert.Single(line.Segments);
        Assert.Equal("Hello World", line.Segments[0].Text);
    }

    [Fact]
    public void Append_EmptySegment_IsIgnored()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));
        line.Append(new StyledSegment("", new TextStyle(Color.Blue)));

        Assert.Equal(5, line.Length);
        Assert.Single(line.Segments);
    }

    [Fact]
    public void ToPlainText_ReturnsCombinedText()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello ", new TextStyle(Color.Red)));
        line.Append(new StyledSegment("World", new TextStyle(Color.Blue)));

        var plain = line.ToPlainText();

        Assert.Equal("Hello World", plain);
    }

    [Fact]
    public void ToPlainText_EmptyLine_ReturnsEmptyString()
    {
        var line = new StyledLine();

        Assert.Equal("", line.ToPlainText());
    }

    [Fact]
    public void Substring_SingleSegment_ReturnsCorrectPortion()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello World", new TextStyle(Color.Red)));

        var sub = line.Substring(6, 5);

        Assert.Equal(5, sub.Length);
        Assert.Equal("World", sub.ToPlainText());
        Assert.Equal(Color.Red, sub.Segments[0].Style.Foreground);
    }

    [Fact]
    public void Substring_SpansMultipleSegments_PreservesStyles()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));
        line.Append(new StyledSegment(" ", new TextStyle(Color.Default)));
        line.Append(new StyledSegment("World", new TextStyle(Color.Blue)));

        // Get "lo Wo" which spans red, default, and blue segments
        var sub = line.Substring(3, 5);

        Assert.Equal(5, sub.Length);
        Assert.Equal("lo Wo", sub.ToPlainText());
        Assert.True(sub.Segments.Count >= 2); // May be 3 depending on coalescing
    }

    [Fact]
    public void Substring_FromStart_ReturnsCorrectPortion()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello World", new TextStyle(Color.Green)));

        var sub = line.Substring(0, 5);

        Assert.Equal("Hello", sub.ToPlainText());
    }

    [Fact]
    public void Substring_FullLine_ReturnsCopy()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));

        var sub = line.Substring(0, 5);

        Assert.Equal("Hello", sub.ToPlainText());
    }

    [Fact]
    public void Substring_ZeroLength_ReturnsEmptyLine()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));

        var sub = line.Substring(2, 0);

        Assert.Equal(0, sub.Length);
        Assert.Empty(sub.Segments);
    }

    [Fact]
    public void Substring_BeyondEnd_ThrowsException()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));

        Assert.Throws<ArgumentOutOfRangeException>(() => line.Substring(3, 10));
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));

        var clone = line.Clone();
        line.Append(new StyledSegment(" World", new TextStyle(Color.Blue)));

        Assert.Equal(11, line.Length);
        Assert.Equal(5, clone.Length);
    }

    [Fact]
    public void SliceByColumns_WideGlyphAtBudgetBoundary_DoesNotPullFromNextSegment()
    {
        // The first segment is "cc中" (c=1, c=1, 中=2 columns). A three-column slice can take
        // "cc" only, because the wide glyph needs two columns and 2 + 2 > 3. The slice must stop
        // there. It must NOT skip the rest of the first segment and take "e" from the next segment.
        var line = new StyledLine();
        line.Append(new StyledSegment("cc中", new TextStyle(Color.Red)));
        line.Append(new StyledSegment("e", new TextStyle(Color.Blue)));

        var slice = line.SliceByColumns(0, 3);

        Assert.Equal("cc", slice.ToPlainText());
    }

    [Fact]
    public void SliceByColumns_StartInsideMultiSegmentLine_KeepsColumnsContiguous()
    {
        // Skip the first two columns ("cc"), then take the rest across the segment boundary.
        var line = new StyledLine();
        line.Append(new StyledSegment("cc中", new TextStyle(Color.Red)));
        line.Append(new StyledSegment("e", new TextStyle(Color.Blue)));

        var slice = line.SliceByColumns(2, 100);

        Assert.Equal("中e", slice.ToPlainText());
    }
}
