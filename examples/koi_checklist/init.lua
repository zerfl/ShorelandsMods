-- Koi checklist: a window with every koi, marked owned or missing, and a line in koi tooltips.
--
-- How a Lua mod works: the host runs this file once when the mod loads, then calls the functions named
-- On...() below when something happens in the game. Everything the mod can call is listed in mods/_api.txt.
--
-- What counts as "yours": every koi in your bags (the koi bag, and the hotbar, where a koi you just bought
-- often sits) and every koi stored in your statue. The game tells your client exactly which koi those are,
-- one by one, so the marks are exact. Koi you put up on a market stall are in neither place, and the mod
-- cannot see them.
--
-- Speed: Lua here runs at roughly a microsecond per table access, so one pass over 1,800 koi costs about
-- 10 ms, half of a callback's 20 ms budget. So the rows are made once, a change rewrites only the rows of
-- the koi that changed, and each filtered list is kept until the koi you own change. A call that uses more
-- than half of its budget gets a line in chat; /mods koi_checklist lists the slowest calls.

local WINDOW = "koi"  -- the id of our window; the host knows it by this name

-- KoiGetAll gives each koi's colour as the game's number, 1 to 8 (see _api.txt). These are the names.
local COLOURS = { "Blue", "Dark blue", "Aqua", "Green", "Yellow", "Red", "Pink", "Purple" }

local OWNED_COLOUR = "#f2d27a"    -- warm gold for koi you have
local MISSING_COLOUR = "#9fb0c2"  -- a quiet grey-blue for koi you do not have yet

-- `local` keeps these variables inside this file. Only the On...() functions are globals on purpose:
-- that is how the host finds them.
local koi = nil          -- every koi from KoiGetAll: { id, name, type }, by id; nil until you are in the world
local rows = {}          -- the window's row for each koi, made once: rows[i] is the row of koi[i]
local index_of = {}      -- koi id -> its place in koi and rows
local of_colour = {}     -- colour -> the places of its koi, for the colour filter
local owned = {}         -- place -> whether you have that koi
local have, have_of = 0, {}  -- how many different koi you have, in all and per colour
local in_bags = {}       -- koi id -> how many you carry
local in_statue = {}     -- koi id -> how many are in your statue
local carried, stored = 0, 0
local lists = {}         -- filtered lists already made, by view ("missing3"); emptied when your koi change
local pending = nil      -- a refresh that is waiting (see OnInventoryChanged)

-- What the window shows: status "all", "owned" or "missing", colour 0 (every colour) or 1 to 8.
-- SessionGet: a hot reload of this file keeps the view the player had (see OnModUnload).
local view = SessionGet("view", { status = "all", colour = 0 })

-- Reopen the window after a hot reload if it was open (OnModUnload remembers it). Only in the world:
-- windows are not drawn at the main menu.
local reopen = SessionGet("open", false) and ZoneGet() ~= nil
SessionSet("open", false)

-- "2 in bags, 1 in statue", or nil for a koi you do not have.
local function where(id)
  local bag, statue = in_bags[id] or 0, in_statue[id] or 0
  if bag > 0 and statue > 0 then
    return bag .. " in bags, " .. statue .. " in statue"
  elseif bag > 0 then
    return bag .. " in bags"
  elseif statue > 0 then
    return statue .. " in statue"
  end
  return nil
end

-- Writes one koi's row and keeps the totals. A row is a plain table of the fields WindowSetList knows;
-- the host copies it when we pass the list, so a change here shows the next time we call WindowSetList.
local function write_row(i)
  local k = koi[i]
  local w = where(k.id)
  local row = rows[i]
  if w then
    row.text = "#" .. k.id .. "  " .. k.name .. "  ·  " .. COLOURS[k.type] .. "  ·  owned, " .. w
    row.color = OWNED_COLOUR
  else
    row.text = "#" .. k.id .. "  " .. k.name .. "  ·  " .. COLOURS[k.type] .. "  ·  missing"
    row.color = MISSING_COLOUR
  end
  local now = w ~= nil
  if now ~= owned[i] then
    local step = now and 1 or -1
    have = have + step
    have_of[k.type] = have_of[k.type] + step
    owned[i] = now
  end
end

