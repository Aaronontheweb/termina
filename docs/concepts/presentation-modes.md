# Terminal Presentation Modes

Termina supports full-screen and inline presentation modes.

`FullScreen` remains the default mode. It preserves the behavior of current applications.

`Inline` is an opt-in mode. It keeps stable output in the primary terminal buffer.

## Full-Screen Mode

Full-screen mode owns the complete terminal viewport.

Termina enters the alternate buffer at application start. Termina leaves that buffer at application exit.

The default input mode is `LegacyMouseTracking`.

Use full-screen mode for these applications:

- dashboards
- forms
- setup wizards
- applications that need a fixed viewport

## Inline Mode

Inline mode owns one bounded live region in the primary buffer.

Termina replaces only that live region after each frame. Stable output stays above the live region.

The terminal owns native selection and scrollback. Termina does not capture mouse-wheel input in this mode.

Inline mode requires `ScrollInputMode.NativeTerminal`.

```csharp
using Termina.Hosting;

builder.Services.AddTermina("/chat", termina =>
{
    termina.ConfigureRuntime(options =>
    {
        options.PresentationMode = TerminalPresentationMode.Inline;
        options.ScrollInputMode = ScrollInputMode.NativeTerminal;
        options.PreferRawInput = true;
    });

    termina.RegisterRoute<ChatPage, ChatViewModel>("/chat");
});
```

Use inline mode for these applications:

- chats
- command shells
- build monitors
- applications with long settled output

## Stable Output

Inject `IInlineOutput` into a view model. Use it to commit a settled layout above the live region.

```csharp
using Termina.Layout;
using Termina.Terminal;

public sealed class ChatViewModel(IInlineOutput output) : ReactiveViewModel
{
    public ValueTask CommitMessageAsync(string text, CancellationToken cancellationToken)
    {
        return output.CommitAsync(new TextNode(text), cancellationToken);
    }
}
```

Termina processes commits in event-loop order. Each commit erases the live region, writes stable content, and restores the live region.

The commit task completes after the live region returns.

## Output Ownership

CAUTION: Direct console writes can corrupt an active live region.

Use `IInlineOutput` for stable application output. Use the current page layout for live output.

Send diagnostic output to a file or another sink. Do not write diagnostic text to standard output during an inline session.

## Compatibility

The presentation API uses an extend-only design.

- `FullScreen` has value `0`.
- `Inline` has value `1`.
- `NativeTerminal` appends value `2` to `ScrollInputMode`.
- The current `IAnsiTerminal` contract does not change.
- `IInlineTerminalControl` supplies the new relative cursor operations.
- The current `AnsiTerminal(bool)` constructor keeps its behavior.

An inline application fails at startup when its terminal lacks `IInlineTerminalControl`.

## Terminal Support

Test inline mode on every supported terminal. Include direct sessions and multiplexer sessions.

The application must restore these states after a normal exit or a failed exit:

- the cursor
- bracketed paste
- keyboard protocol flags
- mouse modes
- terminal input mode

See the [inline demo](https://github.com/Aaronontheweb/termina/tree/dev/demos/Termina.Demo.Inline) for a small test application.
