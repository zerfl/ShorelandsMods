sounds/: sounds that play when something happens. No code needed. The game itself has no sounds.

  sounds/<event>.ogg or .wav plays on that event, for example:
    wallet_gain.ogg   your currency went up        item_gained.ogg   you got an item
    whisper.ogg       someone whispered you        zone_town.ogg     you arrived in town
    music_town.ogg    loops while you are in town  rain_loop.ogg     loops while it rains
  The full list is in mods/_names.txt, section [sounds].

  sounds/sounds.xml (optional) sets a volume and a cooldown per sound:
    <Sounds>
      <Sound name="chat" volume="0.5" cooldown="1" />
    </Sounds>

  .ogg must be Ogg Vorbis (not Opus); .wav always works. An event sound may be at most 30 s, a loop 600 s.
  To hear a file without waiting for its event: the Play button next to it on the Mods page, or
  /preview <your mod> <event> in chat.
