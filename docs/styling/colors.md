# Colors

Termina provides a flexible color system supporting terminal defaults, 256-color palettes, and true color (24-bit RGB).

## Color Modes

### Default

Uses the terminal's default foreground or background color:

```csharp
Color.Default
```

### Named Colors (256-color palette)

Standard terminal colors:

```csharp
// Basic colors
Color.Black
Color.Red
Color.Green
Color.Yellow
Color.Blue
Color.Magenta
Color.Cyan
Color.White

// Bright variants
Color.BrightBlack
Color.BrightRed
Color.BrightGreen
Color.BrightYellow
Color.BrightBlue
Color.BrightMagenta
Color.BrightCyan
Color.BrightWhite

// Grayscale
Color.Gray
Color.DarkGray
Color.LightGray
```

### Indexed Colors

Access any of the 256-color palette:

```csharp
Color.FromIndex(42)  // Index 0-255
```

The 256-color palette includes:
- 0-7: Standard colors
- 8-15: Bright colors
- 16-231: 6×6×6 color cube
- 232-255: Grayscale ramp

### RGB Colors (True Color)

24-bit color for modern terminals:

```csharp
// From RGB values
Color.FromRgb(255, 128, 0)  // Orange

// From hex string
Color.FromHex("#FF8000")
Color.FromHex("FF8000")     // # is optional
```

## Applying Colors

### Foreground Color

```csharp
new TextNode("Colored text")
    .WithForeground(Color.Cyan)
```

### Background Color

```csharp
new TextNode("Highlighted")
    .WithBackground(Color.Yellow)
    .WithForeground(Color.Black)
```

### Both Colors

```csharp
new TextNode("Styled")
    .WithForeground(Color.White)
    .WithBackground(Color.Blue)
```

## Color Interpolation

`Color.Lerp` linearly interpolates between two RGB colors:

```csharp
var midpoint = Color.Lerp(Color.FromRgb(255, 0, 0), Color.FromRgb(0, 0, 255), 0.5f);
// Result: purple (127, 0, 127)
```

If either color is not in RGB mode, `Lerp` returns the first color unchanged.

## Gradients

The `Gradient` record defines a series of color stops and interpolates between them. Use gradients with `GraphNode` and `ProgressBarNode` for smooth color transitions.

```csharp
// Evenly spaced stops
var gradient = Gradient.Create(Color.Red, Color.Yellow, Color.Green);

// Custom stop positions
var custom = Gradient.Create(
    (0.0f, Color.FromRgb(255, 0, 0)),
    (0.3f, Color.FromRgb(255, 200, 0)),
    (1.0f, Color.FromRgb(0, 255, 0)));

// Sample at any position (0–1)
Color color = gradient.Sample(0.5f);
```

At least two colors are required. Positions outside the 0–1 range are clamped.

## Color Support by Component

| Component | Foreground | Background | Other |
|-----------|------------|------------|-------|
| TextNode | ✓ | ✓ | - |
| PanelNode | - | ✓ | Border, Title |
| SpinnerNode | - | - | Spinner, Label |
| TextInputNode | ✓ | ✓ | Placeholder, Cursor, Selection |
| StreamingTextNode | ✓ | ✓ | Prefix |
| GraphNode | - | - | Gradient, Single color |
| ProgressBarNode | - | - | Gradient, Single color, Empty color |

## Terminal Compatibility

::: warning
Not all terminals support all color modes:

- **Default** - Works everywhere
- **Indexed (0-15)** - Works in almost all terminals
- **Indexed (16-255)** - Requires 256-color support
- **RGB** - Requires true color support (most modern terminals)

When RGB colors are used in a terminal that doesn't support them, results may vary.
:::

### Common Terminal Support

| Terminal | 256-color | True Color |
|----------|-----------|------------|
| Windows Terminal | ✓ | ✓ |
| iTerm2 | ✓ | ✓ |
| GNOME Terminal | ✓ | ✓ |
| VS Code Terminal | ✓ | ✓ |
| macOS Terminal.app | ✓ | Limited |
| cmd.exe | Limited | ✗ |

## Color API Reference

::: details View Color implementation
<<< @/../src/Termina/Terminal/Color.cs{csharp}
:::
