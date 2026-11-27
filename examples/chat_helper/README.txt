Chat helper  (an example Lua mod)

What it does
  - Puts the time before every chat line (also lines from the server, shops and mods).
  - Colours your character's name wherever it appears in chat, so you notice when someone talks to you.
  - Colours extra words you choose (a koi you are looking for, "trade", a friend's name).
  - Adds the command /stats. Type it in chat to see how much chat you saw this session:
        Chat in the last 42 min: public 31, global 12, whispers to you 2, your own lines 9. Start again
    Click "Start again" after that line, or type /stats reset, to start counting again. The command
    and the click run on your computer only; nothing is sent.
    The counts carry on when you log out and back in or the mod reloads, and start again when you
    restart the game.

Settings (Options > Mods > Chat helper)
  Timestamps              14:05, 14:05:09, 02:05 PM or Off
  Highlight my name       on or off
  Colour of my name       sky blue, gold, green, rose or lilac
  Also highlight          words separated by commas or spaces, e.g.  koi, trade, Mira
                          (whole words, any case; up to 31 words of at most 40 letters each:
                          others are left out, and chat tells you once which one)
  Colour of those words   as above
  The settings are kept in BepInEx/config/mods.chat_helper.cfg.

Change it
  init.lua is the whole mod, with comments. Some ideas:
    - More formats: add a line to "timestamps" in settings.lua. %H hour, %M minute, %S second,
      %I hour 1-12, %p AM/PM, %d day, %m month, %y / %Y year.
    - Count whispers per person: OnChatMessage( msg ) gives msg.sender.
  Save the file while the game runs and the mod reloads by itself within a second or two.
  mods/_api.txt lists every function and callback a mod can use.
  If two mods set timestamps, the one later in the load order wins.
