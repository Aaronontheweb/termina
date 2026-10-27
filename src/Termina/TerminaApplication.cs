// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using R3;
using Termina.Diagnostics;
using Termina.Hosting;
using Termina.Input;
using Termina.Layout;
using Termina.Navigation;
using Termina.Notifications;
using Termina.Pages;
using Termina.Platform;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Routing;
using Termina.Terminal;
using InputMouseButton = Termina.Input.MouseButton;
using InputMouseEventType = Termina.Input.MouseEventType;

namespace Termina;

/// <summary>
/// The main application orchestrator for Termina TUI applications.
/// </summary>
/// <remarks>
/// <para>
/// TerminaApplication is the "app host" that:
/// </para>
/// <list type="bullet">
///   <item>Owns the event channel infrastructure for input routing</item>
///   <item>Exposes input as IObservable&lt;IInputEvent&gt; for reactive ViewModels</item>
///   <item>Manages page registration and navigation</item>
///   <item>Controls which page is "active" and can render</item>
/// </list>
/// <para>
/// ViewModels are resolved from DI and can inject any services they need.
/// ViewModels subscribe to Input observable to handle keyboard events.
/// </para>
/// </remarks>
public sealed class TerminaApplication : IInlineOutput
{
    private readonly IAnsiTerminal _terminal;
    private readonly DiffingTerminal? _diffingTerminal;
    private readonly InlineTerminal? _inlineTerminal;
    private readonly TerminaRuntimeOptions _runtimeOptions;
    private readonly IServiceProvider? _serviceProvider;
    private readonly Channel<object> _eventChannel;
    private readonly Subject<IInputEvent> _inputSubject = new();
    private readonly RouteMatcher _routeMatcher = new();
    private readonly Dictionary<string, ReactivePageRegistration> _pages = new();
    private readonly Dictionary<string, (IPage Page, ReactiveViewModel ViewModel)> _cachedPages = new();
    private readonly Stack<(string Path, IReadOnlyDictionary<string, object>? Parameters)> _history = new();
    private readonly List<IInputSource> _inputSources = new();
    private readonly FocusManager _focusManager = new();
    private readonly IToastService? _toastService;
    private readonly ToastOverlayNode? _toastOverlay;
    private readonly IDisposable? _toastInvalidationSubscription;
    private readonly TerminaRenderFrameProvider _renderFrameProvider;

    private string? _currentPath;
    private IReadOnlyDictionary<string, object>? _currentParameters;
    private IPage? _currentPage;
    private ReactiveViewModel? _currentViewModel;
    private ReactivePageRegistration? _currentRegistration;
    private CancellationTokenSource? _shutdownCts;
    private DateTime? _firstCtrlCAt;
    private static readonly TimeSpan CtrlCDoublePressWindow = TimeSpan.FromSeconds(2);
    private bool _rawInputActive;
    private bool _kittyKeyboardPushed;
    private bool _mouseEnabledByApp;
    private bool _wheelScrollEnabledByApp;
    private int _pendingNavigationRequests;

    // 1 when RequestRedraw ran after the start of the last render. An ApplicationRedrawRequested
    // event that finds 0 asks for a frame that a later render already drew, so the loop does not
    // render again.
    private int _redrawPending;

    // 1 after RunAsync stopped its loop. Work that a caller awaits and that arrives after this
    // point cannot run, so InvokeAsync cancels it.
    private int _loopStopped;
    private TerminalInputCapabilities _inputCapabilities = new(
        TerminalCapabilityAvailability.Unknown,
        TerminalInputCapabilitySource.None);

    /// <summary>
    /// Creates a new Termina application.
    /// </summary>
    /// <param name="terminal">The ANSI terminal for rendering.</param>
    public TerminaApplication(IAnsiTerminal terminal)
        : this(terminal, runtimeOptions: null, serviceProvider: null)
    {
    }

    /// <summary>
    /// Creates a new Termina application.
    /// </summary>
    /// <param name="terminal">The ANSI terminal for rendering.</param>
    /// <param name="serviceProvider">Optional service provider for resolving ViewModels and input sources.</param>
    public TerminaApplication(IAnsiTerminal terminal, IServiceProvider? serviceProvider)
        : this(terminal, runtimeOptions: null, serviceProvider: serviceProvider)
    {
    }

    /// <summary>
    /// Creates a new Termina application.
    /// </summary>
    /// <param name="terminal">The ANSI terminal for rendering.</param>
    /// <param name="runtimeOptions">Runtime terminal/input options.</param>
    /// <param name="serviceProvider">Optional service provider for resolving ViewModels and input sources.</param>
    public TerminaApplication(
        IAnsiTerminal terminal,
        TerminaRuntimeOptions? runtimeOptions = null,
        IServiceProvider? serviceProvider = null)
    {
        _runtimeOptions = runtimeOptions ?? new TerminaRuntimeOptions();
        ValidateRuntimeOptions(_runtimeOptions);

        ObservableSystem.RegisterUnhandledExceptionHandler(ex =>
        {
            TerminaTrace.Reactive.Error("ObservableSystem", "Unhandled observable error: {0}", ex);
        });

        if (_runtimeOptions.PresentationMode == TerminalPresentationMode.Inline)
        {
            if (terminal is not IInlineTerminalControl inlineControl)
            {
                throw new InvalidOperationException(
                    $"Inline mode requires a terminal that implements {nameof(IInlineTerminalControl)}.");
            }

            _inlineTerminal = new InlineTerminal(terminal, inlineControl);
            _terminal = _inlineTerminal;
            _diffingTerminal = null;
        }
        // Wrap the full-screen terminal for flicker-free output.
        else if (terminal is DiffingTerminal diffing)
        {
            _terminal = terminal;
            _diffingTerminal = diffing;
            _inlineTerminal = null;
        }
        else if (terminal is VirtualTerminal)
        {
            // Keep direct virtual output for current full-screen tests.
            _terminal = terminal;
            _diffingTerminal = null;
            _inlineTerminal = null;
        }
        else
        {
            _diffingTerminal = new DiffingTerminal(terminal);
            _terminal = _diffingTerminal;
            _inlineTerminal = null;
        }

        _serviceProvider = serviceProvider;
        _eventChannel = Channel.CreateUnbounded<object>();
        _renderFrameProvider = new TerminaRenderFrameProvider(
            RequestRenderFrame,
            _runtimeOptions.TimeProvider,
            _runtimeOptions.RenderFrameInterval);
        _toastService = serviceProvider?.GetService<IToastService>();
        _toastOverlay = _toastService != null ? new ToastOverlayNode(_toastService) : null;
        _toastInvalidationSubscription = _toastOverlay?.Invalidated.Subscribe(_ => RequestRedraw());

        // If using DI, check for registered input sources
        if (serviceProvider != null)
        {
            var inputSources = serviceProvider.GetServices<IInputSource>();
            foreach (var source in inputSources)
            {
                _inputSources.Add(source);
            }
        }
    }

