# inline-mode.tape — Termina.Demo.Inline: opt-in inline presentation mode (0.17.0).
# Story beats:
#   1. The app runs in a bounded live region — your shell, not the alternate screen.
#   2. Enter commits settled output ABOVE the live region; the live region stays put.
#   3. R mutates only the live region — committed lines don't move.
#   4. Ctrl+Q exits; committed blocks and the shell prompt remain in scrollback.
Output docs/public/gallery/inline-mode.gif

Source screenshots/tapes/_common.tape
Set Height 620

Hide
Type "dotnet run --no-build -c Release --project demos/Termina.Demo.Inline"
Enter
Sleep 6s
Show

# Commit three stable blocks above the live region.
Enter
Sleep 1600ms
Enter
Sleep 1600ms
Enter
Sleep 1600ms

# Live-region-only updates: revision ticks, committed blocks stay frozen.
Type "R"
Sleep 900ms
Type "R"
Sleep 900ms
Type "R"
Sleep 1500ms

# Exit — settled output remains in the primary buffer, prompt comes back.
Ctrl+Q
Sleep 1200ms

# Prove we're in a normal shell with the app's output still on screen.
Type "echo 'settled output is yours to keep'"
Enter
Sleep 2500ms
