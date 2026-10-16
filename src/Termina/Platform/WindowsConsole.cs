// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Channels;
using R3;
using Termina.Diagnostics;

namespace Termina.Platform;

/// <summary>
/// Windows-specific console implementation using native Console APIs via P/Invoke.
/// </summary>
/// <remarks>
/// <para>
/// Two input strategies, chosen at construction time:
/// </para>
/// <list type="bullet">
/// <item><description><b>Record mode (default)</b> — reads INPUT_RECORDs via <see cref="Console.ReadKey(bool)"/>.
/// Keeps the Windows-native key/resize event shape; mouse-wheel and VT-protocol input are dropped or
/// folded before the input pipeline sees them.</description></item>
/// <item><description><b>Raw-VT mode (opt-in via the <c>TERMINA_RAW_INPUT</c> env var)</b> — sets
/// <c>ENABLE_VIRTUAL_TERMINAL_INPUT</c>, clears <c>ENABLE_PROCESSED_INPUT</c>, switches the input
/// code page to UTF-8, and reads raw bytes from the input handle via <c>ReadFile</c>. Bytes flow
/// through <see cref="Input.EscapeSequenceParser"/> exactly the way they do on Unix, which lets the
/// xterm alternate-scroll (<c>?1007h</c>) wheel path and the kitty keyboard protocol both work on
/// Windows Terminal.</description></item>
/// </list>
/// <para>
/// See: https://docs.microsoft.com/en-us/windows/console/console-functions
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsConsole : IPlatformConsole
{
    #region P/Invoke Constants

    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;

    // Console input mode flags
    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const uint ENABLE_LINE_INPUT = 0x0002;
    private const uint ENABLE_ECHO_INPUT = 0x0004;
    private const uint ENABLE_WINDOW_INPUT = 0x0008;
    private const uint ENABLE_MOUSE_INPUT = 0x0010;
    private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

    // Console output mode flags
    private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
    private const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

    // Input record event types
    private const ushort KEY_EVENT = 0x0001;
    private const ushort MOUSE_EVENT = 0x0002;
    private const ushort WINDOW_BUFFER_SIZE_EVENT = 0x0004;

    // Wait constants
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_TIMEOUT = 0x00000102;
    private const uint WAIT_FAILED = 0xFFFFFFFF;
    private const uint INFINITE = 0xFFFFFFFF;

    // UTF-8 code page (for SetConsoleCP under raw-VT mode)
    private const uint CP_UTF8 = 65001;

    #endregion

    #region P/Invoke Structs

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SMALL_RECT
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD dwSize;
        public COORD dwCursorPosition;
        public ushort wAttributes;
        public SMALL_RECT srWindow;
        public COORD dwMaximumWindowSize;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
        [FieldOffset(4)] public MOUSE_EVENT_RECORD MouseEvent;
        [FieldOffset(4)] public WINDOW_BUFFER_SIZE_RECORD WindowBufferSizeEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEY_EVENT_RECORD
    {
        public int bKeyDown;
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_EVENT_RECORD
    {
        public COORD dwMousePosition;
        public uint dwButtonState;
        public uint dwControlKeyState;
        public uint dwEventFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOW_BUFFER_SIZE_RECORD
    {
        public COORD dwSize;
    }

    #endregion

    #region P/Invoke Methods

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleScreenBufferInfo(
        IntPtr hConsoleOutput,
        out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ReadConsoleInputW(
        IntPtr hConsoleInput,
        [Out] INPUT_RECORD[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr hFile,
        [Out] byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumberOfConsoleInputEvents(
        IntPtr hConsoleInput,
        out uint lpcNumberOfEvents);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCP(uint wCodePageID);

    #endregion

    #region Control Key State Flags

    private const uint RIGHT_ALT_PRESSED = 0x0001;
    private const uint LEFT_ALT_PRESSED = 0x0002;
    private const uint RIGHT_CTRL_PRESSED = 0x0004;
    private const uint LEFT_CTRL_PRESSED = 0x0008;
    private const uint SHIFT_PRESSED = 0x0010;

    #endregion

    private readonly bool _rawVtMode;
    private readonly Subject<ConsoleResizeEvent> _resized = new();
    private readonly Channel<IConsoleInputEvent> _rawEvents =
        Channel.CreateUnbounded<IConsoleInputEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    private IntPtr _inputHandle;
    private IntPtr _outputHandle;
    private uint _originalInputMode;
    private uint _originalOutputMode;
    private uint _originalInputCp;
    private bool _restoredInputCp = true;
    private bool _initialized;
    private bool _disposed;
    private int _lastWidth;
    private int _lastHeight;

    private CancellationTokenSource? _stopCts;
    private Thread? _readerThread;
    /// <summary>
    /// Create a Windows console. <paramref name="rawVtMode"/> opts into the raw-byte input path
    /// that matches the Unix pipeline (required for <c>?1007h</c> wheel events and the kitty
    /// keyboard protocol to reach <see cref="Input.EscapeSequenceParser"/>).
    /// </summary>
    public WindowsConsole(bool rawVtMode = false)
    {
        _rawVtMode = rawVtMode;
    }

    /// <summary>
    /// Check if a Windows console is available (not redirected/piped).
    /// </summary>
    /// <returns>True if a real console is available.</returns>
    public static bool IsConsoleAvailable()
    {
        var inputHandle = GetStdHandle(STD_INPUT_HANDLE);
        if (inputHandle == IntPtr.Zero || inputHandle == new IntPtr(-1))
            return false;

        // Try to get console mode - this fails if handle isn't a real console
        return GetConsoleMode(inputHandle, out _);
    }

    /// <inheritdoc />
    public bool SupportsEventDrivenInput => true;

    /// <inheritdoc />
    public Observable<ConsoleResizeEvent> Resized => _resized;

    /// <inheritdoc />
    public TerminalCapabilities Capabilities => new(RawInputActive: _rawVtMode)
    {
        PreservesKeyModifiers = !_rawVtMode,
    };

    /// <inheritdoc />
    public void Initialize()
    {
        if (_initialized) return;

        TerminaTrace.Platform.Debug(this, "WindowsConsole.Initialize() starting (rawVtMode={0})", _rawVtMode);

        // Get console handles
        _inputHandle = GetStdHandle(STD_INPUT_HANDLE);
        _outputHandle = GetStdHandle(STD_OUTPUT_HANDLE);

        TerminaTrace.Platform.Debug(this, "Console handles: input=0x{0:X}, output=0x{1:X}",
            _inputHandle.ToInt64(), _outputHandle.ToInt64());

        if (_inputHandle == IntPtr.Zero || _inputHandle == new IntPtr(-1))
        {
            TerminaTrace.Platform.Error(this, "Invalid input handle: 0x{0:X}", _inputHandle.ToInt64());
            throw new InvalidOperationException("Failed to get console input handle");
        }

        if (_outputHandle == IntPtr.Zero || _outputHandle == new IntPtr(-1))
        {
            TerminaTrace.Platform.Error(this, "Invalid output handle: 0x{0:X}", _outputHandle.ToInt64());
            throw new InvalidOperationException("Failed to get console output handle");
        }

        // Save original modes for restoration
        if (!GetConsoleMode(_inputHandle, out _originalInputMode))
        {
            var error = Marshal.GetLastWin32Error();
            TerminaTrace.Platform.Error(this, "GetConsoleMode(input) failed: error={0}", error);
            throw new InvalidOperationException($"Failed to get console input mode: {error}");
        }

        if (!GetConsoleMode(_outputHandle, out _originalOutputMode))
        {
            var error = Marshal.GetLastWin32Error();
            TerminaTrace.Platform.Error(this, "GetConsoleMode(output) failed: error={0}", error);
            throw new InvalidOperationException($"Failed to get console output mode: {error}");
        }

        TerminaTrace.Platform.Debug(this, "Original modes: input=0x{0:X4}, output=0x{1:X4}",
            _originalInputMode, _originalOutputMode);

        // Log which flags are currently set on output
        LogOutputModeFlags("Original output flags", _originalOutputMode);

        // Configure input mode. Common bits: drop line/echo, keep window events.
        var newInputMode = (_originalInputMode | ENABLE_WINDOW_INPUT)
                          & ~ENABLE_LINE_INPUT
                          & ~ENABLE_ECHO_INPUT;

        if (_rawVtMode)
        {
            // Raw-VT mode mirrors Unix cfmakeraw:
            //   - ENABLE_VIRTUAL_TERMINAL_INPUT routes keys/mouse/focus through the VT byte
            //     stream we read via ReadFile. Required for ?1007h wheel events and kitty
            //     CSI-u sequences to actually reach EscapeSequenceParser.
            //   - Clear ENABLE_PROCESSED_INPUT so Ctrl+C arrives in-band as \x03 (or the kitty
            //     CSI-u equivalent under report_all_keys), matching how cfmakeraw clears ISIG
            //     on Unix. TerminaApplication's double-Ctrl+C handler then sees the same
            //     KeyPressed shape across platforms.
            newInputMode = (newInputMode | ENABLE_VIRTUAL_TERMINAL_INPUT) & ~ENABLE_PROCESSED_INPUT;
        }
        else
        {
            // Record mode: keep ENABLE_PROCESSED_INPUT so .NET's Console.ReadKey can still
            // intercept Ctrl+C via the cancel-key event. Do NOT set ENABLE_VIRTUAL_TERMINAL_INPUT
            // — it would mangle the INPUT_RECORD stream Console.ReadKey expects.
            // (Bit already cleared by mask above; spelled out for symmetry/readability.)
            newInputMode &= ~ENABLE_VIRTUAL_TERMINAL_INPUT;
        }

        TerminaTrace.Platform.Debug(this, "Setting input mode: 0x{0:X4} -> 0x{1:X4}",
            _originalInputMode, newInputMode);

        if (!SetConsoleMode(_inputHandle, newInputMode))
        {
            var error = Marshal.GetLastWin32Error();
            TerminaTrace.Platform.Error(this, "SetConsoleMode(input) failed: error={0}", error);
            throw new InvalidOperationException($"Failed to set console input mode: {error}");
        }

        // Verify input mode was set correctly
        if (GetConsoleMode(_inputHandle, out var verifyInputMode))
        {
            TerminaTrace.Platform.Debug(this, "Verified input mode: 0x{0:X4} (expected: 0x{1:X4})",
                verifyInputMode, newInputMode);
        }

        // Configure output mode:
        // - Enable virtual terminal processing (VT100/ANSI sequences)
        var newOutputMode = _originalOutputMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING;

        TerminaTrace.Platform.Debug(this, "Setting output mode: 0x{0:X4} -> 0x{1:X4}",
            _originalOutputMode, newOutputMode);
        LogOutputModeFlags("New output flags", newOutputMode);

        if (!SetConsoleMode(_outputHandle, newOutputMode))
        {
            var error = Marshal.GetLastWin32Error();
            // VT100 might not be supported on older Windows versions
            // Log error but continue - some terminals handle VT natively
            TerminaTrace.Platform.Warning(this,
                "SetConsoleMode(output) failed for VT100: error={0} - ANSI sequences may not render", error);
            System.Diagnostics.Debug.WriteLine(
                $"Failed to enable VT100 processing: {error}");
        }
        else
        {
            TerminaTrace.Platform.Info(this, "VT100 processing enabled successfully");
        }

        // Verify output mode was set correctly
        if (GetConsoleMode(_outputHandle, out var verifyOutputMode))
        {
            TerminaTrace.Platform.Debug(this, "Verified output mode: 0x{0:X4} (expected: 0x{1:X4})",
                verifyOutputMode, newOutputMode);
            LogOutputModeFlags("Verified output flags", verifyOutputMode);

            // Critical check: is VT processing actually enabled?
            if ((verifyOutputMode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) == 0)
            {
                TerminaTrace.Platform.Error(this,
                    "CRITICAL: VT100 processing NOT enabled after SetConsoleMode succeeded!");
            }
        }

        // Ensure UTF-8 output encoding (single owner: ConsoleEnvironment)
        ConsoleEnvironment.EnsureUtf8Output();
        TerminaTrace.Platform.Debug(this, "Set Console.OutputEncoding to UTF-8");

        if (_rawVtMode)
        {
            // The console input handle has its own code page for the cooked byte stream we read
            // through ReadFile. If it stays on a SBCS / OEM page, multi-byte UTF-8 from kitty
            // associated-text or pasted Unicode will be mangled before our Decoder sees it.
            _originalInputCp = GetConsoleCP();
            if (_originalInputCp != CP_UTF8)
            {
                if (SetConsoleCP(CP_UTF8))
                {
                    _restoredInputCp = false;
                    TerminaTrace.Platform.Debug(this, "Switched console input CP {0} -> 65001 (UTF-8)", _originalInputCp);
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    TerminaTrace.Platform.Warning(this, "SetConsoleCP(65001) failed: error={0}", error);
                }
            }
            else
            {
                _restoredInputCp = true; // nothing to restore
            }
        }

        // Capture initial window size for resize event deduplication
        var initialSize = GetSize();
        _lastWidth = initialSize.Width;
        _lastHeight = initialSize.Height;
        TerminaTrace.Platform.Debug(this, "Initial window size: {0}x{1}", _lastWidth, _lastHeight);

        if (_rawVtMode)
        {
            _stopCts = new CancellationTokenSource();
            _readerThread = new Thread(RawByteReaderLoop)
            {
                IsBackground = true,
                Name = "Termina.WindowsConsole.RawReader",
            };
            _readerThread.Start();
        }

        _initialized = true;
        TerminaTrace.Platform.Info(this, "WindowsConsole.Initialize() completed successfully");
    }

    private void LogOutputModeFlags(string prefix, uint mode)
    {
        var flags = new System.Collections.Generic.List<string>();
        if ((mode & ENABLE_PROCESSED_OUTPUT) != 0) flags.Add("PROCESSED_OUTPUT");
        if ((mode & ENABLE_WRAP_AT_EOL_OUTPUT) != 0) flags.Add("WRAP_AT_EOL");
        if ((mode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0) flags.Add("VT_PROCESSING");
        if ((mode & DISABLE_NEWLINE_AUTO_RETURN) != 0) flags.Add("DISABLE_NEWLINE_AUTO");

        TerminaTrace.Platform.Debug(this, "{0}: [{1}]", prefix, string.Join(", ", flags));
    }

    /// <inheritdoc />
    public void Restore()
    {
        if (!_initialized) return;

        // Stop the raw reader thread first so we're not racing for the input handle.
        if (_rawVtMode)
        {
            try { _stopCts?.Cancel(); } catch { /* ignore */ }
            try
            {
                if (_inputHandle != IntPtr.Zero && _inputHandle != new IntPtr(-1))
                    CancelIoEx(_inputHandle, IntPtr.Zero);
            }
            catch { /* ignore */ }
            try { _readerThread?.Join(TimeSpan.FromMilliseconds(250)); } catch { /* ignore */ }

            if (!_restoredInputCp)
            {
                try { _ = SetConsoleCP(_originalInputCp); }
                catch { /* ignore */ }
                _restoredInputCp = true;
            }
        }

        // Restore original console modes
        if (_inputHandle != IntPtr.Zero && _inputHandle != new IntPtr(-1))
        {
            SetConsoleMode(_inputHandle, _originalInputMode);
        }

        if (_outputHandle != IntPtr.Zero && _outputHandle != new IntPtr(-1))
        {
            SetConsoleMode(_outputHandle, _originalOutputMode);
        }

        _initialized = false;
    }

    /// <inheritdoc />
    public async ValueTask<IConsoleInputEvent?> ReadInputAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            TerminaTrace.Platform.Error(this, "ReadInputAsync called but console not initialized");
            throw new InvalidOperationException("Console not initialized. Call Initialize() first.");
        }

        if (_rawVtMode)
        {
            try
            {
                return await _rawEvents.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return null; }
            catch (ChannelClosedException) { return null; }
        }

        // Record mode: poll Console.KeyAvailable. The input task is separate from the main
        // loop via the channel architecture, so the short Task.Delay between polls is correct.
        while (!cancellationToken.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                TerminaTrace.Platform.Trace(this, "Key pressed: {0}", key.Key);
                return new ConsoleKeyEvent(key);
            }

            // Check for window resize while we wait
            var currentSize = GetSize();
            if (currentSize.Width != _lastWidth || currentSize.Height != _lastHeight)
            {
                _lastWidth = currentSize.Width;
                _lastHeight = currentSize.Height;
                TerminaTrace.Platform.Debug(this, "Window resized: {0}x{1}", _lastWidth, _lastHeight);
                var resizeEvent = new ConsoleResizeEvent(_lastWidth, _lastHeight);
                _resized.OnNext(resizeEvent);
                return resizeEvent;
            }

            try
            {
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public (int Width, int Height) GetSize()
    {
        if (_outputHandle == IntPtr.Zero || _outputHandle == new IntPtr(-1))
        {
            return (Console.WindowWidth, Console.WindowHeight);
        }

        if (GetConsoleScreenBufferInfo(_outputHandle, out var info))
        {
            // Use the window size, not the buffer size
            var width = info.srWindow.Right - info.srWindow.Left + 1;
            var height = info.srWindow.Bottom - info.srWindow.Top + 1;
            return (width, height);
        }

        return (Console.WindowWidth, Console.WindowHeight);
    }

    /// <summary>
    /// Raw-VT reader loop: blocks on the console input handle with a 100 ms poll cadence, reads
    /// raw VT bytes via <c>ReadFile</c>, wraps each byte as a <see cref="ConsoleKeyEvent"/>, and
    /// pushes to the channel. UTF-8 multi-byte sequences (kitty associated-text, pasted Unicode)
    /// are reassembled before emission so they survive the <c>char</c> boundary.
    /// </summary>
    private void RawByteReaderLoop()
    {
        var stopCt = _stopCts!.Token;
        var buf = new byte[256];
        var chars = new char[2];
        var oneByte = new byte[1];
        var decoder = Encoding.UTF8.GetDecoder();

        while (!stopCt.IsCancellationRequested)
        {
            // Block on the input handle with a 100 ms cap so we can opportunistically poll for
            // resize even when the user is idle. Mirrors UnixConsole's VMIN=0/VTIME=1 cadence.
            var waitResult = WaitForSingleObject(_inputHandle, 100);
            if (stopCt.IsCancellationRequested) break;

            if (waitResult == WAIT_TIMEOUT)
            {
                CheckResize();
                continue;
            }

            if (waitResult == WAIT_FAILED)
            {
                var error = Marshal.GetLastWin32Error();
                TerminaTrace.Platform.Error(this, "WaitForSingleObject(input) failed: error={0}", error);
                break;
            }

            if (waitResult != WAIT_OBJECT_0) continue;

            bool ok;
            uint bytesRead;
            try
            {
                ok = ReadFile(_inputHandle, buf, (uint)buf.Length, out bytesRead, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                TerminaTrace.Platform.Error(this, "ReadFile(input) threw: {0}", ex.Message);
                break;
            }

            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                // ERROR_OPERATION_ABORTED (995) is expected on CancelIoEx during shutdown.
                if (error == 995) break;
                TerminaTrace.Platform.Error(this, "ReadFile(input) failed: error={0}", error);
                break;
            }

            if (bytesRead == 0)
            {
                CheckResize();
                continue;
            }

            for (var i = 0; i < (int)bytesRead; i++)
            {
                var b = buf[i];
                if (b < 0x80)
                {
                    // ASCII / control / escape-sequence byte — emit verbatim, one event each.
                    _rawEvents.Writer.TryWrite(new ConsoleKeyEvent(RawByteKeyMapper.ByteToKeyInfo(b)));
                }
                else
                {
                    // UTF-8 continuation/lead — feed decoder. Emits 0, 1, or 2 chars
                    // (the latter for surrogate-pair codepoints).
                    oneByte[0] = b;
                    var charCount = decoder.GetChars(oneByte, 0, 1, chars, 0);
                    for (var c = 0; c < charCount; c++)
                    {
                        _rawEvents.Writer.TryWrite(new ConsoleKeyEvent(
                            new ConsoleKeyInfo(chars[c], ConsoleKey.None, false, false, false)));
                    }
                }
            }

            CheckResize();
        }

        try { _rawEvents.Writer.TryComplete(); } catch { /* ignore */ }
    }

    private readonly object _resizeLock = new();

    private void CheckResize()
    {
        var (w, h) = GetSize();
        lock (_resizeLock)
        {
            if (w == _lastWidth && h == _lastHeight) return;
            _lastWidth = w;
            _lastHeight = h;
        }

        var evt = new ConsoleResizeEvent(w, h);
        try { _resized.OnNext(evt); } catch { /* ignore */ }
        _rawEvents.Writer.TryWrite(evt);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Restore();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _stopCts?.Dispose();
        _resized.OnCompleted();
        _resized.Dispose();
    }
}
