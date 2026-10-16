# StreamingTextNode

Displays streaming text content with automatic scrolling and word wrapping. Ideal for chat interfaces, logs, and LLM output.

![StreamingTextNode demo: a chat response streaming in token-by-token](/gallery/streaming-chat.gif)

*`StreamingTextNode` rendering a chat response as it streams.*

## Basic Usage

```csharp
// Create with persisted buffer (retains all content)
var stream = StreamingTextNode.Create();

// Append content
stream.AppendLine("User: Hello!");
stream.AppendLine("Bot: Hi there!");

// Append streaming content (character by character)
stream.Append("Thinking");
stream.Append(".");
stream.Append(".");
stream.Append(".");
```

## Buffer Types

### Persisted Buffer (Default)

Retains all content with scrolling support:

```csharp
var stream = StreamingTextNode.Create();

// Supports scrolling
stream.ScrollUp(5);
stream.ScrollDown(5);
stream.ScrollToBottom();
```

### Windowed Buffer

Rolling buffer that retains only recent lines:

```csharp
// Keep last 100 lines
var stream = StreamingTextNode.CreateWindowed(windowSize: 100);
```

## Line Prefixes

Add prefixes to each line (useful for chat):

```csharp
var userStream = StreamingTextNode.Create()
    .WithPrefix("You: ", Color.Cyan);

var botStream = StreamingTextNode.Create()
    .WithPrefix("Bot: ", Color.Green);
```

## Styling

### Node-Level Styling

Set default colors for the entire node:

```csharp
StreamingTextNode.Create()
    .WithForeground(Color.White)
    .WithBackground(Color.Black)
    .WithPrefix("> ", Color.Gray)
```

### Inline Styled Text

Append text with inline colors and text decorations:

```csharp
var stream = StreamingTextNode.Create();

// Colored text
stream.Append("Error: ", foreground: Color.Red);
stream.AppendLine("Something went wrong", foreground: Color.Yellow);

// With background
stream.Append("Highlighted", foreground: Color.Black, background: Color.Yellow);

// With decorations
stream.Append("Bold text", decoration: TextDecoration.Bold);
stream.Append("Italic text", decoration: TextDecoration.Italic);

// Combined decorations
stream.Append("Bold and underlined",
    foreground: Color.Cyan,
    decoration: TextDecoration.Bold | TextDecoration.Underline);
```

### Text Decorations

The following decorations are available:

| Decoration | Description |
|------------|-------------|
| `TextDecoration.None` | No decoration (default) |
| `TextDecoration.Bold` | Bold/bright text |
| `TextDecoration.Dim` | Dimmed text |
| `TextDecoration.Italic` | Italic text |
| `TextDecoration.Underline` | Underlined text |
| `TextDecoration.Strikethrough` | Strikethrough text |

Decorations can be combined using bitwise OR:

```csharp
var style = TextDecoration.Bold | TextDecoration.Italic | TextDecoration.Underline;
stream.Append("All styles", decoration: style);
```

### Using StyledSegment

For more control, use `StyledSegment` directly:

```csharp
var style = new TextStyle(
    foreground: Color.Green,
    background: Color.Default,
    decoration: TextDecoration.Bold
);

stream.Append(new StyledSegment("Success!", style));
```

### Style Precedence

Inline styles override node-level defaults:

```csharp
var stream = StreamingTextNode.Create()
    .WithForeground(Color.White);  // Default white

stream.AppendLine("This is white");  // Uses default
stream.AppendLine("This is red", foreground: Color.Red);  // Override
stream.AppendLine("Back to white");  // Uses default again
```

### Real-World Chat Example

Building a rich chat interface with styled messages:

