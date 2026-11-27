-- The settings of this mod. They show on the Mods page (Options > Mods) and are kept in
-- BepInEx/config/mods.chat_helper.cfg. init.lua reads them with ModSettingGet( "id" ).

-- Colours offered for the highlights: { "#rrggbb", "Label" }.
local colours = {
  { "#7fd4ff", "Sky blue" },
  { "#ffd27f", "Gold" },
  { "#a8e08c", "Green" },
  { "#ff9c9c", "Rose" },
  { "#d7a8ff", "Lilac" },
}

mod_settings = {
  {
    id = "timestamps",
    ui_name = "Timestamps",
    ui_description = "Puts the time before every chat line.",
    value_default = "%H:%M",
    values = {
      { "%H:%M", "14:05" },
      { "%H:%M:%S", "14:05:09" },
      { "%I:%M %p", "02:05 PM" },
      { "off", "Off" },
    },
  },
  { id = "highlight_name", ui_name = "Highlight my name", ui_description = "Colours your character's name wherever it appears in chat.", value_default = true },
  { id = "name_colour", ui_name = "Colour of my name", value_default = "#7fd4ff", values = colours },
  {
    id = "words",
    ui_name = "Also highlight",
    ui_description = "More words to colour, separated by commas or spaces (e.g. koi, trade).",
    value_default = "",
    text_max_length = 200,
  },
  { id = "words_colour", ui_name = "Colour of those words", value_default = "#ffd27f", values = colours },
}
