# Dynamic Layouts

`DynamicLayoutNode` evaluates a factory on the first render. It evaluates the factory again after a call to `Invalidate()`.

`KeyedDynamicLayoutNode<TKey>` selects a child from a key after each invalidation.
The cache policy controls whether that child remains available after another key becomes active.

## Cache Policies

| Event | `RetainAll` (default) | `EvictOnKeyChange` |
|-------|-----------------------|--------------------|
| First use of key A | Create and cache child A | Create child A |
| Invalidate while key A stays active | Reuse child A | Reuse child A |
| Change from key A to key B | Retain child A and activate child B | Create child B, deactivate and retire child A, then activate child B |
| Return from key B to key A | Reuse child A with its prior state | Create a new child A with fresh state |
| Dispose the keyed layout | Dispose every cached child | Dispose the active child and any child that awaits deferred disposal |

`RetainAll` makes each key a stable child identity. Use it when a return must restore text, focus, selection, or scroll state.

`EvictOnKeyChange` makes a key transition a screen boundary. Use it when a return must build a fresh screen.

## Think of the Key as Screen Identity

Choose a key that changes only when the current child must become a different screen instance.

- With `RetainAll`, a key identifies one persistent child. A return to that key restores the child.
- With `EvictOnKeyChange`, an equal key keeps the active child. A different key replaces the child.

Do not include every view-model value in the key. A key that changes too often will reset text, selection, focus, and scroll state.

### Example: List and Editor Workflow

This page must keep editor state during validation updates. It must create a fresh editor after the user leaves and returns.

```csharp
private enum WorkflowScreen { List, Editor }

private WorkflowScreen _screen = WorkflowScreen.List;
private KeyedDynamicLayoutNode<WorkflowScreen> _content = null!;

public override ILayoutNode BuildLayout()
{
    _content = Layouts.KeyedDynamic(
        () => _screen,
        screen => screen switch
        {
            WorkflowScreen.List => BuildList(),
            WorkflowScreen.Editor => BuildEditor(),
            _ => Layouts.Empty()
        },
        KeyedDynamicCachePolicy.EvictOnKeyChange);

    return _content;
}

private void ShowEditor()
{
    _screen = WorkflowScreen.Editor;
    _content.Invalidate();
}

private void ShowList()
{
    _screen = WorkflowScreen.List;
    _content.Invalidate();
}
```

The editor child remains active while the key stays `Editor`. Its controls can process validation updates without a state reset.

The transition to `List` evicts the editor. A later transition to `Editor` calls `BuildEditor()` again.

Use a composite key when the same workflow state sometimes needs a forced reset:

```csharp
private int _editorRevision;

var content = Layouts.KeyedDynamic(
    () => (Screen: _screen, Revision: _screen == WorkflowScreen.Editor ? _editorRevision : 0),
    key => BuildScreen(key.Screen),
    KeyedDynamicCachePolicy.EvictOnKeyChange);
```

Increment `_editorRevision` only when the editor must discard its current controls and state.

## When to Use

| Scenario | Use |
|----------|-----|
| Tabs or wizard steps must keep state after a return | `Layouts.KeyedDynamic<TKey>()` with the default `RetainAll` policy |
| A workflow screen must be fresh after a return | `Layouts.KeyedDynamic<TKey>()` with `EvictOnKeyChange` |
| Observable-driven content updates | `ReactiveProperty<T>` + `.AsLayout()` or `factory.AsDynamicLayout(trigger)` |
| Advanced: custom factory with manual caching | `Layouts.Dynamic()` — low-level, you manage caching |

## Basic Usage

### KeyedDynamic (Recommended)

Use the default policy when each key must keep one child instance. The child keeps its text, selection, focus, and scroll state:

```csharp
public override ILayoutNode BuildLayout()
{
    var currentTab = 0;

    var layout = Layouts.KeyedDynamic(
        () => currentTab,
        tab => tab switch
        {
            0 => new TextNode("Welcome home"),
            1 => new TextNode("Settings panel"),
            _ => new TextNode("Unknown tab")
        });

    // Change tab and invalidate:
    currentTab = 1;
    layout.Invalidate();

    return layout;
}
```

### Evict on Key Change

