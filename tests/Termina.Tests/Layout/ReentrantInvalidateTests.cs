// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;

namespace Termina.Tests.Layout;

/// <summary>
/// Tests for the re-entrant Invalidate() guard on DynamicLayoutNode and KeyedDynamicLayoutNode (#159).
///
/// A factory that invalidates its own node during evaluation used to blank the content (DynamicLayoutNode)
/// or recurse without end (StackOverflow). The guard defers the re-run: it re-evaluates once the current
/// evaluation finishes, and repeats until the factory stops asking (convergence). A factory that never
/// converges is bounded by a fixed cap. The guard flag resets in a finally block, so a throwing factory
/// does not freeze the node.
/// </summary>
public sealed class ReentrantInvalidateTests
{
    // ---- DynamicLayoutNode -----------------------------------------------------------------------------

    [Fact]
    public void DynamicLayoutNode_Invalidate_ReEvaluatesFactoryEachCall()
    {
        var a = new TextNode("a");
        var b = new TextNode("b");
        var calls = 0;
        var node = new DynamicLayoutNode(() => ++calls == 1 ? a : b);

        node.Invalidate();
        Assert.Same(a, node.GetChildNodes().First());

        node.Invalidate();
        Assert.Same(b, node.GetChildNodes().First());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DynamicLayoutNode_ReentrantInvalidate_ConvergesToFinalContent()
    {
        DynamicLayoutNode node = null!;
        var calls = 0;
        var transient = new TextNode("transient");
        var settled = new TextNode("settled");

        node = new DynamicLayoutNode(() =>
        {
            calls++;
            if (calls == 1)
            {
                // Simulate an auto-advance side effect that invalidates this same node mid-evaluation.
                node.Invalidate();
                return transient;
            }

            return settled;
        });

        node.Invalidate();

        // The node must settle on the final content, not the transient value produced during re-entrancy.
        Assert.Same(settled, node.GetChildNodes().First());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DynamicLayoutNode_FactoryThrows_GuardResets()
    {
        DynamicLayoutNode node = null!;
        var shouldThrow = true;
        var recovered = new TextNode("recovered");

        node = new DynamicLayoutNode(() =>
        {
            if (shouldThrow)
                throw new InvalidOperationException("boom");
            return recovered;
        });

        Assert.Throws<InvalidOperationException>(() => node.Invalidate());

        // The guard must have reset in a finally block; a later evaluation must still work.
        shouldThrow = false;
        node.Invalidate();
        Assert.Same(recovered, node.GetChildNodes().First());
    }

    [Fact]
    public void DynamicLayoutNode_ReentrantInvalidate_EveryPass_StopsAtCap()
    {
        DynamicLayoutNode node = null!;
        var calls = 0;

        node = new DynamicLayoutNode(() =>
        {
            calls++;
            node.Invalidate(); // never converges — asks to re-run on every pass
            return new TextNode("pass" + calls);
        });

        node.Invalidate();

        // Bounded: no StackOverflow, no hang. The factory runs at most the cap number of times.
        Assert.Equal(DynamicLayoutNode.MaxReevaluationPasses, calls);
    }

    // ---- KeyedDynamicLayoutNode ------------------------------------------------------------------------

    [Fact]
    public void KeyedDynamicLayoutNode_Invalidate_ReEvaluatesOnKeyChange()
    {
        var key = 0;
        var created = new Dictionary<int, ILayoutNode>();
        var node = new KeyedDynamicLayoutNode<int>(
            () => key,
            k => created[k] = new TextNode("k" + k));

        node.Invalidate();
        Assert.Same(created[0], node.GetChildNodes().First());

        key = 1;
        node.Invalidate();
        Assert.Same(created[1], node.GetChildNodes().First());
    }

    [Fact]
    public void KeyedDynamicLayoutNode_ReentrantInvalidate_ConvergesToFinalKey()
    {
        KeyedDynamicLayoutNode<int> node = null!;
        var key = 0;
        var keyCalls = 0;
        var created = new Dictionary<int, ILayoutNode>();

        node = new KeyedDynamicLayoutNode<int>(
            () =>
            {
                keyCalls++;
                if (keyCalls == 1)
                {
                    key = 1;
                    node.Invalidate();
                }

                return key;
            },
            k => created[k] = new TextNode("k" + k));

        node.Invalidate();

        Assert.Same(created[1], node.GetChildNodes().First());
    }

    [Fact]
    public void KeyedDynamicLayoutNode_KeySelectorThrows_GuardResets()
    {
        KeyedDynamicLayoutNode<int> node = null!;
        var shouldThrow = true;
        var recovered = new TextNode("recovered");

        node = new KeyedDynamicLayoutNode<int>(
            () => shouldThrow ? throw new InvalidOperationException("boom") : 5,
            _ => recovered);

        Assert.Throws<InvalidOperationException>(() => node.Invalidate());

        shouldThrow = false;
        node.Invalidate();
        Assert.Same(recovered, node.GetChildNodes().First());
    }

    [Fact]
    public void KeyedDynamicLayoutNode_ReentrantInvalidate_EveryPass_StopsAtCap()
    {
        KeyedDynamicLayoutNode<int> node = null!;
        var keyCalls = 0;

        node = new KeyedDynamicLayoutNode<int>(
            () =>
            {
                keyCalls++;
                node.Invalidate(); // never converges
                return keyCalls;
            },
            k => new TextNode("k" + k));

        node.Invalidate();

        Assert.Equal(KeyedDynamicLayoutNode<int>.MaxReevaluationPasses, keyCalls);
    }
}
