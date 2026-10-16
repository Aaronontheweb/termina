// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Input;
using Termina.Layout;
using Termina.Reactive;
using Termina.Rendering;

namespace Termina.Tests.Reactive;

/// <summary>
/// Tests for <see cref="ReactivePage{TViewModel}.InvalidateLayout"/>. The
/// invalidation hook lets consumers force a <see cref="ReactivePage{T}.BuildLayout"/>
/// rebuild when external state captured at build time has changed (e.g.,
/// terminal dimensions baked into a <c>HeightAuto</c> constraint that
/// needs to track resize).
/// </summary>
public class ReactivePageInvalidateLayoutTests
{
    [Fact]
    public void InvalidateLayout_TriggersBuildLayoutAgain()
    {
        var page = new CountingPage();
        page.BindForTest(new EmptyViewModel());

        page.OnNavigatedTo();
        Assert.Equal(1, page.BuildLayoutCallCount);

        page.CallInvalidateLayout();

        Assert.Equal(2, page.BuildLayoutCallCount);
    }

    [Fact]
    public void InvalidateLayout_ReplacesLayoutRoot()
    {
        var page = new CountingPage();
        page.BindForTest(new EmptyViewModel());
        page.OnNavigatedTo();

        var firstRoot = ((Pages.IBindablePage)page).LayoutRoot;
        Assert.NotNull(firstRoot);

        page.CallInvalidateLayout();

        var secondRoot = ((Pages.IBindablePage)page).LayoutRoot;
        Assert.NotNull(secondRoot);
        Assert.NotSame(firstRoot, secondRoot);
    }

    [Fact]
    public void InvalidateLayout_PreservesUserSubscriptions()
    {
        var page = new CountingPage();
        page.BindForTest(new EmptyViewModel());
        page.OnNavigatedTo();

        // Add a user subscription tracking some external signal.
        var subject = new Subject<int>();
        var received = new List<int>();
        page.AddUserSubscription(subject.Subscribe(received.Add));

        subject.OnNext(1);
        page.CallInvalidateLayout();
        subject.OnNext(2);

        // Subscriptions added to the protected Subscriptions disposable are
        // only cleared by the framework on navigation away or disposal; an
        // InvalidateLayout must not disturb them.
        Assert.Equal(new[] { 1, 2 }, received);
    }

    [Fact]
    public void InvalidateLayout_PreservesKeyBindings()
    {
        var page = new CountingPage();
        page.BindForTest(new EmptyViewModel());
        page.OnNavigatedTo();

        page.KeyBindings.Register(ConsoleKey.Escape, () => { });
        page.KeyBindings.Register(ConsoleKey.Tab, () => { });
        Assert.Equal(2, page.KeyBindings.Count);

        page.CallInvalidateLayout();

        Assert.Equal(2, page.KeyBindings.Count);
    }

    [Fact]
    public void InvalidateLayout_SeesUpdatedExternalStateInBuildLayout()
    {
        // Concrete consumer scenario: BuildLayout's output reflects a value
        // that changes between calls (e.g., terminal width). After
        // InvalidateLayout, BuildLayout's next invocation must see the
        // current value, not the captured-at-first-render value.
        var page = new ExternalStatePage();
        page.BindForTest(new EmptyViewModel());

        page.ExternalValue = 10;
        page.OnNavigatedTo();
        Assert.Equal(10, page.LastObservedValue);

        page.ExternalValue = 42;
        page.CallInvalidateLayout();

        Assert.Equal(42, page.LastObservedValue);
    }

    // ----- Lifecycle guard tests -----

    [Fact]
    public void InvalidateLayout_BeforeOnNavigatedTo_IsNoOp()
    {
        // Calling InvalidateLayout before the first navigation must not
        // build / activate / subscribe — otherwise the framework's eventual
        // OnNavigatedTo would double-subscribe and double-activate.
        var page = new CountingPage();
        page.BindForTest(new EmptyViewModel());

        page.CallInvalidateLayout();
        Assert.Equal(0, page.BuildLayoutCallCount);

        page.OnNavigatedTo();
        Assert.Equal(1, page.BuildLayoutCallCount);
    }

