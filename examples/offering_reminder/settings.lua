-- The settings of this mod. They show on the Mods page (Options > Mods) and are kept in
-- BepInEx/config/mods.offering_reminder.cfg. init.lua reads them with ModSettingGet( "id" ).

mod_settings = {
  { id = "show_countdown", ui_name = "Show the offering countdown", ui_description = "Time left in the current offering round.", value_default = true },
  { id = "show_weather", ui_name = "Show weather and daylight", ui_description = "Day, dusk, night or dawn, and whether it rains.", value_default = true },
  { id = "notice_start", ui_name = "Notice when a round starts", value_default = true },
  {
    id = "lead",
    ui_name = "Reminder before the end (seconds)",
    ui_description = "How long before a round ends the reminder shows. 0 switches the reminder off.",
    value_default = 30,
    value_min = 0,
    value_max = 120,
  },
  {
    id = "corner",
    ui_name = "Where it sits",
    value_default = "top_left",
    values = {
      { "top_left", "Top left" },
      { "top_right", "Top right" },
      { "left", "Left side" },
      { "right", "Right side" },
    },
  },
}