```csharp
var chat = StreamingTextNode.Create();

// Tool invocation
chat.Append("○ ", foreground: Color.Yellow);
chat.Append("search_web", foreground: Color.Cyan);
chat.AppendLine("(\"latest news\")", foreground: Color.BrightBlack);

// Error message
chat.Append("Error: ", foreground: Color.Red, decoration: TextDecoration.Bold);
chat.AppendLine("Network timeout", foreground: Color.Red);

// Success message
chat.Append("✓ ", foreground: Color.Green);
chat.AppendLine("Task completed", foreground: Color.Green);

// Streaming LLM response with mixed styles
chat.Append("Assistant: ", foreground: Color.Blue, decoration: TextDecoration.Bold);
await foreach (var token in llmResponse)
{
    chat.Append(token);  // Plain text for LLM output
}
chat.AppendLine("");
```

::: tip Why Structured API Instead of Markup?
Termina uses a structured API (not markup like `[red]text[/red]`) because:
- **Safe for LLM output** - No need to escape user content or AI responses
- **Type-safe** - IDE autocompletion and compile-time checking
- **Composable** - Build styled segments programmatically
:::

### Style Preservation in Word Wrap

Styles are automatically preserved when text wraps to multiple lines:

```csharp
// Long styled text that wraps
stream.Append(
    "This is a long red message that will automatically wrap to multiple lines while preserving the red color across all wrapped lines.",
    foreground: Color.Red
);
```

Background highlights stay contiguous across a wrap. The space that joins two words on a wrapped line takes the style of the whitespace that separated them in the source. A highlighted phrase keeps its background across the whole phrase, and an unstyled gap between two highlighted words does not pick up a background.

```csharp
// The yellow highlight covers the whole phrase, including the spaces between words.
stream.Append(
    "Highlighted message that wraps",
    foreground: Color.Black,
    background: Color.Yellow
);
```

## Text Composition and Tracked Segments

StreamingTextNode supports **tracked segments** - text elements that can be independently animated and later removed or replaced. This enables inline animations like spinners, progress bars, timers, and other dynamic content that appears alongside static text.

### Concepts

**Untracked vs Tracked Content:**
- **Untracked** (default): Regular `Append()` and `AppendLine()` calls create immutable content with zero overhead
- **Tracked**: `AppendTracked()` creates mutable segments that can be animated, removed, or replaced

**Segment Types:**

| Type | Interface | Purpose |
|------|-----------|---------|
| `StaticTextSegment` | `ITextSegment` | Static trackable text that can be removed/replaced |
| `SpinnerSegment` | `IAnimatedTextSegment` | Animated spinner with multiple styles |
| Custom implementations | `ITextSegment` or `IAnimatedTextSegment` | Your own tracked segments |

### Caller-Provided IDs

Like HTML elements with `id` attributes, tracked segments use caller-provided IDs. You choose the ID upfront:

```csharp
// Define your segment IDs
private static readonly SegmentId SpinnerId = new(1);
private static readonly SegmentId TimerId = new(2);
private static readonly SegmentId ProgressId = new(3);

// Use them for tracking
stream.AppendTracked(SpinnerId, new SpinnerSegment());
```

This eliminates the need to capture and track returned IDs - you control the identifiers.

### Static Tracked Segments

Use `StaticTextSegment` when you need to track non-animated text for later removal or replacement:

```csharp
var stream = StreamingTextNode.Create();

// Append tracked static text
var placeholder = new StaticTextSegment("[loading...]", Color.Gray);
stream.AppendTracked(new SegmentId(1), placeholder);

// Later, replace with actual content
stream.Replace(
    new SegmentId(1),
    new StaticTextSegment("Operation complete!"),
    keepTracked: false  // Convert to untracked
);
```

### Animated Segments

`SpinnerSegment` provides inline animated spinners with 6 styles:

```csharp
using Termina.Components.Streaming;

var stream = StreamingTextNode.Create();

// Append "Thinking: " followed by animated spinner
stream.Append("Thinking: ");
var spinner = new SpinnerSegment(
    SpinnerStyle.Dots,  // ⠋ ⠙ ⠹ ⠸ ⠼ ⠴ ⠦ ⠧ ⠇ ⠏
    Color.Yellow,
    intervalMs: 80
);
stream.AppendTracked(new SegmentId(1), spinner);
```

