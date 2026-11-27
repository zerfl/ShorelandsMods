-- Session stats: a small panel with what you earned this session.
--
-- How a Lua mod works: the host runs this file once when the mod loads, then calls the functions named
-- On...() below when something happens in the game. Everything the mod can call is listed in mods/_api.txt.
--
-- "This session" means since you started the game. The figures are kept with SessionSet, which the host
-- holds until the game closes: they carry on when the mod reloads (for example after you edit this file),
-- when you switch it off and on, or when you log out and back in, and start again with the next game.

-- `local` keeps these variables inside this file. Only the On...() functions below are globals on
-- purpose: that is how the host finds them.
local session = SessionGet("session")  -- { started, earned, items, last_item }; nil until you are in the world
local all_time = DataLoad("all_time", 0)  -- DataSave keeps this one for good, across game restarts

-- Builds the panel's text from the settings and the figures, and shows it.
local function refresh()
  if session == nil then
    return
  end
  local elapsed = os.time() - session.started
  local lines = {}

  if ModSettingGet("show_time") then
    table.insert(lines, "Session: " .. TextDuration(elapsed, "long"))   -- "1 h 06 min"
  end
  if ModSettingGet("show_earned") then
    table.insert(lines, "Earned: " .. TextNumber(session.earned))      -- "1,750"
  end
  if ModSettingGet("show_per_hour") then
    -- The first minutes would give wild numbers (one sale in a minute = 60 an hour), so wait a little.
    if elapsed < 300 then
      table.insert(lines, "Per hour: wait 5 min")
    else
      table.insert(lines, "Per hour: " .. TextNumber(session.earned * 3600 / elapsed))
    end
  end
  if ModSettingGet("show_items") then
    local line = "Items: " .. session.items
    if session.last_item ~= "" then
      line = line .. " (last: " .. session.last_item .. ")"
    end
    table.insert(lines, line)
  end
  if ModSettingGet("show_total") then
    table.insert(lines, "All time: " .. TextNumber(all_time))
  end

  if #lines == 0 then
    HudShow("panel", false)
    return
  end
  -- The same id again updates the panel. No x/y: the host finds a free place in that corner, clear of the
  -- game's HUD and of other mods' widgets.
  HudText("panel", table.concat(lines, "\n"), { anchor = ModSettingGet("corner"), plate = true, draggable = true })
  HudShow("panel", true)
end

local function save()
  SessionSet("session", session)
  DataSave("all_time", all_time)
end

-- The session time and the per-hour figure change slowly; every 10 seconds is plenty.
-- Money and items update the panel at once (see below).
TimerEvery(10, refresh)

-- You are in the world: after logging in, and right after this mod (re)loads while you are in the world.
-- Before this, reads like WalletGetCurrency() give nil.
function OnPlayerReady()
  if session == nil then
    session = { started = os.time(), earned = 0, items = 0, last_item = "" }
    save()
  end
  refresh()
end

-- Your wallet changed: currency_change says by how much (a purchase is negative). It also fires for CITB
-- and the koi in your statue, with a currency_change of 0.
function OnWalletChanged(currency, citb, currency_change)
  if session ~= nil and currency_change > 0 then
    session.earned = session.earned + currency_change
    all_time = all_time + currency_change
    save()
    refresh()
  end
end

-- You have more of an item than before: a catch, a harvest, a purchase, a trade, a reward.
function OnItemGained(item)
  if session == nil then
    return
  end
  session.items = session.items + item.count
  session.last_item = item.name
  save()
  refresh()
end

-- You changed a setting on the Mods page (or in the settings file): show the panel the new way.
function OnSettingChanged(id, value)
  refresh()
end
