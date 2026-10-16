// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;

namespace Termina.Terminal;

/// <summary>
/// ANSI escape code constants for terminal control.
/// </summary>
public static class AnsiCodes
{
    /// <summary>
    /// Escape character that begins all ANSI sequences.
    /// </summary>
    public const char Escape = '\x1b';

    /// <summary>
    /// Control Sequence Introducer - ESC [
    /// </summary>
    public const string Csi = "\x1b[";

    // Cursor positioning

    /// <summary>
    /// Move cursor to position. Format: CSI {row};{col}H
    /// </summary>
    public static string MoveTo(int row, int col) => $"{Csi}{row + 1};{col + 1}H";

    /// <summary>
    /// Move cursor up n rows. Format: CSI {n}A
    /// </summary>
    public static string MoveUp(int n = 1) => $"{Csi}{n}A";

    /// <summary>
    /// Move cursor down n rows. Format: CSI {n}B
    /// </summary>
    public static string MoveDown(int n = 1) => $"{Csi}{n}B";

    /// <summary>
    /// Move cursor right n columns. Format: CSI {n}C
    /// </summary>
    public static string MoveRight(int n = 1) => $"{Csi}{n}C";

    /// <summary>
    /// Move cursor left n columns. Format: CSI {n}D
    /// </summary>
    public static string MoveLeft(int n = 1) => $"{Csi}{n}D";

    /// <summary>
    /// Save cursor position. Format: CSI s
    /// </summary>
    public const string SaveCursor = $"{Csi}s";

    /// <summary>
    /// Restore cursor position. Format: CSI u
    /// </summary>
    public const string RestoreCursor = $"{Csi}u";

    /// <summary>
    /// Show cursor. Format: CSI ?25h
    /// </summary>
    public const string ShowCursor = $"{Csi}?25h";

    /// <summary>
    /// Hide cursor. Format: CSI ?25l
    /// </summary>
    public const string HideCursor = $"{Csi}?25l";

    // Screen clearing

    /// <summary>
    /// Clear entire screen. Format: CSI 2J
    /// </summary>
    public const string ClearScreen = $"{Csi}2J";

    /// <summary>
    /// Clear from cursor to end of screen. Format: CSI 0J
    /// </summary>
    public const string ClearToEnd = $"{Csi}0J";

    /// <summary>
    /// Clear from cursor to beginning of screen. Format: CSI 1J
    /// </summary>
    public const string ClearToBeginning = $"{Csi}1J";

    /// <summary>
    /// Clear entire line. Format: CSI 2K
    /// </summary>
    public const string ClearLine = $"{Csi}2K";

    /// <summary>
    /// Clear from cursor to end of line. Format: CSI 0K
    /// </summary>
    public const string ClearLineToEnd = $"{Csi}0K";

    /// <summary>
    /// Clear from cursor to beginning of line. Format: CSI 1K
    /// </summary>
    public const string ClearLineToBeginning = $"{Csi}1K";

    // Colors - 256 color mode

    /// <summary>
    /// Set foreground color (256 color). Format: CSI 38;5;{n}m
    /// </summary>
    public static string Foreground256(byte color) => $"{Csi}38;5;{color}m";

    /// <summary>
    /// Set background color (256 color). Format: CSI 48;5;{n}m
    /// </summary>
    public static string Background256(byte color) => $"{Csi}48;5;{color}m";

    // Colors - True color (24-bit)

    /// <summary>
    /// Set foreground color (RGB). Format: CSI 38;2;{r};{g};{b}m
    /// </summary>
    public static string ForegroundRgb(byte r, byte g, byte b) => $"{Csi}38;2;{r};{g};{b}m";

    /// <summary>
    /// Set background color (RGB). Format: CSI 48;2;{r};{g};{b}m
    /// </summary>
    public static string BackgroundRgb(byte r, byte g, byte b) => $"{Csi}48;2;{r};{g};{b}m";

    // Standard colors (basic 16)

    /// <summary>
    /// Set foreground to default. Format: CSI 39m
    /// </summary>
    public const string ForegroundDefault = $"{Csi}39m";

    /// <summary>
    /// Set background to default. Format: CSI 49m
    /// </summary>
    public const string BackgroundDefault = $"{Csi}49m";

    /// <summary>
    /// Reset all attributes. Format: CSI 0m
    /// </summary>
    public const string Reset = $"{Csi}0m";

    // Text attributes

    /// <summary>
    /// Bold text. Format: CSI 1m
    /// </summary>
    public const string Bold = $"{Csi}1m";

    /// <summary>
    /// Dim text. Format: CSI 2m
    /// </summary>
    public const string Dim = $"{Csi}2m";