**Available Spinner Styles:**

| Style | Animation Frames |
|-------|-----------------|
| `SpinnerStyle.Dots` | ⠋ ⠙ ⠹ ⠸ ⠼ ⠴ ⠦ ⠧ ⠇ ⠏ |
| `SpinnerStyle.Line` | - \\ \| / |
| `SpinnerStyle.Arrow` | ← ↖ ↑ ↗ → ↘ ↓ ↙ |
| `SpinnerStyle.Bounce` | ⠁ ⠂ ⠄ ⠂ |
| `SpinnerStyle.Box` | ▖ ▘ ▝ ▗ |
| `SpinnerStyle.Circle` | ◐ ◓ ◑ ◒ |

### Operations on Tracked Segments

```csharp
var stream = StreamingTextNode.Create();
var spinnerId = new SegmentId(1);

// 1. Append a tracked segment
var spinner = new SpinnerSegment(SpinnerStyle.Dots, Color.Red);
stream.AppendTracked(spinnerId, spinner);

// 2. Remove the tracked segment
stream.Remove(spinnerId);

// 3. Replace with another tracked segment (stays tracked)
stream.Replace(spinnerId, new SpinnerSegment(SpinnerStyle.Arrow), keepTracked: true);

// 4. Replace with untracked content (stops tracking, frees ID)
stream.Replace(
    spinnerId,
    new StaticTextSegment("Done!"),
    keepTracked: false
);
```

### ID Reuse

When `keepTracked: false`, the segment ID is freed and can be reused:

```csharp
private static readonly SegmentId ThinkingSpinnerId = new(1);

// First use
stream.AppendTracked(ThinkingSpinnerId, new SpinnerSegment(...));
// ... later ...
stream.Replace(ThinkingSpinnerId, new StaticTextSegment(""), keepTracked: false);

// ID is now free, can reuse for next operation
stream.AppendTracked(ThinkingSpinnerId, new SpinnerSegment(...));
```

### Real-World Example: Chat with Thinking Indicator

A complete example showing a spinner that appears while waiting for an LLM response:

```csharp
public class ChatViewModel : ReactiveViewModel
{
    private static readonly SegmentId ThinkingSpinnerId = new(1);
    private SpinnerSegment? _currentSpinner;

    public void HandleUserMessage(string message)
    {
        // Show user message
        ChatHistory.Append("You: ", Color.Cyan, decoration: TextDecoration.Bold);
        ChatHistory.AppendLine(message);
        ChatHistory.AppendLine("");

        // Show "Assistant: " with inline spinner
        ChatHistory.Append("Assistant: ", Color.Green, decoration: TextDecoration.Bold);
        _currentSpinner = new SpinnerSegment(SpinnerStyle.Dots, Color.Red);
        ChatHistory.AppendTracked(ThinkingSpinnerId, _currentSpinner);

        // Start async response
        _ = StreamResponseAsync();
    }

    private async Task StreamResponseAsync()
    {
        var firstChunk = true;

        await foreach (var token in GetLlmResponse())
        {
            // On first text, replace spinner with empty content
            if (firstChunk)
            {
                ChatHistory.Replace(
                    ThinkingSpinnerId,
                    new StaticTextSegment(""),
                    keepTracked: false
                );
                _currentSpinner?.Dispose();
                _currentSpinner = null;
                firstChunk = false;
            }

            // Append LLM text
            ChatHistory.Append(token);
        }

        ChatHistory.AppendLine("");
        ChatHistory.AppendLine("");
    }
}
```

### Custom Animated Segments

Implement `IAnimatedTextSegment` to create custom animations:

