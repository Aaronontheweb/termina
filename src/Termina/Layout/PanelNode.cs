// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// A layout node that renders a bordered panel with optional title and content.
/// </summary>
public sealed class PanelNode : LayoutNode, IInvalidatingNode
{
    private ILayoutNode _content = new EmptyNode();
    private IDisposable? _contentInvalidationSubscription;
    private readonly Subject<Unit> _invalidated = new();

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <summary>
    /// Panel title (displayed in top border).
    /// </summary>
    public string? Title { get; private set; }

    /// <summary>
    /// Border style.
    /// </summary>
    public BorderStyle Border { get; private set; } = BorderStyle.Single;

    /// <summary>
    /// Border color.
    /// </summary>
    public Color? BorderColor { get; private set; }

    /// <summary>
    /// Background color for the complete panel surface.
    /// </summary>
    public Color? Background { get; private set; }

    /// <summary>
    /// Title color.
    /// </summary>
    public Color? TitleColor { get; private set; }

    /// <summary>
    /// Inner padding.
    /// </summary>
    public int Padding { get; private set; }

    public PanelNode()
    {
        // Default to auto sizing
        HeightConstraint = new SizeConstraint.Auto();
        WidthConstraint = new SizeConstraint.Fill();
    }

    /// <summary>
    /// Set the panel title.
    /// </summary>
    public PanelNode WithTitle(string title)
    {
        Title = title;
        return this;
    }

    /// <summary>
    /// Set the border style.
    /// </summary>
    public PanelNode WithBorder(BorderStyle style)
    {
        Border = style;
        return this;
    }

    /// <summary>
    /// Set the border color.
    /// </summary>
    public PanelNode WithBorderColor(Color color)
    {
        BorderColor = color;
        return this;
    }

    /// <summary>
    /// Set the background color for the complete panel surface.
    /// </summary>
    public PanelNode WithBackground(Color color)
    {
        Background = color;
        return this;
    }

    /// <summary>
    /// Set the title color.
    /// </summary>
    public PanelNode WithTitleColor(Color color)
    {
        TitleColor = color;
        return this;
    }

    /// <summary>
    /// Set the content of the panel.
    /// </summary>
    public PanelNode WithContent(ILayoutNode content)
    {
        // Dispose the old content's subscription, and deactivate the old content
        // (active/inactive pattern) rather than dispose it — a swapped-out node may
        // still be reused. Content is disposed only at teardown, in Dispose().
        _contentInvalidationSubscription?.Dispose();
        if (_content is IActivatableNode oldContent)
            oldContent.OnDeactivate();

        // Set new content
        _content = content;

        // Subscribe to new content's invalidation events
        if (content is IInvalidatingNode invalidating)
        {
            _contentInvalidationSubscription = invalidating.Invalidated
                .Subscribe(_ => _invalidated.OnNext(Unit.Default));
        }

        return this;
    }

    /// <summary>
    /// Set the content to a text string.
    /// </summary>
    public PanelNode WithContent(string text)
    {
        return WithContent(new TextNode(text));
    }

    /// <summary>
    /// Set inner padding.
    /// </summary>
    public PanelNode WithPadding(int padding)
    {
        Padding = padding;
        return this;
    }

    /// <inheritdoc />
    internal override IEnumerable<ILayoutNode> GetChildNodes() => [_content];

    /// <inheritdoc />
    internal override void DisconnectChildInvalidationSubscriptions()
    {
        _contentInvalidationSubscription?.Dispose();
        _contentInvalidationSubscription = null;
    }

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        // Border takes 2 chars horizontally (left + right) and 2 rows vertically (top + bottom)
        var borderSize = Border == BorderStyle.None ? 0 : 2;
        var totalPadding = borderSize + (Padding * 2);

        var innerAvailable = available.Shrink(totalPadding, totalPadding);
        var contentSize = _content.Measure(innerAvailable);

        var totalWidth = contentSize.Width + totalPadding;
        var totalHeight = contentSize.Height + totalPadding;

        var width = WidthConstraint.Compute(available.Width, totalWidth, available.Width);
        var height = HeightConstraint.Compute(available.Height, totalHeight, available.Height);

        return new Size(width, height);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        if (!bounds.HasArea)
            return;

        // Create a sub-context for this panel's bounds so all coordinates are relative to the panel
        var panelContext = context.CreateSubContext(bounds);

        if (Background.HasValue)
        {
            panelContext.SetBackground(Background.Value);
            panelContext.Clear();
        }

        var hasBorder = Border != BorderStyle.None;
        var borderChars = GetBorderChars(Border);

        // Set border color
        if (BorderColor.HasValue)
            panelContext.SetForeground(BorderColor.Value);

