cues/: your own picture and sound for a fishing moment. No code needed.

  Slots: fishing_nibble, fishing_bite, fishing_tug, fishing_spook, fishing_leave (mods/_names.txt, [cues],
  says what the game draws for each).

  cues/<slot>.png         a picture, or a strip of frames side by side (an animation)
  cues/<slot>.ogg or .wav a sound (at most 5 s)
  cues/cues.xml           optional: how to show it, for example
    <Cues>
      <Cue slot="fishing_bite" hide_native="1" frames="4" fps="8" scale="1" />
    </Cues>
  hide_native="1" draws your picture instead of the game's "!" (5x12 pixels with its outline). Frames: the
  PNG's width split into that many equal frames. One frame may be at most 64x64 pixels as drawn.

  To see and hear it without fishing: /preview <your mod> fishing_bite in chat, or Play on the Mods page.
  A cue only shows and plays: no Lua code is told when a fish bites, on purpose.
