# Input Handling

Termina provides a structured input routing system with two phases: **capture** (page-level) and **bubble** (component-level), followed by the ViewModel's input observable for any remaining events.

## Input Flow Overview

```
Keyboard Input
      │
      ▼
┌─────────────────────────────────────────┐
│  CAPTURE PHASE (Page)                   │
│  Page.KeyBindings intercepts first      │
│  Good for: Escape, Tab, global hotkeys  │
└─────────────────────────────────────────┘
      │ (if not consumed)
      ▼
┌─────────────────────────────────────────┐
│  BUBBLE PHASE (Components)              │
│  FocusManager routes to focused node    │
│  Good for: Text input, list navigation  │
└─────────────────────────────────────────┘
      │ (if not consumed)
      ▼
┌─────────────────────────────────────────┐
│  VIEWMODEL (ViewModel.Input)            │
│  Observable receives unconsumed events  │
│  Good for: State changes, fallback      │
└─────────────────────────────────────────┘
```

This model is similar to DOM event handling where events are captured from the top down, then bubble up from the target.

## Page-Level Key Bindings (Capture Phase)

Use `KeyBindings` in your Page to intercept keys **before** focused components receive them. This is essential for navigation keys like Escape that should always work, regardless of what component has focus.

### Basic Usage

```csharp
public class MyPage : ReactivePage<MyViewModel>
{
    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();

        // Register page-level key bindings (capture phase)
        // Pages have direct access to Navigate() and Shutdown()
        KeyBindings.Register(ConsoleKey.Escape, () => Navigate("/menu"));
        KeyBindings.Register(ConsoleKey.Q, () => Shutdown());
    }
}
```

### With Modifiers

```csharp
public override void OnNavigatedTo()
{
    base.OnNavigatedTo();

    // Ctrl+S to save
    KeyBindings.Register(ConsoleKey.S, ConsoleModifiers.Control, () => ViewModel.Save());

    // Shift+Tab for reverse navigation
    KeyBindings.Register(ConsoleKey.Tab, ConsoleModifiers.Shift, () => FocusPrevious());

    // Plain Tab for forward navigation
    KeyBindings.Register(ConsoleKey.Tab, () => FocusNext());
}
```

### Focus Cycling Example

```csharp
public class FormPage : ReactivePage<FormViewModel>
{
    private TextInputNode _nameInput = null!;
    private TextInputNode _emailInput = null!;
    private int _focusIndex;

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();

        KeyBindings.Register(ConsoleKey.Escape, () => Navigate("/"));
        KeyBindings.Register(ConsoleKey.Tab, CycleFocus);

        // Start with first input focused
        _focusIndex = 0;
        Focus.PushFocus(_nameInput);
    }

    private void CycleFocus()
    {
        _focusIndex = (_focusIndex + 1) % 2;
        var target = _focusIndex == 0 ? _nameInput : _emailInput;
        Focus.PushFocus(target);
    }
}
```

### When to Use Page-Level Bindings

| Use Case | Example |
|----------|---------|
| Navigation keys | Escape to go back, Q to quit |
| Global shortcuts | Ctrl+S save, Ctrl+Z undo |
| Focus management | Tab/Shift+Tab between inputs |
| Modal dismissal | Escape to close dialog |
| Menu shortcuts | F1 for help, F5 for refresh |

## Component-Level Input (Bubble Phase)

After the capture phase, input routes to the currently focused component via `FocusManager`. Components implement `IFocusable.HandleInput()` to process keys.

### Built-in Focusable Components

| Component | Handles |
|-----------|---------|
| `TextInputNode` | Character input, backspace, cursor movement |
| `SelectionListNode` | Arrow keys, Enter, number keys 1-9 |
| `ScrollableContainerNode` | Arrow keys for scrolling |

### Focus Management