    [Fact]
    public void InvalidateLayout_AfterOnNavigatingFrom_IsNoOp()
    {
        // Between OnNavigatingFrom and the next OnNavigatedTo the page is
        // "cached inactive" — _layoutRoot is preserved but deactivated.
        // InvalidateLayout here must not resurrect the page.
        var page = new CountingPage();
        page.BindForTest(new EmptyViewModel());
        page.OnNavigatedTo();
        Assert.Equal(1, page.BuildLayoutCallCount);

        page.OnNavigatingFrom();

        page.CallInvalidateLayout();
        Assert.Equal(1, page.BuildLayoutCallCount); // no rebuild while inactive

        // Next navigation correctly reactivates the cached layout (no new build).
        page.OnNavigatedTo();
        Assert.Equal(1, page.BuildLayoutCallCount);
    }

    [Fact]
    public void InvalidateLayout_AfterDispose_Throws()
    {
        var page = new CountingPage();
        page.BindForTest(new EmptyViewModel());
        page.OnNavigatedTo();
        page.Dispose();

        Assert.Throws<ObjectDisposedException>(() => page.CallInvalidateLayout());
    }

    [Fact]
    public void OnNavigatedTo_AfterDispose_Throws()
    {
        var page = new CountingPage();
        page.BindForTest(new EmptyViewModel());
        page.OnNavigatedTo();
        page.Dispose();

        Assert.Throws<ObjectDisposedException>(() => page.OnNavigatedTo());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var page = new CountingPage();
        page.BindForTest(new EmptyViewModel());
        page.OnNavigatedTo();

        page.Dispose();
        page.Dispose(); // must not throw
    }

    [Fact]
    public void InvalidateLayout_ReEntrant_Throws()
    {
        // BuildLayout calling InvalidateLayout would otherwise stack-overflow
        // or leave _layoutRoot pointing at a leaked sub-tree.
        var page = new ReEntrantPage();
        page.BindForTest(new EmptyViewModel());
        page.OnNavigatedTo();

        page.ReEnterOnNextBuild = true;

        Assert.Throws<InvalidOperationException>(() => page.CallInvalidateLayout());
    }

    [Fact]
    public void InvalidateLayout_NullBuildLayout_Throws()
    {
        var page = new NullBuildPage();
        page.BindForTest(new EmptyViewModel());

        // First navigation: BuildLayout returns a real node so the page activates.
        page.ReturnNullNext = false;
        page.OnNavigatedTo();

        // Now make BuildLayout return null and invalidate — should throw,
        // leaving the existing layout intact.
        page.ReturnNullNext = true;
        var firstRoot = ((Pages.IBindablePage)page).LayoutRoot;

        Assert.Throws<InvalidOperationException>(() => page.CallInvalidateLayout());

        // Layout root unchanged after the failed rebuild — build-then-swap
        // exception safety.
        Assert.Same(firstRoot, ((Pages.IBindablePage)page).LayoutRoot);
    }

    [Fact]
    public void InvalidateLayout_BuildLayoutThrows_LeavesLayoutIntact()
    {
        var page = new ThrowingBuildPage();
        page.BindForTest(new EmptyViewModel());

        page.ShouldThrowNext = false;
        page.OnNavigatedTo();
        var firstRoot = ((Pages.IBindablePage)page).LayoutRoot;
        Assert.NotNull(firstRoot);

        page.ShouldThrowNext = true;
        Assert.Throws<InvalidOperationException>(() => page.CallInvalidateLayout());

        // The page is still operational on the old tree.
        Assert.Same(firstRoot, ((Pages.IBindablePage)page).LayoutRoot);

        // Subsequent successful invalidate works.
        page.ShouldThrowNext = false;
        page.CallInvalidateLayout();
        Assert.NotSame(firstRoot, ((Pages.IBindablePage)page).LayoutRoot);
    }

