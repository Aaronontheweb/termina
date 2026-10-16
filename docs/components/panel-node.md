# PanelNode

A bordered container with optional title and content.

## Basic Usage

```csharp
new PanelNode()
    .WithTitle("My Panel")
    .WithContent(new TextNode("Panel content"))
```

## Border Styles

Termina supports four border styles:

```csharp
// Single line border (default)
new PanelNode().WithBorder(BorderStyle.Single)
// ┌─────────┐
// │ Content │
// └─────────┘

// Double line border
new PanelNode().WithBorder(BorderStyle.Double)
// ╔═════════╗
// ║ Content ║
// ╚═════════╝

// Rounded corners
new PanelNode().WithBorder(BorderStyle.Rounded)
// ╭─────────╮
// │ Content │
// ╰─────────╯

// ASCII fallback
new PanelNode().WithBorder(BorderStyle.Ascii)
// +---------+
// | Content |
// +---------+
```

## Styling

```csharp
new PanelNode()
    .WithTitle("Styled Panel")
    .WithBorder(BorderStyle.Rounded)
    .WithBorderColor(Color.Blue)
    .WithTitleColor(Color.Cyan)
    .WithPadding(1)
    .WithContent(content)
```

## Background Surface

`WithBackground` gives the panel a background color. The panel fills its complete bounds with
the color. This includes the border cells, the padding, and the content area.

```csharp
var surface = Color.FromRgb(23, 27, 34);

new PanelNode()
    .WithTitle("Logs")
    .WithTitleColor(Color.Cyan)
    .WithBorder(BorderStyle.None)
    .WithBackground(surface)
    .WithPadding(1)
    .WithContent(content)
```

The surface follows these rules:

- A child that calls `ResetColors` returns to the surface, not to the terminal default.
- A child that selects its own background keeps that background. The surface resumes after the child resets its colors.
- A nested panel with a background shows its own surface. The outer surface resumes after the nested panel.
- The surface does not reach the next sibling of the panel.
- A scrollable or streaming child in the panel still receives the mouse wheel when the pointer is over it.
- A panel without a background does not change the terminal background.

A border-less panel with a background marks a region without border glyphs. The glyphs of a
border become part of a native terminal text selection. A region without them gives a clean copy.

`WithBackground` sets a property. It does not request a redraw. The render reads the value at
render time. Change the value before the next frame, or build a new panel in a reactive layout.

::: tip
Use an indexed color (`Color.FromIndex`) if the app must run on a terminal without 24-bit color.
Termina does not reduce an RGB color for a terminal with less color. A wrong surface color covers the
complete panel, so it is more visible than a wrong text color. See
[Colors](../styling/colors.md#terminal-compatibility).
:::

## Content Types

Panel content can be any layout node or plain text:

```csharp
// Text string (auto-wrapped in TextNode)
new PanelNode().WithContent("Simple text")

// Layout node
new PanelNode().WithContent(new TextNode("Styled").Bold())

// Nested layouts
new PanelNode().WithContent(
    Layouts.Vertical()
        .WithChild(new TextNode("Line 1"))
        .WithChild(new TextNode("Line 2")))
```

## Reactive Content

Bind panel content to observables:

```csharp
new PanelNode()
    .WithTitle("Counter")
    .WithContent(
        ViewModel.CountChanged
            .Select(c => new TextNode($"Value: {c}"))
            .AsLayout())
```

## Size Constraints

PanelNode defaults to `HeightConstraint = Auto` and `WidthConstraint = Fill`:

```csharp
// Fixed height panel
new PanelNode()
    .WithContent(content)
    .Height(5)

// Fill available space
new PanelNode()
    .WithContent(content)
    .Fill()
```

## API Reference

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Title` | `string?` | `null` | Title in top border |
| `Border` | `BorderStyle` | `Single` | Border style |
| `BorderColor` | `Color?` | `null` | Border color |
| `Background` | `Color?` | `null` | Background color for the complete panel |
| `TitleColor` | `Color?` | `null` | Title color |
| `Padding` | `int` | `0` | Inner padding |

### Fluent Methods

| Method | Description |
|--------|-------------|
| `.WithTitle(string)` | Set the panel title |
| `.WithBorder(BorderStyle)` | Set border style |
| `.WithBorderColor(Color)` | Set border color |
| `.WithBackground(Color)` | Set the background color for the complete panel |
| `.WithTitleColor(Color)` | Set title color |
| `.WithContent(ILayoutNode)` | Set content node |
| `.WithContent(string)` | Set text content |
| `.WithPadding(int)` | Set inner padding |

## Source Code

::: details View PanelNode implementation
<<< @/../src/Termina/Layout/PanelNode.cs{csharp}
:::
