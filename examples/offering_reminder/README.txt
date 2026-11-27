Offering reminder  (an example Lua mod)

What it does
  Offerings run in rounds on a clock everyone shares; the game shows the time left near the top left.
  This mod adds, at the top left corner:
    Offering round: 7:12 left       the time left in the running round
    Day, clear                      day, dusk, night or dawn, and whether it rains
  and two notices at the top of the screen:
    "A new offering round started: 10:00 to offer."              when a round starts
    "The offering round ends in 30 s. Last chance to offer!"     shortly before it ends
  While you have an offering in the round (the game shows its own timer in blue), the countdown turns
  gold ("Your offering: results in 2:41") and the reminder tells you when your results come. In the last
  seconds the countdown turns orange.

Settings (Options > Mods > Offering reminder)
  Show the offering countdown          on or off
  Show weather and daylight            on or off
  Notice when a round starts           on or off
  Reminder before the end (seconds)    0 to 120; 0 switches the reminder off
  Where it sits                        top left, top right, left side or right side
  The settings are kept in BepInEx/config/mods.offering_reminder.cfg.

Change it
  init.lua is the whole mod, with comments. Some ideas:
    - Play a sound with the reminder: put a short reminder.ogg in files/ and call
      SoundPlay( "files/reminder.ogg" ) next to the NoticeShow for the reminder.
    - Remind only while you have an offering in the round: check my_offering before NoticeShow.
  Save the file while the game runs and the mod reloads by itself within a second or two.
  mods/_api.txt lists every function and callback a mod can use.
