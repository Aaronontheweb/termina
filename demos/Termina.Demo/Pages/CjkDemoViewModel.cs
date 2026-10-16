using R3;
using Termina.Input;
using Termina.Reactive;

namespace Termina.Demo.Pages;

/// <summary>
/// ViewModel for the Unicode/CJK smoke demo.
/// </summary>
public class CjkDemoViewModel : ReactiveViewModel
{
    public ReactiveProperty<string> InputText { get; } = new("none");
    public ReactiveProperty<string> StatusMessage { get; } = new("CJK text should wrap without gaps, overlap, or mangled columns.");

    public override void OnActivated()
    {
        Input.OfType<IInputEvent, KeyPressed>()
            .Subscribe(HandleKeyPress)
            .DisposeWith(Subscriptions);
    }

    public void OnInputSubmitted(string text)
    {
        InputText.Value = string.IsNullOrWhiteSpace(text) ? "empty" : text;
        StatusMessage.Value = $"Submitted {text.Length} chars.";
    }

    private void HandleKeyPress(KeyPressed key)
    {
        switch (key.KeyInfo.Key)
        {
            case ConsoleKey.C:
                Navigate("/counter");
                break;

            case ConsoleKey.T:
                Navigate("/todos");
                break;

            case ConsoleKey.Q:
                Shutdown();
                break;
        }
    }

    public override void Dispose()
    {
        InputText.Dispose();
        StatusMessage.Dispose();
        base.Dispose();
    }
}
