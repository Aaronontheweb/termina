// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Reflection;
using R3;
using Termina.Hosting;
using Termina.Input;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Hosting;

public class TerminaRuntimeOptionsTests
{
    [Fact]
    public void Defaults_preserve_full_screen_and_legacy_mouse_behavior()
    {
        var options = new TerminaRuntimeOptions();

        Assert.Equal(TerminalPresentationMode.FullScreen, options.PresentationMode);
        Assert.Equal(ScrollInputMode.LegacyMouseTracking, options.ScrollInputMode);
        Assert.Equal(0, (int)TerminalPresentationMode.FullScreen);
        Assert.Equal(1, (int)TerminalPresentationMode.Inline);
        Assert.Equal(0, (int)ScrollInputMode.LegacyMouseTracking);
        Assert.Equal(1, (int)ScrollInputMode.AlternateScroll);
        Assert.Equal(2, (int)ScrollInputMode.NativeTerminal);
    }

    [Fact]
    public async Task RunAsync_InlineModeKeepsPrimaryBufferAndNativeInputOwnership()
    {
        var terminal = new VirtualTerminal();
        var options = new TerminaRuntimeOptions
        {
            PresentationMode = TerminalPresentationMode.Inline,
            ScrollInputMode = ScrollInputMode.NativeTerminal,
        };
        var app = CreateApp(terminal, options);
        app.AddInputSource(new CompletedInputSource());
        app.RegisterRoute<TestPage, TestViewModel>("/");
        app.NavigateTo("/");
        using var cts = new CancellationTokenSource();

        var runTask = app.RunAsync(cts.Token);

        Assert.False(terminal.InAlternateScreen);
        Assert.False(terminal.MouseEnabled);
        Assert.False(terminal.WheelScrollEnabled);
        Assert.True(terminal.Contains("test"));

        cts.Cancel();
        await runTask;
        Assert.True(terminal.CursorVisible);
        Assert.False(terminal.Contains("test"));
    }

    [Fact]
    public async Task CommitAsync_keeps_stable_content_after_inline_exit()
    {
        var terminal = new VirtualTerminal();
        var options = new TerminaRuntimeOptions
        {
            PresentationMode = TerminalPresentationMode.Inline,
            ScrollInputMode = ScrollInputMode.NativeTerminal,
        };
        var app = CreateApp(terminal, options);
        app.AddInputSource(new CompletedInputSource());
        app.RegisterRoute<TestPage, TestViewModel>("/");
        app.NavigateTo("/");
        using var cts = new CancellationTokenSource();
        var runTask = app.RunAsync(cts.Token);

        await app.CommitAsync(new TextNode("settled"), CancellationToken.None);

        Assert.Equal("settled", terminal.GetLine(0));
        Assert.Equal("test", terminal.GetLine(1));

        cts.Cancel();
        await runTask;

        Assert.Equal("settled", terminal.GetLine(0));
        Assert.Equal(string.Empty, terminal.GetLine(1));
    }

    [Fact]
    public async Task CommitAsync_keeps_the_submission_order_for_pending_commits()
    {
        var terminal = new VirtualTerminal();
        var options = new TerminaRuntimeOptions
        {
            PresentationMode = TerminalPresentationMode.Inline,
            ScrollInputMode = ScrollInputMode.NativeTerminal,
        };
        var app = CreateApp(terminal, options);
        app.AddInputSource(new CompletedInputSource());
        app.RegisterRoute<TestPage, TestViewModel>("/");
        app.NavigateTo("/");
        using var cts = new CancellationTokenSource();
        var runTask = app.RunAsync(cts.Token);

        var firstCommit = app.CommitAsync(new TextNode("first"), CancellationToken.None);
        var secondCommit = app.CommitAsync(new TextNode("second"), CancellationToken.None);
        await Task.WhenAll(firstCommit.AsTask(), secondCommit.AsTask());

        Assert.Equal("first", terminal.GetLine(0));
        Assert.Equal("second", terminal.GetLine(1));
        Assert.Equal("test", terminal.GetLine(2));

        cts.Cancel();
        await runTask;
    }

    [Fact]
    public async Task RunAsync_recovers_terminal_and_input_after_inline_render_failure()
    {
        var terminal = new VirtualTerminal();
        var options = new TerminaRuntimeOptions
        {
            PresentationMode = TerminalPresentationMode.Inline,
            ScrollInputMode = ScrollInputMode.NativeTerminal,
        };
        var input = new CancellationAwareInputSource();
        var app = CreateApp(terminal, options);
        app.AddInputSource(input);
        app.RegisterRoute<ThrowingPage, TestViewModel>("/");
        app.NavigateTo("/");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.RunAsync());

