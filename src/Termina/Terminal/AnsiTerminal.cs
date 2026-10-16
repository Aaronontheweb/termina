// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using Termina.Diagnostics;

namespace Termina.Terminal;

/// <summary>
/// Real terminal implementation using ANSI escape sequences.
/// Writes to standard output.
/// </summary>
public sealed class AnsiTerminal : IAnsiTerminal, IInlineTerminalControl, IDisposable
{
    /// <summary>
    /// Explicit output writer for tests/benchmarks, or <c>null</c> to write to
    /// <see cref="Console.Out"/>. A cached <see cref="Console.Out"/> is deliberately
    /// never stored here — see <see cref="Output"/>.
    /// </summary>
    private readonly TextWriter? _explicitOutput;
    private readonly StringBuilder _buffer = new();
    private readonly bool _useAlternateScreen;
    private bool _inAlternateScreen;
    private bool _mouseEnabled;
    private bool _wheelScrollEnabled;
    private long _totalBytesWritten;
    private int _flushCount;

    /// <summary>
    /// Create an AnsiTerminal writing to standard output.
    /// </summary>
    /// <param name="useAlternateScreen">Whether to use alternate screen buffer on startup.</param>
    public AnsiTerminal(bool useAlternateScreen = true)
        : this(null, useAlternateScreen)
    {
    }

    /// <summary>
    /// Create an AnsiTerminal. Pass an explicit <paramref name="output"/> writer for
    /// tests/benchmarks, or <c>null</c> to write to <see cref="Console.Out"/>.
    /// </summary>
    internal AnsiTerminal(TextWriter? output, bool useAlternateScreen = true)
    {
        _explicitOutput = output;
        _useAlternateScreen = useAlternateScreen;

        TerminaTrace.Platform.Debug(this, "AnsiTerminal created: output={0}, useAlternateScreen={1}",
            output?.GetType().Name ?? "Console.Out", useAlternateScreen);

        // NOTE: UTF-8 output encoding is configured once by the platform console
        // (ConsoleEnvironment.EnsureUtf8Output). This terminal deliberately does not
        // set Console.OutputEncoding and never caches Console.Out, because that setter
        // replaces Console.Out with a new TextWriter — see issue #204.

        if (_useAlternateScreen)
        {
            EnterAlternateScreen();
        }

        TerminaTrace.Platform.Debug(this, "AnsiTerminal initialization complete");
    }

    /// <summary>
    /// The writer used for output. Resolved on every access — never cached — because
    /// setting <see cref="Console.OutputEncoding"/> replaces <see cref="Console.Out"/>
    /// with a new <see cref="TextWriter"/>. Caching it risks writing through a writer
    /// bound to a stale encoding, garbling non-ASCII output. See issue #204.
    /// </summary>
    private TextWriter Output => _explicitOutput ?? Console.Out;

    /// <inheritdoc />
    public int Width => GetConsoleWidth();

    /// <inheritdoc />
    public int Height => GetConsoleHeight();

