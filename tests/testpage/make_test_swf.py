"""Writes test.swf: a tiny hand-assembled AS2 movie used to check the assistant without the real game.

Stage 950x600 @30fps (same size as the game).
  * indicator square at (50,50) 200x200 - changes colour every time the button is clicked
  * button at (400,250) 200x100 - grey up / light-grey over / dark-grey down; on release -> nextFrame
  * 40x40 dot at y=480 that slides from x=50 to x=850 once per second (proves rendering continues)

No Flash toolchain needed; the SWF tags are written directly (SWF file format spec v19).
"""
import struct
import sys
from pathlib import Path

STAGE_W, STAGE_H, FPS = 950, 600, 30
TWIPS = 20

INDICATOR_COLORS = [
    (230, 40, 40), (40, 200, 60), (40, 90, 230), (240, 220, 30),
    (220, 40, 220), (30, 210, 220), (250, 140, 20), (255, 255, 255),
]
BUTTON_UP, BUTTON_OVER, BUTTON_DOWN = (128, 128, 128), (205, 205, 205), (60, 60, 60)
BUTTON_X, BUTTON_Y, BUTTON_W, BUTTON_H = 400, 250, 200, 100
DOT_Y, DOT_FRAMES = 480, 30


class Bits:
    def __init__(self):
        self.buf, self.acc, self.n = bytearray(), 0, 0

    def ub(self, value, nbits):
        for i in range(nbits - 1, -1, -1):
            self.acc = (self.acc << 1) | ((value >> i) & 1)
            self.n += 1
            if self.n == 8:
                self.buf.append(self.acc)
                self.acc, self.n = 0, 0

    def sb(self, value, nbits):
        self.ub(value & ((1 << nbits) - 1), nbits)

    def data(self):
        if self.n:
            self.buf.append(self.acc << (8 - self.n))
            self.acc, self.n = 0, 0
        return bytes(self.buf)


def signed_bits(*values):
    return max([1] + [(v.bit_length() if v >= 0 else (~v).bit_length()) + 1 for v in values])


def rect(xmin, xmax, ymin, ymax):
    n = signed_bits(xmin, xmax, ymin, ymax)
    b = Bits()
    b.ub(n, 5)
    for v in (xmin, xmax, ymin, ymax):
        b.sb(v, n)
    return b.data()


def matrix(tx, ty):
    """Translate-only MATRIX, pixels."""
    tx, ty = tx * TWIPS, ty * TWIPS
    b = Bits()
    b.ub(0, 1)  # HasScale
    b.ub(0, 1)  # HasRotate
    n = signed_bits(tx, ty)
    b.ub(n, 5)
    b.sb(tx, n)
    b.sb(ty, n)
    return b.data()


def tag(code, body=b""):
    if len(body) < 0x3F:
        return struct.pack("<H", (code << 6) | len(body)) + body
    return struct.pack("<HI", (code << 6) | 0x3F, len(body)) + body


def define_rect(shape_id, w, h, rgb):
    """DefineShape: solid rectangle with its top-left at the origin, drawn clockwise with fill style 1."""
    W, H = w * TWIPS, h * TWIPS
    body = struct.pack("<H", shape_id) + rect(0, W, 0, H)
    body += bytes([1, 0x00, *rgb])  # one solid fill style
    body += bytes([0])  # no line styles
    b = Bits()
    b.ub(1, 4)  # NumFillBits
    b.ub(0, 4)  # NumLineBits
    # StyleChangeRecord: non-edge, no new styles, no line style, fill1, no fill0, moveTo
    for flag in (0, 0, 0, 1, 0, 1):
        b.ub(flag, 1)
    b.ub(1, 5)
    b.sb(0, 1)
    b.sb(0, 1)
    b.ub(1, 1)  # FillStyle1 = 1
    for dx, dy in ((W, 0), (0, H), (-W, 0), (0, -H)):
        n = max(2, signed_bits(dx, dy))
        b.ub(1, 1)  # edge
        b.ub(1, 1)  # straight
        b.ub(n - 2, 4)
        b.ub(0, 1)  # not a general line
        if dx:
            b.ub(0, 1)  # horizontal
            b.sb(dx, n)
        else:
            b.ub(1, 1)  # vertical
            b.sb(dy, n)
    b.ub(0, 1)  # EndShapeRecord
    b.ub(0, 5)
    return tag(2, body + b.data())


