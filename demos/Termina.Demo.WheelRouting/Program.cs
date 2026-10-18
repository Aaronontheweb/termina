// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.
//
// Termina 0.17.0 pointer-aware wheel routing: two log panes side by side.
//
// Interactive: run it in a mouse-reporting terminal, hover the wheel over a
// pane, scroll — only that pane moves. The framework hit-tests each
// MouseScrollEvent against the measured bounds of the scrollable under the
// pointer (TerminaApplication.RouteMouseScroll).
//
// Scripted (--test): a VirtualInputSource posts the same positioned wheel
// events that VHS cannot synthesize (VHS types keystrokes; it has no mouse).
// The ticks travel through the real production path — MouseEvent(Scroll) →
// MouseScrollEvent{X,Y} → hit-test — so the recorded GIF exercises the actual
// router, not a staged shortcut. The ruler + status line visualize where the
// pointer is so the routing decision is legible in a silent GIF.
//
// Run:
//   dotnet run --project demos/Termina.Demo.WheelRouting            # real mouse
//   dotnet run --project demos/Termina.Demo.WheelRouting -- --test  # scripted

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using R3;
using Termina.Extensions;
using Termina.Hosting;
using Termina.Input;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

var testMode = args.Contains("--test");

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

VirtualInputSource? scriptedInput = null;
if (testMode)
{
    scriptedInput = new VirtualInputSource();
    builder.Services.AddTerminaVirtualInput(scriptedInput);
}

builder.Services.AddTermina("/", termina =>
{
    termina.RegisterRoute<WheelRoutingPage, WheelRoutingViewModel>("/");
});

var host = builder.Build();

if (testMode && scriptedInput is not null)
{
    _ = Task.Run(async () =>
    {
        var vm = await WheelRoutingViewModel.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Let the tape reach "Show" and the first frame settle.
        await Task.Delay(2500);

        // Pointer targets are fractions of the size the framework is actually
        // rendering against — never a separate Console read from the script
        // thread, which can disagree with the app's terminal.
        var leftX = (int)(vm.Cols * 0.27);
        var rightX = (int)(vm.Cols * 0.73);
        var pointerY = vm.Rows / 2;

        // 1. Pointer parks over the LEFT pane: wheel up reads older log lines.
        vm.MovePointer(leftX, pointerY);
        await Task.Delay(900);
        for (var i = 0; i < 5; i++)
        {
            scriptedInput.EnqueueScroll(leftX, pointerY, up: true);
            vm.NoteTick(left: true, up: true);
            await Task.Delay(320);
        }
        await Task.Delay(1000);

        // 2. Glide the pointer to the RIGHT pane, then scroll there.
        await vm.GlideAsync(leftX, rightX, pointerY, 8);
        await Task.Delay(400);
        for (var i = 0; i < 3; i++)
        {
            scriptedInput.EnqueueScroll(rightX, pointerY, up: true);
            vm.NoteTick(left: false, up: true);
            await Task.Delay(320);
        }
        await Task.Delay(800);

        // 3. Still over the RIGHT pane: wheel down returns newest lines.
        for (var i = 0; i < 2; i++)
        {
            scriptedInput.EnqueueScroll(rightX, pointerY, up: false);
            vm.NoteTick(left: false, up: false);
            await Task.Delay(320);
        }
        await Task.Delay(1000);

        // 4. Pointer home to the LEFT, one tick: routing follows the pointer,
        //    the right pane stays exactly where its last tick left it.
        await vm.GlideAsync(rightX, leftX, pointerY, 8);
        await Task.Delay(500);
        scriptedInput.EnqueueScroll(leftX, pointerY, up: true);
        vm.NoteTick(left: true, up: true);

        // Long closing hold: the tape stops before the scripted exit so the
        // GIF loops on the settled two-pane comparison, never a blank shell.
        // Sized so the alt-buffer restore can't race the recording end.
        await Task.Delay(10000);

        scriptedInput.EnqueueKey(ConsoleKey.Q, control: true);
        scriptedInput.Complete();
    });
}

await host.RunAsync();

internal sealed class WheelRoutingPage : ReactivePage<WheelRoutingViewModel>
{
    public override bool HandlePageInput(ConsoleKeyInfo keyInfo)
    {
        if (keyInfo.Key == ConsoleKey.Q && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            Shutdown();
            return true;
        }

        return base.HandlePageInput(keyInfo);
    }

    public override ILayoutNode BuildLayout()
    {
        return Layouts.Vertical()
            .WithChild(new TextNode("Pointer-aware wheel routing — each tick goes to the scroll region under the pointer")
                .WithForeground(Color.Cyan)
                .Bold()
                .Height(1))
            .WithChild(ViewModel.PointerLine
                .Select<string, ILayoutNode>(r => new TextNode(r).WithForeground(Color.Yellow).NoWrap().Height(1))
                .AsLayout(RenderFrameProvider))
            .WithChild(ViewModel.StatusLine
                .Select<string, ILayoutNode>(s => new TextNode(s).WithForeground(Color.White).NoWrap().Height(1))
                .AsLayout(RenderFrameProvider))
            .WithChild(
                Layouts.Horizontal()
                    .WithChild(
                        new PanelNode()
                            .WithTitle("api-gateway · access.log")
                            .WithTitleColor(Color.Cyan)
                            .WithBorder(BorderStyle.Rounded)
                            .WithBorderColor(Color.Cyan)
                            .WithContent(ViewModel.Left.Fill())
                            .Fill(1))
                    .WithChild(
                        new PanelNode()
                            .WithTitle("payments · ledger.log")
                            .WithTitleColor(Color.Magenta)
                            .WithBorder(BorderStyle.Rounded)
                            .WithBorderColor(Color.Magenta)
                            .WithContent(ViewModel.Right.Fill())
                            .Fill(1))
                    .Fill())
            .WithChild(new TextNode("Hover a pane · scroll the wheel · only the pane under the pointer moves   Ctrl+Q quit")
                .WithForeground(Color.BrightBlack)
                .Height(1));
    }
}

