# Making mods for Shorelands

This guide shows you how to make mods for Shorelands: new pictures, sounds, and small Lua mods that add text,
windows and settings to the game. You need the game, a text editor and, for pictures, an image editor. You do not
need to be a programmer. If you have made a Noita mod, most of it will feel familiar; there is a section for you.

Start with [Your first mod in ten minutes](#first). Come back to the other sections when you need them.

Three files in the game's `mods` folder go with this guide:

- `_api.txt`: every function and callback a Lua mod can use, one line each. See [Reading _api.txt and _names.txt](#reference).
- `_names.txt`: every name a picture, sound or cue file can have.
- `example/`: the starter mod. Copy it to start your own.

[TOC]

## What mods can and cannot do {#fair}

A mod changes what you see and hear, and shows you your own stats. It can replace the game's pictures, play
sounds, show text and windows on your screen, add chat commands that only you use, and write text files for OBS.

A mod cannot press anything for you, and it cannot see fishing timing, market stalls or other players (a mod's
line in an item tooltip, `OnItemTooltip`, is asked only for your own items: never for another player's
inventory, a trade partner's offer or the offering results). That is on purpose: the game's developer allows client mods as long as they give nobody an unfair advantage. Because
mods are built so that they cannot do these things, any mod is safe to share and safe to install.

If you want more than this, you need a real plugin: a C# BepInEx plugin, which is a programmer's job and falls
under the developer's rule that plugins giving an unfair advantage must never be shared.

## Using mods {#using}

You can tell the mod host is installed when the game's settings have a **Mods** button.

### Where things are

The game folder is the folder Steam installed Shorelands Playtest in. To open it: in Steam, right-click
**Shorelands Playtest**, then **Manage > Browse local files**. Inside it:

| Folder or file | What it is |
|---|---|
| `mods/` | One folder per mod. The folder name is the mod's id. |
| `mods/_api.txt`, `mods/_names.txt` | The lists that go with this guide. The game writes them; do not edit them. |
| `mods/_originals/` | Pictures you saved from the game with inspect mode or `/export`. Not a mod. |
| `mods_data/<mod>/` | What a mod saved (its data) and the files it wrote for OBS (`export/`). |
| `BepInEx/config/mods.<mod>.cfg` | A mod's settings. |
| `BepInEx/LogOutput.log` | The log: what went wrong, in detail. |

This guide writes paths with `/`. Windows shows the same paths with `\`.

### Installing a mod someone gave you

1. Unzip it into `mods/`. You should end up with `mods/<mod>/mod.xml`. If you get `mods/<mod>/<mod>/mod.xml`,
   move the inner folder up one level.
2. That is all. The game picks the mod up within a second or two, even while you play. A chat line says
   `[<mod>] reloaded: loaded`.

To remove a mod, delete its folder. What it changed goes back to the game's own at once.

### The Mods page

Open it with the menu button at the top right of the screen, then **Settings**, then **Mods**. (At the login
screen: **OPTIONS**, then **Mods**.)

On the left is every mod in its load order, with a switch to turn it on or off. Click a mod to see, on the right:
who made it, where its folders are, its settings, and its files: which were **applied**, which are **waiting**,
**ignored** (with the reason) or **overridden by** another mod. At the bottom: **Inspect**, **Reload mods** and **BACK**.

- **Switch off** stops a mod until you switch it on again, also after a restart. Its pictures and sounds go back
  to the game's own. `/mods off <mod>` in chat does the same.
- **Settings** change at once; there is nothing to save.
- A mod marked **Failed** in red stopped because of an error. Select it to read why. See [When something goes wrong](#wrong).
- **Same key** next to a mod: one of its keys is also another mod's, inspect mode's or one of the game's, so one
  press does both. Select the mod: the line under its key setting says which. Give one of them another key
  (see [Choosing a key](#keys)).
- **Play** next to a sound or cue file plays it now. See [Sounds](#sounds) and [Cues](#cues).

### Typing commands {#commands}

The host's commands, and the ones mods add, are typed in chat:

1. Press **Enter**. The chat field opens.
2. Type the command with its slash, for example `/mods`, and press **Enter**.

A command runs on your computer only; nothing of it is sent to the server or other players. While you type, the
game's suggestion box lists the commands that start with what you typed (`/mo` shows `mods`). A command with a
typo gets the game's `Unknown or incomplete command`, and the host adds `Did you mean /mods?`.

**Long answers.** The chat window is small. Drag the speech bubble at its top left upwards to make the window
taller (the game remembers the height), and turn the mouse wheel over it to scroll back.

### Load order and "overridden by"

Mods load one after the other, in the order of the list on the Mods page. Until you change it, the list is
alphabetical by id. When you press **Load earlier** or **Load later**, the game saves the whole list as it is; from
then on those mods keep their places, and **mods you add later go after them**, in alphabetical order among
themselves. So a new mod goes near the end, but not always last. The page shows each mod's place, for example
`Load order 11 of 12`. When two mods replace the same picture or sound, **the later one wins**. The other one's file
then says *overridden by <the winner>*. Switch the winner off, or move the other one later, and the other one's
file shows instead.

## Your first mod in ten minutes {#first}

Pick a track. Each one ends with something you can see or hear in the game. Keep the game running while you
work: every time you save a file, the mod reloads by itself within a second or two.

> All three tracks start the same way: make a folder for your mod in `mods/`. Its name is the mod's id: lower
> case letters, digits and `_`, starting with a letter (`my_backpack`, not `My Backpack`).

### Track A: replace a picture {#first-art}

Here you draw your own backpack for the bottom right of the screen.

**1. Find its name.** In the world, press **F2**. A banner at the top says inspect mode is on. Point at the
backpack. A panel beside the pointer lists what is drawn there, the topmost first:

```
backpack_1
screen 32x32: art/textures/backpack_1.png
pointer
cursor 12x12: art/textures/pointer.png
```

The first entry is the backpack: its name, its size in pixels, and the file that would replace it. The cursor is
always last. Point at the edge of the backpack, where the plate behind it shows, to get the plate
(`inventory_bg_4`).

**2. Save the original as a reference.** Click. The panel says
`Saved mods/_originals/textures/backpack_1.png` and chat gives the full path. Press **Escape** to leave
inspect mode. This picture shows you the size and layout to work from; your mod will have your own drawing.

**3. Make the mod.** In `mods/`, make a folder `my_backpack`. In it, make a text file `mod.xml` with this line:

```xml sample=track_a file=mod.xml
<Mod name="My backpack" />
```

Only `name` is needed for the mod to load. `description`, `author`, `version` and `made_for_game` are for when you
share it; see [Sharing a mod](#share).

> **Windows hides file endings.** Notepad saves `mod.xml` as `mod.xml.txt` unless you set **Save as type** to
> **All files**. Turn on **View > Show > File name extensions** in File Explorer so you can see the real name.
> If you get it wrong, chat tells you: `skipped: mod.xml is missing; the file is called mod.xml.txt`.

**4. Draw your own.** Open `mods/_originals/textures/backpack_1.png` in an image editor (Paint.NET, GIMP,
Krita, Photopea, Aseprite) to see what you are replacing: 32x32 pixels, transparent around the bag. Make a new
picture of the same size with a transparent background and draw your backpack in it. You may put the original on a
layer underneath to line things up; leave that layer out when you save.

**5. Put it in.** In `my_backpack`, make the folder `art`, and in it the folder `textures`. Save your picture
there as PNG named `backpack_1.png`, so it is `mods/my_backpack/art/textures/backpack_1.png`. Keep the size (32x32)
and the transparent parts transparent. Within a second or two of saving, the backpack in the game changes.

> **Your own art only.** The pictures in `_originals` are the game's. Use them to see sizes and layout. Do not put
> them, or a recolour of them, in a mod you share.

**6. Check.** Type `/mods my_backpack` in chat (press **Enter**, type the line, press **Enter**; see
[Typing commands](#commands)). It says `applied: 1 file (art/textures/ 1)`. If it says something else, read
[Pictures](#art). If it says *overridden by* another mod, that mod replaces the same picture and loads after
yours. A new mod goes near the end of the list, but not always last (see [Load order](#using)): press **Load
later** on your mod on the Mods page until the file is applied.

To undo, delete the PNG or the whole folder.

**An item instead.** Items are pictures too. Press **F2** and point at the hoe in your hotbar. Inspect mode
lists, topmost first,

```
highlight_hotbar
screen 32x32: art/textures/highlight_hotbar.png
Hoe (item 100)
item icon 24x24: art/items/100.png
hotbar_latest
screen 224x32: art/textures/hotbar_latest.png
...
```

The first entry is the frame around the chosen slot, the third the bar itself. The hoe is the second: turn the
mouse wheel to mark it, then click to save it to `mods/_originals/items/100.png`, and press **Escape**. Its file is
`art/items/100.png`. `_names.txt` also lists a texture called `hoe`: that is the picture the icon is cut from (its
line says `the icon of item 100 (Hoe): use art/items/100.png`). **Replace what inspect mode shows under the
pointer**: for an item that is `art/items/<id>.png`, which changes the icon in the hotbar, bags, shops and item
links in chat.

### Track B: add a sound {#first-sound}

The game has no sounds of its own. Here you add one for when you get an item, and your own mark for when a fish
bites.

**1. Make the mod.** Make the folder `mods/my_sounds` with a `mod.xml`:

```xml sample=track_b file=mod.xml
<Mod name="My sounds" />
```

**2. Get a sound.** Any short `.wav` file works, or an `.ogg` in the Ogg Vorbis format. Make one in Audacity
(free; **File > Export Audio**, choose WAV), take one from a free sound site, or, to try the steps first, copy
`mods/sound_pack/sounds/item_gained.ogg`. Keep it short (under a second is good for an event) and quiet.

**3. Name it after the event.** Make the folder `mods/my_sounds/sounds` and put the file in it as
`item_gained.wav` (or `item_gained.ogg`). The event names are in `mods/_names.txt` under `[sounds]`:
`wallet_gain`, `item_gained`, `whisper`, `chat`, `zone_town`, `music_town` and so on. See [Sounds and music](#sounds).

**4. Hear it now.** Type `/preview my_sounds item_gained` in chat. It plays at once, without waiting for an item,
and chat says `Playing item_gained of my_sounds (0.2 s).` Or open the Mods page, select **My sounds** and press
**Play** next to the file: the line under it says how long it is (`Played: 0.2 s long.`; a longer file counts up
while it plays).

**5. Hear it for real.** It plays the next time you get an item: a catch, a harvest, a purchase. If another sound
mod also has `item_gained`, the later one in load order wins. A new mod goes near the end of the list, but not
always last: if nobody ever pressed **Load earlier** or **Load later**, the list is alphabetical and `sound_pack`
comes after `my_sounds`. The Mods page then shows your file as *overridden by sound_pack*; press **Load later** on
your mod until it is applied.

**6. A bite mark.** Draw a small picture for the moment a fish bites, for example 12x16 pixels with a
transparent background, and save it as `mods/my_sounds/cues/fishing_bite.png`. It replaces the game's yellow
"!" over your character. Put a short sound next to it as `cues/fishing_bite.wav` and it plays when a fish bites.
Type `/preview my_sounds fishing_bite`: your mark blinks over your character three times, about a second each,
with its sound. On the Mods page the picture is always drawn under its file, at the size the game draws it, and
**Play** blinks it there with its sound. See [Cues](#cues).

### Track C: a tiny Lua mod {#first-lua}

Here you write a mod that shows a text on the screen, changes it when you start walking, and gets a setting.

**1. Make the mod.** Make the folder `mods/my_first_mod` with a `mod.xml`:

```xml sample=first1 file=mod.xml
<Mod name="My first mod" description="Says hello." />
```

**2. Show a text.** Make a text file `init.lua` in the same folder:

```lua sample=first1 file=init.lua
HudText("hello", "Hello, Shorelands!")
```

Save it. Within a second or two, "Hello, Shorelands!" appears at the top left of the screen (below anything
other mods show there). `HudText` puts a text on the screen;
`"hello"` is the name you give that text, so you can change it later.

**3. React to something.** Replace the contents of `init.lua` with:

```lua sample=first2 file=init.lua
HudText("hello", "Hello, Shorelands!")

-- The game calls this when you start or stop walking, fishing, farming or talking.
function OnActivityChanged(activity)
  HudText("hello", "Hello, Shorelands!\nYou are " .. activity)
end
```

Save, then walk around. The text says "You are walking", then "You are idle". Or, without walking, type
`/try my_first_mod walking` in chat: `/try` calls your callback at once with made-up values (see
[Trying a callback](#try)). A function named `On...` is a *callback*: you write it, the game calls it when that
thing happens. `..` joins two texts, `\n` starts a new line,
and lines starting with `--` are comments, which the game skips.

**4. Add a setting.** Make a second file, `settings.lua`, next to `init.lua`:

```lua sample=first3 file=settings.lua
mod_settings = {
  {
    id = "greeting",
    ui_name = "Greeting",
    ui_description = "The first line of the text.",
    value_default = "Hello, Shorelands!",
  },
}
```

and change `init.lua` to read it:

```lua sample=first3 file=init.lua
-- Shows the greeting from the settings and what you are doing.
local function show(activity)
  HudText("hello", ModSettingGet("greeting") .. "\nYou are " .. activity)
end

-- At the main menu you are not in the world yet, and PlayerGetActivity() gives nil.
show(PlayerGetActivity() or "not in the world yet")

function OnActivityChanged(activity)
  show(activity)
end

-- Someone changed a setting of this mod on the Mods page.
function OnSettingChanged(id, value)
  show(PlayerGetActivity() or "not in the world yet")
end
```

Save both. Open the Mods page and select **My first mod**: there is your setting. Replace the text in the
field with a new greeting, press **Enter**, and the text on the screen changes.

**What next:** open `mods/example/init.lua`. It has every callback the game can call, each with a comment saying
when. `mods/_api.txt` lists every function. The sections below explain them.

## Coming from Noita {#noita}

Most of what you know carries over.

| The same as in Noita | |
|---|---|
| One folder per mod in `mods/`, the folder name is the mod id | `mods/my_mod/` |
| `mod.xml` with `name` and `description` | Only `name` is needed; a name of up to 60 characters, a description of up to 1,000 |
| `init.lua` runs when the mod loads; optional global `On...` functions are callbacks | `function OnPlayerReady() ... end` |
| `settings.lua` with `mod_settings = { { id, ui_name, ui_description, value_default, values, value_min, value_max } }` | Read with `ModSettingGet("speed")` or Noita's `ModSettingGet("my_mod.speed")` |
| `print` writes to the log, `GamePrint` writes in your chat | Your chat lines start with your mod's id, in violet |
| `dofile` and `dofile_once` | Paths inside your mod: `dofile_once("files/util.lua")` |
| `files/` for your own extra files | |
| One text file listing every function | `mods/_api.txt`, in Noita's `Name( arg:type ) -> result` form |
| `mods/example` to copy | |

| Different here | Why |
|---|---|
| A new mod is **on** as soon as its folder is there, and reloads by itself when you save a file | No restart and no enabling step while you work |
| No `data/` folder and no `ModTextFileSetContent` or `ModLuaFileAppend` | The game is compiled code, not Lua and XML files. Pictures and sounds are replaced by name instead: `art/`, `sounds/`, `cues/` |
| No entities or components | The server owns the world; a mod only changes what you see |
| The GUI is made once and updated, not drawn every frame | `HudText("id", text)` again with the same id updates it. No `GuiStartFrame` |
| Every call has a time budget (20 ms) | A mod that runs too long is switched off; see [Fast enough](#budget) |
| Lua 5.2, not LuaJIT | `unpack` and `bit` exist so copied code runs. There is no `io`, `require` or `load`, and `os` has only `time`, `date` and `clock` |
| Settings apply at once | `MOD_SETTING_SCOPE_*` exist only so copied tables run; categories are flattened |
| No Steam Workshop | Share a zip of the mod folder |

Renamed ideas:

| Noita | Here |
|---|---|
| `OnPlayerSpawned( player_entity )` | `OnPlayerReady()` (no entity; read with `PlayerGetName()` and friends) |
| `OnWorldPostUpdate()` | `OnTick( seconds )` ten times a second, or `OnFrame( seconds )` every frame |
| `GuiText` every frame | `HudText( id, text, opts )` once, and again when the text changes |
| `GlobalsSetValue` / `GlobalsGetValue` | `DataSave` / `DataLoad` (kept for good), `SessionSet` / `SessionGet` (kept until the game closes) |
| `compatibility.xml` | `needs_host="0.2.0"` in `mod.xml` (see [Versions](#versions)) |
| `-debug_lua` | Not needed: errors come as one plain sentence in chat, with file and line |

## Pictures {#art}

Pictures go in `art/` and replace the game's by file name. No code is needed.

| File | Replaces |
|---|---|
| `art/textures/<name>.png` | the game picture (texture) with that name, everywhere it is drawn |
| `art/items/<id>.png` | one item's icon, in bags, shops, the "new item" popup and item links in chat |
| `art/koi/<id>.png` | one koi's icon |

**Finding names.** Inspect mode (**F2**, `/inspect`, or **Inspect** on the Mods page) lists what you see at the tip
of the pointer, topmost first: the name, what kind of thing it is (`screen`, `world`, `your character`, `another
player's character`, `ground tile`, `cursor`), the pixel size and the file that would replace it. Something
see-through on top (a glow, a highlight) is listed with what shows through it; anything hidden behind a solid
pixel is not listed, so point at a visible part of what you want. Over something in the world, the ground tile
under it is listed too. Turn the mouse wheel to mark another entry, left-click to save the marked one's original,
right-click or **Escape** to leave. While it is on, the game ignores your clicks. `_names.txt` lists every texture
under `[textures]` (with its size), every item under `[items]` (id, size, name) and every koi under `[koi]`. Items
and koi appear there once you have been in the world; textures appear as the game loads them, so the list grows as
you play.

**Item icons and textures.** An item's icon is cut from a texture, so `_names.txt` lists the same picture twice:
the texture `hoe 24x24` and the item `100 24x24 Hoe`. The texture's line says which item it belongs to: `the icon
of item 100 (Hoe): use art/items/100.png`. Both files change the hoe in the hotbar, but only `art/items/100.png`
reaches item links in chat, and it wins when both are there. The rule of thumb: **what inspect mode shows under the
pointer is the one to replace.** For an item it shows `Hoe (item 100)` and `art/items/100.png`.

**The originals, as a reference.** To see the size and layout of what you replace, save the game's own picture:

- inspect mode, click: saves the marked picture;
- `/export <name>`, for example `/export hotbar_latest`;
- `/export item <id>`, `/export koi <id>`;
- `/export all`: every picture loaded right now.

They go to `mods/_originals/` in the same folders as a mod's `art/` (`textures/`, `items/`, `koi/`), so the path
tells you where your own picture goes. The saved pictures are always the game's own, even while a mod replaces
them. Draw your own picture at that size; do not ship copies or recolours of the game's pictures in a mod you share.

**Rules.**

- Same size as the original, in pixels. A name listed twice in `_names.txt` has two sizes; your PNG replaces the
  one of its size.
- PNG with transparency. Pixel art: draw at the real size, and when you scale, use "nearest neighbour" (no
  smoothing).
- Names are case-sensitive (`Background` and `background` are two pictures), but Windows file names are not, so
  two names that differ only in case cannot both be replaced.
- Most item icons are 24x24; the pet (item 50000) is 32x32. Koi icons are 24x24.
- At most 4096 pixels a side, and a mod's pictures together at most 16 million pixels (all the game's textures
  are about 7 million).
- A texture replaced by name does not reach item links in chat; `art/items/<id>.png` does.
- A plugin that draws its own koi icons wins over a mod. The `[koi]` section of `_names.txt` then says which koi can
  still show yours (the line starting with `--`); for the others the report says *not shown, another plugin draws
  its own icon for this koi and wins*.
- Some things are drawn by code and have no picture: the fishing line, the bite's "!", the rings on the water.
  Inspect mode says *cannot be replaced*. For fishing moments, use [cues](#cues).

**What the report says.** `/mods <mod>` in chat and the Mods page list every file:

| Word | Meaning |
|---|---|
| applied | It shows. |
| waiting | That picture is not loaded right now (you have not been where it is drawn), or items and koi before you are in the world. It shows when it loads. |
| ignored, with a reason | Wrong size (both sizes are named), unknown name (with a suggestion), not a PNG, too big. Fix the file and save. |
| overridden by `<mod>` | Another mod later in the load order replaces the same picture. See [Load order](#using). |

`mods/ui_skin` (a new interface theme, drawn from scratch) and `mods/icon_pack` (item and koi icons) are
complete picture mods to look at. `ui_skin/README.txt` says which texture is which part of the screen.

## Sounds and music {#sounds}

The game has no sounds; a mod adds them. A file in `sounds/` named after an event plays when it happens:

| File name (`.ogg` or `.wav`) | Plays when |
|---|---|
| `wallet_gain`, `wallet_loss` | your currency goes up / down |
| `item_gained` | you get an item and the "new item" popup shows |
| `whisper` | a private message arrives |
| `chat` | a public or global message from another player arrives (your own lines play nothing) |
| `zone_town`, `zone_playerland`, `zone_holyland` | you arrive in town, on the islands, in the holy land (also at login) |
| `rain_start`, `rain_stop` | rain starts / stops |
| `rain_loop` | over and over while it rains |
| `music_town`, `music_playerland`, `music_holyland` | over and over while you are there |
| `music_town_night` (and the others with `_night`) | instead of the music above while it is night, if your mod has it |
| `ui_open`, `ui_close` | a mod's window opens / closes |

An optional `sounds/sounds.xml` sets a volume (0 to 1) and a cooldown (seconds before the same sound may play
again, useful for chat) per sound:

```xml sample=sounds_xml file=sounds/sounds.xml
<Sounds>
  <Sound name="chat" volume="0.5" cooldown="1" />
  <Sound name="music_town" volume="0.3" />
</Sounds>
```

**Rules.** `.ogg` must be Ogg Vorbis; Opus is refused (with a hint). `.wav` always works. A file may be at most
8 MB; all of a mod's sounds together 32 MB and 64 files. An event sound may be at most 30 seconds, a loop
(music, rain) 10 minutes, counted as the file really plays (not what its header claims). Everything follows the
game's master volume. Music changes crossfade.

**Tips.** Make event sounds short and quiet: they play often. Fade the first and last few milliseconds, or you
hear a click. A loop must end the way it starts. **Play** on the Mods page plays a file now, and the line under it
counts the seconds while it plays (`Playing: 1.2 of 3.0 s`). In chat, `/preview <mod> <name>` plays it too and `/preview stop` stops it. At the
game's master volume 0 both tell you that nothing can be heard. `mods/sound_pack` is a complete sound mod to look at.

## Cues: fishing moments {#cues}

A cue is your own picture and sound for a moment of fishing. The game draws it; no Lua code runs at that moment
and no code is told about it. That is the fairness rule at work: a mod can make a bite easier to notice, but it
cannot react to it.

| Slot | The moment |
|---|---|
| `fishing_nibble` | a fish nibbles (the game draws a ring on the water) |
| `fishing_bite` | a fish bites (the game draws a yellow "!" over your character) |
| `fishing_tug` | the fish tugs (a splash and a ring) |
| `fishing_spook` | you reeled in too early and the fish flees (a small splash) |
| `fishing_leave` | the fish got away with the bait (a splash and a ring) |

Files: `cues/<slot>.png` (a picture, or several frames side by side), `cues/<slot>.ogg` or `.wav` (a sound, at
most 5 seconds), either or both. An optional `cues/cues.xml` says how to show them:

```xml sample=cues_xml file=cues/cues.xml
<Cues>
  <Cue slot="fishing_bite" hide_native="1" anchor="mark" scale="1" frames="4" fps="8" volume="0.8" />
</Cues>
```

| Attribute | Meaning | Default |
|---|---|---|
| `hide_native` | 1: draw yours instead of the game's "!"; 0: both. Only the bite has a mark of the game's to hide | 1 when the slot has a picture |
| `anchor` | `mark`: where the game draws its "!" (with its hop); `player`: at your feet; `bobber`: at the bobber | `mark` |
| `offset_x`, `offset_y` | move it, in pixels (-64 to 64) | 0 |
| `scale` | 1 to 8. At 1, one pixel of yours is one pixel of the game's "!" (which is 5x12 with its outline) | 1 |
| `frames`, `fps` | the PNG's width split into this many equal frames (1 to 64), played at this speed (0.5 to 60) | 1, 8 |
| `seconds` | how long it shows (0.05 to 10) | the bite: as long as the game's "!"; the others 0.5 to 0.7 |
| `volume` | 0 to 1 | 1 |

One frame, drawn at its scale, may be at most 64x64 pixels. A slot is one unit: its picture, sound and line in
`cues.xml` all come from the same mod (the last in load order that has the slot).

**Seeing a cue without fishing.** On the Mods page, a cue's picture is always drawn under its file, at the size the
game draws it, with its frames playing. **Play** blinks it as the game would show it (the bite three times, the
others for their own time) and plays its sound; the line under the button counts `Playing: the image 2 of 3 times
1.0 s, with its 0.2 s sound`. The sound file has no Play of its own: the slot's Play plays both, as the fishing
moment does. In chat, `/preview <mod> fishing_bite` shows the cue over your character the same way, with its sound.

## Lua: what matters here {#lua}

A Lua mod has an `init.lua`. Lua is a small language; you need only a little of it. This section covers the parts
that trip people up in this game.

### When your code runs

1. **When the mod loads**, the game runs `init.lua` from top to bottom once. Mods load when the game starts (at the
   main menu, before you log in), and again every time you save one of the mod's files.
2. **When something happens**, the game calls your callbacks: `OnZoneChanged` when you arrive somewhere,
   `OnItemGained` when you get an item, and so on. A callback is just a global function with that name. Leave out
   the ones you do not need.

Because `init.lua` first runs at the main menu, **reads of the world give `nil` there**: `WalletGetCurrency()`,
`ZoneGet()`, `InventoryGetItems(...)`. `OnPlayerReady()` is called once you are in the world (after every
login, and right after a reload while you are in the world). Put world reads there:

```lua sample=ready file=init.lua
-- Wrong here: at the main menu this would be nil.
-- local start = WalletGetCurrency()

function OnPlayerReady()
  local currency = WalletGetCurrency()
  GamePrint("Welcome back, " .. PlayerGetName() .. ". You have " .. TextNumber(currency) .. ".")
end
```

`mods/example/init.lua` has every callback with a comment saying when it is called. `_api.txt` has the same for
each, on the lines starting with `On`.

**Arriving somewhere.** `OnZoneChanged` is called for the zone you log in to as well, right after `OnPlayerReady`.
So a mod that counts arrivals in town counts every login in town as one. To count only trips, remember where you
were and count a change into town from somewhere else:

```lua sample=trips file=init.lua
local trips = 0
local last = nil

local function show()
  HudText("trips", "Trips to town: " .. trips)
end
show()

-- Where you logged in, or where you are when the mod reloads.
function OnPlayerReady()
  last = ZoneGet()
end

function OnZoneChanged(zone)
  if zone == "town" and last ~= "town" then
    trips = trips + 1
    show()
  end
  last = zone
end
```

### Trying a callback now {#try}

You do not have to wait for an event to test your callback. `/try <mod>` in chat lists what it can call in that
mod, for example for the sample above:

```
[test] /try my_mod <what> calls its function for an event with made-up values (only this mod hears it): ready (OnPlayerReady); town, playerland, holyland (OnZoneChanged). Or name the function and its values: /try my_mod OnTick 0.1.
```

`/try my_mod playerland` and then `/try my_mod town` count one trip at once. `/try my_mod whisper Friend hello`
calls `OnChatMessage` with a whisper from Friend, `/try my_mod item 6 2` calls `OnItemGained` with two carrots,
`/try my_mod ready` calls `OnPlayerReady`. Chat answers how long the call took:

```
[test] my_mod OnZoneChanged("town") ran (0.1 of 20 ms).
```

Every line your mod prints meanwhile starts with `[test]`, before its id. Only your mod is called: no sound plays, no other mod
hears it and nothing is sent. Your mod does what it always does, so it may save data. The Mods page lists the
`/try` lines of the selected mod under **Try its functions without waiting for the event**.

### A few Lua rules

```lua sample=rules file=init.lua
-- `local` keeps a variable inside this file. Use it for everything except callbacks.
local count = 0

-- A local function must be written above the code that calls it.
local function add(n)
  count = count + n
end

-- Text is joined with .. ; numbers join fine, nil and true/false do not (use tostring).
add(2)
GamePrint("count is " .. count .. ", raining: " .. tostring(WeatherIsRaining()))

-- A list, its length, and a loop over it.
local fish = { "Blue", "Green", "Purple" }
for i, name in ipairs(fish) do
  print(i, name)
end
print("there are " .. #fish)

-- A table with named fields.
local item = { name = "Carrot", count = 2 }
print(item.name .. " x" .. item.count)

-- nil means "nothing". `x or default` gives default when x is nil.
local zone = ZoneGet() or "nowhere yet"
print(zone)
```

`print` writes one line to `BepInEx/LogOutput.log` after your mod's id: line breaks become spaces, a line is cut at
1000 characters, and at most 100 lines or 20000 characters a second are kept (the log says when it cut some).

**Habits from other languages.** Comments start with `--` (a long one is `--[[ ... ]]`), not `#` or `//`. Not
equal is `~=`. Lua writes `and`, `or`, `not` instead of `&&`, `||`, `!`. There is no `+=` or `++`: write
`n = n + 1`. `elseif` is one word; `else if` starts a second `if` that needs its own `end`. A block ends with `end`,
not `}`. When you slip, the error in chat ends with what to write instead (see [When something goes wrong](#wrong)).

**`nil` and `false` are different.** A table entry you never set is `nil`, not `false`. So `if owned[id] == false`
misses entries you never set; write `if not owned[id]` to cover both.

**Reads give copies.** `InventoryGetItems("all")` gives you a new table each time. Changing it changes nothing in
the game.

**Your own files.** Put extra Lua in `files/` and load it with `dofile_once("files/util.lua")`. It runs once and
gives back what the file returns (see `mods/example/files/helpers.lua`). Paths always stay inside your mod's
folder.

### Fast enough: the budget {#budget}

Each call into your mod (the run of `init.lua`, each callback, each timer, each command) may take at most
**20 ms**. `init.lua` and `OnPlayerReady` may take 250 ms, because that is where you build big lists. A call that
takes more is stopped and the mod is switched off until you change its files, with one chat line. An endless
loop is caught the same way.

Before that happens you get a warning: when a call uses more than half its budget, chat says once, for example

```
[koi_checklist] OnInventoryChanged used 12 of 20 ms; a call over its budget switches the mod off.
```

`/mods <mod>` and the Mods page show **Slowest calls**, the most each kind of call has used since the mod
loaded.

All of a mod's calls together, with the host's work for it (drawing its widgets and windows, writing its
files), may take at most **1,500 ms in any 5 seconds**. At half of that chat says so once, for example
`[my_mod] used 751 of 1,500 ms in 5 seconds, most of it in its timers; a mod over that is switched off.` When
many of a mod's timers are due in one frame, those past its share of the frame (5 ms) wait for the next one. A
mod may also keep at most **32 MB** of Lua data (the tables and texts it holds on to between calls). `/mods <mod>`
shows both on its **Load** line. A mod may also have at most 64 timers, 16 chat commands, 8 key settings and
1,000 saved keys. To stay fast:

- Build big lists once, in `OnPlayerReady` or `init.lua`, and keep them in a `local`. Lua here is slow:
  going over 1,800 koi with a few lookups each takes about 12 ms.
- Do not redo work in `OnTick` or `OnFrame`. Read a value, compare it with the last one, update only when it
  changed. Most mods do not need `OnFrame` at all.
- For a long list in a window, let the host search it (`WindowAddSearch` without a function) instead of
  searching in Lua.

```lua sample=budget file=init.lua
-- Count the koi of each colour once, when you are in the world, and keep the result.
local colours = { "Blue", "Dark blue", "Aqua", "Green", "Yellow", "Red", "Pink", "Purple" }
local per_colour = {}

function OnPlayerReady()
  per_colour = {}
  for _, koi in ipairs(KoiGetAll()) do
    if koi.type > 0 then
      per_colour[koi.type] = (per_colour[koi.type] or 0) + 1
    end
  end
  for type, name in ipairs(colours) do
    print(name .. ": " .. (per_colour[type] or 0) .. " koi")
  end
end
```

### Reloading, and what is kept {#reload}

When you save a file of your mod, the game stops the mod and starts it again, within a second or two (chat says
`[<mod>] reloaded: loaded`). `/reloadmods` and **Reload mods** on the Mods page reload every mod.

A reload, switching the mod off, and closing the game throw away everything the mod made: its texts and windows,
timers, commands, sounds, chat highlights and its Lua variables. Just before, the game calls `OnModUnload()`.
Three things are kept:

| Kept | How long | How |
|---|---|---|
| Settings | for good | `settings.lua`, `ModSettingGet` |
| Saved data | for good, also when the mod is updated (`mods_data/<mod>/data.json`) | `DataSave( key, value )`, `DataLoad( key, default )` |
| Session data | until the game closes (also across reloads and logging out and in) | `SessionSet( key, value )`, `SessionGet( key, default )` |

Values can be numbers, texts, `true`/`false`, and tables of those, at most 256 KB per mod (as JSON). `data.json` is
written within 2 seconds of a change, at most every 10 seconds, and when the mod stops or the game closes, so saving
in every call costs nothing extra. Where a HUD text was dragged to is kept too.

```lua sample=kept file=init.lua
-- Items you got: this game session, and since the mod was installed.
local session = SessionGet("items", 0)
local total = DataLoad("items", 0)

local function show()
  HudText("items", "Items this session: " .. session .. "\nAll time: " .. total)
end
show()

function OnItemGained(item)
  session = session + item.count
  total = total + item.count
  SessionSet("items", session)
  DataSave("items", total)
  show()
end
```

To see it count without farming, type `/try <mod> item 6 2` (two carrots); then save the file and watch the
numbers stay.

## HUD texts, icons and bars {#hud}

A HUD widget is made once and updated by calling the same function with the same id again:

```lua sample=hud file=init.lua
HudText("title", "Fishing trip", { anchor = "top_right", plate = true, color = "#ffd27f" })
HudIcon("rod", 101, { anchor = "top_right" })                -- an item's icon, by item id
HudBar("progress", 0.25, { anchor = "top_right", width = 80 })

-- Later, the same ids update them:
TimerAfter(2, function()
  HudText("title", "Fishing trip: halfway")
  HudBar("progress", 0.5)
end)
```

Options (all optional; given once, they stay until given again):

| Option | Meaning |
|---|---|
| `anchor` | `top_left` (default), `top`, `top_right`, `left`, `center`, `right`, `bottom_left`, `bottom`, `bottom_right` |
| `x`, `y` | an exact place, in UI units from the anchor's edges. Leave them out and the host stacks your widget in that corner, clear of the game's buttons and other mods' widgets. Leaving them out is usually best. |
| `color` | `"#rrggbb"`. `colour` works too, here and in list rows. |
| `size` | 1 to 3 (text and icons) |
| `plate` | `true`: on the game's dark plate, readable over grass and water |
| `draggable` | `true`: the player can drag it; right-click puts it back |
| `width`, `height` | bars only: 8 to 200 and 2 to 16 UI units |
| `meta` | icons only: for item 9999 (a koi), which koi |

`\n` starts a new line; long lines wrap. `HudShow( id, false )` hides a widget, `HudRemove( id )` removes it.
A HUD icon can also be a PNG of your mod: `HudIcon("logo", "files/logo.png")`, at most 256x256 pixels (a mod
may use 256 different icon files, list icons included). The screen is 640x360 UI units at
1280x720 and 1920x1080, 800x450 at 1600x900.

## Windows {#windows}

A window has a title, an X to close it, and from top to bottom: an optional search field, a row of buttons, a
text and a scrolling list. You make it once, and open it when the player asks (a key or a command):

```lua sample=window file=init.lua
-- A window with every koi, searchable. /allkoi opens and closes it.
WindowCreate("koi", "All koi", 260, 300)        -- id, title, width, height (UI units)
WindowAddSearch("koi", "Search koi")            -- the host filters the list as you type
WindowAddButton("koi", "close", "Close", function()
  WindowClose("koi")
end)

function OnPlayerReady()
  local rows = {}
  for _, koi in ipairs(KoiGetAll()) do
    if koi.id ~= 0 then                          -- #0 "None" is the table's empty entry
      table.insert(rows, { text = "#" .. koi.id .. " " .. koi.name, koi = koi.id })
    end
  end
  WindowSetText("koi", #rows .. " koi")
  WindowSetList("koi", rows)
end

CommandRegister("allkoi", "opens the list of every koi")
function OnCommand_allkoi(args)
  WindowToggle("koi")
end
```

- A row is a text, or a table `{ text, icon = item id or "files/x.png", meta, koi = koi id, color }`. Up to
  10,000 rows; only the visible ones are drawn. One list may use up to 64 different icon files.
- Escape or the X closes a window; `OnWindowClosed( id, by_player )` tells you. After the player closes one, your
  mod cannot open a window for 2 seconds (`WindowOpen` gives `false`), except from the player's own key, command
  or button: a closed window stays closed until the player asks for it again.
- A window is at most the screen less a strip at the top (26 units), where the game's menu button stays free.
- `WindowSetButton( id, button, { label = "...", selected = true } )` relabels a button or shows it pressed, for
  a row of filters like All / Owned / Missing.
- `WindowGetSearch( id )` gives the search text and how many rows match; `OnWindowSearchChanged` is called as the
  player types. `WindowSetSearch( id, text )` puts a text into the field (`""` empties it), for a Clear button or a
  filter that searches for the player.
- Windows show only in the world. At most 8 windows per mod, 8 buttons per window.

`mods/koi_checklist` is a full window mod: filters, a search, live updates and a tooltip line.

## Settings {#settings}

`settings.lua` sets one table, `mod_settings`. Each entry is one setting; `value_default` decides what kind it is
and how the Mods page shows it:

```lua sample=settings file=settings.lua
mod_settings = {
  -- On/off: a toggle.
  { id = "show_clock", ui_name = "Show the clock", value_default = true },

  -- A choice: a dropdown. ModSettingGet gives the id ("top_left"), the page shows the label.
  { id = "corner", ui_name = "Corner", value_default = "top_left",
    values = { { "top_left", "Top left" }, { "top_right", "Top right" } } },

  -- A choice of colours: the ids are the colours themselves, so they go straight into colour = ...
  { id = "colour", ui_name = "Text colour", value_default = "#ffffff",
    values = { { "#ffffff", "White" }, { "#ffd27f", "Gold" }, { "#8cc4f2", "Blue" } } },

  -- A whole number with a range of 10 or less: - and + buttons. Anything else: a slider.
  { id = "lines", ui_name = "Lines", value_default = 3, value_min = 1, value_max = 5 },
  { id = "volume", ui_name = "Volume", value_default = 0.5, value_min = 0, value_max = 1 },

  -- A text: a field. text_max_length: how many characters (default 200, at most 1,000).
  { id = "title", ui_name = "Title", value_default = "My stats", text_max_length = 40 },

  -- A key: a button that waits for the key you press next.
  { id = "toggle_key", ui_name = "Show or hide", type = "key", value_default = "L" },
}
```

```lua sample=settings file=init.lua
local function show()
  if not ModSettingGet("show_clock") then
    HudRemove("clock")
    return
  end
  HudText("clock", ModSettingGet("title") .. "\n" .. os.date("%H:%M"),
    { anchor = ModSettingGet("corner"), colour = ModSettingGet("colour") })
end
show()
TimerEvery(10, show)

-- The key of a "key" setting was pressed; id is the setting's id.
function OnKey(id)
  if id == "toggle_key" then
    ModSettingSet("show_clock", not ModSettingGet("show_clock"))
    show()
  end
end

-- The player changed a setting on the Mods page (not called for your own ModSettingSet).
function OnSettingChanged(id, value)
  show()
end
```

Press **L** to hide and show the clock. `/try <mod> key toggle_key` does the same from chat, and `/try <mod> setting
corner` calls `OnSettingChanged` as if you had changed the corner.

- `ui_description` adds a line of explanation under the setting.
- `ModSettingSet( id, value )` changes a setting from your code at once; the settings file is written within a
  second (at most every 5 seconds, and when the mod stops). A key setting is the exception: only the player picks
  its key (on the Mods page), so `ModSettingSet` leaves it as it is and returns `false`.
- Up to 100 settings, 200 entries with headings and categories, and 64 `values` per choice. An `id` has up to 64
  characters, a `ui_name` and a choice's label 100, a `ui_description` 500. More is an error that names the limit.
- The settings are kept in `BepInEx/config/mods.<mod>.cfg`; an edit there while the game runs reaches the mod
  within a second. A value that does not fit (out of range, an unknown choice) is corrected, never an error.
- Noita's categories (`{ category_id = ..., settings = { ... } }`) work; the page shows their settings in one
  list.
- The Mods page shows the settings only while the mod is on and has no error.

### Choosing a key {#keys}

Key names are Unity's and case-sensitive: `"L"`, `"F4"`, `"Digit8"`, `"Space"`, `"Semicolon"`. `OnKey` is not
called while the player types in chat or a text field. Pick a key nothing else uses. Avoid:

- the game's keys: W A S D, E, Tab, P, O, 1 to 7, Enter, Escape (or what the player set on the game's
  **Controls** page);
- F2, inspect mode;
- the example mods' keys: F4 (`example`) and K (`koi_checklist`);
- function keys in general: other BepInEx plugins often use them. The host
  cannot see other plugins' keys, so it cannot warn you about those.

When a key setting has the same key as another mod's setting, inspect mode or one of the game's keys, chat says so
once, the Mods page marks the mod **Same key**, and the line under the setting says what else uses it:

```
[my_mod] K is also Koi checklist's key for "Open the checklist": one press does both. Change one on the Mods page.
```

The player can give either one another key there.

## Chat and commands {#chat}

**Decorate chat.** These change how chat looks on your screen only. They apply to every line, also the server's:

```lua sample=chatdecor file=init.lua
ChatSetTimestamps("%H:%M")          -- "14:05" before every line; nil switches it off
ChatHighlight("koi", "#ffd27f")     -- the word koi in gold, any case, as a whole word
ChatHighlight("trade", "#a8e08c")
```

Timestamp codes: `%H` hour, `%M` minute, `%S` second, `%I` hour of 12, `%p` AM/PM, `%d` day, `%m` month,
`%y`/`%Y` year, `%%` a percent sign. Between them a format may use letters, digits, spaces and `: . - / , ( ) [ ] |`.
Up to 32 words per mod, each up to 40 characters. `ChatHighlightClear()` removes yours.

**Read chat.** `OnChatMessage( msg )` gets every player's line, yours too: `msg.type` is `"public"`, `"global"` or
`"private"`, plus `msg.sender` and `msg.text`. Server, info and shop lines do not arrive: a line arrives only when
its sender is a player.

```lua sample=whispers file=init.lua
local whispers = 0

function OnChatMessage(msg)
  if msg.type == "private" and msg.sender ~= PlayerGetName() then
    whispers = whispers + 1
    HudText("whispers", "Whispers: " .. whispers, { anchor = "bottom_left" })
  end
end
```

`/try <mod> whisper Friend hello` gives it a whisper from Friend without anyone sending one.

**Write in chat.** `GamePrint( text )` shows a line in your own chat. Nothing is sent to anyone. The line starts
with your mod's id and is violet, so a player can always tell a mod's line from the game's:

```
[Info]: [my_mod] heads, tails
```

You cannot remove or change that start or the colour; colour tags in the text show as typed.

**Clickable text.** `GamePrintLink( text, label, fn )` prints a line like `GamePrint`, with `label` after the text
on a violet box. A click on the label calls `fn`, your own function:

```lua sample=chatlink file=init.lua
WindowCreate("notes", "My notes", 200, 100)
WindowSetText("notes", "Bait: worms. Spot: the pier.")

function OnPlayerReady()
  GamePrintLink("Your notes are here.", "Open them", function() WindowOpen("notes") end)
end
```

```
[Info]: [my_mod] Your notes are here. Open them
```

A line can have up to three: `GamePrintLink("Again?", "Yes", yes, "No", no)`. A label has up to 40 characters. A
click counts as the player's own, like a window button, so `fn` may open a window. Clicking only runs your Lua:
nothing is sent, and the link never opens the game's player menu. Your links stop working when your mod reloads,
and only your newest 64 work; a click on an older one says so in chat.

**Commands.** `CommandRegister( name, description )` adds `/name`, for you only. Typing it calls
`OnCommand_<name>( args )`, with `args` the rest of the line:

```lua sample=command file=init.lua
CommandRegister("coin", "flips a coin; /coin 3 flips three")

function OnCommand_coin(args)
  local times = tonumber(args) or 1
  local results = {}
  for i = 1, math.min(times, 10) do
    if math.random() < 0.5 then
      table.insert(results, "heads")
    else
      table.insert(results, "tails")
    end
  end
  GamePrint(table.concat(results, ", "))
end
```

A name is lower case letters, digits and `_`. Names the game, the server or another mod already uses are refused
with a chat line. A command is typed with its slash (see [Typing commands](#commands)); it runs on your computer
only, and the game's suggestion box lists it as soon as the mod has registered it. The host's own commands:
`/mods`, `/mods <mod>`, `/mods off <mod>`, `/mods on <mod>`, `/reloadmods`, `/inspect`, `/export`, `/preview`, `/try`.

## Notices, timers, sounds, text files {#more}

**Notices** show a short text at the top centre for a few seconds: `NoticeShow( text, seconds )`. Your mod's id
in violet stands above it.

**Timers** call a function later or again and again (real seconds):

```lua sample=timers file=init.lua
TimerAfter(3, function()
  NoticeShow("Three seconds after loading.")
end)

local ticks = 0
local id = TimerEvery(60, function()
  ticks = ticks + 1
  print("another minute: " .. ticks)
end)
-- TimerCancel(id) stops it.
```

**Sounds from Lua.** `SoundPlay( "files/ding.wav", 0.5 )` plays a file of your mod once; `SoundLoop( id, file,
volume )` plays one over and over until `SoundStop( id )`. Same file rules as [sounds](#sounds).

**Text files for OBS.** `ExportText( name, text )` writes `mods_data/<mod>/export/<name>.txt`. In OBS, add a
**Text (GDI+)** source, tick **Read from file** and pick that file; OBS shows the new text whenever it changes. The
full path is on the Mods page (**Files for OBS**) and in `/mods <mod>`. A file is written at most every 0.25 s, and
a mod's files together at most 8 times and 128 KB a second; a newer text waits and is written when its turn comes
(and when the mod stops). Up to 32 files of 64 KB.

```lua sample=export file=init.lua
local function write(zone)
  ExportText("zone", "Now in: " .. (zone or "the menu"))
end
write(ZoneGet())

function OnZoneChanged(zone)
  write(zone)
end
```

Check the file without travelling: `/try <mod> holyland` writes `Now in: holyland`. `mods/stream_ticker` does this for your last item and
your currency.

**Numbers and times as text.** `TextNumber(45380)` gives `"45,380"`, `TextNumber(2.5, 1)` gives `"2.5"`.
`TextDuration(252)` gives `"4:12"`, `TextDuration(4000, "long")` gives `"1 h 06 min"`. `os.date("%H:%M")` is the
time now, `os.time()` the seconds since 1970 (good for "how long ago"). `string.format("%d of %d", 3, 10)` builds
a text from parts.

## Reading _api.txt and _names.txt {#reference}

Both files are plain text in `mods/`; open them in any text editor and use its search (**Ctrl+F**).

Its first line names the ShorelandsMods version it was written by (the `needs_host` for your `mod.xml`, see
[Versions](#versions)). Then it has one line per function, grouped by topic (Player, Wallet, Inventory, HUD, Windows,
...). A line reads:

```
WalletGetCurrency() -> currency:number|nil  -- your currency (the number on the wallet); nil when you are not in the world
```

- `WalletGetCurrency()` is what you write. Arguments in `[brackets]` are optional.
- `-> currency:number|nil` is what you get back: a number, or `nil`.
- After `--` comes what it does, and when it gives `nil` or `false`.
- Lines starting with `On` are callbacks: you write that function, the game calls it.

Search for a word of what you want to do (`koi`, `wallet`, `chat`, `window`), not for a function name you guess.

`_names.txt` has a section per kind of file: `[textures]` (name, then size), `[items]` (id, size, name), `[koi]`
(id, name), `[sounds]` and `[cues]`. To find the id of an item or koi, search for its name.

## When something goes wrong {#wrong}

**An error** gives one chat line, in this form:

```
[my_mod] init.lua line 7: there is no function called WalletGetCurrecny. Did you mean WalletGetCurrency? The mod is off until its files change.
```

That is the mod, the file and line, and what happened. The mod is then off: fix the file and save, and it starts
again by itself. The Mods page shows the mod as **Failed** with the same sentence. The log
(`BepInEx/LogOutput.log`) has the raw error too.

Common messages and what they mean:

| The message says | What to do |
|---|---|
| there is no function called X. Did you mean Y? | A typo, or a function that does not exist. Check `_api.txt`. |
| X is nil, so it has no fields to read (is it set yet?) | You wrote `X.something` but X is `nil`. Often a read at the main menu: move it into `OnPlayerReady`. |
| tried to join text, but one of them is nil (is it set yet?) | `..` with a `nil`. Use `(x or "")`; for `true`/`false` use `tostring(x)`. |
| tried to add numbers, but X is nil | Arithmetic with a `nil`: a variable never set, or a read that gave `nil`. |
| 'end' is missing (to close the 'function' on line 3). Every function, if, for and while needs its own end. | A block is not closed. Count your `function`/`if`/`for` and `end`. The line is where Lua gave up, often the end of the file; the block it names is the one to look at. |
| this is not valid Lua: unexpected symbol near '#'. Comments start with --, not #. | A habit from another language. The last sentence says what Lua wants: `--` for comments, `~=` for not equal, `and`/`or`/`not`, `n = n + 1` instead of `+=` and `++`, `elseif` as one word. |
| 'end' is missing (to close the 'if' on line 1). The else if on line 3 starts a new if that needs its own end; write elseif as one word. | Write `elseif`, or close the inner `if` with its own `end`. |
| 'then' is missing before 'x'. Write then after the condition: if x > 1 then ... end. | An `if` needs `then` after its condition; a `for` or `while` needs `do`. |
| this end has nothing to close (one end too many?) | Remove the extra `end`. |
| HudText: argument 2 (text) must be text, but it is nil | A function got the wrong kind of value, or too few. `_api.txt` has its arguments. |
| it ran too long (more than 200,000 steps in one call); an endless loop? | A loop that never ends, or far too much work at once. See [the budget](#budget). |

**A notice** is not an error. When a function gets a value it cannot use (a word too long to highlight, an
unknown corner, a number out of range), it leaves it out or corrects it, chat says so once, and the mod runs on:

```
[my_mod] HudText: the option "ancor" is not known (did you mean "anchor"?) and is left out. Options: anchor, x, y, color, size, plate, draggable
```

**A mod that does not appear** in the list or says **Not loaded**: the folder name is not a valid id (only lower
case letters, digits and `_`), the folder has no `mod.xml` (look for `mod.xml.txt`, or a folder one level too
deep), or the mod needs a newer host.

**A mod's window, HUD, notices or sound are in the way** and you want out:

- **Press Escape three times quickly.** If a mod keeps opening a window you closed, this closes every mod
  window and opens the game's menu on the Mods page with that mod selected: click **Switch off**.
- **Type `/mods off <mod>`** in chat (Enter, then type; it works even when a window covers the chat). The mod
  stops at once: its windows, HUD, notices and sounds go away, and it stays off until `/mods on <mod>` or the Mods
  page's switch. `/mods` lists the mods' ids.
- The game's **menu button** at the top right is never covered by a mod: Settings, then **Mods**.

**A picture or sound that does not change**: `/mods <mod>` says why for each file (wrong size, unknown name,
overridden by another mod, waiting). See [Pictures](#art).

**Asking for help**, send:

1. what you did and what you expected,
2. the chat line (a screenshot is fine) and what `/mods <mod>` says,
3. your mod folder as a zip,
4. `BepInEx/LogOutput.log` (it may have your character's name in it).

## Sharing a mod {#share}

Zip the mod's folder itself, so the zip holds `my_mod/mod.xml` and not just the files. The other player unzips it
into their `mods/`.

Leave out what you do not use: test files, old versions. Never put pictures from `_originals` in it, changed or not:
they are the game's art. Your saved data lives in `mods_data/`, not in the mod folder, so it is never in the zip,
and players keep theirs when they install your new version.

Before you share:

- [ ] `mod.xml` has a `name`, a short `description`, your `author` name, a `version` like `1.0.0`, `needs_host`
  (see [Versions](#versions)) and `made_for_game` (the game version, shown in the top line of `_names.txt`).
- [ ] The mod loads at the game's start with no chat line, and works after logging in.
- [ ] `/mods <mod>` lists no ignored files, and the Mods page shows no **Unused files**.
- [ ] No "used N of 20 ms" notice while you play.
- [ ] The Mods page shows no **Same key**, and your keys are not function keys other plugins are likely to use.
- [ ] Switching it off on the Mods page and on again works.
- [ ] It works together with the other mods you have, and with the `example` mods.
- [ ] A `README.txt` says what it does and what its settings mean.

### Versions {#versions}

```xml sample=versions file=mod.xml
<Mod name="My backpack" description="A blue backpack." version="1.0.0" author="you"
     made_for_game="0.69i" needs_host="0.2.0" />
```

`version` is your mod's: three numbers, starting at `1.0.0`. When you share a new one, raise the last number for a
fix, the middle one when you add something, and the first when players have to do something again (a setting
changed its meaning, a key moved). ShorelandsMods shows it and does nothing else with it.

`needs_host` is the ShorelandsMods your mod needs: write the version from the first line of `_api.txt` (also in
`/mods`) that you made the mod with. With an older ShorelandsMods, a player gets one line instead of errors: `it
needs host 0.3.0, you have 0.2.1; update ShorelandsMods to use it`. ShorelandsMods counts the same way: a new last
number only fixes things, a new middle number adds things, a new first number can break mods. While its first
number is 0, as now, a new middle number can break mods too, so look at the changes when it goes up.
