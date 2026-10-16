# Core Concepts

Termina is built on a few key concepts that work together to create reactive terminal UIs.

## The MVVM Pattern

Termina follows the Model-View-ViewModel (MVVM) pattern:

| Component | Role | Termina Class |
|-----------|------|---------------|
| **ViewModel** | State + Logic | `ReactiveViewModel` |
| **View** | UI Layout | `ReactivePage<TViewModel>` |
| **Model** | Data | Your domain objects |

## Key Components

### ViewModels

ViewModels hold your application state using `[Reactive]` properties:

```csharp
public partial class MyViewModel : ReactiveViewModel
{
    [Reactive] private int _count;  // Generates Count and CountChanged
}
```

### Pages

Pages build the UI layout and bind to ViewModel observables:

```csharp
public class MyPage : ReactivePage<MyViewModel>
{
    protected override ILayoutNode BuildLayout()
    {
        return ViewModel.CountChanged
            .Select(c => new TextNode($"Count: {c}"))
            .AsLayout();
    }
}
```

### Routing

Pages are registered with routes and parameters:

```csharp
builder.Services.AddTermina("/", termina =>
{
    termina.RegisterRoute<HomePage, HomeViewModel>("/");
    termina.RegisterRoute<DetailPage, DetailViewModel>("/items/{id:int}");
});
```

## Data Flow

```
Keyboard Input
      │
      ▼
Page (capture) → Focused Component (bubble) → ViewModel
      │                    │                       │
      └────────────────────┴───────────────────────┘
                           ↓
                  [Reactive] Property Change
                           ↓
                  Observable emits new value
                           ↓
                  Page re-renders region
                           ↓
                  ANSI Terminal Output
```

1. User presses a key
2. Page's `KeyBindings` checked first (capture phase)
3. If not handled, focused component receives input (bubble phase)
4. If not handled, ViewModel's `Input` observable receives event
5. State change updates a `[Reactive]` property
6. The property's `*Changed` observable emits
7. Page's reactive binding receives the value
8. Only the affected region re-renders

## Sections

- [Architecture](/concepts/architecture) - MVVM and reactive patterns
- [Reactive Properties](/concepts/reactive-properties) - The `[Reactive]` attribute
- [Observables & Rx](/concepts/observables) - Understanding `IObservable<T>`
- [Render Loop Threading](/concepts/render-loop-threading) - Marshaling async work onto the Termina loop
- [Source Generators](/concepts/source-generators) - How code generation works
- [Routing](/concepts/routing) - ASP.NET-style route templates
- [Navigation](/concepts/navigation) - Moving between pages
- [Input Handling](/concepts/input-handling) - Keyboard, mouse, and resize
- [Unicode Terminal Width](/concepts/unicode-terminal-width) - Terminal-cell measurement for CJK, emoji, and combining text
- [Presentation Modes](/concepts/presentation-modes) - Full-screen and primary-buffer application ownership
- [Hosting & DI](/concepts/hosting) - Integration with Microsoft.Extensions
