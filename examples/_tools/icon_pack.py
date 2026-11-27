"""Builds examples/icon_pack, "Harvest & Tackle": new 24x24 icons for six items and three koi, all drawn here.
Items are built from shapes (ellipses, thick lines, tapered strokes) filled with a material, lit from the top left
(lit edge, mid tone, shaded edge), outlined outside in one ink, then finished with hand-placed detail pixels.
Koi are pixel maps. Nothing is read from the game."""
import math

from pixel import EXAMPLES, Canvas, from_rows, rgba

MOD = EXAMPLES / "icon_pack"

INK = "#211a26"
# Material ramps: lit, mid, shade, deep.
MATERIALS = {
    "pumpkin": ("#ffc061", "#f08a2b", "#c5561f", "#8c3418"),
    "stem": ("#a7b75a", "#6f8a33", "#4b6226", "#33441c"),
    "leaf": ("#a6dc63", "#62ad3d", "#3d7c32", "#285626"),
    "carrot": ("#ffb45a", "#f07a24", "#c2501c", "#8a3416"),
    "greens": ("#9be06a", "#4fae44", "#2f7d3a", "#215a2e"),
    "kelp": ("#a8d98a", "#58a36c", "#377451", "#24503d"),
    "kelp_back": ("#6fae74", "#3f8058", "#2b5e47", "#1d4235"),
    "bladder": ("#ffd77a", "#e0a23a", "#a86a26", "#6e4420"),
    "wood": ("#d29a5e", "#a8693a", "#7b4627", "#54301e"),
    "iron": ("#e4ecf2", "#a9b7c4", "#74849a", "#4c586e"),
    "rod": ("#e7d08a", "#bf9f52", "#8c6d34", "#5d4626"),
    "cork": ("#f2d6a2", "#d6ae72", "#a8804e", "#76563a"),
    "red": ("#ff8a7a", "#e2413b", "#a82a32", "#6e1c28"),
    "white": ("#ffffff", "#eef0f4", "#c4c8d4", "#8e93a6"),
}


class Sprite:
    """A 24x24 material map; render() lights, outlines and returns a Canvas."""

    def __init__(self, size=24):
        self.n = size
        self.mat = [[None] * size for _ in range(size)]

    def put(self, x, y, m):
        if 0 <= x < self.n and 0 <= y < self.n:
            self.mat[y][x] = m

    def ellipse(self, cx, cy, rx, ry, m):
        for y in range(self.n):
            for x in range(self.n):
                if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1:
                    self.put(x, y, m)

    def stroke(self, pts, m, w0, w1=None):
        """A tapered stroke along a polyline: width w0 at the start, w1 at the end."""
        w1 = w0 if w1 is None else w1
        segs = list(zip(pts, pts[1:]))
        total = sum(math.dist(a, b) for a, b in segs)
        done = 0.0
        for (ax, ay), (bx, by) in segs:
            L = math.dist((ax, ay), (bx, by))
            for y in range(self.n):
                for x in range(self.n):
                    px, py = x + 0.5, y + 0.5
                    t = max(0.0, min(1.0, ((px - ax) * (bx - ax) + (py - ay) * (by - ay)) / (L * L)))
                    d = math.dist((px, py), (ax + t * (bx - ax), ay + t * (by - ay)))
                    w = w0 + (w1 - w0) * (done + t * L) / total
                    if d <= w / 2:
                        self.put(x, y, m)
            done += L

    def ribbon(self, pts, m, wmax, wend=1.0):
        """A blade along a polyline: wend at both ends, wmax in the middle."""
        segs = list(zip(pts, pts[1:]))
        total = sum(math.dist(a, b) for a, b in segs)
        for y in range(self.n):
            for x in range(self.n):
                px, py, done = x + 0.5, y + 0.5, 0.0
                for (ax, ay), (bx, by) in segs:
                    L = math.dist((ax, ay), (bx, by))
                    t = max(0.0, min(1.0, ((px - ax) * (bx - ax) + (py - ay) * (by - ay)) / (L * L)))
                    d = math.dist((px, py), (ax + t * (bx - ax), ay + t * (by - ay)))
                    s = (done + t * L) / total
                    if d <= (wend + (wmax - wend) * math.sin(math.pi * s)) / 2:
                        self.put(x, y, m)
                        break
                    done += L

    def lens(self, a, b, width, m):
        """A leaf shape from a to b, widest in the middle."""
        (ax, ay), (bx, by) = a, b
        L = math.dist(a, b)
        for y in range(self.n):
            for x in range(self.n):
                px, py = x + 0.5, y + 0.5
                t = ((px - ax) * (bx - ax) + (py - ay) * (by - ay)) / (L * L)
                if 0 <= t <= 1:
                    d = abs((px - ax) * (by - ay) - (py - ay) * (bx - ax)) / L
                    if d <= width / 2 * math.sin(math.pi * t) ** 0.8:
                        self.put(x, y, m)

    def render(self):
        n, mat = self.n, self.mat
        c = Canvas(n, n)

        def other(x, y, m):
            return not (0 <= x < n and 0 <= y < n) or mat[y][x] != m

        for y in range(n):
            for x in range(n):
                m = mat[y][x]
                if m is None:
                    continue
                lit, mid, shade, _ = MATERIALS[m]
                if other(x - 1, y, m) or other(x, y - 1, m):
                    col = lit
                elif other(x + 1, y, m) or other(x, y + 1, m) or other(x + 1, y + 1, m):
                    col = shade
                else:
                    col = mid
                c.set(x, y, col)
        for y in range(n):
            for x in range(n):
                if mat[y][x] is None and any(
                    0 <= x + dx < n and 0 <= y + dy < n and mat[y + dy][x + dx] is not None
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
                ):
                    c.set(x, y, INK)
        return c