Use `EvictOnKeyChange` when a workflow transition must discard the prior screen.
An invalidation with the same key still reuses the active child.

A different key deactivates and evicts the active child. A return to a prior key calls the factory again.

```csharp
var layout = Layouts.KeyedDynamic(
    () => currentStep,
    step => BuildStep(step),
    KeyedDynamicCachePolicy.EvictOnKeyChange);
```

Termina deactivates the replaced child during invalidation.
Termina disposes that child on the next measure or render pass, after the current input callback completes.

The keyed layout owns each child that its factory returns.
With `EvictOnKeyChange`, return a new child instance after every key change.
Use `Layouts.Deferred()` when the selected content is owned outside the keyed layout.

### Dynamic (Low-Level)

For cases where you need full control over the factory:

```csharp
var dynamicNode = Layouts.Dynamic(() => BuildCurrentContent());
// ... later, when state changes:
dynamicNode.Invalidate();
```

## Choosing the Right Dynamic Node

| Need | Use |
|------|-----|
| Tabs or wizard steps must keep state after a return | `Layouts.KeyedDynamic<TKey>()` with `RetainAll` |
| A workflow screen must be fresh after a return | `Layouts.KeyedDynamic<TKey>()` with `EvictOnKeyChange` |
| Observable-driven content updates | `observable.AsLayout()` or `factory.AsDynamicLayout(trigger)` |
| Advanced: custom factory with manual caching | `Layouts.Dynamic()` — low-level, you manage caching |

## With Invalidation Trigger

When your factory depends on state that changes over time, use `Invalidate()` or the `AsDynamicLayout` extension to trigger re-evaluation:

```csharp
// Manual invalidation
var dynamicNode = Layouts.Dynamic(() => BuildCurrentContent());
// ... later, when state changes:
dynamicNode.Invalidate();

// Or subscribe an observable trigger
Func<ILayoutNode> factory = () => BuildCurrentContent();
var node = factory.AsDynamicLayout(someObservable.Select(_ => Unit.Default));
```

## Before / After Comparison

**Before** (manual `Subject<Unit>` merge trick):

```csharp
// ViewModel
public ReactiveProperty<int> CurrentTab { get; } = new(0);

// Page
ViewModel.CurrentTab
    .Select<int, ILayoutNode>(tab => tab switch
    {
        0 => BuildHomeTab(),
        1 => BuildSettingsTab(),
        _ => new EmptyNode()
    })
    .AsLayout()
```

**After** (one-liner with `Layouts.KeyedDynamic`):

```csharp
// No ViewModel property needed — page-local state
var tab = 0;
var layout = Layouts.KeyedDynamic(
    () => tab,
    t => t switch
    {
        0 => BuildHomeTab(),
        1 => BuildSettingsTab(),
        _ => new EmptyNode()
    });
// Call layout.Invalidate() when tab changes
```

## How It Works

- The factory is called once on first `Measure()`/`Render()`, then only when `Invalidate()` is called
- `Invalidate()` eagerly evaluates the factory so the new child is immediately available for tree traversal (e.g., focus propagation)
- Under `RetainAll`, reference equality avoids lifecycle transitions when the node restores a cached child
- When the child changes: the old child is deactivated, the new child is activated (mirrors `ReactiveLayoutNode` behavior)
- The default `RetainAll` policy assigns one persistent child to each visited key
- The `EvictOnKeyChange` policy keeps the child only until the selected key changes
- `EvictOnKeyChange` deactivates the replaced child during invalidation
- `EvictOnKeyChange` disposes the replaced child on the next measure or render pass
- `GetChildNodes()` returns the current child for focus tree traversal
- Extends `LayoutNode` so fluent sizing (`.Fill()`, `.Width()`, `.Height()`) works

## Fluent Sizing

```csharp
Layouts.KeyedDynamic(() => currentTab, tab => BuildTab(tab))
    .Fill()        // Fill remaining height
    .WidthFill()   // Fill available width
```

::: tip
For page-level state that doesn't need to be in the ViewModel, `Layouts.KeyedDynamic()` avoids the overhead of creating a `ReactiveProperty<T>` and observable pipeline. For state that _should_ be in the ViewModel, use [Reactive Properties](../concepts/reactive-properties.md) instead.
:::