        if (hasBorder && bounds.Height >= 2 && bounds.Width >= 2)
        {
            // Top border with title
            panelContext.WriteAt(0, 0, borderChars.TopLeft.ToString());

            var titleStart = 2;
            var titleEnd = titleStart;

            if (!string.IsNullOrEmpty(Title) && bounds.Width > 4)
            {
                var maxTitleLen = bounds.Width - 4;
                var displayTitle = DisplayWidth.GetColumnCount(Title) > maxTitleLen ? DisplayWidth.TruncateToColumns(Title, maxTitleLen) : Title;

                // Write border before title
                panelContext.WriteAt(1, 0, new string(borderChars.Horizontal, 1));

                // Write title
                if (TitleColor.HasValue)
                    panelContext.SetForeground(TitleColor.Value);
                panelContext.WriteAt(2, 0, displayTitle);
                if (BorderColor.HasValue)
                    panelContext.SetForeground(BorderColor.Value);
                else if (TitleColor.HasValue)
                    panelContext.ResetColors();

                titleEnd = 2 + DisplayWidth.GetColumnCount(displayTitle);
            }

            // Rest of top border
            var remainingTop = bounds.Width - titleEnd - 1;
            if (remainingTop > 0)
                panelContext.WriteAt(titleEnd, 0, new string(borderChars.Horizontal, remainingTop));
            panelContext.WriteAt(bounds.Width - 1, 0, borderChars.TopRight.ToString());

            // Side borders
            for (var y = 1; y < bounds.Height - 1; y++)
            {
                panelContext.WriteAt(0, y, borderChars.Vertical.ToString());
                panelContext.WriteAt(bounds.Width - 1, y, borderChars.Vertical.ToString());
            }

            // Bottom border
            panelContext.WriteAt(0, bounds.Height - 1, borderChars.BottomLeft.ToString());
            panelContext.WriteAt(1, bounds.Height - 1, new string(borderChars.Horizontal, bounds.Width - 2));
            panelContext.WriteAt(bounds.Width - 1, bounds.Height - 1, borderChars.BottomRight.ToString());
        }

        // Reset colors before content
        if (BorderColor.HasValue)
            panelContext.ResetColors();

        // Render content inside border
        var borderOffset = hasBorder ? 1 : 0;
        var contentBounds = new Rect(
            borderOffset + Padding,
            borderOffset + Padding,
            bounds.Width - 2 * (borderOffset + Padding),
            bounds.Height - 2 * (borderOffset + Padding));

        if (contentBounds.HasArea)
        {
            // Create a sub-context for the content area
            var contentContext = panelContext.CreateSubContext(contentBounds);
            if (Background.HasValue)
                contentContext = new SurfaceRenderContext(contentContext, Background.Value);
            var innerBounds = new Rect(0, 0, contentBounds.Width, contentBounds.Height);
            _content.Render(contentContext, innerBounds);
        }

        if (Background.HasValue)
            panelContext.ResetColors();
    }

    /// <inheritdoc />
    public override void OnActivate()
    {
        if (_content is IActivatableNode contentNode)
        {
            contentNode.OnActivate();
        }
        base.OnActivate();
    }

    /// <inheritdoc />
    public override void OnDeactivate()
    {
        if (_content is IActivatableNode contentNode)
        {
            contentNode.OnDeactivate();
        }
        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _contentInvalidationSubscription?.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _content.Dispose();
        base.Dispose();
    }

    private static BorderChars GetBorderChars(BorderStyle style) => style switch
    {
        BorderStyle.Single => new BorderChars('┌', '┐', '└', '┘', '─', '│'),
        BorderStyle.Double => new BorderChars('╔', '╗', '╚', '╝', '═', '║'),
        BorderStyle.Rounded => new BorderChars('╭', '╮', '╰', '╯', '─', '│'),
        BorderStyle.Ascii => new BorderChars('+', '+', '+', '+', '-', '|'),
        _ => new BorderChars(' ', ' ', ' ', ' ', ' ', ' ')
    };

    private readonly record struct BorderChars(
        char TopLeft, char TopRight,
        char BottomLeft, char BottomRight,
        char Horizontal, char Vertical);

    private sealed class SurfaceRenderContext : IRenderContext
    {
        private readonly IRenderContext _inner;
        private readonly Color _background;

        public SurfaceRenderContext(IRenderContext inner, Color background)
        {
            _inner = inner;
            _background = background;
            _inner.SetBackground(background);
        }

        public int Width => _inner.Width;
        public int Height => _inner.Height;

        public void WriteAt(int x, int y, string text) => _inner.WriteAt(x, y, text);
        public void WriteAt(int x, int y, char c) => _inner.WriteAt(x, y, c);
        public void SetForeground(Color color) => _inner.SetForeground(color);
        public void SetBackground(Color color) => _inner.SetBackground(color);

        public void ResetColors()
        {
            _inner.ResetColors();
            _inner.SetBackground(_background);
        }

        public void SetDecoration(TextDecoration decoration) => _inner.SetDecoration(decoration);
        public void ApplyStyle(TextStyle style) => _inner.ApplyStyle(style);
        public void Fill(int x, int y, int width, int height, char c = ' ') =>
            _inner.Fill(x, y, width, height, c);
        public void Clear() => _inner.Clear();

        public IRenderContext CreateSubContext(Rect bounds) =>
            new SurfaceRenderContext(_inner.CreateSubContext(bounds), _background);
    }
}