def tone(m, i):
    return MATERIALS[m][i]


def pumpkin():  # 1
    s = Sprite()
    s.ellipse(7.5, 14.5, 5.5, 6.5, "pumpkin")
    s.ellipse(16.5, 14.5, 5.5, 6.5, "pumpkin")
    s.ellipse(12, 14, 5.5, 7.5, "pumpkin")
    s.stroke([(12.5, 8), (12.5, 5), (14, 3)], "stem", 2.6, 2)
    s.lens((14.5, 5.5), (21, 3), 4, "leaf")
    c = s.render()
    for x, y in [(9, 9), (9, 10), (8, 11), (8, 12), (8, 13), (8, 14), (8, 15), (8, 16), (9, 17), (9, 18),
                 (15, 9), (15, 10), (16, 11), (16, 12), (16, 13), (16, 14), (16, 15), (16, 16), (15, 17), (15, 18)]:
        c.set(x, y, tone("pumpkin", 2))  # ribs
    for x, y in [(4, 12), (4, 13), (5, 11), (11, 9), (11, 10)]:
        c.set(x, y, tone("pumpkin", 0))  # shine
    for x, y in [(19, 17), (18, 18), (17, 19), (14, 20), (13, 20), (12, 20)]:
        c.set(x, y, tone("pumpkin", 3))
    for x, y in [(16, 5), (17, 4), (18, 4)]:
        c.set(x, y, tone("leaf", 2))  # leaf vein
    return c


def leaf():  # 2
    s = Sprite()
    s.lens((4.5, 19.5), (20.5, 3.5), 11, "leaf")
    s.stroke([(2.5, 21.5), (6, 18)], "stem", 1.6)
    c = s.render()
    vein = tone("leaf", 0)
    for i in range(6, 19):  # midrib on the leaf's axis x + y = 23
        c.set(i, 23 - i, vein)
    for i in (8, 11, 14):  # side veins, angled towards the tip
        for x, y in [(i, 22 - i), (i, 21 - i), (i + 1, 23 - i), (i + 2, 23 - i)]:
            c.set(x, y, vein)
    return c


