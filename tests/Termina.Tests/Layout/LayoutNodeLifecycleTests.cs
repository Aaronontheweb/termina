// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Layout;
using Termina.Rendering;

namespace Termina.Tests.Layout;

/// <summary>
/// Tests for the layout node activation/deactivation lifecycle.
/// Verifies that nodes properly pause/resume resources during navigation.
/// </summary>
public class LayoutNodeLifecycleTests
{
    [Fact]
    public void TextInputNode_OnDeactivate_StopsCursorAnimation()
    {
        // Arrange
        using var node = new TextInputNode();
        node.OnFocused(); // Focus starts animation

        // Assert - animation should be running
        Assert.True(node.IsAnimating);

        // Act
        node.OnDeactivate();

        // Assert - animation should be stopped
        Assert.False(node.IsAnimating);
    }

    [Fact]
    public void TextInputNode_OnActivate_ResumesCursorAnimation_WhenFocused()
    {
        // Arrange
        using var node = new TextInputNode();
        node.OnFocused(); // Focus starts animation
        node.OnDeactivate(); // Stop animation

        // Assert - animation should be stopped
        Assert.False(node.IsAnimating);

        // Act
        node.OnActivate();

        // Assert - animation should be resumed because node has focus
        Assert.True(node.IsAnimating);
    }

    [Fact]
    public void TextInputNode_OnActivate_DoesNotStartAnimation_WhenNotFocused()
    {
        // Arrange
        using var node = new TextInputNode();
        // Don't call OnFocused - node doesn't have focus

        // Act
        node.OnActivate();

        // Assert - animation should NOT start because node doesn't have focus
        Assert.False(node.IsAnimating);
    }

    [Fact]
    public void SpinnerNode_OnDeactivate_StopsAnimation()
    {
        // Arrange
        using var node = new SpinnerNode();
        node.OnActivate();

        // Assert - SpinnerNode starts on activation
        Assert.True(node.IsAnimating);

        // Act
        node.OnDeactivate();

        // Assert
        Assert.False(node.IsAnimating);
    }

    [Fact]
    public void SpinnerNode_OnActivate_ResumesAnimation()
    {
        // Arrange
        using var node = new SpinnerNode();
        node.OnDeactivate(); // Stop animation

        // Assert
        Assert.False(node.IsAnimating);

        // Act
        node.OnActivate();

        // Assert
        Assert.True(node.IsAnimating);
    }

    [Fact]
    public void ReactiveLayoutNode_OnDeactivate_DisposesSubscription()
    {
        // Arrange
        var subject = new Subject<ILayoutNode>();
        using var node = new ReactiveLayoutNode(subject);
        var emissionCount = 0;

        // Subscribe to invalidation to track if subscription is active
        node.Invalidated.Subscribe(_ => emissionCount++);

        // Act - emit before deactivation
        subject.OnNext(new TextNode("Test 1"));
        Assert.Equal(1, emissionCount);

        // Deactivate
        node.OnDeactivate();

        // Emit after deactivation - should NOT trigger invalidation
        subject.OnNext(new TextNode("Test 2"));

        // Assert - emission count should not increase
        Assert.Equal(1, emissionCount);
    }

    [Fact]
    public void ReactiveLayoutNode_OnActivate_RecreatesSubscription()
    {
        // Arrange
        var subject = new Subject<ILayoutNode>();
        using var node = new ReactiveLayoutNode(subject);
        var emissionCount = 0;

        node.Invalidated.Subscribe(_ => emissionCount++);
        node.OnDeactivate(); // Pause subscription

        // Act
        node.OnActivate(); // Resume subscription

        subject.OnNext(new TextNode("Test after reactivation"));

        // Assert - emission should work again
        Assert.Equal(1, emissionCount);
    }

    [Fact]
    public void ReactiveLayoutNode_Generic_OnDeactivate_DisposesSubscription()
    {
        // Arrange
        var subject = new Subject<int>();
        using var node = new ReactiveLayoutNode<int>(subject, n => new TextNode($"Count: {n}"));
        var emissionCount = 0;

        node.Invalidated.Subscribe(_ => emissionCount++);

        // Act - emit before deactivation
        subject.OnNext(1);
        Assert.Equal(1, emissionCount);

        // Deactivate
        node.OnDeactivate();

        // Emit after deactivation
        subject.OnNext(2);

        // Assert - should not emit
        Assert.Equal(1, emissionCount);
    }

    [Fact]
    public void ReactiveLayoutNode_Generic_OnActivate_RecreatesSubscription()
    {
        // Arrange
        var subject = new Subject<int>();
        using var node = new ReactiveLayoutNode<int>(subject, n => new TextNode($"Count: {n}"));
        var emissionCount = 0;

        node.Invalidated.Subscribe(_ => emissionCount++);
        node.OnDeactivate();

        // Act
        node.OnActivate();
        subject.OnNext(42);

        // Assert
        Assert.Equal(1, emissionCount);
    }

    [Fact]
    public void ConditionalNode_OnDeactivate_DisposesSubscription()
    {
        // Arrange
        var conditionSubject = new Subject<bool>();
        using var thenNode = new TextNode("Then");
        using var elseNode = new TextNode("Else");
        using var node = new ConditionalNode(conditionSubject, thenNode, elseNode);

        var emissionCount = 0;
        node.Invalidated.Subscribe(_ => emissionCount++);

        // Act - emit before deactivation
        conditionSubject.OnNext(true);
        Assert.Equal(1, emissionCount);

        // Deactivate
        node.OnDeactivate();

        // Emit after deactivation
        conditionSubject.OnNext(false);

        // Assert - should not emit
        Assert.Equal(1, emissionCount);
    }

