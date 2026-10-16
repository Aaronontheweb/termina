// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Rendering;

namespace Termina.Tests.Layout;

/// <summary>
/// Regression tests for issue #70. A container must deactivate — not dispose — its old content on a content
/// switch, and dispose content only at teardown. Destroying a node on a switch stops it rendering or handling
/// input and risks ObjectDisposedException for in-flight R3 subscriptions. These pin the intended lifecycle
/// for <see cref="PanelNode"/> and <see cref="ScrollableContainerNode"/>.
/// </summary>
public sealed class ContentSwitchDeactivationTests
{
    /// <summary>A layout node that records how many times it is deactivated and whether it is disposed.</summary>
    private sealed class LifecycleSpy : LayoutNode
    {
        public int DeactivateCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public override Size Measure(Size available) => new Size(0, 0);
        public override void Render(IRenderContext context, Rect bounds) { }
        public override void OnDeactivate() { DeactivateCount++; base.OnDeactivate(); }
        public override void Dispose() { IsDisposed = true; base.Dispose(); }
    }

    [Fact]
    public void PanelNode_WithContent_DeactivatesOldContent_DoesNotDispose()
    {
        var panel = new PanelNode();
        var first = new LifecycleSpy();
        var second = new LifecycleSpy();

        panel.WithContent(first);
        panel.WithContent(second);

        // Swapping content must deactivate the old content, not destroy it.
        Assert.Equal(1, first.DeactivateCount);
        Assert.False(first.IsDisposed);

        // Teardown disposes the current content.
        panel.Dispose();
        Assert.True(second.IsDisposed);
    }

    [Fact]
    public void ScrollableContainerNode_WithContent_DeactivatesOldContent_DoesNotDispose()
    {
        var scroll = new ScrollableContainerNode();
        var first = new LifecycleSpy();
        var second = new LifecycleSpy();

        scroll.WithContent(first);
        scroll.WithContent(second);

        Assert.Equal(1, first.DeactivateCount);
        Assert.False(first.IsDisposed);

        scroll.Dispose();
        Assert.True(second.IsDisposed);
    }
}
