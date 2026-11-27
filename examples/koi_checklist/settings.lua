-- The settings of this mod. They show on the Mods page (Options > Mods) and are kept in
-- BepInEx/config/mods.koi_checklist.cfg. init.lua reads them with ModSettingGet( "id" ).

mod_settings = {
  {
    id = "open",
    ui_name = "Open the checklist",
    ui_description = "The key that opens and closes the koi checklist. /koi in chat does the same.",
    type = "key",
    value_default = "K",
  },
  {
    id = "start_missing",
    ui_name = "Start on missing koi",
    ui_description = "When the checklist opens, show only the koi you do not have yet.",
    value_default = false,
  },
  {
    id = "tooltip",
    ui_name = "Line in koi tooltips",
    ui_description = "Adds a line to a koi's tooltip saying whether it is in your collection.",
    value_default = true,
  },
}
