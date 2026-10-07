// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// Defines automatic scrolling behavior for scrollable containers.
/// </summary>
public enum AutoScrollPolicy
{
    /// <summary>
    /// No automatic scrolling - scroll position is manually controlled.
    /// </summary>
    None,

    /// <summary>
    /// Automatically scroll to bottom when new content is added,
    /// but only if already at or near the bottom.
    /// </summary>
    TailWhenAtBottom,

    /// <summary>
    /// Always scroll to bottom when content changes.
    /// </summary>
    AlwaysTail
}

/// <summary>
/// A layout node that provides vertical scrolling for content that exceeds the viewport.
/// Manages scroll position state internally.
/// </summary>
public sealed class ScrollableContainerNode : LayoutNode, IInvalidatingNode, IScrollable, IPointerScrollable
{
    private ILayoutNode _content = new EmptyNode();
    private int _scrollOffset;
    private int _contentHeight;
    private int _viewportHeight;
    private int _previousContentHeight;
    private ScreenBounds _lastRenderedBounds = ScreenBounds.Empty;
    private readonly Subject<Unit> _invalidated = new();
    private IDisposable? _contentSubscription;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <summary>
    /// Gets or sets the automatic scroll policy.
    /// </summary>
    public AutoScrollPolicy AutoScroll { get; set; } = AutoScrollPolicy.TailWhenAtBottom;

    /// <summary>
    /// Gets or sets whether to display a scrollbar.
    /// </summary>
    public bool ShowScrollbar { get; set; } = true;

    /// <summary>
    /// Gets or sets the scrollbar track color.
    /// </summary>
    public Color ScrollbarTrackColor { get; set; } = Color.BrightBlack;

    /// <summary>
    /// Gets or sets the scrollbar thumb color.
    /// </summary>
    public Color ScrollbarThumbColor { get; set; } = Color.White;

    /// <summary>
    /// Gets the current scroll offset.
    /// </summary>
    public int ScrollOffset => _scrollOffset;

    /// <summary>
    /// Gets the total content height.
    /// </summary>
    public int ContentHeight => _contentHeight;

    /// <summary>
    /// Gets whether scrolling down is possible.
    /// </summary>
    public bool CanScrollDown => _scrollOffset < MaxScroll;

    /// <summary>
    /// Gets whether scrolling up is possible.
    /// </summary>
    public bool CanScrollUp => _scrollOffset > 0;

    /// <summary>
    /// Gets the maximum scroll offset.
    /// </summary>
    public int MaxScroll => Math.Max(0, _contentHeight - _viewportHeight);

    /// <summary>
    /// Gets whether the view is at or near the bottom (within a few lines).
    /// </summary>
    public bool IsNearBottom => _scrollOffset >= MaxScroll - 2;

    ScreenBounds IPointerScrollable.LastRenderedBounds => _lastRenderedBounds;

    /// <summary>
    /// Set the content to scroll.
    /// </summary>
    public ScrollableContainerNode WithContent(ILayoutNode content)
    {
        // Dispose the previous content's subscription, and deactivate the previous
        // content (active/inactive pattern) rather than dispose it. Content is
        // disposed only at teardown, in Dispose().
        _contentSubscription?.Dispose();
        if (_content is IActivatableNode oldContent)
            oldContent.OnDeactivate();
        _content = content;

        // If content is invalidating, subscribe to the observable
        if (_content is IInvalidatingNode invalidating)
        {
            _contentSubscription = invalidating.Invalidated.Subscribe(_ => OnContentInvalidated());
        }

        return this;
    }

    /// <summary>
    /// Set the auto-scroll policy.
    /// </summary>
    public ScrollableContainerNode WithAutoScroll(AutoScrollPolicy policy)
    {
        AutoScroll = policy;
        return this;
    }

    /// <summary>
    /// Enable or disable the scrollbar.
    /// </summary>
    public ScrollableContainerNode WithScrollbar(bool show)
    {
        ShowScrollbar = show;
        return this;
    }

    /// <summary>
    /// Set scrollbar colors.
    /// </summary>
    public ScrollableContainerNode WithScrollbarColors(Color track, Color thumb)
    {
        ScrollbarTrackColor = track;
        ScrollbarThumbColor = thumb;
        return this;
    }

    /// <summary>
    /// Scroll down by one line.
    /// </summary>
    public void ScrollDown()
    {
        if (CanScrollDown)
        {
            _scrollOffset++;
            _invalidated.OnNext(Unit.Default);
        }
    }

    /// <summary>
    /// Scroll up by one line.
    /// </summary>
    public void ScrollUp()
    {
        if (CanScrollUp)
        {
            _scrollOffset--;
            _invalidated.OnNext(Unit.Default);
        }
    }

