// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Rendering;

/// <summary>
/// Context provided to components during rendering.
/// All coordinates are relative to the region's top-left corner (0,0).
/// The context translates these to actual screen coordinates.
/// </summary>
public interface IRenderContext
{
    /// <summary>
    /// The width available for rendering.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// The height available for rendering.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Write text at the specified position (relative to region).
    /// </summary>
    /// <param name="x">X position (column) relative to region.</param>
    /// <param name="y">Y position (row) relative to region.</param>
    /// <param name="text">The text to write.</param>
    void WriteAt(int x, int y, string text);

    /// <summary>
    /// Write a single character at the specified position (relative to region).
    /// </summary>
    /// <param name="x">X position (column) relative to region.</param>
    /// <param name="y">Y position (row) relative to region.</param>
    /// <param name="c">The character to write.</param>
    void WriteAt(int x, int y, char c);

    /// <summary>
    /// Set the foreground color for subsequent writes.
    /// </summary>
    /// <param name="color">The foreground color.</param>
    void SetForeground(Color color);

    /// <summary>
    /// Set the background color for subsequent writes.
    /// </summary>
    /// <param name="color">The background color.</param>
    void SetBackground(Color color);

    /// <summary>
    /// Reset colors to terminal defaults.
    /// </summary>
    void ResetColors();

    /// <summary>
    /// Set text decorations (bold, italic, underline, etc.) for subsequent writes.
    /// </summary>
    /// <param name="decoration">The decorations to apply.</param>
    void SetDecoration(TextDecoration decoration);

    /// <summary>
    /// Apply a complete text style (foreground, background, decorations) for subsequent writes.
    /// </summary>
    /// <param name="style">The style to apply.</param>
    void ApplyStyle(TextStyle style);

    /// <summary>
    /// Fill a rectangular area with a character.
    /// </summary>
    /// <param name="x">Starting X position.</param>
    /// <param name="y">Starting Y position.</param>
    /// <param name="width">Width of the area.</param>
    /// <param name="height">Height of the area.</param>
    /// <param name="c">Character to fill with (default is space).</param>
    void Fill(int x, int y, int width, int height, char c = ' ');

    /// <summary>
    /// Clear the entire region (fill with spaces).
    /// </summary>
    void Clear();

    /// <summary>
    /// Create a sub-context with an offset and clipped bounds.
    /// The sub-context's (0,0) is at the specified offset in the parent context.
    /// </summary>
    /// <param name="bounds">The bounds for the sub-context relative to this context.</param>
    /// <returns>A new render context clipped to the specified bounds.</returns>
    IRenderContext CreateSubContext(Layout.Rect bounds);
}

internal interface IScreenPositionedRenderContext
{
    Layout.ScreenBounds GetScreenBounds(Layout.Rect bounds);
}
