-- Chat helper: timestamps, highlighted words, and a /stats command.
--
-- How a Lua mod works: the host runs this file once when the mod loads, then calls the functions named
-- On...() below when something happens in the game. Everything the mod can call is listed in mods/_api.txt.
--
-- Timestamps and highlights are drawn by the host on every chat line. The mod only says what it wants;
-- it never sees which lines matched.

-- What /stats reports, counted since the game started. Kept with SessionSet, which the host holds until
-- the game closes: the counts carry on when the mod reloads (for example after you edit this file) or when
-- you log out and back in, and start again with the next game.
local function new_counts()
  return { started = os.time(), public = 0, global = 0, whispers = 0, mine = 0 }
end

local counts = SessionGet("counts")
if counts == nil then
  -- The first load in this run of the game: start counting now.
  counts = new_counts()
  SessionSet("counts", counts)
end

local function apply_timestamps()
  local format = ModSettingGet("timestamps")
  if format == "off" then
    ChatSetTimestamps(nil)
  else
    ChatSetTimestamps(format)
  end
end

local function apply_highlights()
  -- Start from nothing, so a word removed from the setting stops being coloured.
  ChatHighlightClear()

  if ModSettingGet("highlight_name") then
    -- Your name is only known once you are in the world (nil at the main menu).
    local name = PlayerGetName()
    if name ~= nil then
      ChatHighlight(name, ModSettingGet("name_colour"))
    end
  end

  -- Every run of characters that are not commas or spaces is one word. The host takes up to 32 words per
  -- mod of up to 40 characters each; it leaves out any beyond that and tells the player once.
  for word in string.gmatch(ModSettingGet("words"), "[^,%s]+") do
    ChatHighlight(word, ModSettingGet("words_colour"))
  end
end

apply_timestamps()
apply_highlights()

-- Adds /stats to the chat. It runs only on your computer; nothing is sent.
-- Typing it calls OnCommand_stats below.
CommandRegister("stats", "shows how much chat you saw this session; /stats reset starts again")

-- You are in the world (after logging in, or after this mod reloads there): now your name is known.
function OnPlayerReady()
  apply_highlights()
end

-- A player's chat line arrived (yours too). Server and shop lines do not come here.
function OnChatMessage(msg)
  if msg.sender == PlayerGetName() then
    counts.mine = counts.mine + 1
  elseif msg.type == "private" then
    counts.whispers = counts.whispers + 1
  elseif msg.type == "global" then
    counts.global = counts.global + 1
  else
    counts.public = counts.public + 1
  end
  SessionSet("counts", counts)
end

local function reset()
  counts = new_counts()
  SessionSet("counts", counts)
  GamePrint("Chat stats start again from now.")
end

function OnCommand_stats(args)
  if args == "reset" then
    reset()
    return
  end
  -- TextDuration( seconds, "long" ) writes "42 min" or "1 h 06 min".
  -- GamePrintLink prints the line with a clickable "Start again" after it: a click calls reset, just as
  -- typing /stats reset does. Like everything here it runs on your computer only; nothing is sent.
  GamePrintLink(string.format("Chat in the last %s: public %d, global %d, whispers to you %d, your own lines %d.",
    TextDuration(os.time() - counts.started, "long"), counts.public, counts.global, counts.whispers, counts.mine),
    "Start again", reset)
end

-- You changed a setting on the Mods page (or in the settings file).
function OnSettingChanged(id, value)
  if id == "timestamps" then
    apply_timestamps()
  else
    apply_highlights()
  end
end
