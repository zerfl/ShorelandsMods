art/: pictures that replace the game's. No code needed.

  art/textures/<name>.png   replaces the game picture with that name. It must have the same size in pixels.
  art/items/<id>.png        replaces one item's icon (24x24 for most items).
  art/koi/<id>.png          replaces one koi's icon (24x24).

Where the names come from
  - In the game, press F2 (inspect mode) and point at something: it shows the name, the size and the file
    name to use. Click to save the game's own picture to mods/_originals/; copy it here and paint over it.
  - mods/_names.txt lists every name with its size: [textures], [items] and [koi].
  - /export <name> in chat saves one picture to mods/_originals/, /export all saves them all.

Save a PNG here while the game runs and it shows within a second. /mods <your mod> in chat says which files
were applied and why any were not. Delete the file and the game's own picture comes back.
