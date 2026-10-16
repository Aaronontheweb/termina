// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Layout;
using Termina.Notifications;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Notifications;

public class ToastOverlayNodeTests
{
    [Fact]
    public void Render_UsesTopLeftPlacement_WhenConfigured()
    {
        var toastService = new TestToastService();
        var node = new ToastOverlayNode(toastService);
        var terminal = new VirtualTerminal(40, 8);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 8);

        toastService.Show("Copied", new ToastOptions(Position: ToastPosition.TopLeft));
        node.Render(context, new Rect(0, 0, 40, 8));

        Assert.Equal('╭', terminal.GetChar(1, 1));
    }

    [Fact]
    public void Render_UsesBottomCenterPlacement_WhenConfigured()
    {
        var toastService = new TestToastService();
        var node = new ToastOverlayNode(toastService);
        var terminal = new VirtualTerminal(40, 8);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 8);

        toastService.Show("Copied", new ToastOptions(Position: ToastPosition.BottomCenter));
        node.Render(context, new Rect(0, 0, 40, 8));

        var expectedX = (40 - 12) / 2;
        Assert.Equal('╭', terminal.GetChar(expectedX, 4));
    }

    [Fact]
    public void Render_CjkMessage_SizesPanelByDisplayColumns()
    {
        var toastService = new TestToastService();
        var node = new ToastOverlayNode(toastService);
        var terminal = new VirtualTerminal(40, 8);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 8);

        toastService.Show("已复制", new ToastOptions(Position: ToastPosition.TopLeft));
        node.Render(context, new Rect(0, 0, 40, 8));

        Assert.Equal('已', terminal.GetChar(5, 2));
        Assert.Equal('制', terminal.GetChar(9, 2));
        Assert.Equal('│', terminal.GetChar(12, 2));
    }

    private sealed class TestToastService : IToastService
    {
        private readonly Subject<ToastMessage?> _subject = new();

        public Observable<ToastMessage?> CurrentToast => _subject;

        public void Show(string message, ToastOptions? options = null)
        {
            _subject.OnNext(new ToastMessage(message, options?.Position ?? ToastPosition.BottomRight));
        }
    }
}
