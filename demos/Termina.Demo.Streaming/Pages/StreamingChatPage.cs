// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Components.Streaming;
using Termina.Demo.Streaming.Actors;
using Termina.Extensions;
using Termina.Input;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Demo.Streaming.Pages;

/// <summary>
/// Demo page showing streaming text components with LLM simulation.
/// Handles all UI concerns including layout nodes, focus, and input routing.
/// </summary>
public class StreamingChatPage : ReactivePage<StreamingChatViewModel>
{
    // Layout nodes owned by the Page
    private StreamingTextNode _chatHistory = null!;
    private TextInputNode _promptInput = null!;

    // Decision list for interactive choices
    private SelectionListNode<LlmMessages.DecisionChoice>? _decisionList;
    private readonly Subject<ILayoutNode?> _decisionListChanged = new();

    protected override void OnBound()
    {
        // Create layout nodes
        _chatHistory = StreamingTextNode.Create()
            .WithPrefix("  ", Color.Gray)
            .WithScrollbar();

        _promptInput = new TextInputNode()
            .WithPlaceholder("Enter your question...")
            .WithForeground(Color.Cyan)
            .WithHistory();

        // Subscribe to ViewModel chat output and update nodes
        ViewModel.ChatOutput
            .Subscribe(message =>
            {
                switch (message)
                {
                    case AppendText text:
                        if (text.IsNewLine)
                            _chatHistory.AppendLine(text.Text, text.Foreground, null, text.Decoration);
                        else
                            _chatHistory.Append(text.Text, text.Foreground, null, text.Decoration);
                        break;

                    case AppendTrackedSegment tracked:
                        _chatHistory.AppendTracked(tracked.Id, tracked.Segment);
                        break;

                    case RemoveTrackedSegment remove:
                        _chatHistory.Remove(remove.Id);
                        break;

                    case ReplaceTrackedSegment replace:
                        _chatHistory.Replace(replace.Id, replace.NewSegment, replace.KeepTracked);
                        break;

                    case ShowDecisionPoint decision:
                        ShowDecisionList(decision);
                        break;

                    case HideDecisionPoint:
                        HideDecisionList();
                        break;

                    default:
                        throw new ArgumentException($"Unknown message type: {message.GetType()}");
                }
            })
            .DisposeWith(Subscriptions);

        // Subscribe to prompt input submission
        _promptInput.Submitted
            .Subscribe(text =>
            {
                ViewModel.HandleSubmit(text);
                _promptInput.Clear();
            })
            .DisposeWith(Subscriptions);

        // Handle keyboard input - Page routes to interactive layout nodes
        ViewModel.Input.OfType<IInputEvent, KeyPressed>()
            .Subscribe(HandleKeyPress)
            .DisposeWith(Subscriptions);

        // Route mouse scroll events to chat history (when no focused IScrollable captures them)
        ViewModel.Input.OfType<IInputEvent, MouseScrollEvent>()
            .Subscribe(scroll =>
            {
                IScrollable scrollable = _chatHistory;
                if (scroll.Delta > 0)
                    scrollable.ScrollUp(3);
                else
                    scrollable.ScrollDown(3);
            })
            .DisposeWith(Subscriptions);

        // Emit welcome message
        ViewModel.EmitWelcomeMessage();
    }

    private void HandleKeyPress(KeyPressed key)
    {
        var keyInfo = key.KeyInfo;

        // Ctrl+Q always quits
        if (keyInfo.Key == ConsoleKey.Q && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            ViewModel.RequestShutdown();
            return;
        }

        // When decision list is visible, route input to it
        if (ViewModel.ShowDecisionList.Value && _decisionList != null)
        {
            // Escape cancels the decision
            if (keyInfo.Key == ConsoleKey.Escape)
            {
                ViewModel.HandleDecisionCancelled();
                return;
            }

            // Let the selection list handle the input
            _decisionList.HandleInput(key);
            return;
        }

        // Escape handling
        if (keyInfo.Key == ConsoleKey.Escape)
        {
            if (ViewModel.IsGenerating.Value)
            {
                ViewModel.CancelGeneration();
            }
            else
            {
                ViewModel.RequestShutdown();
            }

            return;
        }

        // Page Up/Down scroll chat history
        if (_chatHistory.HandleInput(keyInfo, viewportHeight: 10, viewportWidth: 80))
        {
            return;
        }

        // When not generating, let the text input handle keys (including history via Up/Down)
        if (!ViewModel.IsGenerating.Value)
        {
            _promptInput.HandleInput(key);
        }
    }