def carrot():  # 6
    s = Sprite()
    s.stroke([(15.5, 9.5), (4, 21)], "carrot", 7, 1.2)
    s.stroke([(16, 8), (18, 2.5)], "greens", 2.2, 1.6)
    s.stroke([(16.5, 8.5), (21.5, 5)], "greens", 2.2, 1.6)
    s.stroke([(16, 8), (13, 3)], "greens", 2, 1.4)
    c = s.render()
    for x, y in [(12, 11), (13, 12), (9, 14), (10, 15), (7, 17), (8, 18)]:
        c.set(x, y, tone("carrot", 2))  # rings
    for x, y in [(11, 9), (10, 10), (8, 12), (7, 13)]:
        c.set(x, y, tone("carrot", 0))
    return c


def wave(base, top, phase, y_top=2.5, amp=1.8, steps=16):
    """Points of a wavy strand from (base, 21.5) up to (top, y_top)."""
    return [(base + (top - base) * t + amp * math.sin(t * 8 + phase), 21.5 - (21.5 - y_top) * t)
            for t in (i / steps for i in range(steps + 1))]


def seaweed():  # 10: a bunch of wavy kelp strands, the middle ones behind, two air bladders
    s = Sprite()
    s.ribbon(wave(11, 9.5, 2.6, 4), "kelp_back", 3.0, 1.6)
    s.ribbon(wave(13, 15, 4.6, 2), "kelp_back", 3.0, 1.6)
    s.ribbon(wave(9.5, 4, 0.6, 5.5), "kelp", 3.0, 1.6)
    s.ribbon(wave(14.5, 20, 1.2, 6.5), "kelp", 3.0, 1.6)
    s.ellipse(5.5, 15, 1.5, 1.5, "bladder")
    s.ellipse(18.5, 14, 1.5, 1.5, "bladder")
    return s.render()


def hoe():  # 100: handle up to the right, a thin shank, a flat blade hanging from it
    s = Sprite()
    s.stroke([(3.5, 21.5), (15.5, 9.5)], "wood", 2.4)
    s.stroke([(15, 10), (18.5, 6.5)], "iron", 1.6)
    s.stroke([(18.5, 6.5), (21, 12.5)], "iron", 2.0, 5.0)
    c = s.render()
    for x, y in [(19, 7), (19, 8), (20, 9)]:
        c.set(x, y, tone("iron", 0))
    return c


def fishing_rod():  # 101
    s = Sprite()
    s.stroke([(3, 21.5), (21, 3)], "rod", 2.2, 1.2)
    s.stroke([(2.5, 22), (7, 17.5)], "cork", 2.8)
    s.ellipse(8.5, 18.5, 2.0, 2.0, "iron")  # reel
    s.ellipse(20.5, 16.5, 2.3, 2.3, "red")
    c = s.render()
    for y in range(4, 14):  # line from the tip to the float, no outline
        c.set(20, y, "#f4f4ff")
    for x, y in [(19, 17), (20, 17), (21, 17), (22, 17), (19, 18), (20, 18), (21, 18)]:
        if c.get(x, y) not in (None, rgba(INK)):
            c.set(x, y, tone("white", 1))  # white lower half of the float
    c.set(19, 15, tone("red", 0))
    c.set(8, 18, "#ffffff")
    return c


ITEMS = {1: pumpkin, 2: leaf, 6: carrot, 10: seaweed, 100: hoe, 101: fishing_rod}

