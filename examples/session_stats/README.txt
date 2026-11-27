Session stats  (an example Lua mod)

What it does
  A small panel at the top right shows, for this session (since you started the game):
    Session    how long you have been playing
    Earned     currency you earned; spending does not lower it
    Per hour   earned per hour of the session (shown after the first 5 minutes)
    Items      items you gained (catches, harvests, purchases, trades, rewards), and the last one
    All time   currency earned since you installed this mod (off by default)
  The figures carry on when you log out and back in, switch the mod off and on, or when it reloads. They
  start again when you restart the game.

Settings (Options > Mods > Session stats)
  Where it sits          top right, top left, right side, left side or bottom left. The panel finds a free
                         spot there, clear of the game's buttons and of other mods.
  Show ...               switch each line on or off. With every line off, the panel hides.
  You can also drag the panel with the mouse; it stays where you put it. Right-click it to put it back.
  The settings are kept in BepInEx/config/mods.session_stats.cfg.

Change it
  init.lua is the whole mod, with comments. Some ideas:
    - Count spending too: in OnWalletChanged, add a "spent" figure when currency_change is below 0.
    - Show the panel only while fishing: OnActivityChanged( activity ) tells you what you are doing.
  Save the file while the game runs and the mod reloads by itself within a second or two.
  mods/_api.txt lists every function and callback a mod can use.
  The all-time total is saved in mods_data/session_stats/data.json, outside the mod folder, so a new
  version of the mod keeps it. The session figures are kept by the game until it closes.