    [Fact]
    public void InvalidateLayout_RequestsRedraw()
    {
        var vm = new TrackingViewModel();
        var page = new CountingPage();
        page.BindForTest(vm);
        page.OnNavigatedTo();

        var redrawsBefore = vm.RedrawRequestCount;
        page.CallInvalidateLayout();

        // The new tree won't necessarily emit Invalidated synchronously, so
        // InvalidateLayout must explicitly request a redraw — otherwise the
        // user-visible frame stays stale until the next unrelated reactive
        // emission.
        Assert.True(vm.RedrawRequestCount > redrawsBefore,
            $"Expected RequestRedraw to be called by InvalidateLayout. " +
            $"Before: {redrawsBefore}, After: {vm.RedrawRequestCount}");
    }

    // ----- IInvalidatingNode wiring tests -----

    [Fact]
    public void InvalidateLayout_OldInvalidatingNode_DoesNotDriveRedraws()
    {
        // After invalidate, the OLD layout's Invalidated emissions must NOT
        // trigger RequestRedraw — otherwise _layoutSubscriptions accumulates
        // dead subscriptions and every event fires N times.
        var vm = new TrackingViewModel();
        var page = new InvalidatingNodePage();
        page.BindForTest(vm);
        page.OnNavigatedTo();

        var oldNode = page.LastBuilt!;
        page.CallInvalidateLayout();
        var redrawsAfterInvalidate = vm.RedrawRequestCount;

        // Fire the old tree's Invalidated — no redraw should follow.
        oldNode.RaiseInvalidated();
        Assert.Equal(redrawsAfterInvalidate, vm.RedrawRequestCount);
    }

    [Fact]
    public void InvalidateLayout_NewInvalidatingNode_DrivesRedraws()
    {
        var vm = new TrackingViewModel();
        var page = new InvalidatingNodePage();
        page.BindForTest(vm);
        page.OnNavigatedTo();

        page.CallInvalidateLayout();
        var newNode = page.LastBuilt!;
        var baselineRedraws = vm.RedrawRequestCount;

        // Fire the new tree's Invalidated — exactly one redraw must follow.
        newNode.RaiseInvalidated();
        Assert.Equal(baselineRedraws + 1, vm.RedrawRequestCount);
    }

    [Fact]
    public void InvalidateLayout_RepeatedCalls_DoNotAccumulateSubscriptions()
    {
        // Multiple invalidations in a row must not pile up subscriptions on
        // the live tree (each invalidate replaces all _layoutSubscriptions).
        var vm = new TrackingViewModel();
        var page = new InvalidatingNodePage();
        page.BindForTest(vm);
        page.OnNavigatedTo();

        page.CallInvalidateLayout();
        page.CallInvalidateLayout();
        page.CallInvalidateLayout();

        var liveNode = page.LastBuilt!;
        var baseline = vm.RedrawRequestCount;
        liveNode.RaiseInvalidated();

        Assert.Equal(baseline + 1, vm.RedrawRequestCount);
    }

    [Fact]
    public void InvalidateLayout_AbandonedWrapper_DoesNotReceiveReusedChildInvalidation()
    {
        var page = new ReusedChildInPanelPage();
        page.BindForTest(new EmptyViewModel());
        page.OnNavigatedTo();

        var oldRoot = Assert.IsType<PanelNode>(((Pages.IBindablePage)page).LayoutRoot);
        var oldRootInvalidations = 0;
        oldRoot.Invalidated.Subscribe(_ => oldRootInvalidations++);

        page.CallInvalidateLayout();

        var newRoot = Assert.IsType<PanelNode>(((Pages.IBindablePage)page).LayoutRoot);
        var newRootInvalidations = 0;
        newRoot.Invalidated.Subscribe(_ => newRootInvalidations++);

        page.SharedChild.RaiseInvalidated();

        Assert.Equal(0, oldRootInvalidations);
        Assert.Equal(1, newRootInvalidations);
    }

    [Fact]
    public void InvalidateLayout_ReusedFocusedNode_RemainsActive()
    {
        using var focusManager = new FocusManager();

        var page = new ReusedInputPage();
        page.BindForTest(new EmptyViewModel());
        page.WireFocusForTest(focusManager);
        page.OnNavigatedTo();

        focusManager.SetFocus(page.Input);
        Assert.True(page.Input.IsAnimating);

        page.CallInvalidateLayout();

        Assert.True(page.Input.IsAnimating);
        Assert.True(page.Input.HasFocus);

        var handled = focusManager.RouteInput(new ConsoleKeyInfo('x', (ConsoleKey)0, false, false, false));

        Assert.True(handled);
        Assert.Equal("x", page.Input.Text);
    }

