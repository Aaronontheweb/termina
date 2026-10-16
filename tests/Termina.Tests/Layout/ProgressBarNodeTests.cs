using R3;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

public class ProgressBarNodeTests
{
    // ── fluent API ────────────────────────────────────────────────────────────

    [Fact]
    public void WithGradient_ReturnsSelf()
    {
        var node = new ProgressBarNode();
        var gradient = Gradient.Create(Color.Green, Color.Red);
        Assert.Same(node, node.WithGradient(gradient));
    }

    [Fact]
    public void WithColor_ReturnsSelf()
    {
        var node = new ProgressBarNode();
        Assert.Same(node, node.WithColor(Color.Cyan));
    }

    [Fact]
    public void WithEmptyColor_ReturnsSelf()
    {
        var node = new ProgressBarNode();
        Assert.Same(node, node.WithEmptyColor(Color.DarkGray));
    }

    // ── render: edge cases ────────────────────────────────────────────────────

    [Fact]
    public void Render_EmptyBounds_DoesNotThrow()
    {
        var node = new ProgressBarNode().WithValue(0.5);
        var ctx = new TestRenderContext(0, 0);
        var bounds = new Rect(0, 0, 0, 0);

        var ex = Record.Exception(() => node.Render(ctx, bounds));
        Assert.Null(ex);
    }

    // ── render: half-full ─────────────────────────────────────────────────────

    [Fact]
    public void Render_HalfFull_WritesFilledAndEmptyCells()
    {
        // width=10, value=0.5 → 5 filled, 5 empty
        var node = new ProgressBarNode().WithValue(0.5);
        var ctx = new TestRenderContext(10, 1);
        var bounds = new Rect(0, 0, 10, 1);

        node.Render(ctx, bounds);

        var cells = ctx.WrittenCells;
        Assert.Equal(10, cells.Count);

        // First 5 should be fill char
        for (var i = 0; i < 5; i++)
            Assert.Equal("█", cells[i].Text);

        // Remaining 5 should be empty char
        for (var i = 5; i < 10; i++)
            Assert.Equal("░", cells[i].Text);
    }

    // ── render: gradient ─────────────────────────────────────────────────────

    [Fact]
    public void Render_WithGradient_AppliesGradientColors()
    {
        // Green→Red gradient, value=1.0, width=10 → all 10 cols filled
        // col 0 → t=0 → green, col 9 → t=1 → red
        var gradient = Gradient.Create(Color.Green, Color.Red);
        var node = new ProgressBarNode()
            .WithGradient(gradient)
            .WithValue(1.0);

        var ctx = new TestRenderContext(10, 1);
        node.Render(ctx, new Rect(0, 0, 10, 1));

        var colors = ctx.ForegroundColors;
        Assert.True(colors.Count >= 2, "Expected at least 2 color entries");

        var firstColor = colors.First(c => c.X == 0).Color;
        var lastColor = colors.First(c => c.X == 9).Color;

        Assert.Equal(Color.Green, firstColor);
        Assert.Equal(Color.Red, lastColor);
    }

    // ── render: label ─────────────────────────────────────────────────────────

    [Fact]
    public void Render_WithLabel_RendersLabelContaining67()
    {
        // value=0.67, format="{0:P0}" → label contains "67"
        var node = new ProgressBarNode()
            .WithValue(0.67)
            .WithLabel("{0:P0}");

        var ctx = new TestRenderContext(20, 1);
        node.Render(ctx, new Rect(0, 0, 20, 1));

        var labelCell = ctx.WrittenCells.FirstOrDefault(c => c.Text.Contains("67"));
        Assert.NotEqual(default, labelCell);
    }

    [Fact]
    public void Render_CjkLabel_ReservesDisplayColumns()
    {
        var node = new ProgressBarNode()
            .WithValue(0.5)
            .WithLabel("已复制");
        var terminal = new VirtualTerminal(20, 1);
        var context = new RegionRenderContext(terminal, 0, 0, 20, 1);

        node.Render(context, new Rect(0, 0, 10, 1));

        Assert.Equal('已', terminal.GetChar(4, 0));
        Assert.Equal('制', terminal.GetChar(8, 0));
    }

