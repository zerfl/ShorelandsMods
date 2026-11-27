-- Example: the starter mod. Copy this folder, rename the copy (lower case letters, digits and _ only, for
-- example my_mod), change name="..." in its mod.xml, and keep what you need from this file.
--
-- How a Lua mod works: the host runs this file once when the mod loads (when the game starts, and again each
-- time you save a file of the mod). After that it calls the On...() functions below when something happens.
-- Every one of them is optional: delete the ones you do not need. mods/_api.txt lists every function and
-- callback; mods/_guide.html explains them with examples.
--
-- As it ships, this mod shows nothing. Switch on "Show the demo" in its settings (Menu > Settings > Mods >
-- Example) and it shows a text on the screen and every event in chat. With the demo off, each callback still
-- writes one line to the log (BepInEx/LogOutput.log in the game folder), so you can see when they run.
-- Callbacks that run very often (OnTick, OnFrame) are commented out: remove the -- in front to try them.

-- Your own helper code can live in files/. dofile_once runs the file once and gives back what it returns.
local helpers = dofile_once("files/helpers.lua")

-- `local` keeps a variable inside this file. Only the On...() functions are global, on purpose: that is how
-- the host finds them.

-- Saved data stays for good (mods_data/example/data.json): here, how many times this mod has loaded.
local loads = DataLoad("loads", 0) + 1
DataSave("loads", loads)

-- Session data stays until the game closes, also when the mod reloads: here, when this game session started.
local started = SessionGet("started")
if started == nil then
  started = os.time()
  SessionSet("started", started)
end

print("loaded, " .. loads .. " times so far")

-- Writes a line to the log; with the demo on, also in your chat (only you see it; nothing is sent). The host puts
-- "[example]" before every chat line of this mod.
local function event(text)
  print(text)
  if ModSettingGet("demo") then
    GamePrint(text)
  end
end

-- The demo text on the screen. HudText with the same id ("demo") updates it instead of making another.
local function update_demo()
  if not ModSettingGet("demo") then
    HudRemove("demo")
    return
  end
  local lines = { ModSettingGet("greeting") }
  local zone = ZoneGet()  -- nil at the main menu: reads of the world only work once you are in it
  if zone == nil then
    table.insert(lines, "Not in the world yet")
  else
    table.insert(lines, "Zone: " .. zone .. ", you are " .. PlayerGetActivity())
    table.insert(lines, "Currency: " .. TextNumber(WalletGetCurrency()))
  end
  table.insert(lines, "Session: " .. TextDuration(os.time() - started, "long") .. ", load number " .. loads)
  HudText("demo", table.concat(lines, "\n"), {
    anchor = ModSettingGet("corner"),
    size = ModSettingGet("size"),
    plate = true,
    draggable = true,  -- you can drag it; right-click puts it back
  })
end

-- Every 10 seconds, so the session time moves on.
TimerEvery(10, update_demo)
update_demo()

-- A window: made once here, filled and opened later. The player can drag it and close it with X or Escape.
WindowCreate("demo", "Example window", 240, 200)
WindowSetText("demo", "Your hotbar. Type in the field to filter the list.")
WindowAddSearch("demo", "Search the hotbar")  -- no function given: the host filters the list itself

local function fill_window()
  local items = InventoryGetItems("hotbar")  -- nil at the main menu
  if items == nil then
    WindowSetList("demo", { "Not in the world yet" })
    return
  end
  local rows = {}
  for _, item in ipairs(items) do
    -- icon = the item id; for a koi (item 9999), meta says which koi.
    table.insert(rows, { text = helpers.item_text(item), icon = item.id, meta = item.meta })
  end
  WindowSetList("demo", rows)
end

WindowAddButton("demo", "refresh", "Refresh", fill_window)
WindowAddButton("demo", "close", "Close", function()
  WindowClose("demo")
end)

-- A chat command, /example. It runs on your computer only; nothing is sent to the server.
CommandRegister("example", "the example mod: /example, /example notice, /example window")

-- Things this mod could do but does not as shipped (remove the -- to try one):
-- ChatSetTimestamps("%H:%M")                     -- the time before every chat line
-- ChatHighlight("koi", "#ffd27f")                -- the word koi in gold wherever it shows in chat
-- ExportText("hello", "Hello, OBS")              -- writes mods_data/example/export/hello.txt for OBS
-- SoundPlay("files/ding.ogg", 0.5)               -- plays a sound file of this mod (put one there first)
-- TimerAfter(5, function() NoticeShow("Five seconds after loading") end)


---------------------------------------------------------------------------------------------------------------
-- Callbacks. The host calls each one when its moment comes, if this file defines it.
---------------------------------------------------------------------------------------------------------------

-- You are in the world: once after each login, and again whenever this mod loads or reloads while you are in
-- the world. Before it, reads like WalletGetCurrency() give nil. It may take longer than the others (250 ms),
-- so it is the place to build big lists, for example from KoiGetAll().
function OnPlayerReady()
  event("OnPlayerReady: hello, " .. PlayerGetName())
  update_demo()
