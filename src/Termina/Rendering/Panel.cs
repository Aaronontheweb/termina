// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Rendering;

/// <summary>
/// The style of border to draw around the panel.
/// </summary>
public enum BorderStyle
{
    /// <summary>
    /// No border.
    /// </summary>
    None,

    /// <summary>
    /// Single-line box drawing characters (┌─┐│└┘).
    /// </summary>
    Single,

    /// <summary>
    /// Double-line box drawing characters (╔═╗║╚╝).
    /// </summary>
    Double,

    /// <summary>
    /// Rounded corners with single lines (╭─╮│╰╯).
    /// </summary>
    Rounded,

    /// <summary>
    /// ASCII characters (+, -, |).
    /// </summary>
    Ascii
}

/// <summary>
/// A container component that draws a border around content.
/// </summary>
public sealed class Panel : IRenderable
{
    /// <summary>
    /// Gets or sets the content to display inside the panel.
    /// </summary>
    public IRenderable? Content { get; set; }

    /// <summary>
    /// Gets or sets the panel title, displayed in the top border.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the border style.
    /// </summary>
    public BorderStyle Border { get; set; } = BorderStyle.Single;

    /// <summary>
    /// Gets or sets the border color.
    /// </summary>
    public Color BorderColor { get; set; } = Color.Default;

    /// <summary>
    /// Gets or sets the background color for the panel interior.
    /// </summary>
    public Color Background { get; set; } = Color.Default;

    /// <inheritdoc />
    public void Render(IRenderContext context)
    {
        if (Border == BorderStyle.None)
        {
            // No border - render content directly
            Content?.Render(context);
            return;
        }

        var (topLeft, topRight, bottomLeft, bottomRight, horizontal, vertical) = GetBorderChars();

        context.SetForeground(BorderColor);
        context.SetBackground(Background);

        var width = context.Width;
        var height = context.Height;

        // Draw top border
        context.WriteAt(0, 0, topLeft);
        for (var x = 1; x < width - 1; x++)
            context.WriteAt(x, 0, horizontal);
        if (width > 1)
            context.WriteAt(width - 1, 0, topRight);

        // Draw title if present
        if (!string.IsNullOrEmpty(Title) && width > 4)
        {
            var maxTitleLen = width - 4; // Leave room for borders and spaces
            var displayTitle = DisplayWidth.GetColumnCount(Title) > maxTitleLen ? DisplayWidth.TruncateToColumns(Title, maxTitleLen) : Title;
            var titleX = 2; // After corner and space
            context.WriteAt(titleX, 0, displayTitle);
        }

        // Draw side borders
        for (var y = 1; y < height - 1; y++)
        {
            context.WriteAt(0, y, vertical);
            if (width > 1)
                context.WriteAt(width - 1, y, vertical);
        }

        // Draw bottom border
        if (height > 1)
        {
            context.WriteAt(0, height - 1, bottomLeft);
            for (var x = 1; x < width - 1; x++)
                context.WriteAt(x, height - 1, horizontal);
            if (width > 1)
                context.WriteAt(width - 1, height - 1, bottomRight);
        }

        context.ResetColors();

        // Render content in interior
        if (Content != null && width > 2 && height > 2)
        {
            // Create a sub-context for the interior
            var interiorContext = new InteriorRenderContext(context, 1, 1, width - 2, height - 2);
            Content.Render(interiorContext);
        }
    }

    /// <inheritdoc />
    public (int Width, int Height) Measure(int availableWidth, int availableHeight)
    {
        if (Border == BorderStyle.None)
        {
            // No border overhead
            if (Content == null)
                return (0, 0);
            return Content.Measure(availableWidth, availableHeight);
        }

        // Border adds 2 to each dimension
        var borderOverhead = 2;

        if (Content == null)
        {
            // Just the border
            return (Math.Min(borderOverhead, availableWidth), Math.Min(borderOverhead, availableHeight));
        }

        var contentAvailableWidth = Math.Max(0, availableWidth - borderOverhead);
        var contentAvailableHeight = Math.Max(0, availableHeight - borderOverhead);

        var (contentWidth, contentHeight) = Content.Measure(contentAvailableWidth, contentAvailableHeight);

        return (
            Math.Min(contentWidth + borderOverhead, availableWidth),
            Math.Min(contentHeight + borderOverhead, availableHeight)
        );
    }

    /// <summary>
    /// Gets the border characters for the current border style.
    /// </summary>
    private (char TopLeft, char TopRight, char BottomLeft, char BottomRight, char Horizontal, char Vertical) GetBorderChars()
    {
        return Border switch
        {
            BorderStyle.Single => ('┌', '┐', '└', '┘', '─', '│'),
            BorderStyle.Double => ('╔', '╗', '╚', '╝', '═', '║'),
            BorderStyle.Rounded => ('╭', '╮', '╰', '╯', '─', '│'),
            BorderStyle.Ascii => ('+', '+', '+', '+', '-', '|'),
            _ => ('┌', '┐', '└', '┘', '─', '│')
        };
    }

    /// <summary>
    /// A render context that offsets and clips to the panel interior.
    /// </summary>
    private sealed class InteriorRenderContext : IRenderContext
    {
        private readonly IRenderContext _parent;
        private readonly int _offsetX;
        private readonly int _offsetY;

        public InteriorRenderContext(IRenderContext parent, int offsetX, int offsetY, int width, int height)
        {
            _parent = parent;
            _offsetX = offsetX;
            _offsetY = offsetY;
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        public void WriteAt(int x, int y, string text)
        {
            if (y < 0 || y >= Height)
                return;
            text = DisplayWidth.SanitizeTerminalText(text);
            var skipColumns = 0;
            if (x < 0)
            {
                skipColumns = -x;
                x = 0;
            }
            if (x >= Width)
                return;
            text = DisplayWidth.SliceByColumns(text, skipColumns, Width - x);
            if (string.IsNullOrEmpty(text))
                return;
            _parent.WriteAt(x + _offsetX, y + _offsetY, text);
        }

        public void WriteAt(int x, int y, char c)
        {
            WriteAt(x, y, c.ToString());
        }

        public void SetForeground(Color color) => _parent.SetForeground(color);
        public void SetBackground(Color color) => _parent.SetBackground(color);
        public void ResetColors() => _parent.ResetColors();
        public void SetDecoration(Terminal.TextDecoration decoration) => _parent.SetDecoration(decoration);
        public void ApplyStyle(Terminal.TextStyle style) => _parent.ApplyStyle(style);

        public void Fill(int x, int y, int width, int height, char c = ' ')
        {
            // Clip to our bounds
            if (x < 0) { width += x; x = 0; }
            if (y < 0) { height += y; y = 0; }
            if (x + width > Width) width = Width - x;
            if (y + height > Height) height = Height - y;
            if (width <= 0 || height <= 0)
                return;
            _parent.Fill(x + _offsetX, y + _offsetY, width, height, c);
        }

        public void Clear() => Fill(0, 0, Width, Height, ' ');

        public IRenderContext CreateSubContext(Layout.Rect bounds)
        {
            var clippedX = Math.Max(0, bounds.X);
            var clippedY = Math.Max(0, bounds.Y);
            var clippedRight = Math.Min(Width, bounds.Right);
            var clippedBottom = Math.Min(Height, bounds.Bottom);
            var clippedWidth = Math.Max(0, clippedRight - clippedX);
            var clippedHeight = Math.Max(0, clippedBottom - clippedY);

            return new InteriorRenderContext(
                _parent,
                _offsetX + clippedX,
                _offsetY + clippedY,
                clippedWidth,
                clippedHeight);
        }
    }
}
