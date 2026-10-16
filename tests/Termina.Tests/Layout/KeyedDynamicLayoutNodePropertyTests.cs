// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using CsCheck;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;
using LayoutSize = Termina.Layout.Size;

namespace Termina.Tests.Layout;

/// <summary>
/// Property-based lifecycle tests for <see cref="KeyedDynamicLayoutNode{TKey}"/> (CsCheck).
/// </summary>
/// <remarks>
/// <para>
/// USER STORY. A page can change its selected key several times before the terminal draws another
/// frame. <see cref="KeyedDynamicCachePolicy.RetainAll"/> must restore each visited screen.
/// <see cref="KeyedDynamicCachePolicy.EvictOnKeyChange"/> must create a fresh screen after a key
/// change.
/// </para>
/// <para>
/// THE RISK. Event order can expose stale children, early disposal, missed disposal, or duplicate
/// disposal. Specific examples cover known orders but cannot cover their many combinations.
/// </para>
/// <para>
/// THE ORACLE. A small reference model tracks the selected key, the expected child identity, and
/// children that await a layout pass. It checks the public child and each disposal count after every
/// generated step.
/// </para>
/// <para>
/// HOW TO REPRODUCE A FAILURE. CsCheck prints a reduced command trace and a seed. Set
/// <c>CsCheck_Seed</c> to that seed to run the same trace again.
/// </para>
/// </remarks>
public class KeyedDynamicLayoutNodePropertyTests
{
    private const int Iter = 2_000;
    private static readonly LayoutSize Available = new(80, 24);
    private static readonly Rect Bounds = new(0, 0, 80, 24);
    private static readonly NullRenderContext RenderContext = new();

    // A small key set makes returns to prior keys common. An optional layout pass creates batches of
    // one or more retired children between frames.
    private static readonly Gen<List<TraceStep>> TraceGen =
        (from key in Gen.Int[0, 3]
         from layoutPass in Gen.OneOfConst(LayoutPass.None, LayoutPass.Measure, LayoutPass.Render)
         select new TraceStep(key, layoutPass)).List[1, 40];

    // The factory first returns fresh children. It then returns one prior child, which can be the
    // current child or a child that a prior layout pass disposed.
    private static readonly Gen<ReuseScenario> ReuseScenarioGen =
        from freshTransitions in Gen.Int[1, 12]
        from reuseIndex in Gen.Int[0, freshTransitions]
        select new ReuseScenario(freshTransitions, reuseIndex);

    [Fact]
    public void RetainAll_RestoresEachVisitedScreenWithoutEarlyDisposal() =>
        // A user can leave a tab and return later. The tab must keep the same controls and state, and
        // no visited tab can receive disposal before the parent layout receives disposal.
        TraceGen.Sample(trace => VerifyTrace(KeyedDynamicCachePolicy.RetainAll, trace), iter: Iter);

    [Fact]
    public void EvictOnKeyChange_CreatesFreshScreensAndDefersDisposal() =>
        // A workflow can change screens more than once during one input callback. Each transition
        // needs a fresh child, while all retired children stay alive until the next layout pass.
        TraceGen.Sample(trace => VerifyTrace(KeyedDynamicCachePolicy.EvictOnKeyChange, trace), iter: Iter);

    [Fact]
    public void EvictOnKeyChange_RejectsEveryPreviouslyReturnedScreen() =>
        // A factory can accidentally retain an old screen and return it later. Termina must reject
        // that screen instead of restoring stale state or activating a child that it disposed.
        ReuseScenarioGen.Sample(VerifyPriorChildReuseFails, iter: Iter);

