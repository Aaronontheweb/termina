// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Input;

/// <summary>
/// Describes whether the active input path preserves modifiers on the Enter key.
/// </summary>
public enum TerminalCapabilityAvailability
{
    /// <summary>
    /// The input path has not supplied a conclusive result.
    /// </summary>
    Unknown,

    /// <summary>
    /// The input path preserves modified Enter keys.
    /// </summary>
    Available,

    /// <summary>
    /// The input path cannot distinguish modified Enter keys from bare Enter.
    /// </summary>
    Unavailable,
}

/// <summary>
/// Identifies the source of a terminal input capability result.
/// </summary>
public enum TerminalInputCapabilitySource
{
    /// <summary>
    /// No input path has supplied a result.
    /// </summary>
    None,

    /// <summary>
    /// A native console input record preserves key modifiers.
    /// </summary>
    NativeConsole,

    /// <summary>
    /// The Kitty keyboard protocol reported its active flags.
    /// </summary>
    KittyKeyboardProtocol,

    /// <summary>
    /// The active terminal uses a legacy byte input path.
    /// </summary>
    LegacyTerminal,

    /// <summary>
    /// The application supplied its own input source.
    /// </summary>
    CustomInput,
}

/// <summary>
/// Describes capabilities of the active terminal input path.
/// </summary>
/// <param name="ModifiedEnterKeySupport">Whether the path preserves modifiers on Enter.</param>
/// <param name="Source">The source of the capability result.</param>
public readonly record struct TerminalInputCapabilities(
    TerminalCapabilityAvailability ModifiedEnterKeySupport,
    TerminalInputCapabilitySource Source);

/// <summary>
/// Reports a change to the active terminal input capabilities.
/// </summary>
/// <param name="Capabilities">The current capabilities.</param>
public sealed record TerminalInputCapabilitiesChanged(TerminalInputCapabilities Capabilities) : IInputEvent;

