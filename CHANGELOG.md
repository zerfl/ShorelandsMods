# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), the version numbers
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). A mod states the host version it needs in its
`mod.xml` (`needs_host`).

## [0.3.0] - Unreleased

The first public version.

### Added

- **The mod host.** One BepInEx plugin that loads every folder in the game's `mods` folder as a mod, in a load
  order you can change, and reloads a mod within a second or two when one of its files changes, also while you
  play. A mod that fails is switched off with a sentence saying what went wrong and where.
- **Pictures.** `art/textures/<name>.png` replaces a game texture by name, `art/items/<id>.png` an item icon
  (also in chat item links) and `art/koi/<id>.png` a koi icon. Changes are undone exactly when a mod is switched
  off or removed.
- **Sounds and music.** `sounds/<event>.ogg` or `.wav` replaces a game sound or a music track by event name.
- **Cues.** Own pictures (animated strips) and sounds for the fishing moments: nibble, bite, tug, leave, spook.
- **Lua mods** (Lua 5.2), in a sandbox: callbacks for zone, activity, wallet, items, inventory, chat, light,
  rain, the offering round, tooltips, ticks and frames; HUD texts, icons and bars; windows with lists, buttons
  and search fields; chat commands, chat highlights and clickable chat links; notices, timers, sounds; saved data
  per mod; text files for OBS. Every call has a time and memory budget; a mod cannot send anything to the server
  and can only read and write inside its own folders.
- **Settings for mods.** `settings.lua` in Noita's format: toggles, numbers, sliders, choices, text and keys,
  shown on a **Mods** page in the game's settings, which also switches mods on and off, orders them and lists
  every file of a mod as applied, waiting, ignored (with the reason) or overridden.
- **Tools for authors.** Inspect mode (F2) names the picture under the mouse and saves the game's original as a
  template; `/mods`, `/try` and `/export` in chat; `mods/_api.txt` lists every function and callback,
  `mods/_names.txt` every texture, item, koi, sound and cue name.
- **The guide** (`mods/_guide.html`) and nine example mods: a starter mod with every callback, a chat helper, a
  koi checklist, an offering reminder, session stats, a stream ticker for OBS, an icon pack, a sound pack and a
  UI theme.
