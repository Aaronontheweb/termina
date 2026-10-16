# TextInputNode

A single-line text input with cursor, selection, and keyboard handling.

For multi-line input, see [`TextAreaNode`](./text-area-node.md). Both share a common base class (`TextInputBaseNode`) for cursor management, selection, history, and paste handling.

![TextInputNode demo: typing into single-line inputs and a multi-line text area](/gallery/gallery-text-input.gif)

*Single-line inputs and a multi-line text area.*

## Basic Usage

```csharp
var input = new TextInputNode()
    .WithPlaceholder("Enter text...");

// Handle submission
input.Submitted.Subscribe(text => Console.WriteLine($"Submitted: {text}"));
```

## Features

- Blinking cursor
- Text selection (Shift+Arrow keys)
- Word-by-word navigation (Ctrl+Arrow)
- Password masking
- Max length validation
- Placeholder text

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `←/→` | Move cursor |
| `Ctrl+←/→` | Move by word |
| `Shift+←/→` | Select text |
| `Home/End` | Jump to start/end |
| `Backspace` | Delete before cursor |
| `Ctrl+Backspace` | Delete word before |
| `Delete` | Delete after cursor |
| `Ctrl+A` | Select all |
| `↑/↓` | Navigate history (when enabled via `WithHistory()`) |
| `Enter` | Submit (auto-records to history when enabled) |
| `Escape` | Clear text |

## Input History

TextInputNode has built-in, opt-in input history. When enabled, Up/Down arrow keys navigate through previous submissions, and Enter auto-records non-empty text.

```csharp
var input = new TextInputNode()
    .WithPlaceholder("Enter command...")
    .WithHistory();          // Unlimited history

var input2 = new TextInputNode()
    .WithPlaceholder("Enter command...")
    .WithHistory(maxEntries: 50);  // Keep last 50 entries
```

History is **off by default** — existing behavior is unchanged unless you call `WithHistory()`.

### Programmatic History

Use `AddHistory()` for submissions that bypass the normal Enter flow:

```csharp
// E.g., custom prompts from a SelectionListNode's "Other" option
input.AddHistory(customPrompt);
```

### History Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `↑` | Recall previous entry (saves current input on first press) |
| `↓` | Recall next entry (restores saved input when past the end) |

## Pre-filled Values

Assigning `Text` does not move the cursor. A new input keeps the cursor at position 0, so the first typed character would land before the default value. Call `MoveCursorToEnd()` after the assignment to start with the cursor after the text:

```csharp
var endpoint = new TextInputNode();
endpoint.Text = "http://localhost:11434";
endpoint.MoveCursorToEnd();
```

`Text` keeps the cursor where it is on purpose. A binding that writes `Text` back while the user types does not make the cursor jump.

`CursorPosition` is a UTF-16 index into the editable text, the text after the last committed segment. It is not an index into `Text`. A multi-line paste commits the text before it, and the paste itself, as segments the cursor cannot enter. For example, when `Text` is `abc[Pasted 2 lines, 5 chars] def`, the editable text is ` def` and the valid range is 0 to 4. A multi-line value that you assign to `Text` becomes a paste summary, so the editable text is empty and the only valid position is 0. The setter moves a position inside a surrogate pair or a grapheme cluster to the start of that element and clamps other out-of-range values. Like the `Text` setter, it clears the selection and invalidates the node even when the position does not change, and it does not raise `TextChanged`. A focused node shows the cursor and restarts the blink cycle. It works before the node is attached or focused.

## Password Mode

```csharp
new TextInputNode()
    .AsPassword()        // Use default mask '•'

new TextInputNode()
    .AsPassword('*')     // Use custom mask character
```

## Styling

```csharp
new TextInputNode()
    .WithForeground(Color.White)
    .WithBackground(Color.Blue)
    .WithPlaceholder("Type here...")
```

### Colors

| Property | Default | Description |
|----------|---------|-------------|
| `Foreground` | terminal default | Text color |
| `Background` | terminal default | Background color |
| `PlaceholderColor` | `BrightBlack` | Placeholder text color |
| `CursorColor` | `White` | Cursor background color |
| `SelectionColor` | `Blue` | Selection background color |

## Handling Input

TextInputNode is a layout node that should be **owned by the Page**. There are two patterns for input handling:

### Pattern 1: Inside a Modal (Recommended)

When TextInputNode is inside a Modal, Focus automatically routes input:

```csharp
public class MyPage : ReactivePage<MyViewModel>
{
    private TextInputNode _textInput = null!;
    private ModalNode _modal = null!;

    protected override void OnBound()
    {
        _textInput = new TextInputNode()
            .WithPlaceholder("Enter command...");
        _modal = Layouts.Modal().WithContent(_textInput);

        _textInput.Submitted
            .Subscribe(text => ViewModel.OnTextSubmitted(text))
            .DisposeWith(Subscriptions);

        // When showing modal, Focus handles input routing automatically
        ViewModel.IsShowingModalChanged
            .Where(show => show)
            .Subscribe(_ => Focus.PushFocus(_modal))
            .DisposeWith(Subscriptions);
    }
}
```