```csharp
public class MyPage : ReactivePage<MyViewModel>
{
    private SelectionListNode<string> _list = null!;

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();

        // Set initial focus
        Focus.PushFocus(_list);
    }

    private void ShowTextInput()
    {
        // Push new focus (previous is saved)
        Focus.PushFocus(_textInput);
    }

    private void HideTextInput()
    {
        // Pop focus (returns to previous)
        Focus.PopFocus();
    }
}
```

### Custom Focusable Component

```csharp
public class CustomInput : LayoutNode, IFocusable
{
    public bool IsFocused { get; set; }

    public bool HandleInput(ConsoleKeyInfo keyInfo)
    {
        // Return true if you handled (consumed) the key
        // Return false to let it bubble to ViewModel
        if (keyInfo.Key == ConsoleKey.Enter)
        {
            OnSubmit();
            return true;  // Consumed
        }

        return false;  // Not consumed, continue to ViewModel
    }
}
```

`IFocusable` also has a `HandleInput(KeyPressed)` overload.
Its default implementation calls `HandleInput(ConsoleKeyInfo)` with `KeyPressed.KeyInfo`.
Implement it when your component accepts text, because `KeyPressed.Text` can contain more than one character.
A component that forwards input to child components must forward the `KeyPressed` event, not only `KeyInfo`.

## ViewModel Input Observable

Keys not consumed by the capture or bubble phases arrive at the ViewModel's `Input` observable:

```csharp
public partial class MyViewModel : ReactiveViewModel
{
    public override void OnActivated()
    {
        // Handle keys that weren't consumed by Page or focused component
        Input.OfType<KeyPressed>()
            .Subscribe(HandleKey)
            .DisposeWith(Subscriptions);
    }

    private void HandleKey(KeyPressed key)
    {
        // This only receives keys not handled elsewhere
        switch (key.KeyInfo.Key)
        {
            case ConsoleKey.F5:
                Refresh();
                break;
        }
    }
}
```

::: warning Migration Note
Prior to v0.5, the ViewModel's `Input` observable received all keys. Now, with page-level key bindings, navigation keys like Escape should be handled in the Page using `KeyBindings.Register()` rather than in the ViewModel.
:::

## Input Events

All input events implement `IInputEvent`. The framework provides:

- `KeyPressed` - Keyboard input
- `MouseEvent` - Mouse clicks, movement, scrolling
- `MouseScrollEvent` - Mouse wheel scroll (routed to `IScrollable` components)
- `PasteEvent` - Bracketed paste (routed to `IPasteReceiver` components)
- `ResizeEvent` - Terminal window resize

### Automatic Routing

Some events are automatically routed to the focused component before reaching the ViewModel's `Input` observable:

| Event | Routed To | Fallback |
|-------|-----------|----------|
| `MouseScrollEvent` | Focused `IScrollable` component | `ViewModel.Input` |
| `PasteEvent` | Focused `IPasteReceiver` component | `ViewModel.Input` |

This means `StreamingTextNode` (which implements `IScrollable`) automatically handles mouse wheel scrolling, and `TextInputNode` (which implements `IPasteReceiver`) automatically handles paste — no manual wiring needed.

## Keyboard Input Details

### Checking Modifiers

```csharp
private void HandleKey(KeyPressed key)
{
    var info = key.KeyInfo;

    // Check for Ctrl+C
    if (info.Key == ConsoleKey.C &&
        (info.Modifiers & ConsoleModifiers.Control) != 0)
    {
        Shutdown();
    }
}
```

### Character Input

```csharp
private void HandleKey(KeyPressed key)
{
    if (key.Text is not null)
    {
        Text += key.Text;
        return;
    }

    var ch = key.KeyInfo.KeyChar;
    if (ch != '\0' && !char.IsControl(ch))
    {
        Text += ch;
    }
}
```

`KeyPressed.Text` contains the text that the key press produces, when the terminal reports it.
The kitty keyboard protocol can report text with more than one UTF-16 character in one key event, for example an emoji or a letter with a combining accent.
`KeyInfo.KeyChar` contains only the first UTF-16 character of that text.