-- Reads your bags and your statue. Rewrites only the rows of koi whose numbers changed, and says whether
-- any did. Most inventory changes (a harvest, a seed planted) change no koi at all.
local function read_owned()
  local items = InventoryGetItems("all")  -- every bag: hotbar, farming, koi, pets
  local statue = WalletGetKoiCounts()     -- { [koi id] = how many } in your statue
  if items == nil or statue == nil then   -- nil: not in the world
    return false
  end
  local bags = {}
  carried, stored = 0, 0
  for _, item in ipairs(items) do
    if item.id == 9999 then  -- 9999 is the koi item; meta says which koi it is
      bags[item.meta] = (bags[item.meta] or 0) + item.count
      carried = carried + item.count
    end
  end

  local changed = {}
  for id, n in pairs(bags) do if in_bags[id] ~= n then changed[id] = true end end
  for id in pairs(in_bags) do if bags[id] == nil then changed[id] = true end end
  for id, n in pairs(statue) do
    stored = stored + n
    if in_statue[id] ~= n then changed[id] = true end
  end
  for id in pairs(in_statue) do if statue[id] == nil then changed[id] = true end end
  in_bags, in_statue = bags, statue

  local any = false
  for id in pairs(changed) do
    local i = index_of[id]
    if i then
      write_row(i)
      any = true
    end
  end
  if any then
    lists = {}
  end
  return any
end

