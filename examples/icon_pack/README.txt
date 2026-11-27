Harvest & Tackle icons  (an example mod: pictures only, no code)

What it changes
  Six items get new icons: Pumpkin (1) a ribbed orange pumpkin with a leaf, Leaf (2) a broad leaf with pale veins,
  Carrot (6) a carrot with its greens, Seaweed (10) a bunch of wavy kelp with air bladders, Hoe (100) a hoe with an
  iron blade, Basic Fishing Rod (101) a cane rod with a reel and a red and white float. Three koi get new icons:
  Blue Skin (160), Angry (161) and Emo Hair (69). You see them in the hotbar, the inventory, the "new item" popup
  and in item links in chat.

  If another plugin draws its own koi icons, its pictures win. Type /mods icon_pack in chat and you get, e.g.:
      art/koi/69.png: not shown, another plugin draws its own icon for this koi and wins
  _names.txt then lists the koi that can still get your icon ("shows only for: ...").

The art
  All nine icons are original pixel art, drawn from scratch by a script (examples/_tools/icon_pack.py in the
  ShorelandsMods source); no pixel is taken from the game. One style for all items: a 1 px ink outline (#211a26),
  light from the top left (a lit edge, a middle tone, a shaded edge per material), and a few hand-placed details
  (ribs, veins, glints). The koi are hand-placed pixel maps.
  For a variant, change the colours in MATERIALS or the shapes in examples/_tools/icon_pack.py and run
  python3 icon_pack.py (plain Python, no extra packages).

Make your own
  1. Copy this folder and rename the copy (lower case letters, digits and _ only, e.g. my_icons).
     Change name="..." in mod.xml.
  2. art/items/<item id>.png replaces that item's icon, art/koi/<koi id>.png that koi's icon.
     The ids, names and sizes are in the game folder: mods/_names.txt, sections [items] and [koi]
     (they appear there after you have been in the world once). Most icons are 24x24 pixels; the pet (50000) is 32x32.
  3. Draw at exactly that size, 1 pixel = 1 game pixel, and save as PNG with transparency. No smoothing or
     blur when you resize: the game shows your pixels as they are.
  4. Save the file while the game runs: the mod reloads by itself within about a second.
     /mods my_icons in chat says which files were applied and why any were not.
  Delete a file (or the whole folder) and the game's own icon comes back.