        Assert.Equal("Render failure", exception.Message);
        Assert.True(input.CancellationObserved);
        Assert.True(terminal.CursorVisible);
        Assert.False(terminal.InAlternateScreen);
        Assert.False(terminal.MouseEnabled);
        Assert.False(terminal.WheelScrollEnabled);
    }

    [Theory]
    [InlineData(TerminalPresentationMode.Inline, ScrollInputMode.LegacyMouseTracking)]
    [InlineData(TerminalPresentationMode.Inline, ScrollInputMode.AlternateScroll)]
    [InlineData(TerminalPresentationMode.FullScreen, ScrollInputMode.NativeTerminal)]
    public void Constructor_rejects_incompatible_presentation_and_scroll_modes(
        TerminalPresentationMode presentationMode,
        ScrollInputMode scrollInputMode)
    {
        var options = new TerminaRuntimeOptions
        {
            PresentationMode = presentationMode,
            ScrollInputMode = scrollInputMode,
        };

        Assert.Throws<ArgumentException>(() => CreateApp(new VirtualTerminal(), options));
    }

    [Fact]
    public void Constructor_rejects_inline_mode_for_a_terminal_without_relative_control()
    {
        var options = new TerminaRuntimeOptions
        {
            PresentationMode = TerminalPresentationMode.Inline,
            ScrollInputMode = ScrollInputMode.NativeTerminal,
        };
        var terminal = new DiffingTerminal(new VirtualTerminal());

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TerminaApplication(terminal, options));

        Assert.Contains(nameof(IInlineTerminalControl), exception.Message);
    }

    [Fact]
    public async Task RunAsync_DefaultsToLegacyMouseTracking()
    {
        var terminal = new VirtualTerminal();
        var app = CreateApp(terminal);
        app.RegisterRoute<TestPage, TestViewModel>("/");
        app.NavigateTo("/");

        using var cts = new CancellationTokenSource();
        var runTask = app.RunAsync(cts.Token);
        await WaitForConditionAsync(() => terminal.MouseEnabled);

        Assert.True(terminal.InAlternateScreen);
        Assert.True(terminal.MouseEnabled);
        Assert.False(terminal.WheelScrollEnabled);

        cts.Cancel();
        await runTask;

        Assert.False(terminal.InAlternateScreen);
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public async Task RunAsync_AlternateScrollWithoutRawInput_FallsBackToLegacyMouseTracking()
    {
        var terminal = new VirtualTerminal();
        var options = new TerminaRuntimeOptions
        {
            ScrollInputMode = ScrollInputMode.AlternateScroll,
        };

        var app = CreateApp(terminal, options);
        app.RegisterRoute<TestPage, TestViewModel>("/");
        app.NavigateTo("/");

        using var cts = new CancellationTokenSource();
        var runTask = app.RunAsync(cts.Token);
        await WaitForConditionAsync(() => terminal.MouseEnabled);

        Assert.True(terminal.MouseEnabled);
        Assert.False(terminal.WheelScrollEnabled);

        cts.Cancel();
        await runTask;
    }

    [Fact]
    public void ProcessEvent_DefaultCtrlCHandling_IgnoresCtrlCWhenRawInputInactive()
    {
        var app = CreateApp(new VirtualTerminal(), new TerminaRuntimeOptions());

        InvokeProcessEvent(app, new KeyPressed(new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, true)));

        Assert.Null(GetFirstCtrlCAt(app));
    }

    [Fact]
    public void ProcessEvent_DefaultCtrlCHandling_InterceptsCtrlCWhenRawInputActive()
    {
        var app = CreateApp(new VirtualTerminal(), new TerminaRuntimeOptions());
        SetRawInputActive(app, true);

        InvokeProcessEvent(app, new KeyPressed(new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, true)));

        Assert.NotNull(GetFirstCtrlCAt(app));
    }

    [Fact]
    public void ProcessEvent_KittyFlagReportMarksModifiedEnterAvailable()
    {
        var app = CreateApp(new VirtualTerminal(), new TerminaRuntimeOptions());
        var changes = new List<TerminalInputCapabilitiesChanged>();
        using var subscription = app.Input
            .OfType<IInputEvent, TerminalInputCapabilitiesChanged>()
            .Subscribe(changes.Add);
        SetInputCapabilities(app, new TerminalInputCapabilities(
            TerminalCapabilityAvailability.Unknown,
            TerminalInputCapabilitySource.KittyKeyboardProtocol));

        InvokeProcessEvent(app, new KittyKeyboardFlagsReported(9));

        Assert.Equal(
            new TerminalInputCapabilities(
                TerminalCapabilityAvailability.Available,
                TerminalInputCapabilitySource.KittyKeyboardProtocol),
            app.InputCapabilities);
        Assert.Equal(app.InputCapabilities, Assert.Single(changes).Capabilities);
    }

    [Fact]
    public void ProcessEvent_DeviceAttributesWithoutKittyReportMarksModifiedEnterUnavailable()
    {
        var app = CreateApp(new VirtualTerminal(), new TerminaRuntimeOptions());
        var changes = new List<TerminalInputCapabilitiesChanged>();
        using var subscription = app.Input
            .OfType<IInputEvent, TerminalInputCapabilitiesChanged>()
            .Subscribe(changes.Add);
        SetInputCapabilities(app, new TerminalInputCapabilities(
            TerminalCapabilityAvailability.Unknown,
            TerminalInputCapabilitySource.KittyKeyboardProtocol));

        InvokeProcessEvent(app, new PrimaryDeviceAttributesReported());

        Assert.Equal(
            new TerminalInputCapabilities(
                TerminalCapabilityAvailability.Unavailable,
                TerminalInputCapabilitySource.LegacyTerminal),
            app.InputCapabilities);
        Assert.Equal(app.InputCapabilities, Assert.Single(changes).Capabilities);
    }

    [Fact]
    public void EscapeSequenceParser_CsiArrowIsKeyBeforeDeckmConfirmed_AndKeyWithKitty()
    {
        // Without DECCKM confirmed, CSI A is a keyboard arrow.
        var parser = new EscapeSequenceParser { KittyReportAllKeysVisible = false };
        var events = FeedString(parser, "\x1b[A");

        var key1 = Assert.Single(events);
        Assert.IsType<KeyPressed>(key1);

        // With Kitty active, CSI A is also a keyboard arrow.
        parser = new EscapeSequenceParser { KittyReportAllKeysVisible = true };
        events = FeedString(parser, "\x1b[A");

        var key2 = Assert.Single(events);
        Assert.IsType<KeyPressed>(key2);
    }

    private static TerminaApplication CreateApp(VirtualTerminal terminal, TerminaRuntimeOptions? options = null)
    {
        var services = new TestServiceProvider();
        return new TerminaApplication(terminal, options, services);
    }

    private static void InvokeProcessEvent(TerminaApplication app, object evt)
    {
        var method = typeof(TerminaApplication).GetMethod(
            "ProcessEvent",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        method!.Invoke(app, [evt]);
    }

    private static DateTime? GetFirstCtrlCAt(TerminaApplication app)
    {
        var field = typeof(TerminaApplication).GetField(
            "_firstCtrlCAt",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(field);
        return (DateTime?)field!.GetValue(app);
    }

    private static void SetRawInputActive(TerminaApplication app, bool value)
    {
        var field = typeof(TerminaApplication).GetField(
            "_rawInputActive",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(field);
        field!.SetValue(app, value);
    }

    private static void SetInputCapabilities(TerminaApplication app, TerminalInputCapabilities value)
    {
        var field = typeof(TerminaApplication).GetField(
            "_inputCapabilities",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(field);
        field!.SetValue(app, value);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.Fail("Condition was not met within the expected time.");
    }

    private static List<IInputEvent> FeedString(EscapeSequenceParser parser, string s)
    {
        var all = new List<IInputEvent>();
        foreach (var c in s)
        {
            var info = c == '\x1b'
                ? new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)
                : new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false);
            all.AddRange(parser.Process(info));
        }

        return all;
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IEnumerable<IInputSource>))
                return Array.Empty<IInputSource>();

            return null;
        }
    }

    private sealed class CompletedInputSource : IInputSource
    {
        public Task RunAsync(
            System.Threading.Channels.ChannelWriter<object> writer,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CancellationAwareInputSource : IInputSource
    {
        public bool CancellationObserved { get; private set; }

        public async Task RunAsync(
            System.Threading.Channels.ChannelWriter<object> writer,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() =>
            {
                CancellationObserved = true;
                completion.TrySetCanceled(cancellationToken);
            });
            await completion.Task;
        }
    }

    private sealed class TestPage : ReactivePage<TestViewModel>
    {
        public override ILayoutNode BuildLayout() => new TextNode("test");
    }

    private sealed class ThrowingPage : ReactivePage<TestViewModel>
    {
        public override ILayoutNode BuildLayout() => new ThrowingLayoutNode();
    }

    private sealed class ThrowingLayoutNode : LayoutNode
    {
        public override Size Measure(Size available) => new(1, 1);

        public override void Render(IRenderContext context, Rect bounds) =>
            throw new InvalidOperationException("Render failure");
    }

    private sealed class TestViewModel : ReactiveViewModel
    {
    }
}