```csharp
public class BlinkSegment : IAnimatedTextSegment
{
    private readonly Timer _timer;
    private readonly Subject<Unit> _invalidated = new();
    private bool _visible = true;
    private readonly string _text;
    private readonly TextStyle _style;

    public BlinkSegment(string text, Color color, int intervalMs = 500)
    {
        _text = text;
        _style = new TextStyle(color, Color.Default, TextDecoration.None);
        _timer = new Timer(intervalMs);
        _timer.Elapsed += (_, _) =>
        {
            _visible = !_visible;
            _invalidated.OnNext(Unit.Default);
        };
        Start();
    }

    public IObservable<Unit> Invalidated => _invalidated;
    public bool IsAnimating => _timer.Enabled;

    public StyledSegment GetCurrentSegment()
    {
        return _visible
            ? new StyledSegment(_text, _style)
            : new StyledSegment(new string(' ', _text.Length), _style);
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
    }
}

// Usage
var blink = new BlinkSegment("URGENT", Color.Red);
stream.AppendTracked(new SegmentId(1), blink);
```

## Block-Level Segments

Block-level segments force content to start on a new line and wrap vertically within their container. This is ideal for content that should update in-place without pushing surrounding text around, such as LLM "thinking" indicators or status blocks.

### The `.AsBlock()` Extension

Any `ITextSegment` can be wrapped as a block using the `.AsBlock()` fluent API:

```csharp
using Termina.Components.Streaming;

var stream = StreamingTextNode.Create();

// Regular inline content
stream.Append("Status: ");

// Block-level content (starts on new line)
var statusBlock = new StaticTextSegment(
    "Processing...",
    new TextStyle { Foreground = Color.Yellow }
).AsBlock();

stream.AppendTracked(new SegmentId(1), statusBlock);
```

### How Block Segments Work

When you append a `BlockSegment`:
1. If the current line has content, a newline is automatically inserted
2. The block content renders on its own line
3. The content wraps vertically to fit the container width
4. Surrounding content is not pushed when the block updates

This creates a "window within a window" effect where the block updates in-place.

### Real-World Example: LLM Thinking Indicator

From the streaming chat demo, showing how thinking blocks update in-place:

```csharp
public partial class StreamingChatViewModel : ReactiveViewModel
{
    private static readonly SegmentId ThinkingSpinnerId = new(1);
    private static readonly SegmentId ThinkingBlockId = new(2);
    private bool _thinkingBlockShown;

    private async Task ConsumeResponseStreamAsync()
    {
        await foreach (var token in response.TokenStream)
        {
            switch (token)
            {
                case LlmMessages.ThinkingToken thinking:
                    // On first thinking token, replace spinner with thinking block
                    if (!_thinkingBlockShown)
                    {
                        _chatOutput.OnNext(new ReplaceTrackedSegment(
                            ThinkingSpinnerId,
                            new StaticTextSegment("", TextStyle.Default),
                            KeepTracked: false));

                        // Add thinking block that updates in place
                        var thinkingSegment = new StaticTextSegment(
                            $"💭 {thinking.Text}",
                            new TextStyle
                            {
                                Foreground = Color.BrightBlack,
                                Decoration = TextDecoration.Italic
                            }).AsBlock();

                        _chatOutput.OnNext(new AppendTrackedSegment(ThinkingBlockId, thinkingSegment));
                        _thinkingBlockShown = true;
                    }
                    else
                    {
                        // Update existing thinking block in-place
                        var thinkingSegment = new StaticTextSegment(
                            $"💭 {thinking.Text}",
                            new TextStyle
                            {
                                Foreground = Color.BrightBlack,
                                Decoration = TextDecoration.Italic
                            }).AsBlock();

                        _chatOutput.OnNext(new ReplaceTrackedSegment(
                            ThinkingBlockId,
                            thinkingSegment,
                            KeepTracked: true));
                    }
                    break;

                case LlmMessages.TextChunk chunk:
                    // Remove thinking block when actual content arrives
                    if (!HasReceivedText)
                    {
                        _chatOutput.OnNext(new RemoveTrackedSegment(ThinkingBlockId));
                        HasReceivedText = true;
                    }
                    _chatOutput.OnNext(new AppendText(chunk.Text));
                    break;
            }
        }
    }
}
```