    /// <summary>
    /// Observable stream of input events. ViewModels subscribe to this.
    /// </summary>
    public Observable<IInputEvent> Input => _inputSubject.AsObservable();

    /// <summary>
    /// Gets the latest capabilities of the active terminal input path.
    /// </summary>
    public TerminalInputCapabilities InputCapabilities => _inputCapabilities;

    /// <summary>
    /// Gets the R3 frame provider bound to this application's render loop.
    /// </summary>
    public FrameProvider RenderFrameProvider => _renderFrameProvider;

    /// <summary>
    /// Gets the focus manager for routing input to focused components.
    /// </summary>
    public IFocusManager Focus => _focusManager;

    /// <summary>
    /// Gets the current navigation path, if any.
    /// </summary>
    public string? CurrentPath => _currentPath;

    /// <summary>
    /// Whether navigation history allows going back.
    /// </summary>
    public bool CanGoBack => _history.Count > 0;

    /// <summary>
    /// Add an input source to the application.
    /// </summary>
    /// <param name="inputSource">The input source to add.</param>
    /// <returns>This application for fluent chaining.</returns>
    public TerminaApplication AddInputSource(IInputSource inputSource)
    {
        _inputSources.Add(inputSource);
        return this;
    }

    /// <summary>
    /// Register a reactive page with a route template.
    /// </summary>
    /// <typeparam name="TPage">The page type (must implement ReactivePage&lt;TViewModel&gt;).</typeparam>
    /// <typeparam name="TViewModel">The ViewModel type.</typeparam>
    /// <param name="routeTemplate">Route template (e.g., "/tasks/{id:int}").</param>
    /// <param name="behavior">How the page behaves on navigation.</param>
    public void RegisterRoute<TPage, TViewModel>(
        string routeTemplate,
        NavigationBehavior behavior = NavigationBehavior.ResetOnNavigation)
        where TPage : ReactivePage<TViewModel>, new()
        where TViewModel : ReactiveViewModel, new()
    {
        var template = RouteParser.Parse(routeTemplate);
        var registration = new ReactivePageRegistration(
            template,
            behavior,
            () => new TPage(),
            () => new TViewModel());

        _pages[template.Template] = registration;
        _routeMatcher.AddRoute(template, template.Template);
    }

    /// <summary>
    /// Register a page from a descriptor (used by TerminaBuilder).
    /// </summary>
    internal void RegisterPageFromDescriptor(ReactivePageRegistrationDescriptor descriptor)
    {
        var registration = new ReactivePageRegistration(
            descriptor.RouteTemplate,
            descriptor.Behavior,
            () => (IPage)descriptor.PageFactory(_serviceProvider!),
            () => descriptor.ViewModelFactory(_serviceProvider!));

        _pages[descriptor.PageKey] = registration;
        _routeMatcher.AddRoute(descriptor.RouteTemplate, descriptor.PageKey);
    }

    /// <summary>
    /// Navigate to a path (e.g., "/tasks/42").
    /// </summary>
    /// <param name="path">The path to navigate to.</param>
    public void NavigateTo(string path)
    {
        // Try to match the path against registered routes
        if (!_routeMatcher.TryMatch(path, out var pageKey, out var parameters))
            throw new InvalidOperationException($"No route matches path '{path}'.");

        if (!_pages.TryGetValue(pageKey!, out var registration))
            throw new InvalidOperationException($"Page '{pageKey}' is not registered.");

        NavigateToInternal(path, registration, parameters);
    }

    /// <summary>
    /// Navigate to a path with route values.
    /// </summary>
    /// <param name="routeTemplate">The route template (e.g., "/tasks/{id}").</param>
    /// <param name="routeValues">The route values to substitute.</param>
    public void NavigateTo(string routeTemplate, object? routeValues)
    {
        var path = RouteMatcher.BuildPath(routeTemplate, routeValues);
        NavigateTo(path);
    }

