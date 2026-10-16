// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using R3;
using Microsoft.Extensions.Time.Testing;
using Termina.Clipboard;
using Termina.Layout;
using Termina.Notifications;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Tests.Layout;

public class CopyableTextNodeTests
{
    [Fact]
    public void Enter_CopiesFullContent()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "https://example.com");

        var handled = node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.True(handled);
        Assert.Equal("https://example.com", clipboard.LastCopiedText);
    }

    [Fact]
    public void Enter_CopiesCompleteSemanticContentWithoutDisplayDecoration()
    {
        const string display = "\u001b[32m✓ shell_execute → output...\u001b[0m";
        const string semantic = "shell_execute\nExit code: 0\ncomplete output";
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, display)
            .WithSemanticContent(semantic);

        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        var copiedText = Assert.IsType<string>(clipboard.LastCopiedText);
        Assert.Equal(semantic, copiedText);
        Assert.DoesNotContain('\u001b', copiedText);
        Assert.DoesNotContain("✓", copiedText);
    }

    [Fact]
    public void TryCopy_WhenClipboardFails_ReturnsFalseAndKeepsSelection()
    {
        var clipboard = new TestClipboardService { Succeeds = false };
        var node = new CopyableTextNode(clipboard, "abcd");
        node.OnFocused();
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));

        var success = node.TryCopy();

        Assert.False(success);
        Assert.True(node.HasSelection);
        Assert.Equal("ab", node.SelectedText);
        Assert.Equal("ab", clipboard.LastCopiedText);
    }

    [Fact]
    public void NonEnterKey_IsNotHandled()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "abc");

        var handled = node.HandleInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));

        Assert.False(handled);
        Assert.Null(clipboard.LastCopiedText);
    }

    [Fact]
    public void ShiftArrow_SelectsText_AndEnterCopiesSelection()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "abcd");

        node.OnFocused();
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));
        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal("ab", clipboard.LastCopiedText);
    }

    [Fact]
    public void CtrlA_SelectsAll_AndCtrlCCopiesSelection()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "select all");

        node.OnFocused();
        node.HandleInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, true));
        node.HandleInput(new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true));

        Assert.Equal("select all", clipboard.LastCopiedText);
    }

    [Fact]
    public void Escape_ClearsSelection()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "abcd");

        node.OnFocused();
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));

        var handled = node.HandleInput(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));

        Assert.True(handled);
        Assert.False(node.HasSelection);
    }

    [Fact]
    public void FocusChanges_RaiseInvalidation()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "abc");
        var invalidations = 0;
        using var subscription = node.Invalidated.Subscribe(_ => invalidations++);

        node.OnFocused();
        node.OnBlurred();

        Assert.Equal(2, invalidations);
    }

    [Fact]
    public void Render_WhenFocused_ShowsHint()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "copy me");
        var terminal = new VirtualTerminal(40, 4);
        var context = new RegionRenderContext(terminal, 0, 0, 40, 4);

        node.OnFocused();
        node.Render(context, new Rect(0, 0, 40, 4));

        Assert.Contains("copy me", terminal.ToString());
        Assert.Contains("Press Enter to copy", terminal.ToString());
    }

    [Fact]
    public void Render_WithSelection_HighlightsSelectedCharacters()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "abcd");
        var terminal = new VirtualTerminal(10, 3);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 3);

        node.OnFocused();
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));
        node.Render(context, new Rect(0, 0, 10, 3));

        Assert.Equal(Color.BrightYellow, terminal.GetBackground(0, 0));
        Assert.Equal(Color.BrightYellow, terminal.GetBackground(1, 0));
        Assert.NotEqual(Color.BrightYellow, terminal.GetBackground(2, 0));
    }

    [Fact]
    public void Render_CjkContent_WrapsAndPositionsByDisplayColumns()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "你好世界");
        var terminal = new VirtualTerminal(10, 3);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 3);

        node.Render(context, new Rect(0, 0, 4, 3));

        Assert.Equal("你好", terminal.GetLine(0));
        Assert.Equal("世界", terminal.GetLine(1));
        Assert.Equal('你', terminal.GetChar(0, 0));
        Assert.Equal('好', terminal.GetChar(2, 0));
    }

    [Fact]
    public void CopySelection_EmojiSelection_UsesWholeTextElement()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "😀A");

        node.OnFocused();
        node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false));
        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal("😀", clipboard.LastCopiedText);
    }

    [Fact]
    public void CustomCopyBinding_TriggersCopy()
    {
        var clipboard = new TestClipboardService();
        var node = new CopyableTextNode(clipboard, "hello")
            .WithCopyBindings(new CopyKeyBinding(ConsoleKey.F5));

        var handled = node.HandleInput(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false));

        Assert.True(handled);
        Assert.Equal("hello", clipboard.LastCopiedText);
    }

    [Fact]
    public void InlineIndicator_RendersAfterSuccessfulCopy()
    {
        var clipboard = new TestClipboardService();
        var timeProvider = new FakeTimeProvider();
        var node = new CopyableTextNode(clipboard, "hello")
            .WithFeedbackMode(CopyFeedbackMode.InlineIndicator)
            .WithInlineIndicator("OK");
        LayoutRuntimeContextInjector.Apply(
            node,
            new LayoutRuntimeContext(new TestFrameProvider(), timeProvider, () => { }));
        var terminal = new VirtualTerminal(10, 3);
        var context = new RegionRenderContext(terminal, 0, 0, 10, 3);

        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        node.Render(context, new Rect(0, 0, 10, 3));

        Assert.Equal('O', terminal.GetChar(8, 0));
        Assert.Equal('K', terminal.GetChar(9, 0));
    }

    [Fact]
    public void ToastFeedback_UsesConfiguredPlacement()
    {
        var clipboard = new TestClipboardService();
        var toastService = new TestToastService();
        var node = new CopyableTextNode(clipboard, "hello", toastService)
            .WithToastPosition(ToastPosition.TopLeft)
            .WithFeedbackMode(CopyFeedbackMode.Toast);

        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal(ToastPosition.TopLeft, toastService.LastToast?.Position);
    }

    private sealed class TestClipboardService : IClipboardService
    {
        public string? LastCopiedText { get; private set; }

        public bool Succeeds { get; init; } = true;

        public bool Copy(string text)
        {
            LastCopiedText = text;
            return Succeeds;
        }
    }

    private sealed class TestToastService : IToastService
    {
        private readonly Subject<ToastMessage?> _subject = new();

        public ToastMessage? LastToast { get; private set; }

        public Observable<ToastMessage?> CurrentToast => _subject;

        public void Show(string message, ToastOptions? options = null)
        {
            LastToast = new ToastMessage(message, options?.Position ?? ToastPosition.BottomRight);
            _subject.OnNext(LastToast);
        }
    }

    private sealed class TestFrameProvider : FrameProvider
    {
        public override long GetFrameCount() => 0;

        public override void Register(IFrameRunnerWorkItem callback)
        {
        }
    }
}