In this example:
- The thinking text (like "💭 Analyzing the question..." or "💭 Formulating response...") updates every ~250ms
- The block stays on its own line, wrapping vertically if needed
- When actual response text arrives, the thinking block is removed without leaving a gap
- The surrounding content (previous messages, following response) stays in place

### Block Segments with Animation

Block segments work seamlessly with animated segments:

```csharp
// Animated spinner as a block
var spinnerBlock = new SpinnerSegment(
    SpinnerStyle.Dots,
    Color.Yellow
).AsBlock();

stream.AppendTracked(new SegmentId(1), spinnerBlock);

// The spinner animates on its own line
// Updates don't affect surrounding content
```

### Use Cases

- **LLM Thinking Indicators**: Show intermediate "thinking" steps that update in-place
- **Multi-line Status Blocks**: Status information that needs 2-3 lines and updates frequently
- **In-place Progress Indicators**: Progress that wraps across multiple lines
- **Temporary Notifications**: Alerts that appear, update, then disappear without disrupting flow
- **Live Metrics**: Real-time stats that update in a fixed block without scrolling

### Performance Considerations

- **Untracked appends**: O(1), zero overhead
- **Tracked appends**: O(1), minimal tracking overhead
- **Remove/Replace**: O(n) buffer rebuild, but only when mutating
- **Animation frames**: No rebuild, just invalidation for redraw
- **Block segments**: Same performance as regular segments, just with automatic newline insertion

Most content should be untracked. Only use tracked segments for dynamic elements that need mutation.

### General Tracked Segment Use Cases

- **Spinners**: Loading indicators while waiting for async operations
- **Timers**: Countdown or elapsed time displays
- **Progress bars**: Text-based progress indicators
- **Blinking text**: Attention-grabbing alerts
- **Status indicators**: Live updating connection status, typing indicators
- **Placeholders**: Temporary content replaced when data loads

## Scrollbar

`StreamingTextNode` can display a visual scrollbar to indicate the current scroll position within the content. The scrollbar auto-hides when all content fits in the viewport.

### Basic Scrollbar

```csharp
var stream = StreamingTextNode.Create()
    .WithScrollbar();  // Enable with default options
```

### Custom Scrollbar

```csharp
var stream = StreamingTextNode.Create()
    .WithScrollbar(new ScrollbarOptions(
        TrackChar: '│',         // Default: '░'
        ThumbChar: '┃',         // Default: '█'
        TrackColor: Color.BrightBlack,  // Default: BrightBlack
        ThumbColor: Color.Cyan,         // Default: White
        AutoHide: true          // Default: true (hide when content fits)
    ));
```

### ScrollbarOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TrackChar` | `char` | `'░'` | Character for the track (non-thumb area) |
| `ThumbChar` | `char` | `'█'` | Character for the thumb (position indicator) |
| `TrackColor` | `Color?` | `BrightBlack` | Track foreground color |
| `ThumbColor` | `Color?` | `White` | Thumb foreground color |
| `AutoHide` | `bool` | `true` | Hide scrollbar when content fits in viewport |

The scrollbar occupies 1 column on the right edge, reducing the content area width by 1. When `AutoHide` is `true` (the default), the full width is available until content exceeds the viewport.

## Mouse Wheel Scrolling

`StreamingTextNode` implements `IScrollable`, which enables automatic mouse wheel scroll support. When a `StreamingTextNode` has focus, mouse wheel events are routed to it automatically — no additional code is needed.

By default, Termina captures wheel input using legacy mouse tracking. Apps that want wheel scrolling without taking over native terminal selection can opt into raw input plus alternate-scroll mode. See [Terminal Input Modes](/concepts/terminal-input-modes).

```csharp
// Mouse wheel scrolling works automatically when the node has focus
var stream = StreamingTextNode.Create()
    .WithScrollbar();  // Visual indicator of scroll position

// The framework routes MouseScrollEvent to IScrollable.ScrollUp/ScrollDown
// Each wheel tick scrolls 3 lines
```

### Scroll State

Query the current scroll state programmatically:

```csharp
var canUp = stream.CanScrollUp;    // More content above?
var canDown = stream.CanScrollDown; // More content below?
```

