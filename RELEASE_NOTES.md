# Release Notes — Termina 0.17.0

**Release date:** 2026-10-16

####

**Behavior Changes**

- **The event loop no longer runs queued work after it stops** ([#400](https://github.com/Aaronontheweb/termina/pull/400))
  - When the loop stops, it drops all work that is still in the queue. This includes work that `Post` or `InvokeAsync` queued before the call to `Shutdown()`. Earlier versions could run that work.
  - A task that `InvokeAsync` returned for dropped work ends as canceled with `TaskCanceledException`. The task ends this way even when the token of the caller is not canceled.
  - `InvokeAsync` called after the loop stopped returns a canceled task. `Post` called after the loop stopped does not run its work.
  - A queued `CommitAsync` call for inline output ends as canceled in the same way.
  - Code that must run during shutdown must run before `Shutdown()`, not as queued work.
- **`RequestRedraw` can merge with a redraw request that is already queued** ([#400](https://github.com/Aaronontheweb/termina/pull/400))
  - Change the state before you call `RequestRedraw()`. A render that runs between the call and the state change can draw the old state. The loop then skips the merged request.
  - A `RedrawRequested.Instance` that an input source writes to the event channel always renders.
  - `Input` subscribers still see every redraw request as `RedrawRequested`.

**New Features**

- **Added an opt-in inline presentation mode** ([#373](https://github.com/Aaronontheweb/termina/pull/373), fixes [#366](https://github.com/Aaronontheweb/termina/issues/366) and [#370](https://github.com/Aaronontheweb/termina/issues/370))
  - `TerminalPresentationMode.Inline` keeps settled output in the primary buffer.
  - `IInlineOutput` commits stable layouts above one bounded live region.
  - `ScrollInputMode.NativeTerminal` leaves selection and scrollback under terminal control.
  - `IInlineTerminalControl` adds relative cursor operations without a change to `IAnsiTerminal`. `AnsiTerminal` and `VirtualTerminal` implement the new contract.
  - Full-screen mode remains the default mode.

- **Added semantic clipboard content for copyable text** ([#374](https://github.com/Aaronontheweb/termina/pull/374), fixes [#367](https://github.com/Aaronontheweb/termina/issues/367))
  - `WithSemanticContent` separates complete clipboard text from compact display text.
  - `TryCopy` reports clipboard failure and preserves the current selection.

- **Added a modified Enter key capability result** ([#375](https://github.com/Aaronontheweb/termina/pull/375), fixes [#240](https://github.com/Aaronontheweb/termina/issues/240))
  - Termina queries active Kitty keyboard flags without a terminal-name guess.
  - `TerminalInputCapabilitiesChanged` reports whether modified Enter keys remain distinct.
  - `TerminaApplication.InputCapabilities` exposes the latest result.
  - Windows console records report native modifier support.

- **Added prompt history cancellation** ([#376](https://github.com/Aaronontheweb/termina/pull/376), fixes [#368](https://github.com/Aaronontheweb/termina/issues/368))
  - `CancelHistoryNavigation` restores the draft that existed before history navigation.
  - The API resets history navigation without a change to current method signatures.

- **Added pointer-aware wheel routing** ([#376](https://github.com/Aaronontheweb/termina/pull/376), fixes [#240](https://github.com/Aaronontheweb/termina/issues/240))
  - `MouseScrollEvent` preserves zero-based pointer coordinates and keyboard modifiers.
  - Termina routes wheel input to the measured scroll region under the pointer.
  - `ScrollableContainerNode` now implements the current `IScrollable` contract.

- **Added an optional panel background surface** ([#379](https://github.com/Aaronontheweb/termina/pull/379), fixes [#378](https://github.com/Aaronontheweb/termina/issues/378))
  - `PanelNode.WithBackground(Color)` sets the surface. `PanelNode.Background` reads it.
  - The panel fills its complete bounds, including the border cells.
  - A child that resets its colors returns to the surface. A child that selects its own background keeps it.
  - A scrollable or streaming node in a surface panel keeps its screen bounds, so the mouse wheel reaches it.
  - A panel without a background keeps its current behavior.

**Performance**

- **The event loop renders once for each batch of queued events** ([#400](https://github.com/Aaronontheweb/termina/pull/400))
  - The loop processes the events that are queued when a batch starts, and then renders once. Events that arrive during the batch go to the next batch.
  - Before, the loop rendered the full page after every event. A slow render let the queue grow without limit.
  - 20 `RequestRedraw` calls in one loop turn now cause 2 to 3 renders. Before, they caused 24 renders.
  - The cursor blink alone now causes about 1.9 renders each second. Before, it caused about 4.3.
  - The final frame is the same as before.
- **`TextAreaNode` measures a long line cheaply** ([#400](https://github.com/Aaronontheweb/termina/pull/400))
  - `Measure` keeps the column counts of the text until the text changes. A call with unchanged text takes under 0.01 ms and allocates no memory. Before, it took 43 ms and allocated 16 MB for a line of 600,000 characters.
  - `DisplayWidth.GetColumnCount` returns the length of printable ASCII text without an allocation.
  - `TextAreaNode` keeps one line wrap for each width. A layout that measures and renders at different widths no longer wraps the line twice for each frame.
  - One render of an unchanged 600,000-character line takes under 1 ms instead of 69 ms, and allocates 0.27 MB instead of 32 MB.
- **Ctrl+Q exits fast with a large transcript or a large paste** ([#400](https://github.com/Aaronontheweb/termina/pull/400))
  - The loop waits for each batch with `WaitToReadAsync` and stops at a shutdown request. Before, the loop kept running while events kept arriving.
  - An app with a 1,000,000-character line now exits in about 360 ms. Before, it did not exit within 90 s.
- **Wrapping a very long line is now linear in its length** ([#398](https://github.com/Aaronontheweb/termina/pull/398), fixes [#397](https://github.com/Aaronontheweb/termina/issues/397), first shipped in 0.16.4)
  - `StyledWordWrapper`, `WordWrapper`, and `TextAreaNode` no longer re-measure the rest of a long line for every wrapped row.
- **Text writes to the diffing terminal allocate less** ([#379](https://github.com/Aaronontheweb/termina/pull/379))
  - `DiffingTerminal.Write` skips the grapheme parse when every char is printable ASCII. All other text uses the old loop.
  - A full-screen frame of a panel with a background allocates 40 KB instead of 400 KB. It renders in about 0.7 ms instead of 1.5 ms.
  - This change applies to every app and every render, not only panels. The output is the same.

**Bug Fixes**

- **Fixed tail follow after a large content update** ([#383](https://github.com/Aaronontheweb/termina/pull/383))
  - A `ScrollableContainerNode` with `TailWhenAtBottom` stopped at the old offset after the content grew by more than 2 rows. This happens often now, because the loop renders once for each batch of events.
  - `Measure` now reads `IsNearBottom` before it replaces the content height.
  - When the content shrinks, `Measure` clamps the offset to the new range. Before, a shrink could leave a blank pane.
  - A user who scrolled up stays where the user is.

- **Restored all terminal and input state after a render failure**
  - Termina now cancels input tasks before it propagates a render exception.
  - Termina restores the cursor, mouse modes, keyboard modes, and input mode.

- **Corrected text-area prompt history traversal** ([#377](https://github.com/Aaronontheweb/termina/pull/377), fixes [#368](https://github.com/Aaronontheweb/termina/issues/368))
  - Up recalls prompt history from the first visual line and saves the current draft.
  - Down restores the saved draft from the last visual line.
  - Up and Down keep their current visual movement between multiline rows.

- **Made container disposal safe when layout retirement overlaps teardown** ([#384](https://github.com/Aaronontheweb/termina/pull/384), first shipped in 0.16.2)
  - `ContainerNode.Dispose()` now claims teardown atomically, so concurrent disposal no longer completes the same reactive invalidation subject twice.

- **Added a way to place the cursor in a pre-filled text input** ([#395](https://github.com/Aaronontheweb/termina/pull/395), fixes [#394](https://github.com/Aaronontheweb/termina/issues/394), first shipped in 0.16.3)
  - `TextInputBaseNode.CursorPosition` gets and sets the cursor as a UTF-16 index into the editable text.
  - `TextInputBaseNode.MoveCursorToEnd()` moves the cursor to the end of the text.

**Compatibility**

- Current enum values and public signatures keep their behavior. The release adds no public API change that breaks a caller, apart from the event loop behavior changes above.
- The current `AnsiTerminal(bool)` constructor keeps its behavior.
- An API approval test prevents accidental contract changes.

**Maintenance**

- Bumped `Microsoft.SourceLink.GitHub` to 10.0.303 ([#396](https://github.com/Aaronontheweb/termina/pull/396)) and pinned `System.Security.Cryptography.Pkcs` to 10.0.13 in the test project ([#399](https://github.com/Aaronontheweb/termina/pull/399)) to clear security advisory warnings.
- Updated `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Hosting` to 10.0.11 ([#380](https://github.com/Aaronontheweb/termina/pull/380), [#389](https://github.com/Aaronontheweb/termina/pull/389)).
- Updated `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.Analyzers` to 5.9.0 for the repository ([#387](https://github.com/Aaronontheweb/termina/pull/387)). The source generators and analyzers that ship in the package stay on 5.3.0, as in 0.16.4 ([#403](https://github.com/Aaronontheweb/termina/pull/403)). Consumers keep the same .NET SDK requirement.
- Updated the `incrementalist.cmd` tool to 1.2.2 ([#385](https://github.com/Aaronontheweb/termina/pull/385)).

####

# Release Notes — Termina 0.16.4

**Release date:** 2026-10-14

####

**Bug Fixes**

- **Wrapping a very long line is now linear in its length** ([#398](https://github.com/Aaronontheweb/termina/pull/398), fixes [#397](https://github.com/Aaronontheweb/termina/issues/397))
  - `StyledWordWrapper` (used by `StreamingTextNode`), `WordWrapper` (used by `TextNode`) and `TextAreaNode` re-measured the rest of a long word or line for every wrapped row, so wrapping was quadratic in line length.
  - A line of 100,000 characters with no break opportunities now wraps in milliseconds instead of seconds. An app rendering such a line no longer pins a core or stops handling input at normal frame rates.
  - Wrapped output is the same as 0.16.3, with one exception: a zero-width-only segment between two segments of the same style inside a word wider than the row. 0.16.3 could draw part of that text twice; it is now drawn once.

####

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