    /// <summary>
    /// Gets the console width, with fallback for non-TTY environments.
    /// </summary>
    private static int GetConsoleWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            // No TTY available (e.g., CI environment, redirected output)
            return 80;
        }
    }

    /// <summary>
    /// Gets the console height, with fallback for non-TTY environments.
    /// </summary>
    private static int GetConsoleHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch (IOException)
        {
            // No TTY available (e.g., CI environment, redirected output)
            return 24;
        }
    }

    /// <inheritdoc />
    public void MoveTo(int x, int y)
    {
        _buffer.Append(AnsiCodes.MoveTo(y, x));
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        _buffer.Append(text);
    }

    /// <inheritdoc />
    public void Write(char c)
    {
        _buffer.Append(c);
    }

    /// <inheritdoc />
    public void SetForeground(Color color)
    {
        _buffer.Append(color.ToForegroundAnsi());
    }

    /// <inheritdoc />
    public void SetBackground(Color color)
    {
        _buffer.Append(color.ToBackgroundAnsi());
    }

    /// <inheritdoc />
    public void ResetColors()
    {
        _buffer.Append(AnsiCodes.Reset);
    }

    /// <inheritdoc />
    public void SaveCursor()
    {
        _buffer.Append(AnsiCodes.SaveCursor);
    }

    /// <inheritdoc />
    public void RestoreCursor()
    {
        _buffer.Append(AnsiCodes.RestoreCursor);
    }

    /// <inheritdoc />
    public void MoveCursorUp(int rows)
    {
        if (rows > 0)
            _buffer.Append(AnsiCodes.MoveUp(rows));
    }

    /// <inheritdoc />
    public void MoveCursorDown(int rows)
    {
        if (rows > 0)
            _buffer.Append(AnsiCodes.MoveDown(rows));
    }

    /// <inheritdoc />
    public void MoveCursorToLineStart()
    {
        _buffer.Append('\r');
    }

    /// <inheritdoc />
    public void EraseLine()
    {
        _buffer.Append(AnsiCodes.ClearLine);
    }

    /// <inheritdoc />
    public void WriteLineBreak()
    {
        _buffer.Append("\r\n");
    }

    /// <inheritdoc />
    public void SetCursorVisible(bool visible)
    {
        _buffer.Append(visible ? AnsiCodes.ShowCursor : AnsiCodes.HideCursor);
    }

    /// <inheritdoc />
    public void ClearRegion(int x, int y, int width, int height)
    {
        var spaces = new string(' ', width);
        for (var row = 0; row < height; row++)
        {
            MoveTo(x, y + row);
            Write(spaces);
        }
    }

    /// <inheritdoc />
    public void ClearScreen()
    {
        _buffer.Append(AnsiCodes.ClearScreen);
        MoveTo(0, 0);
    }

    /// <inheritdoc />
    public void Flush()
    {
        if (_buffer.Length > 0)
        {
            var content = _buffer.ToString();
            var byteCount = Encoding.UTF8.GetByteCount(content);

            _flushCount++;
            _totalBytesWritten += byteCount;

            // Log flush details - truncate content preview for readability
            var preview = content.Length > 100
                ? content.Substring(0, 100).Replace("\x1b", "\\e") + "..."
                : content.Replace("\x1b", "\\e");

            TerminaTrace.Render.Debug(this, "Flush #{0}: {1} chars, {2} bytes",
                _flushCount, content.Length, byteCount);
            TerminaTrace.Render.Debug(this, "Content preview: {0}", preview);

            // Resolve Console.Out fresh on every flush — never cache it (see Output).
            var output = Output;
            output.Write(content);
            output.Flush();
            _buffer.Clear();

            TerminaTrace.Render.Debug(this, "Flush #{0} complete", _flushCount);
        }
    }

    /// <inheritdoc />
    public void EnterAlternateScreen()
    {
        if (!_inAlternateScreen)
        {
            TerminaTrace.Platform.Debug(this, "Entering alternate screen buffer");
            _buffer.Append(AnsiCodes.EnterAlternateScreen);
            _inAlternateScreen = true;
        }
    }

    /// <inheritdoc />
    public void ExitAlternateScreen()
    {
        if (_inAlternateScreen)
        {
            TerminaTrace.Platform.Debug(this, "Exiting alternate screen buffer");
            _buffer.Append(AnsiCodes.ExitAlternateScreen);
            _inAlternateScreen = false;
        }
    }

    /// <inheritdoc />
    public void EnableMouse()
    {
        if (!_mouseEnabled)
        {
            _buffer.Append(AnsiCodes.EnableMouseNormal);
            _buffer.Append(AnsiCodes.EnableMouseSgr);
            _mouseEnabled = true;
        }
    }

    /// <inheritdoc />
    public void DisableMouse()
    {
        if (_mouseEnabled)
        {
            _buffer.Append(AnsiCodes.DisableMouseSgr);
            _buffer.Append(AnsiCodes.DisableMouseNormal);
            _mouseEnabled = false;
        }
    }

    /// <inheritdoc />
    public void EnableWheelScroll()
    {
        if (!_wheelScrollEnabled)
        {
            _buffer.Append(AnsiCodes.EnableAlternateScroll);
            // DECCKM: keyboard arrows -> SS3 (ESC O A/B), wheel still sends CSI (ESC [ A/B).
            // The EscapeSequenceParser uses this distinction to emit MouseScrollEvent for wheel
            // ticks while leaving real keyboard arrows as plain KeyPressed(UpArrow/DownArrow).
            _buffer.Append(AnsiCodes.EnableCursorKeyApplicationMode);
            _wheelScrollEnabled = true;
        }
    }

    /// <inheritdoc />
    public void DisableWheelScroll()
    {
        if (_wheelScrollEnabled)
        {
            _buffer.Append(AnsiCodes.DisableCursorKeyApplicationMode);
            _buffer.Append(AnsiCodes.DisableAlternateScroll);
            _wheelScrollEnabled = false;
        }
    }

    /// <inheritdoc />
    public void CopyToClipboard(string text)
    {
        var belSequence = AnsiCodes.Osc52Clipboard(text);
        var stSequence = AnsiCodes.Osc52Clipboard(text, useStringTerminator: true);
        TerminaTrace.Platform.Info(this, "AnsiTerminal.CopyToClipboard: textLength={0}, tmux={1}", text.Length, Environment.GetEnvironmentVariable("TMUX") is not null);
        TerminaTrace.Platform.Debug(this, "OSC52 lengths: bel={0}, st={1}", belSequence.Length, stSequence.Length);
        if (Environment.GetEnvironmentVariable("TMUX") is not null)
        {
            // Emit both OSC terminators and both tmux delivery modes to maximize
            // compatibility across tmux versions and terminal emulators.
            _buffer.Append(belSequence);
            _buffer.Append(stSequence);
            _buffer.Append(AnsiCodes.TmuxPassthrough(belSequence));
            _buffer.Append(AnsiCodes.TmuxPassthrough(stSequence));
            TerminaTrace.Platform.Debug(this, "Queued OSC52 BEL/ST plain + tmux passthrough sequences");
            return;
        }

        _buffer.Append(belSequence);
        _buffer.Append(stSequence);
        TerminaTrace.Platform.Debug(this, "Queued OSC52 BEL/ST plain sequences");
    }

    /// <summary>
    /// Dispose the terminal, restoring original state.
    /// </summary>
    public void Dispose()
    {
        if (_mouseEnabled)
        {
            DisableMouse();
        }

        if (_wheelScrollEnabled)
        {
            DisableWheelScroll();
        }

        if (_inAlternateScreen)
        {
            ExitAlternateScreen();
        }

        SetCursorVisible(true);
        ResetColors();
        Flush();
    }
}
