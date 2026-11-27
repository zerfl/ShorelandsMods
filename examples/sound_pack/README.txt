Soft chiptune sounds  (an example mod: sounds only, no code)

What it changes
  The game has no sounds. This adds gentle ones:
    wallet_gain / wallet_loss   coin chime when your currency goes up / down
    item_gained                 a soft pop when you get an item
    whisper                     two quiet notes when someone whispers you
    chat                        a tiny tick for public and global chat (not for your own lines)
    zone_town / zone_playerland / zone_holyland   a short tune when you arrive there (also at login)
    rain_start / rain_stop      rain fading in / out, and rain_loop plays while it rains
    ui_open / ui_close          when a mod window opens / closes
  and a fishing bite cue: when a fish bites, a little white bubble with a "!" hops above you instead of the
  game's yellow "!", with a short "plink".

Make your own
  1. Copy this folder and rename the copy (e.g. my_sounds). Change name="..." in mod.xml.
  2. sounds/<event name>.ogg (or .wav) plays on that event. The names are in mods/_names.txt, section [sounds].
     Leave a file out and that event stays silent.
  3. sounds/sounds.xml sets each sound's volume (0 to 1) and cooldown (seconds before it can play again,
     useful for chat). Remove a line and the sound plays at full volume with a 0.05 s cooldown.
  4. Keep sounds short and quiet: an event sound may be at most 30 s, a loop 600 s, a cue sound 5 s, a file 8 MB.
     Fade the start and end (a few milliseconds) or you hear a click. A loop must end where it starts.
     .ogg must be Ogg Vorbis (Opus is refused); .wav works everywhere.
  5. The bite cue: cues/fishing_bite.png is a strip of 4 frames side by side (each 12x16 pixels here),
     cues/fishing_bite.ogg the sound, cues/cues.xml says how to show it (frames, fps, scale, hide the game's "!").
     The other fishing moments (fishing_nibble, fishing_tug, fishing_spook, fishing_leave) work the same way.
  Everything follows the game's master volume.
