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
/// Page for the counter demo.
/// Demonstrates the new declarative layout API.
/// </summary>
public class CounterPage : ReactivePage<CounterViewModel>
{
    public override ILayoutNode BuildLayout()
    {
        return Layouts.Vertical()
            .WithChild(
                new PanelNode()
                    .WithTitle("Counter Demo")
                    .WithBorder(BorderStyle.Double)
                    .WithBorderColor(Color.Blue)
                    .WithContent(
                        ViewModel.Count
                            .Select<int, ILayoutNode>(count => new TextNode($"\n  Count: {count}\n")
                                .WithForeground(Color.Cyan)
                                .Bold())
                            .AsLayout())
                    .Fill())
            .WithChild(
                new TextNode("[↑] Increment [↓] Decrement [R] Reset [T] Todos [U] Unicode [Q] Quit")
                    .WithForeground(Color.BrightBlack)
                    .Height(1))
            .WithChild(
                ViewModel.StatusMessage
                    .Select<string, ILayoutNode>(msg => new TextNode(msg).WithForeground(Color.White))
                    .AsLayout()
                    .Height(1))
            .WithChild(
                new TextNode($"Trace log: {ViewModel.TraceFilePath}")
                    .WithForeground(Color.DarkGray)
                    .Height(1));
    }
}
