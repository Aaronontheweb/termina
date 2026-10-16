# TERMINA002: Layout nodes in ViewModels

## Summary

| Property | Value |
| --- | --- |
| Severity | Error |
| Category | `Termina.Architecture` |
| First known version | 0.3.0 |
| Code fix | None in the current source |

## Bad behavior

The rule reports a field or property that meets both conditions:

- The containing type derives from `ReactiveViewModel`.
- The field or property type implements `ILayoutNode`.

The rule reports fields and properties. It does not report local variables.

## Risk

A ViewModel should contain application state and business logic. A layout node is a UI component. When a ViewModel stores a layout node, the UI and application state share one type boundary. This reduces testability and breaks the MVVM separation.

## Bad example

```csharp
using Termina.Layout;
using Termina.Reactive;

public sealed class ChatViewModel : ReactiveViewModel
{
    private readonly TextNode _message = new("Ready");
}
```

`TextNode` implements `ILayoutNode`, so `TERMINA002` reports `_message`.

## Good example

Keep state in the ViewModel and create the layout node in the Page.

```csharp
using Termina.Layout;
using Termina.Reactive;

public sealed class ChatViewModel : ReactiveViewModel
{
    public string Message { get; set; } = "Ready";
}

public sealed class ChatPage
{
    private readonly ChatViewModel _viewModel = new();

    public ILayoutNode BuildLayout()
        => new TextNode(_viewModel.Message);
}
```

## Fix guidance

1. Remove the layout node field or property from the ViewModel.
2. Keep the data that the ViewModel needs to expose.
3. Create the layout node in the Page `BuildLayout()` method.
4. Let the Page observe ViewModel state and update the layout.

## Code-fix status

The current analyzer source defines a `DiagnosticAnalyzer`. It does not define a `CodeFixProvider`. No automatic code fix exists.

## Suppression guidance

Do not suppress this rule when the type stores a UI component by mistake. If the design has a reviewed exception, suppress the diagnostic at the smallest scope:

```csharp
#pragma warning disable TERMINA002
private readonly TextNode _message = new("Ready");
#pragma warning restore TERMINA002
```

Document the reason for the exception. A project-wide suppression hides later MVVM boundary errors.
