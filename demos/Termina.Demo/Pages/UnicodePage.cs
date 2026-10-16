// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Extensions;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Demo.Pages;

/// <summary>
/// Page for the Unicode/CJK rendering demo.
/// </summary>
public class UnicodePage : ReactivePage<CjkDemoViewModel>
{
    private StreamingTextNode _chatHistory = null!;
    private TextInputNode _textInput = null!;

    protected override void OnBound()
    {
        _chatHistory = StreamingTextNode.Create()
            .WithPrefix("  ", Color.Gray)
            .WithScrollbar();

        _chatHistory.AppendLine("You: 你是谁", foreground: Color.Cyan, decoration: TextDecoration.Bold);
        _chatHistory.AppendLine("NetClaw: 我是 NetClaw 你个人AI助手，可帮搜信息、代写代码、审查项目。", foreground: Color.White);
        _chatHistory.AppendLine("日本語: こんにちは世界 カタカナ", foreground: Color.Yellow);
        _chatHistory.AppendLine("한국어: 안녕하세요 세계", foreground: Color.Green);

        _textInput = new TextInputNode()
            .WithPlaceholder("ASCII input here; CJK smoke text is rendered above")
            .WithForeground(Color.Cyan);

        _textInput.Submitted
            .Subscribe(ViewModel.OnInputSubmitted)
            .DisposeWith(Subscriptions);
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _textInput.Clear();
    }

    public override ILayoutNode BuildLayout()
    {
        var samples = Layouts.Vertical()
            .WithChild(new TextNode("Static CJK: 你好世界 こんにちは 하 world").Height(1))
            .WithChild(new TextNode("Mixed width: 我是NetClaw你个人AI助手可帮搜信息代写代码审查项目").Height(1))
            .WithChild(
                ViewModel.InputText
                    .Select<string, ILayoutNode>(text => new TextNode($"Submitted: {text}").WithForeground(Color.Yellow))
                    .AsLayout(RenderFrameProvider)
                    .Height(1))
            .WithChild(
                ViewModel.StatusMessage
                    .Select<string, ILayoutNode>(msg => new TextNode(msg).WithForeground(Color.White))
                    .AsLayout(RenderFrameProvider)
                    .Height(1));

        return Layouts.Vertical()
            .WithChild(
                new PanelNode()
                    .WithTitle("Unicode / CJK Smoke Demo")
                    .WithBorder(BorderStyle.Double)
                    .WithBorderColor(Color.Magenta)
                    .WithContent(samples)
                    .Height(7))
            .WithChild(
                new PanelNode()
                    .WithTitle("NetClaw-style Chat History")
                    .WithTitleColor(Color.Yellow)
                    .WithBorder(BorderStyle.Rounded)
                    .WithBorderColor(Color.Gray)
                    .WithContent(_chatHistory.Fill())
                    .Fill())
            .WithChild(
                new PanelNode()
                    .WithTitle("Input")
                    .WithTitleColor(Color.Cyan)
                    .WithBorder(BorderStyle.Rounded)
                    .WithBorderColor(Color.Cyan)
                    .WithContent(_textInput)
                    .Height(3))
            .WithChild(
                new TextNode("[C] Counter [T] Todos [Q] Quit")
                    .WithForeground(Color.BrightBlack)
                    .Height(1));
    }
}
