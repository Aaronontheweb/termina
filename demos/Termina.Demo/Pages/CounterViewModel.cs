using R3;
using Termina.Input;
using Termina.Reactive;

namespace Termina.Demo.Pages;

/// <summary>
/// ViewModel for a simple counter demo.
/// Demonstrates reactive properties and input handling.
/// </summary>
public class CounterViewModel : ReactiveViewModel
{
    private readonly TraceFileInfo _traceFileInfo;

    public ReactiveProperty<int> Count { get; } = new(0);
    public ReactiveProperty<string> StatusMessage { get; } = new("Press Up/Down to change count, T for todos, Q to quit");

    public CounterViewModel(TraceFileInfo traceFileInfo)
    {
        _traceFileInfo = traceFileInfo;
    }

    /// <summary>
    /// Gets the path to the trace log file for display in the UI.
    /// </summary>
    public string TraceFilePath => _traceFileInfo.FilePath;

    public override void OnActivated()
    {
        // Subscribe to keyboard input
        Input.OfType<IInputEvent, KeyPressed>()
            .Subscribe(HandleKeyPress)
            .DisposeWith(Subscriptions);
    }

    private void HandleKeyPress(KeyPressed key)
    {
        switch (key.KeyInfo.Key)
        {
            case ConsoleKey.UpArrow:
                Count.Value++;
                StatusMessage.Value = $"Incremented to {Count.Value}";
                break;

            case ConsoleKey.DownArrow:
                Count.Value--;
                StatusMessage.Value = $"Decremented to {Count.Value}";
                break;

            case ConsoleKey.R:
                Count.Value = 0;
                StatusMessage.Value = "Counter reset";
                break;

            case ConsoleKey.T:
                Navigate("/todos");
                break;

            case ConsoleKey.U:
                Navigate("/unicode");
                break;

            case ConsoleKey.Q:
                Shutdown();
                break;
        }
    }

    public override void Dispose()
    {
        Count.Dispose();
        StatusMessage.Dispose();
        base.Dispose();
    }
}
