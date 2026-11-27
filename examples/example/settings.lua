-- The settings of this mod: one of each kind. They show on the Mods page (Menu > Settings > Mods) and are kept in
-- BepInEx/config/mods.example.cfg. init.lua reads them with ModSettingGet( "id" ) and hears about changes in
-- OnSettingChanged. What decides the kind is value_default (and values, or type = "key").

mod_settings = {
  -- true/false: a toggle.
  {
    id = "demo",
    ui_name = "Show the demo",
    ui_description = "Shows a text on the screen and every event in chat. Off: the mod shows nothing (it still writes the events to the log).",
    value_default = false,
  },

  -- A choice: a dropdown. Each entry is { "id", "Label" }; ModSettingGet gives the id.
  {
    id = "corner",
    ui_name = "Where the demo text sits",
    value_default = "top_left",
    values = {
      { "top_left", "Top left" },
      { "top_right", "Top right" },
      { "bottom_left", "Bottom left" },
      { "center", "Middle of the screen" },
    },
  },

  -- A whole number with a small range (10 or less): + and - buttons.
  {
    id = "size",
    ui_name = "Size of the demo text",
    value_default = 1,
    value_min = 1,
    value_max = 3,
  },

  -- A number with a bigger range: a slider.
  {
    id = "notice_seconds",
    ui_name = "How long the demo notice shows (seconds)",
    ui_description = "/example notice shows one.",
    value_default = 3,
    value_min = 1,
    value_max = 30,
  },

  -- A text: a field to type in. text_max_length is how many characters it takes (default 200).
  {
    id = "greeting",
    ui_name = "Greeting",
    ui_description = "The first line of the demo text.",
    value_default = "Hello from the example mod!",
    text_max_length = 60,
  },

  -- A key: a button that waits for the next key you press. The key name is Unity's: "F4", "L", "Digit8", "Space".
  -- Pressing it calls OnKey( "window_key" ) in init.lua.
  {
    id = "window_key",
    type = "key",
    ui_name = "Key for the demo window",
    ui_description = "Opens and closes the demo window while the demo is on.",
    value_default = "F4",
  },
}
