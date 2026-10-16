// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Input;
using Termina.Layout;
using Termina.Pages;

namespace Termina.Reactive;

/// <summary>
/// Base class for pages that bind to reactive view models.
/// Pages subscribe to ViewModel observable properties and update components accordingly.
/// </summary>
/// <typeparam name="TViewModel">The ViewModel type this page binds to.</typeparam>
/// <remarks>
/// <para>
/// ReactivePage is the "View" in MVVM pattern. It:
/// </para>
/// <list type="bullet">
///   <item>Builds a declarative layout tree in <see cref="BuildLayout"/></item>
///   <item>Uses observable bindings to automatically update when ViewModel changes</item>
///   <item>Manages subscription lifecycle automatically</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class CounterPage : ReactivePage&lt;CounterViewModel&gt;
/// {
///     public override ILayoutNode BuildLayout()
///     {
///         return Layout.Vertical()
///             .WithChild(ViewModel.CountChanged.Select(c => $"Count: {c}").AsLayout())
///             .WithChild(new TextNode("[+] Increment  [-] Decrement  [Q] Quit"));
///     }
/// }
/// </code>
/// </example>
public abstract class ReactivePage<TViewModel> : IBindablePage, IDisposable
    where TViewModel : ReactiveViewModel
{
    private readonly CompositeDisposable _subscriptions = new();
    // Framework-internal subscriptions tied to the current _layoutRoot (e.g.,
    // IInvalidatingNode.Invalidated → RequestRedraw). Kept separate from
    // _subscriptions so InvalidateLayout() can tear them down and re-wire
    // without touching user code's page-level subscriptions.
    private readonly CompositeDisposable _layoutSubscriptions = new();
    private readonly PageKeyBindings _keyBindings = new();
    private ILayoutNode? _layoutRoot;

    // Lifecycle state — guards InvalidateLayout against bad call patterns
    // (before activation, after disposal, re-entrant from BuildLayout).
    private bool _isActivated;
    private bool _isDisposed;
    private bool _isRebuilding;

    /// <summary>
    /// Determines how focus is automatically assigned when this page is navigated to.
    /// Set in the page constructor or <see cref="OnBound"/> method.
    /// </summary>
    protected FocusPolicy FocusPolicy { get; set; } = FocusPolicy.Manual;

    /// <summary>
    /// The ViewModel this page is bound to.
    /// Available after <see cref="Bind"/> is called by the framework.
    /// </summary>
    protected TViewModel ViewModel { get; private set; } = default!;

    /// <summary>
    /// Composite disposable for page subscriptions.
    /// Subscriptions are automatically cleared when navigating away.
    /// </summary>
    protected CompositeDisposable Subscriptions => _subscriptions;

    /// <summary>
    /// Focus manager for this page.
    /// Use this to manage focus for modals and interactive controls.
    /// </summary>
    protected IFocusManager Focus { get; private set; } = null!;

    /// <summary>
    /// R3 frame provider bound to the owning Termina application render loop.
    /// </summary>
    protected FrameProvider RenderFrameProvider => ViewModel.RenderFrameProvider;

    private Action<string> _navigate = _ => { };
    private Action<string, object?> _navigateWithParams = (_, _) => { };
    private Action _shutdown = () => { };

    /// <summary>
    /// Navigates to the specified route.
    /// Use this for input-driven navigation (e.g., Escape to go back).
    /// </summary>
    /// <param name="route">The route to navigate to.</param>
    /// <example>
    /// <code>
    /// KeyBindings.Register(ConsoleKey.Escape, () => Navigate("/menu"));
    /// </code>
    /// </example>
    protected void Navigate(string route) => _navigate(route);

    /// <summary>
    /// Navigates to the specified route with parameters.
    /// </summary>
    /// <param name="routeTemplate">The route template (e.g., "/items/{id}").</param>
    /// <param name="parameters">Anonymous object with parameter values.</param>
    protected void NavigateWithParams(string routeTemplate, object? parameters) =>
        _navigateWithParams(routeTemplate, parameters);

    /// <summary>
    /// Requests application shutdown.
    /// </summary>
    protected void Shutdown() => _shutdown();

    /// <summary>
    /// Enqueue work to run on the owning Termina application's render loop.
    /// </summary>
    protected void Post(Action action) => ViewModel.Post(action);

    /// <summary>
    /// Enqueue work to run on the owning Termina application's render loop and await completion.
    /// </summary>
    protected Task InvokeAsync(Action action, CancellationToken cancellationToken = default) =>
        ViewModel.InvokeAsync(action, cancellationToken);

    /// <summary>
    /// Key bindings for this page.
    /// Register bindings to intercept keys before they reach focused components.
    /// This implements a "capture phase" for input handling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Key bindings registered here are checked before the focused component
    /// receives input. This solves the common problem where components consume
    /// keys (like Escape) that the page needs for navigation.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public override void OnNavigatedTo()
    /// {
    ///     base.OnNavigatedTo();
    ///     KeyBindings.Register(ConsoleKey.Escape, () => Navigate("/menu"));
    ///     KeyBindings.Register(ConsoleKey.Tab, () => CycleFocus());
    /// }
    /// </code>
    /// </example>
    protected PageKeyBindings KeyBindings => _keyBindings;

    /// <summary>
    /// Build the layout tree for this page.
    /// Override this to compose your page's UI declaratively.
    /// </summary>
    public abstract ILayoutNode BuildLayout();

    /// <summary>
    /// Called when the ViewModel is bound to this page.
    /// Override to perform additional setup after binding.
    /// </summary>
    protected virtual void OnBound()
    {
        // Override in derived classes if needed
    }

    /// <summary>
    /// Binds the ViewModel to this page (interface implementation for AOT compatibility).
    /// </summary>
    void IBindablePage.BindViewModel(ReactiveViewModel viewModel)
    {
        Bind((TViewModel)viewModel);
    }

    /// <summary>
    /// Wires up focus management to this page.
    /// </summary>
    void IBindablePage.WireUpFocus(IFocusManager focusManager)
    {
        Focus = focusManager;
    }

    /// <summary>
    /// Wires up navigation capabilities to this page.
    /// </summary>
    void IBindablePage.WireUpNavigation(Action<string> navigate, Action<string, object?> navigateWithParams, Action shutdown)
    {
        _navigate = navigate;
        _navigateWithParams = navigateWithParams;
        _shutdown = shutdown;
    }

    /// <summary>
    /// Handles page-level keyboard input before it reaches focused components.
    /// Override this method for custom page-level input handling beyond key bindings.
    /// </summary>
    /// <param name="keyInfo">The key press information.</param>
    /// <returns>True if the page handled the input, false to let focused components handle it.</returns>
    public virtual bool HandlePageInput(ConsoleKeyInfo keyInfo)
    {
        // Check registered key bindings first
        return _keyBindings.TryHandle(keyInfo);
    }

    /// <summary>
    /// Binds the ViewModel to this page.
    /// Called by the framework during page initialization.
    /// </summary>
    internal void Bind(TViewModel viewModel)
    {
        ViewModel = viewModel;
        OnBound();
    }

    /// <summary>
    /// Called when the page becomes active (navigated to).
    /// Override to perform page-specific initialization.
    /// </summary>
    public virtual void OnNavigatedTo()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // Idempotent: if the framework calls OnNavigatedTo twice without an
        // intervening OnNavigatingFrom (or a user override calls base after
        // already-activated state), skip the wiring so we don't double-subscribe
        // or double-activate.
        if (_isActivated)
            return;

        BuildAndActivateLayout();
        _isActivated = true;
    }

    /// <summary>
    /// Discards the cached layout root and rebuilds it via
    /// <see cref="BuildLayout"/>. Use this when external state captured at
    /// <see cref="BuildLayout"/>-time has changed (e.g., terminal dimensions
    /// baked into a <c>HeightAuto</c> constraint, theme values frozen into
    /// node colors) and you need the layout tree to re-evaluate those values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Preserved across invalidate:</b> user subscriptions in
    /// <see cref="Subscriptions"/>, registered <see cref="KeyBindings"/>, and
    /// the user's keyboard focus (when the focused node still exists in the
    /// new tree). <see cref="FocusPolicy"/> is NOT re-applied unless the
    /// previous focus target is gone (orphaned by the rebuild).
    /// </para>
    /// <para>
    /// <b>Build-then-swap exception safety:</b> the new tree is built and
    /// activated before the old one is replaced. If <see cref="BuildLayout"/>
    /// throws, the page keeps its existing layout — no half-state.
    /// </para>
    /// <para>
    /// <b>The old layout root is deactivated but NOT disposed</b> — callers
    /// may hold references to nodes used inside <see cref="BuildLayout"/>
    /// (e.g., a streaming text component initialized in <see cref="OnBound"/>
    /// and reused as panel content); disposing would destroy that state.
    /// Framework-owned invalidation subscriptions on abandoned wrapper nodes
    /// are disconnected during the swap so those wrappers do not keep driving
    /// redraws or stay retained through reused child nodes.
    /// </para>
    /// <para>
    /// <b>User subscriptions targeting BuildLayout-created nodes:</b>
    /// subscriptions registered against nodes created inline in
    /// <see cref="BuildLayout"/> will continue firing into the orphaned
    /// old nodes after invalidate, NOT the new nodes. Either subscribe
    /// against page-field nodes (initialized in <see cref="OnBound"/>) that
    /// you reuse in <see cref="BuildLayout"/>, or re-subscribe at the start
    /// of each <see cref="BuildLayout"/> call.
    /// </para>
    /// <para>
    /// <b>Contract:</b> Throws <see cref="ObjectDisposedException"/> after
    /// <see cref="Dispose"/>. Throws <see cref="InvalidOperationException"/>
    /// if called re-entrantly (e.g., from inside <see cref="BuildLayout"/>
    /// or a node lifecycle callback) or if <see cref="BuildLayout"/> returns
    /// <see langword="null"/>. Silently no-ops if the page is not currently
    /// active (between <see cref="OnNavigatingFrom"/> and the next
    /// <see cref="OnNavigatedTo"/>, or before the first navigation) — in
    /// that case the next navigation will rebuild fresh anyway.
    /// </para>
    /// <para>
    /// <b>Thread affinity:</b> must be called on the same dispatcher
    /// thread that drives navigation/rendering.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The page has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// Called re-entrantly during another <see cref="InvalidateLayout"/> or
    /// <see cref="BuildLayout"/> on the same page, or
    /// <see cref="BuildLayout"/> returned <see langword="null"/>.
    /// </exception>
    protected void InvalidateLayout()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isRebuilding)
        {
            throw new InvalidOperationException(
                "InvalidateLayout cannot be called re-entrantly from BuildLayout " +
                "or a layout node lifecycle callback.");
        }

        // No-op when inactive: the next OnNavigatedTo will build fresh anyway,
        // so doing the work now would just resurrect an inactive page and
        // cause double-subscribe + double-activate on the next navigation.
        if (!_isActivated)
            return;

        _isRebuilding = true;
        try
        {
            var oldRoot = _layoutRoot;

            // Build new tree FIRST so a throwing BuildLayout leaves the page
            // with its existing layout intact (no clear/null commit yet).
            var newRoot = BuildLayout()
                ?? throw new InvalidOperationException(
                    $"BuildLayout() returned null for page {GetType().FullName}.");

            ApplyRuntimeContext(newRoot);

            var newNodes = CollectLayoutNodes(newRoot);

            // Tear down the old framework wiring before we touch the previous
            // tree so any invalidation raised during deactivation doesn't
            // schedule redraws against the outgoing layout.
            _layoutSubscriptions.Clear();

            // Deactivate the old tree before activating the new one. If the
            // page reuses child node instances across rebuilds, this ensures
            // those nodes finish in the active state after the new tree is
            // activated rather than being shut back down by the old tree.
            if (oldRoot is IActivatableNode oldNode)
                oldNode.OnDeactivate();

            // Disconnect invalidation subscriptions on old nodes that won't
            // survive the rebuild. This keeps abandoned wrappers from being
            // retained through reused children once the swap completes.
            DisconnectAbandonedNodes(oldRoot, newNodes);

            // Atomic swap: from here on, _layoutRoot points at the new tree
            // and the old tree is fully replaced.
            _layoutRoot = newRoot;

            // Wire framework invalidation on the new tree before activation so
            // synchronous Invalidated emissions during OnActivate are observed.
            if (newRoot is IInvalidatingNode invalidating)
            {
                invalidating.Invalidated
                    .Subscribe(_ => ViewModel.RequestRedraw())
                    .DisposeWith(_layoutSubscriptions);
            }

            if (newRoot is IActivatableNode newNode)
                newNode.OnActivate();

            ReconcileFocus(newRoot);

            // Request a redraw so the new tree actually renders. Without
            // this, callers who invalidate in response to an event that
            // doesn't otherwise touch a ReactiveProperty would see no visual
            // change until the next unrelated reactive emission.
            ViewModel.RequestRedraw();
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    /// <summary>
    /// Build (if needed) and activate the layout tree, wiring framework-level
    /// subscriptions. Called from <see cref="OnNavigatedTo"/> only;
    /// <see cref="InvalidateLayout"/> takes its own build-then-swap path for
    /// exception safety.
    /// </summary>
    private void BuildAndActivateLayout()
    {
        // Build layout once on first navigation, reactivate on subsequent visits.
        if (_layoutRoot == null)
        {
            _layoutRoot = BuildLayout()
                ?? throw new InvalidOperationException(
                    $"BuildLayout() returned null for page {GetType().FullName}.");
        }

        ApplyRuntimeContext(_layoutRoot);

        // Subscribe to layout invalidation events to trigger redraws. Lives in
        // _layoutSubscriptions so InvalidateLayout can replace it without
        // touching user-held entries in _subscriptions.
        if (_layoutRoot is IInvalidatingNode invalidating)
        {
            invalidating.Invalidated
                .Subscribe(_ => ViewModel.RequestRedraw())
                .DisposeWith(_layoutSubscriptions);
        }

        // Activate the layout tree (resume subscriptions, timers, etc.)
        if (_layoutRoot is IActivatableNode node)
        {
            node.OnActivate();
        }

        // Auto-focus based on policy
        if (FocusPolicy != FocusPolicy.Manual && _layoutRoot != null)
        {
            ApplyFocusPolicy(_layoutRoot);
        }
    }

    private static HashSet<ILayoutNode> CollectLayoutNodes(ILayoutNode root)
    {
        var visited = new HashSet<ILayoutNode>();
        var stack = new Stack<ILayoutNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current) || current is not LayoutNode layoutNode)
                continue;

            foreach (var child in layoutNode.GetChildNodes())
                stack.Push(child);
        }

        return visited;
    }

    private void OnLayoutStructureChanged()
    {
        if (_layoutRoot is { } root)
            ReconcileFocus(root);
    }

    private void ReconcileFocus(ILayoutNode root)
    {
        if (Focus is null || Focus.CurrentFocus is not { } currentFocus)
            return;

        var focusables = Focus.CollectFocusables(root);
        if (focusables.Any(focusable => ReferenceEquals(focusable, currentFocus)))
            return;

        Focus.ClearFocus();
        if (FocusPolicy != FocusPolicy.Manual)
            ApplyFocusPolicy(root);
    }

    private void ApplyRuntimeContext(ILayoutNode root)
    {
        var context = new LayoutRuntimeContext(
            ViewModel.RenderFrameProvider,
            ViewModel.TimeProvider,
            ViewModel.RequestRedraw,
            OnLayoutStructureChanged);
        LayoutRuntimeContextInjector.Apply(root, context);
    }

    private static void DisconnectAbandonedNodes(ILayoutNode? root, HashSet<ILayoutNode> retainedNodes)
    {
        if (root is not LayoutNode layoutNode || retainedNodes.Contains(root))
            return;

        layoutNode.DisconnectChildInvalidationSubscriptions();

        foreach (var child in layoutNode.GetChildNodes())
            DisconnectAbandonedNodes(child, retainedNodes);
    }

    /// <summary>
    /// Cycle focus forward through focusable nodes in the layout tree.
    /// Register as a key binding: <c>KeyBindings.Register(ConsoleKey.Tab, CycleFocusForward);</c>
    /// </summary>
    protected void CycleFocusForward()
    {
        if (_layoutRoot == null) return;
        var focusables = Focus.CollectFocusables(_layoutRoot);
        Focus.CycleFocus(focusables);
    }

    /// <summary>
    /// Cycle focus backward through focusable nodes in the layout tree.
    /// Register as a key binding: <c>KeyBindings.Register(ConsoleKey.Tab, ConsoleModifiers.Shift, CycleFocusBackward);</c>
    /// </summary>
    protected void CycleFocusBackward()
    {
        if (_layoutRoot == null) return;
        var focusables = Focus.CollectFocusables(_layoutRoot);
        Focus.CycleFocus(focusables, reverse: true);
    }

    /// <summary>
    /// Apply the configured focus policy to the layout tree.
    /// </summary>
    private void ApplyFocusPolicy(ILayoutNode root)
    {
        var focusables = Focus.CollectFocusables(root);
        if (focusables.Count == 0)
            return;

        IFocusable? target = FocusPolicy switch
        {
            FocusPolicy.FirstFocusable => focusables[0],
            FocusPolicy.ByPriority => focusables.OrderByDescending(f => f.FocusPriority).First(),
            _ => null
        };

        if (target != null)
        {
            Focus.SetFocus(target);
        }
    }

    /// <summary>
    /// Called when the page is being deactivated (navigating away).
    /// Clears page subscriptions and deactivates the layout tree.
    /// </summary>
    public virtual void OnNavigatingFrom()
    {
        // Clear page-level subscriptions, framework layout subscriptions, and key bindings.
        _subscriptions.Clear();
        _layoutSubscriptions.Clear();
        _keyBindings.Clear();

        // Deactivate layout (pause, don't dispose)
        if (_layoutRoot is IActivatableNode node)
        {
            node.OnDeactivate();
        }

        // InvalidateLayout becomes a no-op until the next OnNavigatedTo —
        // there's no point rebuilding a tree that isn't being rendered.
        _isActivated = false;

        // Note: _layoutRoot is NOT disposed or nulled - it's preserved for reactivation
    }

    /// <summary>
    /// Disposes the page and its layout tree.
    /// This is called when the page is truly destroyed, not just navigated away from.
    /// </summary>
    public virtual void Dispose()
    {
        if (_isDisposed)
            return;

        // Final cleanup when page is truly destroyed (not just navigated away).
        // Set _isDisposed before the disposal cascade so any late callback that
        // re-enters InvalidateLayout or OnNavigatedTo throws cleanly instead
        // of resurrecting a dead page.
        _isDisposed = true;
        _isActivated = false;

        _subscriptions.Dispose();
        _layoutSubscriptions.Dispose();
        _layoutRoot?.Dispose();
        _layoutRoot = null;
    }

    /// <summary>
    /// Gets the current layout root for rendering.
    /// </summary>
    ILayoutNode? IBindablePage.LayoutRoot => _layoutRoot;
}
