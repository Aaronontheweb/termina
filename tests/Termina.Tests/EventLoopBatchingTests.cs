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
/// The event loop processes the queued events as a batch and renders the page once for each batch.
/// A render after each event made the queue grow without limit when one render was slower than the
/// arrival of events, and work posted to the loop, such as a shutdown, did not run.
/// </summary>
public class EventLoopBatchingTests
{
    [Fact]
    public void Queued_redraw_requests_cause_one_render()
    {
        var (app, page) = CreateApp();

        for (var i = 0; i < 50; i++)
            app.RequestRedraw();
        app.ProcessQueuedEvents(CancellationToken.None);

        Assert.Equal(1, page.Probe.Renders);
        Assert.Equal(0, QueueLength(app));
    }

    [Fact]
    public void Redraw_requests_that_a_render_already_drew_cause_no_render()
    {
        var (app, page) = CreateApp();
        var observed = 0;
        using var subscription = app.Input.OfType<IInputEvent, RedrawRequested>().Subscribe(_ => observed++);

        // A view model changes its state in loop work and requests a redraw five times. The render
        // after the work already shows the new state.
        app.Post(() =>
        {
            for (var i = 0; i < 5; i++)
                app.RequestRedraw();
        });
        app.ProcessQueuedEvents(CancellationToken.None);
        app.ProcessQueuedEvents(CancellationToken.None);

        Assert.Equal(1, page.Probe.Renders);
        Assert.Equal(0, QueueLength(app));
        // Input subscribers still get every redraw request.
        Assert.Equal(5, observed);
    }

    [Fact]
    public void Redraw_request_during_a_render_causes_one_more_render()
    {
        var (app, page) = CreateApp();
        page.Probe.OnRender = () =>
        {
            // A background thread requests a redraw while the page renders.
            page.Probe.OnRender = null;
            app.RequestRedraw();
        };

        app.Post(() => { });
        app.ProcessQueuedEvents(CancellationToken.None);
        Assert.Equal(1, page.Probe.Renders);

        app.ProcessQueuedEvents(CancellationToken.None);
        Assert.Equal(2, page.Probe.Renders);

        app.ProcessQueuedEvents(CancellationToken.None);
        Assert.Equal(2, page.Probe.Renders);
    }

    [Fact]
    public void Batch_holds_only_the_events_that_were_queued_when_it_started()
    {
        var (app, page) = CreateApp();
        var ran = new List<string>();
        app.Post(() =>
        {
            ran.Add("first");
            app.Post(() => ran.Add("second"));
        });

        app.ProcessQueuedEvents(CancellationToken.None);

        Assert.Equal(["first"], ran);
        Assert.Equal(1, page.Probe.Renders);

        app.ProcessQueuedEvents(CancellationToken.None);

        Assert.Equal(["first", "second"], ran);
        Assert.Equal(2, page.Probe.Renders);
    }

    [Fact]
    public void Events_after_a_shutdown_do_not_run_and_the_last_frame_is_drawn()
    {
        var (app, page) = CreateApp();
        using var shutdown = new CancellationTokenSource();
        var ranAfterShutdown = false;
        app.Post(() => page.Status = "changed");
        app.Post(shutdown.Cancel);
        app.Post(() => ranAfterShutdown = true);

        app.ProcessQueuedEvents(shutdown.Token);

        Assert.False(ranAfterShutdown);
        Assert.Equal(1, page.Probe.Renders);
        Assert.Equal("changed", page.Probe.LastRendered);
    }

    [Fact]
    public async Task RunAsync_stops_after_a_shutdown_while_a_producer_keeps_queueing_events()
    {
        var (app, page) = CreateApp();
        app.AddInputSource(new IdleInputSource());

        // This work queues itself again each time it runs, so the queue is never empty. The loop
        // must still observe the shutdown.
        void Requeue() => app.Post(Requeue);
        app.Post(Requeue);
        for (var i = 0; i < 20; i++)
            app.RequestRedraw();
        app.Post(app.Shutdown);

        // Run the loop on another thread, so a loop that never yields fails the wait and does not hang the test.
        var run = Task.Run(() => app.RunAsync());
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        // One initial render and one render for the batch, not one render for each event.
        Assert.Equal(2, page.Probe.Renders);
    }

    [Fact]
    public async Task RunAsync_with_a_long_line_renders_once_for_many_queued_events()
    {
        var (app, page) = CreateApp();
        app.AddInputSource(new IdleInputSource());
        page.Area.Text = new string('a', 600_000);
        page.Area.MoveCursorToEnd();

        // An app that refuses a submitted message changes the text, the transcript and the status,
        // and each change requests a redraw.
        for (var i = 0; i < 25; i++)
            app.RequestRedraw();
        app.Post(app.Shutdown);

        // Run the loop on another thread, so a loop that never yields fails the wait and does not hang the test.
        var run = Task.Run(() => app.RunAsync());
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(2, page.Probe.Renders);
    }

    private static (TerminaApplication App, BatchPage Page) CreateApp()
    {
        var app = new TerminaApplication(new VirtualTerminal(80, 24));
        app.RegisterRoute<BatchPage, BatchViewModel>("/");
        app.NavigateTo("/");
        var page = BatchPage.Last!;
        // Navigation can queue a redraw. Start each test with an empty queue and no renders.
        DrainQueue(app);
        page.Probe.Renders = 0;
        return (app, page);
    }

    private static Channel<object> GetEventChannel(TerminaApplication app)
    {
        var field = typeof(TerminaApplication).GetField(
            "_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (Channel<object>)field!.GetValue(app)!;
    }

    private static int QueueLength(TerminaApplication app) => GetEventChannel(app).Reader.Count;

    private static void DrainQueue(TerminaApplication app)
    {
        while (GetEventChannel(app).Reader.TryRead(out _))
        {
        }
    }

    private sealed class IdleInputSource : IInputSource
    {
        public Task RunAsync(ChannelWriter<object> writer, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RenderProbe(Func<string> text) : ILayoutNode
    {
        public int Renders;
        public string? LastRendered;
        public Action? OnRender;

        public SizeConstraint WidthConstraint { get; } = new SizeConstraint.Fill();
        public SizeConstraint HeightConstraint { get; } = new SizeConstraint.Fixed(1);

        public Size Measure(Size available) => new(available.Width, 1);

        public void Render(IRenderContext context, Rect bounds)
        {
            Renders++;
            LastRendered = text();
            context.WriteAt(bounds.X, bounds.Y, LastRendered);
            OnRender?.Invoke();
        }

        public void Dispose()
        {
        }
    }

    private sealed class BatchViewModel : ReactiveViewModel
    {
    }

    private sealed class BatchPage : ReactivePage<BatchViewModel>
    {
        public static BatchPage? Last;

        public BatchPage()
        {
            Last = this;
            Probe = new RenderProbe(() => Status);
        }

        public string Status { get; set; } = "ready";

        public RenderProbe Probe { get; }

        public TextAreaNode Area { get; } = new();

        public override ILayoutNode BuildLayout() => Layouts.Vertical()
            .WithChild(Area)
            .WithChild(Probe);
    }
}