    void IScrollable.ScrollDown(int lines) => ScrollTo(_scrollOffset + Math.Max(0, lines));

    void IScrollable.ScrollUp(int lines) => ScrollTo(_scrollOffset - Math.Max(0, lines));

    /// <summary>
    /// Scroll down by a page.
    /// </summary>
    public void PageDown()
    {
        var newOffset = Math.Min(_scrollOffset + _viewportHeight, MaxScroll);
        if (newOffset != _scrollOffset)
        {
            _scrollOffset = newOffset;
            _invalidated.OnNext(Unit.Default);
        }
    }

    /// <summary>
    /// Scroll up by a page.
    /// </summary>
    public void PageUp()
    {
        var newOffset = Math.Max(_scrollOffset - _viewportHeight, 0);
        if (newOffset != _scrollOffset)
        {
            _scrollOffset = newOffset;
            _invalidated.OnNext(Unit.Default);
        }
    }

    /// <summary>
    /// Scroll to a specific offset.
    /// </summary>
    public void ScrollTo(int offset)
    {
        var newOffset = Math.Clamp(offset, 0, MaxScroll);
        if (newOffset != _scrollOffset)
        {
            _scrollOffset = newOffset;
            _invalidated.OnNext(Unit.Default);
        }
    }

    /// <summary>
    /// Scroll to the top.
    /// </summary>
    public void ScrollToTop()
    {
        ScrollTo(0);
    }

    /// <summary>
    /// Scroll to the bottom.
    /// </summary>
    public void ScrollToBottom()
    {
        ScrollTo(MaxScroll);
    }

    private void OnContentInvalidated()
    {
        // Content changed - check if we need to auto-scroll
        ApplyAutoScrollPolicy();
        _invalidated.OnNext(Unit.Default);
    }

