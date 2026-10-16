// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Input event fired when the user scrolls the mouse wheel.
/// </summary>
/// <remarks>
/// This event is emitted when mouse reporting is active
/// (<c>ESC[?1000h</c> combined with SGR mode <c>ESC[?1006h</c>).
/// The terminal reports scroll wheel events using SGR button codes 64 (up) and 65 (down).
/// </remarks>
/// <param name="Delta">
/// The scroll direction and magnitude.
/// Positive values indicate scrolling up (toward older/earlier content).
/// Negative values indicate scrolling down (toward newer/later content).
/// Each wheel tick typically has a magnitude of 1.
/// </param>
public sealed record MouseScrollEvent(int Delta) : IInputEvent
{
    /// <summary>
    /// Gets the zero-based screen column for the wheel event when the terminal reports it.
    /// </summary>
    public int? X { get; init; }

    /// <summary>
    /// Gets the zero-based screen row for the wheel event when the terminal reports it.
    /// </summary>
    public int? Y { get; init; }

    /// <summary>
    /// Gets the keyboard modifiers that accompanied the wheel event.
    /// </summary>
    public ConsoleModifiers Modifiers { get; init; }
}
