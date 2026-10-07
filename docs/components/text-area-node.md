# TextAreaNode

A multi-line text input with word wrap, vertical scrolling, and newline insertion.

`TextAreaNode` shares its editing core (cursor, selection, history, paste) with [`TextInputNode`](./text-input-node.md) via the common base class `TextInputBaseNode`.

## Basic Usage

```csharp
var textArea = new TextAreaNode()
    .WithPlaceholder("Enter your message...");

// Enter submits, Ctrl+Enter inserts newlines
textArea.Submitted.Subscribe(text => Console.WriteLine($"Submitted:\n{text}"));
```

## Features

- Multi-line text editing with Ctrl+Enter for newlines
- Word wrap with dynamic height
- Vertical scrolling when content exceeds viewport
- Visual line navigation (Up/Down moves between wrapped lines)
- All shared features from `TextInputBaseNode`: cursor blink, selection, word navigation, history, paste

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `Enter` | Submit |
| `Ctrl+Enter` | Insert newline (configurable modifier) |
| `Alt+Enter` | Insert newline (universal fallback) |
| `←/→` | Move cursor |
| `Ctrl+←/→` | Move by word |
| `Shift+←/→` | Select text |
| `↑/↓` | Move between visual lines (or history when text is empty) |
| `Home` | Start of current visual line |
| `End` | End of current visual line |
| `Ctrl+Home` | Start of document |
| `Ctrl+End` | End of document |
| `Backspace` | Delete before cursor |
| `Ctrl+Backspace` | Delete word before |
| `Delete` | Delete after cursor |
| `Ctrl+A` | Select all |
| `Escape` | Clear text |

## Multi-Line Input

Enter submits (same as `TextInputNode`). Use Ctrl+Enter (or Alt+Enter) to insert newlines:

```csharp
var textArea = new TextAreaNode()
    .WithPlaceholder("Write your story...");

// Ctrl+Enter inserts newlines while editing
// Enter submits the full multi-line content
textArea.Submitted.Subscribe(text =>
{
    // text contains the full multi-line content
    // e.g., "line 1\nline 2\nline 3"
    ProcessMultiLineInput(text);
});
```

## Newline Modifier

By default, `Ctrl+Enter` inserts a newline and bare `Enter` submits. You can change which modifier inserts a newline:

```csharp
// Shift+Enter inserts newline instead of Ctrl+Enter
new TextAreaNode()
    .WithNewlineModifier(ConsoleModifiers.Shift);
```

### Terminal Compatibility

