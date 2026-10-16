using R3;
using Termina.Clipboard;
using Termina.Diagnostics;
using Termina.Notifications;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// A focusable read-only text node that copies its full content when Enter is pressed.
/// </summary>
public sealed class CopyableTextNode : LayoutNode, IFocusable, IInvalidatingNode
{
    private readonly IClipboardService _clipboardService;
    private readonly IToastService? _toastService;
    private readonly Subject<Unit> _invalidated = new();
    private IDisposable? _inlineIndicatorSubscription;
    private IReadOnlyList<CopyKeyBinding> _copyBindings =
    [
        new(ConsoleKey.Enter),
        new(ConsoleKey.C, ConsoleModifiers.Control)
    ];
    private int _cursorPosition;
    private int _selectionStart = -1;
    private string? _semanticContent;
    private bool _showInlineIndicator;
    private bool _hasFocus;
    private bool _disposed;

    public CopyableTextNode(IClipboardService clipboardService, string content, IToastService? toastService = null)
    {
        _clipboardService = clipboardService;
        _toastService = toastService;
        Content = content ?? string.Empty;
        WidthConstraint = new SizeConstraint.Fill();
        HeightConstraint = SizeConstraint.AutoSize();
    }

    public string Content { get; private set; }

    /// <summary>
    /// Gets the complete text used for a full-content copy action.
    /// </summary>
    /// <remarks>
    /// This value defaults to <see cref="Content"/>. A text selection still copies
    /// the selected display text.
    /// </remarks>
    public string SemanticContent => _semanticContent ?? Content;

    public string? Hint { get; private set; } = "Press Enter to copy";

    public Color Foreground { get; private set; } = Color.White;

    public Color FocusedForeground { get; private set; } = Color.Black;

    public Color FocusedBackground { get; private set; } = Color.Cyan;

    public Color SelectionForeground { get; private set; } = Color.Black;

    public Color SelectionBackground { get; private set; } = Color.BrightYellow;

    public Color InlineIndicatorForeground { get; private set; } = Color.BrightGreen;

    public string InlineIndicatorText { get; private set; } = "✓";

    public CopyFeedbackMode FeedbackMode { get; private set; } = CopyFeedbackMode.Toast;

    public ToastPosition ToastPosition { get; private set; } = ToastPosition.BottomRight;

    public TimeSpan FeedbackDuration { get; private set; } = TimeSpan.FromSeconds(2);

    public bool CanFocus => true;

    public bool HasFocus => _hasFocus;

    public int FocusPriority => 5;

    public Observable<Unit> Invalidated => _invalidated;

    public bool HasSelection => _selectionStart >= 0 && _selectionStart != _cursorPosition;

    public string SelectedText
    {
        get
        {
            if (!HasSelection)
                return string.Empty;

            var start = Math.Min(_selectionStart, _cursorPosition);
            var end = Math.Max(_selectionStart, _cursorPosition);
            return Content[start..end];
        }
    }

    public CopyableTextNode WithContent(string content)
    {
        Content = content ?? string.Empty;
        _cursorPosition = DisplayWidth.ClampToTextElementBoundary(Content, _cursorPosition);
        if (_selectionStart > Content.Length)
            _selectionStart = -1;
        Invalidate();
        return this;
    }

    /// <summary>
    /// Sets the complete text used for a full-content copy action.
    /// </summary>
    /// <remarks>
    /// Use this value when the display text contains decoration, truncation, or
    /// terminal control data that the clipboard text must omit.
    /// </remarks>
    public CopyableTextNode WithSemanticContent(string semanticContent)
    {
        ArgumentNullException.ThrowIfNull(semanticContent);
        _semanticContent = semanticContent;
        return this;
    }

    public CopyableTextNode WithHint(string? hint)
    {
        Hint = hint;
        Invalidate();
        return this;
    }

    public CopyableTextNode WithForeground(Color color)
    {
        Foreground = color;
        return this;
    }

    public CopyableTextNode WithFocusedColors(Color foreground, Color background)
    {
        FocusedForeground = foreground;
        FocusedBackground = background;
        return this;
    }

    public CopyableTextNode WithSelectionColors(Color foreground, Color background)
    {
        SelectionForeground = foreground;
        SelectionBackground = background;
        return this;
    }

    public CopyableTextNode WithCopyBindings(params CopyKeyBinding[] bindings)
    {
        _copyBindings = bindings is { Length: > 0 }
            ? bindings.ToList()
            : [new(ConsoleKey.Enter), new(ConsoleKey.C, ConsoleModifiers.Control)];
        return this;
    }

    public CopyableTextNode WithFeedbackMode(CopyFeedbackMode mode)
    {
        FeedbackMode = mode;
        return this;
    }

    public CopyableTextNode WithToastPosition(ToastPosition position)
    {
        ToastPosition = position;
        return this;
    }

    public CopyableTextNode WithFeedbackDuration(TimeSpan duration)
    {
        FeedbackDuration = duration;
        return this;
    }

