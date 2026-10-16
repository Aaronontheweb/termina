// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Threading.Channels;
using Termina.Input;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests;

/// <summary>
/// <see cref="RedrawRequested.Instance"/> and the channel writer that an <see cref="IInputSource"/>
/// receives are public. An event that an input source writes straight to the channel must cause a
/// render, as it did before the loop coalesced redraw requests.
/// </summary>
public class EventLoopRedrawContractTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public void RedrawRequested_written_to_the_channel_causes_a_render()
    {
        var (app, page) = CreateApp();
        SettleFirstRender(app, page);

        page.Status = "changed";
        GetEventChannel(app).Writer.TryWrite(RedrawRequested.Instance);
        app.ProcessQueuedEvents(CancellationToken.None);

        Assert.Equal(1, page.Probe.Renders);
        Assert.Equal("changed", page.Probe.LastRendered);
    }

    [Fact]
    public async Task RunAsync_renders_for_a_RedrawRequested_that_an_input_source_writes()
    {
        var (app, page) = CreateApp();
        var source = new RedrawSource(page);
        app.AddInputSource(source);

        var run = Task.Run(() => app.RunAsync());
        try
        {
            // The source changes the page state and writes the event. It never calls RequestRedraw.
            await source.Rendered.Task.WaitAsync(Guard);
        }
        catch (TimeoutException)
        {
            Assert.Fail("The loop did not render after an input source wrote RedrawRequested.");
        }
        finally
        {
            app.Shutdown();
            await run.WaitAsync(Guard);
        }

        Assert.Equal("changed", page.Probe.LastRendered);
    }

    private static (TerminaApplication App, RedrawPage Page) CreateApp()
    {
        var app = new TerminaApplication(new VirtualTerminal(80, 24));
        app.RegisterRoute<RedrawPage, RedrawViewModel>("/");
        app.NavigateTo("/");
        var page = RedrawPage.Last!;
        DrainQueue(app);
        return (app, page);
    }

    // Draw once, so that the redraw flag is clear. Then the test starts at the state after a render.
    private static void SettleFirstRender(TerminaApplication app, RedrawPage page)
    {
        app.Post(() => { });
        app.ProcessQueuedEvents(CancellationToken.None);
        Assert.Equal(1, page.Probe.Renders);
        page.Probe.Renders = 0;
    }

    private static Channel<object> GetEventChannel(TerminaApplication app)
    {
        var field = typeof(TerminaApplication).GetField(
            "_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (Channel<object>)field!.GetValue(app)!;
    }

    private static void DrainQueue(TerminaApplication app)
    {
        while (GetEventChannel(app).Reader.TryRead(out _))
        {
        }
    }

    private sealed class RedrawSource(RedrawPage page) : IInputSource
    {
        public TaskCompletionSource Rendered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(ChannelWriter<object> writer, CancellationToken cancellationToken)
        {
            // Wait for the first render, so that the redraw flag is clear when the event arrives.
            await page.Probe.FirstRender.Task.WaitAsync(cancellationToken);
            page.Probe.OnRendered = text =>
            {
                if (text == "changed")
                    Rendered.TrySetResult();
            };
            page.Status = "changed";
            writer.TryWrite(RedrawRequested.Instance);
        }
    }

    private sealed class RenderProbe(Func<string> text) : ILayoutNode
    {
        public int Renders;
        public volatile string? LastRendered;
        public volatile Action<string>? OnRendered;

        public TaskCompletionSource FirstRender { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SizeConstraint WidthConstraint { get; } = new SizeConstraint.Fill();
        public SizeConstraint HeightConstraint { get; } = new SizeConstraint.Fixed(1);

        public Size Measure(Size available) => new(available.Width, 1);

        public void Render(IRenderContext context, Rect bounds)
        {
            Renders++;
            var current = text();
            LastRendered = current;
            context.WriteAt(bounds.X, bounds.Y, current);
            FirstRender.TrySetResult();
            OnRendered?.Invoke(current);
        }

        public void Dispose()
        {
        }
    }

    private sealed class RedrawViewModel : ReactiveViewModel
    {
    }

    private sealed class RedrawPage : ReactivePage<RedrawViewModel>
    {
        public static RedrawPage? Last;
        private volatile string _status = "ready";

        public RedrawPage()
        {
            Last = this;
            Probe = new RenderProbe(() => _status);
        }

        public string Status
        {
            get => _status;
            set => _status = value;
        }

        public RenderProbe Probe { get; }

        public override ILayoutNode BuildLayout() => Probe;
    }
}
