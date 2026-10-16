// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Threading.Channels;
using R3;
using Termina.Input;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests;

/// <summary>
/// The event loop must not render a page that a queued navigation is about to replace. These tests
/// run the real loop (<c>RunAsync</c> and <c>ProcessQueuedEvents</c>). <see cref="NavigationThreadSafetyTests"/>
/// covers the same rule through a private helper that the loop no longer calls.
/// </summary>
public class EventLoopNavigationBatchingTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RunAsync_does_not_render_the_page_that_input_navigates_away_from()
    {
        var app = CreateApp(navTarget: "/dest", watched: "dest");

        await RunUntilWatchedPageRendersAsync(app);

        // The start page renders once, before the key. The Enter key queues a navigation, so the
        // loop skips the render of the start page and draws the destination.
        Assert.Equal(["start", "dest"], Scenario.Rendered);
        Assert.Equal("/dest", app.CurrentPath);
    }

    [Fact]
    public async Task RunAsync_does_not_render_a_page_that_redirects_on_activation()
    {
        var app = CreateApp(navTarget: "/redirecting", watched: "final");

        await RunUntilWatchedPageRendersAsync(app);

        // The middle page navigates to the final page when it activates. It never renders.
        Assert.Equal(["start", "final"], Scenario.Rendered);
        Assert.Equal("/final", app.CurrentPath);
    }

    [Fact]
    public void A_navigation_queued_by_one_event_in_a_batch_skips_the_batch_render()
    {
        var app = CreateApp(navTarget: "/dest", watched: "dest");
        var writer = GetEventChannel(app).Writer;

        // One batch holds the key that navigates and two events that would force a render.
        writer.TryWrite(new KeyPressed(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        app.Post(() => { });
        app.RequestRedraw();
        app.ProcessQueuedEvents(CancellationToken.None);

        Assert.Empty(Scenario.Rendered);
        Assert.Equal("/start", app.CurrentPath);

        // The next batch holds the navigation. It draws the destination.
        app.ProcessQueuedEvents(CancellationToken.None);

        Assert.Equal(["dest"], Scenario.Rendered);
        Assert.Equal("/dest", app.CurrentPath);
    }

    private static TerminaApplication CreateApp(string navTarget, string watched)
    {
        Scenario.Reset(navTarget, watched);
        var app = new TerminaApplication(new VirtualTerminal(80, 24));
        app.RegisterRoute<Start, StartViewModel>("/start");
        app.RegisterRoute<Named, NamedViewModel>("/dest");
        app.RegisterRoute<Final, NamedViewModel>("/final");
        app.RegisterRoute<Redirecting, RedirectingViewModel>("/redirecting");
        app.NavigateTo("/start");
        // Navigation can queue a redraw. Start with an empty queue.
        var queue = GetEventChannel(app);
        while (queue.Reader.TryRead(out _))
        {
        }

        return app;
    }

    private static async Task RunUntilWatchedPageRendersAsync(TerminaApplication app)
    {
        app.AddInputSource(new EnterSource());
        var run = Task.Run(() => app.RunAsync());
        try
        {
            await Scenario.WatchedRendered.Task.WaitAsync(Guard);
        }
        catch (TimeoutException)
        {
            Assert.Fail("The destination page did not render.");
        }
        finally
        {
            app.Shutdown();
            await run.WaitAsync(Guard);
        }
    }

    private static Channel<object> GetEventChannel(TerminaApplication app)
    {
        var field = typeof(TerminaApplication).GetField(
            "_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (Channel<object>)field!.GetValue(app)!;
    }

    // The framework creates the pages, so they read the shared state of the running test here.
    // The tests of this class run one after another.
    private static class Scenario
    {
        public static List<string> Rendered { get; private set; } = [];
        public static string NavTarget { get; private set; } = "";
        public static string Watched { get; private set; } = "";
        public static TaskCompletionSource StartRendered { get; private set; } = New();
        public static TaskCompletionSource WatchedRendered { get; private set; } = New();

        public static void Reset(string navTarget, string watched)
        {
            Rendered = [];
            NavTarget = navTarget;
            Watched = watched;
            StartRendered = New();
            WatchedRendered = New();
        }

        private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Waits for the first render of the start page, then writes Enter.
    private sealed class EnterSource : IInputSource
    {
        public async Task RunAsync(ChannelWriter<object> writer, CancellationToken cancellationToken)
        {
            await Scenario.StartRendered.Task.WaitAsync(cancellationToken);
            writer.TryWrite(new KeyPressed(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        }
    }

    private sealed class LogNode(string name) : ILayoutNode
    {
        public SizeConstraint WidthConstraint { get; } = new SizeConstraint.Fill();
        public SizeConstraint HeightConstraint { get; } = new SizeConstraint.Fixed(1);

        public Size Measure(Size available) => new(available.Width, 1);

        public void Render(IRenderContext context, Rect bounds)
        {
            Scenario.Rendered.Add(name);
            context.WriteAt(bounds.X, bounds.Y, name);
            if (name == "start")
                Scenario.StartRendered.TrySetResult();
            if (name == Scenario.Watched)
                Scenario.WatchedRendered.TrySetResult();
        }

        public void Dispose()
        {
        }
    }

    private sealed class StartViewModel : ReactiveViewModel
    {
        public override void OnActivated()
        {
            base.OnActivated();
            Input.OfType<IInputEvent, KeyPressed>()
                .Subscribe(key =>
                {
                    if (key.KeyInfo.Key == ConsoleKey.Enter)
                        Navigate(Scenario.NavTarget);
                })
                .DisposeWith(Subscriptions);
        }
    }

    private sealed class RedirectingViewModel : ReactiveViewModel
    {
        public override void OnActivated()
        {
            base.OnActivated();
            Navigate("/final");
        }
    }

    private sealed class NamedViewModel : ReactiveViewModel
    {
    }

    private sealed class Start : ReactivePage<StartViewModel>
    {
        public override ILayoutNode BuildLayout() => new LogNode("start");
    }

    private sealed class Named : ReactivePage<NamedViewModel>
    {
        public override ILayoutNode BuildLayout() => new LogNode("dest");
    }

    private sealed class Final : ReactivePage<NamedViewModel>
    {
        public override ILayoutNode BuildLayout() => new LogNode("final");
    }

    private sealed class Redirecting : ReactivePage<RedirectingViewModel>
    {
        public override ILayoutNode BuildLayout() => new LogNode("middle");
    }
}
