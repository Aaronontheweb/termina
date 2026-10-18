# wheel-routing.tape — Termina.Demo.WheelRouting: pointer-aware wheel routing (0.17.0).
# Two log panes; a scripted VirtualInputSource posts positioned wheel events that
# travel through the real routing path (MouseEvent -> MouseScrollEvent{X,Y} ->
# hit-test). The ruler + status line make "which pane is under the pointer"
# legible without sound or captions. The app animates itself once launched;
# the tape only starts it and waits.
Output docs/public/gallery/wheel-routing.gif

Source screenshots/tapes/_common.tape
Set Width 1400
Set Height 760

Hide
Type "dotnet run --no-build -c Release --project demos/Termina.Demo.WheelRouting -- --test"
Enter
# The app places the pointer over the LEFT pane ~4s after launch (startup +
# a 2.5s settle delay inside the script). Open the recording just before that
# so the LEFT ticks land on camera.
Sleep 4s
Show

# Measured choreography from launch: LEFT x5 ticks (5-7s) -> glide RIGHT
# (~8s) -> RIGHT x3 up, x2 down (9-11s) -> glide LEFT (~13s) -> final LEFT
# tick (~14s) -> settled comparison. The script then holds 10s and exits at
# ~24s — stop the recording at 17s so the GIF loops on the settled state and
# the alt-buffer restore never enters the frame.
Sleep 13s
