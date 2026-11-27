ShorelandsMods @VERSION@
========================

Casual mods for Shorelands: art packs, sound packs, fishing cues and small Lua scripts (HUD lines,
windows, chat helpers) that you put into a folder. Nine example mods and a guide for making your own come
with it. Mods only change what you see and hear on your own PC; they cannot act in the game for you.

Needs: BepInEx 5 (made with 5.4.23, Unity Mono, x64) installed in the game folder.

Install
  1. Close the game.
  2. Open the game folder: in Steam, right-click Shorelands, then Manage > Browse local files.
  3. Unzip this zip into that folder, so that BepInEx/plugins/ShorelandsMods/ and mods/ are next to
     Shorelands.exe. Allow it to merge folders.
  4. Start the game. The game's settings now have a Mods button (menu at the top right, Settings, Mods;
     at the login screen: OPTIONS, Mods). It shows the version, as does the chat command /mods.

The guide: open mods/_guide.html in your browser. At the first start the game writes mods/_api.txt (every
function a Lua mod can use) and mods/_names.txt (every name a picture or sound can replace); the guide
refers to both.

Switching mods off: each mod has a switch on the Mods page. To remove one, delete its folder in mods/.

Uninstall
  Delete BepInEx/plugins/ShorelandsMods/ and this file. Everything the mods changed goes back to the game's
  own at the next start. To remove the mods and what they saved as well, also delete mods/, mods_data/,
  BepInEx/config/com.zerfl.shorelands.mods.cfg and BepInEx/config/mods.*.cfg.

Licences: BepInEx/plugins/ShorelandsMods/LICENSE.txt (MIT) and THIRD-PARTY-NOTICES.txt, which lists the
bundled libraries.
