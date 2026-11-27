Stream ticker  (an example Lua mod)

What it does
  Keeps two text files up to date while you play, for OBS or any streaming program that can show a
  text file:
    last_item.txt    the last item you got, e.g.  Last item: Carrot x2
    currency.txt     your currency, e.g.          Currency: 45,380
  Nothing shows in the game. The last item is remembered, so it is still there after a restart.

Where the files are
  In the game folder, under mods_data\stream_ticker\export\ , for a Steam install usually
    C:\Program Files (x86)\Steam\steamapps\common\Shorelands Playtest\mods_data\stream_ticker\export\
  (Steam > Shorelands Playtest > Manage > Browse local files shows the game folder.)
  last_item.txt is written when the game starts, currency.txt once you are in the world. Each changes
  as soon as your currency or your last item does.

Show them in OBS
  1. In Sources, click + and pick "Text (GDI+)". Name it.
  2. Tick "Read from file" and choose last_item.txt (or currency.txt) in the folder above.
  3. Pick a font and colour you like, then OK. OBS reads the file again whenever it changes.
  Repeat for the other file.

Settings (Options > Mods > Stream ticker)
  Text of last_item.txt    {name} is replaced by the item, {count} by how many you got.
                           Default: Last item: {name} x{count}
  Text of currency.txt     {currency} is replaced by your currency, {citb} by your CITB.
                           Default: Currency: {currency}
  Commas in numbers        45,380 or 45380
  A {word} the mod does not know stays in the text as it is, so a typo is easy to spot. Each text takes
  at most 100 characters; a longer one is cut.
  The settings are kept in BepInEx/config/mods.stream_ticker.cfg.

Change it
  init.lua is the whole mod, with comments. Some ideas:
    - A third file: ExportText( "zone", ZoneGet() ) in OnZoneChanged writes zone.txt.
    - Count items for a "catches today" file, like session_stats does.
  Save the file while the game runs and the mod reloads by itself within a second or two.
  mods/_api.txt lists every function and callback a mod can use.
