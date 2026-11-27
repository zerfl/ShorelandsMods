"""Builds examples/ui_skin, "Harbour Dusk": navy-painted panels in brass frames, dusk-lavender glass for slots and
the hotbar's portholes, sea-foam for whatever is selected or pressed. Every pixel is drawn here from the palette below
with rectangles, discs and hand-placed pixel maps; nothing is read from the game. Light comes from the top left.

Layout facts the textures keep (measured on the game's sprites; break them and things sit wrong in the game):
- inventory_bg_1/2/3 and inventory_slot are 9-sliced with a 2 px border: only the outer 2 px ring may carry detail,
  each edge must be the same along its length, and the centre must be one flat colour (it is stretched).
- inventory_bg_2 carries white words (shop details, settings) and the black wallet number (inventory header): its
  fill must sit near luminance 0.18, where both reach about 4.5:1.
- hotbar_latest: 7 sockets 22 px wide, centres x = 26.5 + 30 k, y = 15.5; highlight_hotbar's ring (26 px) is placed
  over a socket. pointer: the hotspot is the pixel corner (3, 2) from the top left, so the tip pixel is (3, 2).
- The 64x32 menu plates carry their word in the picture (normal and hover rows 5-26; click is the same 1 px lower).
- koistatue_layout is 232x94 art pixels shown 4x: slots, the Deposit/Withdraw boxes, the list and the search field
  keep their rectangles (the game puts its own items and input over them).
"""
import math

from font import BOLD, SMALL, measure, text
from pixel import EXAMPLES, Canvas, contrast, rgba

MOD = EXAMPLES / "ui_skin"

PALETTE = {
    "ink": "#171528",  # outlines
    "navy_lo": "#1f2444",
    "navy": "#2b3260",  # inventory_bg_1, menu plates
    "navy_hi": "#3e4884",  # inventory_bg_3, hover
    "dusk_lo": "#4d5487",
    "dusk": "#6c72a6",  # inventory_bg_2, slots, porthole glass
    "dusk_hi": "#9198cc",
    "mist": "#c9cdef",
    "brass_dk": "#6a3d22",
    "brass_lo": "#a5652b",
    "brass": "#d9a03e",
    "brass_hi": "#ffe08e",
    "foam": "#7ef2d2",  # selection ring, pressed words
    "white": "#ffffff",
}
P = {k: rgba(v) for k, v in PALETTE.items()}


def nine(size, fill, outline, lit, shade, mixed):
    """A 9-slice plate: transparent corner pixel, 1 px outline, 1 px bevel (lit top/left, shade bottom/right)."""
    n = size - 1
    c = Canvas(size, size)
    c.rect(0, 0, n, n, P[fill])
    c.frame(0, 0, n, n, P[outline])
    c.frame(1, 1, n - 1, n - 1, P[lit])
    for i in range(1, n):
        c.set(i, n - 1, P[shade])
        c.set(n - 1, i, P[shade])
    c.set(n - 1, 1, P[mixed])
    c.set(1, n - 1, P[mixed])
    for x, y in [(0, 0), (n, 0), (0, n), (n, n)]:
        c.set(x, y, None)
    return c


def slot_tile(c, x0, y0, w, h, fill="dusk", groove="navy_lo", lip="dusk_hi", shadow="dusk_lo"):
    """An inset glass tile: dark groove top/left, lit lip bottom/right, a shadow line inside the groove. The corner
    pixels keep what was there (transparent on an empty canvas)."""
    x1, y1 = x0 + w - 1, y0 + h - 1
    corners = [(x, y, c.get(x, y)) for x, y in [(x0, y0), (x1, y0), (x0, y1), (x1, y1)]]
    c.rect(x0, y0, x1, y1, P[fill])
    for x in range(x0, x1 + 1):
        c.set(x, y0, P[groove])
        c.set(x, y1, P[lip])
        c.set(x, y0 + 1, P[shadow])
    for y in range(y0, y1 + 1):
        c.set(x0, y, P[groove])
        c.set(x1, y, P[lip])
        if y > y0:
            c.set(x0 + 1, y, P[shadow])
    for x, y, was in corners:
        c.set(x, y, was)


