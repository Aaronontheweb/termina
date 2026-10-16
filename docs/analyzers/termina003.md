# TERMINA003: Layout node child disposal

## Summary

| Property | Value |
| --- | --- |
| Severity | Warning |
| Category | `Termina.Lifecycle` |
| First known version | 0.16.0 |
| Code fix | None in the current source |

## Bad behavior

The rule reports `Dispose()` on a field or property that implements `ILayoutNode` when all conditions below apply:

- The access uses the current container, such as `_child`, `this._child`, or `base._child`.
- The containing type derives from `LayoutNode`.
- The call is outside the container teardown.

The rule does not report disposal in `Dispose()`, `DisposeAsync()`, `Dispose(bool)`, or a finalizer. It also does not report locals, parameters, collection elements, or members of another object.

## Risk

Termina uses an active and inactive lifecycle for layout nodes. `Dispose()` destroys a child. A destroyed child cannot render or handle input. A content switch should deactivate the old child and keep it available for later use or final cleanup.

## Bad example

```csharp
using Termina.Layout;

public sealed class ContentContainer : LayoutNode
{
    private ContentNode? _currentChild;

    public void SwitchTo(ContentNode next)
    {
        _currentChild?.Dispose();
        _currentChild = next;
    }
}
```

`TERMINA003` reports the `Dispose()` call because `SwitchTo` is not teardown.

## Good example

Call `OnDeactivate()` when the container switches content. Dispose the child in the container teardown.

```csharp
using Termina.Layout;

public sealed class ContentContainer : LayoutNode
{
    private ContentNode? _currentChild;

    public void SwitchTo(ContentNode next)
    {
        _currentChild?.OnDeactivate();
        _currentChild = next;
    }

    public override void Dispose()
    {
        _currentChild?.Dispose();
        base.Dispose();
    }
}
```

## Fix guidance

1. Use `OnDeactivate()` when the old child leaves the active content.
2. Keep the child reference if the container can reuse it.
3. Dispose the child in the container's final teardown.
4. Confirm that the child implements the activation lifecycle before you call `OnDeactivate()`.

## Code-fix status

The current analyzer source defines a `DiagnosticAnalyzer`. It does not define a `CodeFixProvider`. No automatic code fix exists.

## Suppression guidance

Do not suppress this rule to keep a content switch simple. If disposal is intentional and the lifecycle contract allows it, suppress the diagnostic at the call site and document why disposal is safe:

```csharp
#pragma warning disable TERMINA003
_currentChild?.Dispose();
#pragma warning restore TERMINA003
```

Do not use suppression to bypass final cleanup guidance.
