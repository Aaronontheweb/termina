// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Terminal;

namespace Termina.Tests.Terminal;

/// <summary>
/// Regression tests for how <see cref="AnsiTerminal"/> resolves its output writer.
/// </summary>
/// <remarks>
/// These tests mutate process-global <see cref="Console.Out"/>. They share a
/// collection so they never run concurrently with other console-touching tests.
/// </remarks>
[Collection("Console serialization")]
public class AnsiTerminalTests
{
    [Fact]
    public void Inline_controls_emit_relative_cursor_sequences()
    {
        var output = new StringWriter();
        using var terminal = new AnsiTerminal(output, useAlternateScreen: false);

        terminal.MoveCursorUp(2);
        terminal.MoveCursorDown(3);
        terminal.MoveCursorToLineStart();
        terminal.EraseLine();
        terminal.WriteLineBreak();
        terminal.Flush();

        Assert.Contains(AnsiCodes.MoveUp(2), output.ToString());
        Assert.Contains(AnsiCodes.MoveDown(3), output.ToString());
        Assert.Contains($"\r{AnsiCodes.ClearLine}\r\n", output.ToString());
    }

    [Fact]
    public void Flush_writes_to_current_ConsoleOut_not_a_stale_reference()
    {
        // Regression test for #204: AnsiTerminal used to capture Console.Out at
        // construction. Setting Console.OutputEncoding (done by the platform console
        // during startup) replaces Console.Out with a new TextWriter, so the captured
        // reference went stale — on a non-UTF-8 console this garbled Unicode output.
        var original = Console.Out;
        try
        {
            // Terminal constructed BEFORE Console.Out is replaced.
            using var terminal = new AnsiTerminal(useAlternateScreen: false);

            // Replace Console.Out, exactly as setting Console.OutputEncoding does at startup.
            var captured = new StringWriter();
            Console.SetOut(captured);

            terminal.Write("┌──┐");
            terminal.Flush();

            // A terminal that cached Console.Out would have written to the pre-swap
            // writer, leaving `captured` empty.
            Assert.Contains("┌──┐", captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Flush_uses_explicit_writer_when_one_is_provided()
    {
        // The internal constructor's explicit-writer seam must bypass Console.Out entirely.
        var explicitWriter = new StringWriter();
        var consoleSink = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(consoleSink);

            using var terminal = new AnsiTerminal(explicitWriter, useAlternateScreen: false);
            terminal.Write("hello");
            terminal.Flush();

            Assert.Contains("hello", explicitWriter.ToString());
            Assert.Empty(consoleSink.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
