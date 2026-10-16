// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Diagnostics;
using Termina.Terminal;

namespace Termina.Platform;

/// <summary>
/// Cross-platform helper for pushing and popping the kitty keyboard protocol enhancement bits
/// (<c>CSI &gt; N u</c> / <c>CSI &lt; u</c>) on the terminal's per-screen stack.
/// </summary>
/// <remarks>
/// The owning application selects the exact kitty flags to negotiate. Common value is <c>8</c>
/// (<c>report_all_keys</c>), which forces all keyboard input through <c>CSI ... u</c> /
/// <c>CSI 1;mods X</c> — unambiguously separating real arrow keys from <c>?1007h</c> wheel
/// events that continue to arrive as legacy <c>CSI A/B</c> or <c>SS3 OA/OB</c>.
/// <para>
/// Output bytes are written via <see cref="Console.Out"/> and require the host terminal to
/// be in a VT-processing output mode. On Windows this means
/// <c>ENABLE_VIRTUAL_TERMINAL_PROCESSING</c> must already be set on the output handle by
/// the platform console.
/// </para>
/// </remarks>
internal static class KittyKeyboardEnhancement
{
    /// <summary>
    /// Pushes the specified kitty keyboard protocol flags onto the terminal's per-screen stack.
    /// </summary>
    /// <param name="traceSource">Object used for trace-source attribution in logs.</param>
    /// <param name="flags">The kitty keyboard protocol flags to push.</param>
    /// <param name="tmuxPassthrough">If true, also forwards the sequence to the outer terminal via tmux DCS passthrough.</param>
    /// <returns>True if the enhancement was pushed; false if <paramref name="flags"/> is non-positive or the write failed.</returns>
    public static bool TryEnter(object traceSource, int flags, bool tmuxPassthrough)
    {
        if (flags <= 0) return false;

        try
        {
            var sequence = $"\x1b[>{flags}u";
            Console.Out.Write(sequence);
            if (tmuxPassthrough)
                Console.Out.Write(AnsiCodes.TmuxPassthrough(sequence));
            Console.Out.Flush();
            TerminaTrace.Platform.Info(traceSource, "Pushed kitty keyboard flags={0}", flags);
            return true;
        }
        catch (Exception ex)
        {
            TerminaTrace.Platform.Error(traceSource, "Failed to push kitty keyboard flags: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Queries active keyboard flags and then requests primary device attributes.
    /// </summary>
    /// <remarks>
    /// A compatible terminal returns keyboard flags before device attributes.
    /// An incompatible terminal returns only device attributes.
    /// </remarks>
    public static bool TryQuery(object traceSource)
    {
        try
        {
            Console.Out.Write(AnsiCodes.QueryKittyKeyboard);
            Console.Out.Write(AnsiCodes.RequestPrimaryDeviceAttributes);
            Console.Out.Flush();
            TerminaTrace.Platform.Info(traceSource, "Queried kitty keyboard support");
            return true;
        }
        catch (Exception ex)
        {
            TerminaTrace.Platform.Error(traceSource, "Failed to query kitty keyboard support: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Pops the kitty enhancement from the terminal's per-screen stack by writing
    /// <c>CSI &lt; u</c>. Safe to call on shutdown / restore paths; swallows failures.
    /// </summary>
    public static void TryLeave(bool tmuxPassthrough)
    {
        try
        {
            Console.Out.Write(AnsiCodes.DisableKittyKeyboard);
            if (tmuxPassthrough)
                Console.Out.Write(AnsiCodes.TmuxPassthrough(AnsiCodes.DisableKittyKeyboard));
            Console.Out.Flush();
        }
        catch
        {
            // Tearing down — nothing useful we can do here.
        }
    }
}