    private void NavigateToInternal(
        string path,
        ReactivePageRegistration registration,
        IReadOnlyDictionary<string, object>? parameters)
    {
        TerminaTrace.Page.Info(this, "Navigating to: {0}", path);

        // Notify current page/ViewModel they're leaving
        if (_currentPage != null && _currentViewModel != null)
        {
            TerminaTrace.Page.Debug(this, "Deactivating current page: {0}", _currentPage.GetType().Name);
            _currentPage.OnNavigatingFrom();
            _currentViewModel.OnDeactivating();
        }

        // Push current page to history (if not going to same page)
        if (_currentPath != null && _currentPath != path)
        {
            _history.Push((_currentPath, _currentParameters));
            TerminaTrace.Page.Debug(this, "Pushed to history: {0}, depth={1}", _currentPath, _history.Count);
        }

        // Generate a cache key that includes parameters for PreserveState pages
        var cacheKey = registration.RouteTemplate.Template;

        // Get or create page instance
        if (registration.Behavior == NavigationBehavior.PreserveState &&
            _cachedPages.TryGetValue(cacheKey, out var cached))
        {
            TerminaTrace.Page.Debug(this, "Using cached page: {0}", cacheKey);
            _currentPage = cached.Page;
            _currentViewModel = cached.ViewModel;

            // Still need to inject new parameters if the route has them
            if (parameters != null && _currentViewModel is IRouteParameterReceiver receiver)
            {
                receiver.SetRouteParameters(parameters);
            }
        }
        else
        {
            TerminaTrace.Page.Debug(this, "Creating new page, behavior={0}", registration.Behavior);
            _currentPage = registration.PageFactory();
            _currentViewModel = registration.ViewModelFactory();

            // Inject route parameters before wiring up
            if (parameters != null && _currentViewModel is IRouteParameterReceiver receiver)
            {
                receiver.SetRouteParameters(parameters);
            }

            // Wire up ViewModel with navigation, shutdown, redraw, and input.
            // Navigation is routed through the event channel (RequestNavigation)
            // so it is processed on the render loop thread, not the caller's.
            _currentViewModel.WireUp(
                RequestNavigation,
                RequestNavigation,
                Shutdown,
                RequestRedraw,
                Input,
                RenderFrameProvider,
                _runtimeOptions.TimeProvider,
                Post,
                InvokeAsync);

            // Bind page to ViewModel and wire up focus and navigation
            BindPageToViewModel(_currentPage, _currentViewModel);
            WireUpPageFocus(_currentPage);
            WireUpPageNavigation(_currentPage);

            // Cache if PreserveState
            if (registration.Behavior == NavigationBehavior.PreserveState)
            {
                _cachedPages[cacheKey] = (_currentPage, _currentViewModel);
                TerminaTrace.Page.Debug(this, "Cached page: {0}", cacheKey);
            }
        }

        _currentPath = path;
        _currentParameters = parameters;
        _currentRegistration = registration;

        // Notify new page/ViewModel they're active
        _currentPage.OnNavigatedTo();
        _currentViewModel.OnActivated();
        TerminaTrace.Page.Info(this, "Navigation complete: {0}, page={1}", path, _currentPage.GetType().Name);
    }

    /// <summary>
    /// Binds a page to its ViewModel using the IBindablePage interface (AOT-compatible).
    /// </summary>
    private static void BindPageToViewModel(IPage page, ReactiveViewModel viewModel)
    {
        if (page is IBindablePage bindablePage)
        {
            bindablePage.BindViewModel(viewModel);
        }
    }

    /// <summary>
    /// Wires up focus management to the page.
    /// </summary>
    private void WireUpPageFocus(IPage page)
    {
        if (page is IBindablePage bindablePage)
        {
            bindablePage.WireUpFocus(_focusManager);
        }
    }

    /// <summary>
    /// Wires up navigation capabilities to the page.
    /// </summary>
    private void WireUpPageNavigation(IPage page)
    {
        if (page is IBindablePage bindablePage)
        {
            // Route through the event channel so navigation is processed on the
            // render loop thread (see RequestNavigation).
            bindablePage.WireUpNavigation(RequestNavigation, RequestNavigation, Shutdown);
        }
    }

    /// <summary>
    /// Go back to the previous page in history.
    /// </summary>
    public void GoBack()
    {
        if (_history.Count > 0)
        {
            var (previousPath, _) = _history.Pop();
            _currentPath = null; // Prevent pushing to history
            NavigateTo(previousPath);
        }
    }

    /// <summary>
    /// Request graceful shutdown of the application.
    /// </summary>
    /// <remarks>
    /// The loop stops at once. It does not run work that is still in the queue, also work that
    /// <see cref="Post"/> or <see cref="InvokeAsync"/> queued before this call. A task that
    /// <see cref="InvokeAsync"/> returned for such work ends as canceled.
    /// </remarks>
    public void Shutdown()
    {
        _shutdownCts?.Cancel();
    }

    /// <summary>
    /// Request a UI redraw. Used by ViewModels when async content changes.
    /// </summary>
    /// <remarks>
    /// Change the state before you call this method. The loop can merge this request with a redraw
    /// request that is already in the queue. A render that runs between a call that comes first and
    /// the state change can draw the old state, and the loop then skips the merged request.
    /// </remarks>
    public void RequestRedraw()
    {
        // Set the flag before the event is queued, so the loop always sees the flag when it reads the event.
        Volatile.Write(ref _redrawPending, 1);

        // The loop can skip this event when a later render already drew the change. A
        // RedrawRequested.Instance that an input source writes always causes a render.
        _eventChannel.Writer.TryWrite(ApplicationRedrawRequested.Instance);
    }

    /// <inheritdoc />
    public ValueTask CommitAsync(ILayoutNode content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (_inlineTerminal is null)
            throw new InvalidOperationException("Stable content commits require inline presentation mode.");

        if (_shutdownCts is null)
            throw new InvalidOperationException("The Termina application must be active before it can commit inline output.");

        if (cancellationToken.IsCancellationRequested)
            return new ValueTask(Task.FromCanceled(cancellationToken));

        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdownCts.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = linkedCancellation.Token.Register(
            () => completion.TrySetCanceled(linkedCancellation.Token));
        var request = new InlineCommitRequested(
            content,
            completion,
            linkedCancellation,
            registration);

        if (!_eventChannel.Writer.TryWrite(request))
        {
            registration.Dispose();
            linkedCancellation.Dispose();
            throw new InvalidOperationException("Unable to post an inline commit to the Termina event loop.");
        }

        return new ValueTask(completion.Task);
    }

