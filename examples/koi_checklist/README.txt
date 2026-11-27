Koi checklist  (an example Lua mod)

What it does
  Press K (or type /koi in chat) for a window that lists every koi in the game, about 1,800, with its
  picture, number, name, colour and whether you have it:
    #431  Heart Glasses  ·  Aqua  ·  owned, 7 in bags          in gold
    #2  Cartman Beanie  ·  Blue  ·  missing                    in grey
  Above the list:
    You have 434 of 1,816 koi; 1,382 missing.
    Carried: 20.  In your statue: 492.
    Blue 151/754   Dark blue 34/80   ...                       how many of each colour you have
  Buttons: All, Owned, Missing (the one in use looks pressed). By colour shows one colour at a time and
  then says which ("Colour: Blue", "Colour: Dark blue", ... and then every colour again); All colours goes
  back to every colour.
  Search: type part of a name or a number ("glasses", "431"). With several words, a koi must match all
  of them. "owned", "missing" and colours work too ("hair purple missing"), but a colour word also finds
  koi with that word in their name ("Red Beanie" is a Blue koi): use the colour button for exactly one
  colour. While you search, the last line above the list counts the matches ("12 of 1,816 shown.").
  The list keeps up while it is open: buy, catch, store or take out a koi and its line changes.
  K, /koi, Escape or the X closes the window. You can drag it by its plate.

  A koi's tooltip (hover it in your bags, in your statue or on your side of a trade) gets one more line,
  after the mod's name in violet:
    [koi_checklist] In your collection: 2 in bags, 1 in statue
  or
    Missing from your collection

What counts as "yours"
  A koi is yours when it is in one of your bags (the koi bag, and the hotbar, where a koi you just
  bought often lands) or stored in your koi statue. The game tells your own client exactly which koi
  those are, one by one, so the marks and the numbers ("2 in statue") are exact.
  What the mod cannot know:
    - koi you put up for sale on a market stall: they are in neither place, so they show as missing;
    - koi you had before and gave away, sold or traded: the game keeps no list of koi you ever had,
      so the checklist is about what you have now.
  The colour is the koi's fish colour, one of the game's eight. Some colours have a single koi; pink
  has none at the moment, so it is left out.

Settings (Options > Mods > Koi checklist)
  Open the checklist     the key; click it on the Mods page and press another key to change it
  Start on missing koi   the window opens showing only the koi you do not have yet
  Line in koi tooltips   on or off
  Changes take effect at once. The settings are kept in BepInEx/config/mods.koi_checklist.cfg.

Change it
  init.lua is the whole mod, with comments. Some ideas:
    - Count only one colour in the header: skip the others in show().
    - Make a koi you have three or more of stand out: give its row another color in write_row().
  Save the file while the game runs and the mod reloads by itself within a second or two; an open
  window stays open, on the same filter.
  mods/_api.txt lists every function and callback a mod can use.
