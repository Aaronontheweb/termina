# Unicode Terminal Width

Termina renders to terminal cells, not UTF-16 characters. Any code that measures,
clips, wraps, pads, scrolls, or positions text must use terminal display columns.

This matters for global text because a single user-visible text element can map to
different terminal widths:

| Text kind | Typical terminal width |
|-----------|------------------------|
| ASCII letters, digits, punctuation | 1 column |
| CJK ideographs, kana, Hangul, fullwidth forms | 2 columns |
| Many emoji and pictographic symbols | 2 columns |
| Combining marks, variation selectors, controls | 0 columns |

## Goals

Termina should render common Unicode terminal text without corrupting layout.

This includes:

- Measuring text by terminal columns instead of `string.Length`.
- Wrapping and truncating without splitting a Unicode text element.
- Preserving wide-cell alignment for CJK, Hangul, kana, fullwidth forms, and emoji.
- Preserving zero-width marks as part of their text element.
- Keeping ANSI styling metadata separate from printable text.
- Making cursor and selection positions line up with the visible terminal cells.

## Non-Goals

This support does not attempt to solve every international text rendering problem.

Out of scope:

- Bidirectional text layout.
- Complex shaping for scripts that require font or renderer-specific shaping.
- Locale-specific ambiguous-width policies.
- Unicode line-breaking rules beyond terminal-cell wrapping.
- Legacy terminal encodings other than the .NET string input already received.

## Core Model

Termina uses these distinct concepts:

| Concept | Meaning | Use |
|---------|---------|-----|
| Source index | UTF-16 index into a .NET `string` | Editing, selection, clipboard slicing |
| Text element | Unicode grapheme-like unit from `StringInfo.ParseCombiningCharacters` | Safe render/truncate boundaries |
| Display column | Terminal cell position | Layout, wrapping, clipping, cursor x-position |
| Continuation cell | Trailing cell reserved by a wide text element | Terminal buffers and diffing |

`DisplayWidth` is the shared utility for moving between these concepts.

## Invariants

Rendering and layout code must follow these rules:

- Use `DisplayWidth.GetColumnCount` for visual width.
- Use `DisplayWidth.EnumerateCells` when rendering text element by text element.
- Use `DisplayWidth.SliceByColumns`, `TruncateToColumns`, or `TruncateStartToColumns` for clipping and truncation.
- Use `DisplayWidth.CursorPositionToColumn` when converting a source cursor index to an x-position.
- Use `DisplayWidth.GetTextElementAt` when rendering the glyph under a cursor.
- Do not use `string.Length` as visual width.
- Do not use `Substring`, range slicing, `PadLeft`, or `PadRight` for visual clipping or padding unless the code has already converted column widths explicitly.
- Do not render ANSI escape sequences as text. Styling must be terminal state or cell metadata.
- Wide text elements must reserve continuation cells in terminal buffers so later writes cannot overlap half of a glyph.

`string.Length` is still valid for source text operations such as clipboard content,
selection ranges, persisted values, and non-visual bounds checks.

## Testing Requirements

Any component that measures, wraps, truncates, scrolls, or positions text should have
tests that include at least one wide text case. Prefer tests that assert both the
visible text and the cell coordinates.

Recommended coverage:

- CJK text wrapping at a narrow width.
- Cursor positioning before, on, and after a wide text element.
- Truncation at a boundary that would otherwise split a wide element.
- Padding around CJK or emoji content.
- ANSI decoration through the diffing renderer.

These tests should use `VirtualTerminal` or `DiffingTerminal` so they validate cell
placement rather than only string output.