    private void ApplyAutoScrollPolicy(bool? wasNearBottom = null)
    {
        switch (AutoScroll)
        {
            case AutoScrollPolicy.AlwaysTail:
                _scrollOffset = MaxScroll;
                break;
            case AutoScrollPolicy.TailWhenAtBottom:
                // If we were at or near the bottom before, stay at the bottom
                if ((wasNearBottom ?? IsNearBottom) || _previousContentHeight <= _viewportHeight)
                {
                    _scrollOffset = MaxScroll;
                }
                break;
            case AutoScrollPolicy.None:
            default:
                // Clamp to valid range
                _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScroll);
                break;
        }
    }

    /// <inheritdoc />
    internal override IEnumerable<ILayoutNode> GetChildNodes() => [_content];

    /// <inheritdoc />
    internal override void DisconnectChildInvalidationSubscriptions()
    {
        _contentSubscription?.Dispose();
        _contentSubscription = null;
    }

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        var wasNearBottom = IsNearBottom;

        // Calculate available width accounting for scrollbar
        var contentWidth = ShowScrollbar && available.Width > 1
            ? available.Width - 1
            : available.Width;

        // Measure content with unlimited height to get true content size
        var contentAvailable = new Size(contentWidth, int.MaxValue / 2);
        var contentSize = _content.Measure(contentAvailable);

        _previousContentHeight = _contentHeight;
        _contentHeight = contentSize.Height;
        _viewportHeight = HeightConstraint.Compute(available.Height, contentSize.Height, available.Height);

        // Apply auto-scroll if content height changed
        if (_contentHeight != _previousContentHeight)
        {
            ApplyAutoScrollPolicy(wasNearBottom);
        }

        var width = WidthConstraint.Compute(available.Width, contentSize.Width + (ShowScrollbar ? 1 : 0), available.Width);

        return new Size(width, _viewportHeight);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        _lastRenderedBounds = context is IScreenPositionedRenderContext positioned
            ? positioned.GetScreenBounds(bounds)
            : ScreenBounds.Empty;

        if (!bounds.HasArea)
            return;

        // Offset the rendering context to this node's slot so inner writes
        // land at the correct terminal row instead of always writing at row 0.
        // This is the same pattern every other node uses (PanelNode,
        // StreamingTextNode, SelectionListNode, etc.).
        var boundsContext = context.CreateSubContext(bounds);

        _viewportHeight = bounds.Height;
        var contentWidth = ShowScrollbar && bounds.Width > 1
            ? bounds.Width - 1
            : bounds.Width;

        // Create a clipped/scrolled render context for content
        var contentBounds = new Rect(0, 0, contentWidth, bounds.Height);
        var scrolledContext = new ScrolledRenderContext(boundsContext, 0, -_scrollOffset, contentWidth, _contentHeight);

        // Render content
        _content.Render(scrolledContext, new Rect(0, 0, contentWidth, _contentHeight));

        // Draw scrollbar if needed
        if (ShowScrollbar && _contentHeight > _viewportHeight)
        {
            DrawScrollbar(boundsContext, bounds);
        }
    }

    private void DrawScrollbar(IRenderContext context, Rect bounds)
    {
        var x = bounds.Width - 1;
        var trackHeight = bounds.Height;

        if (trackHeight <= 0 || _contentHeight <= _viewportHeight)
            return;

        // Calculate thumb size and position
        var thumbHeight = Math.Max(1, (int)((float)_viewportHeight / _contentHeight * trackHeight));
        var maxThumbTop = trackHeight - thumbHeight;
        var thumbTop = MaxScroll > 0
            ? (int)((float)_scrollOffset / MaxScroll * maxThumbTop)
            : 0;

        // Draw track and thumb
        for (var y = 0; y < trackHeight; y++)
        {
            if (y >= thumbTop && y < thumbTop + thumbHeight)
            {
                // Thumb
                context.SetForeground(ScrollbarThumbColor);
                context.WriteAt(x, y, '█');
            }
            else
            {
                // Track
                context.SetForeground(ScrollbarTrackColor);
                context.WriteAt(x, y, '░');
            }
        }

        context.ResetColors();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _contentSubscription?.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
        _content.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// A render context that applies vertical scroll offset.
    /// </summary>
    private sealed class ScrolledRenderContext : IRenderContext, IScreenPositionedRenderContext
    {
        private readonly IRenderContext _parent;
        private readonly int _offsetX;
        private readonly int _offsetY;
        private readonly int _clipHeight;

        public ScrolledRenderContext(IRenderContext parent, int offsetX, int offsetY, int width, int clipHeight)
        {
            _parent = parent;
            _offsetX = offsetX;
            _offsetY = offsetY;
            Width = width;
            _clipHeight = clipHeight;
        }

        public int Width { get; }
        public int Height => _clipHeight;

        public void WriteAt(int x, int y, string text)
        {
            var actualY = y + _offsetY;
            if (actualY < 0 || actualY >= _parent.Height)
                return;
            text = DisplayWidth.SanitizeTerminalText(text);
            var skipColumns = 0;
            if (x < 0)
            {
                skipColumns = -x;
                x = 0;
            }
            if (x >= Width)
                return;
            text = DisplayWidth.SliceByColumns(text, skipColumns, Width - x);
            if (string.IsNullOrEmpty(text))
                return;
            _parent.WriteAt(x + _offsetX, actualY, text);
        }

        public void WriteAt(int x, int y, char c)
        {
            WriteAt(x, y, c.ToString());
        }

        public void SetForeground(Color color) => _parent.SetForeground(color);
        public void SetBackground(Color color) => _parent.SetBackground(color);
        public void ResetColors() => _parent.ResetColors();
        public void SetDecoration(Terminal.TextDecoration decoration) => _parent.SetDecoration(decoration);
        public void ApplyStyle(Terminal.TextStyle style) => _parent.ApplyStyle(style);

        public void Fill(int x, int y, int width, int height, char c = ' ')
        {
            var actualY = y + _offsetY;
            if (actualY < 0)
            {
                height += actualY;
                actualY = 0;
            }
            if (actualY + height > _parent.Height)
                height = _parent.Height - actualY;
            if (height <= 0)
                return;
            _parent.Fill(x + _offsetX, actualY, width, height, c);
        }

        public void Clear() => Fill(0, 0, Width, Height, ' ');

        public IRenderContext CreateSubContext(Rect bounds)
        {
            var clippedX = Math.Max(0, bounds.X);
            var clippedY = Math.Max(0, bounds.Y);
            var clippedRight = Math.Min(Width, bounds.Right);
            var clippedBottom = Math.Min(_clipHeight, bounds.Bottom);
            var clippedWidth = Math.Max(0, clippedRight - clippedX);
            var clippedHeight = Math.Max(0, clippedBottom - clippedY);

            return new ScrolledRenderContext(
                _parent,
                _offsetX + clippedX,
                _offsetY + clippedY,
                clippedWidth,
                clippedHeight);
        }

        ScreenBounds IScreenPositionedRenderContext.GetScreenBounds(Rect bounds)
        {
            var clippedX = Math.Max(0, bounds.X);
            var clippedY = Math.Max(0, bounds.Y);
            var clippedRight = Math.Min(Width, bounds.Right);
            var clippedBottom = Math.Min(_clipHeight, bounds.Bottom);

            if (clippedRight <= clippedX || clippedBottom <= clippedY
                || _parent is not IScreenPositionedRenderContext positioned)
            {
                return ScreenBounds.Empty;
            }

            return positioned.GetScreenBounds(new Rect(
                _offsetX + clippedX,
                _offsetY + clippedY,
                clippedRight - clippedX,
                clippedBottom - clippedY));
        }
    }
}
