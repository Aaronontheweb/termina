// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;
using Termina.Terminal;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Tests for the StyledWordWrapper class.
/// </summary>
public class StyledWordWrapperTests
{
    [Fact]
    public void WrapLine_ShortLine_NoWrap()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello", new TextStyle(Color.Red)));

        var wrapped = StyledWordWrapper.WrapLine(line, 20);

        Assert.Single(wrapped);
        Assert.Equal("Hello", wrapped[0].ToPlainText());
    }

    [Fact]
    public void WrapLine_LongLine_WrapsAtWordBoundary()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello World", new TextStyle(Color.Red)));

        var wrapped = StyledWordWrapper.WrapLine(line, 6);

        Assert.Equal(2, wrapped.Count);
        Assert.Equal("Hello", wrapped[0].ToPlainText());
        Assert.Equal("World", wrapped[1].ToPlainText());
    }

    [Fact]
    public void WrapLine_PreservesStyleAcrossWrap()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello World", new TextStyle(Color.Red)));

        var wrapped = StyledWordWrapper.WrapLine(line, 6);

        Assert.Equal(Color.Red, wrapped[0].Segments[0].Style.Foreground);
        Assert.Equal(Color.Red, wrapped[1].Segments[0].Style.Foreground);
    }

    [Fact]
    public void WrapLine_MixedStyles_PreservesStylesPerWord()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Red ", new TextStyle(Color.Red)));
        line.Append(new StyledSegment("Blue", new TextStyle(Color.Blue)));

        var wrapped = StyledWordWrapper.WrapLine(line, 5);

        Assert.Equal(2, wrapped.Count);
        Assert.Equal("Red", wrapped[0].ToPlainText());
        Assert.Equal("Blue", wrapped[1].ToPlainText());
        Assert.Equal(Color.Red, wrapped[0].Segments[0].Style.Foreground);
        Assert.Equal(Color.Blue, wrapped[1].Segments[0].Style.Foreground);
    }

    [Fact]
    public void WrapLine_LongWord_BreaksWord()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Supercalifragilistic", new TextStyle(Color.Green)));

        var wrapped = StyledWordWrapper.WrapLine(line, 10);

        Assert.Equal(2, wrapped.Count);
        Assert.Equal("Supercalif", wrapped[0].ToPlainText());
        Assert.Equal("ragilistic", wrapped[1].ToPlainText());
    }

    [Fact]
    public void WrapLine_LongWord_PreservesStyleAcrossBreak()
    {
        var line = new StyledLine();
        var style = new TextStyle(Color.Yellow, Color.Default, TextDecoration.Bold);
        line.Append(new StyledSegment("Supercalifragilistic", style));

        var wrapped = StyledWordWrapper.WrapLine(line, 10);

        Assert.Equal(Color.Yellow, wrapped[0].Segments[0].Style.Foreground);
        Assert.Equal(TextDecoration.Bold, wrapped[0].Segments[0].Style.Decoration);
        Assert.Equal(Color.Yellow, wrapped[1].Segments[0].Style.Foreground);
        Assert.Equal(TextDecoration.Bold, wrapped[1].Segments[0].Style.Decoration);
    }

    [Fact]
    public void WrapLine_EmptyLine_ReturnsSingleEmptyLine()
    {
        var line = new StyledLine();

        var wrapped = StyledWordWrapper.WrapLine(line, 10);

        Assert.Single(wrapped);
        Assert.Equal(0, wrapped[0].Length);
    }

    [Fact]
    public void WrapLine_MultipleSpaces_Handled()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hello    World", new TextStyle(Color.Red)));

        var wrapped = StyledWordWrapper.WrapLine(line, 8);

        // The two words do not fit together at width 8, so they wrap onto separate lines and
        // the whitespace is dropped at the break.
        Assert.Equal(2, wrapped.Count);
        Assert.Equal("Hello", wrapped[0].ToPlainText());
        Assert.Equal("World", wrapped[1].ToPlainText());
    }

    [Fact]
    public void WrapLines_MultipleLines_WrapsEach()
    {
        var lines = new List<StyledLine>
        {
            CreateLine("Hello World", Color.Red),
            CreateLine("Foo Bar Baz", Color.Blue)
        };

        var wrapped = StyledWordWrapper.WrapLines(lines, 6);

        // "Hello World" wraps to 2 lines, "Foo Bar Baz" wraps to 3 lines
        Assert.True(wrapped.Count >= 4);
    }

    [Fact]
    public void WrapLine_MidWordStyleChange_PreservesStyles()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("Hel", new TextStyle(Color.Red)));
        line.Append(new StyledSegment("lo World", new TextStyle(Color.Blue)));

        var wrapped = StyledWordWrapper.WrapLine(line, 6);

        // First line should have mixed styles within "Hello"
        Assert.Equal("Hello", wrapped[0].ToPlainText());
        Assert.True(wrapped[0].Segments.Count >= 2); // "Hel" (red) + "lo" (blue)
    }

    [Fact]
    public void WrapLine_WidthOne_CharacterPerLine()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("ABC", new TextStyle(Color.Red)));

        var wrapped = StyledWordWrapper.WrapLine(line, 1);

        Assert.Equal(3, wrapped.Count);
        Assert.Equal("A", wrapped[0].ToPlainText());
        Assert.Equal("B", wrapped[1].ToPlainText());
        Assert.Equal("C", wrapped[2].ToPlainText());
    }

    [Fact]
    public void WrapLine_MultiWordSegmentWithBackground_PreservesBackgroundColorOnSeparatorSpace()
    {
        var line = new StyledLine();
        var style = new TextStyle(Color.Black, Color.BrightMagenta, TextDecoration.Bold);
        line.Append(new StyledSegment("[Highlighted Message]", style));

        var wrapped = StyledWordWrapper.WrapLine(line, 30);

        Assert.Single(wrapped);
        Assert.Equal("[Highlighted Message]", wrapped[0].ToPlainText());

        // All segments in the wrapped line (including the space separator segment between words) must inherit background color
        foreach (var segment in wrapped[0].Segments)
        {
            Assert.Equal(Color.BrightMagenta, segment.Style.Background);
        }
    }

    [Fact]
    public void WrapLine_BackgroundWordAfterUnstyledWord_SpaceSeparatorDoesNotInheritBackground()
    {
        var line = new StyledLine();
        line.Append(new StyledSegment("[YT]", new TextStyle(Color.Red))); // Unstyled background
        line.Append(new StyledSegment(" ", TextStyle.Default));
        line.Append(new StyledSegment("[GIFT SUB]", new TextStyle(Color.Black, Color.BrightGreen, TextDecoration.Bold)));

        var wrapped = StyledWordWrapper.WrapLine(line, 30);

        Assert.Single(wrapped);

        // Find the space segment between [YT] and [GIFT SUB]
        var spaceSegment = wrapped[0].Segments.First(s => s.Text == " ");
        Assert.NotEqual(Color.BrightGreen, spaceSegment.Style.Background);
    }

    // The separator inserted between two words on a wrapped line must carry the style of the
    // whitespace that separated them in the SOURCE. These cases force a real wrap so the
    // separator branch runs, unlike the two fast-path cases above.

    [Fact]
    public void WrapLine_ContiguousHighlight_ForcedWrap_SeparatorKeepsSourceBackground()
    {
        // Single magenta segment. The space between the two words is magenta in the source.
        // Trailing "EXTRA" pushes the line past the width so the wrapper actually runs.
        var line = new StyledLine();
        var style = new TextStyle(Color.Black, Color.BrightMagenta, TextDecoration.Bold);
        line.Append(new StyledSegment("[Highlighted Message] EXTRA", style));

        var wrapped = StyledWordWrapper.WrapLine(line, 21);

        Assert.Equal("[Highlighted Message]", wrapped[0].ToPlainText());
        foreach (var segment in wrapped[0].Segments)
            Assert.Equal(Color.BrightMagenta, segment.Style.Background);

        // The separator shares the words' style, so the whole line coalesces into one segment.
        Assert.Single(wrapped[0].Segments);
    }

    [Fact]
    public void WrapLine_UnstyledSpaceBetweenBadges_ForcedWrap_SeparatorHasNoBackground()
    {
        // The space between [YT] and [GIFT is a default-styled segment in the source.
        var line = new StyledLine();
        line.Append(new StyledSegment("[YT]", new TextStyle(Color.Red)));
        line.Append(new StyledSegment(" ", TextStyle.Default));
        line.Append(new StyledSegment("[GIFT SUB] and more text", new TextStyle(Color.Black, Color.BrightGreen, TextDecoration.Bold)));

        var wrapped = StyledWordWrapper.WrapLine(line, 10);

        var spaceSegment = wrapped[0].Segments.First(s => s.Text == " ");
        Assert.NotEqual(Color.BrightGreen, spaceSegment.Style.Background);
    }

    [Fact]
    public void WrapLine_SameBackgroundButUnstyledSourceSpace_ForcedWrap_SeparatorStaysUnstyled()
    {
        // Two magenta words separated by a DEFAULT space in the source. A same-background
        // heuristic would paint the separator magenta. The source space was unstyled, so the
        // separator must stay unstyled.
        var line = new StyledLine();
        var magenta = new TextStyle(Color.White, Color.BrightMagenta);
        line.Append(new StyledSegment("AAA", magenta));
        line.Append(new StyledSegment(" ", TextStyle.Default));
        line.Append(new StyledSegment("BBB", magenta));
        line.Append(new StyledSegment(" CCC", magenta)); // extra word forces a wrap at width 8

        var wrapped = StyledWordWrapper.WrapLine(line, 8);

        Assert.Equal("AAA BBB", wrapped[0].ToPlainText());
        var spaceSegment = wrapped[0].Segments.First(s => s.Text == " ");
        Assert.NotEqual(Color.BrightMagenta, spaceSegment.Style.Background);
    }

    [Fact]
    public void WrapLine_DifferentBackgrounds_ForcedWrap_SeparatorHasNoBackground()
    {
        // Red-background word, a default source space, then a green-background word. The words
        // stay on one line after a forced wrap. The separator must not bleed either background.
        var line = new StyledLine();
        line.Append(new StyledSegment("AAA", new TextStyle(Color.Default, Color.Red)));
        line.Append(new StyledSegment(" ", TextStyle.Default));
        line.Append(new StyledSegment("BBB CCC", new TextStyle(Color.Default, Color.BrightGreen)));

        var wrapped = StyledWordWrapper.WrapLine(line, 8);

        Assert.Equal("AAA BBB", wrapped[0].ToPlainText());
        var spaceSegment = wrapped[0].Segments.First(s => s.Text == " ");
        Assert.Equal(Color.Default, spaceSegment.Style.Background);
    }

    [Fact]
    public void WrapLine_StyledSourceSpace_ForcedWrap_SeparatorMatchesSourceWhitespace()
    {
        // The source space between AA and BB carries a green background. The separator must
        // reproduce that source style, not a default space.
        var line = new StyledLine();
        line.Append(new StyledSegment("AA", TextStyle.Default));
        line.Append(new StyledSegment(" ", new TextStyle(Color.Default, Color.BrightGreen)));
        line.Append(new StyledSegment("BB CC", TextStyle.Default));

        var wrapped = StyledWordWrapper.WrapLine(line, 6);

        Assert.Equal("AA BB", wrapped[0].ToPlainText());
        var spaceSegment = wrapped[0].Segments.First(s => s.Text == " ");
        Assert.Equal(Color.BrightGreen, spaceSegment.Style.Background);
    }

    [Fact]
    public void WrapLine_WideCharacterWord_BreaksOnColumns_PreservesStyle()
    {
        // Each CJK glyph is two columns wide. A four-column width must break the word on
        // display columns, not on char count, and keep the style on both halves.
        var line = new StyledLine();
        var style = new TextStyle(Color.Yellow, Color.Default, TextDecoration.Bold);
        line.Append(new StyledSegment("你好世界", style));

        var wrapped = StyledWordWrapper.WrapLine(line, 4);

        Assert.Equal(2, wrapped.Count);
        Assert.Equal("你好", wrapped[0].ToPlainText());
        Assert.Equal("世界", wrapped[1].ToPlainText());
        Assert.Equal(Color.Yellow, wrapped[0].Segments[0].Style.Foreground);
        Assert.Equal(TextDecoration.Bold, wrapped[1].Segments[0].Style.Decoration);
    }

    // Characterization of CURRENT behavior. A future refactor that preserves whitespace runs
    // and leading indentation would change these two cases, and should update them on purpose.

    [Fact]
    public void WrapLine_FastPath_PreservesMultipleSpacesVerbatim()
    {
        // When the whole line fits, it is cloned verbatim, so runs of spaces survive.
        var line = new StyledLine();
        line.Append(new StyledSegment("Hi     There", new TextStyle(Color.Red)));

        var wrapped = StyledWordWrapper.WrapLine(line, 20);

        Assert.Single(wrapped);
        Assert.Equal("Hi     There", wrapped[0].ToPlainText());
    }

    [Fact]
    public void WrapLine_ForcedWrap_CollapsesInternalSpaceRunToSingleSeparator()
    {
        // When a wrap runs, a run of spaces between two words on one output line collapses to a
        // single separator space.
        var line = new StyledLine();
        line.Append(new StyledSegment("AA    BB CCCCCCCC", new TextStyle(Color.Red)));

        var wrapped = StyledWordWrapper.WrapLine(line, 6);

        Assert.Equal("AA BB", wrapped[0].ToPlainText());
    }

    [Fact]
    public void WrapLine_ForcedWrap_DropsLeadingIndentation()
    {
        // When a wrap runs, leading whitespace is dropped rather than kept as indentation.
        var line = new StyledLine();
        line.Append(new StyledSegment("    Hello World", new TextStyle(Color.Red)));

        var wrapped = StyledWordWrapper.WrapLine(line, 6);

        Assert.Equal("Hello", wrapped[0].ToPlainText());
    }

    private static StyledLine CreateLine(string text, Color color)
    {
        var line = new StyledLine();
        line.Append(new StyledSegment(text, new TextStyle(color)));
        return line;
    }
}