    private void ShowDecisionList(ShowDecisionPoint decision)
    {
        // Dispose any existing decision list
        _decisionList?.Dispose();

        // Create rich content for each choice - inline style, no panel
        _decisionList = new SelectionListNode<LlmMessages.DecisionChoice>(
                decision.Choices,
                choice => new SelectionItemContent()
                    .AddLine(
                        new StaticTextSegment(choice.Title, Color.White, decoration: TextDecoration.Bold),
                        new StaticTextSegment(" ", Color.Default),
                        new StaticTextSegment($"[{choice.Category}]", Color.BrightBlack))
                    .AddLine(
                        new StaticTextSegment("   " + choice.Description, Color.Gray)))
            .WithHighlightColors(Color.Black, Color.Cyan)
            .WithForeground(Color.White)
            .WithVisibleRows(8)
            .WithShowNumbers(true)
            .WithOtherOption("Something else...");

        // Subscribe to selection confirmation
        _decisionList.SelectionConfirmed
            .Subscribe(selected =>
            {
                if (selected.Count > 0)
                {
                    ViewModel.HandleDecisionSelection(selected[0].Title);
                }
            })
            .DisposeWith(Subscriptions);

        // Subscribe to "Something else..." custom input
        _decisionList.OtherSelected
            .Subscribe(customPrompt =>
            {
                _promptInput.AddHistory(customPrompt);
                ViewModel.HandleCustomPrompt(customPrompt);
            })
            .DisposeWith(Subscriptions);

        // Subscribe to cancellation
        _decisionList.Cancelled
            .Subscribe(_ => ViewModel.HandleDecisionCancelled())
            .DisposeWith(Subscriptions);

        // Focus the decision list
        _decisionList.OnFocused();

        // Signal layout change - just the list node itself, no panel wrapper
        _decisionListChanged.OnNext(_decisionList);
    }

    private void HideDecisionList()
    {
        _decisionList?.Dispose();
        _decisionList = null;
        _decisionListChanged.OnNext(null);
    }

    public override ILayoutNode BuildLayout()
    {
        return Layouts.Vertical()
            // Header
            .WithChild(
                new TextNode("🤖 Streaming Chat Demo - Simulated LLM with Akka.NET")
                    .WithForeground(Color.Cyan)
                    .Bold()
                    .Height(1))
            .WithChild(new EmptyNode().Height(1))
            // Chat history panel - fills available space
            .WithChild(
                new PanelNode()
                    .WithTitle("Chat History")
                    .WithTitleColor(Color.Yellow)
                    .WithBorder(BorderStyle.Rounded)
                    .WithBorderColor(Color.Gray)
                    .WithContent(_chatHistory.Fill())
                    .Fill())
            .WithChild(new EmptyNode().Height(1))
            // Decision list - appears between chat history and input when active
            .WithChild(
                _decisionListChanged
                    .Prepend((ILayoutNode?)null)
                    .Select(decisionList => decisionList == null
                        ? (ILayoutNode)new EmptyNode().Height(0)
                        : Layouts.Vertical()
                            .WithChild(new TextNode("  Choose an option:").WithForeground(Color.Yellow).Height(1))
                            .WithChild(decisionList)
                            .HeightAuto()) // Auto-size to content, don't compete with Fill() elements
                    .AsLayout(RenderFrameProvider))
            // Input panel - always visible
            .WithChild(
                new PanelNode()
                    .WithTitle("Your Prompt")
                    .WithTitleColor(Color.Cyan)
                    .WithBorder(BorderStyle.Rounded)
                    .WithBorderColor(Color.Cyan)
                    .WithContent(_promptInput)
                    .Height(3))
            // Status bar
            .WithChild(
                Observable.CombineLatest(
                        ViewModel.IsGenerating,
                        ViewModel.ShowDecisionList.Prepend(false),
                        (isGenerating, showDecision) => showDecision
                            ? "[↑/↓] Navigate [Enter] Select [1-4] Quick Select [Esc] Skip"
                            : isGenerating
                                ? "[Esc] Cancel  [PgUp/PgDn/Wheel] Scroll  [Ctrl+Q] Quit"
                                : "[Enter] Send  [Ctrl+Shift+V] Paste  [↑/↓] History  [PgUp/PgDn/Wheel] Scroll  [Esc] Clear/Quit  [Ctrl+Q] Quit")
                    .Select<string, ILayoutNode>(text => new TextNode(text).WithForeground(Color.BrightBlack).NoWrap())
                    .AsLayout(RenderFrameProvider)
                    .Height(1))
            .WithChild(
                ViewModel.StatusMessage
                    .Select<string, ILayoutNode>(msg => new TextNode(msg).WithForeground(Color.White))
                    .AsLayout(RenderFrameProvider)
                    .Height(1));
    }
}
