-- The settings of this mod. They show on the Mods page (Options > Mods) and are kept in
-- BepInEx/config/mods.session_stats.cfg. init.lua reads them with ModSettingGet( "id" ).

mod_settings = {
  {
    id = "corner",
    ui_name = "Where it sits",
    ui_description = "The corner or side of the screen for the panel. You can also drag it; right-click puts it back.",
    value_default = "top_right",
    values = {
      { "top_right", "Top right" },
      { "top_left", "Top left" },
      { "right", "Right side" },
      { "left", "Left side" },
      { "bottom_left", "Bottom left" },
    },
  },
  { id = "show_time", ui_name = "Show session time", ui_description = "How long this session has lasted.", value_default = true },
  { id = "show_earned", ui_name = "Show earned", ui_description = "Currency earned this session (spending does not lower it).", value_default = true },
  { id = "show_per_hour", ui_name = "Show earned per hour", value_default = true },
  { id = "show_items", ui_name = "Show items gained", ui_description = "Items gained this session, and the last one.", value_default = true },
  { id = "show_total", ui_name = "Show all-time total", ui_description = "Currency earned since you installed this mod.", value_default = false },
}