    /// <summary>
    /// Enqueues work to run on the Termina render loop thread.
    /// </summary>
    /// <remarks>
    /// The work does not run when the loop stops first. A stop drops all work that is still in the
    /// queue, also work that this method queued before a <see cref="Shutdown"/> call. Work that
    /// this method queues after the loop stopped does not run.
    /// </remarks>
    /// <param name="action">The work to run on the loop.</param>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_eventChannel.Writer.TryWrite(new LoopWorkRequested(action)))
            throw new InvalidOperationException("Unable to post work to the Termina event loop.");
    }

    /// <summary>
    /// Enqueues work to run on the Termina render loop thread and returns a task that
    /// completes when the work has run.
    /// </summary>
    /// <remarks>
    /// The work does not run when the loop stops first. A stop drops all work that is still in the
    /// queue, also work that was queued before a <see cref="Shutdown"/> call. The returned task then
    /// ends as canceled (<see cref="TaskCanceledException"/>), even when the token of the caller is
    /// not canceled.
    /// </remarks>
    /// <param name="action">The work to run on the loop.</param>
    /// <param name="cancellationToken">Cancels the work if it has not run yet.</param>
    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration cancellationRegistration = default;
        if (cancellationToken.CanBeCanceled)
        {
            cancellationRegistration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
        }

        void Abandon()
        {
            completion.TrySetCanceled();
            cancellationRegistration.Dispose();
        }

        try
        {
            var work = new LoopWorkRequested(() =>
            {
                try
                {
                    if (completion.Task.IsCompleted)
                        return;

                    cancellationToken.ThrowIfCancellationRequested();
                    action();
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    cancellationRegistration.Dispose();
                }
            }, Abandon);

            if (!_eventChannel.Writer.TryWrite(work))
                throw new InvalidOperationException("Unable to post work to the Termina event loop.");
        }
        catch
        {
            cancellationRegistration.Dispose();
            throw;
        }

        // The loop drains the queue when it stops. Work written after that drain is not drained,
        // so cancel it here. Interlocked gives a full fence between the write above and this read.
        if (Interlocked.CompareExchange(ref _loopStopped, 0, 0) != 0)
            Abandon();

        return completion.Task;
    }

    /// <summary>
    /// Installs this application's render frame provider as R3's process-wide default
    /// until the returned scope is disposed.
    /// </summary>
    public IDisposable SetDefaultObservableSystem()
    {
        var previousFrameProvider = ObservableSystem.DefaultFrameProvider;
        ObservableSystem.DefaultFrameProvider = RenderFrameProvider;
        return new DefaultObservableSystemScope(previousFrameProvider);
    }

    // Navigation requested by a ViewModel or page runs on whatever thread the
    // caller is on (e.g. an async continuation on the thread pool). Posting a
    // NavigationRequested event to the channel defers NavigateToInternal to the
    // render loop thread, so page swaps are serialized against rendering. A
    // direct cross-thread NavigateTo would publish a not-yet-bound _currentPage
    // to the render thread — a data race that crashes under ARM64's weak memory
    // model (the render loop calls BuildLayout() before OnBound() has run).
    private void RequestNavigation(string path)
    {
        EnqueueNavigationRequest(new NavigationRequested(path));
    }

    private void RequestNavigation(string routeTemplate, object? routeValues)
    {
        EnqueueNavigationRequest(
            new NavigationRequested(RouteMatcher.BuildPath(routeTemplate, routeValues)));
    }

    private void EnqueueNavigationRequest(NavigationRequested request)
    {
        Interlocked.Increment(ref _pendingNavigationRequests);
        if (!_eventChannel.Writer.TryWrite(request))
            Interlocked.Decrement(ref _pendingNavigationRequests);
    }

    private void RequestRenderFrame()
    {
        _eventChannel.Writer.TryWrite(RenderFrameRequested.Instance);
    }

    /// <summary>
    /// Run the application until cancellation or shutdown is requested.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        TerminaTrace.Page.Info(this, "RunAsync starting");

        // Create platform console for native input handling
        IPlatformConsole? platformConsole = null;

        if (_inputSources.Count == 0)
        {
            // Use platform-specific console for event-driven input (no polling on Windows)
            platformConsole = PlatformConsoleFactory.Create(_runtimeOptions);
            platformConsole.Initialize();
            _rawInputActive = platformConsole.Capabilities.RawInputActive;

            var kittyFlags = GetKittyKeyboardFlags();
            var kittyReportAllKeysVisible = _rawInputActive && (kittyFlags & 8) != 0;
            _inputCapabilities = InitialInputCapabilities(platformConsole.Capabilities, kittyFlags);

            _inputSources.Add(new PlatformInputSource(
                platformConsole,
                new PlatformInputConfiguration(_rawInputActive, kittyReportAllKeysVisible)));
            TerminaTrace.Input.Debug(this, "Added PlatformInputSource");
        }
        else
        {
            _inputCapabilities = new TerminalInputCapabilities(
                TerminalCapabilityAvailability.Unknown,
                TerminalInputCapabilitySource.CustomInput);
        }

        // Create linked token - cancelled by either external token OR Shutdown()
        TerminaTrace.Input.Debug(this, "Creating linked cancellation token");
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = _shutdownCts.Token;
        Interlocked.Exchange(ref _loopStopped, 0);
        TerminaTrace.Input.Debug(this, "Linked token created");

        // Start each input source - they push to the shared channel
        TerminaTrace.Input.Debug(this, "Starting {0} input source(s)...", _inputSources.Count);
        var inputTasks = _inputSources
            .Select(source => source.RunAsync(_eventChannel.Writer, linkedToken))
            .ToList();

        TerminaTrace.Input.Debug(this, "Input tasks started, count={0}", inputTasks.Count);
        try
        {
            try
            {
                if (_runtimeOptions.PresentationMode == TerminalPresentationMode.FullScreen)
                {
                    TerminaTrace.Render.Debug(this, "About to enter alternate screen");
                    _terminal.EnterAlternateScreen();
                }

                _terminal.SetCursorVisible(false);
                _terminal.Flush();
                TerminaTrace.Render.Debug(this, "Terminal presentation mode entered, cursor hidden, flushed");

                var inTmux = Environment.GetEnvironmentVariable("TMUX") is not null;
                Console.Write(AnsiCodes.EnableBracketedPaste);

                // When running inside tmux, the inner-pane ESC[?2004h above is intercepted by tmux
                // and never reaches the outer terminal. The outer terminal therefore does not know
                // to wrap Ctrl+Shift+V pastes with ESC[200~...ESC[201~. Use a DCS passthrough to
                // also enable bracketed paste in the outer terminal.
                // Requires: set -g allow-passthrough on  in ~/.tmux.conf (tmux 3.3+).
                if (inTmux)
                {
                    Console.Write(AnsiCodes.TmuxPassthrough(AnsiCodes.EnableBracketedPaste));
                }

                var kittyFlags = GetKittyKeyboardFlags();
                _kittyKeyboardPushed = KittyKeyboardEnhancement.TryEnter(this, kittyFlags, inTmux);
                if (_rawInputActive && kittyFlags > 0)
                {
                    if (!_kittyKeyboardPushed || !KittyKeyboardEnhancement.TryQuery(this))
                    {
                        _inputCapabilities = new TerminalInputCapabilities(
                            TerminalCapabilityAvailability.Unavailable,
                            TerminalInputCapabilitySource.LegacyTerminal);
                    }
                }

                _inputSubject.OnNext(new TerminalInputCapabilitiesChanged(_inputCapabilities));

                if (_runtimeOptions.ScrollInputMode == ScrollInputMode.AlternateScroll && _rawInputActive)
                {
                    // Alternate-scroll preserves native selection/clipboard behavior but only works
                    // correctly when the input parser sees raw byte sequences.
                    _terminal.EnableWheelScroll();
                    _wheelScrollEnabledByApp = true;
                    _mouseEnabledByApp = false;
                }
                else if (_runtimeOptions.ScrollInputMode != ScrollInputMode.NativeTerminal)
                {
                    _terminal.EnableMouse();
                    _mouseEnabledByApp = true;
                    _wheelScrollEnabledByApp = false;
                }
                else
                {
                    _mouseEnabledByApp = false;
                    _wheelScrollEnabledByApp = false;
                }

                _terminal.Flush();

                // Initial render
                TerminaTrace.Render.Debug(this, "Starting initial render");
                if (!HasPendingNavigationRequests())
                    RenderCurrentPage();
                TerminaTrace.Render.Debug(this, "Initial render complete");

                // Single-threaded event loop - all input sources merge here.
                // WaitToReadAsync observes the token for each batch. ReadAllAsync does not
                // observe it while the queue is not empty, so a steady producer kept the loop
                // alive after Shutdown.
                while (await _eventChannel.Reader.WaitToReadAsync(linkedToken))
                {
                    ProcessQueuedEvents(linkedToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            finally
            {
                _shutdownCts.Cancel();
                var inTmux = Environment.GetEnvironmentVariable("TMUX") is not null;

                // Disable bracketed paste and kitty keyboard protocol before restoring terminal
                if (_kittyKeyboardPushed)
                {
                    KittyKeyboardEnhancement.TryLeave(inTmux);
                    _kittyKeyboardPushed = false;
                }

                Console.Write(AnsiCodes.DisableBracketedPaste);
                if (inTmux)
                {
                    Console.Write(AnsiCodes.TmuxPassthrough(AnsiCodes.DisableBracketedPaste));
                }

                if (_wheelScrollEnabledByApp)
                    _terminal.DisableWheelScroll();
                if (_mouseEnabledByApp)
                    _terminal.DisableMouse();

                _inlineTerminal?.ClearLiveRegion();
                _terminal.SetCursorVisible(true);
                _terminal.ResetColors();
                if (_runtimeOptions.PresentationMode == TerminalPresentationMode.FullScreen)
                    _terminal.ExitAlternateScreen();
                _terminal.Flush();

                if (_runtimeOptions.PresentationMode == TerminalPresentationMode.FullScreen)
                    Console.WriteLine();

                // Complete the input subject
                _inputSubject.OnCompleted();

                // Restore and dispose platform console
                platformConsole?.Dispose();
            }
        }
        finally
        {
            // Wait for all input sources to complete.
            try
            {
                await Task.WhenAll(inputTasks);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            finally
            {
                Interlocked.Exchange(ref _loopStopped, 1);
                AbandonQueuedEvents();
                _shutdownCts.Dispose();
                _shutdownCts = null;
            }
        }
    }

    /// <summary>
    /// Process an event by routing it to the appropriate handlers.
    /// </summary>
    private void ProcessEvent(object evt)
    {
        TerminaTrace.Input.Trace(this, "ProcessEvent: {0}", evt.GetType().Name);

        // Subscribers see every redraw request as the public event.
        if (evt is ApplicationRedrawRequested)
            evt = RedrawRequested.Instance;

        // Framework-level Ctrl+C handling: first press shows a hint, second press
        // within CtrlCDoublePressWindow shuts the app down. We scope this to raw-input
        // mode by default so regular Console.ReadKey apps keep their existing behavior.
        if (ShouldInterceptCtrlC()
            && evt is KeyPressed ctrlC
            && ctrlC.KeyInfo.Key == ConsoleKey.C
            && ctrlC.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            var now = DateTime.UtcNow;
            if (_firstCtrlCAt.HasValue && (now - _firstCtrlCAt.Value) <= CtrlCDoublePressWindow)
            {
                TerminaTrace.Page.Info(this, "Ctrl+C pressed twice — shutting down");
                _firstCtrlCAt = null;
                Shutdown();
                return;
            }

            _firstCtrlCAt = now;
            _toastService?.Show(
                "Press Ctrl+C again to quit",
                new ToastOptions(Duration: CtrlCDoublePressWindow, Position: ToastPosition.BottomCenter));
            RequestRedraw();
            return;
        }

        if (evt is KeyPressed keyPressedEvent
            && !(keyPressedEvent.KeyInfo.Key == ConsoleKey.C
                 && keyPressedEvent.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)))
        {
            _firstCtrlCAt = null;
        }

        // Handle system events
        switch (evt)
        {
            case KittyKeyboardFlagsReported keyboardFlags:
                var supportsModifiedEnter = (keyboardFlags.Flags & 9) != 0;
                PublishInputCapabilities(new TerminalInputCapabilities(
                    supportsModifiedEnter
                        ? TerminalCapabilityAvailability.Available
                        : TerminalCapabilityAvailability.Unavailable,
                    TerminalInputCapabilitySource.KittyKeyboardProtocol));
                return;

            case PrimaryDeviceAttributesReported:
                if (_inputCapabilities.ModifiedEnterKeySupport == TerminalCapabilityAvailability.Unknown
                    && _inputCapabilities.Source == TerminalInputCapabilitySource.KittyKeyboardProtocol)
                {
                    PublishInputCapabilities(new TerminalInputCapabilities(
                        TerminalCapabilityAvailability.Unavailable,
                        TerminalInputCapabilitySource.LegacyTerminal));
                }
                return;

            case TerminalInputCapabilitiesChanged capabilitiesChanged:
                _inputCapabilities = capabilitiesChanged.Capabilities;
                break;

            case ShutdownRequested:
                TerminaTrace.Page.Info(this, "ShutdownRequested received");
                Shutdown();
                return;

            case NavigationRequested navReq:
                TerminaTrace.Page.Debug(this, "NavigationRequested: {0}", navReq.PageKey);
                Interlocked.Decrement(ref _pendingNavigationRequests);
                NavigateTo(navReq.PageKey);
                return;

            case NavigationBackRequested:
                TerminaTrace.Page.Debug(this, "NavigationBackRequested, canGoBack={0}", CanGoBack);
                GoBack();
                return;

            case LoopWorkRequested loopWork:
                ExecuteLoopWork(loopWork.Action);
                return;

            case RenderFrameRequested:
                _renderFrameProvider.AdvanceFrame();
                return;

            case ResizeEvent resize:
                TerminaTrace.Render.Debug(this, "ResizeEvent: {0}x{1}", resize.Width, resize.Height);
                // Force full refresh on resize since terminal dimensions changed
                if (_diffingTerminal is not null
                    && resize.Width > 0
                    && resize.Height > 0
                    && (resize.Width != _diffingTerminal.Width || resize.Height != _diffingTerminal.Height))
                {
                    _diffingTerminal.ForceFullRefresh();
                }
                // Fall through to publish on _inputSubject so pages/ViewModels
                // that subscribe to ResizeEvent can recompute width-sensitive
                // layout (matches MouseScrollEvent's break/fall-through pattern
                // a few cases below).
                break;

            case PasteEvent pasteEvent:
                if (_focusManager.CurrentFocus is IPasteReceiver focused)
                {
                    if (focused.HandlePaste(pasteEvent))
                    {
                        ShowPasteToast(pasteEvent.Content);
                    }
                }
                else if (FindPasteReceiver(GetCurrentLayoutRoot()) is { } fallback)
                {
                    if (fallback.HandlePaste(pasteEvent))
                    {
                        ShowPasteToast(pasteEvent.Content);
                    }
                }
                else
                {
                    _inputSubject.OnNext(pasteEvent);
                }
                return;

            case MouseScrollEvent mouseScroll:
                const int linesPerTick = 3;
                if (RouteMouseScroll(mouseScroll, linesPerTick))
                    return;
                break; // No scroll target — fall through to the ViewModel input observable

            case MouseEvent { EventType: InputMouseEventType.Scroll } mouseEvent:
                var adaptedScroll = new MouseScrollEvent(
                    mouseEvent.Button == InputMouseButton.WheelUp ? +1 : -1)
                {
                    X = mouseEvent.X,
                    Y = mouseEvent.Y,
                    Modifiers = mouseEvent.Modifiers,
                };
                if (mouseEvent.Button is InputMouseButton.WheelUp or InputMouseButton.WheelDown
                    && RouteMouseScroll(adaptedScroll, linesPerTick))
                    return;
                break; // Preserve the current public MouseEvent path when no target handles it
        }

        // Route input events: Page (capture) -> Focus Manager (bubble) -> ViewModel
        if (evt is IInputEvent inputEvent)
        {
            // For key presses, use capture-then-bubble pattern
            if (inputEvent is KeyPressed keyPressed)
            {
                TerminaTrace.Input.Trace(this, "KeyPressed: key={0}, mods={1}", keyPressed.KeyInfo.Key, keyPressed.KeyInfo.Modifiers);

                // CAPTURE PHASE: Page-level key bindings get first chance
                // This allows pages to intercept keys (like Escape) before focused components consume them
                if (_currentPage is IBindablePage bindablePage)
                {
                    if (bindablePage.HandlePageInput(keyPressed.KeyInfo))
                    {
                        TerminaTrace.Input.Trace(this, "Key consumed by page-level handler");
                        return; // Input was consumed by page
                    }
                }

                // BUBBLE PHASE: Focused components handle remaining keys
                if (_focusManager.RouteInput(keyPressed))
                {
                    TerminaTrace.Input.Trace(this, "Key consumed by focused component");
                    return; // Input was consumed by focused component
                }
            }

            // If not consumed, route to ViewModel via observable
            TerminaTrace.Input.Trace(this, "Routing input to ViewModel");
            _inputSubject.OnNext(inputEvent);
        }
    }

    /// <summary>
    /// Processes, in order, the events that are in the queue now, and then renders the page once.
    /// </summary>
    /// <remarks>
    /// A render draws the whole page, so one render after a batch shows the same final state as one
    /// render after each event. When a render takes longer than events arrive (for example a very
    /// long line in a text area, with the cursor blink and redraw requests), a render after each
    /// event makes the queue grow without limit, and work posted to the loop, such as a shutdown,
    /// never runs. The batch holds only the events that were queued when it started. Events that
    /// arrive during the batch go to the next batch, so a steady producer cannot postpone the render.
    /// </remarks>
    internal void ProcessQueuedEvents(CancellationToken cancellationToken)
    {
        var reader = _eventChannel.Reader;
        var remaining = Math.Max(1, reader.Count);
        var renderNeeded = false;

        // Stop at a shutdown request: the events after it must not run on a closing application.
        while (remaining-- > 0
               && !cancellationToken.IsCancellationRequested
               && reader.TryRead(out var evt))
        {
            renderNeeded = ProcessQueuedEvent(evt, renderNeeded);
        }

        // Render after a shutdown request too, so the last frame shows every processed event.
        RenderIfNeeded(renderNeeded);
    }

    private void ProcessEventAndMaybeRender(object evt) =>
        RenderIfNeeded(ProcessQueuedEvent(evt, renderNeeded: false));

    /// <summary>
    /// Processes one event and returns whether the page needs a render after it.
    /// </summary>
    private bool ProcessQueuedEvent(object evt, bool renderNeeded)
    {
        // An inline commit renders the page itself.
        if (evt is InlineCommitRequested inlineCommit)
        {
            ProcessInlineCommit(inlineCommit);
            return renderNeeded;
        }

        ProcessEvent(evt);

        return renderNeeded
            || evt is not ApplicationRedrawRequested
            || Volatile.Read(ref _redrawPending) != 0;
    }

    private void RenderIfNeeded(bool renderNeeded)
    {
        // If an event enqueued a navigation, do not render the page that is
        // one event-loop turn away from being replaced.
        if (renderNeeded && !HasPendingNavigationRequests())
            RenderCurrentPage();
    }

    /// <summary>
    /// Empties the queue after the loop stopped. A batch stops at a shutdown request, so events can
    /// stay in the queue. This cancels each task that a caller awaits for one of them, and releases
    /// the cancellation resources of each inline commit.
    /// </summary>
    private void AbandonQueuedEvents()
    {
        while (_eventChannel.Reader.TryRead(out var evt))
        {
            switch (evt)
            {
                case LoopWorkRequested { Abandon: { } abandon }:
                    abandon();
                    break;

                case InlineCommitRequested commit:
                    commit.Completion.TrySetCanceled();
                    commit.Registration.Dispose();
                    commit.Cancellation.Dispose();
                    break;

                case NavigationRequested:
                    Interlocked.Decrement(ref _pendingNavigationRequests);
                    break;
            }
        }
    }

    private void ProcessInlineCommit(InlineCommitRequested request)
    {
        try
        {
            if (request.Completion.Task.IsCompleted)
                return;

            request.Cancellation.Token.ThrowIfCancellationRequested();
            _inlineTerminal!.Commit(request.Content);
            RenderCurrentPage();
            request.Completion.TrySetResult();
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            request.Completion.TrySetCanceled(request.Cancellation.Token);
        }
        catch (Exception ex)
        {
            request.Completion.TrySetException(ex);
        }
        finally
        {
            request.Registration.Dispose();
            request.Cancellation.Dispose();
        }
    }

    private bool HasPendingNavigationRequests() =>
        Volatile.Read(ref _pendingNavigationRequests) > 0;

    private static void ExecuteLoopWork(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            try
            {
                ObservableSystem.GetUnhandledExceptionHandler().Invoke(ex);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Recursively walks the layout tree to find the first <see cref="IPasteReceiver"/>.
    /// </summary>
    private static IPasteReceiver? FindPasteReceiver(ILayoutNode? node)
    {
        if (node is null) return null;
        if (node is IPasteReceiver receiver) return receiver;

        var children = node switch
        {
            LayoutNode layoutNode => layoutNode.GetChildNodes(),
            IContainerNode container => container.Children,
            _ => Enumerable.Empty<ILayoutNode>()
        };

        foreach (var child in children)
        {
            var found = FindPasteReceiver(child);
            if (found != null) return found;
        }
        return null;
    }

    private bool RouteMouseScroll(MouseScrollEvent mouseScroll, int linesPerTick)
    {
        var scrollable = mouseScroll is { X: { } x, Y: { } y }
            ? FindScrollableAt(GetCurrentLayoutRoot(), x, y)
            : null;
        scrollable ??= _focusManager.CurrentFocus as IScrollable;

        if (scrollable is null)
            return false;

        if (mouseScroll.Delta > 0)
            scrollable.ScrollUp(linesPerTick);
        else if (mouseScroll.Delta < 0)
            scrollable.ScrollDown(linesPerTick);

        return mouseScroll.Delta != 0;
    }

    private static IScrollable? FindScrollableAt(ILayoutNode? node, int x, int y)
    {
        if (node is null)
            return null;

        var children = node switch
        {
            LayoutNode layoutNode => layoutNode.GetChildNodes(),
            IContainerNode container => container.Children,
            _ => Enumerable.Empty<ILayoutNode>()
        };

        foreach (var child in children.Reverse())
        {
            if (FindScrollableAt(child, x, y) is { } childTarget)
                return childTarget;
        }

        return node is IPointerScrollable pointerScrollable
            && pointerScrollable.LastRenderedBounds.Contains(x, y)
                ? pointerScrollable
                : null;
    }

    private int GetKittyKeyboardFlags() => (int)_runtimeOptions.KittyKeyboardMode;

    private static TerminalInputCapabilities InitialInputCapabilities(
        TerminalCapabilities platformCapabilities,
        int kittyFlags)
    {
        if (platformCapabilities.PreservesKeyModifiers)
        {
            return new TerminalInputCapabilities(
                TerminalCapabilityAvailability.Available,
                TerminalInputCapabilitySource.NativeConsole);
        }

        return kittyFlags > 0 && platformCapabilities.RawInputActive
            ? new TerminalInputCapabilities(
                TerminalCapabilityAvailability.Unknown,
                TerminalInputCapabilitySource.KittyKeyboardProtocol)
            : new TerminalInputCapabilities(
                TerminalCapabilityAvailability.Unavailable,
                TerminalInputCapabilitySource.LegacyTerminal);
    }

    private void PublishInputCapabilities(TerminalInputCapabilities capabilities)
    {
        if (_inputCapabilities == capabilities)
            return;

        _inputCapabilities = capabilities;
        _inputSubject.OnNext(new TerminalInputCapabilitiesChanged(capabilities));
    }

    private static void ValidateRuntimeOptions(TerminaRuntimeOptions options)
    {
        if (options.PresentationMode == TerminalPresentationMode.Inline
            && options.ScrollInputMode != ScrollInputMode.NativeTerminal)
        {
            throw new ArgumentException(
                $"{nameof(TerminalPresentationMode.Inline)} mode requires {nameof(ScrollInputMode.NativeTerminal)} scroll input.",
                nameof(options));
        }

        if (options.PresentationMode == TerminalPresentationMode.FullScreen
            && options.ScrollInputMode == ScrollInputMode.NativeTerminal)
        {
            throw new ArgumentException(
                $"{nameof(ScrollInputMode.NativeTerminal)} scroll input requires {nameof(TerminalPresentationMode.Inline)} mode.",
                nameof(options));
        }
    }

    private bool ShouldInterceptCtrlC() => _runtimeOptions.CtrlCHandlingMode switch
    {
        CtrlCHandlingMode.Disabled => false,
        CtrlCHandlingMode.DoublePressWhenRawInput => _rawInputActive,
        CtrlCHandlingMode.DoublePressAlways => true,
        _ => false,
    };

    /// <summary>
    /// Renders the current page directly to the terminal.
    /// </summary>
    /// <remarks>
    /// When using DiffingTerminal, ClearScreen() only clears the pending buffer,
    /// not the actual screen. On Flush(), only changed cells are output.
    /// </remarks>
    private void RenderCurrentPage()
    {
        // This render draws every change that was requested before this point. Clear the flag
        // first, so that a request during the render still causes one more render.
        Interlocked.Exchange(ref _redrawPending, 0);

        var layoutRoot = GetCurrentLayoutRoot() ?? new TextNode("No page active");
        if (_toastOverlay != null)
        {
            layoutRoot = new StackLayout([layoutRoot, new DeferredNode(() => _toastOverlay)]);
        }

        // Clear the pending buffer (DiffingTerminal) or screen (other terminals)
        _terminal.ClearScreen();

        // Measure and render the layout
        var available = new Size(_terminal.Width, _terminal.Height);
        var measured = layoutRoot.Measure(available);

        // Create a full-screen render context
        var context = new RegionRenderContext(_terminal, 0, 0, _terminal.Width, _terminal.Height);
        var bounds = new Rect(0, 0, _terminal.Width, _terminal.Height);

        layoutRoot.Render(context, bounds);

        // Flush output
        _terminal.Flush();
    }

    /// <summary>
    /// Gets the layout root from the current page.
    /// </summary>
    private ILayoutNode? GetCurrentLayoutRoot()
    {
        if (_currentPage == null)
            return null;

        // If it's a ReactivePage, get the cached layout root
        if (_currentPage is IBindablePage bindable)
        {
            var layoutRoot = bindable.LayoutRoot;
            if (layoutRoot != null)
                return layoutRoot;
        }

        // Fall back to building the layout fresh
        return _currentPage.BuildLayout();
    }

    private void ShowPasteToast(string content)
    {
        if (_toastService is null)
            return;

        var lineCount = content.Count(c => c == '\n') + 1;
        var message = lineCount > 1
            ? $"Pasted {lineCount} lines"
            : $"Pasted {content.Length} characters";

        _toastService.Show(message);
    }

    /// <summary>
    /// Clears the page cache and disposes all cached pages.
    /// </summary>
    private void ClearPageCache()
    {
        foreach (var (page, viewModel) in _cachedPages.Values)
        {
            // Properly dispose cached pages
            if (page is IDisposable disposablePage)
                disposablePage.Dispose();
            viewModel.Dispose();
        }
        _cachedPages.Clear();
    }

    /// <summary>
    /// Disposes the application and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        // Dispose current page if it's disposable
        if (_currentPage is IDisposable currentDisposable)
            currentDisposable.Dispose();

        // Clear and dispose all cached pages
        ClearPageCache();

        // Complete and dispose the input subject
        _inputSubject.OnCompleted();
        _inputSubject.Dispose();

        // Dispose all input sources
        foreach (var source in _inputSources)
        {
            if (source is IDisposable disposable)
                disposable.Dispose();
        }

        _toastInvalidationSubscription?.Dispose();
        _toastOverlay?.Dispose();
        _renderFrameProvider.Dispose();
    }

    /// <param name="Action">The work to run on the loop.</param>
    /// <param name="Abandon">Settles the caller's task when the loop stops before the work runs.</param>
    private sealed record LoopWorkRequested(Action Action, Action? Abandon = null);

    /// <summary>
    /// The redraw event that <see cref="RequestRedraw"/> queues. The loop tells it apart from a
    /// <see cref="RedrawRequested"/> that an input source writes, because only this event can be
    /// skipped when the redraw flag is clear.
    /// </summary>
    private sealed class ApplicationRedrawRequested
    {
        public static readonly ApplicationRedrawRequested Instance = new();

        private ApplicationRedrawRequested()
        {
        }
    }

    private sealed record InlineCommitRequested(
        ILayoutNode Content,
        TaskCompletionSource Completion,
        CancellationTokenSource Cancellation,
        CancellationTokenRegistration Registration);

    private sealed class RenderFrameRequested
    {
        public static readonly RenderFrameRequested Instance = new();

        private RenderFrameRequested()
        {
        }
    }

    private sealed class DefaultObservableSystemScope(FrameProvider previousFrameProvider) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            ObservableSystem.DefaultFrameProvider = previousFrameProvider;
            _disposed = true;
        }
    }
}

/// <summary>
/// Registration information for a reactive page.
/// </summary>
internal sealed class ReactivePageRegistration
{
    public RouteTemplate RouteTemplate { get; }
    public NavigationBehavior Behavior { get; }
    public Func<IPage> PageFactory { get; }
    public Func<ReactiveViewModel> ViewModelFactory { get; }

    public ReactivePageRegistration(
        RouteTemplate routeTemplate,
        NavigationBehavior behavior,
        Func<IPage> pageFactory,
        Func<ReactiveViewModel> viewModelFactory)
    {
        RouteTemplate = routeTemplate;
        Behavior = behavior;
        PageFactory = pageFactory;
        ViewModelFactory = viewModelFactory;
    }
}
