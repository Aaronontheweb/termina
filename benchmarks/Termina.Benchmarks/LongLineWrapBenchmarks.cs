// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using System.Text;
using BenchmarkDotNet.Attributes;
using Termina.Components.Streaming;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Benchmarks;

/// <summary>
/// Benchmarks word wrapping of one long line, and rendering it through a <see cref="StreamingTextNode"/>.
/// </summary>
/// <remarks>
/// Wrapping one line of n characters must scale linearly with n. The Unbroken content has no break
/// opportunity, so every row comes from splitting a single word.
/// </remarks>
[MemoryDiagnoser]
public class LongLineWrapBenchmarks
{
    private const int Width = 120;
    private const int Height = 40;

    private string _text = null!;
    private TextAreaNode _textArea = null!;
    private bool _flip;
    private StyledLine _line = null!;
    private StreamingTextNode _node = null!;
    private VirtualTerminal _terminal = null!;
    private RegionRenderContext _context = null!;
    private Rect _bounds;

    [Params(1_000, 5_000, 10_000, 30_000, 100_000)]
    public int Length { get; set; }

    [Params("Unbroken", "Words", "Cjk", "EmojiZwj")]
    public string Content { get; set; } = "Unbroken";

    [GlobalSetup]
    public void Setup()
    {
        var text = Build(Content, Length);
        _text = text;
        _textArea = new TextAreaNode();
        _line = new StyledLine();
        // Two styles so that the wrapper has to carry styles across the breaks.
        var half = text.Length / 2;
        var cut = half;
        // never cut inside a surrogate pair
        if (cut > 0 && cut < text.Length && char.IsLowSurrogate(text[cut])) cut++;
        _line.Append(text[..cut], Color.Green);
        _line.Append(text[cut..], Color.Red);

        _node = StreamingTextNode.Create();
        _node.AppendLine(text);
        _terminal = new VirtualTerminal(Width, Height);
        _context = new RegionRenderContext(_terminal, 0, 0, Width, Height);
        _bounds = new Rect(0, 0, Width, Height);
    }

    [Benchmark]
    public int WrapLine() => StyledWordWrapper.WrapLine(_line, Width).Count;

    [Benchmark]
    public int WordWrapperWrapLine() => WordWrapper.WrapLine(_text, Width).Count;

    // Setting the text invalidates the wrapped rows, as an edit does, and Measure wraps them again.
    [Benchmark]
    public Size TextAreaMeasure()
    {
        _flip = !_flip;
        _textArea.Text = _flip ? _text : _text + "x";
        return _textArea.Measure(new Size(Width, Height));
    }

    // A frame measures the text area while its text stays the same, as the cursor blink does.
    // The column count is cached, so this must not depend on the line length.
    [Benchmark]
    public Size TextAreaMeasureUnchanged() => _textArea.Measure(new Size(Width, Height));

    [Benchmark]
    public void RenderStreamingTextNode() => _node.Render(_context, _bounds);

    private static string Build(string kind, int length)
    {
        var sb = new StringBuilder(length + 8);
        switch (kind)
        {
            case "Unbroken":
                sb.Append('a', length);
                break;
            case "Words":
                var rng = new Random(42);
                while (sb.Length < length)
                {
                    sb.Append((char)('a' + rng.Next(26)), rng.Next(1, 12));
                    sb.Append(' ');
                }
                sb.Length = length;
                break;
            case "Cjk":
                while (sb.Length < length) sb.Append((char)(0x4E00 + sb.Length % 500));
                break;
            case "EmojiZwj":
                // family ZWJ sequence: 11 UTF-16 chars, one text element
                while (sb.Length < length) sb.Append("\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466");
                break;
        }
        return sb.ToString();
    }
}
