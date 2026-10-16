// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Rendering;

/// <summary>
/// A simple text component that renders a string.
/// </summary>
public sealed class Text : IRenderable
{
    /// <summary>
    /// Create a text component with the specified content.
    /// </summary>
    /// <param name="content">The text content to display.</param>
    public Text(string content)
    {
        Content = content;
    }

    /// <summary>
    /// The text content to display.
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// Foreground color for the text.
    /// </summary>
    public Color Foreground { get; init; } = Color.Default;

    /// <summary>
    /// Background color for the text.
    /// </summary>
    public Color Background { get; init; } = Color.Default;

    /// <inheritdoc />
    public void Render(IRenderContext context)
    {
        if (string.IsNullOrEmpty(Content))
            return;

        context.SetForeground(Foreground);
        context.SetBackground(Background);

        var lines = Content.Split('\n');
        for (var y = 0; y < lines.Length && y < context.Height; y++)
        {
            var line = lines[y];
            // Truncate to fit display width
            if (DisplayWidth.GetColumnCount(line) > context.Width)
            {
                line = DisplayWidth.TruncateToColumns(line, context.Width);
            }
            context.WriteAt(0, y, line);
        }

        context.ResetColors();
    }

    /// <inheritdoc />
    public (int Width, int Height) Measure(int availableWidth, int availableHeight)
    {
        if (string.IsNullOrEmpty(Content))
            return (0, 0);

        var lines = Content.Split('\n');
        var maxWidth = 0;
        foreach (var line in lines)
        {
            var colCount = DisplayWidth.GetColumnCount(line);
            if (colCount > maxWidth)
                maxWidth = colCount;
        }

        return (Math.Min(maxWidth, availableWidth), Math.Min(lines.Length, availableHeight));
    }

    /// <summary>
    /// Create a text component with foreground color.
    /// </summary>
    public static Text Colored(string content, Color foreground)
        => new(content) { Foreground = foreground };

    /// <summary>
    /// Create a text component with foreground and background colors.
    /// </summary>
    public static Text Colored(string content, Color foreground, Color background)
        => new(content) { Foreground = foreground, Background = background };
}
