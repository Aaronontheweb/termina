// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Rendering;

/// <summary>
/// Render context that translates relative coordinates to absolute screen positions.
/// All rendering is clipped to the region bounds.
/// </summary>
public sealed class RegionRenderContext : IRenderContext, IScreenPositionedRenderContext
{
    private readonly IAnsiTerminal _terminal;
    private readonly int _offsetX;
    private readonly int _offsetY;

    /// <summary>
    /// Create a render context for a specific screen region.
    /// </summary>
    /// <param name="terminal">The terminal to render to.</param>
    /// <param name="offsetX">The X offset (screen column) of the region's top-left corner.</param>
    /// <param name="offsetY">The Y offset (screen row) of the region's top-left corner.</param>
    /// <param name="width">The width of the region.</param>
    /// <param name="height">The height of the region.</param>
    public RegionRenderContext(IAnsiTerminal terminal, int offsetX, int offsetY, int width, int height)
    {
        _terminal = terminal;
        _offsetX = offsetX;
        _offsetY = offsetY;
        Width = width;
        Height = height;
    }

    /// <inheritdoc />
    public int Width { get; }

    /// <inheritdoc />
    public int Height { get; }

    /// <inheritdoc />
    public void WriteAt(int x, int y, string text)
    {
        if (y < 0 || y >= Height || x >= Width)
            return;

        text = DisplayWidth.SanitizeTerminalText(text);

        // Clip text to fit within region by terminal columns.
        var startX = Math.Max(0, x);
        var skipColumns = startX - x;
        var availableWidth = Width - startX;

        if (availableWidth <= 0)
            return;

        var clippedText = DisplayWidth.SliceByColumns(text, skipColumns, availableWidth);
        if (string.IsNullOrEmpty(clippedText))
            return;

        _terminal.MoveTo(_offsetX + startX, _offsetY + y);
        _terminal.Write(clippedText);
    }

    /// <inheritdoc />
    public void WriteAt(int x, int y, char c)
    {
        WriteAt(x, y, c.ToString());
    }

    /// <inheritdoc />
    public void SetForeground(Color color)
    {
        _terminal.SetForeground(color);
    }

    /// <inheritdoc />
    public void SetBackground(Color color)
    {
        _terminal.SetBackground(color);
    }

    /// <inheritdoc />
    public void ResetColors()
    {
        _terminal.ResetColors();
    }

    /// <inheritdoc />
    public void SetDecoration(TextDecoration decoration)
    {
        if (_terminal is DiffingTerminal diffingTerminal)
        {
            diffingTerminal.SetDecoration(decoration);
            return;
        }

        if (_terminal is InlineTerminal inlineTerminal)
        {
            inlineTerminal.SetDecoration(decoration);
            return;
        }

        // Reset any previous decorations first
        if (decoration == TextDecoration.None)
        {
            // Just reset - handled by ResetColors
            return;
        }

        // Apply requested decorations
        if (decoration.HasFlag(TextDecoration.Bold))
            _terminal.Write(AnsiCodes.Bold);
        if (decoration.HasFlag(TextDecoration.Dim))
            _terminal.Write(AnsiCodes.Dim);
        if (decoration.HasFlag(TextDecoration.Italic))
            _terminal.Write(AnsiCodes.Italic);
        if (decoration.HasFlag(TextDecoration.Underline))
            _terminal.Write(AnsiCodes.Underline);
        if (decoration.HasFlag(TextDecoration.Strikethrough))
            _terminal.Write(AnsiCodes.Strikethrough);
    }

    /// <inheritdoc />
    public void ApplyStyle(TextStyle style)
    {
        if (style.HasForeground)
            SetForeground(style.Foreground);
        if (style.HasBackground)
            SetBackground(style.Background);
        if (style.HasDecoration)
            SetDecoration(style.Decoration);
    }

    /// <inheritdoc />
    public void Fill(int x, int y, int width, int height, char c = ' ')
    {
        // Clip to region bounds
        var startX = Math.Max(0, x);
        var startY = Math.Max(0, y);
        var endX = Math.Min(Width, x + width);
        var endY = Math.Min(Height, y + height);

        if (startX >= endX || startY >= endY)
            return;

        var fillWidth = endX - startX;
        var fillChar = DisplayWidth.GetColumnCount(c) == 1 ? c : ' ';
        var fillLine = new string(fillChar, fillWidth);

        for (var row = startY; row < endY; row++)
        {
            _terminal.MoveTo(_offsetX + startX, _offsetY + row);
            _terminal.Write(fillLine);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        Fill(0, 0, Width, Height);
    }

    /// <inheritdoc />
    public IRenderContext CreateSubContext(Layout.Rect bounds)
    {
        // Clip bounds to our region
        var clippedX = Math.Max(0, bounds.X);
        var clippedY = Math.Max(0, bounds.Y);
        var clippedRight = Math.Min(Width, bounds.Right);
        var clippedBottom = Math.Min(Height, bounds.Bottom);

        var clippedWidth = Math.Max(0, clippedRight - clippedX);
        var clippedHeight = Math.Max(0, clippedBottom - clippedY);

        return new RegionRenderContext(
            _terminal,
            _offsetX + clippedX,
            _offsetY + clippedY,
            clippedWidth,
            clippedHeight);
    }

    Layout.ScreenBounds IScreenPositionedRenderContext.GetScreenBounds(Layout.Rect bounds)
    {
        var clippedX = Math.Max(0, bounds.X);
        var clippedY = Math.Max(0, bounds.Y);
        var clippedRight = Math.Min(Width, bounds.Right);
        var clippedBottom = Math.Min(Height, bounds.Bottom);

        if (clippedRight <= clippedX || clippedBottom <= clippedY)
            return Layout.ScreenBounds.Empty;

        return new Layout.ScreenBounds(
            _offsetX + clippedX,
            _offsetY + clippedY,
            clippedRight - clippedX,
            clippedBottom - clippedY);
    }
}