    public CopyableTextNode WithInlineIndicator(string text, Color? foreground = null)
    {
        InlineIndicatorText = text;
        if (foreground.HasValue)
            InlineIndicatorForeground = foreground.Value;
        return this;
    }

    public void OnFocused()
    {
        _hasFocus = true;
        _cursorPosition = DisplayWidth.ClampToTextElementBoundary(Content, _cursorPosition);
        TerminaTrace.Focus.Debug(this, "CopyableTextNode focused: contentLength={0}", Content.Length);
        Invalidate();
    }

    public void OnBlurred()
    {
        _hasFocus = false;
        TerminaTrace.Focus.Debug(this, "CopyableTextNode blurred");
        Invalidate();
    }

    public bool HandleInput(ConsoleKeyInfo key)
    {
        TerminaTrace.Input.Trace(this, "CopyableTextNode.HandleInput: key={0}, hasFocus={1}", key.Key, _hasFocus);

        bool handled;

        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && (key.KeyChar == 'a' || key.KeyChar == 'A'))
        {
            SelectAll();
            handled = true;
        }
        else if (IsCopyBinding(key))
        {
            TryCopy();
            handled = true;
        }
        else
        {
            handled = key.Key switch
            {
                ConsoleKey.LeftArrow => MoveCursor(-1, key.Modifiers),
                ConsoleKey.RightArrow => MoveCursor(1, key.Modifiers),
                ConsoleKey.Home => MoveToBoundary(0, key.Modifiers),
                ConsoleKey.End => MoveToBoundary(Content.Length, key.Modifiers),
                ConsoleKey.Escape => ClearSelection(),
                _ => false
            };
        }

        if (handled)
        {
            Invalidate();
        }