    /// <summary>
    /// Italic text. Format: CSI 3m
    /// </summary>
    public const string Italic = $"{Csi}3m";

    /// <summary>
    /// Underline text. Format: CSI 4m
    /// </summary>
    public const string Underline = $"{Csi}4m";

    /// <summary>
    /// Blink text. Format: CSI 5m
    /// </summary>
    public const string Blink = $"{Csi}5m";

    /// <summary>
    /// Reverse video (swap fg/bg). Format: CSI 7m
    /// </summary>
    public const string Reverse = $"{Csi}7m";

    /// <summary>
    /// Strikethrough text. Format: CSI 9m
    /// </summary>
    public const string Strikethrough = $"{Csi}9m";

    // Reset individual attributes

    /// <summary>
    /// Reset bold. Format: CSI 22m
    /// </summary>
    public const string ResetBold = $"{Csi}22m";

    /// <summary>
    /// Reset italic. Format: CSI 23m
    /// </summary>
    public const string ResetItalic = $"{Csi}23m";

    /// <summary>
    /// Reset underline. Format: CSI 24m
    /// </summary>
    public const string ResetUnderline = $"{Csi}24m";

    /// <summary>
    /// Reset blink. Format: CSI 25m
    /// </summary>
    public const string ResetBlink = $"{Csi}25m";

    /// <summary>
    /// Reset reverse. Format: CSI 27m
    /// </summary>
    public const string ResetReverse = $"{Csi}27m";

    /// <summary>
    /// Reset strikethrough. Format: CSI 29m
    /// </summary>
    public const string ResetStrikethrough = $"{Csi}29m";

    // Bracketed paste mode

    /// <summary>
    /// Enable bracketed paste mode. Format: CSI ?2004h
    /// When enabled, the terminal wraps pasted text with <c>ESC[200~</c> ... <c>ESC[201~</c>,
    /// allowing the application to distinguish pasted content from typed input.
    /// </summary>
    public const string EnableBracketedPaste = $"{Csi}?2004h";

    /// <summary>
    /// Disable bracketed paste mode. Format: CSI ?2004l
    /// Should be sent before exiting the application to restore normal paste behavior.
    /// </summary>
    public const string DisableBracketedPaste = $"{Csi}?2004l";

    // Kitty keyboard protocol (progressive enhancement)
    // Standard terminals cannot distinguish Ctrl+Enter from Enter (both send 0x0D).
    // The kitty keyboard protocol solves this by sending structured CSI u escape sequences.
    // Spec: https://sw.kovidgoyal.net/kitty/keyboard-protocol/
    // Background: https://blog.fsck.com/releases/2026/02/26/terminal-keyboard-protocol/
    // Supported by: kitty, WezTerm, Ghostty, Alacritty, foot, and others.

    /// <summary>
    /// Build a kitty keyboard protocol flag-push sequence.
    /// Format: CSI &gt; flags u
    /// </summary>
    public static string EnableKittyKeyboard(int flags) => $"{Csi}>{flags}u";

    /// <summary>
    /// Query the active kitty keyboard protocol flags. Format: CSI ? u
    /// </summary>
    internal const string QueryKittyKeyboard = $"{Csi}?u";

    /// <summary>
    /// Request primary device attributes. Format: CSI c
    /// </summary>
    internal const string RequestPrimaryDeviceAttributes = $"{Csi}c";

    /// <summary>
    /// Pop the kitty keyboard protocol flag stack, restoring the terminal's
    /// previous keyboard mode. Format: CSI &lt; u
    /// </summary>
    public const string DisableKittyKeyboard = $"{Csi}<u";

    /// <summary>
    /// Wraps an ANSI escape sequence in a tmux DCS passthrough, allowing it to reach the
    /// outer terminal when the application runs inside a tmux session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// tmux intercepts most ANSI sequences from inner applications and handles them itself.
    /// To send a sequence to the <em>outer</em> terminal (e.g., to enable bracketed paste at
    /// the outer level so Ctrl+Shift+V pastes are wrapped with <c>ESC[200~</c>), wrap the
    /// sequence with this passthrough.
    /// </para>
    /// <para>
    /// Requires <c>set -g allow-passthrough on</c> in <c>~/.tmux.conf</c> (tmux 3.3+).
    /// Without that setting, tmux silently drops the DCS sequence.
    /// </para>
    /// </remarks>
    /// <param name="seq">The escape sequence to forward. All <c>ESC</c> bytes are automatically doubled as required by the DCS passthrough format.</param>
    /// <returns>The wrapped DCS passthrough sequence ready to write to stdout.</returns>
    public static string TmuxPassthrough(string seq)
    {
        // DCS passthrough syntax: ESC P tmux; ESC <seq-with-doubled-ESCs> ESC \
        var doubled = seq.Replace("\x1b", "\x1b\x1b");
        return $"\x1bPtmux;\x1b{doubled}\x1b\\";
    }

