"""Mock-ups of the ui_skin and icon_pack art in the game's layouts (positions measured on screenshots of the shop,
the inventory and the hotbar), written at 2x and 4x to logs/art-preview/ of this repository. Usage: python3 preview.py [out_dir]"""
import sys
from pathlib import Path

import icon_pack
import ui_skin
from font import BOLD, SMALL, measure, text
from pixel import EXAMPLES, Canvas, rgba

OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else EXAMPLES.parent / "logs" / "art-preview"
P = ui_skin.P
TEX = ui_skin.textures()
ICONS = {i: fn() for i, fn in icon_pack.ITEMS.items()} | {f"koi{k}": icon_pack.koi(k) for k in icon_pack.KOI}
WHITE, BLACK, SHADOW = rgba("#ffffff"), rgba("#000000"), rgba("#1a1a22")  # the game's text shadow, opaque here
GREEN_TEXT = rgba("#9cf07a")


def ground(w, h):
    """Grass with a strip of sea, so the art is seen on the colours the game puts behind it."""
    c = Canvas(w, h)
    c.rect(0, 0, w - 1, h - 1, "#4c8a34")
    for y in range(h):
        for x in range(w):
            if (x * 7 + y * 13) % 29 == 0:
                c.set(x, y, "#3f7a2c")
            if x < w // 6:
                c.set(x, y, "#2f6fa8" if (x + y) % 11 else "#3a80b8")
    return c


def nine_slice(tex, w, h, b=2):
    out = Canvas(w, h)
    for y in range(h):
        sy = y if y < b else tex.h - (h - y) if y >= h - b else b + (y - b) * (tex.h - 2 * b) // (h - 2 * b)
        for x in range(w):
            sx = x if x < b else tex.w - (w - x) if x >= w - b else b + (x - b) * (tex.w - 2 * b) // (w - 2 * b)
            out.set(x, y, tex.get(sx, sy))
    return out


def label(c, x, y, s, colour=WHITE, font=SMALL):
    """The game draws white words with a 1 px drop shadow, black words without."""
    return text(c, x, y, s, font, colour, shadow=SHADOW if colour != BLACK else None)


def hotbar_scene(selected=0):
    c = ground(260, 48)
    hx, hy = 18, 8
    c.blit(TEX["hotbar_latest"], hx, hy)
    order = [100, 101, 10, 2, "koi161", 1, 6]
    counts = {10: "64", 2: "3", 1: "12", 6: "5"}
    for k, item in enumerate(order):
        cx, cy = hx + int(ui_skin.SOCKETS[k] + 0.5), hy + int(ui_skin.SOCKET_Y + 0.5)
        c.blit(ICONS[item], cx - 12, cy - 12)
        if item in counts:
            s = counts[item]
            label(c, cx + 9 - measure(s, SMALL), cy + 4, s)
    sx = hx + int(ui_skin.SOCKETS[selected] + 0.5)
    c.blit(TEX["highlight_hotbar"], sx - 16, hy + int(ui_skin.SOCKET_Y + 0.5) - 16)
    c.blit(TEX["pointer"], 190, 34)
    return c


