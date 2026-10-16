# unicode-cjk.tape — Termina.Demo Unicode/CJK smoke page.
# Static CJK, Japanese, Korean, and NetClaw-style chat text rendering.
Output docs/public/gallery/unicode-cjk.gif

Source screenshots/tapes/_common.tape
Set Width 1200
Set Height 760

Hide
Type "dotnet run --no-build -c Release --project demos/Termina.Demo"
Enter
Sleep 5s
Show

Sleep 1s
Type "u"
Sleep 2s
Screenshot docs/public/gallery/unicode-cjk.png
Sleep 4s

Hide
Type "q"
Sleep 1s
