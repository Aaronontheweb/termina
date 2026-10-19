// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using R3;
using Termina.Layout;

namespace Termina.Input;

/// <summary>
/// Manages keyboard focus for interactive components.
/// </summary>
/// <remarks>
/// The focus manager uses a stack-based model to support nested modals:
/// when a modal opens, it pushes itself onto the stack and captures all input.
/// When it closes, it pops itself off and focus returns to the previous component.
/// </remarks>
public interface IFocusManager
{
    /// <summary>
    /// Observable that emits when focus changes.
    /// </summary>
    Observable<IFocusable?> FocusChanged { get; }

    /// <summary>
    /// Gets the currently focused component, or null if nothing has focus.
    /// </summary>
    IFocusable? CurrentFocus { get; }

    /// <summary>
    /// Push a focusable onto the focus stack.
    /// </summary>
    /// <remarks>
    /// Use this for modals and overlays that need to capture all input.
    /// The pushed component becomes the new focus and will receive all input
    /// until it is popped.
    /// </remarks>
    /// <param name="focusable">The component to push onto the focus stack.</param>
    void PushFocus(IFocusable focusable);

    /// <summary>
    /// Pop the top focusable from the focus stack.
    /// </summary>
    /// <remarks>
    /// Call this when closing a modal to return focus to the previous component.
    /// </remarks>
    void PopFocus();

    /// <summary>
    /// Set focus to a specific component, replacing the current focus.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="PushFocus"/>, this replaces the current focus rather than
    /// stacking on top of it. Use for navigating between controls.
    /// </remarks>
    /// <param name="focusable">The component to focus.</param>
    void SetFocus(IFocusable focusable);

    /// <summary>
    /// Clear all focus, leaving no component focused.
    /// </summary>
    void ClearFocus();

    /// <summary>
    /// Route keyboard input to the currently focused component.
    /// </summary>
    /// <param name="key">The key info to route.</param>
    /// <returns>True if the input was consumed, false if no component handled it.</returns>
    bool RouteInput(ConsoleKeyInfo key);

    /// <summary>
    /// Route a keyboard input event, including the text that the key produces, to the currently focused component.
    /// </summary>
    /// <remarks>
    /// The default implementation calls <see cref="RouteInput(ConsoleKeyInfo)"/> with <see cref="KeyPressed.KeyInfo"/>.
    /// </remarks>
    /// <param name="input">The key event to route.</param>
    /// <returns>True if the input was consumed, false if no component handled it.</returns>
    bool RouteInput(KeyPressed input) => RouteInput(input.KeyInfo);

    /// <summary>
    /// Depth-first tree walk collecting all focusable nodes where <see cref="IFocusable.CanFocus"/> is true.
    /// </summary>
    /// <param name="root">The root of the layout tree to walk.</param>
    /// <returns>Focusable nodes in depth-first order.</returns>
    IReadOnlyList<IFocusable> CollectFocusables(ILayoutNode root);

    /// <summary>
    /// Cycle focus to the next (or previous) focusable in the list, with wrap-around.
    /// </summary>
    /// <param name="focusables">The ordered list of focusable nodes.</param>
    /// <param name="reverse">True to cycle backward, false to cycle forward.</param>
    void CycleFocus(IReadOnlyList<IFocusable> focusables, bool reverse = false);
}