K = (20, 20, 28, 255)
KOI = {
    160: (  # Blue Skin: a little blue blob
        {"K": K, "L": "#8cd2ff", "B": "#4a9be6", "D": "#2c64b4", "W": "#ffffff", "E": "#142040", "S": (0, 0, 0, 70)},
        [
            "........................",
            "........................",
            "..........KKKK..........",
            "........KKLLBBKK........",
            ".......KLLWLBBBBK.......",
            "......KLLWLBBBBBBK......",
            "......KLLLBBBBBBBK......",
            ".....KLLBBBBBBBBBBK.....",
            ".....KLBBBBBBBBBBBK.....",
            "....KLBBBEWBBBEWBBBK....",
            "....KLBBBEEBBBEEBBBK....",
            "....KBBBBEEBBBEEBBDK....",
            "....KBBBBBBBBBBBBBDK....",
            "....KBBBBBBEEEBBBBDK....",
            "....KBBBBBBBBBBBBDDK....",
            "....KBBBBBBBBBBBBDDK....",
            ".....KBBBBBBBBBBDDK.....",
            ".....KDBBBBBBBBBDDK.....",
            "......KDDBBBBBBDDK......",
            ".......KKDDDDDDKK.......",
            ".....SSSKKKKKKKKSSS.....",
            "......SSSSSSSSSSSS......",
            "........................",
            "........................",
        ],
    ),
    161: (  # Angry: a cross face with an anger mark
        {"K": K, "Y": "#ffc850", "O": "#e68c32", "R": "#b4501e", "V": "#e63232", "W": "#ffffff", "M": "#7a2a14"},
        [
            "........................",
            "...................V.V..",
            ".........KKKKKK...VV.VV.",
            ".......KKYYYYYYKK.......",
            "......KYYYYYYYYYOK.VV.VV",
            ".....KYYYYYYYYYYYOK.V.V.",
            "....KYYYYYYYYYYYYOOK....",
            "....KYYKKYYYYYYKKYOK....",
            "...KYYYYKKYYYYKKYYYOK...",
            "...KYYYYYKKYYKKYYYYOK...",
            "...KYYYYWKYYYYKWYYYOK...",
            "...KYYYYKKYYYYKKYYYOK...",
            "...KYYYYYYYYYYYYYYYOK...",
            "...KYYYYYYYYYYYYYYOOK...",
            "...KYYYYYYKKKKYYYYOOK...",
            "...KYYYYYKMMMMKYYYOOK...",
            "....KYYYKYYYYYYKYOOK....",
            "....KOYYYYYYYYYYOORK....",
            ".....KOOYYYYYYOORRK.....",
            "......KROOOOOORRRK......",
            ".......KKRRRRRRKK.......",
            ".........KKKKKK.........",
            "........................",
            "........................",
        ],
    ),
    69: (  # Emo Hair: a side-swept black fringe with a pink streak
        {"K": K, "H": "#2a2438", "G": "#4a4060", "P": "#ff5aa0", "Q": "#b43c78"},
        [
            "........................",
            "........................",
            "........KKKKKKK.........",
            "......KKHHGGGHHKK.......",
            ".....KHHGGGGGHHHHK......",
            "....KHHGGPPGGHHHHHK.....",
            "....KHGGPPQGHHHHHHHK....",
            "...KHHGPPQHHHHHHHHHK....",
            "...KHGPPQHHHHHHHHHHHK...",
            "...KHPPQHHHHHHHHHHHHK...",
            "..KHHPQHHHHHHHHHHHHHK...",
            "..KHHQHHHHHHHHHHHKHHK...",
            "..KHHHHHHHHHHHHHK.KHK...",
            "..KHHHHHHHHHHHHK..KHK...",
            "..KHHHHHHHHHHHK...KHK...",
            "..KHHHHHHHHHHK....KHK...",
            "..KHHHHHHHHHK.....KK....",
            "..KHHHHHHHHK............",
            "...KHHHHHHK.............",
            "...KHHHHHK..............",
            "....KHHHK...............",
            ".....KKK................",
            "........................",
            "........................",
        ],
    ),
}


def koi(k):
    palette, rows = KOI[k]
    return from_rows(rows, {ch: rgba(c) for ch, c in palette.items()})


def build():
    for item, fn in ITEMS.items():
        fn().save(MOD / "art" / "items" / f"{item}.png")
    for k in KOI:
        koi(k).save(MOD / "art" / "koi" / f"{k}.png")


if __name__ == "__main__":
    build()