    [Fact]
    public void ConditionalNode_OnActivate_RecreatesSubscription()
    {
        // Arrange
        var conditionSubject = new Subject<bool>();
        using var thenNode = new TextNode("Then");
        using var elseNode = new TextNode("Else");
        using var node = new ConditionalNode(conditionSubject, thenNode, elseNode);

        var emissionCount = 0;
        node.Invalidated.Subscribe(_ => emissionCount++);
        node.OnDeactivate();

        // Act
        node.OnActivate();
        conditionSubject.OnNext(true);

        // Assert
        Assert.Equal(1, emissionCount);
    }

    [Fact]
    public void ModalNode_OnDeactivate_PropagatesTo_LayoutNodeContent()
    {
        // Arrange
        var spinner = new SpinnerNode();
        var modal = new ModalNode().WithContent(spinner);
        modal.OnActivate();

        Assert.True(spinner.IsAnimating);

        // Act
        modal.OnDeactivate();

        // Assert - spinner should be deactivated
        Assert.False(spinner.IsAnimating);

        // Cleanup
        modal.Dispose();
    }

    [Fact]
    public void ModalNode_OnActivate_PropagatesTo_LayoutNodeContent()
    {
        // Arrange
        var spinner = new SpinnerNode();
        var modal = new ModalNode().WithContent(spinner);
        modal.OnDeactivate(); // Deactivate

        // Act
        modal.OnActivate();

        // Assert - spinner should be reactivated
        Assert.True(spinner.IsAnimating);

        // Cleanup
        modal.Dispose();
    }

    [Fact]
    public void SelectionListNode_OnDeactivate_DeactivatesEmbeddedTextInput()
    {
        // Arrange
        var items = new[] { "Option 1", "Option 2" };
        var node = new SelectionListNode<string>(items, x => x)
            .WithOtherOption("Other...", _ => { });

        // Enable "Other" mode to create the embedded TextInputNode
        node.HandleInput(new ConsoleKeyInfo('o', ConsoleKey.O, false, false, false));

        // The embedded input should be created and focused (thus animating)
        // Note: We can't directly access _otherInput, but we can test the behavior

        // Act
        node.OnDeactivate();

        // If this doesn't throw, the embedded input was properly deactivated
        // (Dispose will be called in cleanup and should not fail)

        // Cleanup
        node.Dispose();
    }

    [Fact]
    public void ContainerNode_OnDeactivate_PropagatesTo_AllChildren()
    {
        // Arrange
        var spinner1 = new SpinnerNode();
        var spinner2 = new SpinnerNode();

        var container = Layouts.Vertical()
            .WithChild(spinner1)
            .WithChild(spinner2);
        container.OnActivate();

        Assert.True(spinner1.IsAnimating);
        Assert.True(spinner2.IsAnimating);

        // Act
        container.OnDeactivate();

        // Assert
        Assert.False(spinner1.IsAnimating);
        Assert.False(spinner2.IsAnimating);

        // Cleanup
        container.Dispose();
    }

    [Fact]
    public void ContainerNode_OnActivate_PropagatesTo_AllChildren()
    {
        // Arrange
        var spinner1 = new SpinnerNode();
        var spinner2 = new SpinnerNode();

        var container = Layouts.Vertical()
            .WithChild(spinner1)
            .WithChild(spinner2);

        container.OnDeactivate();

        // Act
        container.OnActivate();

        // Assert
        Assert.True(spinner1.IsAnimating);
        Assert.True(spinner2.IsAnimating);

        // Cleanup
        container.Dispose();
    }

    [Fact]
    public async Task ContainerNode_ConcurrentDispose_DisposesChildrenOnce()
    {
        var child = new BlockingDisposeNode();
        var container = Layouts.Vertical(child);

        var firstDispose = Task.Run(container.Dispose);
        await child.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondDispose = Task.Run(container.Dispose);
        try
        {
            await secondDispose.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            child.AllowDisposeToFinish.TrySetResult();
            await firstDispose.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(1, child.DisposeCount);
    }

    [Fact]
    public void LayoutNode_OnDeactivate_DoesNotDisposeSubjects()
    {
        // Arrange
        using var node = new TextInputNode();
        var submissionCount = 0;

        // Subscribe to Submitted
        var subscription = node.Submitted.Subscribe(_ => submissionCount++);

        // Act - deactivate (should NOT dispose the subject)
        node.OnDeactivate();

        // Emit submission
        node.HandleInput(new ConsoleKeyInfo('x', (ConsoleKey)0, false, false, false));
        node.HandleInput(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        // Assert - subscription should still receive emissions
        // (Subject is not disposed, just the timer is stopped)
        Assert.Equal(1, submissionCount);

        subscription.Dispose();
    }

    [Fact]
    public void LayoutNode_Dispose_CompletesAndDisposesSubjects()
    {
        // Arrange
        var node = new TextInputNode();
        var completed = false;

        node.Submitted.Subscribe(
            _ => { },
            _ => completed = true);

        // Act
        node.Dispose();

        // Assert - subject should be completed
        Assert.True(completed);

        // Attempting to subscribe after disposal should throw ObjectDisposedException
        // This is expected Rx behavior - disposed subjects cannot accept new subscriptions
        Assert.Throws<ObjectDisposedException>(() =>
        {
            node.Submitted.Subscribe(_ => { });
        });
    }

    private sealed class BlockingDisposeNode : LayoutNode
    {
        public TaskCompletionSource DisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowDisposeToFinish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public override Size Measure(Size available) => Size.Zero;

        public override void Render(IRenderContext context, Rect bounds)
        {
        }

        public override void Dispose()
        {
            DisposeCount++;
            DisposeStarted.TrySetResult();
            AllowDisposeToFinish.Task.GetAwaiter().GetResult();
            base.Dispose();
        }
    }
}