-- The rows of a view. "All koi, every colour" is simply every row; the others are made once and kept.
local function list_of(status, colour)
  if status == "all" and colour == 0 then
    return rows
  end
  local key = status .. colour
  if lists[key] then
    return lists[key]
  end
  local list = {}
  local want = status == "owned"
  if colour == 0 then
    for i = 1, #rows do
      if owned[i] == want then list[#list + 1] = rows[i] end
    end
  else
    for _, i in ipairs(of_colour[colour]) do
      if status == "all" or owned[i] == want then list[#list + 1] = rows[i] end
    end
  end
  lists[key] = list
  return list
end

-- The totals above the list. Also called on every keystroke in the search (see OnWindowSearchChanged),
-- so it only counts a few things and asks the host how many rows match.
local function write_header()
  local list = list_of(view.status, view.colour)

  -- "Blue 151/754" for each colour that has koi at all (the game has a colour without any).
  local parts = {}
  for t, name in ipairs(COLOURS) do
    if #of_colour[t] > 0 then
      parts[#parts + 1] = name .. " " .. have_of[t] .. "/" .. #of_colour[t]
    end
  end

  local showing = ({ all = "All koi", owned = "Owned koi", missing = "Missing koi" })[view.status]
  if view.colour > 0 then
    showing = showing .. ", " .. COLOURS[view.colour] .. " only"
  else
    showing = showing .. ", every colour"
  end
  -- The host's search narrows the list further; WindowGetSearch says what is typed and how many rows match.
  local search, matching = WindowGetSearch(WINDOW)
  if search ~= "" then
    showing = showing .. ": " .. TextNumber(matching) .. " of " .. TextNumber(#list) .. " shown."
  else
    showing = showing .. ": " .. TextNumber(#list) .. ". Type above to search them."
  end

  WindowSetText(WINDOW, table.concat({
    "You have " .. TextNumber(have) .. " of " .. TextNumber(#koi) .. " koi; " .. TextNumber(#koi - have) .. " missing.",
    "Carried: " .. carried .. ".  In your statue: " .. stored .. ".",
    table.concat(parts, "   ", 1, math.min(4, #parts)),
    table.concat(parts, "   ", 5, #parts),
    showing,
  }, "\n"))
end

-- Gives the window its list, the totals above it and the buttons' look, for the current view.
local function show()
  if koi == nil then
    return
  end
  WindowSetList(WINDOW, list_of(view.status, view.colour))
  write_header()  -- after WindowSetList, so the search count is of the new list

  -- The buttons of the current view look pressed; the colour button names the colour shown.
  for _, status in ipairs({ "all", "owned", "missing" }) do
    WindowSetButton(WINDOW, status, { selected = view.status == status })
  end
  if view.colour > 0 then
    WindowSetButton(WINDOW, "colour", { label = "Colour: " .. COLOURS[view.colour], selected = true })
  else
    WindowSetButton(WINDOW, "colour", { label = "By colour", selected = false })
  end
  WindowSetButton(WINDOW, "every", { selected = view.colour == 0 })
end

local function set_view(status, colour)
  view.status, view.colour = status, colour
  show()
end

-- The next colour that has koi: after "every colour" (0) comes Blue (1), after Purple every colour again.
local function next_colour(c)
  repeat
    c = (c + 1) % (#COLOURS + 1)
  until c == 0 or #of_colour[c] > 0
  return c
end

local function toggle()
  -- Windows are drawn only in the world; at the main menu the key does nothing.
  if ZoneGet() == nil then
    return
  end
  if WindowIsOpen(WINDOW) then
    WindowClose(WINDOW)  -- the player can also close it with Escape or its X
  else
    -- The window starts on the view the settings ask for, every time it opens.
    set_view(ModSettingGet("start_missing") and "missing" or "all", 0)
    WindowOpen(WINDOW)
  end
end

-- The window is made once, here; it stays closed until WindowOpen. Sizes are in UI units: the screen is
-- 640x360 of them (800x450 at 1600x900), so 330x300 fits every size the game offers.
WindowCreate(WINDOW, "Koi checklist", 330, 300)
-- No function: the host filters the rows by the words typed (name, #id, colour, "owned", "missing").
WindowAddSearch(WINDOW, "Search: name, #id, colour, missing")
-- Each button has a name ("all", "colour") so show() can change its look and its label later.
WindowAddButton(WINDOW, "all", "All", function() set_view("all", view.colour) end)
WindowAddButton(WINDOW, "owned", "Owned", function() set_view("owned", view.colour) end)
WindowAddButton(WINDOW, "missing", "Missing", function() set_view("missing", view.colour) end)
WindowAddButton(WINDOW, "colour", "By colour", function() set_view(view.status, next_colour(view.colour)) end)
WindowAddButton(WINDOW, "every", "All colours", function() set_view(view.status, 0) end)

CommandRegister("koi", "Opens or closes the koi checklist.")

-- You are in the world: after logging in, and right after this mod (re)loads while you are in the world.
-- The game's koi table exists only from now on, and this callback has a bigger time budget (250 ms) than
-- the others (20 ms), so this is where the 1,800 rows are made, once.
function OnPlayerReady()
  if koi == nil then
    local all = KoiGetAll()
    if all == nil then
      return
    end
    koi = {}
    for t = 1, #COLOURS do
      of_colour[t], have_of[t] = {}, 0
    end
    for _, k in ipairs(all) do
      -- The table starts with #0 "None", the game's empty entry, without a colour: it is no koi.
      if COLOURS[k.type] then
        local i = #koi + 1
        koi[i] = k
        index_of[k.id] = i
        rows[i] = { koi = k.id }  -- koi = id: the host draws that koi's icon
        owned[i] = false
        table.insert(of_colour[k.type], i)
      end
    end
  end
  -- After a relogin your bags may hold other koi: read them and write every row once.
  in_bags, in_statue = {}, {}
  read_owned()
  for i = 1, #koi do
    write_row(i)
  end
  lists = {}
  if reopen then
    reopen = false
    show()
    WindowOpen(WINDOW)
  elseif WindowIsOpen(WINDOW) then
    show()
  end
end

-- Runs a little after the last of a burst of changes (see below). A closed window is left alone: it gets
-- its list when it opens.
local function refresh()
  pending = nil
  if koi ~= nil and read_owned() and WindowIsOpen(WINDOW) then
    show()
  end
end

-- Something in your bags changed: a koi bought or caught, moved to the statue, but also every harvest and
-- every seed. It can come every frame, so we only start a short timer, and one read covers the whole burst.
function OnInventoryChanged()
  if pending == nil then
    pending = TimerAfter(0.25, refresh)
  end
end

-- Also called when only the koi in your statue changed (both changes are 0 then).
function OnWalletChanged()
  OnInventoryChanged()
end

-- The key setting "open" was pressed (the player can rebind it on the Mods page).
function OnKey(id)
  if id == "open" then
    toggle()
  end
end

-- The player typed /koi.
function OnCommand_koi(args)
  toggle()
end

-- The player typed in the window's search: the header says how many koi match.
function OnWindowSearchChanged(id, text, shown)
  if id == WINDOW and koi ~= nil then
    write_header()
  end
end

-- The player hovers an item: return one line to add to its tooltip, or nil for none.
function OnItemTooltip(item)
  if item.id ~= 9999 or koi == nil or not ModSettingGet("tooltip") then
    return nil
  end
  local w = where(item.meta)
  if w then
    return "In your collection: " .. w
  end
  return "Missing from your collection"
end

-- A setting changed on the Mods page or in the settings file. The key and the tooltip switch are read when
-- they are used; "start on missing" also changes the open window at once.
function OnSettingChanged(id, value)
  if id == "start_missing" and WindowIsOpen(WINDOW) then
    set_view(value and "missing" or "all", view.colour)
  end
end

-- Before a reload (for example after you save this file): remember whether the window was open and what it
-- showed, so the new version picks up where this one left off. The host closes our window either way.
function OnModUnload()
  SessionSet("open", WindowIsOpen(WINDOW) == true)
  SessionSet("view", view)
end
