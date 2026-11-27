-- The settings of this mod. They show on the Mods page (Options > Mods) and are kept in
-- BepInEx/config/mods.stream_ticker.cfg. init.lua reads them with ModSettingGet( "id" ).

mod_settings = {
  {
    id = "item_format",
    ui_name = "Text of last_item.txt",
    ui_description = "{name} is the item, {count} how many you got.",
    value_default = "Last item: {name} x{count}",
    text_max_length = 100,
  },
  {
    id = "currency_format",
    ui_name = "Text of currency.txt",
    ui_description = "{currency} is your currency, {citb} your CITB.",
    value_default = "Currency: {currency}",
    text_max_length = 100,
  },
  { id = "commas", ui_name = "Commas in numbers", ui_description = "Writes 45,380 instead of 45380.", value_default = true },
}
