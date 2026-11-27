"""Pillow helpers of sound_pack.py (its cue strip). The art generators use pixel.py instead."""
from pathlib import Path

from PIL import Image

TOOLS = Path(__file__).resolve().parent
EXAMPLES = TOOLS.parent


def save(im, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    im.save(path, optimize=True)


def hexrgb(s):
    s = s.lstrip("#")
    return tuple(int(s[i : i + 2], 16) for i in (0, 2, 4))


def from_rows(rows, palette):
    """Pixel art from text rows: each character is a key of `palette` (an RGBA tuple), '.' is transparent."""
    im = Image.new("RGBA", (len(rows[0]), len(rows)), (0, 0, 0, 0))
    px = im.load()
    for y, row in enumerate(rows):
        assert len(row) == im.width, (y, row)
        for x, ch in enumerate(row):
            if ch != ".":
                px[x, y] = palette[ch]
    return im