    /// <summary>
    /// Build an OSC 52 clipboard write sequence for the provided text.
    /// Format: ESC ] 52 ; c ; {base64(utf8 text)} BEL
    /// </summary>
    public static string Osc52Clipboard(string text)
    {
        return Osc52Clipboard(text, useStringTerminator: false);
    }

    /// <summary>
    /// Build an OSC 52 clipboard write sequence for the provided text.
    /// Format: ESC ] 52 ; c ; {base64(utf8 text)} (BEL or ST)
    /// </summary>
    public static string Osc52Clipboard(string text, bool useStringTerminator)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        return useStringTerminator
            ? $"\x1b]52;c;{base64}\x1b\\"
            : $"\x1b]52;c;{base64}\x07";
    }

    // Mouse tracking

    /// <summary>
    /// Enable mouse tracking (X10 mode). Format: CSI ?9h
    /// </summary>
    public const string EnableMouseX10 = $"{Csi}?9h";

    /// <summary>
    /// Disable mouse tracking (X10 mode). Format: CSI ?9l
    /// </summary>
    public const string DisableMouseX10 = $"{Csi}?9l";

    /// <summary>
    /// Enable mouse tracking (normal mode - button events). Format: CSI ?1000h
    /// </summary>
    public const string EnableMouseNormal = $"{Csi}?1000h";

    /// <summary>
    /// Disable mouse tracking (normal mode). Format: CSI ?1000l
    /// </summary>
    public const string DisableMouseNormal = $"{Csi}?1000l";

    /// <summary>
    /// Enable mouse tracking (SGR extended mode for large terminals). Format: CSI ?1006h
    /// </summary>
    public const string EnableMouseSgr = $"{Csi}?1006h";

    /// <summary>
    /// Disable mouse tracking (SGR extended mode). Format: CSI ?1006l
    /// </summary>
    public const string DisableMouseSgr = $"{Csi}?1006l";

    /// <summary>
    /// Enable alternate scroll mode (xterm 1007). While the alternate screen buffer is active,
    /// the terminal translates mouse wheel events into cursor up/down key sequences instead of
    /// raw mouse events. This lets applications get wheel-driven scrolling without enabling any
    /// mouse tracking mode — preserving the terminal's native click-drag text selection,
    /// triple-click word selection, middle-click paste, and OS clipboard integration.
    /// Supported by xterm (≥277), iTerm2, kitty, WezTerm, Alacritty, Windows Terminal, and
    /// VTE-based terminals (GNOME Terminal ≥0.40). Legacy <c>conhost.exe</c> and a few embedded
    /// terminals ignore this mode; in that case the wheel produces no events and PgUp/PgDn
    /// remains the keyboard fallback. Format: CSI ?1007h
    /// </summary>
    public const string EnableAlternateScroll = $"{Csi}?1007h";

    /// <summary>
    /// Disable alternate scroll mode. Format: CSI ?1007l
    /// </summary>
    public const string DisableAlternateScroll = $"{Csi}?1007l";

    /// <summary>
    /// Enable DECCKM (cursor key application mode). Real keyboard arrow keys then send
    /// <c>ESC O A/B/C/D</c> (SS3) instead of <c>ESC [ A/B/C/D</c> (CSI). When paired with
    /// <see cref="EnableAlternateScroll"/>, this lets the input parser distinguish wheel-as-arrow
    /// events (still CSI form) from real keyboard arrows (SS3 form). Format: CSI ?1h
    /// </summary>
    public const string EnableCursorKeyApplicationMode = $"{Csi}?1h";

    /// <summary>
    /// Disable DECCKM (cursor keys return to normal CSI form). Format: CSI ?1l
    /// </summary>
    public const string DisableCursorKeyApplicationMode = $"{Csi}?1l";

    // Alternate screen buffer

    /// <summary>
    /// Switch to alternate screen buffer. Format: CSI ?1049h
    /// </summary>
    public const string EnterAlternateScreen = $"{Csi}?1049h";

    /// <summary>
    /// Switch back to main screen buffer. Format: CSI ?1049l
    /// </summary>
    public const string ExitAlternateScreen = $"{Csi}?1049l";

    // Terminal queries

    /// <summary>
    /// Query cursor position. Terminal responds with CSI {row};{col}R
    /// </summary>
    public const string QueryCursorPosition = $"{Csi}6n";

    /// <summary>
    /// Query terminal size. Format: CSI 18t (response: CSI 8;{height};{width}t)
    /// </summary>
    public const string QueryTerminalSize = $"{Csi}18t";
}
