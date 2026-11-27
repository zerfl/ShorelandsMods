-- Offering reminder: a countdown for the offering round, notices, and a weather line.
--
-- How a Lua mod works: the host runs this file once when the mod loads, then calls the functions named
-- On...() below when something happens in the game. Everything the mod can call is listed in mods/_api.txt.
--
-- Offerings run in rounds on a clock everyone shares (the game shows the time left at the top left).
-- When a round ends, everyone who offered in it gets their results, and the next round starts at once.

local my_offering = OfferingHasOwn() == true  -- true while you have an offering in the running round
local shown = {}                              -- what each widget shows now, so the host is only asked when something changed

-- The end of the round we last reminded you about (in os.time() seconds). Kept with SessionSet, which the
-- host holds until the game closes, so a reload of the mod does not remind twice.
local reminded_for = SessionGet("reminded_for", 0)

local day_names = { day = "Day", to_night = "Dusk", night = "Night", to_day = "Dawn" }

-- Shows a widget with this text and colour, or hides it. HudText with the same id updates the widget.
-- No x/y: the host stacks the widgets in the chosen corner, clear of the game's HUD and of other mods.
local function show(id, visible, text, colour)
  if not visible then
    HudShow(id, false)
    shown[id] = nil
    return
  end
  local wanted = text .. colour .. ModSettingGet("corner")
  if shown[id] ~= wanted then
    shown[id] = wanted
    HudText(id, text, { anchor = ModSettingGet("corner"), color = colour, plate = true })
    HudShow(id, true)
  end
end

local function remind(left)
  local lead = ModSettingGet("lead")
  -- Two readings of the same round give the same end time, give or take a second.
  local round_end = math.floor(os.time() + left + 0.5)
  if lead == 0 or left > lead or math.abs(reminded_for - round_end) <= 2 then
    return
  end
  reminded_for = round_end
  SessionSet("reminded_for", reminded_for)
  if my_offering then
    NoticeShow("Your offering's results come in " .. math.ceil(left) .. " s.", 5)
  else
    NoticeShow("The offering round ends in " .. math.ceil(left) .. " s. Last chance to offer!", 5)
  end
end

-- Runs ten times a second (see TimerEvery below): reads the clock and the weather and updates the widgets.
local function update()
  local left = OfferingGetSecondsLeft()   -- nil outside the world, or for a moment between two rounds
  if left ~= nil then
    remind(left)
  end

  -- TextDuration( 252 ) writes "4:12", the way the game's timer counts.
  local text, colour
  if left == nil then
    text, colour = "Offering: waiting", "#c8c8c8"
  elseif my_offering then
    text, colour = "Your offering: results in " .. TextDuration(left), "#ffd27f"
  elseif left <= ModSettingGet("lead") then
    text, colour = "Offering ends in " .. TextDuration(left), "#ffb070"
  else
    text, colour = "Offering round: " .. TextDuration(left) .. " left", "#ffffff"
  end
  show("countdown", ModSettingGet("show_countdown"), text, colour)

  local state = DayStateGet()             -- nil outside the world
  if state ~= nil then
    local weather = day_names[state]
    if WeatherIsRaining() then
      weather = weather .. ", rain"
    else
      weather = weather .. ", clear"
    end
    show("weather", ModSettingGet("show_weather"), weather, "#a8d8ff")
  end
end

-- Make both widgets now, so they always stack in this order (countdown above weather).
show("countdown", ModSettingGet("show_countdown"), "Offering: waiting", "#c8c8c8")
show("weather", ModSettingGet("show_weather"), "Weather: waiting", "#a8d8ff")

-- Ten times a second, so the countdown changes together with the game's own timer. It costs next to
-- nothing: a few reads, and the host is only asked to redraw when a text changed.
TimerEvery(0.1, update)

-- You are in the world (after logging in, or after this mod reloads there): read your offering's state.
function OnPlayerReady()
  my_offering = OfferingHasOwn() == true
  update()
end

-- A new round started; seconds is its time to offer.
function OnOfferingRoundStarted(seconds)
  if ModSettingGet("notice_start") then
    NoticeShow("A new offering round started: " .. TextDuration(seconds) .. " to offer.", 5)
  end
  update()
end

-- true: your offering joined the running round (the game's timer turns blue);
-- false: you have none in it any more (the round ended and the results came).
function OnOfferingHasOwnChanged(has_own)
  my_offering = has_own
  update()
end

-- You changed a setting on the Mods page (or in the settings file): draw the widgets again.
function OnSettingChanged(id, value)
  shown = {}
  update()
end
