// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Input;
using Termina.Reactive;
using Termina.Terminal;

namespace Termina.Demo.RegionBased;

/// <summary>
/// ViewModel for the counter demo.
/// Demonstrates reactive properties with the v2 rendering infrastructure.
/// </summary>
public class CounterViewModel : ReactiveViewModel
{
    public ReactiveProperty<int> Count { get; } = new(0);
    public ReactiveProperty<string> StatusMessage { get; } = new("Press Up/Down to change count, Enter to submit message, Escape to quit");
    public ReactiveProperty<string> InputText { get; } = new("");
    public ReactiveProperty<List<string>> Messages { get; } = new(new());

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

            case ConsoleKey.Enter:
                if (!string.IsNullOrWhiteSpace(InputText.Value))
                {
                    var newMessages = new List<string>(Messages.Value)
                    {
                        $"[{DateTime.Now:HH:mm:ss}] {InputText.Value}"
                    };
                    // Keep last 8 messages
                    if (newMessages.Count > 8)
                        newMessages.RemoveAt(0);
                    Messages.Value = newMessages;
                    InputText.Value = "";
                    StatusMessage.Value = "Message sent!";
                }
                break;

            case ConsoleKey.Backspace:
                if (InputText.Value.Length > 0)
                {
                    var text = InputText.Value;
                    InputText.Value = text[..DisplayWidth.GetPreviousTextElementIndex(text, text.Length)];
                }
                break;

            case ConsoleKey.Escape:
                Shutdown();
                break;

            default:
                if (key.Text is not null)
                {
                    InputText.Value += key.Text;
                    break;
                }

                if (key.KeyInfo.KeyChar >= 32 && key.KeyInfo.KeyChar < 127)
                {
                    InputText.Value += key.KeyInfo.KeyChar;
                }
                break;
        }
    }

    public override void Dispose()
    {
        Count.Dispose();
        StatusMessage.Dispose();
        InputText.Dispose();
        Messages.Dispose();
        base.Dispose();
    }
}
