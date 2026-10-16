// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Terminal;

/// <summary>
/// Provides relative cursor operations for a bounded primary-buffer region.
/// </summary>
/// <remarks>
/// An <see cref="IAnsiTerminal"/> implementation must also implement this interface
/// before an application can select <see cref="Hosting.TerminalPresentationMode.Inline"/>.
/// </remarks>
public interface IInlineTerminalControl
{
    /// <summary>
    /// Moves the cursor up by the specified row count.
    /// </summary>
    void MoveCursorUp(int rows);

    /// <summary>
    /// Moves the cursor down by the specified row count.
    /// </summary>
    void MoveCursorDown(int rows);

    /// <summary>
    /// Moves the cursor to the first column of its current row.
    /// </summary>
    void MoveCursorToLineStart();

    /// <summary>
    /// Erases the complete current row without a cursor move.
    /// </summary>
    void EraseLine();

    /// <summary>
    /// Moves the cursor to the first column of the next row.
    /// </summary>
    void WriteLineBreak();
}
