// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Hosting;

/// <summary>
/// Runtime options that control how Termina negotiates terminal input behavior.
/// </summary>
public sealed class TerminaRuntimeOptions
{
    /// <summary>
    /// Selects how Termina owns the terminal display.
    /// </summary>
    public TerminalPresentationMode PresentationMode { get; set; } = TerminalPresentationMode.FullScreen;

    /// <summary>
    /// Prefer raw-byte input over the fallback <see cref="Console.ReadKey(bool)"/> pipeline when available.
    /// This is required for alternate-scroll wheel disambiguation and full kitty keyboard reporting.
    /// </summary>
    public bool PreferRawInput { get; set; }

    /// <summary>
    /// Selects which terminal scroll mode Termina enables during app startup.
    /// </summary>
    public ScrollInputMode ScrollInputMode { get; set; } = ScrollInputMode.LegacyMouseTracking;

    /// <summary>
    /// Selects which kitty keyboard enhancement flags Termina negotiates, if any.
    /// </summary>
    public KittyKeyboardMode KittyKeyboardMode { get; set; } = KittyKeyboardMode.DisambiguateOnly;

    /// <summary>
    /// Selects whether Termina intercepts Ctrl+C globally.
    /// </summary>
    public CtrlCHandlingMode CtrlCHandlingMode { get; set; } = CtrlCHandlingMode.DoublePressWhenRawInput;

    /// <summary>
    /// Time provider used by the render frame provider for paced follow-up frames.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Minimum delay between follow-up render frames when frame work remains active.
    /// </summary>
    public TimeSpan RenderFrameInterval { get; set; } = TimeSpan.FromMilliseconds(16);
}

/// <summary>
/// Configures how Termina receives scroll-wheel input.
/// </summary>
public enum ScrollInputMode
{
    /// <summary>
    /// Use SGR mouse tracking. This is the most compatible default but captures click-drag selection.
    /// </summary>
    LegacyMouseTracking = 0,

    /// <summary>
    /// Use xterm alternate-scroll mode. This preserves native terminal selection but requires raw input.
    /// </summary>
    AlternateScroll = 1,

    /// <summary>
    /// Leave scroll and selection input under native terminal control.
    /// </summary>
    NativeTerminal = 2,
}

/// <summary>
/// Configures how Termina owns the terminal display.
/// </summary>
public enum TerminalPresentationMode
{
    /// <summary>
    /// Use the alternate buffer and own the complete terminal viewport.
    /// </summary>
    FullScreen = 0,

    /// <summary>
    /// Use the primary buffer and own a bounded live region.
    /// </summary>
    Inline = 1,
}

/// <summary>
/// Kitty keyboard protocol flag presets.
/// </summary>
public enum KittyKeyboardMode
{
    /// <summary>
    /// Do not negotiate kitty keyboard enhancements.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Flag 1: disambiguate escape codes.
    /// </summary>
    DisambiguateOnly = 1,

    /// <summary>
    /// Flag 8: report all keys.
    /// </summary>
    ReportAllKeys = 8,

    /// <summary>
    /// Flags 8 | 1: report all keys and disambiguate escape codes.
    /// </summary>
    ReportAllKeysPlusDisambiguate = 9,

    /// <summary>
    /// Flags 16 | 8 | 1: report associated text and all keys, and disambiguate escape codes.
    /// </summary>
    ReportAllKeysWithAssociatedText = 25,

    /// <summary>
    /// Flags 8 | 2 | 1: report all keys, event types, and disambiguate escape codes.
    /// </summary>
    ReportAllKeysWithEventTypes = 11,
}

/// <summary>
/// Configures whether Termina intercepts Ctrl+C at the framework level.
/// </summary>
public enum CtrlCHandlingMode
{
    /// <summary>
    /// Never intercept Ctrl+C; let the platform and app own it.
    /// </summary>
    Disabled,

    /// <summary>
    /// Only require double-press Ctrl+C when raw input is active.
    /// </summary>
    DoublePressWhenRawInput,

    /// <summary>
    /// Require double-press Ctrl+C in all modes.
    /// </summary>
    DoublePressAlways,
}
