# Termina

![Termina Logo](https://raw.githubusercontent.com/Aaronontheweb/termina/refs/heads/dev/assets/termina-icon.png)

[![NuGet Downloads](https://img.shields.io/nuget/dt/Termina)](https://www.nuget.org/packages/Termina) ![GitHub License](https://img.shields.io/github/license/Aaronontheweb/termina) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/Aaronontheweb/termina/pr_validation.yml) ![GitHub Release](https://img.shields.io/github/v/release/Aaronontheweb/termina)

**Termina** is a reactive terminal UI (TUI) framework for .NET with declarative layouts and surgical region-based rendering. It provides an MVVM architecture with reactive properties, ASP.NET Core-style routing, and seamless integration with Microsoft.Extensions.Hosting.

## See It In Action

![Termina component gallery: keyboard-driven menus, live spinners, and selection lists](https://raw.githubusercontent.com/Aaronontheweb/termina/refs/heads/dev/docs/public/gallery/hero-overview.gif)

*A guided tour of the Termina component gallery.*

## Documentation

**[Full Documentation](https://aaronontheweb.github.io/termina/)**

- [Getting Started Guide](https://aaronontheweb.github.io/termina/guide/getting-started)
- [Tutorials](https://aaronontheweb.github.io/termina/tutorials/)
- [Component Reference](https://aaronontheweb.github.io/termina/components/)
- [Architecture](https://aaronontheweb.github.io/termina/concepts/architecture)

## Features

- **Reactive MVVM Architecture** - ViewModels with `ReactiveProperty<T>` for observable state management
- **Declarative Layouts** - Tree-based layout system with size constraints (Fixed, Fill, Auto, Percent)
- **Surgical Rendering** - Only changed regions re-render, enabling smooth streaming updates
- **ASP.NET Core-Style Routing** - Route templates with parameters (`/tasks/{id:int}`) and type constraints
- **Source Generators** - AOT-compatible code generation for route parameters
- **Streaming Support** - Native `StreamingTextNode` for real-time content like LLM output
- **Dependency Injection** - Full integration with `Microsoft.Extensions.DependencyInjection`
- **Hosting Integration** - Works with `Microsoft.Extensions.Hosting` for clean lifecycle management
- **Presentation Modes** - Supports the default full-screen mode and an opt-in primary-buffer mode

## Installation

```bash
dotnet add package Termina
dotnet add package Microsoft.Extensions.Hosting
```

> **Upgrading to 0.7.0?** This release migrates from System.Reactive to R3 with breaking API changes. See the [Migration Guide](https://aaronstannard.com/termina/guide/migration-0.7.html) for details.

## Quick Start

### 1. Define a ViewModel

```csharp
using R3;
using Termina.Input;
using Termina.Reactive;

public class CounterViewModel : ReactiveViewModel
{
    public ReactiveProperty<int> Count { get; } = new(0);
    public ReactiveProperty<string> Message { get; } = new("Press Up/Down to change count");

    public override void OnActivated()
    {
        Input.OfType<IInputEvent, KeyPressed>()
            .Subscribe(HandleKey)
            .DisposeWith(Subscriptions);
    }

    private void HandleKey(KeyPressed key)
    {
        switch (key.KeyInfo.Key)
        {
            case ConsoleKey.UpArrow:
                Count.Value++;
                Message.Value = $"Count: {Count.Value}";
                break;
            case ConsoleKey.DownArrow:
                Count.Value--;
                Message.Value = $"Count: {Count.Value}";
                break;
            case ConsoleKey.Escape:
                Shutdown();
                break;
        }
    }

    public override void Dispose()
    {
        Count.Dispose();
        Message.Dispose();
        base.Dispose();
    }
}
```

`ReactiveProperty<T>` is both a value holder and an `Observable<T>` — subscribe directly in your Page for reactive UI bindings.

### 2. Define a Page

```csharp
using R3;
using Termina.Extensions;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

public class CounterPage : ReactivePage<CounterViewModel>
{
    public override ILayoutNode BuildLayout()
    {
        return Layouts.Vertical()
            .WithChild(
                new PanelNode()
                    .WithTitle("Counter Demo")
                    .WithBorder(BorderStyle.Rounded)
                    .WithBorderColor(Color.Cyan)
                    .WithContent(
                        ViewModel.Count
                            .Select<int, ILayoutNode>(count => new TextNode($"Count: {count}")
                                .WithForeground(Color.BrightCyan))
                            .AsLayout())
                    .Height(5))
            .WithChild(
                ViewModel.Message
                    .Select<string, ILayoutNode>(msg => new TextNode(msg))
                    .AsLayout()
                    .Height(1));
    }
}
```

### 3. Configure and Run

```csharp
using Microsoft.Extensions.Hosting;
using Termina.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTermina("/counter", termina =>
{
    termina.RegisterRoute<CounterPage, CounterViewModel>("/counter");
});

await builder.Build().RunAsync();
```

## Layout System

Termina uses a declarative tree-based layout system:

```csharp
Layouts.Vertical()
    .WithChild(header.Height(3))           // Fixed height
    .WithChild(content.Fill())             // Take remaining space
    .WithChild(sidebar.Width(20))          // Fixed width
    .WithChild(footer.Height(1));          // Fixed height

Layouts.Horizontal()
    .WithChild(menu.Width(30))
    .WithChild(main.Fill(2))               // 2x weight
    .WithChild(aside.Fill(1));             // 1x weight
```

## Routing

ASP.NET Core-style route templates with parameter support:

```csharp
builder.Services.AddTermina("/", termina =>
{
    termina.RegisterRoute<HomePage, HomeViewModel>("/");
    termina.RegisterRoute<TasksPage, TasksViewModel>("/tasks");
    termina.RegisterRoute<TaskDetailPage, TaskDetailViewModel>("/tasks/{id:int}");
    termina.RegisterRoute<UserPage, UserViewModel>("/users/{name}");
});
```

### Route Parameter Injection

```csharp
public partial class TaskDetailViewModel : ReactiveViewModel
{
    [FromRoute] private int _id;  // Injected from route

    public override void OnActivated()
    {
        LoadTask(Id);  // Id is already populated
    }
}
```

### Navigation

```csharp
Navigate("/tasks/42");
NavigateWithParams("/tasks/{id}", new { id = 42 });
Shutdown();  // Exit the application
```

## Streaming Content

For real-time content like LLM output, Pages own `StreamingTextNode` and subscribe to ViewModel observables:

```csharp
// In Page
private StreamingTextNode _output = null!;

protected override void OnBound()
{
    _output = StreamingTextNode.Create();
    ViewModel.StreamOutput.Subscribe(chunk => _output.Append(chunk));
}

// In ViewModel
public Observable<string> StreamOutput => _streamOutput;
private readonly Subject<string> _streamOutput = new();

private async Task StreamResponse()
{
    await foreach (var chunk in GetStreamingData())
    {
        _streamOutput.OnNext(chunk);  // Character-level updates
    }
}
```

## Testing

`VirtualInputSource` enables automated testing:

```csharp
var scriptedInput = new VirtualInputSource();
builder.Services.AddTerminaVirtualInput(scriptedInput);

scriptedInput.EnqueueKey(ConsoleKey.UpArrow);
scriptedInput.EnqueueString("Hello World");
scriptedInput.EnqueueKey(ConsoleKey.Enter);
scriptedInput.Complete();

await host.RunAsync();
```

## Requirements

- .NET 10.0 or later
- AOT-compatible (Native AOT publishing supported)

## License

Apache 2.0 - See [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.