    [Fact]
    public void InvalidateLayout_ReplacedFocusedNode_ClearsManualFocus()
    {
        using var focusManager = new FocusManager();

        var page = new ReplacingInputPage();
        page.BindForTest(new EmptyViewModel());
        page.WireFocusForTest(focusManager);
        page.OnNavigatedTo();

        var firstInput = page.CurrentInput;
        focusManager.SetFocus(firstInput);

        page.CallInvalidateLayout();

        var secondInput = page.CurrentInput;
        Assert.NotSame(firstInput, secondInput);
        Assert.Null(focusManager.CurrentFocus);

        var handled = focusManager.RouteInput(new ConsoleKeyInfo('x', (ConsoleKey)0, false, false, false));

        Assert.False(handled);
        Assert.Equal(string.Empty, firstInput.Text);
        Assert.Equal(string.Empty, secondInput.Text);
    }

    [Fact]
    public void ChildInvalidation_ReplacedFocusedNode_ClearsManualFocusBeforeDisposal()
    {
        using var focusManager = new FocusManager();

        var page = new KeyedInputPage();
        page.BindForTest(new EmptyViewModel());
        page.WireFocusForTest(focusManager);
        page.OnNavigatedTo();
        page.MeasureLayout();

        var firstInput = page.CurrentInput;
        focusManager.SetFocus(firstInput);

        page.ChangeKey(1);
        var secondInput = page.CurrentInput;
        Assert.NotSame(firstInput, secondInput);
        Assert.Null(focusManager.CurrentFocus);

        page.MeasureLayout();
        var handled = focusManager.RouteInput(new ConsoleKeyInfo('x', (ConsoleKey)0, false, false, false));

        Assert.False(handled);
        Assert.Equal(string.Empty, firstInput.Text);
        Assert.Equal(string.Empty, secondInput.Text);
    }

    private sealed class CountingPage : ReactivePage<EmptyViewModel>
    {
        public int BuildLayoutCallCount { get; private set; }

        public override ILayoutNode BuildLayout()
        {
            BuildLayoutCallCount++;
            return new TextNode("test");
        }

        public void BindForTest(ReactiveViewModel vm) => Bind((EmptyViewModel)vm);
        public void CallInvalidateLayout() => InvalidateLayout();
        public void AddUserSubscription(IDisposable d) => Subscriptions.Add(d);
        public new PageKeyBindings KeyBindings => base.KeyBindings;
    }

    private sealed class ExternalStatePage : ReactivePage<EmptyViewModel>
    {
        public int ExternalValue { get; set; }
        public int LastObservedValue { get; private set; }

        public override ILayoutNode BuildLayout()
        {
            LastObservedValue = ExternalValue;
            return new TextNode(ExternalValue.ToString());
        }

        public void BindForTest(EmptyViewModel vm) => Bind(vm);
        public void CallInvalidateLayout() => InvalidateLayout();
    }

    private sealed class ReEntrantPage : ReactivePage<EmptyViewModel>
    {
        public bool ReEnterOnNextBuild { get; set; }

        public override ILayoutNode BuildLayout()
        {
            if (ReEnterOnNextBuild)
            {
                ReEnterOnNextBuild = false; // arm only once to avoid runaway recursion if guard breaks
                InvalidateLayout(); // must throw
            }
            return new TextNode("test");
        }

        public void BindForTest(EmptyViewModel vm) => Bind(vm);
        public void CallInvalidateLayout() => InvalidateLayout();
    }

    private sealed class NullBuildPage : ReactivePage<EmptyViewModel>
    {
        public bool ReturnNullNext { get; set; }

        public override ILayoutNode BuildLayout()
        {
            // Returning null is illegal per the contract; deliberately suppress
            // the non-nullable warning for the test.
            return ReturnNullNext ? null! : new TextNode("test");
        }

        public void BindForTest(EmptyViewModel vm) => Bind(vm);
        public void CallInvalidateLayout() => InvalidateLayout();
    }

