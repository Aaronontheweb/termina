# Navigation

Termina provides navigation actions within ViewModels to move between pages.

## Navigation Actions

ViewModels have access to protected navigation methods wired up by the framework:

### Navigate

Navigate directly by path:

```csharp
Navigate("/");           // Go to home
Navigate("/settings");   // Go to settings
Navigate("/items/42");   // Go to item 42
```

### NavigateWithParams

Navigate using a template with parameters:

```csharp
NavigateWithParams("/items/{id}", new { id = 42 });
NavigateWithParams("/users/{name}", new { name = "alice" });
```

### Shutdown

Request graceful application shutdown:

```csharp
Shutdown();  // Exits the application
```

## Navigation Example

```csharp
public partial class MenuViewModel : ReactiveViewModel
{
    [Reactive] private int _selectedIndex;

    public override void OnActivated()
    {
        Input.OfType<KeyPressed>()
            .Subscribe(HandleKey)
            .DisposeWith(Subscriptions);
    }

    private void HandleKey(KeyPressed key)
    {
        switch (key.KeyInfo.Key)
        {
            case ConsoleKey.Enter:
                NavigateToSelected();
                break;
            case ConsoleKey.Escape:
                Shutdown();
                break;
        }
    }

    private void NavigateToSelected()
    {
        var route = SelectedIndex switch
        {
            0 => "/counter",
            1 => "/todo",
            2 => "/settings",
            _ => "/"
        };
        Navigate(route);
    }
}
```

## Navigation Behavior

When registering routes, you can control how navigation behaves:

```csharp
termina.RegisterRoute<DetailPage, DetailViewModel>(
    "/items/{id}",
    NavigationBehavior.PreserveState);  // Keep ViewModel state
```

### ResetOnNavigation (Default)

- Creates new Page and ViewModel on each navigation
- State is reset each time
- Use for pages that should start fresh

### PreserveState

- Reuses existing Page and ViewModel instances
- State persists across navigations
- **Layout tree is preserved and reactivated**, not disposed
- ViewModel lifecycle:
  - `OnDeactivating()` disposes subscriptions
  - `OnActivated()` recreates subscriptions
- Layout lifecycle:
  - `OnDeactivate()` pauses timers, stops animations, pauses observable subscriptions
  - `OnActivate()` resumes timers, animations, and subscriptions
- Use for pages with expensive state, data caching, or complex UI state (e.g., form inputs, scroll positions)

## Going Back

Termina tracks navigation history automatically. A forward navigation pushes the current page onto the stack, and the back APIs pop it.

### Navigation History

When a page navigates to a different path, the current page is pushed onto the history stack:

```csharp
Navigate("/items/42");   // "/menu" is pushed onto history
Navigate("/settings");   // "/items/42" is pushed onto history
```

History rules:

- The concrete path is stored, so route parameters survive a back navigation.
- Navigating to the same path does not push a new entry.
- Going back restores the previous route without re-pushing the current page, so you never get a ping-pong loop between two pages.

### Host-Driven Back Navigation

The host owns the history. Call `TerminaApplication.GoBack()` to return to the previous route:

```csharp
if (termina.CanGoBack)
{
    termina.GoBack();
}
```

- `CanGoBack` is `true` when the history stack has at least one entry.
- `GoBack()` is a no-op when the history is empty. It does not throw and it does not shut the application down.
- Call `GoBack()` from host code when a destination page can be reached from more than one caller. The history stack returns the user to the route they actually came from, so nothing gets hard-coded.

### Page and ViewModel Patterns

Pages handle back navigation through the key-binding capture phase. Key bindings registered in `OnNavigatedTo` are checked before focused components receive input, so Escape can drive navigation even when a text input has focus:

```csharp
public override void OnNavigatedTo()
{
    base.OnNavigatedTo();

    // Fixed caller route: navigate back explicitly
    KeyBindings.Register(ConsoleKey.Escape, () => Navigate("/menu"));

    // Multi-caller route: raise an event the host subscribes to
    KeyBindings.Register(ConsoleKey.Escape, () => BackRequested?.Invoke());
}

// User-defined event, wired by host code to TerminaApplication.GoBack()
public event Action? BackRequested;
```

Two patterns apply:

1. **Fixed caller route.** When a page can only be reached from one place, register a key binding that calls `Navigate()` back to that route. This is the simplest pattern and needs no host wiring.
2. **Multi-caller route.** When a page can be reached from several routes, do not hard-code a caller route. Raise an event or callback that host code handles by calling `TerminaApplication.GoBack()`, which returns the user to wherever they actually came from.

ViewModels can do the same. Subscribe to `Input` for `KeyPressed` events, and expose a callback or event that host code wires to `GoBack()`.

### Nested State Consumes Escape First

Components with their own internal state consume Escape before page-level navigation runs. `WizardNode` is the reference example: Escape walks back through sub-steps and steps first, and `TryGoBack()` returns false when the wizard is already at its first step:

```csharp
if (wizard.TryGoBack())
{
    // Escape was consumed by the wizard's internal state
    return;
}

// Wizard is at its first step, so page-level back navigation applies
```

Follow this pattern in your own components: consume the key locally until the nested state is exhausted, then let the page (or host) handle application-level back navigation.

### NavigationBackRequested

`Termina.Navigation.NavigationBackRequested` is the framework event that requests a history-based back navigation. The application event loop handles it by calling `GoBack()`:

```csharp
case NavigationBackRequested:
    GoBack();
    return;
```

The event is mainly useful for custom input sources that map a global key (for example Ctrl+B) to back navigation. An input source pushes events into the application channel, so it can emit `NavigationBackRequested` just like it emits `KeyPressed`:

```csharp
public sealed class BackKeyInputSource : IInputSource
{
    public async Task RunAsync(ChannelWriter<object> writer, CancellationToken cancellationToken)
    {
        // Read keys from the terminal; when Ctrl+B is pressed:
        await writer.WriteAsync(new NavigationBackRequested(), cancellationToken);
    }
}
```

When no history exists, a `NavigationBackRequested` is ignored. The application does not shut down and nothing throws. Prefer calling `GoBack()` directly from host code. Reserve the event for input sources and integrations that push events into the application channel.

### Back Navigation and PreserveState

Going back to a page registered with `NavigationBehavior.PreserveState` reuses the cached page and ViewModel instances. The lifecycle methods still run:

- `OnDeactivating()` runs on the page being left.
- `OnActivated()` runs on the restored page when it becomes active again.

State kept in `ReactiveProperty` fields survives the round trip, so a form partially filled in before navigating away is intact when the user comes back.

## Lifecycle Methods

### OnActivated

Called when navigating to the page:

```csharp
public override void OnActivated()
{
    // Setup subscriptions
    Input.OfType<KeyPressed>()
        .Subscribe(HandleKey)
        .DisposeWith(Subscriptions);

    // Load data
    LoadItems();
}
```

### OnDeactivating

Called when navigating away:

```csharp
public override void OnDeactivating()
{
    // Subscriptions are auto-disposed
    base.OnDeactivating();  // Important: call base

    // Optional: save state, cancel operations
    SaveDraft();
}
```

## RequestRedraw

For asynchronous content updates (like streaming), request a UI refresh:

```csharp
private async Task StreamDataAsync()
{
    await foreach (var chunk in dataStream)
    {
        Messages = Messages.Append(chunk).ToList();
        RequestRedraw();  // Trigger UI update
    }
}
```

## ViewModel Source Code

::: details View ReactiveViewModel implementation
<<< @/../src/Termina/Reactive/ReactiveViewModel.cs{csharp}
:::