        return handled;
    }

    public override Size Measure(Size available)
    {
        var lines = BuildRenderedLines(available.Width > 0 ? available.Width : Math.Max(1, Content.Length));
        var hintHeight = string.IsNullOrWhiteSpace(Hint) ? 0 : 1;
        return new Size(available.Width, lines.Count + hintHeight);
    }

    public override void Render(IRenderContext context, Rect bounds)
    {
        if (!bounds.HasArea)
            return;

        var nodeContext = context.CreateSubContext(bounds);
        var lines = BuildRenderedLines(bounds.Width);
        var hasHint = !string.IsNullOrWhiteSpace(Hint);
        var lineCount = hasHint
            ? Math.Min(lines.Count, Math.Max(0, bounds.Height - 1))
            : Math.Min(lines.Count, bounds.Height);

        nodeContext.SetForeground(Foreground);
        nodeContext.Fill(0, 0, bounds.Width, lineCount, ' ');

        for (var i = 0; i < lineCount; i++)
        {
            RenderLine(nodeContext, i, lines[i], bounds.Width);
        }

        nodeContext.ResetColors();

        if (_showInlineIndicator && bounds.Width > 0 && lineCount > 0)
        {
            var inlineText = DisplayWidth.GetColumnCount(InlineIndicatorText) > bounds.Width
                ? DisplayWidth.TruncateToColumns(InlineIndicatorText, bounds.Width)
                : InlineIndicatorText;
            var indicatorX = Math.Max(0, bounds.Width - DisplayWidth.GetColumnCount(inlineText));
            nodeContext.SetForeground(InlineIndicatorForeground);
            nodeContext.WriteAt(indicatorX, 0, inlineText);
            nodeContext.ResetColors();
        }

        if (hasHint && bounds.Height > lineCount)
        {
            nodeContext.SetForeground(_hasFocus ? Color.BrightBlack : Color.Gray);
            nodeContext.WriteAt(0, lineCount, DisplayWidth.GetColumnCount(Hint!) > bounds.Width
                ? DisplayWidth.TruncateToColumns(Hint!, bounds.Width)
                : Hint!);
            nodeContext.ResetColors();
        }
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _inlineIndicatorSubscription?.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Copies the selected display text, or the complete semantic content when no
    /// selection exists.
    /// </summary>
    /// <returns>True when at least one clipboard transport reported success.</returns>
    public bool TryCopy()
    {
        var textToCopy = HasSelection ? SelectedText : SemanticContent;
        TerminaTrace.Input.Info(this, "CopyableTextNode copy requested: contentLength={0}, selectionLength={1}", Content.Length, textToCopy.Length);
        var success = _clipboardService.Copy(textToCopy);
        ShowFeedback(success);
        return success;
    }

    private bool IsCopyBinding(ConsoleKeyInfo key)
    {
        return _copyBindings.Any(binding => binding.Key == key.Key && binding.Modifiers == key.Modifiers);
    }

    private void ShowFeedback(bool success)
    {
        if (!success)
            return;

        if (FeedbackMode is CopyFeedbackMode.Toast or CopyFeedbackMode.ToastAndInline)
        {
            _toastService?.Show("Copied to clipboard", new ToastOptions(FeedbackDuration, ToastPosition));
        }

        if (FeedbackMode is CopyFeedbackMode.InlineIndicator or CopyFeedbackMode.ToastAndInline)
        {
            _showInlineIndicator = true;
            _inlineIndicatorSubscription?.Dispose();
            var ticks = Observable.Interval(FeedbackDuration, GetTimeProvider()).Take(1);
            if (GetFrameProvider() is { } frameProvider)
                ticks = ticks.ObserveOn(frameProvider);

            _inlineIndicatorSubscription = ticks
                .Subscribe(_ =>
                {
                    _showInlineIndicator = false;
                    Invalidate();
                });
            Invalidate();
        }
    }

    private TimeProvider GetTimeProvider() => RuntimeContext?.TimeProvider ?? TimeProvider.System;

    private FrameProvider? GetFrameProvider() => RuntimeContext?.RenderFrameProvider;

    private bool MoveCursor(int delta, ConsoleModifiers modifiers)
    {
        if (Content.Length == 0)
            return false;

        var newPosition = delta < 0
            ? DisplayWidth.GetPreviousTextElementIndex(Content, _cursorPosition)
            : DisplayWidth.GetNextTextElementIndex(Content, _cursorPosition);
        UpdateSelectionForMove(modifiers, newPosition);
        return true;
    }

    private bool MoveToBoundary(int position, ConsoleModifiers modifiers)
    {
        var newPosition = DisplayWidth.ClampToTextElementBoundary(Content, position);
        UpdateSelectionForMove(modifiers, newPosition);
        return true;
    }

    private bool ClearSelection()
    {
        if (!HasSelection)
            return false;

        _selectionStart = -1;
        return true;
    }

    private void SelectAll()
    {
        _selectionStart = 0;
        _cursorPosition = Content.Length;
    }

    private void UpdateSelectionForMove(ConsoleModifiers modifiers, int newPosition)
    {
        if (modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            if (_selectionStart < 0)
                _selectionStart = _cursorPosition;
        }
        else
        {
            _selectionStart = -1;
        }

        _cursorPosition = newPosition;
    }

    private void RenderLine(IRenderContext context, int row, RenderedLine line, int width)
    {
        var column = 0;
        foreach (var cell in DisplayWidth.EnumerateCells(line.Text))
        {
            if (column >= width)
                break;

            var sourceIndex = line.StartIndex + cell.StartIndex;
            var isSelected = IsSelected(sourceIndex);
            var isCursor = _hasFocus && !HasSelection && sourceIndex == _cursorPosition;

            if (isSelected)
            {
                context.SetForeground(SelectionForeground);
                context.SetBackground(SelectionBackground);
            }
            else if (isCursor)
            {
                context.SetForeground(FocusedForeground);
                context.SetBackground(FocusedBackground);
            }
            else
            {
                context.SetForeground(Foreground);
                context.SetBackground(Color.Default);
            }

            context.WriteAt(column, row, cell.Text);
            column += cell.ColumnWidth;
        }

        if (_hasFocus && !HasSelection && column < width && _cursorPosition == line.StartIndex + line.Text.Length)
        {
            context.SetForeground(FocusedForeground);
            context.SetBackground(FocusedBackground);
            context.WriteAt(column, row, ' ');
        }

        context.ResetColors();
    }

    private bool IsSelected(int sourceIndex)
    {
        if (!HasSelection)
            return false;

        var start = Math.Min(_selectionStart, _cursorPosition);
        var end = Math.Max(_selectionStart, _cursorPosition);
        return sourceIndex >= start && sourceIndex < end;
    }

    private List<RenderedLine> BuildRenderedLines(int width)
    {
        if (width <= 0)
            return [new RenderedLine(string.Empty, 0)];

        var rendered = new List<RenderedLine>();
        var sourceLines = Content.Split('\n');
        var sourceOffset = 0;

        for (var i = 0; i < sourceLines.Length; i++)
        {
            var line = sourceLines[i];
            if (line.Length == 0)
            {
                rendered.Add(new RenderedLine(string.Empty, sourceOffset));
            }
            else
            {
                var chunkStart = 0;
                var chunkEnd = 0;
                var columns = 0;

                foreach (var cell in DisplayWidth.EnumerateCells(line))
                {
                    if (columns > 0 && columns + cell.ColumnWidth > width)
                    {
                        rendered.Add(new RenderedLine(line[chunkStart..chunkEnd], sourceOffset + chunkStart));
                        chunkStart = cell.StartIndex;
                        columns = 0;
                    }

                    chunkEnd = cell.StartIndex + cell.Length;
                    columns += cell.ColumnWidth;
                }

                if (chunkEnd > chunkStart)
                {
                    rendered.Add(new RenderedLine(line[chunkStart..chunkEnd], sourceOffset + chunkStart));
                }
            }

            sourceOffset += line.Length;
            if (i < sourceLines.Length - 1)
                sourceOffset += 1;
        }

        return rendered;
    }

    private void Invalidate()
    {
        if (!_disposed)
            _invalidated.OnNext(Unit.Default);
    }

    private sealed record RenderedLine(string Text, int StartIndex);
}
