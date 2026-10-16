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
/// Page for the todo list demo.
/// Handles all UI concerns including layout nodes and focus management.
/// Reacts to ViewModel state changes.
/// </summary>
public class TodoListPage : ReactivePage<TodoListViewModel>
{
    // Layout nodes owned by the Page
    private ModalNode _addModal = null!;
    private ModalNode _priorityModal = null!;
    private SelectionListNode<string> _priorityList = null!;
    private TextInputNode _textInput = null!;

    protected override void OnBound()
    {
        // Layout nodes will be created in BuildLayout() where they're used
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();

        // Subscribe to text input submission
        _textInput.Submitted
            .Subscribe(text => ViewModel.OnTextInputSubmitted(text))
            .DisposeWith(Subscriptions);

        // Subscribe to priority selection events
        _priorityList.SelectionConfirmed
            .Subscribe(selected =>
            {
                var priority = selected.FirstOrDefault() ?? "Normal";
                ViewModel.OnPrioritySelected(priority);
            })
            .DisposeWith(Subscriptions);

        _priorityList.OtherSelected
            .Subscribe(customPriority => ViewModel.OnPrioritySelected(customPriority))
            .DisposeWith(Subscriptions);

        _priorityList.Cancelled
            .Subscribe(_ => ViewModel.OnPriorityCancelled())
            .DisposeWith(Subscriptions);

        _addModal.Dismissed
            .Subscribe(_ => ViewModel.OnAddModalDismissed())
            .DisposeWith(Subscriptions);

        _priorityModal.Dismissed
            .Subscribe(_ => ViewModel.OnPriorityModalDismissed())
            .DisposeWith(Subscriptions);

        // React to state changes for focus management
        // When IsAddingItem becomes true (and not showing priority), focus add modal
        ViewModel.IsAddingItem
            .CombineLatest(
                ViewModel.ShowPriorityModal,
                (adding, showPriority) => (adding, showPriority))
            .DistinctUntilChanged()
            .Subscribe(state =>
            {
                if (state.adding && !state.showPriority)
                {
                    _textInput.Clear();
                    Focus.PushFocus(_addModal);
                    Focus.PushFocus(_textInput);
                }
                else if (!state.adding && !state.showPriority)
                {
                    Focus.ClearFocus();
                }
            })
            .DisposeWith(Subscriptions);

        // When ShowPriorityModal becomes true, switch focus to priority modal
        ViewModel.ShowPriorityModal
            .Where(show => show)
            .Subscribe(_ =>
            {
                Focus.PopFocus(); // Remove add modal focus
                Focus.PushFocus(_priorityModal);
                Focus.PushFocus(_priorityList);
            })
            .DisposeWith(Subscriptions);
    }

    public override ILayoutNode BuildLayout()
    {
        // Create layout nodes as part of the layout tree lifecycle
        _textInput = new TextInputNode()
            .WithPlaceholder("Enter task description...");

        _priorityList = Layouts.SelectionList("High", "Medium", "Low")
            .WithMode(SelectionMode.Single)
            .WithShowNumbers(true)
            .WithHighlightColors(Color.Black, Color.Cyan)
            .WithOtherOption("Custom priority...");

        _addModal = Layouts.Modal()
            .WithTitle("Add New Task")
            .WithTitleColor(Color.Cyan)
            .WithFooter("Enter: Add | Esc: Cancel")
            .WithFooterColor(Color.Gray)
            .WithBorder(BorderStyle.Rounded)
            .WithBorderColor(Color.Cyan)
            .WithBackdrop(BackdropStyle.Dim)
            .WithPosition(ModalPosition.Center)
            .WithPadding(1)
            .WithContent(_textInput)
            .WithDismissOnEscape(true);

        _priorityModal = Layouts.Modal()
            .WithTitle("Select Priority")
            .WithTitleColor(Color.Yellow)
            .WithFooter("Enter: Select | Esc: Cancel")
            .WithFooterColor(Color.Gray)
            .WithBorder(BorderStyle.Rounded)
            .WithBorderColor(Color.Yellow)
            .WithBackdrop(BackdropStyle.Dim)
            .WithPosition(ModalPosition.Center)
            .WithPadding(1)
            .WithContent(_priorityList)
            .WithDismissOnEscape(true);

        // Build the main content
        var mainContent = Layouts.Vertical()
            .WithChild(
                new PanelNode()
                    .WithTitle("Todo List Demo")
                    .WithBorder(BorderStyle.Double)
                    .WithBorderColor(Color.Cyan)
                    .WithContent(
                        ViewModel.Items
                            .CombineLatest(ViewModel.SelectedIndex, (items, selectedIdx) => (items, selectedIdx))
                            .Select(tuple => BuildTodoList(tuple.items, tuple.selectedIdx))
                            .AsLayout())
                    .Fill())
            .WithChild(
                new TextNode("[↑/↓] Navigate [Space] Toggle [A] Add [D] Delete [C] Counter [U] Unicode [Q] Quit")
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

        // Build the layer with modal overlays
        return Layouts.Stack()
            .WithChild(mainContent)
            .WithChild(
                ViewModel.IsAddingItem
                    .CombineLatest(ViewModel.ShowPriorityModal,
                        (adding, showPriority) => adding && !showPriority)
                    .Select(showModal => showModal
                        ? (ILayoutNode)_addModal
                        : Layouts.Empty())
                    .AsLayout())
            .WithChild(
                ViewModel.ShowPriorityModal
                    .Select(showPriority => showPriority
                        ? (ILayoutNode)_priorityModal
                        : Layouts.Empty())
                    .AsLayout());
    }

    private static ILayoutNode BuildTodoList(IReadOnlyList<TodoItem> items, int selectedIndex)
    {
        if (items.Count == 0)
        {
            return new TextNode("\n  No items - press [A] to add\n")
                .WithForeground(Color.DarkGray)
                .Italic();
        }

        var completedCount = items.Count(i => i.IsCompleted);
        var statsText = $"{completedCount}/{items.Count} completed";

        var container = Layouts.Vertical()
            .WithChild(
                new TextNode($"\n  {statsText}\n")
                    .WithForeground(Color.Gray));

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var isSelected = i == selectedIndex;
            var checkbox = item.IsCompleted ? "[✓]" : "[ ]";
            var text = $"  {checkbox} {item.Description}";

            var textNode = new TextNode(text);

            if (isSelected)
            {
                textNode = textNode.WithForeground(Color.Black).WithBackground(Color.Green);
            }
            else if (item.IsCompleted)
            {
                textNode = textNode.WithForeground(Color.DarkGray);
            }
            else
            {
                textNode = textNode.WithForeground(Color.White);
            }

            container = container.WithChild(textNode.Height(1));
        }

        return container;
    }
}