## Mouse Input

### Mouse Event Properties

```csharp
Input.OfType<MouseEvent>()
    .Subscribe(mouse =>
    {
        var x = mouse.X;           // Column position
        var y = mouse.Y;           // Row position
        var button = mouse.Button; // Which button
        var type = mouse.EventType;// Press, Release, etc.
        var mods = mouse.Modifiers;// Shift, Ctrl, Alt
    })
    .DisposeWith(Subscriptions);
```

### Mouse Buttons

| Button | Description |
|--------|-------------|
| `MouseButton.None` | No button |
| `MouseButton.Left` | Left click |
| `MouseButton.Right` | Right click |
| `MouseButton.Middle` | Middle button |
| `MouseButton.WheelUp` | Scroll up |
| `MouseButton.WheelDown` | Scroll down |

### Mouse Event Types

| Type | Description |
|------|-------------|
| `MouseEventType.Press` | Button pressed down |
| `MouseEventType.Release` | Button released |
| `MouseEventType.Drag` | Move while button held |
| `MouseEventType.Move` | Move without button |
| `MouseEventType.Scroll` | Wheel scrolled |

### Click Handling Example

```csharp
Input.OfType<MouseEvent>()
    .Where(m => m.EventType == MouseEventType.Press &&
                m.Button == MouseButton.Left)
    .Subscribe(HandleClick)
    .DisposeWith(Subscriptions);

private void HandleClick(MouseEvent mouse)
{
    if (IsInButtonBounds(mouse.X, mouse.Y))
    {
        OnButtonClicked();
    }
}
```

## Terminal Resize

```csharp
Input.OfType<ResizeEvent>()
    .Subscribe(resize =>
    {
        var width = resize.Width;   // New width in columns
        var height = resize.Height; // New height in rows

        // Layout is automatically recalculated
        // Use this for custom resize logic
    })
    .DisposeWith(Subscriptions);
```

## Mouse Wheel Scrolling

By default, mouse wheel events are detected via SGR mouse escape sequences and emitted as `MouseScrollEvent`:

```csharp
Input.OfType<MouseScrollEvent>()
    .Subscribe(scroll =>
    {
        // scroll.Delta: +1 = scroll up, -1 = scroll down
        if (scroll.Delta > 0)
            ScrollContentUp();
        else
            ScrollContentDown();
    })
    .DisposeWith(Subscriptions);
```

::: tip Automatic Routing
If the focused component implements `IScrollable` (like `StreamingTextNode`), mouse scroll events are routed directly to it — each tick scrolls 3 lines. The event only reaches `ViewModel.Input` if no `IScrollable` component has focus.
:::

### Alternate-Scroll Mode

Apps that want to preserve native terminal selection can opt into raw input plus alternate-scroll mode via `ConfigureRuntime()`.

In that configuration:

- wheel ticks arrive through the terminal's alternate-scroll path
- arrow keys remain keyboard input
- kitty keyboard reporting can further improve disambiguation on supported terminals

See [Terminal Input Modes](/concepts/terminal-input-modes) for the supported configuration and fallback behavior.

## Paste Events