### IScrollable Interface

Any component that implements `IScrollable` automatically receives mouse wheel events when focused:

```csharp
public interface IScrollable
{
    bool CanScrollUp { get; }
    bool CanScrollDown { get; }
    void ScrollUp(int lines = 1);
    void ScrollDown(int lines = 1);
}
```

## Scrolling API

For persisted buffers:

```csharp
var stream = StreamingTextNode.Create();

// Manual scrolling
stream.ScrollUp(lines: 5, viewportWidth: 80);
stream.ScrollDown(lines: 5);
stream.ScrollToBottom();

// Handle keyboard scrolling
stream.HandleInput(keyInfo, viewportHeight: 20, viewportWidth: 80);
```

### Scroll Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `PageUp` | Scroll up one page |
| `PageDown` | Scroll down one page |
| `Ctrl+Home` | Scroll to top |
| `Ctrl+End` | Scroll to bottom |
| Mouse wheel | Scroll 3 lines per tick |

## Real-World Example

From the streaming chat demo:

```csharp
public partial class ChatViewModel : ReactiveViewModel
{
    public StreamingTextNode ChatHistory { get; }

    public ChatViewModel()
    {
        ChatHistory = StreamingTextNode.Create();
    }

    private async Task HandleResponse(IAsyncEnumerable<string> tokens)
    {
        ChatHistory.AppendLine("");
        ChatHistory.Append("Bot: ");

        await foreach (var token in tokens)
        {
            ChatHistory.Append(token);
        }

        ChatHistory.AppendLine("");
    }
}
```

## API Reference

### Factory Methods

```csharp
// Persisted buffer (all content retained)
StreamingTextNode.Create()

// Windowed buffer (rolling window)
StreamingTextNode.CreateWindowed(windowSize: 100)
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Buffer` | `IStreamingTextBuffer` | Underlying buffer |
| `Foreground` | `Color?` | Text color |
| `Background` | `Color?` | Background color |
| `Prefix` | `string?` | Line prefix |
| `PrefixColor` | `Color?` | Prefix color |
| `ContentChanged` | `IObservable<Unit>` | Content change notifications |
| `CanScrollUp` | `bool` | Whether content exists above viewport |
| `CanScrollDown` | `bool` | Whether content exists below viewport |

### Methods

#### Untracked Content

| Method | Description |
|--------|-------------|
| `.Append(string)` | Append plain text |
| `.Append(string, foreground?, background?, decoration?)` | Append styled text |
| `.Append(StyledSegment)` | Append a styled segment |
| `.AppendLine(string)` | Append plain text line |
| `.AppendLine(string, foreground?, background?, decoration?)` | Append styled line |
| `.Clear()` | Clear all content (tracked and untracked) |

#### Tracked Segments

| Method | Description |
|--------|-------------|
| `.AppendTracked(SegmentId, ITextSegment)` | Append tracked segment with caller-provided ID |
| `.Remove(SegmentId)` | Remove tracked segment by ID |
| `.Replace(SegmentId, ITextSegment, keepTracked)` | Replace segment; `keepTracked: false` converts to untracked |

#### Scrolling

| Method | Description |
|--------|-------------|
| `.ScrollUp(lines, width)` | Scroll up |
| `.ScrollDown(lines)` | Scroll down |
| `.ScrollToBottom()` | Jump to bottom |
| `.HandleInput(key, height, width)` | Handle scroll keys |

#### Configuration

| Method | Description |
|--------|-------------|
| `.WithForeground(Color)` | Set default text color |
| `.WithBackground(Color)` | Set default background color |
| `.WithPrefix(string, Color?)` | Set line prefix |
| `.WithScrollbar()` | Enable scrollbar with default options |
| `.WithScrollbar(ScrollbarOptions)` | Enable scrollbar with custom options |

## Source Code

::: details View StreamingTextNode implementation
<<< @/../src/Termina/Layout/StreamingTextNode.cs{csharp}
:::