end

-- You arrived in a zone ("town", "playerland" = the islands, "holyland", "unknown"), also the first after login.
function OnZoneChanged(zone)
  event("OnZoneChanged: " .. zone)
  update_demo()
end

-- What you are doing changed: "idle", "walking", "farming", "fishing" or "talking".
function OnActivityChanged(activity)
  event("OnActivityChanged: " .. activity)
  update_demo()
end

-- Your wallet changed: the new currency and CITB, and the change of each (a purchase gives a negative
-- currency_change). Also when only the koi in your statue changed (both changes 0).
function OnWalletChanged(currency, citb, currency_change, citb_change)
  event("OnWalletChanged: currency " .. currency .. " (" .. currency_change .. "), CITB " .. citb .. " (" .. citb_change .. ")")
  update_demo()
end

-- You have more of an item than before (a catch, a harvest, a purchase, a trade, a reward):
-- item = { id, name, count (how many more), meta, category }.
function OnItemGained(item)
  event("OnItemGained: " .. helpers.item_text(item) .. " (item " .. item.id .. ")")
end

-- Something in your bags changed (gained, lost, used or moved). Read InventoryGetItems to see what.
function OnInventoryChanged()
  event("OnInventoryChanged")
  if WindowIsOpen("demo") then
    fill_window()
  end
end

-- A player's chat line arrived: msg = { type = "public", "global" or "private", sender, text }. Your own lines
-- too. Server, info and shop lines never arrive here. (This log line leaves the text out, so a log you share
-- holds nobody's chat.)
function OnChatMessage(msg)
  event("OnChatMessage: a " .. msg.type .. " line from " .. msg.sender)
end

-- The day/night light changed: level from 0 (dark) to 1 (full day). Steps during dusk and dawn, once at login.
function OnLightChanged(level)
  event("OnLightChanged: " .. level)
end

-- Rain started (true) or stopped (false) where you are.
function OnRainChanged(raining)
  event("OnRainChanged: " .. tostring(raining))
end

-- true: an offering of yours joined the running round; false: you have none in it any more.
function OnOfferingHasOwnChanged(has_own)
  event("OnOfferingHasOwnChanged: " .. tostring(has_own))
end

-- A new offering round started; seconds is its time left.
function OnOfferingRoundStarted(seconds)
  event("OnOfferingRoundStarted: " .. TextDuration(seconds) .. " to offer")
end

-- Ten times a second; seconds since the last time (about 0.1). Keep it small: read, compare, update on a change.
-- function OnTick(seconds)
-- end

-- Every frame (60 or more times a second). Its time counts against this mod's budget; you rarely need it.
-- function OnFrame(seconds)
-- end

-- The key of one of this mod's type = "key" settings was pressed; id is that setting's id. Not while you type
-- in chat or a text field. A mod never hears any other key.
function OnKey(id)
  event("OnKey: " .. id)
  if id == "window_key" and ModSettingGet("demo") then
    fill_window()
    WindowToggle("demo")
  end
end

-- You changed one of this mod's settings on the Mods page (or in its .cfg file while the game runs).
function OnSettingChanged(id, value)
  event("OnSettingChanged: " .. id .. " = " .. tostring(value))
  if id == "demo" and not value then
    WindowClose("demo")
  end
  update_demo()
end

-- You point at an item and its tooltip shows: item = { id, name, meta, category }. Return one line to add
-- below the tooltip (at most 120 characters), or nil for none.
function OnItemTooltip(item)
  if ModSettingGet("demo") then
    return "Example mod: this is item " .. item.id
  end
  return nil
end

-- You typed /example in chat; args is the rest of the line ("" if none). The name after OnCommand_ is the
-- name given to CommandRegister above.
function OnCommand_example(args)
  if args == "notice" then
    NoticeShow(ModSettingGet("greeting"), ModSettingGet("notice_seconds"))
  elseif args == "window" then
    fill_window()
    WindowToggle("demo")
  else
    GamePrint("Example mod: load number " .. loads .. ", the demo is " .. helpers.on_off(ModSettingGet("demo"))
      .. ". Try /example notice or /example window.")
  end
end

-- A window of this mod closed: by_player is true for its X or Escape, false when the mod closed it.
function OnWindowClosed(id, by_player)
  event("OnWindowClosed: " .. id .. ", by you: " .. tostring(by_player))
end

-- You typed in a window's search field (one made without a function): text is what is in it, shown how many
-- rows match. It runs on every key, so only define it if you show the text or the count.
function OnWindowSearchChanged(id, text, shown)
  event("OnWindowSearchChanged: " .. id .. " \"" .. text .. "\", " .. shown .. " rows")
end

-- The mod is about to reload (you saved a file), be switched off, or the game closes. Everything it made
-- (texts, windows, timers, its command) goes away after this; saved data and session data stay.
function OnModUnload()
  print("OnModUnload")
end
