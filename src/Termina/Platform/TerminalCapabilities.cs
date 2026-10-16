// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Platform;

/// <summary>
/// Snapshot of transport-level input capabilities the platform console exposes after initialization.
/// </summary>
/// <param name="RawInputActive">
/// True when the console is delivering raw bytes to the parser rather than relying on
/// <see cref="Console.ReadKey(bool)"/> or other cooked input handling.
/// </param>
public readonly record struct TerminalCapabilities(bool RawInputActive)
{
    /// <summary>
    /// Gets whether the platform input path preserves key modifiers without a terminal protocol.
    /// </summary>
    public bool PreservesKeyModifiers { get; init; }
}