internal sealed class WheelRoutingViewModel : ReactiveViewModel
{
    /// <summary>Signals the --test script that the page is live and panes are populated.</summary>
    public static readonly TaskCompletionSource<WheelRoutingViewModel> Ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IAnsiTerminal _terminal;
    private int _pointerX = -1;
    private int _pointerY;
    private int _leftUp;
    private int _leftDown;
    private int _rightUp;
    private int _rightDown;

    public WheelRoutingViewModel(IAnsiTerminal terminal)
    {
        _terminal = terminal;

        // Deterministic content — the GIF must not depend on wall-clock time.
        for (var i = 1; i <= 120; i++)
        {
            var ms = 3 + (i * 7) % 40;
            var warn = i % 17 == 0;
            Left.AppendLine(
                $"{(warn ? "WARN" : "INFO")} GET /v1/orders/{i:D4} 200 {ms}ms",
                warn ? Color.Yellow : Color.White);

            var cents = (i * 137) % 9973;
            Right.AppendLine(
                $"INFO charge {i:D4} captured ${cents / 100}.{cents % 100:D2} ok",
                Color.White);
        }
    }

    /// <summary>Columns the framework is actually rendering against.</summary>
    public int Cols => Math.Max(_terminal.Width, 60);

    /// <summary>Rows the framework is actually rendering against.</summary>
    public int Rows => Math.Max(_terminal.Height, 16);

    public ReactiveProperty<string> PointerLine { get; } = new(string.Empty);
    public ReactiveProperty<string> StatusLine { get; } = new("starting…");

    public StreamingTextNode Left { get; } =
        StreamingTextNode.Create().WithPrefix("  ", Color.BrightBlack).WithScrollbar();

    public StreamingTextNode Right { get; } =
        StreamingTextNode.Create().WithPrefix("  ", Color.BrightBlack).WithScrollbar();

    public override void OnActivated()
    {
        base.OnActivated();

        PointerLine.Value = Ruler(Cols, _pointerX);

        // Real wheel ticks are routed by the framework; mirror the ones that
        // arrive so the ruler and counters tell the same story in interactive
        // mode as the scripted --test run does in the GIF.
        Input.OfType<IInputEvent, MouseScrollEvent>()
            .Subscribe(s =>
            {
                if (s.X is { } x)
                {
                    MovePointer(x, s.Y ?? _pointerY);
                    NoteTick(left: x < Cols / 2, up: s.Delta > 0);
                }
            })
            .DisposeWith(Subscriptions);

        Ready.TrySetResult(this);
    }

    public void MovePointer(int x, int y)
    {
        Post(() =>
        {
            _pointerX = x;
            _pointerY = y;
            PointerLine.Value = Ruler(Cols, x);
            StatusLine.Value = Status();
        });
    }

    public void NoteTick(bool left, bool up)
    {
        Post(() =>
        {
            if (left)
            {
                if (up) _leftUp++; else _leftDown++;
            }
            else
            {
                if (up) _rightUp++; else _rightDown++;
            }

            StatusLine.Value = Status();
        });
    }

    /// <summary>Animates the ruler between two columns, pausing between steps.</summary>
    public Task GlideAsync(int fromX, int toX, int y, int steps)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            for (var i = 1; i <= steps; i++)
            {
                var x = fromX + (toX - fromX) * i / steps;
                MovePointer(x, y);
                await Task.Delay(60);
            }

            tcs.TrySetResult();
        });
        return tcs.Task;
    }

    private string Status()
    {
        var line = $"pointer ({_pointerX},{_pointerY})  →  {Pane()} pane";
        if (_leftUp + _leftDown + _rightUp + _rightDown > 0)
        {
            line += $"   wheel ticks: LEFT ↑{_leftUp} ↓{_leftDown}   RIGHT ↑{_rightUp} ↓{_rightDown}";
        }

        return line;
    }

    private string Pane() => _pointerX < Cols / 2 ? "LEFT" : "RIGHT";

    private string Ruler(int cols, int pointerX)
    {
        var chars = new char[cols];
        Array.Fill(chars, '·');
        chars[cols / 2] = '│';
        if (pointerX >= 0 && pointerX < cols)
        {
            chars[pointerX] = '▲';
        }

        return new string(chars);
    }

    public override void Dispose()
    {
        PointerLine.Dispose();
        StatusLine.Dispose();
        Left.Dispose();
        Right.Dispose();
        base.Dispose();
    }
}