Standard Linux terminals send the same byte (`0x0D`) for both Enter and Ctrl+Enter, making them
indistinguishable. Termina enables the [kitty keyboard protocol](https://sw.kovidgoyal.net/kitty/keyboard-protocol/)
at startup, which causes modern terminals to send distinct [CSI u escape sequences](https://blog.fsck.com/releases/2026/02/26/terminal-keyboard-protocol/)
(e.g. `ESC[13;5u` for Ctrl+Enter). This is supported by kitty, WezTerm, Ghostty, Alacritty, foot, and others.

Terminals that do not support the kitty protocol will silently ignore the enable sequence and
Ctrl+Enter will behave the same as Enter (submit). **Alt+Enter always works** as a universal
fallback because terminals encode the Alt modifier as an ESC prefix (`ESC` + `CR`), which is
reliably detected even inside tmux.

## Word Wrap

Word wrap is enabled by default. Text wraps at word boundaries when it exceeds the available width:

```csharp
// Disable word wrap (long lines extend beyond viewport)
new TextAreaNode()
    .WithWordWrap(false);
```

## Height Control

TextAreaNode uses `Auto` height by default (min: 1, max: 10 rows). The height grows with content up to the maximum:

```csharp
// Limit to 5 visible rows (scrolls when content exceeds)
new TextAreaNode()
    .WithMaxHeight(5);
```

## MaxLines

Limit the number of logical lines (newlines) the user can enter:

```csharp
// Allow at most 10 lines
new TextAreaNode()
    .WithMaxLines(10);
```

When at the limit, Ctrl+Enter is consumed but no newline is inserted.

## Input History

Like `TextInputNode`, history is opt-in. When the text area is empty, Up/Down navigate history. When there is content, Up/Down navigate between visual lines instead:

```csharp
var textArea = new TextAreaNode()
    .WithHistory(maxEntries: 20);
```

## Paste Handling

Paste behavior is identical to `TextInputNode` — both use the shared `TextInputBaseNode` logic:

- **Single-line paste**: inserted inline at the cursor position
- **Multi-line paste**: stored as a committed segment with a summary display (e.g., `[Pasted 3 lines, 45 chars]`), with the full content preserved for submission

```csharp
textArea.HandlePaste(new PasteEvent("line 1\nline 2\nline 3"));
// textArea.Text shows "[Pasted 3 lines, 29 chars] "
// But Enter submits the full original content: "line 1\nline 2\nline 3"
```

## Styling

```csharp
new TextAreaNode()
    .WithForeground(Color.White)
    .WithBackground(Color.DarkBlue)
    .WithPlaceholder("Describe the issue...")
```

### Colors

| Property | Default | Description |
|----------|---------|-------------|
| `Foreground` | terminal default | Text color |
| `Background` | terminal default | Background color |
| `PlaceholderColor` | `BrightBlack` | Placeholder text color |
| `CursorColor` | `White` | Cursor background color |
| `SelectionColor` | `Blue` | Selection background color |

## Comparison: TextInputNode vs TextAreaNode

| Aspect | TextInputNode | TextAreaNode |
|--------|--------------|-------------|
| **Lines** | Single-line | Multi-line |
| **Enter** | Submit | Submit |
| **Newline** | N/A | `Ctrl+Enter` or `Alt+Enter` (configurable) |
| **Up/Down** | History only | Visual lines (history when empty) |
| **Home/End** | Start/end of text | Start/end of visual line |
| **Multi-line paste** | Summary placeholder | Summary placeholder |
| **Height** | Fixed 1 row | Auto (1–10 rows, configurable) |
| **Scroll** | Horizontal | Vertical |

## Observables

| Observable | Type | Description |
|------------|------|-------------|
| `TextChanged` | `Observable<string>` | Emits when text changes (including newlines) |
| `Submitted` | `Observable<string>` | Emits on `Enter` |
| `Invalidated` | `Observable<Unit>` | Emits when redraw is needed |

## API Reference

### Constructor

```csharp
public TextAreaNode(int cursorBlinkMs = 530)
```

Text areas attached to a page layout tree receive runtime context automatically, so cursor-blink invalidation is delivered on the Termina render loop. For deterministic component tests, set a `LayoutRuntimeContext` before focusing the node.

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Text` | `string` | `""` | Current text value (includes newlines) |
| `Placeholder` | `string?` | `null` | Placeholder text |
| `MaxLength` | `int` | `0` | Max total characters (0 = unlimited) |
| `HasSelection` | `bool` | - | Has selected text |
| `SelectedText` | `string` | - | Currently selected text |
| `CursorPosition` | `int` | `0` | Cursor as a UTF-16 index into the editable text (clamped, clears selection). See [Pre-filled Values](./text-input-node.md#pre-filled-values) |

### Fluent Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `WithPlaceholder(string)` | `TextAreaNode` | Set placeholder text |
| `WithForeground(Color)` | `TextAreaNode` | Set text color |
| `WithBackground(Color)` | `TextAreaNode` | Set background color |
| `WithMaxLength(int)` | `TextAreaNode` | Set max character count |
| `WithMaxLines(int)` | `TextAreaNode` | Set max logical lines (0 = unlimited) |
| `WithMaxHeight(int)` | `TextAreaNode` | Set max visible rows |
| `WithWordWrap(bool)` | `TextAreaNode` | Enable/disable word wrap |
| `WithNewlineModifier(ConsoleModifiers)` | `TextAreaNode` | Set newline key modifier |
| `WithHistory(int)` | `TextAreaNode` | Enable input history |

### Methods

| Method | Description |
|--------|-------------|
| `HandleInput(ConsoleKeyInfo)` | Process a key press |
| `HandlePaste(PasteEvent)` | Handle pasted content (shared base class logic) |
| `MoveCursorToEnd()` | Move the cursor after the last editable character |
| `Clear()` | Clear text and reset cursor |
| `AddHistory(string)` | Programmatically add a history entry |
| `Start()` | Start cursor animation |
| `Stop()` | Stop cursor animation |

## Source Code

::: details View TextAreaNode implementation
<<< @/../src/Termina/Layout/TextAreaNode.cs{csharp}
:::
