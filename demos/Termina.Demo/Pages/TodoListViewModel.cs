using R3;
using Termina.Input;
using Termina.Reactive;

namespace Termina.Demo.Pages;

/// <summary>
/// ViewModel for a todo list demo.
/// Contains only state and business logic - no UI concerns.
/// </summary>
public class TodoListViewModel : ReactiveViewModel
{
    private readonly TraceFileInfo _traceFileInfo;

    public TodoListViewModel(TraceFileInfo traceFileInfo)
    {
        _traceFileInfo = traceFileInfo;
    }

    /// <summary>
    /// Gets the path to the trace log file for display in the UI.
    /// </summary>
    public string TraceFilePath => _traceFileInfo.FilePath;

    public ReactiveProperty<IReadOnlyList<TodoItem>> Items { get; } = new(new List<TodoItem>
    {
        new("Learn Termina reactive patterns", false),
        new("Build a TUI app", false),
        new("Deploy to production", false),
        new("Celebrate success", false)
    });

    public ReactiveProperty<int> SelectedIndex { get; } = new(0);
    public ReactiveProperty<string> StatusMessage { get; } = new("Navigate with ↑/↓, Space to toggle, C for counter, Q to quit");
    public ReactiveProperty<bool> IsAddingItem { get; } = new(false);
    public ReactiveProperty<bool> ShowPriorityModal { get; } = new(false);
    public ReactiveProperty<string> PendingTaskText { get; } = new("");

    public override void OnActivated()
    {
        // Subscribe to keyboard input
        Input.OfType<IInputEvent, KeyPressed>()
            .Subscribe(HandleKeyPress)
            .DisposeWith(Subscriptions);
    }

    /// <summary>
    /// Called by Page when text input is submitted.
    /// </summary>
    public void OnTextInputSubmitted(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            PendingTaskText.Value = text.Trim();
            ShowPriorityModal.Value = true;
            StatusMessage.Value = "Select priority with ↑/↓ or number keys, Enter to confirm";
        }
        else
        {
            CancelAddItem();
        }
    }

    /// <summary>
    /// Called by Page when priority is selected.
    /// </summary>
    public void OnPrioritySelected(string priority)
    {
        var newItems = Items.Value.ToList();
        var displayText = priority != "Normal" ? $"[{priority}] {PendingTaskText.Value}" : PendingTaskText.Value;
        newItems.Add(new TodoItem(displayText, false));
        Items.Value = newItems;
        SelectedIndex.Value = Items.Value.Count - 1;
        StatusMessage.Value = $"Added: {displayText}";

        // Clean up state
        ShowPriorityModal.Value = false;
        IsAddingItem.Value = false;
        PendingTaskText.Value = "";
    }

    /// <summary>
    /// Called by Page when priority selection is cancelled.
    /// </summary>
    public void OnPriorityCancelled()
    {
        ShowPriorityModal.Value = false;
    }

    /// <summary>
    /// Called by Page when add modal is dismissed.
    /// </summary>
    public void OnAddModalDismissed()
    {
        CancelAddItem();
    }

    /// <summary>
    /// Called by Page when priority modal is dismissed.
    /// </summary>
    public void OnPriorityModalDismissed()
    {
        ShowPriorityModal.Value = false;
        IsAddingItem.Value = false;
        StatusMessage.Value = "Cancelled adding item";
    }

    private void HandleKeyPress(KeyPressed key)
    {
        // Don't handle keys when in modal mode - let the modals handle input
        if (IsAddingItem.Value || ShowPriorityModal.Value)
            return;

        switch (key.KeyInfo.Key)
        {
            case ConsoleKey.UpArrow:
                if (SelectedIndex.Value > 0)
                {
                    SelectedIndex.Value--;
                    StatusMessage.Value = $"Selected: {Items.Value[SelectedIndex.Value].Description}";
                }
                break;

            case ConsoleKey.DownArrow:
                if (SelectedIndex.Value < Items.Value.Count - 1)
                {
                    SelectedIndex.Value++;
                    StatusMessage.Value = $"Selected: {Items.Value[SelectedIndex.Value].Description}";
                }
                break;

            case ConsoleKey.Spacebar:
                ToggleSelected();
                break;

            case ConsoleKey.A:
                StartAddingItem();
                break;

            case ConsoleKey.D:
                DeleteSelected();
                break;

            case ConsoleKey.C:
                Navigate("/counter");
                break;

            case ConsoleKey.U:
                Navigate("/unicode");
                break;

            case ConsoleKey.Q:
                Shutdown();
                break;
        }
    }

    private void StartAddingItem()
    {
        IsAddingItem.Value = true;
        ShowPriorityModal.Value = false;
        PendingTaskText.Value = "";
        StatusMessage.Value = "Enter task name and press Enter, or Escape to cancel";
    }

    private void CancelAddItem()
    {
        IsAddingItem.Value = false;
        ShowPriorityModal.Value = false;
        PendingTaskText.Value = "";
        StatusMessage.Value = "Cancelled adding item";
    }

    private void ToggleSelected()
    {
        if (Items.Value.Count == 0) return;

        var item = Items.Value[SelectedIndex.Value];
        var newItem = item with { IsCompleted = !item.IsCompleted };

        var newItems = Items.Value.ToList();
        newItems[SelectedIndex.Value] = newItem;
        Items.Value = newItems;

        StatusMessage.Value = newItem.IsCompleted
            ? $"Completed: {newItem.Description}"
            : $"Uncompleted: {newItem.Description}";
    }

    private void DeleteSelected()
    {
        if (Items.Value.Count == 0) return;

        var deleted = Items.Value[SelectedIndex.Value];
        var newItems = Items.Value.ToList();
        newItems.RemoveAt(SelectedIndex.Value);
        Items.Value = newItems;

        if (SelectedIndex.Value >= Items.Value.Count && Items.Value.Count > 0)
        {
            SelectedIndex.Value = Items.Value.Count - 1;
        }

        StatusMessage.Value = $"Deleted: {deleted.Description}";
    }

    public override void Dispose()
    {
        Items.Dispose();
        SelectedIndex.Dispose();
        StatusMessage.Dispose();
        IsAddingItem.Dispose();
        ShowPriorityModal.Dispose();
        PendingTaskText.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// A simple todo item.
/// </summary>
public record TodoItem(string Description, bool IsCompleted);
