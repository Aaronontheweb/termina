// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Reflection;
using R3;
using Termina.Input;
using Termina.Layout;
using Termina.Platform;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;
using Termina.Tests.Layout;

namespace Termina.Tests.Input;

/// <summary>
/// Feeds raw kitty keyboard protocol bytes through <see cref="TerminalInputPipeline"/> and the
/// application input routing into text controls, as a terminal with the report associated text
/// flag sends them.
/// </summary>
public sealed class KittyAssociatedTextInputPathTests
{
    private const string Draft = "draft ";

    public static readonly TheoryData<string, string> AssociatedTextCases = new()
    {
        // Two code points in one key event.
        { "\x1b[0;1;72:105u", "Hi" },
        // A base letter and a combining accent in one key event.
        { "\x1b[101;1;101:769u", "é" },
        // A code point outside the Basic Multilingual Plane, which is a surrogate pair in UTF-16.
        { "\x1b[0;1;128512u", "\U0001F600" },
        // A text-only event with an empty modifier field.
        { "\x1b[0;;229u", "å" },
        // A shifted letter whose associated text is the uppercase letter.
        { "\x1b[97;2;65u", "A" },
        // An empty text field, which means the base key gives the text.
        { "\x1b[97;2;u", "a" },
        // A sequence longer than the old 32-character buffer limit.
        { "\x1b[0;1;97:98:99:100:101:102:103:104:105u", "abcdefghi" },
    };

    public static readonly TheoryData<string> RejectedTextCases = new()
    {
        // A control code point in the text field.
        "\x1b[97;1;27u",
        // A non-numeric text field.
        "\x1b[97;2;?u",
        // A surrogate code point in the text field.
        "\x1b[97;1;55296u",
        // A sequence longer than the buffer limit.
        "\x1b[0;1;" + string.Join(':', Enumerable.Repeat("97", 400)) + "u",
    };

    [Theory]
    [MemberData(nameof(AssociatedTextCases))]
    public void TextInputNode_InsertsFullAssociatedText(string input, string expected)
    {
        using var textInput = new TextInputNode();

        var text = TypeIntoFocusedNode(textInput, input);

        Assert.Equal(Draft + expected, text);
        Assert.Equal((Draft + expected).Length, textInput.CursorPosition);
    }

    [Theory]
    [MemberData(nameof(AssociatedTextCases))]
    public void TextAreaNode_InsertsFullAssociatedText(string input, string expected)
    {
        using var area = new TextAreaNode();

        var text = TypeIntoFocusedNode(area, input);

        Assert.Equal(Draft + expected, text);
        Assert.Equal((Draft + expected).Length, area.CursorPosition);
    }

    [Theory]
    [MemberData(nameof(AssociatedTextCases))]
    public void RenderingTextInput_InsertsFullAssociatedText(string input, string expected)
    {
        using var renderingInput = new TextInput { IsFocused = true, Text = Draft };
        renderingInput.CursorPosition = Draft.Length;

        foreach (var keyPressed in FeedRawInput(input).OfType<KeyPressed>())
            renderingInput.HandleKey(keyPressed);

        Assert.Equal(Draft + expected, renderingInput.Text);
        Assert.Equal((Draft + expected).Length, renderingInput.CursorPosition);
    }

    [Theory]
    [MemberData(nameof(RejectedTextCases))]
    public void TextInputNode_KeepsDraftForRejectedText(string input)
    {
        using var textInput = new TextInputNode();

        var text = TypeIntoFocusedNode(textInput, input);

        Assert.Equal(Draft, text);
    }

    [Fact]
    public void FilePickerNode_StartsFilterWithFullAssociatedText()
    {
        var fileSystem = new FakeFileSystemProvider()
            .AddDirectory("/docs",
                new FileSystemEntry("Ha.txt", "/docs/Ha.txt", false),
                new FileSystemEntry("Hi.txt", "/docs/Hi.txt", false));
        using var picker = new FilePickerNode("/docs").WithFileSystemProvider(fileSystem);
        picker.OnFocused();
        IReadOnlyList<string>? selected = null;
        picker.SelectionConfirmed.Subscribe(paths => selected = paths);

        foreach (var keyPressed in FeedRawInput("\x1b[0;1;72:105u\r").OfType<KeyPressed>())
            picker.HandleInput(keyPressed);

        Assert.NotNull(selected);
        Assert.Equal("/docs/Hi.txt", selected[0]);
    }

    [Fact]
    public void ModalNode_ForwardsFullAssociatedTextToContent()
    {
        using var textInput = new TextInputNode();
        using var modal = new ModalNode().WithContent(textInput);

        var text = TypeIntoFocusedNode(textInput, "\x1b[0;1;72:105u", modal);

        Assert.Equal(Draft + "Hi", text);
    }

    private static string TypeIntoFocusedNode(TextInputBaseNode node, string input, ILayoutNode? root = null)
    {
        node.Text = Draft;
        node.MoveCursorToEnd();
        TextControlPage.Root = root ?? node;

        var app = new TerminaApplication(new VirtualTerminal(40, 10), new TestServiceProvider());
        try
        {
            app.RegisterRoute<TextControlPage, TextControlViewModel>("/text");
            app.NavigateTo("/text");
            InvokePrivate(app, "RenderCurrentPage");
            Assert.True(node.HasFocus);

            foreach (var inputEvent in FeedRawInput(input))
                InvokePrivate(app, "ProcessEvent", inputEvent);

            return node.Text;
        }
        finally
        {
            TextControlPage.Root = new EmptyNode();
            app.Dispose();
        }
    }

    private static List<IInputEvent> FeedRawInput(string input)
    {
        var pipeline = new TerminalInputPipeline(kittyReportAllKeysVisible: true);
        var events = new List<IInputEvent>();
        foreach (var b in input.Select(c => (byte)c))
            events.AddRange(pipeline.Process(RawByteKeyMapper.ByteToKeyInfo(b)));

        Assert.False(pipeline.IsBufferingEscape);
        return events;
    }

    private static void InvokePrivate(TerminaApplication app, string name, params object[] args)
    {
        var method = typeof(TerminaApplication).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        method!.Invoke(app, args);
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

    private sealed class TextControlPage : ReactivePage<TextControlViewModel>
    {
        public TextControlPage()
        {
            FocusPolicy = FocusPolicy.FirstFocusable;
        }

        public static ILayoutNode Root { get; set; } = new EmptyNode();

        public override ILayoutNode BuildLayout() => Root;
    }

    private sealed class TextControlViewModel : ReactiveViewModel
    {
    }
}