### Pattern 2: Always-Visible Input

For always-visible text inputs, route input via `ViewModel.Input`:

```csharp
public class MyPage : ReactivePage<MyViewModel>
{
    private TextInputNode _promptInput = null!;

    protected override void OnBound()
    {
        _promptInput = new TextInputNode()
            .WithPlaceholder("Enter command...");

        // Route input from ViewModel to the text input
        ViewModel.Input.OfType<IInputEvent, KeyPressed>()
            .Subscribe(key => _promptInput.HandleInput(key.KeyInfo))
            .DisposeWith(Subscriptions);

        // Handle submission
        _promptInput.Submitted
            .Subscribe(text => {
                ViewModel.OnTextSubmitted(text);
                _promptInput.Clear();
            })
            .DisposeWith(Subscriptions);
    }
}
```

## Paste Handling

`TextInputNode` implements `IPasteReceiver` and automatically handles bracketed paste mode. When the user pastes text from the clipboard, Termina detects the terminal's paste escape sequences and delivers the content as a single `PasteEvent` rather than individual key presses.

In Termina 0.11.0, focused paste routing is covered by real terminal conformance tests for direct terminals, tmux, kitty, and kitty plus tmux. See the [0.11.0 upgrade advisory](/guide/upgrade-0.11) if your app needs users to copy text from the screen and paste it into a Termina input.

### How It Works

1. The user pastes text (Ctrl+V or right-click paste)
2. The terminal wraps the content in `ESC[200~...ESC[201~` markers
3. Termina detects the markers and emits a `PasteEvent`
4. `TextInputNode` shows a summary: `[Pasted 500 lines, 12345 chars]`
5. On **Enter**, the full paste content (with newlines preserved) is submitted
6. On any **editing action** (typing, backspace, delete, escape), the paste is cleared

This prevents multi-line pastes from triggering individual submissions for each line — a common issue in terminal applications.

```csharp
// Paste handling is automatic — no additional setup needed
var input = new TextInputNode()
    .WithPlaceholder("Paste or type here...");

// Submitted receives the full paste content when Enter is pressed
input.Submitted.Subscribe(text =>
{
    // 'text' contains the full paste with newlines preserved
    Console.WriteLine($"Received {text.Length} chars");
});
```

### Paste in ViewModels

If you need to handle paste events at the ViewModel level (e.g., when no `TextInputNode` has focus):

```csharp
Input.OfType<PasteEvent>()
    .Subscribe(paste =>
    {
        // paste.Content contains the full pasted text
        ProcessPastedContent(paste.Content);
    })
    .DisposeWith(Subscriptions);
```

### Custom Input Components

If you build a custom focusable input node, implement `IPasteReceiver` so paste content goes directly to the focused component:

```csharp
public sealed class CustomInputNode : LayoutNode, IFocusable, IPasteReceiver
{
    public bool HandlePaste(PasteEvent paste)
    {
        if (string.IsNullOrEmpty(paste.Content))
            return false;

        InsertTextAtCursor(paste.Content);
        return true;
    }
}
```

## Observables

| Observable | Type | Description |
|------------|------|-------------|
| `TextChanged` | `Observable<string>` | Emits when text changes |
| `Submitted` | `Observable<string>` | Emits when Enter is pressed |
| `Invalidated` | `Observable<Unit>` | Emits when redraw is needed |

## API Reference

### Constructor

```csharp
public TextInputNode(int cursorBlinkMs = 530)
```

Inputs attached to a page layout tree receive runtime context automatically, so cursor-blink invalidation is delivered on the Termina render loop. For deterministic component tests, set a `LayoutRuntimeContext` before focusing the node.

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Text` | `string` | `""` | Current text value |
| `Placeholder` | `string?` | `null` | Placeholder text |
| `MaxLength` | `int` | `0` | Max length (0 = unlimited) |
| `IsPassword` | `bool` | `false` | Password mode |
| `PasswordChar` | `char` | `'•'` | Mask character |
| `HasSelection` | `bool` | - | Has selected text |
| `SelectedText` | `string` | - | Currently selected text |
| `CursorPosition` | `int` | `0` | Cursor as a UTF-16 index into the editable text (clamped, clears selection) |

### Methods

| Method | Description |
|--------|-------------|
| `WithHistory(int maxEntries = 0)` | Enable built-in input history (0 = unlimited) |
| `AddHistory(string entry)` | Programmatically add a history entry (no-op when disabled) |
| `HandleInput(ConsoleKeyInfo)` | Process a key press |
| `HandlePaste(PasteEvent)` | Handle bracketed paste (implements `IPasteReceiver`) |
| `MoveCursorToEnd()` | Move the cursor after the last editable character |
| `Clear()` | Clear text and reset cursor |
| `Start()` | Start cursor animation |
| `Stop()` | Stop cursor animation |

## Source Code

::: details View TextInputNode implementation
<<< @/../src/Termina/Layout/TextInputNode.cs{csharp}
:::
