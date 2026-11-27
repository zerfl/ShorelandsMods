Harbour Dusk UI  (an example mod: pictures only, no code)

What it changes
  A new UI theme, the harbour at dusk: navy-painted panels in brass frames, dusk-lavender glass in the slots and in
  the hotbar's brass portholes, a sea-foam ring on the selected slot, brass-rimmed round buttons, the menu plates
  (APPLY, BACK, OPTIONS, LOGIN, ...) as navy name-boards with brass corners, koistatue_layout with brass
  Deposit/Withdraw tabs, and a white arrow cursor. Light falls from the top left on every piece; outlines are 1 px.
  Left as they are on purpose: the green/teal/pink inventory tabs (their colours tell the bags apart), the teal
  backpack plate, the teal Deposit/Withdraw buttons of the CosmiKoi statue window, the chat window (the game tints
  it dark itself), and icons such as the backpack and the coins.

The art
  Every picture in art/textures/ is original pixel art, drawn from scratch by a script (examples/_tools/ui_skin.py in
  the ShorelandsMods source) with rectangles, circles and hand-placed pixel maps in the palette below. No pixel is
  taken from the game. The words on the menu plates, on BUY and in the koi statue window are lettered in the
  script's own pixel fonts.

  Palette (14 colours)
    ink       #171528  outlines
    navy_lo   #1f2444  pressed plates, wells
    navy      #2b3260  inventory_bg_1, menu plates, round buttons
    navy_hi   #3e4884  inventory_bg_3, plates pointed at
    dusk_lo   #4d5487  glass in shadow
    dusk      #6c72a6  inventory_bg_2, slots, porthole glass
    dusk_hi   #9198cc  lit lip of the glass
    mist      #c9cdef  glints, the search hint
    brass_dk  #6a3d22  rivet shadows
    brass_lo  #a5652b  brass in shade
    brass     #d9a03e  brass
    brass_hi  #ffe08e  lit brass
    foam      #7ef2d2  the selection ring, pressed words
    white     #ffffff  words, cursor

  Text contrast (the game writes white words on most plates and the black wallet number on inventory_bg_2):
    white on inventory_bg_1 12.2:1, on inventory_bg_3 8.5:1, on inventory_bg_2 4.6:1; black on inventory_bg_2 4.6:1;
    white stack counts on a slot 4.6:1; words on the menu plates 12.2:1 (pointed at 8.5:1, pressed 11.1:1).

Make your own
  1. Copy this folder and rename the copy (e.g. my_skin). Change name="..." in mod.xml.
  2. Every file in art/textures/ replaces the game texture with the same name. The names and sizes of all game
     textures are in the game folder: mods/_names.txt, section [textures]. Your PNG must have the same size.
  3. For a variant of this theme, change the colours in PALETTE at the top of examples/_tools/ui_skin.py and run
     python3 ui_skin.py (plain Python, no extra packages); python3 preview.py draws mock-ups of the shop, the
     inventory and the hotbar to look at before you install. Or open these PNGs in a pixel editor and paint on them.
  4. Keep the shapes the game relies on. inventory_bg_1/2/3 and inventory_slot are stretched from their middle: only
     their outer 2 pixels may carry detail, and the middle must be one colour. The hotbar's 7 sockets and the koi
     statue's slots, boxes, list and search field must stay where they are. inventory_bg_2 carries white and black
     words: keep its fill a middle tone so both stay readable.
  5. Save while the game runs and look: changes show within about a second. /mods my_skin in chat lists what applied.
  Which file is what:
    hotbar_latest = hotbar, highlight_hotbar = selection ring, pointer = cursor,
    inventory_bg_1 = shop list and inventory back, inventory_bg_2 = item details, wallet bar, settings,
    inventory_bg_3 = buttons and small plates (Menu, chat bar, zone name), inventory_slot / ui_slot = slots,
    ui_exit, buy, arrow_* = round buttons, koistatue_layout = a koi statue layout (232x94 art pixels shown 4x; the
    CosmiKoi statue window of game 0.69i is made of inventory_bg_1/2 and the slots instead),
    apply, back, confirm, graphics, audio, revert, login*, options* = the menu plates (the word is part of the
    picture; _hover is the look while pointed at, _click while pressed, drawn 1 px lower).
  Two names that differ only in upper/lower case (background / Background) cannot both be replaced on Windows.
