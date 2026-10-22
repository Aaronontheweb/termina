namespace Termina.Input;

/// <summary>
/// Low-level keyboard input event.
/// Contains the raw ConsoleKeyInfo from the keyboard.
/// </summary>
public sealed record KeyPressed(ConsoleKeyInfo KeyInfo) : IInputEvent
{
    /// <summary>
    /// Gets the text that the key press produces, when the terminal reports it.
    /// </summary>
    /// <remarks>
    /// The text can contain more than one UTF-16 character, for example a surrogate pair or a letter with
    /// a combining accent. In that case <see cref="ConsoleKeyInfo.KeyChar"/> contains only the first UTF-16
    /// character. Insert this text, when it is set, instead of <see cref="ConsoleKeyInfo.KeyChar"/>.
    /// The value is <see langword="null"/> when the key does not produce printable text or when the
    /// terminal does not report text.
    /// </remarks>
    public string? Text { get; init; }
}
