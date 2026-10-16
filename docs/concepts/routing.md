# Routing

Termina uses ASP.NET-style route templates for navigation between pages.

## Route Registration

Register routes when configuring Termina:

```csharp
builder.Services.AddTermina("/", termina =>
{
    // Simple routes
    termina.RegisterRoute<HomePage, HomeViewModel>("/");
    termina.RegisterRoute<AboutPage, AboutViewModel>("/about");

    // Route with parameters
    termina.RegisterRoute<DetailPage, DetailViewModel>("/items/{id}");
    termina.RegisterRoute<UserPage, UserViewModel>("/users/{name}");
});
```

## Route Parameters

### Basic Parameters

Capture path segments as parameters:

```csharp
// Route: "/items/{id}"
// Matches: "/items/42", "/items/abc"
termina.RegisterRoute<DetailPage, DetailViewModel>("/items/{id}");
```

### Typed Parameters

Constrain parameters to specific types:

```csharp
// Only matches integers
termina.RegisterRoute<DetailPage, DetailViewModel>("/items/{id:int}");
// Matches: "/items/42"
// Doesn't match: "/items/abc"

// Only matches GUIDs
termina.RegisterRoute<DetailPage, DetailViewModel>("/items/{id:guid}");
```

### Supported Type Constraints

| Constraint | Pattern | Example Match |
|------------|---------|---------------|
| `int` | `{id:int}` | `42`, `-1` |
| `bool` | `{flag:bool}` | `true`, `false` |
| `guid` | `{id:guid}` | `550e8400-e29b-...` |
| (none) | `{name}` | Any string |

## Receiving Parameters

### Using [FromRoute]

The simplest approach - use source generation:

```csharp
public partial class DetailViewModel : ReactiveViewModel
{
    [FromRoute] private int _id;

    public override void OnActivated()
    {
        // Id is already populated
        LoadItem(Id);
    }
}
```

### Manual Parameter Access

Access raw parameters via the route context:

```csharp
public class DetailViewModel : ReactiveViewModel
{
    public int Id { get; private set; }

    internal void ApplyRouteParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue("id", out var value))
        {
            Id = Convert.ToInt32(value);
        }
    }
}
```

## Initial Route

The first argument to `AddTermina` is the starting route:

```csharp
// Start at home page
builder.Services.AddTermina("/", termina => { ... });

// Start at specific page
builder.Services.AddTermina("/dashboard", termina => { ... });
```

## Route Matching

Routes are matched in registration order. The first match wins:

```csharp
// More specific routes should come first
termina.RegisterRoute<SpecificPage, SpecificViewModel>("/items/special");
termina.RegisterRoute<DetailPage, DetailViewModel>("/items/{id}");
termina.RegisterRoute<ListPage, ListViewModel>("/items");
```

## Navigating with Parameters

### Simple Path

```csharp
Navigate("/items/42");
```

### Template with Values

```csharp
NavigateWithParams("/items/{id}", new { id = 42 });
NavigateWithParams("/users/{name}", new { name = "alice" });
```

## Going Back

Termina tracks navigation history across route transitions. See [Navigation](/concepts/navigation.html#going-back) for `GoBack()`, `CanGoBack`, and `NavigationBackRequested`.

## Route Source Code

::: details View RouteTemplate implementation
<<< @/../src/Termina/Routing/RouteTemplate.cs{csharp}
:::