def panels():
    return {
        "inventory_bg_1": nine(96, "navy", "ink", "brass_hi", "brass_lo", "brass"),
        "inventory_bg_2": nine(96, "dusk", "ink", "brass_hi", "brass_lo", "brass"),
        "inventory_bg_3": nine(96, "navy_hi", "ink", "dusk", "navy_lo", "dusk_lo"),
    }


def slots():
    inv = Canvas(24, 24)
    slot_tile(inv, 0, 0, 24, 24)
    well = Canvas(24, 24)
    slot_tile(well, 0, 0, 24, 24, fill="navy_hi", groove="navy_lo", lip="dusk", shadow="navy")
    return {"inventory_slot": inv, "ui_slot": well}


# Round 11x11 buttons: ink rim, brass ring lit from the top left, navy face, white glyph.
ROUND = [
    "..KKKKKKK..",
    ".KHHHHHBBK.",
    "KHHfffffBLK",
    "KHfffffffLK",
    "KHfffffffLK",
    "KHfffffffLK",
    "KBfffffffLK",
    "KBfffffffLK",
    "KBLfffffLLK",
    ".KLLLLLLLK.",
    "..KKKKKKK..",
]
# The page arrows on the shop list: a plain dusk disc with a darker rim below.
ROUND_ALT = [
    "..fffffff..",
    ".fffffffff.",
    "fffffffffff",
    "fffffffffff",
    "fffffffffff",
    "fffffffffff",
    "fffffffffff",
    "fffffffffff",
    "LfffffffffL",
    ".LLfffffLL.",
    "..LLLLLLL..",
]
GLYPHS = {  # 5x5, placed at (3, 3)
    "x": ["W...W", ".W.W.", "..W..", ".W.W.", "W...W"],
    "left": ["...W.", "..WW.", ".WWW.", "..WW.", "...W."],
    "right": [".W...", ".WW..", ".WWW.", ".WW..", ".W..."],
}


def round_button(glyph, alt=False):
    c = Canvas(11, 11)
    if alt:
        c.rows(0, 0, ROUND_ALT, {"f": P["dusk"], "L": P["dusk_lo"]})
    else:
        c.rows(0, 0, ROUND, {"K": P["ink"], "H": P["brass_hi"], "B": P["brass"], "L": P["brass_lo"], "f": P["navy"]})
    c.rows(3, 3, GLYPHS[glyph], {"W": P["white"]})
    return c


BUY = [  # 24x14 pill; the word is drawn on the face
    "..KKKKKKKKKKKKKKKKKKKK..",
    ".KHHHHHHHHHHHHHHHHHHHBK.",
    "KHHffffffffffffffffffLLK",
    "KHffffffffffffffffffffLK",
    "KHffffffffffffffffffffLK",
    "KHffffffffffffffffffffLK",
    "KHffffffffffffffffffffLK",
    "KHffffffffffffffffffffLK",
    "KHffffffffffffffffffffLK",
    "KHffffffffffffffffffffLK",
    "KBffffffffffffffffffffLK",
    "KBLffffffffffffffffffLLK",
    ".KLLLLLLLLLLLLLLLLLLLLK.",
    "..KKKKKKKKKKKKKKKKKKKK..",
]