    [Fact]
    public void WithWideFillAndEmptyChars_IgnoresInvalidBarCharacters()
    {
        var node = new ProgressBarNode()
            .WithValue(1.0)
            .WithFillChar('你')
            .WithEmptyChar('好');
        var terminal = new VirtualTerminal(8, 1);
        var context = new RegionRenderContext(terminal, 0, 0, 8, 1);

        node.Render(context, new Rect(0, 0, 4, 1));

        Assert.Equal('█', terminal.GetChar(0, 0));
        Assert.DoesNotContain("你", terminal.GetLine(0));
        Assert.DoesNotContain("好", terminal.GetLine(0));
    }

    // ── render: range normalization ───────────────────────────────────────────

    [Fact]
    public void Render_WithRange_NormalizesCorrectly()
    {
        // range 0-200, value=100 → normalized=0.5 → 5 filled of 10
        var node = new ProgressBarNode()
            .WithRange(0, 200)
            .WithValue(100);

        var ctx = new TestRenderContext(10, 1);
        node.Render(ctx, new Rect(0, 0, 10, 1));

        var filled = ctx.WrittenCells.Count(c => c.Text == "█");
        Assert.Equal(5, filled);
    }

    // ── dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CompletesInvalidated()
    {
        var node = new ProgressBarNode();
        var completed = false;
        node.Invalidated.Subscribe(
            onNext: _ => { },
            onErrorResume: _ => { },
            onCompleted: _ => completed = true
        );

        node.Dispose();

        Assert.True(completed);
    }

    // ── shared test context (mirrors GraphNodeTests.TestRenderContext) ─────────

    private sealed class TestRenderContext : IRenderContext
    {
        private readonly int _width;
        private readonly int _height;
        private Color? _currentForeground;

        public List<(int X, int Y, string Text)> WrittenCells { get; } = [];
        public List<(int X, int Y, Color Color)> ForegroundColors { get; } = [];

        public TestRenderContext(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public int Width => _width;
        public int Height => _height;

        public void WriteAt(int x, int y, char c)
        {
            WrittenCells.Add((x, y, c.ToString()));
            if (_currentForeground.HasValue)
                ForegroundColors.Add((x, y, _currentForeground.Value));
        }

        public void WriteAt(int x, int y, string text)
        {
            WrittenCells.Add((x, y, text));
            if (_currentForeground.HasValue)
                ForegroundColors.Add((x, y, _currentForeground.Value));
        }

        public void SetForeground(Color color) => _currentForeground = color;
        public void SetBackground(Color color) { }
        public void ResetColors() => _currentForeground = null;
        public void SetDecoration(TextDecoration decoration) { }
        public void ApplyStyle(TextStyle style) { }
        public void Fill(int x, int y, int width, int height, char c = ' ') { }
        public void Clear() { }

        public IRenderContext CreateSubContext(Rect bounds) => new SubContext(this, bounds);

        private sealed class SubContext : IRenderContext
        {
            private readonly TestRenderContext _parent;
            private readonly Rect _bounds;

            public SubContext(TestRenderContext parent, Rect bounds)
            {
                _parent = parent;
                _bounds = bounds;
            }

            public int Width => _bounds.Width;
            public int Height => _bounds.Height;

            public void WriteAt(int x, int y, char c) => _parent.WriteAt(_bounds.X + x, _bounds.Y + y, c);
            public void WriteAt(int x, int y, string text) => _parent.WriteAt(_bounds.X + x, _bounds.Y + y, text);
            public void SetForeground(Color color) => _parent.SetForeground(color);
            public void SetBackground(Color color) => _parent.SetBackground(color);
            public void ResetColors() => _parent.ResetColors();
            public void SetDecoration(TextDecoration decoration) { }
            public void ApplyStyle(TextStyle style) { }
            public void Fill(int x, int y, int width, int height, char c = ' ') { }
            public void Clear() { }

            public IRenderContext CreateSubContext(Rect bounds) =>
                new SubContext(_parent, new Rect(_bounds.X + bounds.X, _bounds.Y + bounds.Y, bounds.Width, bounds.Height));
        }
    }
}
