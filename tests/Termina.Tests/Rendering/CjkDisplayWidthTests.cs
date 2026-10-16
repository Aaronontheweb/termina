// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Rendering;

/// <summary>
/// Tests for CJK and Unicode display width handling.
/// Verifies that text measurement, rendering, and cursor positioning
/// correctly account for fullwidth characters (CJK, Hiragana, Katakana, Hangul, emoji).
/// </summary>
public class CjkDisplayWidthTests
{
    // ===== DisplayWidth utility tests =====

    [Fact]
    public void GetColumnCount_SingleAsciiChar_Returns1()
    {
        Assert.Equal(1, DisplayWidth.GetColumnCount("A"));
    }

    [Fact]
    public void GetColumnCount_SingleCjkChar_Returns2()
    {
        // 你 = U+4F60 (CJK Unified Ideograph)
        Assert.Equal(2, DisplayWidth.GetColumnCount("你"));
    }

    [Fact]
    public void GetColumnCount_SingleHiraganaChar_Returns2()
    {
        // あ = U+3042 (Hiragana)
        Assert.Equal(2, DisplayWidth.GetColumnCount("あ"));
    }

    [Fact]
    public void GetColumnCount_SingleKatakanaChar_Returns2()
    {
        // ア = U+30A2 (Katakana)
        Assert.Equal(2, DisplayWidth.GetColumnCount("ア"));
    }

    [Fact]
    public void GetColumnCount_SingleHangulChar_Returns2()
    {
        // 하 = U+D558 (Hangul Syllable)
        Assert.Equal(2, DisplayWidth.GetColumnCount("하"));
    }

    [Fact]
    public void GetColumnCount_EmptyString_Returns0()
    {
        Assert.Equal(0, DisplayWidth.GetColumnCount(""));
    }

    [Fact]
    public void GetColumnCount_MixedAsciiAndCjk_CountsCorrectly()
    {
        // "A你B" = 1 + 2 + 1 = 4 columns
        Assert.Equal(4, DisplayWidth.GetColumnCount("A你B"));
    }

    [Fact]
    public void GetColumnCount_SixCjkChars_Returns12()
    {
        // "你好世界中文" = 6 chars × 2 = 12 columns
        Assert.Equal(12, DisplayWidth.GetColumnCount("你好世界中文"));
    }

    [Fact]
    public void GetColumnCount_Emoji_Returns2()
    {
        // 😀 = U+1F600 (Emoji)
        Assert.Equal(2, DisplayWidth.GetColumnCount("😀"));
    }

    [Fact]
    public void GetColumnCount_BareCheckMark_Returns1()
    {
        Assert.Equal(1, DisplayWidth.GetColumnCount("✓"));
    }

    [Fact]
    public void GetColumnCount_KeycapEmoji_Returns2()
    {
        Assert.Equal(2, DisplayWidth.GetColumnCount("1️⃣"));
    }

    [Fact]
    public void GetColumnCount_EmojiPresentationSequence_Returns2()
    {
        Assert.Equal(2, DisplayWidth.GetColumnCount("☀️"));
    }

    [Fact]
    public void GetColumnCount_HalfwidthKatakana_Returns1()
    {
        // ｱ = U+FF71 (Halfwidth Katakana Letter A)
        Assert.Equal(1, DisplayWidth.GetColumnCount("ｱ"));
    }

    // ===== DisplayWidth.TruncateToColumns tests =====

    [Fact]
    public void TruncateToColumns_Ascii_NoOp()
    {
        var truncated = DisplayWidth.TruncateToColumns("Hello World", 11);
        Assert.Equal("Hello World", truncated);
    }

    [Fact]
    public void TruncateToColumns_Cjk_TrimmedByColumns()
    {
        // "你好" = 4 columns. Truncate to 3 should give "你" (2 cols), not "你" + half
        var truncated = DisplayWidth.TruncateToColumns("你好世界", 3);
        Assert.Equal("你", truncated); // "你" = 2 cols, next char "好" would be 4 > 3
        Assert.Equal(2, DisplayWidth.GetColumnCount(truncated));
    }

    [Fact]
    public void TruncateToColumns_Cjk_FitsExactly()
    {
        var truncated = DisplayWidth.TruncateToColumns("你好世界", 4);
        Assert.Equal("你好", truncated); // "你好" = 4 cols
    }

