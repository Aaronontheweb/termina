// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using R3;
using Termina.Diagnostics;
using Termina.Extensions;
using Termina.Hosting;
using Termina.Layout;
using Termina.Reactive;
using Termina.Terminal;

var builder = Host.CreateApplicationBuilder(args);

var tracePath = Environment.GetEnvironmentVariable("TERMINA_INLINE_TRACE");
if (!string.IsNullOrWhiteSpace(tracePath))
{
    builder.Services.AddTerminaFileTracing(
        tracePath,
        TerminaTraceCategory.All,
        TerminaTraceLevel.Trace);
}

builder.Services.AddTermina("/", termina =>
{
    termina.ConfigureRuntime(options =>
    {
        options.PresentationMode = TerminalPresentationMode.Inline;
        options.ScrollInputMode = ScrollInputMode.NativeTerminal;
        options.PreferRawInput = true;
        options.KittyKeyboardMode = KittyKeyboardMode.ReportAllKeysPlusDisambiguate;
    });

    termina.RegisterRoute<InlineDemoPage, InlineDemoViewModel>("/");
});

await builder.Build().RunAsync();

internal sealed class InlineDemoPage : ReactivePage<InlineDemoViewModel>
{
    public override bool HandlePageInput(ConsoleKeyInfo keyInfo)
    {
        if (keyInfo.Key == ConsoleKey.Q && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            Shutdown();
            return true;
        }

        if (keyInfo.Key == ConsoleKey.Enter)
        {
            ViewModel.CommitNextBlock();
            return true;
        }

        if (keyInfo.Key == ConsoleKey.R)
        {
            ViewModel.ChangeLiveRegion();
            return true;
        }

        return base.HandlePageInput(keyInfo);
    }

    public override ILayoutNode BuildLayout()
    {
        return Layouts.Vertical()
            .WithChild(new TextNode("Termina inline-mode prototype").WithForeground(Color.Cyan).Bold().Height(1))
            .WithChild(
                ViewModel.LiveText
                    .Select<string, ILayoutNode>(text => new TextNode(text).WithForeground(Color.Yellow).Height(1))
                    .AsLayout(RenderFrameProvider))
            .WithChild(new TextNode("Enter: commit a stable block   R: change live content   Ctrl+Q: quit")
                .WithForeground(Color.Gray)
                .Height(1))
            .WithChild(new TextNode("Use terminal scrollback and native selection after several commits.")
                .WithForeground(Color.BrightBlack)
                .Height(1));
    }
}

internal sealed class InlineDemoViewModel(IInlineOutput inlineOutput) : ReactiveViewModel
{
    private int _stableBlockCount;
    private int _liveRevision;

    public ReactiveProperty<string> LiveText { get; } = new("Live region revision 0. Stable blocks: 0.");

    public void CommitNextBlock()
    {
        _ = CommitNextBlockAsync();
    }

    public void ChangeLiveRegion()
    {
        _liveRevision++;
        LiveText.Value = $"Live region revision {_liveRevision}. Stable blocks: {_stableBlockCount}.";
    }

    private async Task CommitNextBlockAsync()
    {
        var nextBlock = _stableBlockCount + 1;
        try
        {
            await inlineOutput.CommitAsync(
                new TextNode($"Stable block {nextBlock}: settled output remains in primary scrollback.")
                    .WithForeground(Color.Green),
                CancellationToken.None);

            await InvokeAsync(() =>
            {
                _stableBlockCount = nextBlock;
                LiveText.Value = $"Live region revision {_liveRevision}. Stable blocks: {_stableBlockCount}.";
            });
        }
        catch (Exception ex)
        {
            await InvokeAsync(() => LiveText.Value = $"Inline commit failed: {ex.Message}");
        }
    }

    public override void Dispose()
    {
        LiveText.Dispose();
        base.Dispose();
    }
}
