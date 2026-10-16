// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;
using Xunit.Abstractions;

namespace Termina.Tests.Layout;

/// <summary>
/// A parent can measure a text area at one width and render it at another, for example
/// <c>WidthAuto(1, 60)</c> in a vertical layout on an 80-column screen. A frame must still wrap a
/// long line once, not once for the measure and once for the render.
/// </summary>
public class TextAreaNodeMixedWidthTests(ITestOutputHelper output)
{
    private const int LongLine = 600_000;

    [Fact]
    public void Steady_frame_with_different_measure_and_render_widths_does_not_wrap_the_long_line_again()
    {
        var mixed = AllocationOfSteadyFrame(widthAuto: true);
        var equal = AllocationOfSteadyFrame(widthAuto: false);
        output.WriteLine($"mixed={mixed} equal={equal}");

        // Both layouts show the same long line. After the first frame the wrap is cached, so a
        // steady frame must cost about the same. A second wrap of the line allocates tens of MB.
        Assert.True(mixed < 4 * 1024 * 1024, $"A steady frame allocated {mixed} bytes (equal widths: {equal}).");
    }

    private static long AllocationOfSteadyFrame(bool widthAuto)
    {
        var node = new TextAreaNode();
        if (widthAuto)
            node.WidthAuto(1, 60);
        node.Text = new string('a', LongLine);
        node.MoveCursorToEnd();
        var layout = Layouts.Vertical().WithChild(node);

        DrawFrame(layout);
        DrawFrame(layout);

        var before = GC.GetAllocatedBytesForCurrentThread();
        DrawFrame(layout);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void DrawFrame(ILayoutNode layout)
    {
        var terminal = new VirtualTerminal(80, 30);
        var size = layout.Measure(new Size(80, 30));
        layout.Render(
            new RegionRenderContext(terminal, 0, 0, 80, 30),
            new Rect(0, 0, 80, Math.Min(size.Height, 30)));
    }
}
