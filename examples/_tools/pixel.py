"""Pixel canvas and PNG writer for the art generators: Python standard library only, writes PNGs, never reads one."""
import struct
import zlib
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
EXAMPLES = TOOLS.parent


def rgba(c):
    """'#rrggbb', '#rrggbbaa' or a tuple -> (r, g, b, a); None stays None (transparent)."""
    if c is None or isinstance(c, tuple):
        return c if c is None or len(c) == 4 else c + (255,)
    c = c.lstrip("#")
    v = tuple(int(c[i : i + 2], 16) for i in range(0, len(c), 2))
    return v if len(v) == 4 else v + (255,)


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.px = [None] * (w * h)

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y * self.w + x] = rgba(c)

    def get(self, x, y):
        return self.px[y * self.w + x] if 0 <= x < self.w and 0 <= y < self.h else None

    def rect(self, x0, y0, x1, y1, c):
        """Filled, corners inclusive."""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def frame(self, x0, y0, x1, y1, c):
        for x in range(x0, x1 + 1):
            self.set(x, y0, c)
            self.set(x, y1, c)
        for y in range(y0, y1 + 1):
            self.set(x0, y, c)
            self.set(x1, y, c)

    def disc(self, cx, cy, r, c):
        """Every pixel whose centre lies within r of (cx, cy); centres may be half-integers."""
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r:
                    self.set(x, y, c)

    def rows(self, x0, y0, rows, palette):
        """Stamps a hand-placed pixel map: each character is a key of `palette`, '.' leaves the pixel alone."""
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch != ".":
                    self.set(x0 + x, y0 + y, palette[ch])

    def blit(self, other, x0, y0):
        for y in range(other.h):
            for x in range(other.w):
                c = other.px[y * other.w + x]
                if c is not None:
                    self.set(x0 + x, y0 + y, over(c, self.get(x0 + x, y0 + y)))

    def recolour_where(self, test, c):
        """Sets every pixel for which test(x, y, colour) holds."""
        for y in range(self.h):
            for x in range(self.w):
                if test(x, y, self.get(x, y)):
                    self.set(x, y, c)

    def scaled(self, k):
        out = Canvas(self.w * k, self.h * k)
        for y in range(out.h):
            for x in range(out.w):
                out.px[y * out.w + x] = self.px[(y // k) * self.w + x // k]
        return out

    def shifted(self, dx, dy):
        out = Canvas(self.w, self.h)
        out.blit(self, dx, dy)
        return out

    def save(self, path):
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        raw = bytearray()
        for y in range(self.h):
            raw.append(0)
            for x in range(self.w):
                raw.extend(self.px[y * self.w + x] or (0, 0, 0, 0))

        def chunk(kind, data):
            return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

        png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 6, 0, 0, 0))
        png += chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
        path.write_bytes(png)


def from_rows(rows, palette):
    c = Canvas(len(rows[0]), len(rows))
    for y, row in enumerate(rows):
        assert len(row) == c.w, (y, row)
    c.rows(0, 0, rows, palette)
    return c


def over(src, dst):
    if src[3] == 255 or dst is None:
        return src
    a = src[3] / 255
    return tuple(round(s * a + d * (1 - a)) for s, d in zip(src[:3], dst[:3])) + (max(src[3], dst[3]),)


def _lin(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def luminance(c):
    r, g, b = (_lin(v) for v in rgba(c)[:3])
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(a, b):
    """WCAG contrast ratio."""
    la, lb = sorted((luminance(a), luminance(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)
