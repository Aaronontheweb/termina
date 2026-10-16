// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Layout;
using Termina.Rendering;

namespace Termina.Tests.Layout;

/// <summary>
/// Regression tests for <see cref="GridNode.SetCell"/> content switching (see #345 / #70).
///
/// Re-setting a cell must deactivate the old content (not dispose it), and must tear down that cell's
/// invalidation subscription so a swapped-out cell no longer bumps the grid — without leaking or growing
/// subscriptions. Content is disposed only at teardown. The per-cell subscription array must also survive a
/// grid resize.
/// </summary>
public sealed class GridNodeCellSwitchTests
{
    /// <summary>A layout node that records deactivation/disposal and can raise invalidations on demand.</summary>
    private sealed class InvalidatingSpy : LayoutNode, IInvalidatingNode
    {
        private readonly Subject<Unit> _invalidated = new();

        public Observable<Unit> Invalidated => _invalidated;
        public int DeactivateCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public void RaiseInvalidated() => _invalidated.OnNext(Unit.Default);

        public override Size Measure(Size available) => new Size(0, 0);
        public override void Render(IRenderContext context, Rect bounds) { }
        public override void OnDeactivate() { DeactivateCount++; base.OnDeactivate(); }

        public override void Dispose()
        {
            IsDisposed = true;
            _invalidated.OnCompleted();
            _invalidated.Dispose();
            base.Dispose();
        }
    }

    [Fact]
    public void SetCell_ReplacingContent_DeactivatesOldContent_DoesNotDispose()
    {
        var grid = new GridNode(1, 1);
        var first = new InvalidatingSpy();
        var second = new InvalidatingSpy();

        grid.SetCell(0, 0, first);
        grid.SetCell(0, 0, second);

        // Swapping a cell must deactivate the old content, not destroy it.
        Assert.Equal(1, first.DeactivateCount);
        Assert.False(first.IsDisposed);

        // Teardown disposes the current cell content.
        grid.Dispose();
        Assert.True(second.IsDisposed);
    }

    [Fact]
    public void SetCell_ReplacingContent_TearsDownOldCellSubscription()
    {
        var grid = new GridNode(1, 1);
        var first = new InvalidatingSpy();
        var second = new InvalidatingSpy();

        grid.SetCell(0, 0, first);
        grid.SetCell(0, 0, second);

        var gridInvalidations = 0;
        using var _ = grid.Invalidated.Subscribe(__ => gridInvalidations++);

        // The old, swapped-out cell survives (deactivated, not disposed)...
        Assert.False(first.IsDisposed);

        // ...but its invalidation subscription is gone, so it no longer bumps the grid.
        first.RaiseInvalidated();
        Assert.Equal(0, gridInvalidations);

        // The current cell still forwards its invalidations.
        second.RaiseInvalidated();
        Assert.Equal(1, gridInvalidations);
    }

    [Fact]
    public void SetCell_TriggeringResize_PreservesExistingCellSubscription()
    {
        var grid = new GridNode(1, 1);
        var existing = new InvalidatingSpy();

        grid.SetCell(0, 0, existing);
        // Setting a far cell grows the grid; the existing cell's subscription must survive the resize.
        grid.SetCell(2, 2, new InvalidatingSpy());

        var gridInvalidations = 0;
        using var _ = grid.Invalidated.Subscribe(__ => gridInvalidations++);

        existing.RaiseInvalidated();
        Assert.Equal(1, gridInvalidations);
    }
}