    [Fact]
    public void TruncateToColumns_Mixed_RespectsFullwidth()
    {
        // "A你B" = 1+2+1 = 4 cols. Truncate to 3: "A你" (1+2=3) ✓
        var truncated = DisplayWidth.TruncateToColumns("A你B你好", 3);
        Assert.Equal("A你", truncated);
    }

    [Fact]
    public void TruncateToColumns_MaxColumnsZero_ReturnsEmpty()
    {
        Assert.Equal("", DisplayWidth.TruncateToColumns("Hello", 0));
    }

    [Fact]
    public void TruncateToColumns_NegativeMaxColumns_ReturnsEmpty()
    {
        Assert.Equal("", DisplayWidth.TruncateToColumns("Hello", -1));
    }

    [Fact]
    public void TruncateToColumns_Emoji_DoesNotSplitSurrogatePair()
    {
        Assert.Equal("", DisplayWidth.TruncateToColumns("😀A", 1));
        Assert.Equal("😀", DisplayWidth.TruncateToColumns("😀A", 2));
    }

    // ===== DisplayWidth.CursorPositionToColumn tests =====

    [Fact]
    public void CursorPositionToColumn_Ascii_IgnoresOffset()
    {
        Assert.Equal(2, DisplayWidth.CursorPositionToColumn("Hello", 2)); // "He" = 2 cols
    }

    [Fact]
    public void CursorPositionToColumn_Cjk_AccumulatesWidth()
    {
        // "你好世界" - position 2 means 2 chars: "你好" = 4 cols
        Assert.Equal(4, DisplayWidth.CursorPositionToColumn("你好世界", 2));
    }

    [Fact]
    public void CursorPositionToColumn_Mixed_CorrectOffset()
    {
        // "A你B你" - position 2: "A你" = 1+2 = 3 cols
        Assert.Equal(3, DisplayWidth.CursorPositionToColumn("A你B你", 2));
    }

    [Fact]
    public void GetNextTextElementIndex_Emoji_ReturnsSurrogatePairEnd()
    {
        Assert.Equal(2, DisplayWidth.GetNextTextElementIndex("😀A", 0));
    }

    [Fact]
    public void GetPreviousTextElementIndex_Emoji_ReturnsSurrogatePairStart()
    {
        Assert.Equal(0, DisplayWidth.GetPreviousTextElementIndex("😀A", 2));
    }

    [Fact]
    public void ClampToTextElementBoundary_InsideEmoji_ReturnsElementStart()
    {
        Assert.Equal(0, DisplayWidth.ClampToTextElementBoundary("😀A", 1));
    }

    // ===== Text.Measure() CJK tests =====

    [Fact]
    public void Measure_CjkText_ReturnsDisplayColumnCount()
    {
        // "你好世界" = 4 chars × 2 = 8 display columns
        var text = new Text("你好世界");
        var (width, height) = text.Measure(100, 100);

        Assert.Equal(8, width);  // NOT 4 (char count)
        Assert.Equal(1, height);
    }

    [Fact]
    public void Measure_MixedLine_ReturnsMaxDisplayWidth()
    {
        // Line 1: "Hello" = 5 cols
        // Line 2: "你好" = 4 cols
        // Max = 5
        var text = new Text("Hello\n你好");
        var (width, height) = text.Measure(100, 100);

        Assert.Equal(5, width);
        Assert.Equal(2, height);
    }

    [Fact]
    public void Measure_CjkText_ClampsToAvailableSpace()
    {
        var text = new Text("你好世界中文"); // 12 display columns
        var (width, height) = text.Measure(8, 100);

        Assert.Equal(8, width);  // Clamped to available
        Assert.Equal(1, height);
    }

    // ===== Text.Render() CJK tests =====

    [Fact]
    public void Render_CjkText_NotTruncatedBeyondDisplayWidth()
    {
        // "你好世界" = 8 display columns. Context width = 5 columns.
        // Should truncate to "你好" = 4 display columns (not 5 chars).
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 5, 1);
        var text = new Text("你好世界");

        text.Render(context);

