Example  (the starter mod)

What it is
  The mod to copy when you make your own, like Noita's mods/example. It is on, but shows nothing until you
  switch on "Show the demo" (Menu > Settings > Mods > Example). Then it shows a text on the screen, writes
  every event in your chat, adds a line to item tooltips, and F4 opens a small window with your hotbar.
  /example in chat works either way: /example, /example notice, /example window.

What is in here
  mod.xml        the name and description the Mods page shows. Only name="..." is needed.
  settings.lua   one setting of each kind: on/off, a choice, two numbers, a text and a key.
  init.lua       every callback the game can call, each with a comment saying when it runs.
  files/         your own extra Lua, images and sounds (helpers.lua shows how to load one).
  art/           pictures that replace the game's, by file name (see art/README.txt).
  sounds/        sounds played when something happens, by file name (see sounds/README.txt).
  cues/          pictures and sounds for fishing moments (see cues/README.txt).
  A mod needs only mod.xml and whatever it uses: a picture pack has no init.lua at all.

Make your own
  1. Copy this folder inside mods/ and rename the copy: lower case letters, digits and _ only (my_mod).
  2. In the copy's mod.xml change name="Example" to your mod's name.
  3. Delete what you do not need, change the rest, save. The mod reloads by itself within a second or two.
  The guide is mods/_guide.html (double-click it). mods/_api.txt lists every function.
