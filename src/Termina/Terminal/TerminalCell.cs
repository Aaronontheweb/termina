// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Termina.Terminal;

/// <summary>
/// Represents a single cell in the terminal buffer.
/// Each cell contains text and its associated styling (colors and decorations).
/// </summary>
/// <remarks>
/// Used for double-buffering in diff-based rendering to track what's on screen
/// and compare against pending changes to minimize ANSI output.
/// </remarks>
public readonly record struct TerminalCell : IEquatable<TerminalCell>
{
    /// <summary>
    /// The first UTF-16 character displayed in this cell.
    /// </summary>
    public char Character { get; init; }

    /// <summary>
    /// The complete text element displayed in this cell.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// True when this cell is the trailing half of a wide text element.
    /// Continuation cells reserve terminal columns but are not emitted directly.
    /// </summary>
    public bool IsContinuation { get; init; }

    /// <summary>
    /// The foreground (text) color.
    /// </summary>
    public Color Foreground { get; init; }

    /// <summary>
    /// The background color.
    /// </summary>
    public Color Background { get; init; }

    /// <summary>
    /// Text decorations (bold, italic, underline, etc.).
    /// </summary>
    public TextDecoration Decoration { get; init; }

    /// <summary>
    /// Creates a new terminal cell with the specified character and styling.
    /// </summary>
    public TerminalCell(char character, Color foreground, Color background, TextDecoration decoration)
    {
        Character = character;
        Text = character.ToString();
        Foreground = foreground;
        Background = background;
        Decoration = decoration;
        IsContinuation = false;
    }

    /// <summary>
    /// Creates a new terminal cell with the specified text element and styling.
    /// </summary>
    public TerminalCell(string text, Color foreground, Color background, TextDecoration decoration, bool isContinuation = false)
    {
        Text = string.IsNullOrEmpty(text) ? " " : text;
        Character = Text[0];
        Foreground = foreground;
        Background = background;
        Decoration = decoration;
        IsContinuation = isContinuation;
    }

    /// <summary>
    /// An empty cell - a space with default colors and no decoration.
    /// </summary>
    public static TerminalCell Empty => new()
    {
        Character = ' ',
        Text = " ",
        Foreground = Color.Default,
        Background = Color.Default,
        Decoration = TextDecoration.None,
        IsContinuation = false
    };

    /// <summary>
    /// Creates a cell with the specified character and default styling.
    /// </summary>
    public static TerminalCell FromChar(char c) => new()
    {
        Character = c,
        Text = c.ToString(),
        Foreground = Color.Default,
        Background = Color.Default,
        Decoration = TextDecoration.None,
        IsContinuation = false
    };

    /// <summary>
    /// Creates a continuation cell for the trailing half of a wide text element.
    /// </summary>
    public static TerminalCell Continuation(Color foreground, Color background, TextDecoration decoration) => new()
    {
        Character = ' ',
        Text = " ",
        Foreground = foreground,
        Background = background,
        Decoration = decoration,
        IsContinuation = true
    };

    /// <summary>
    /// Returns a new cell with the same styling but a different character.
    /// </summary>
    public TerminalCell WithCharacter(char c) => this with { Character = c, Text = c.ToString(), IsContinuation = false };

    /// <summary>
    /// Returns a new cell with the same character but different foreground color.
    /// </summary>
    public TerminalCell WithForeground(Color color) => this with { Foreground = color };

    /// <summary>
    /// Returns a new cell with the same character but different background color.
    /// </summary>
    public TerminalCell WithBackground(Color color) => this with { Background = color };

    /// <summary>
    /// Returns a new cell with the same character but different decoration.
    /// </summary>
    public TerminalCell WithDecoration(TextDecoration decoration) => this with { Decoration = decoration };

    /// <summary>
    /// Checks if this cell has the same styling (colors and decoration) as another cell.
    /// Useful for optimizing ANSI output - only emit style changes when needed.
    /// </summary>
    public bool HasSameStyle(TerminalCell other) =>
        Foreground == other.Foreground &&
        Background == other.Background &&
        Decoration == other.Decoration;

    public bool Equals(TerminalCell other) =>
        Character == other.Character &&
        Text == other.Text &&
        IsContinuation == other.IsContinuation &&
        Foreground == other.Foreground &&
        Background == other.Background &&
        Decoration == other.Decoration;

    public override int GetHashCode() => HashCode.Combine(Character, Text, IsContinuation, Foreground, Background, Decoration);

    public override string ToString() => $"'{Text}' (FG:{Foreground}, BG:{Background}, Deco:{Decoration}, Continuation:{IsContinuation})";
}