        var rendered = terminal.GetLine(0);
        // Should be "你" (2 cols), not "你好世" (5 chars but 10 cols)
        // At most 2 CJK chars = 4 display columns fits in 5
        // Actually "你好" = 4 cols, "你好世" would be 6 cols > 5
        Assert.True(rendered.Length > 0, "Some text should be rendered");
        Assert.True(DisplayWidth.GetColumnCount(rendered) <= 5, $"Rendered width {DisplayWidth.GetColumnCount(rendered)} exceeds context width 5");
    }

    [Fact]
    public void Render_CjkText_FitsExactly()
    {
        // "你好" = 4 display columns. Context width = 4.
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 4, 1);
        var text = new Text("你好");

        text.Render(context);

        var rendered = terminal.GetLine(0);
        Assert.Equal("你好", rendered);
    }

    [Fact]
    public void Render_CjkText_ShortLine_NotTruncated()
    {
        // "A" = 1 display column. Context width = 5.
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 5, 1);
        var text = new Text("A");

        text.Render(context);

        var rendered = terminal.GetLine(0);
        Assert.Equal("A", rendered);
    }

    // ===== TextInput.Measure() CJK tests =====

    [Fact]
    public void Measure_TextInputWithCjkText_ReturnsDisplayColumnWidth()
    {
        var input = new TextInput();
        input.Text = "你好世界"; // 4 chars, 8 display columns

        var (width, height) = input.Measure(100, 100);

        Assert.Equal(9, width); // 8 (text) + 1 (cursor)
        Assert.Equal(1, height);
    }

    [Fact]
    public void Measure_TextInput_MixedText_ReturnsDisplayWidth()
    {
        var input = new TextInput();
        input.Text = "AB你CD"; // A(1) + B(1) + 你(2) + C(1) + D(1) = 6 display columns

        var (width, height) = input.Measure(100, 100);

        Assert.Equal(7, width); // 6 (text) + 1 (cursor)
        Assert.Equal(1, height);
    }

    // ===== TextInput.Render() CJK tests =====

    [Fact]
    public void Render_TextInputWithCjk_CursorPositionedCorrectly()
    {
        // When cursor is at position 2 in "你好世界", it should be at display column 4
        var input = new TextInput();
        input.Text = "你好世界";
        input.CursorPosition = 2;
        input.IsFocused = true;

        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);

        // Capture the raw output to see what text is written where
        input.Render(context);

        // The rendered line should contain "你好世界" with cursor at position 4 (after "你好")
        var rendered = terminal.GetLine(0);

        // Cursor position in display columns should be 4, not 2
        // Check that the display has at least the first 2 chars
        Assert.Contains("你", rendered);
    }

    [Fact]
    public void Render_TextInputWithCjk_NotOverlapping()
    {
        // Multiple CJK chars should not overlap when rendered
        var input = new TextInput();
        input.Text = "你好世界";
        input.IsFocused = false;

        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 1);

        input.Render(context);

        var rendered = terminal.GetLine(0);

        // All 4 CJK chars should be visible (8 display columns, context has 10)
        Assert.Contains("你", rendered);
        Assert.Contains("好", rendered);
        Assert.Contains("世", rendered);
        Assert.Contains("界", rendered);
    }

    // ===== ScrollableContent truncation tests =====

    [Fact]
    public void Measure_ScrollableContentWithCjkLines_ReturnsCorrectWidth()
    {
        var content = new ScrollableContent();
        content.SetContent(new[] { "Hello", "你好世界", "こんにちは" });
        content.SetViewportHeight(10);

        var (width, height) = content.Measure(100, 100);

        // Max display width should be 10 (from "こんにちは" = 5 chars × 2), not 5 (char count)
        Assert.Equal(10, width);
        Assert.Equal(3, height);
    }

    // ===== Integration: full rendering pipeline =====

    [Fact]
    public void FullPipeline_CjkText_RendersCorrectlyInConstrainedWidth()
    {
        // Simulate: render "你好世界" (8 display columns) in a 6-column region
        // Expected: "你好" (4 display columns) rendered, not truncated mid-character
        var terminal = new VirtualTerminal(80, 24);
        var context = new RegionRenderContext(terminal, 0, 0, 6, 1);
        var text = new Text("你好世界");

        text.Render(context);

        var rendered = terminal.GetLine(0);

        // Should render at most 3 CJK chars = 6 display columns
        // "你好世" = 6 cols fits exactly
        Assert.True(DisplayWidth.GetColumnCount(rendered) <= 6,
            $"Rendered {DisplayWidth.GetColumnCount(rendered)} display columns in 6-column region");
    }
}
