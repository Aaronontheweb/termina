// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Threading.Channels;
using Termina.Input;
using Termina.Layout;
using Termina.Reactive;
using Termina.Terminal;

namespace Termina.Tests;

/// <summary>
/// The event loop stops at a shutdown request. A caller that awaits work queued behind the shutdown
/// must not wait forever.
/// </summary>
public class EventLoopShutdownTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task InvokeAsync_queued_behind_a_shutdown_does_not_hang()
    {
        var app = CreateApp();
        app.AddInputSource(new IdleInputSource());
        

        // The shutdown runs first. The work after it is already in the queue when the loop sees the
        // cancelled token.
        app.Post(app.Shutdown);
        app.Post(() => { });
        var invoked = app.InvokeAsync(() => { });

        await Task.Run(() => app.RunAsync()).WaitAsync(Guard);

        // The loop has stopped, so nothing else can finish the task. The bounded wait turns a hang
        // into a failure.
        await AssertSettledAsync(invoked);
        Assert.True(
            invoked.IsCompletedSuccessfully || invoked.IsCanceled || invoked.IsFaulted,
            $"InvokeAsync task ended in state {invoked.Status}.");
    }

    [Fact]
    public async Task InvokeAsync_does_not_report_success_for_an_action_that_did_not_run()
    {
        var app = CreateApp();
        app.AddInputSource(new IdleInputSource());
        var actionRan = false;

        app.Post(app.Shutdown);
        var invoked = app.InvokeAsync(() => actionRan = true);

        await Task.Run(() => app.RunAsync()).WaitAsync(Guard);
        await AssertSettledAsync(invoked);

        // The task may succeed only when the action ran. Otherwise it must be canceled or faulted.
        Assert.Equal(actionRan, invoked.IsCompletedSuccessfully);
        Assert.True(invoked.IsCompletedSuccessfully || invoked.IsCanceled || invoked.IsFaulted);
    }

    private static async Task AssertSettledAsync(Task task)
    {
        try
        {
            await task.WaitAsync(Guard);
        }
        catch (TimeoutException)
        {
            Assert.Fail("The InvokeAsync task did not complete after the event loop stopped.");
        }
        catch (Exception)
        {
            // A canceled or faulted task is a settled task. The caller asserts its state.
        }
    }

    private static TerminaApplication CreateApp()
    {
        var app = new TerminaApplication(new VirtualTerminal(80, 24));
        app.RegisterRoute<IdlePage, IdleViewModel>("/");
        app.NavigateTo("/");
        return app;
    }

    private sealed class IdleInputSource : IInputSource
    {
        public Task RunAsync(ChannelWriter<object> writer, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class IdleViewModel : ReactiveViewModel
    {
    }

    private sealed class IdlePage : ReactivePage<IdleViewModel>
    {
        public override ILayoutNode BuildLayout() => new TextNode("idle");
    }
}
