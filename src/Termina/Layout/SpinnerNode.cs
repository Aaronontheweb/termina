// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// Spinner animation styles.
/// </summary>
public enum SpinnerStyle
{
    /// <summary>
    /// Dots: ⠋ ⠙ ⠹ ⠸ ⠼ ⠴ ⠦ ⠧ ⠇ ⠏
    /// </summary>
    Dots,

    /// <summary>
    /// Line: - \ | /
    /// </summary>
    Line,

    /// <summary>
    /// Arrow: ← ↖ ↑ ↗ → ↘ ↓ ↙
    /// </summary>
    Arrow,

    /// <summary>
    /// Bounce: ⠁ ⠂ ⠄ ⠂
    /// </summary>
    Bounce,

    /// <summary>
    /// Box: ▖ ▘ ▝ ▗
    /// </summary>
    Box,

    /// <summary>
    /// Circle: ◐ ◓ ◑ ◒
    /// </summary>
    Circle
}

/// <summary>
/// A stateful layout node that displays an animated spinner.
/// Manages its own animation timer internally.
/// </summary>
public sealed class SpinnerNode : LayoutNode, IAnimatedNode, IInvalidatingNode
{
    private static readonly Dictionary<SpinnerStyle, string[]> Frames = new()
    {
        [SpinnerStyle.Dots] = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"],
        [SpinnerStyle.Line] = ["-", "\\", "|", "/"],
        [SpinnerStyle.Arrow] = ["←", "↖", "↑", "↗", "→", "↘", "↓", "↙"],
        [SpinnerStyle.Bounce] = ["⠁", "⠂", "⠄", "⠂"],
        [SpinnerStyle.Box] = ["▖", "▘", "▝", "▗"],
        [SpinnerStyle.Circle] = ["◐", "◓", "◑", "◒"]
    };

    private readonly int _intervalMs;
    private readonly string[] _frames;
    private readonly Subject<Unit> _invalidated = new();
    private IDisposable? _timerSubscription;
    private int _currentFrame;

    /// <summary>
    /// Text to display after the spinner.
    /// </summary>
    public string? Label { get; private set; }

    /// <summary>
    /// Spinner color.
    /// </summary>
    public Color? SpinnerColor { get; private set; }

    /// <summary>
    /// Label color.
    /// </summary>
    public Color? LabelColor { get; private set; }

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <inheritdoc />
    public bool IsAnimating { get; private set; }

    public SpinnerNode(SpinnerStyle style = SpinnerStyle.Dots, int intervalMs = 80)
    {
        _frames = Frames[style];
        _intervalMs = intervalMs;

        HeightConstraint = new SizeConstraint.Fixed(1);
        WidthConstraint = new SizeConstraint.Auto();
    }

    /// <summary>
    /// Set the label text.
    /// </summary>
    public SpinnerNode WithLabel(string label)
    {
        Label = label;
        return this;
    }

    /// <summary>
    /// Set the spinner color.
    /// </summary>
    public SpinnerNode WithSpinnerColor(Color color)
    {
        SpinnerColor = color;
        return this;
    }

    /// <summary>
    /// Set the label color.
    /// </summary>
    public SpinnerNode WithLabelColor(Color color)
    {
        LabelColor = color;
        return this;
    }

    /// <inheritdoc />
    public void Start()
    {
        if (!IsAnimating)
        {
            IsAnimating = true;
            var ticks = Observable.Interval(TimeSpan.FromMilliseconds(_intervalMs), GetTimeProvider());
            if (GetFrameProvider() is { } frameProvider)
                ticks = ticks.ObserveOn(frameProvider);

            _timerSubscription ??= ticks
                .Subscribe(_ =>
                {
                    _currentFrame = (_currentFrame + 1) % _frames.Length;
                    _invalidated.OnNext(Unit.Default);
                });
        }
    }

    /// <inheritdoc />
    public override void SetRuntimeContext(LayoutRuntimeContext context)
    {
        var restart = IsAnimating;
        if (restart)
            Stop();

        base.SetRuntimeContext(context);

        if (restart)
            Start();
    }

    private TimeProvider GetTimeProvider() => RuntimeContext?.TimeProvider ?? TimeProvider.System;

    private FrameProvider? GetFrameProvider() => RuntimeContext?.RenderFrameProvider;

    /// <inheritdoc />
    public void Stop()
    {
        if (IsAnimating)
        {
            _timerSubscription?.Dispose();
            _timerSubscription = null;
            IsAnimating = false;
        }
    }

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        var frameWidth = DisplayWidth.GetColumnCount(_frames[0]);
        var labelWidth = string.IsNullOrEmpty(Label) ? 0 : DisplayWidth.GetColumnCount(Label) + 1; // +1 for space
        var totalWidth = frameWidth + labelWidth;

        var width = WidthConstraint.Compute(available.Width, totalWidth, available.Width);
        return new Size(width, 1);
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        if (!bounds.HasArea)
            return;

        // Create a sub-context so coordinates are relative to this node's bounds
        var spinnerContext = context.CreateSubContext(bounds);

        var frame = _frames[_currentFrame];
        var x = 0;

        // Draw spinner
        if (SpinnerColor.HasValue)
            spinnerContext.SetForeground(SpinnerColor.Value);
        spinnerContext.WriteAt(x, 0, frame);
        x += DisplayWidth.GetColumnCount(frame);

        // Draw label
        if (!string.IsNullOrEmpty(Label))
        {
            if (LabelColor.HasValue)
                spinnerContext.SetForeground(LabelColor.Value);
            else if (SpinnerColor.HasValue)
                spinnerContext.ResetColors();

            spinnerContext.WriteAt(x, 0, " ");
            x++;

            var maxLabelLen = bounds.Width - x;
            var displayLabel = DisplayWidth.GetColumnCount(Label) > maxLabelLen ? DisplayWidth.TruncateToColumns(Label, maxLabelLen) : Label;
            spinnerContext.WriteAt(x, 0, displayLabel);
        }

        if (SpinnerColor.HasValue || LabelColor.HasValue)
            spinnerContext.ResetColors();
    }

    /// <inheritdoc />
    public override void OnActivate()
    {
        // Resume animation
        Start();
        base.OnActivate();
    }

    /// <inheritdoc />
    public override void OnDeactivate()
    {
        // Stop animation to conserve resources
        Stop();
        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Stop();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
        base.Dispose();
    }
}
