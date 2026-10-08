# Release Notes — Termina 0.16.3

**Release date:** 2026-10-08

####

**Bug Fixes**

- **Added a way to place the cursor in a pre-filled text input** ([#395](https://github.com/Aaronontheweb/termina/pull/395), fixes [#394](https://github.com/Aaronontheweb/termina/issues/394))
  - Assigning `Text` on a `TextInputNode` or `TextAreaNode` leaves the cursor where it was, so a pre-filled field started with the cursor in front of its default text.
  - `TextInputBaseNode.CursorPosition` gets and sets the cursor as a UTF-16 index into the editable text, clamped to a text-element boundary.
  - `TextInputBaseNode.MoveCursorToEnd()` moves the cursor to the end of the text.
  - Both work before the node is attached or focused. The `Text` setter is unchanged.

**Maintenance**

- **Bumped `Microsoft.SourceLink.GitHub` to 10.0.303** ([#396](https://github.com/Aaronontheweb/termina/pull/396)) to clear the NU1902 advisory on `Microsoft.Build.Tasks.Git`.

####

# Release Notes — Termina 0.16.2

**Release date:** 2026-08-14

####

**Bug Fixes**

- **Made container disposal safe when layout retirement overlaps teardown** ([#384](https://github.com/Aaronontheweb/termina/pull/384))
  - `ContainerNode.Dispose()` now claims teardown atomically.
  - Concurrent or repeated disposal no longer completes the same reactive invalidation subject twice.
  - Child layout nodes are still disposed exactly once.

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