def place(depth, char=None, x=None, y=None, move=False):
    flags = (0x01 if move else 0) | (0x02 if char is not None else 0) | (0x04 if x is not None else 0)
    body = bytes([flags]) + struct.pack("<H", depth)
    if char is not None:
        body += struct.pack("<H", char)
    if x is not None:
        body += matrix(x, y)
    return tag(26, body)


def show_frame():
    return tag(1)


def do_action(*actions):
    return tag(12, bytes(actions) + b"\x00")


ACTION_NEXT_FRAME, ACTION_STOP = 0x04, 0x07


def define_sprite(sprite_id, frames):
    body = struct.pack("<HH", sprite_id, len(frames))
    for frame_tags in frames:
        body += frame_tags + show_frame()
    return tag(39, body + tag(0))


def define_button2(button_id, records, release_actions):
    """records: (state bits, character id, depth, x, y). State bits: hit 8, down 4, over 2, up 1."""
    chars = b""
    for states, char_id, depth, x, y in records:
        chars += bytes([states]) + struct.pack("<HH", char_id, depth) + matrix(x, y) + b"\x00"  # empty CXFORMWITHALPHA
    chars += b"\x00"  # CharacterEndFlag
    on_release = bytes([0x08, 0x00])  # CondOverDownToOverUp
    cond_action = struct.pack("<H", 0) + on_release + bytes(release_actions) + b"\x00"
    body = struct.pack("<H", button_id) + b"\x00" + struct.pack("<H", 2 + len(chars)) + chars + cond_action
    return tag(34, body)


def build():
    tags = [
        tag(69, bytes([0x01, 0, 0, 0])),  # FileAttributes: AS2, UseNetwork
        tag(9, bytes([34, 34, 51])),  # SetBackgroundColor
    ]

    indicator_ids = []
    for i, rgb in enumerate(INDICATOR_COLORS):
        shape_id = 10 + i
        tags.append(define_rect(shape_id, 200, 200, rgb))
        indicator_ids.append(shape_id)

    tags.append(define_rect(30, BUTTON_W, BUTTON_H, BUTTON_UP))
    tags.append(define_rect(31, BUTTON_W, BUTTON_H, BUTTON_OVER))
    tags.append(define_rect(32, BUTTON_W, BUTTON_H, BUTTON_DOWN))
    tags.append(define_button2(40, [
        (0x01, 30, 1, BUTTON_X, BUTTON_Y),
        (0x02, 31, 2, BUTTON_X, BUTTON_Y),
        (0x04, 32, 3, BUTTON_X, BUTTON_Y),
        (0x08, 30, 4, BUTTON_X, BUTTON_Y),
    ], [ACTION_NEXT_FRAME]))

    tags.append(define_rect(50, 40, 40, (255, 200, 0)))
    step = (850 - 50) / (DOT_FRAMES - 1)
    dot_frames = [place(1, 50, 50, DOT_Y)]
    dot_frames += [place(1, None, round(50 + i * step), DOT_Y, move=True) for i in range(1, DOT_FRAMES)]
    tags.append(define_sprite(60, dot_frames))

    tags += [place(1, 60, 0, 0), place(2, indicator_ids[0], 50, 50), place(3, 40, 0, 0), do_action(ACTION_STOP), show_frame()]
    for shape_id in indicator_ids[1:]:
        tags += [place(2, shape_id, 50, 50, move=True), do_action(ACTION_STOP), show_frame()]
    tags.append(tag(0))

    body = rect(0, STAGE_W * TWIPS, 0, STAGE_H * TWIPS) + struct.pack("<HH", FPS << 8, len(INDICATOR_COLORS)) + b"".join(tags)
    return b"FWS" + bytes([8]) + struct.pack("<I", 8 + len(body)) + body


if __name__ == "__main__":
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).with_name("test.swf")
    data = build()
    out.write_bytes(data)
    print(f"wrote {out} ({len(data)} bytes)")