Termina automatically enables [bracketed paste mode](https://en.wikipedia.org/wiki/Bracketed-paste) so multi-line pastes are received as a single `PasteEvent` instead of individual key presses:

```csharp
Input.OfType<PasteEvent>()
    .Subscribe(paste =>
    {
        // paste.Content contains the full text including newlines
        var lines = paste.Content.Split('\n');
    })
    .DisposeWith(Subscriptions);
```

::: tip Automatic Routing
If the focused component implements `IPasteReceiver` (like `TextInputNode`), paste events are routed directly to it. `TextInputNode` shows a summary placeholder and submits the full content on Enter. The event only reaches `ViewModel.Input` if no `IPasteReceiver` component has focus.
:::

## Clipboard Copy

Termina also supports terminal-native clipboard copy for read-only values through `IClipboardService` and components such as `CopyableTextNode`.

```csharp
var node = new CopyableTextNode(clipboardService, ViewModel.AuthUrl, toastService)
    .WithFeedbackMode(CopyFeedbackMode.Toast)
    .WithToastPosition(ToastPosition.TopRight);
```

`CopyableTextNode` handles its own keyboard interactions when focused:

- caret movement with `Left/Right`
- selection with `Shift+Left/Right`
- copy with configurable bindings such as `Enter` and `Ctrl+C`
- optional success feedback via toast, inline indicator, or both

Clipboard transport details live behind `IClipboardService`, so pages do not need to branch on `tmux`, `ssh`, or terminal type.

## Input Filtering with Rx

### Filter by Type

```csharp
Input.OfType<KeyPressed>()  // Only keyboard events
Input.OfType<MouseEvent>()  // Only mouse events
```

### Filter by Condition

```csharp
Input.OfType<KeyPressed>()
    .Where(k => k.KeyInfo.Key == ConsoleKey.Enter)
    .Subscribe(HandleEnter);
```

### Throttle Input

```csharp
Input.OfType<KeyPressed>()
    .Throttle(TimeSpan.FromMilliseconds(100))
    .Subscribe(HandleKey);
```

### Combine Streams

```csharp
var escPressed = Input.OfType<KeyPressed>()
    .Where(k => k.KeyInfo.Key == ConsoleKey.Escape);

var rightClick = Input.OfType<MouseEvent>()
    .Where(m => m.Button == MouseButton.Right);

Observable.Merge(escPressed.Select(_ => Unit.Default),
                 rightClick.Select(_ => Unit.Default))
    .Subscribe(_ => CloseMenu());
```

## Best Practices

### 1. Use Page-Level Bindings for Navigation

```csharp
// ✅ Good - Page intercepts Escape before any component
public override void OnNavigatedTo()
{
    KeyBindings.Register(ConsoleKey.Escape, () => Navigate("/"));
}

// ❌ Avoid - Component might consume Escape first
public override void OnActivated()
{
    Input.OfType<KeyPressed>()
        .Where(k => k.KeyInfo.Key == ConsoleKey.Escape)
        .Subscribe(_ => Navigate("/"));
}
```

### 2. Let Components Handle Their Own Input

```csharp
// ✅ Good - SelectionListNode handles its own navigation
Focus.PushFocus(_selectionList);  // Arrow keys, Enter work automatically

// ❌ Avoid - Manually routing input to components
Input.OfType<KeyPressed>()
    .Subscribe(k => _selectionList.HandleInput(k));
```

### 3. Use ViewModel.Input for State Changes

```csharp
// ✅ Good - ViewModel handles business logic keys
Input.OfType<KeyPressed>()
    .Where(k => k.KeyInfo.Key == ConsoleKey.F5)
    .Subscribe(_ => RefreshData());
```

## Source Code

::: details View PageKeyBindings
<<< @/../src/Termina/Input/PageKeyBindings.cs{csharp}
:::

::: details View KeyPressed
<<< @/../src/Termina/Input/KeyPressed.cs{csharp}
:::

::: details View MouseEvent
<<< @/../src/Termina/Input/MouseEvent.cs{csharp}
:::

::: details View ResizeEvent
<<< @/../src/Termina/Input/ResizeEvent.cs{csharp}
:::

::: details View MouseScrollEvent
<<< @/../src/Termina/Input/MouseScrollEvent.cs{csharp}
:::

::: details View PasteEvent
<<< @/../src/Termina/Input/PasteEvent.cs{csharp}
:::

::: details View IPasteReceiver
<<< @/../src/Termina/Input/IPasteReceiver.cs{csharp}
:::

::: details View IScrollable
<<< @/../src/Termina/Layout/IScrollable.cs{csharp}
:::
