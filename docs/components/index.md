# Component Library

Termina provides a set of built-in layout nodes (components) for building terminal UIs. Each component handles a specific rendering concern and can be composed together.

![Component gallery demo: browsing the built-in components in the Termina demo gallery](/gallery/gallery-menu.gif)

*The component gallery — `demos/Termina.Demo.Gallery`.*

## Display Components

| Component | Description |
|-----------|-------------|
| [TextNode](/components/text-node) | Renders styled text with word wrapping |
| [PanelNode](/components/panel-node) | Bordered container with title |
| [SpinnerNode](/components/spinner-node) | Animated loading indicator |
| [GraphNode](/components/graph-node) | Live scrolling graph with gradient coloring |
| [ProgressBarNode](/components/progress-bar-node) | Progress bar with gradient fill and label |
| [StreamingTextNode](/components/streaming-text-node) | Streaming text with scrolling |

## Input Components

| Component | Description |
|-----------|-------------|
| [TextInputNode](/components/text-input-node) | Single-line text input with cursor |
| [TextAreaNode](/components/text-area-node) | Multi-line text input with word wrap and vertical scrolling |
| [CopyableTextNode](/components/copyable-text-node) | Read-only text with keyboard selection and clipboard support |
| [SelectionListNode](/components/selection-list-node) | Interactive list selection with keyboard navigation |
| [FilePickerNode](/components/file-picker-node) | File/folder picker with directory navigation and filtering |

## Container Components

| Component | Description |
|-----------|-------------|
| [ScrollableContainer](/components/scrollable-container) | Vertical scrolling container |
| [StackLayout](/components/stack-layout) | Overlapping children (z-stack) |
| [ModalNode](/components/modal-node) | Modal overlay with backdrop |

## Reactive Components

| Component | Description |
|-----------|-------------|
| [ReactiveLayoutNode](/components/reactive-layout) | Updates content from observables |
| [ConditionalNode](/components/conditional-node) | Show/hide based on condition |
| `DynamicLayoutNode` | Re-evaluates factory on invalidation |
| `KeyedDynamicLayoutNode` | Key-based content with retained or transition-evicted children |

## Composite Components

| Component | Description |
|-----------|-------------|
| `WizardNode` | Multi-step wizard with progress, navigation, and focus |

## Utility Components

| Component | Description |
|-----------|-------------|
| [EmptyNode](/components/empty-node) | Placeholder that renders nothing |
| [DeferredNode](/components/deferred-node) | Delegates to node without owning it |

## Common Patterns

All components inherit from `LayoutNode` and share common methods:

```csharp
// Size constraints
node.Height(3);                      // Fixed height
node.Width(40);                      // Fixed width
node.Fill();                         // Fill remaining height
node.WidthFill();                    // Fill remaining width
node.HeightAuto();                   // Auto-size to content
node.Width(SizeConstraint.Percent(50)); // Percentage

// Get constraint object for custom use
node.HeightConstraint = SizeConstraint.Fill(weight: 2);
```

## Creating Custom Components

See the [Custom Components](/advanced/custom-components) guide for building your own layout nodes.