def buy():
    c = from_map(BUY, {"K": P["ink"], "H": P["brass_hi"], "B": P["brass"], "L": P["brass_lo"], "f": P["navy"]})
    w = measure("BUY", SMALL)
    text(c, (24 - w) // 2, 4, "BUY", SMALL, P["white"], shadow=P["ink"])
    return c


def from_map(rows, palette):
    c = Canvas(len(rows[0]), len(rows))
    c.rows(0, 0, rows, palette)
    return c


def chamfer_plate(c, x0, y0, x1, y1, cut, fill, rim_lit, rim_mid, rim_shade, outline="ink"):
    """An octagonal plate: corners cut at 45 degrees, `cut` px deep; 1 px outline, 1 px rim, then the fill. The rim
    is lit where it faces up or left, shaded where it faces down or right."""

    def inside(x, y):
        if not (x0 <= x <= x1 and y0 <= y <= y1):
            return False
        k = min(y - y0, y1 - y, cut)
        return x0 + cut - k <= x <= x1 - cut + k

    def depth(x, y):  # 0 on the outline, 1 on the rim, 2 inside
        for d in (0, 1):
            if not all(inside(x + dx, y + dy) for dx in range(-d - 1, d + 2) for dy in range(-d - 1, d + 2)):
                return d
        return 2

    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if not inside(x, y):
                continue
            d = depth(x, y)
            if d == 0:
                c.set(x, y, P[outline])
            elif d == 2:
                c.set(x, y, P[fill])
            else:
                lit = (y - y0 <= 1) or (x - x0 <= 1) or (x - x0) + (y - y0) <= cut + 2
                shade = (y1 - y <= 1) or (x1 - x <= 1) or (x1 - x) + (y1 - y) <= cut + 2
                c.set(x, y, P[rim_mid] if lit == shade else P[rim_lit] if lit else P[rim_shade])


PLAQUE_WORDS = {
    "apply": "APPLY", "audio": "AUDIO", "back": "BACK", "confirm": "CONFIRM", "graphics": "GRAPHICS",
    "revert": "REVERT", "login": "LOGIN", "options": "OPTIONS",
}
PLAQUE_STATES = {  # suffix: (face, rim lit, rim shade, word colour, rows down)
    "": ("navy", "brass_hi", "brass_lo", "white", 0),
    "_hover": ("navy_hi", "brass_hi", "brass", "white", 0),
    "_click": ("navy_lo", "brass", "brass_dk", "foam", 1),
}


def plaque(word, state):
    face, lit, shade, colour, dy = PLAQUE_STATES[state]
    c = Canvas(64, 32)
    y0, y1 = 5 + dy, 26 + dy
    chamfer_plate(c, 1, y0, 62, y1, 3, face, lit, "brass", shade)
    for x, y in [(5, y0 + 3), (57, y0 + 3), (5, y1 - 4), (57, y1 - 4)]:  # rivets
        c.set(x, y, P[lit])
        c.set(x + 1, y, P["brass"])
        c.set(x, y + 1, P["brass"])
        c.set(x + 1, y + 1, P["brass_dk"])
    w = measure(word, BOLD)
    text(c, 32 - (w + 1) // 2, y0 + 6, word, BOLD, P[colour], shadow=P["ink"])
    return c


def plaques():
    out = {}
    for name, word in PLAQUE_WORDS.items():
        out[name] = plaque(word, "")
    for name in ("login", "options"):
        for state in ("_hover", "_click"):
            out[name + state] = plaque(PLAQUE_WORDS[name], state)
    return out


ANCHOR = [  # brass anchor on the hotbar's left end
    "....HB....",
    "...H..L...",
    "...H..L...",
    "....BL....",
    ".HHHBLLLL.",
    "....BL....",
    "....BL....",
    "....BL....",
    "....BL....",
    "H...BL...L",
    "HB..BL..BL",
    ".HB.BL.BL.",
    "..HBBLBL..",
    "...HBLL...",
    "....BL....",
]

SOCKETS = [26.5 + 30 * k for k in range(7)]
SOCKET_Y = 15.5


def hotbar():
    c = Canvas(224, 32)
    chamfer_plate(c, 0, 2, 223, 29, 3, "navy", "navy_hi", "navy_hi", "navy_lo")
    for x in range(4, 220):  # brass rails
        c.set(x, 4, P["brass"])
        c.set(x, 27, P["brass_lo"])
    for x in [41.5 + 30 * k for k in range(6)]:  # rivets between the portholes
        for y in (8, 22):
            xi = int(x - 0.5)
            c.set(xi, y, P["brass_hi"])
            c.set(xi + 1, y, P["brass"])
            c.set(xi, y + 1, P["brass"])
            c.set(xi + 1, y + 1, P["brass_dk"])
    c.rows(3, 9, ANCHOR, {"H": P["brass_hi"], "B": P["brass"], "L": P["brass_lo"]})
    for cx in SOCKETS:
        porthole(c, cx, SOCKET_Y)
    return c


def porthole(c, cx, cy):
    c.disc(cx, cy, 12.0, P["ink"])
    ring = Canvas(c.w, c.h)
    ring.disc(cx, cy, 11.0, P["brass"])
    for y in range(c.h):
        for x in range(c.w):
            if ring.get(x, y) is None:
                continue
            a = math.atan2(y + 0.5 - cy, x + 0.5 - cx)
            lit = math.cos(a + 3 * math.pi / 4)  # 1 where the ring faces the top left
            c.set(x, y, P["brass_hi"] if lit > 0.55 else P["brass_lo"] if lit < -0.55 else P["brass"])
    c.disc(cx, cy, 9.6, P["ink"])
    c.disc(cx, cy, 8.8, P["dusk_lo"])
    c.disc(cx + 0.6, cy + 0.6, 8.3, P["dusk"])
    for dx, dy in [(-6, -3), (-5, -4), (-5, -5), (-4, -6), (-3, -6)]:  # glint on the glass
        c.set(int(cx - 0.5) + dx, int(cy - 0.5) + dy, P["mist"])


def highlight():
    c = Canvas(32, 32)
    for y in range(32):
        for x in range(32):
            if 12.4 <= math.hypot(x + 0.5 - 16, y + 0.5 - 16) <= 13.4:
                c.set(x, y, P["foam"])
    for x, y in [(15, 0), (16, 0), (15, 1), (16, 1), (15, 30), (16, 30), (15, 31), (16, 31),
                 (0, 15), (0, 16), (1, 15), (1, 16), (30, 15), (30, 16), (31, 15), (31, 16)]:
        c.set(x, y, P["foam"])
    return c


POINTER = [  # the tip pixel's top-left corner is the hotspot (3, 2)
    "............",
    "............",
    "...K........",
    "...KK.......",
    "...KWK......",
    "...KWWK.....",
    "...KWWWK....",
    "...KWWWWK...",
    "...KWWWWWK..",
    "...KWWBKKKK.",
    "...KWBK.....",
    "...KKK......",
]


def pointer():
    return from_map(POINTER, {"K": P["ink"], "W": P["white"], "B": P["brass"]})


# koistatue_layout: art pixels (232x94), each shown as 4x4 texels. Rectangles are (x0, y0, x1, y1), inclusive.
KOI = {
    "frame": (2, 3, 228, 92),
    "grid": [(8 + 27 * i, 9 + 27 * j) for j in range(3) for i in range(4)],  # 24x24 slots
    "grid_handle": (116, 9, 117, 20),
    "deposit_tab": (128, 1, 159, 13),
    "deposit_box": (125, 14, 159, 46),
    "deposit_slot": (131, 19),
    "withdraw_tab": (123, 47, 160, 58),
    "withdraw_box": (121, 59, 159, 91),
    "withdraw_slot": (131, 64),
    "divider_x": 160,
    "title_y": 11,  # centred over the list; its x-height rows pass under the close button
    "list": (165, 18, 223, 87),
    "list_handle": (220, 20, 221, 31),
    "search": (167, 75, 221, 85),
    "close": (220, 1),
}


def handle(c, x0, y0, x1, y1):
    c.rect(x0, y0, x1, y1, P["brass"])
    c.rect(x0, y0, x0, y1 - 1, P["brass_hi"])
    c.set(x1, y1, P["brass_lo"])


def koistatue():
    k = KOI
    c = Canvas(232, 94)
    chamfer_plate(c, *k["frame"], 3, "navy", "brass_hi", "brass", "brass_lo")
    for x, y in k["grid"]:
        slot_tile(c, x, y, 24, 24)
    handle(c, *k["grid_handle"])
    for box, slot in [("deposit_box", "deposit_slot"), ("withdraw_box", "withdraw_slot")]:
        x0, y0, x1, y1 = k[box]
        c.rect(x0, y0, x1, y1, P["ink"])
        c.rect(x0 + 1, y0 + 1, x1 - 1, y1 - 1, P["navy_lo"])
        slot_tile(c, *k[slot], 24, 24, fill="navy", groove="ink", lip="navy_hi", shadow="navy_lo")
    for tab, word in [("deposit_tab", "Deposit"), ("withdraw_tab", "Withdraw")]:
        x0, y0, x1, y1 = k[tab]
        chamfer_plate(c, x0, y0, x1, y1, 2, "brass", "brass_hi", "brass", "brass_lo")
        w = measure(word, SMALL)
        text(c, (x0 + x1 + 1 - w) // 2, y0 + 3, word, SMALL, P["navy_lo"])
    dx = k["divider_x"]
    c.rect(dx, 4, dx, 91, P["ink"])
    c.rect(dx + 1, 4, dx + 1, 91, P["brass_lo"])
    lx0, ly0, lx1, ly1 = k["list"]
    title = "Kosmikoi Statue"
    text(c, (lx0 + lx1 + 1 - measure(title, SMALL)) // 2, k["title_y"], title, SMALL, P["white"], shadow=P["ink"])
    slot_tile(c, lx0, ly0, lx1 - lx0 + 1, ly1 - ly0 + 1)
    handle(c, *k["list_handle"])
    x0, y0, x1, y1 = k["search"]
    c.rect(x0, y0, x1, y1, P["navy_lo"])
    c.frame(x0, y0, x1, y1, P["ink"])
    c.rect(x0 + 1, y1, x1 - 1, y1, P["dusk_hi"])
    text(c, x0 + 3, y0 + 2, "Search...", SMALL, P["mist"])
    c.rows(x1 - 9, y0 + 2, [".##...", "#..#..", "#..#..", ".##...", "...#..", "....#."], {"#": P["mist"]})
    c.blit(round_button("x"), *k["close"])
    return c.scaled(4)


def textures():
    out = {**panels(), **slots(), **plaques()}
    out["hotbar_latest"] = hotbar()
    out["highlight_hotbar"] = highlight()
    out["pointer"] = pointer()
    out["ui_exit"] = round_button("x")
    out["arrow_left"] = round_button("left")
    out["arrow_right"] = round_button("right")
    out["arrow_left_alt"] = round_button("left", alt=True)
    out["arrow_right_alt"] = round_button("right", alt=True)
    out["buy"] = buy()
    out["koistatue_layout"] = koistatue()
    return out


SIZES = {  # from mods/_names.txt, section [textures]
    "hotbar_latest": (224, 32), "highlight_hotbar": (32, 32), "pointer": (12, 12),
    "inventory_bg_1": (96, 96), "inventory_bg_2": (96, 96), "inventory_bg_3": (96, 96),
    "inventory_slot": (24, 24), "ui_slot": (24, 24), "ui_exit": (11, 11), "buy": (24, 14),
    "arrow_left": (11, 11), "arrow_right": (11, 11), "arrow_left_alt": (11, 11), "arrow_right_alt": (11, 11),
    "koistatue_layout": (928, 376),
    **{n: (64, 32) for n in ["apply", "audio", "back", "confirm", "graphics", "revert", "login", "login_click",
                             "login_hover", "options", "options_click", "options_hover"]},
}

# Where the game (or this skin) writes words, on which fill, in which colour.
TEXT_ON = [
    ("inventory_bg_1", "navy", "white"),
    ("inventory_bg_2", "dusk", "white"),
    ("inventory_bg_2", "dusk", "black"),
    ("inventory_bg_3", "navy_hi", "white"),
    ("slots: stack counts", "dusk", "white"),
    ("menu plates", "navy", "white"),
    ("menu plates, hover", "navy_hi", "white"),
    ("menu plates, pressed", "navy_lo", "foam"),
    ("Deposit/Withdraw tabs", "brass", "navy_lo"),
    ("search field", "navy_lo", "mist"),
]


def contrasts():
    black = rgba("#000000")
    return [(where, fill, colour, contrast(black if colour == "black" else P[colour], P[fill]))
            for where, fill, colour in TEXT_ON]


def build():
    tex = textures()
    assert set(tex) == set(SIZES), set(tex) ^ set(SIZES)
    for name, im in tex.items():
        assert (im.w, im.h) == SIZES[name], (name, im.w, im.h)
        im.save(MOD / "art" / "textures" / f"{name}.png")
    for where, fill, colour, ratio in contrasts():
        print(f"{colour:>8} on {where:<24} ({fill} {PALETTE[fill]}): {ratio:.1f}:1")


if __name__ == "__main__":
    build()