def shop_scene():
    c = ground(300, 170)
    x0, y0 = 20, 12
    left = nine_slice(TEX["inventory_bg_1"], 128, 147)
    c.blit(left, x0, y0)
    label(c, x0 + 9, y0 + 7, "Stream Merchant")
    for j in range(4):
        for i in range(4):
            sx, sy = x0 + 10 + 27 * i, y0 + 20 + 27 * j
            c.blit(TEX["inventory_slot"], sx, sy)
    c.blit(ICONS["koi69"], x0 + 10, y0 + 20)
    c.blit(ICONS[1], x0 + 37, y0 + 20)
    label(c, x0 + 10 + 27 + 13, y0 + 20 + 17, "4")
    label(c, x0 + 10, y0 + 135, "Page")
    c.blit(TEX["arrow_left_alt"], x0 + 69, y0 + 132)
    label(c, x0 + 85, y0 + 134, "1/1")
    c.blit(TEX["arrow_right_alt"], x0 + 107, y0 + 132)
    rx = x0 + 128
    right = nine_slice(TEX["inventory_bg_2"], 133, 147)
    c.blit(right, rx, y0)
    c.blit(TEX["ui_exit"], rx + 120, y0 + 2)
    label(c, rx + 7, y0 + 7, "Pumpkin")
    well = Canvas(48, 48)
    for y in range(48):
        for x in range(48):
            well.set(x, y, TEX["ui_slot"].get(x // 2, y // 2))
    c.blit(well, rx + 7, y0 + 17)
    c.blit(ICONS[1].scaled(2), rx + 7, y0 + 17)
    label(c, rx + 61, y0 + 21, "Price")
    label(c, rx + 61, y0 + 32, "6,000")
    label(c, rx + 61, y0 + 46, "You have")
    label(c, rx + 61, y0 + 57, "51,380")
    label(c, rx + 7, y0 + 72, "In stock: 6", GREEN_TEXT)
    label(c, rx + 20, y0 + 103, "Amount")
    label(c, rx + 72, y0 + 103, "Total:")
    c.blit(TEX["arrow_left"], rx + 9, y0 + 114)
    label(c, rx + 31, y0 + 116, "1")
    c.blit(TEX["arrow_right"], rx + 50, y0 + 114)
    label(c, rx + 72, y0 + 116, "6,000")
    c.blit(TEX["buy"], rx + 101, y0 + 128)
    c.blit(TEX["pointer"], rx + 108, y0 + 134)
    return c


def inventory_scene():
    c = ground(190, 150)
    x0, y0 = 14, 8
    c.blit(nine_slice(TEX["inventory_bg_2"], 112, 26), x0, y0)
    label(c, x0 + 6, y0 + 4, "51,380", BLACK)
    label(c, x0 + 60, y0 + 4, "1", BLACK)
    label(c, x0 + 6, y0 + 15, "white words", WHITE)
    body = nine_slice(TEX["inventory_bg_1"], 166, 112)
    c.blit(body, x0, y0 + 28)
    items = [10, 10, 2, 6, 1, 100, 101, "koi160", "koi161", "koi69", 2, 6, 1, 10, 101, 100, 6, 2]
    for k, item in enumerate(items):
        i, j = k % 6, k // 6
        sx, sy = x0 + 5 + 26 * i, y0 + 33 + 26 * j
        c.blit(TEX["inventory_slot"], sx, sy)
        c.blit(ICONS[item], sx, sy)
        if isinstance(item, int) and item < 100:
            label(c, sx + 15, sy + 16, str(64 - 3 * k))
    return c


def menu_scene():
    c = ground(300, 150)
    x0, y0 = 10, 8
    c.blit(nine_slice(TEX["inventory_bg_3"], 140, 134), x0, y0)
    label(c, x0 + 62, y0 + 6, "Menu")
    for k, word in enumerate(["Resume", "Settings", "Exit Game"]):
        bx, by = x0 + 30, y0 + 22 + 36 * k
        c.blit(nine_slice(TEX["inventory_bg_3"], 80, 30), bx, by)
        label(c, bx + (80 - measure(word, SMALL)) // 2, by + 12, word)
    px, py = x0 + 150, y0
    names = ["apply", "back", "confirm", "graphics", "revert", "audio", "login", "login_hover", "login_click",
             "options", "options_hover", "options_click"]
    for k, n in enumerate(names):
        c.blit(TEX[n], px + 68 * (k % 2), py + 22 * (k // 2) - 4)
    return c


def text_panels():
    """White and black sample words on each plate."""
    c = Canvas(312, 60)
    c.rect(0, 0, 311, 59, "#4c8a34")
    for k, name in enumerate(["inventory_bg_1", "inventory_bg_2", "inventory_bg_3"]):
        x = 4 + 103 * k
        c.blit(nine_slice(TEX[name], 99, 52), x, 4)
        label(c, x + 6, 10, "bg " + name[-1], WHITE)
        label(c, x + 6, 21, "White 6,000", WHITE)
        label(c, x + 6, 32, "Black 51,380", BLACK)
    return c


def sheet():
    names = sorted(n for n in TEX if n != "koistatue_layout")
    c = Canvas(330, 300)
    c.rect(0, 0, 329, 299, "#4c8a34")
    x, y, row_h = 4, 4, 0
    for n in names:
        t = TEX[n]
        if x + t.w > 326:
            x, y, row_h = 4, y + row_h + 4, 0
        c.blit(t, x, y)
        x += t.w + 4
        row_h = max(row_h, t.h)
    return c


def icons_scene():
    c = Canvas(10 + 30 * 9, 82)
    c.rect(0, 0, c.w - 1, c.h - 1, "#4c8a34")
    keys = list(icon_pack.ITEMS) + [f"koi{k}" for k in icon_pack.KOI]
    for k, key in enumerate(keys):
        x = 6 + 30 * k
        c.blit(TEX["inventory_slot"], x, 4)
        c.blit(ICONS[key], x, 4)
        c.blit(ICONS[key], x, 32)
        bg = Canvas(24, 14)
        bg.rect(0, 0, 23, 13, "#e8dcc0")
        c.blit(bg, x, 58)
        c.blit(ICONS[key], x, 54)
    return c


def save(name, canvas):
    for k in (2, 4):
        canvas.scaled(k).save(OUT / f"{name}@{k}x.png")


def main():
    save("hotbar", hotbar_scene())
    save("shop", shop_scene())
    save("inventory", inventory_scene())
    save("menu", menu_scene())
    save("text", text_panels())
    save("sheet", sheet())
    save("icons", icons_scene())
    TEX["koistatue_layout"].save(OUT / "koistatue@1x.png")
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
