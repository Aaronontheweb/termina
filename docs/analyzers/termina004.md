# TERMINA004: Stateful node recreation

## Summary

| Property | Value |
| --- | --- |
| Severity | Warning |
| Category | `Termina.State` |
| First known version | 0.16.0 |
| Code fix | No automatic fix; select a repair from this guide |

## Bad behavior

The rule reports a call to `Invalidate()` on a `DynamicLayoutNode` field when its factory creates a stateful layout node. The current source treats these types as stateful:

- `ScrollableContainerNode`
- `SelectionListNode<T>`
- `TextInputNode`
- `StreamingTextNode`

The analyzer also checks a private helper that the factory calls one level deep. It treats a `??=` or `??` fallback as a reuse pattern.

## Risk

`DynamicLayoutNode` runs its factory again when code calls `Invalidate()`. A new stateful node loses state such as scroll offset, selected index, input text, or cursor position. The user can lose their current UI position or input.

## Bad example

```csharp
using Termina.Layout;

public sealed class ResultsPage
{
    private DynamicLayoutNode? _content;

    public ILayoutNode BuildLayout()
    {
        _content = new DynamicLayoutNode(
            () => new ScrollableContainerNode());
        return _content;
    }

    public void Refresh()
        => _content?.Invalidate();
}
```

Each `Invalidate()` call creates a new `ScrollableContainerNode`. The new node does not keep the old scroll state.

## Choose the correct repair

`TERMINA004` identifies state loss, but it cannot determine the intended screen identity. Choose the repair that matches the page behavior:

| Required behavior | Repair |
| --- | --- |
| Keep one stateful child while the surrounding content updates | Create the child outside the factory, or cache it with `??=` |
| Update only a stateless or reactive section inside the stateful child | Put that section in a smaller dynamic node and invalidate that node |
| Restore each screen when its key becomes active again | Use `KeyedDynamicLayoutNode<TKey>` with the default `RetainAll` policy |
| Create a fresh screen after the user leaves and returns | Use `KeyedDynamicLayoutNode<TKey>` with `EvictOnKeyChange` |

### Reuse one child

Create the stateful node once and reuse it in the factory.

```csharp
using Termina.Layout;

public sealed class ResultsPage
{
    private DynamicLayoutNode? _content;
    private ScrollableContainerNode? _scroll;

    public ILayoutNode BuildLayout()
    {
        _content = new DynamicLayoutNode(() =>
        {
            _scroll ??= new ScrollableContainerNode();
            return _scroll;
        });
        return _content;
    }

    public void Refresh()
        => _content?.Invalidate();
}
```

This repair keeps the scroll container while the dynamic factory updates its surrounding content.

### Invalidate a smaller child

Keep the stateful container outside the broad factory. Put only the content that changes in a nested dynamic node.

```csharp
private ScrollableContainerNode? _scroll;
private DynamicLayoutNode? _rows;

public ILayoutNode BuildLayout()
{
    _rows = new DynamicLayoutNode(BuildRows);
    _scroll = new ScrollableContainerNode().WithContent(_rows);
    return _scroll;
}

public void RefreshRows()
    => _rows?.Invalidate();
```

This repair preserves the container state because the refresh replaces only the row content.

### Preserve each keyed screen

Use the default `RetainAll` policy when a return to a key must restore its previous controls and state.

```csharp
private int _selectedTab;
private KeyedDynamicLayoutNode<int>? _content;

public ILayoutNode BuildLayout()
{
    _content = Layouts.KeyedDynamic(
        () => _selectedTab,
        tab => BuildTab(tab));

    return _content;
}
```

Each visited tab keeps one child until the keyed layout receives disposal.

### Replace a keyed screen after a transition

Use `EvictOnKeyChange` when the active screen must stay stable, but a later return must create a fresh screen.

```csharp
private enum WorkflowScreen { List, Editor }

private WorkflowScreen _screen;
private KeyedDynamicLayoutNode<WorkflowScreen>? _content;

public ILayoutNode BuildLayout()
{
    _content = Layouts.KeyedDynamic(
        () => _screen,
        screen => BuildScreen(screen),
        KeyedDynamicCachePolicy.EvictOnKeyChange);

    return _content;
}
```

An invalidation with the same key keeps the active child. A key change retires that child and creates a new child.

See [Dynamic layouts](../concepts/dynamic-layouts) for the full policy lifecycle and key design guidance.

## Fix guidance

1. Decide what identifies one screen instance.
2. Decide whether a return must restore or replace that screen.
3. Select the narrowest repair from the table above.
4. Verify text, focus, selection, and scroll state across both refreshes and screen transitions.

## Code-fix status

No automatic code fix exists. The analyzer cannot infer:

- which application value identifies a screen
- whether a return to a prior key must restore or replace state
- whether the application can invalidate a smaller child
- which component owns each child and must dispose it

A partial code fix would handle only some source shapes and could select the wrong lifecycle. Use this page to make the decision explicitly.

## Suppression guidance

Do not suppress this rule when a factory creates a stateful node on every run. If the factory preserves state through another mechanism that the analyzer cannot detect, suppress the diagnostic at the `Invalidate()` call and document that mechanism:

```csharp
#pragma warning disable TERMINA004
_content?.Invalidate();
#pragma warning restore TERMINA004
```

Review the state-preservation mechanism when the factory changes.
