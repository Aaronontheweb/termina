// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Layout;

/// <summary>
/// Interface for layout nodes that support programmatic scrolling.
/// </summary>
/// <remarks>
/// The scroll direction convention used here matches the reading direction:
/// <list type="bullet">
///   <item><see cref="ScrollUp"/> moves toward older/earlier content (upward in the buffer).</item>
///   <item><see cref="ScrollDown"/> moves toward newer/later content (downward toward the bottom).</item>
/// </list>
/// Implementations should use cached viewport dimensions when the viewport size is not
/// otherwise available (e.g., outside of a <c>Render</c> call).
/// </remarks>
public interface IScrollable
{
    /// <summary>
    /// Gets whether scrolling upward (toward older content) is possible.
    /// </summary>
    bool CanScrollUp { get; }

    /// <summary>
    /// Gets whether scrolling downward (toward newer content) is possible.
    /// </summary>
    bool CanScrollDown { get; }

    /// <summary>
    /// Scrolls upward toward older content by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll. Defaults to 1.</param>
    void ScrollUp(int lines = 1);

    /// <summary>
    /// Scrolls downward toward newer content by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll. Defaults to 1.</param>
    void ScrollDown(int lines = 1);
}

internal interface IPointerScrollable : IScrollable
{
    ScreenBounds LastRenderedBounds { get; }
}