    private sealed class ThrowingBuildPage : ReactivePage<EmptyViewModel>
    {
        public bool ShouldThrowNext { get; set; }

        public override ILayoutNode BuildLayout()
        {
            if (ShouldThrowNext)
                throw new InvalidOperationException("simulated build failure");
            return new TextNode("test");
        }

        public void BindForTest(EmptyViewModel vm) => Bind(vm);
        public void CallInvalidateLayout() => InvalidateLayout();
    }

    private sealed class InvalidatingNodePage : ReactivePage<TrackingViewModel>
    {
        public TestInvalidatingNode? LastBuilt { get; private set; }

        public override ILayoutNode BuildLayout()
        {
            LastBuilt = new TestInvalidatingNode();
            return LastBuilt;
        }

        public void BindForTest(TrackingViewModel vm) => Bind(vm);
        public void CallInvalidateLayout() => InvalidateLayout();
    }

    private sealed class ReusedChildInPanelPage : ReactivePage<EmptyViewModel>
    {
        public TestInvalidatingNode SharedChild { get; } = new();

        public override ILayoutNode BuildLayout() => new PanelNode().WithContent(SharedChild);

        public void BindForTest(EmptyViewModel vm) => Bind(vm);
        public void CallInvalidateLayout() => InvalidateLayout();
    }

    private sealed class ReusedInputPage : ReactivePage<EmptyViewModel>
    {
        public TextInputNode Input { get; } = new();

        public override ILayoutNode BuildLayout() => new PanelNode().WithContent(Input);

        public void BindForTest(EmptyViewModel vm) => Bind(vm);
        public void CallInvalidateLayout() => InvalidateLayout();
        public void WireFocusForTest(IFocusManager focusManager) => ((Pages.IBindablePage)this).WireUpFocus(focusManager);
    }

    private sealed class ReplacingInputPage : ReactivePage<EmptyViewModel>
    {
        public TextInputNode CurrentInput { get; private set; } = null!;

        public override ILayoutNode BuildLayout()
        {
            CurrentInput = new TextInputNode();
            return CurrentInput;
        }

        public void BindForTest(EmptyViewModel vm) => Bind(vm);
        public void CallInvalidateLayout() => InvalidateLayout();
        public void WireFocusForTest(IFocusManager focusManager) => ((Pages.IBindablePage)this).WireUpFocus(focusManager);
    }

    private sealed class KeyedInputPage : ReactivePage<EmptyViewModel>
    {
        private KeyedDynamicLayoutNode<int> _layout = null!;
        private int _currentKey;

        public TextInputNode CurrentInput { get; private set; } = null!;

        public override ILayoutNode BuildLayout()
        {
            _layout = new KeyedDynamicLayoutNode<int>(
                () => _currentKey,
                _ => CurrentInput = new TextInputNode(),
                KeyedDynamicCachePolicy.EvictOnKeyChange);
            return _layout;
        }

        public void BindForTest(EmptyViewModel vm) => Bind(vm);
        public void WireFocusForTest(IFocusManager focusManager) => ((Pages.IBindablePage)this).WireUpFocus(focusManager);
        public void MeasureLayout() => _layout.Measure(new Size(80, 24));

        public void ChangeKey(int key)
        {
            _currentKey = key;
            _layout.Invalidate();
        }
    }

    private sealed class TestInvalidatingNode : LayoutNode, IInvalidatingNode
    {
        private readonly Subject<Unit> _invalidated = new();

        public Observable<Unit> Invalidated => _invalidated;

        public void RaiseInvalidated() => _invalidated.OnNext(Unit.Default);

        public override Size Measure(Size available) => new(0, 0);

        public override void Render(IRenderContext context, Rect bounds)
        {
            // No-op for tests.
        }
    }

    private class EmptyViewModel : ReactiveViewModel
    {
    }

    private sealed class TrackingViewModel : EmptyViewModel
    {
        public int RedrawRequestCount { get; private set; }

        public TrackingViewModel()
        {
            WireUp(
                navigate: _ => { },
                navigateWithParams: (_, _) => { },
                shutdown: () => { },
                requestRedraw: () => RedrawRequestCount++,
                input: Observable.Empty<IInputEvent>());
        }
    }
}
