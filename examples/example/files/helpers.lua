-- Helpers of the example mod. init.lua loads this file with dofile_once( "files/helpers.lua" ): the file runs
-- once, and what it returns (the table below) is what dofile_once gives back. Put your own code in files/ the
-- same way when init.lua gets long.

local helpers = {}

-- { name = "Carrot", count = 2 } -> "Carrot x2"
function helpers.item_text(item)
  return item.name .. " x" .. item.count
end

-- true -> "on", false or nil -> "off"
function helpers.on_off(value)
  if value then
    return "on"
  end
  return "off"
end

return helpers