    private static void VerifyTrace(KeyedDynamicCachePolicy policy, IReadOnlyList<TraceStep> trace)
    {
        var selectedKey = 0;
        var children = new List<TrackingNode>();
        var node = new KeyedDynamicLayoutNode<int>(
            () => selectedKey,
            key =>
            {
                var child = new TrackingNode(key, children.Count);
                children.Add(child);
                return child;
            },
            policy);

        node.OnActivate();
        node.Measure(Available);
        node.Measure(Available); // Dispose the initial EmptyNode under EvictOnKeyChange.

        var expectedFactoryCalls = 1;
        var expectedCurrent = Assert.Single(children);
        var activeKey = 0;
        var firstChildByKey = new Dictionary<int, TrackingNode> { [0] = expectedCurrent };
        var pendingDisposal = new List<TrackingNode>();
        var expectedDisposed = new HashSet<TrackingNode>();

        foreach (var step in trace)
        {
            var keyChanged = step.Key != activeKey;
            var previousChild = expectedCurrent;
            selectedKey = step.Key;

            node.Invalidate();

            if (policy == KeyedDynamicCachePolicy.RetainAll)
            {
                if (!firstChildByKey.TryGetValue(step.Key, out expectedCurrent))
                {
                    expectedFactoryCalls++;
                    Assert.Equal(expectedFactoryCalls, children.Count);
                    expectedCurrent = children[^1];
                    firstChildByKey.Add(step.Key, expectedCurrent);
                }
            }
            else if (keyChanged)
            {
                expectedFactoryCalls++;
                pendingDisposal.Add(previousChild);
                Assert.Equal(expectedFactoryCalls, children.Count);
                expectedCurrent = children[^1];
            }

            activeKey = step.Key;
            AssertState(node, children, expectedCurrent, expectedFactoryCalls, expectedDisposed);

            if (step.LayoutPass != LayoutPass.None)
            {
                ApplyLayoutPass(node, step.LayoutPass);
                expectedDisposed.UnionWith(pendingDisposal);
                pendingDisposal.Clear();
            }

            AssertState(node, children, expectedCurrent, expectedFactoryCalls, expectedDisposed);
        }

        node.Dispose();

        Assert.All(children, child => Assert.Equal(1, child.DisposeCount));
    }

    private static void VerifyPriorChildReuseFails(ReuseScenario scenario)
    {
        var selectedKey = 0;
        var returnPriorChild = false;
        var children = new List<TrackingNode>();
        var node = new KeyedDynamicLayoutNode<int>(
            () => selectedKey,
            key =>
            {
                if (returnPriorChild)
                    return children[scenario.ReuseIndex];

                var child = new TrackingNode(key, children.Count);
                children.Add(child);
                return child;
            },
            KeyedDynamicCachePolicy.EvictOnKeyChange);

        node.Measure(Available);
        node.Measure(Available);

        for (var key = 1; key <= scenario.FreshTransitions; key++)
        {
            selectedKey = key;
            node.Invalidate();
            node.Measure(Available);
        }

        returnPriorChild = true;
        selectedKey = scenario.FreshTransitions + 1;

        var exception = Assert.Throws<InvalidOperationException>(node.Invalidate);

        Assert.Contains("Return a new child", exception.Message);

        node.Dispose();

        Assert.All(children, child => Assert.Equal(1, child.DisposeCount));
    }

    private static void ApplyLayoutPass(KeyedDynamicLayoutNode<int> node, LayoutPass layoutPass)
    {
        switch (layoutPass)
        {
            case LayoutPass.Measure:
                node.Measure(Available);
                break;
            case LayoutPass.Render:
                node.Render(RenderContext, Bounds);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layoutPass), layoutPass, "A layout pass is required.");
        }
    }

    private static void AssertState(
        KeyedDynamicLayoutNode<int> node,
        IReadOnlyCollection<TrackingNode> children,
        TrackingNode expectedCurrent,
        int expectedFactoryCalls,
        IReadOnlySet<TrackingNode> expectedDisposed)
    {
        Assert.Equal(expectedFactoryCalls, children.Count);
        Assert.Same(expectedCurrent, node.GetChildNodes().Single());
        Assert.Equal(0, expectedCurrent.DisposeCount);

        foreach (var child in children)
            Assert.Equal(expectedDisposed.Contains(child) ? 1 : 0, child.DisposeCount);
    }

    private readonly record struct TraceStep(int Key, LayoutPass LayoutPass);

    private readonly record struct ReuseScenario(int FreshTransitions, int ReuseIndex);

    private enum LayoutPass
    {
        None,
        Measure,
        Render
    }

    private sealed class TrackingNode(int key, int instance) : LayoutNode
    {
        public int Key { get; } = key;
        public int Instance { get; } = instance;
        public int DisposeCount { get; private set; }

        public override string ToString() => $"Child {Instance} for key {Key}";

        public override LayoutSize Measure(LayoutSize available) => new(10, 1);

        public override void Render(IRenderContext context, Rect bounds)
        {
        }

        public override void Dispose()
        {
            DisposeCount++;
            base.Dispose();
        }
    }

    private sealed class NullRenderContext : IRenderContext
    {
        public int Width => Bounds.Width;
        public int Height => Bounds.Height;

        public void WriteAt(int x, int y, string text)
        {
        }

        public void WriteAt(int x, int y, char c)
        {
        }

        public void SetForeground(Color color)
        {
        }

        public void SetBackground(Color color)
        {
        }

        public void ResetColors()
        {
        }

        public void SetDecoration(TextDecoration decoration)
        {
        }

        public void ApplyStyle(TextStyle style)
        {
        }

        public void Fill(int x, int y, int width, int height, char c = ' ')
        {
        }

        public void Clear()
        {
        }

        public IRenderContext CreateSubContext(Rect bounds) => this;
    }
}
