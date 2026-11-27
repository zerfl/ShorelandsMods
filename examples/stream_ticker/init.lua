-- Stream ticker: keeps two text files up to date for OBS (or any program that shows a text file).
--
-- How a Lua mod works: the host runs this file once when the mod loads, then calls the functions named
-- On...() below when something happens in the game. Everything the mod can call is listed in mods/_api.txt.
--
-- ExportText( "currency", text ) writes <game>/mods_data/stream_ticker/export/currency.txt. It is the only
-- way a mod can hand something to another program.

-- The last item you got, kept in saved data so the file is right again after a restart of the game.
local last_item = DataLoad("last_item", { name = "", count = 0 })

-- 45380 -> "45,380" when the setting asks for commas (TextNumber writes them), else "45380".
local function number_text(number)
  if ModSettingGet("commas") then
    return TextNumber(number)
  end
  return tostring(math.floor(number))
end

-- fill( "Last item: {name}", { name = "Carrot" } ) -> "Last item: Carrot".
-- A {word} that is not in values stays as it is, so a typo in the setting is easy to spot.
local function fill(format, values)
  local text = string.gsub(format, "{(%w+)}", function(word)
    if values[word] ~= nil then
      return tostring(values[word])
    end
    return nil  -- nil keeps "{word}" unchanged
  end)
  return text
end

local function write_currency()
  local currency = WalletGetCurrency()  -- nil when you are not in the world
  if currency == nil then
    return
  end
  local values = { currency = number_text(currency), citb = number_text(WalletGetCitb()) }
  ExportText("currency", fill(ModSettingGet("currency_format"), values))
end

local function write_last_item()
  if last_item.name == "" then
    ExportText("last_item", "")  -- nothing yet; an empty file still lets you set up OBS
    return
  end
  local values = { name = last_item.name, count = number_text(last_item.count) }
  ExportText("last_item", fill(ModSettingGet("item_format"), values))
end

-- The last item is known already (saved data); the currency only once you are in the world.
write_last_item()

-- You are in the world (after logging in, or after this mod reloads there): write both files.
function OnPlayerReady()
  write_currency()
  write_last_item()
end

-- Your wallet changed.
function OnWalletChanged(currency, citb)
  write_currency()
end

-- You have more of an item than before: a catch, a harvest, a purchase, a trade, a reward.
function OnItemGained(item)
  last_item = { name = item.name, count = item.count }
  DataSave("last_item", last_item)
  write_last_item()
end

-- You changed a setting on the Mods page (or in the settings file): write the files the new way.
function OnSettingChanged(id, value)
  write_currency()
  write_last_item()
end
