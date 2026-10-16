// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Diagnostics;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Layout;

/// <summary>
/// A multi-step wizard component with progress indicator, step content, and navigation.
/// </summary>
/// <typeparam name="TStep">Enum type representing the wizard steps.</typeparam>
public sealed class WizardNode<TStep> : LayoutNode, IFocusable, IInvalidatingNode
    where TStep : struct, Enum
{
    private readonly List<WizardStepConfig<TStep>> _steps = new();
    private readonly Dictionary<TStep, int> _stepIndex = new();
    private readonly Subject<TStep> _stepChanged = new();
    private readonly Subject<WizardBeforeAdvanceArgs<TStep>> _beforeAdvance = new();
    private readonly Subject<Unit> _completed = new();
    private readonly Subject<Unit> _invalidated = new();

    private int _currentStepIndex;
    private int _currentSubStep;
    private bool _hasFocus;
    private bool _isActive;
    private IFocusable? _focusedChild;

    // Internal layout
    private KeyedDynamicLayoutNode<int>? _contentNode;
    private IDisposable? _contentInvalidationSubscription;
    private string? _title;
    private WizardProgressStyle _progressStyle = WizardProgressStyle.BlockBar;
    private BorderStyle? _borderStyle;
    private Color? _borderColor;

    /// <summary>
    /// Observable that emits when the current step changes.
    /// </summary>
    public Observable<TStep> StepChanged => _stepChanged;

    /// <summary>
    /// Observable that fires before advancing. Subscribe to set <see cref="WizardBeforeAdvanceArgs{TStep}.Cancel"/> for validation.
    /// </summary>
    public Observable<WizardBeforeAdvanceArgs<TStep>> BeforeAdvance => _beforeAdvance;

    /// <summary>
    /// Observable that emits when the wizard completes (advances past the last step).
    /// </summary>
    public Observable<Unit> Completed => _completed;

    /// <inheritdoc />
    public Observable<Unit> Invalidated => _invalidated;

    /// <summary>
    /// Gets the current step.
    /// </summary>
    public TStep CurrentStep => _steps.Count > 0 ? _steps[_currentStepIndex].Step : default;

    /// <summary>
    /// Gets the current sub-step index (0-based).
    /// </summary>
    public int CurrentSubStep => _currentSubStep;

    /// <summary>
    /// Gets the total number of steps.
    /// </summary>
    public int StepCount => _steps.Count;

    #region IFocusable

    /// <inheritdoc />
    public bool CanFocus => true;

    /// <inheritdoc />
    public bool HasFocus => _hasFocus;

    /// <inheritdoc />
    public int FocusPriority => 10;

    /// <inheritdoc />
    public void OnFocused()
    {
        _hasFocus = true;
        PropagateFocusToContent();
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public void OnBlurred()
    {
        _hasFocus = false;
        BlurContentChild();
        _invalidated.OnNext(Unit.Default);
    }

    /// <inheritdoc />
    public bool HandleInput(ConsoleKeyInfo key)
    {
        TerminaTrace.Input.Debug(this, "HandleInput: key={0}, contentNode={1}",
            key.Key, _contentNode != null ? "exists" : "null");

        // Delegate to focusable nodes in step content first (recursive tree walk)
        if (_contentNode != null && DelegateInputToContent(_contentNode, key))
        {
            TerminaTrace.Input.Debug(this, "HandleInput: delegated to child, key={0}", key.Key);
            return true;
        }

        // Then handle wizard-level keys
        var result = key.Key switch
        {
            ConsoleKey.Enter => TryAdvance(),
            ConsoleKey.Tab when key.Modifiers == 0 => TryAdvance(),
            ConsoleKey.Escape => TryGoBack(),
            ConsoleKey.Tab when (key.Modifiers & ConsoleModifiers.Shift) != 0 => TryGoBack(),
            _ => false
        };
        TerminaTrace.Input.Debug(this, "HandleInput: wizard handled key={0}, result={1}", key.Key, result);
        return result;
    }

    /// <summary>
    /// Recursively walk the content tree to find and delegate input to focusable children.
    /// </summary>
    private static bool DelegateInputToContent(ILayoutNode node, ConsoleKeyInfo key)
    {
        TerminaTrace.Input.Trace(node, "DelegateInput: visiting {0}, IsFocusable={1}, IsLayoutNode={2}",
            node.GetType().Name, node is IFocusable, node is LayoutNode);

        // Check if this node is focusable and can handle input
        if (node is IFocusable { CanFocus: true } focusable)
        {
            TerminaTrace.Input.Debug(node, "DelegateInput: forwarding key={0} to {1}",
                key.Key, node.GetType().Name);
            if (focusable.HandleInput(key))
                return true;
            TerminaTrace.Input.Debug(node, "DelegateInput: {0} did not handle key={1}",
                node.GetType().Name, key.Key);
        }

        // Recurse into LayoutNode children (which expose GetChildNodes)
        if (node is LayoutNode layoutNode)
        {
            var children = layoutNode.GetChildNodes();
            TerminaTrace.Input.Trace(node, "DelegateInput: recursing into {0} children of {1}",
                children.Count(), node.GetType().Name);
            foreach (var child in children)
            {
                if (DelegateInputToContent(child, key))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Propagate focus to the first focusable child in the current step content.
    /// </summary>
    private void PropagateFocusToContent()
    {
        BlurContentChild();

        if (_steps.Count == 0 || _contentNode == null)
            return;

        _focusedChild = FindFirstFocusable(_contentNode);
        _focusedChild?.OnFocused();
    }

    /// <summary>
    /// Blur the currently focused content child.
    /// </summary>
    private void BlurContentChild()
    {
        if (_focusedChild is { HasFocus: true })
            _focusedChild.OnBlurred();
        _focusedChild = null;
    }

    /// <summary>
    /// Walk the layout tree depth-first to find the first focusable node.
    /// </summary>
    private static IFocusable? FindFirstFocusable(ILayoutNode node)
    {
        if (node is IFocusable { CanFocus: true } focusable)
            return focusable;

        if (node is LayoutNode layoutNode)
        {
            foreach (var child in layoutNode.GetChildNodes())
            {
                var found = FindFirstFocusable(child);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    #endregion

    #region Fluent Builder

    /// <summary>
    /// Add a step to the wizard.
    /// </summary>
    public WizardNode<TStep> WithStep(TStep step, string displayName, Func<ILayoutNode> contentFactory,
        string? helpText = null, int subSteps = 1)
    {
        var config = new WizardStepConfig<TStep>(step, displayName, contentFactory, helpText, subSteps);
        _stepIndex[step] = _steps.Count;
        _steps.Add(config);
        return this;
    }

    /// <summary>
    /// Set the progress bar style.
    /// </summary>
    public WizardNode<TStep> WithProgressStyle(WizardProgressStyle style)
    {
        _progressStyle = style;
        return this;
    }

    /// <summary>
    /// Set the wizard title.
    /// </summary>
    public WizardNode<TStep> WithTitle(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>
    /// Add a border around the wizard.
    /// </summary>
    public WizardNode<TStep> WithBorder(BorderStyle style = BorderStyle.Single, Color? color = null)
    {
        _borderStyle = style;
        _borderColor = color;
        return this;
    }

    #endregion

    #region Navigation

    /// <summary>
    /// Try to advance to the next step. Returns true if navigation occurred.
    /// </summary>
    public bool TryAdvance()
    {
        if (_steps.Count == 0)
            return false;

        var currentConfig = _steps[_currentStepIndex];

        // Check sub-steps first
        if (_currentSubStep < currentConfig.SubStepCount - 1)
        {
            _currentSubStep++;
            _contentNode?.Invalidate();
            if (_hasFocus)
                PropagateFocusToContent();
            _invalidated.OnNext(Unit.Default);
            return true;
        }

        // On last step — fire completed
        if (_currentStepIndex >= _steps.Count - 1)
        {
            _completed.OnNext(Unit.Default);
            return true;
        }

        // Fire BeforeAdvance for validation
        var nextStep = _steps[_currentStepIndex + 1].Step;
        var args = new WizardBeforeAdvanceArgs<TStep>(CurrentStep, nextStep);
        _beforeAdvance.OnNext(args);

        if (args.Cancel)
            return false;

        _currentStepIndex++;
        _currentSubStep = 0;
        _contentNode?.Invalidate();
        if (_hasFocus)
            PropagateFocusToContent();
        _invalidated.OnNext(Unit.Default);
        _stepChanged.OnNext(CurrentStep);
        return true;
    }

    /// <summary>
    /// Try to go back to the previous step. Returns true if navigation occurred.
    /// </summary>
    public bool TryGoBack()
    {
        if (_steps.Count == 0)
            return false;

        // Go back within sub-steps first
        if (_currentSubStep > 0)
        {
            _currentSubStep--;
            _contentNode?.Invalidate();
            if (_hasFocus)
                PropagateFocusToContent();
            _invalidated.OnNext(Unit.Default);
            return true;
        }

        if (_currentStepIndex <= 0)
            return false;

        _currentStepIndex--;
        // Return to last sub-step of previous step
        _currentSubStep = _steps[_currentStepIndex].SubStepCount - 1;
        _contentNode?.Invalidate();
        if (_hasFocus)
            PropagateFocusToContent();
        _invalidated.OnNext(Unit.Default);
        _stepChanged.OnNext(CurrentStep);
        return true;
    }

    /// <summary>
    /// Jump directly to a specific step.
    /// </summary>
    public bool GoToStep(TStep step)
    {
        if (!_stepIndex.TryGetValue(step, out var index))
            return false;

        if (index == _currentStepIndex)
            return false;

        _currentStepIndex = index;
        _currentSubStep = 0;
        _contentNode?.Invalidate();
        if (_hasFocus)
            PropagateFocusToContent();
        _invalidated.OnNext(Unit.Default);
        _stepChanged.OnNext(CurrentStep);
        return true;
    }

    /// <summary>
    /// Advance to the next sub-step within the current step.
    /// Returns false if already at the last sub-step.
    /// </summary>
    public bool AdvanceSubStep()
    {
        if (_steps.Count == 0)
            return false;

        var config = _steps[_currentStepIndex];
        if (_currentSubStep >= config.SubStepCount - 1)
            return false;

        _currentSubStep++;
        _contentNode?.Invalidate();
        if (_hasFocus)
            PropagateFocusToContent();
        _invalidated.OnNext(Unit.Default);
        return true;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    public override Size Measure(Size available)
    {
        if (_steps.Count == 0)
            return Size.Zero;

        var height = 0;
        var width = available.Width;

        // Progress bar takes 1 line (if shown)
        if (_progressStyle != WizardProgressStyle.None)
            height += 1;

        // Title takes 1 line (if set)
        if (_title != null)
            height += 1;

        // Content area fills remaining
        var contentHeight = Math.Max(1, available.Height - height - (_steps[_currentStepIndex].HelpText != null ? 1 : 0));
        height += contentHeight;

        // Help text takes 1 line
        if (_steps[_currentStepIndex].HelpText != null)
            height += 1;

        // Border adds 2 lines/cols
        if (_borderStyle.HasValue)
        {
            height += 2;
            width = Math.Max(0, width);
        }

        return new Size(
            WidthConstraint.Compute(available.Width, width, available.Width),
            HeightConstraint.Compute(available.Height, height, available.Height));
    }

    /// <inheritdoc />
    public override void Render(IRenderContext context, Rect bounds)
    {
        if (_steps.Count == 0 || !bounds.HasArea)
            return;

        var renderBounds = bounds;

        // Render border if configured
        if (_borderStyle.HasValue)
        {
            RenderBorder(context, bounds);
            // Shrink bounds for inner content
            renderBounds = new Rect(bounds.X + 1, bounds.Y + 1, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - 2));
        }

        var y = renderBounds.Y;
        var currentConfig = _steps[_currentStepIndex];

        // Render title
        if (_title != null)
        {
            context.WriteAt(renderBounds.X, y, _title);
            y++;
        }

        // Render progress bar
        if (_progressStyle != WizardProgressStyle.None && y < renderBounds.Bottom)
        {
            RenderProgress(context, new Rect(renderBounds.X, y, renderBounds.Width, 1));
            y++;
        }

        // Render help text at bottom
        var helpHeight = currentConfig.HelpText != null ? 1 : 0;
        var contentBottom = renderBounds.Bottom - helpHeight;

        // Render content area using DynamicLayoutNode
        if (y < contentBottom)
        {
            EnsureContentNode();
            var contentBounds = new Rect(renderBounds.X, y, renderBounds.Width, contentBottom - y);
            _contentNode!.Render(context, contentBounds);
        }

        // Render help text
        if (currentConfig.HelpText != null && contentBottom < renderBounds.Bottom)
        {
            var helpStyle = new TextStyle(Color.DarkGray);
            context.ApplyStyle(helpStyle);
            context.WriteAt(renderBounds.X, contentBottom, currentConfig.HelpText);
            context.ResetColors();
        }
    }

    private void EnsureContentNode()
    {
        if (_contentNode != null)
            return;

        _contentNode = new KeyedDynamicLayoutNode<int>(
            () => _currentStepIndex,
            stepIndex =>
            {
                if (_steps.Count == 0)
                    return new EmptyNode();
                return _steps[stepIndex].ContentFactory();
            });
        ApplyRuntimeContextToChild(_contentNode);

        // Propagate content invalidation (e.g., SelectionListNode highlight changes)
        // up through the wizard so the page triggers a redraw
        _contentInvalidationSubscription = _contentNode.Invalidated
            .Subscribe(_ => _invalidated.OnNext(Unit.Default));

        if (_isActive)
            _contentNode.OnActivate();

        // Eagerly evaluate so real content is available for focus finding and input delegation
        _contentNode.Invalidate();

        // If wizard already has focus (OnFocused called before first Render),
        // propagate focus to the content's first focusable child now
        if (_hasFocus && _focusedChild == null)
            PropagateFocusToContent();
    }

    private void RenderProgress(IRenderContext context, Rect bounds)
    {
        var text = _progressStyle switch
        {
            WizardProgressStyle.BlockBar => RenderBlockBar(bounds.Width),
            WizardProgressStyle.Arrow => RenderArrowChain(bounds.Width),
            WizardProgressStyle.Dots => RenderDots(bounds.Width),
            _ => string.Empty
        };

        if (text.Length > 0)
        {
            context.WriteAt(bounds.X, bounds.Y, DisplayWidth.GetColumnCount(text) > bounds.Width
                ? DisplayWidth.TruncateToColumns(text, bounds.Width)
                : text);
        }
    }

    private string RenderBlockBar(int width)
    {
        var label = $" Step {_currentStepIndex + 1} of {_steps.Count} ";
        var barWidth = Math.Max(0, width - label.Length - 2); // [====>    ]
        if (barWidth <= 0)
            return label;

        var filled = (int)((double)(_currentStepIndex + 1) / _steps.Count * barWidth);
        filled = Math.Clamp(filled, 0, barWidth);

        var bar = new string('=', Math.Max(0, filled - 1)) +
                  (filled > 0 ? ">" : "") +
                  new string(' ', barWidth - filled);
        return $"[{bar}]{label}";
    }

    private string RenderArrowChain(int width)
    {
        var parts = new List<string>();
        for (var i = 0; i < _steps.Count; i++)
        {
            var name = _steps[i].DisplayName;
            parts.Add(i == _currentStepIndex ? $"[{name}]" : name);
        }
        var text = string.Join(" > ", parts);
        return DisplayWidth.GetColumnCount(text) > width ? DisplayWidth.TruncateToColumns(text, width) : text;
    }

    private string RenderDots(int width)
    {
        var parts = new List<string>();
        for (var i = 0; i < _steps.Count; i++)
        {
            parts.Add(i <= _currentStepIndex ? "[*]" : "[ ]");
        }
        var dots = string.Join(" ", parts);
        var label = $" {_steps[_currentStepIndex].DisplayName}";
        var text = dots + label;
        return DisplayWidth.GetColumnCount(text) > width ? DisplayWidth.TruncateToColumns(text, width) : text;
    }

    private void RenderBorder(IRenderContext context, Rect bounds)
    {
        var chars = _borderStyle switch
        {
            BorderStyle.Single => BorderChars.Single,
            BorderStyle.Double => BorderChars.Double,
            BorderStyle.Rounded => BorderChars.Rounded,
            BorderStyle.Ascii => BorderChars.Ascii,
            _ => BorderChars.Single
        };

        if (_borderColor.HasValue)
            context.SetForeground(_borderColor.Value);

        // Top border with optional title
        context.WriteAt(bounds.X, bounds.Y, chars.TopLeft);
        var availableTopBarWidth = Math.Max(0, bounds.Width - 2);
        if (_title != null)
        {
            var titleText = $" {_title} ";
            var displayTitle = DisplayWidth.GetColumnCount(titleText) > availableTopBarWidth
                ? DisplayWidth.TruncateToColumns(titleText, availableTopBarWidth)
                : titleText;
            var titleWidth = DisplayWidth.GetColumnCount(displayTitle);
            var topBarWidth = Math.Max(0, availableTopBarWidth - titleWidth);

            context.WriteAt(bounds.X + 1, bounds.Y, displayTitle);
            for (var i = 0; i < topBarWidth; i++)
                context.WriteAt(bounds.X + 1 + titleWidth + i, bounds.Y, chars.Horizontal);
        }
        else
        {
            for (var i = 0; i < bounds.Width - 2; i++)
                context.WriteAt(bounds.X + 1 + i, bounds.Y, chars.Horizontal);
        }
        context.WriteAt(bounds.Right - 1, bounds.Y, chars.TopRight);

        // Side borders
        for (var y = bounds.Y + 1; y < bounds.Bottom - 1; y++)
        {
            context.WriteAt(bounds.X, y, chars.Vertical);
            context.WriteAt(bounds.Right - 1, y, chars.Vertical);
        }

        // Bottom border
        if (bounds.Height > 1)
        {
            context.WriteAt(bounds.X, bounds.Bottom - 1, chars.BottomLeft);
            for (var i = 0; i < bounds.Width - 2; i++)
                context.WriteAt(bounds.X + 1 + i, bounds.Bottom - 1, chars.Horizontal);
            context.WriteAt(bounds.Right - 1, bounds.Bottom - 1, chars.BottomRight);
        }

        if (_borderColor.HasValue)
            context.ResetColors();
    }

    #endregion

    #region Lifecycle

    /// <inheritdoc />
    internal override IEnumerable<ILayoutNode> GetChildNodes()
    {
        if (_contentNode != null)
            return [_contentNode];
        return [];
    }

    /// <inheritdoc />
    internal override void DisconnectChildInvalidationSubscriptions()
    {
        _contentInvalidationSubscription?.Dispose();
        _contentInvalidationSubscription = null;
    }

    /// <inheritdoc />
    public override void OnActivate()
    {
        _isActive = true;
        _contentNode?.OnActivate();
        base.OnActivate();
    }

    /// <inheritdoc />
    public override void OnDeactivate()
    {
        _isActive = false;
        _contentNode?.OnDeactivate();
        base.OnDeactivate();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _stepChanged.OnCompleted();
        _stepChanged.Dispose();
        _beforeAdvance.OnCompleted();
        _beforeAdvance.Dispose();
        _completed.OnCompleted();
        _completed.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();

        _contentInvalidationSubscription?.Dispose();
        _contentNode?.Dispose();
        base.Dispose();
    }

    #endregion

    /// <summary>
    /// Border character sets for different styles.
    /// </summary>
    private static class BorderChars
    {
        public static readonly BorderCharSet Single = new('┌', '─', '┐', '│', '└', '┘');
        public static readonly BorderCharSet Double = new('╔', '═', '╗', '║', '╚', '╝');
        public static readonly BorderCharSet Rounded = new('╭', '─', '╮', '│', '╰', '╯');
        public static readonly BorderCharSet Ascii = new('+', '-', '+', '|', '+', '+');
    }

    private record struct BorderCharSet(char TopLeft, char Horizontal, char TopRight, char Vertical, char BottomLeft, char BottomRight);
}
