# Release Notes — Termina 0.17.0-beta.6

**Release date:** 2026-08-12

####

**New Features**

- **Added optional panel background surfaces** ([#378](https://github.com/Aaronontheweb/termina/issues/378))
  - `PanelNode.WithBackground` fills the complete panel bounds.
  - Child color resets keep the selected surface background.
  - Panels without a background keep their current behavior.

####

# Release Notes — Termina 0.17.0-beta.5

**Release date:** 2026-08-11

####

**Bug Fixes**

- **Corrected text-area prompt history traversal** ([#368](https://github.com/Aaronontheweb/termina/issues/368))
  - Up recalls prompt history from the first visual line and saves the current draft.
  - Down restores the saved draft from the last visual line.
  - Up and Down keep their current visual movement between multiline rows.

####

# Release Notes — Termina 0.17.0-beta.4

**Release date:** 2026-08-11

####

**New Features**

- **Added prompt history cancellation** ([#368](https://github.com/Aaronontheweb/termina/issues/368))
  - `CancelHistoryNavigation` restores the draft that existed before history navigation.
  - The API resets history navigation without a change to current method signatures.

- **Added pointer-aware wheel routing** ([#240](https://github.com/Aaronontheweb/termina/issues/240))
  - `MouseScrollEvent` preserves zero-based pointer coordinates and keyboard modifiers.
  - Termina routes wheel input to the measured scroll region under the pointer.
  - `ScrollableContainerNode` now implements the current `IScrollable` contract.

- **Added a modified Enter key capability result** ([#240](https://github.com/Aaronontheweb/termina/issues/240))
  - Termina queries active Kitty keyboard flags without a terminal-name guess.
  - `TerminalInputCapabilitiesChanged` reports whether modified Enter keys remain distinct.
  - Windows console records report native modifier support.

- **Added semantic clipboard content for copyable text**
  - `WithSemanticContent` separates complete clipboard text from compact display text.
  - `TryCopy` reports clipboard failure and preserves the current selection.

- **Added an opt-in inline presentation mode** ([#366](https://github.com/Aaronontheweb/termina/issues/366))
  - `TerminalPresentationMode.Inline` keeps settled output in the primary buffer.
  - `IInlineOutput` commits stable layouts above one bounded live region.
  - `ScrollInputMode.NativeTerminal` leaves selection and scrollback under terminal control.

- **Added extend-only terminal control APIs** ([#370](https://github.com/Aaronontheweb/termina/issues/370))
  - `IInlineTerminalControl` adds relative cursor operations without a change to `IAnsiTerminal`.
  - `AnsiTerminal` and `VirtualTerminal` implement the new contract.
  - Full-screen mode remains the default mode.

**Bug Fixes**

- **Restored all terminal and input state after a render failure**
  - Termina now cancels input tasks before it propagates a render exception.
  - Termina restores the cursor, mouse modes, keyboard modes, and input mode.

**Compatibility**

- Current enum values and public signatures keep their behavior.
- The current `AnsiTerminal(bool)` constructor keeps its behavior.
- An API approval test now prevents accidental contract changes.

####

# Release Notes — Termina 0.16.1

**Release date:** 2026-08-08

####

**New Features**

- **Added cache policy to keyed dynamic layouts** ([#361](https://github.com/Aaronontheweb/termina/pull/361))
  - `KeyedDynamicLayoutNode` now supports `AllKeys` and `CurrentOnly` cache policies.
  - `AllKeys` remains the default policy, preserving existing behavior and source compatibility.
  - `CurrentOnly` keeps only the active child and replaces it when the key changes.
  - Replaced children deactivate immediately and dispose during the next layout pass.

####

# Release Notes — Termina 0.16.0

**Release date:** 2026-08-07

####

**New Features**

- **Roslyn analyzers now ship with the Termina library** ([#346](https://github.com/Aaronontheweb/termina/pull/346))
  - Termina now includes its Roslyn analyzers in the package.
  - The compiler runs the analyzers on your project and reports common layout node mistakes at build time.
  - This change also fixes a node that Termina did not dispose when content switched.

- **New analyzer TERMINA003 for layout node child disposal** ([#343](https://github.com/Aaronontheweb/termina/pull/343))
  - TERMINA003 warns when code disposes a child layout node outside `Dispose()`.
  - `Dispose()` destroys the node, so the node can no longer render or handle input.
  - The analyzer tells you to call `OnDeactivate()` to switch content.

- **New analyzer TERMINA004 for stateful node recreation** ([#337](https://github.com/Aaronontheweb/termina/pull/337))
  - TERMINA004 warns when a dynamic layout factory creates a new stateful node on each run.
  - A new node resets state such as the scroll position.
  - The analyzer tells you to reuse the node, to invalidate a smaller child, or to use `KeyedDynamicLayoutNode`.

**Bug Fixes**

- **Fixed glyph corruption when word-wrap breaks a long word that contains wide characters** ([#351](https://github.com/Aaronontheweb/termina/pull/351))
  - `StyledLine.SliceByColumns` no longer drops or reorders glyphs.
  - The corruption occurred when a long word contained wide characters (CJK or emoji) across segments.
  - Streamed text now keeps the correct content and order.

- **Fixed the style of word-wrap space separators** ([#349](https://github.com/Aaronontheweb/termina/pull/349))
  - Word-wrap space separators now inherit the whitespace style from the source text.
  - A wrapped line keeps the correct foreground and background for the space between words.

- **Guarded dynamic layout nodes against re-entrant Invalidate** ([#348](https://github.com/Aaronontheweb/termina/pull/348))
  - `DynamicLayoutNode` and `KeyedDynamicLayoutNode` no longer re-enter `Invalidate()`.
  - The guard prevents a stack overflow and inconsistent layout during a factory run.

- **Fixed GridNode cell subscription tracking on content swap** ([#347](https://github.com/Aaronontheweb/termina/pull/347))
  - `GridNode` now tracks cell subscriptions per content node.
  - The grid deactivates the old subscriptions when it swaps a cell.
  - This change prevents stale updates and resource leaks.

**Documentation**

- **Documented the built-in back navigation APIs** ([#350](https://github.com/Aaronontheweb/termina/pull/350))
  - New documentation explains the back navigation APIs.

####

# Release Notes — Termina 0.15.1

**Release date:** 2026-07-21

####

**Bug Fixes**

- **Fixed horizontal scroll offset in TextInputNode** ([#331](https://github.com/Aaronontheweb/termina/pull/331))
  - TextInputNode now correctly resets its horizontal scroll offset when text is submitted or cleared
  - Fixes visual artifacts where the cursor would appear misaligned after clearing input

####

# Release Notes — Termina 0.15.0

**Release date:** 2026-07-01

####

**Bug Fixes**

- **Fixed CJK and Unicode display width handling** ([#321](https://github.com/Aaronontheweb/termina/pull/321))
  - Terminal rendering now correctly accounts for East Asian and wide Unicode character widths in layout and cursor behavior

**Dependency Updates**

- Updated `actions/setup-dotnet` from 5.2.0 to 5.3.0 ([#270](https://github.com/Aaronontheweb/termina/pull/270))
- Updated `Microsoft.Extensions.TimeProvider.Testing` from 10.6.0 to 10.7.0 ([#288](https://github.com/Aaronontheweb/termina/pull/288))
- Updated `dotnet-sdk` from 10.0.201 to 10.0.301 ([#290](https://github.com/Aaronontheweb/termina/pull/290))
- Updated `Microsoft.NET.Test.Sdk` from 18.6.0 to 18.7.0 ([#319](https://github.com/Aaronontheweb/termina/pull/319))

####

#### 0.14.0 June 23rd 2026 ####

**New Features**:

- **Gradient primitives, GraphNode, and ProgressBarNode** ([#298](https://github.com/Aaronontheweb/termina/pull/298))
  - New gradient system for rich visual theming
  - `GraphNode` for data visualization and flow diagrams
  - `ProgressBarNode` for animated progress indicators

- **Toast notifications with colors and icons** ([#296](https://github.com/Aaronontheweb/termina/pull/296))
  - Enhanced `ToastNode` with configurable foreground/background colors
  - Icon support for status differentiation

- **Modal footers with configurable colors** ([#292](https://github.com/Aaronontheweb/termina/pull/292), [#293](https://github.com/Aaronontheweb/termina/pull/293))
  - `ModalNode` now supports `WithFooter` and `WithFooterColor` for adding a styled footer section
  - Footer renders below modal content with optional color theming
  - Includes a Gallery demo showcasing the feature on TodoList modals

- **Render loop frame provider** ([#306](https://github.com/Aaronontheweb/termina/pull/306))
  - New `IRenderLoopFrameProvider` for deterministic frame timing control
  - Enables precise animation and layout update scheduling

**Bug Fixes**:

- **Suppressed stale pre-swap frames on deferred navigation** ([#315](https://github.com/Aaronontheweb/termina/pull/315))
  - Fixed visual artifacts when navigation is queued from an input handler during render swap

- **Eliminated no-op resize full refresh** ([#312](https://github.com/Aaronontheweb/termina/pull/312))
  - Resizes that don't change bounds no longer trigger expensive full layout refresh

- **Corrected ScrollableContainerNode bounds.Y handling** ([#301](https://github.com/Aaronontheweb/termina/pull/301))
  - `ScrollableContainerNode` now respects `bounds.Y` instead of overwriting layout above it
  - Fixes layout stacking issues in scrollable containers

- **Fixed CSI Z (backtab/Shift+Tab) parsing** ([#297](https://github.com/Aaronontheweb/termina/pull/297))
  - CSI Z sequences now correctly map to Tab with Shift modifier instead of being misinterpreted

**Dependency Updates**:

- Updated `Akka.Hosting` from 1.5.68 to 1.5.69 ([#299](https://github.com/Aaronontheweb/termina/pull/299))
- Updated `OpenTelemetry.Api` from 1.15.3 to 1.16.0 ([#291](https://github.com/Aaronontheweb/termina/pull/291))

---

# Release Notes — Termina 0.14.0-beta.3

**Release date:** 2026-06-22

####

This is the third beta of Termina 0.14.0 — a focused render-loop stability release for deferred navigation.

**Bug Fixes**

- Suppressed stale pre-swap frames when deferred navigation is queued from an input handler (#314)

---

### Contributors

Aaron Stannard
