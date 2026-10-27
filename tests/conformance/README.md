# Terminal Conformance

This directory holds the first-pass terminal conformance harness for Termina.

Goals:

- verify that alternate-scroll does not regress native text selection
- verify that selected screen text can reach the terminal clipboard
- verify that terminal clipboard paste reaches the focused Termina input
- verify that wheel input remains distinct from keyboard arrows when raw input + kitty reporting are enabled
- verify that fallback / baseline terminal behavior still produces the expected setup sequences

The initial automation target is Linux on GitHub-hosted runners:

- plain PTY capture
- `tmux`
- `kitty` under `Xvfb`

Current Linux coverage:

- baseline PTY startup captures legacy mouse-tracking setup bytes
- DA1 injection writes a real Windows Terminal device-attributes reply into a live PTY and asserts it never reaches the focused text input
- kitty under `Xvfb` checks native selection, explicit copy to clipboard, clipboard paste into focused input, alternate-scroll setup, kitty keyboard setup, and no mouse-tracking setup
- tmux checks raw key routing, tmux mouse on/off metadata, and bracketed paste delivery into focused input
- kitty + tmux checks native selection, explicit copy to clipboard, clipboard paste into focused input, and no mouse-tracking setup while passing through tmux

The suite intentionally does **not** merge into the main validation workflow yet. It is designed to be iterated on in a dedicated branch until the runner behavior is stable.
