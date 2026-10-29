"""LiteOptions icon: a top-down Pebble Mouse 2 (M350s), graphite, drawn from scratch."""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 512        # working canvas
SS = 4         # supersampling for masks

def _grid(n):
    c = (np.arange(n) + 0.5) / SS
    return np.meshgrid(c, c)

def se_mask(cx, cy, a, b, n=3.0):
    """Anti-aliased superellipse |x/a|^n + |y/b|^n <= 1 (the Pebble's squircle-ish outline)."""
    X, Y = _grid(S * SS)
    v = (np.abs((X - cx) / a) ** n + np.abs((Y - cy) / b) ** n) <= 1
    m = Image.fromarray((v * 255).astype(np.uint8))
    return m.resize((S, S), Image.BOX)

def circle_mask(cx, cy, r):
    m = Image.new("L", (S * SS, S * SS), 0)
    ImageDraw.Draw(m).ellipse([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS, (cy + r) * SS], fill=255)
    return m.resize((S, S), Image.BOX)

def rr_mask(box, r):
    m = Image.new("L", (S * SS, S * SS), 0)
    ImageDraw.Draw(m).rounded_rectangle([v * SS for v in box], radius=r * SS, fill=255)
    return m.resize((S, S), Image.BOX)

def lin(c1, c2, angle_deg):
    """Linear gradient over the canvas; c1 at the start of the angle's direction."""
    a = np.radians(angle_deg)
    X, Y = np.meshgrid(np.arange(S), np.arange(S))
    t = X * np.cos(a) + Y * np.sin(a)
    t = (t - t.min()) / (t.max() - t.min())
    c1, c2 = np.array(c1, float), np.array(c2, float)
    rgb = c1[None, None, :] * (1 - t[..., None]) + c2[None, None, :] * t[..., None]
    out = np.dstack([rgb, np.full((S, S), 255.0)]).astype(np.uint8)
    return Image.fromarray(out, "RGBA")

def layer(fill, mask):
    """A solid or gradient fill clipped to a mask, as its own RGBA layer."""
    src = fill if isinstance(fill, Image.Image) else Image.new("RGBA", (S, S), fill)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(src, (0, 0), mask)
    return out

def mul_alpha(img, k):
    a = img.getchannel("A").point(lambda v: int(v * k))
    img = img.copy(); img.putalpha(a); return img

def pebble(size, tray=False, accent=True):
    small = size <= 24
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    h = S * (0.94 if small else 0.88)
    w = h * (0.62 if small else 0.57)
    cx, cy = S / 2, S / 2 - (0 if small else S * 0.01)
    top = cy - h / 2

    outline = se_mask(cx, cy, w / 2, h / 2)
    # soft drop shadow on the bigger sizes
    if size >= 48:
        sh = layer((0, 0, 0, 120), se_mask(cx, cy + S * 0.035, w / 2 * 0.96, h / 2 * 0.98))
        img = Image.alpha_composite(img, sh.filter(ImageFilter.GaussianBlur(S * 0.028)))

    px = S / size   # canvas units per output pixel
    # bottom shell: the thin lighter band round the edge, lit from the top right.
    # At tray sizes it is a full pixel and brighter, so the mouse holds its shape on a dark taskbar.
    rim = lin((176, 178, 184), (118, 120, 126), 120) if size <= 32 else lin((124, 126, 132), (62, 63, 67), 120)
    img = Image.alpha_composite(img, layer(rim, outline))
    e1 = max(w * 0.022, px * (1.0 if size <= 32 else 0.8))
    if size >= 32:
        # the groove between the shells
        img = Image.alpha_composite(img, layer((18, 18, 20, 255), se_mask(cx, cy, w / 2 - e1, h / 2 - e1)))
        e2 = e1 + max(w * 0.018, px * 0.6)
    else:
        e2 = e1
    # top shell: matte graphite, lighter towards the top right
    shell = se_mask(cx, cy, w / 2 - e2, h / 2 - e2)
    body = lin((112, 113, 118), (60, 61, 65), 115) if small else lin((100, 101, 106), (50, 51, 55), 115)
    img = Image.alpha_composite(img, layer(body, shell))
    # broad soft sheen on the upper right
    sheen = layer((255, 255, 255, 34), se_mask(cx + w * 0.10, top + h * 0.28, w * 0.36, h * 0.30))
    sheen = sheen.filter(ImageFilter.GaussianBlur(S * 0.04))
    sheen.putalpha(Image.fromarray(np.minimum(np.array(sheen.getchannel("A")), np.array(shell))))
    img = Image.alpha_composite(img, sheen)

    d = ImageDraw.Draw(img)
    # centre seam, from the top edge to just past the middle
    sw = max(S * 0.012, px * (0.9 if small else 0.7))
    seam_end = top + h * (0.50 if small else 0.555)
    d.line([(cx, top + e1), (cx, seam_end)], fill=(16, 16, 18, 255), width=int(sw))

    wcy = top + h * (0.25 if not small else 0.24)
    if not small:
        # dished well round the wheel: shadowed at the top, catching light at the bottom
        r = w * 0.19
        img = Image.alpha_composite(img, layer(lin((22, 22, 24), (70, 71, 75), 90), circle_mask(cx, wcy, r + max(S * 0.005, px * 0.5))))
        img = Image.alpha_composite(img, layer(lin((46, 47, 50), (98, 99, 103), 90), circle_mask(cx, wcy, r)))
    # the wheel
    ww = w * (0.24 if small else 0.165)
    wh = h * (0.17 if small else 0.125)
    box = [cx - ww / 2, wcy - wh / 2, cx + ww / 2, wcy + wh / 2]
    if accent:
        glow = layer((70, 150, 255, 150), rr_mask([box[0] - ww * 0.25, box[1] - ww * 0.25, box[2] + ww * 0.25, box[3] + ww * 0.25], ww * 0.6))
        img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(S * (0.02 if small else 0.022))))
        img = Image.alpha_composite(img, layer(lin((125, 195, 255), (32, 104, 240), 90), rr_mask(box, ww * 0.42)))
    else:
        if not small:
            img = Image.alpha_composite(img, layer((14, 14, 16, 255), rr_mask([box[0] - S * 0.006, box[1] - S * 0.008, box[2] + S * 0.006, box[3] + S * 0.004], ww * 0.45)))
        img = Image.alpha_composite(img, layer(lin((150, 151, 156), (96, 97, 101), 90) if small else lin((122, 123, 128), (78, 79, 83), 90), rr_mask(box, ww * 0.42)))
    if not small:
        # a fine highlight along the wheel's top edge
        hl = layer((255, 255, 255, 90), rr_mask([box[0] + ww * 0.18, box[1] + wh * 0.06, box[2] - ww * 0.18, box[1] + wh * 0.16], wh * 0.05))
        img = Image.alpha_composite(img, hl)
    return img.resize((size, size), Image.LANCZOS)


import struct, io

def make(size):
    return pebble(size, tray=size <= 64, accent=True)

def dib(img):
    """32-bit DIB with an AND mask, as Windows and System.Drawing.Icon expect."""
    w, h = img.size
    px = img.convert('RGBA').load()
    hdr = struct.pack('<IiiHHIIiiII', 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    data = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            data += bytes((b, g, r, a))
    row = ((w + 31) // 32) * 4
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        bits = bytearray(row)
        for x in range(w):
            if px[x, y][3] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        mask += bits
    return hdr + bytes(data) + bytes(mask)

_cache = {}
def icon(s):
    if s not in _cache: _cache[s] = make(s)
    return _cache[s]

def write_ico(path, sizes):
    blobs = []
    for s in sizes:
        img = icon(s)
        if s >= 256:
            buf = io.BytesIO(); img.save(buf, 'PNG'); blobs.append((s, buf.getvalue()))
        else:
            blobs.append((s, dib(img)))
    out = bytearray(struct.pack('<HHH', 0, 1, len(blobs)))
    offset = 6 + 16 * len(blobs)
    for s, blob in blobs:
        out += struct.pack('<BBBBHHII', s % 256, s % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    for _, blob in blobs:
        out += blob
    open(path, 'wb').write(out)

if __name__ == "__main__":
    write_ico('app.ico', [16, 20, 24, 32, 40, 48, 64, 128, 256])
    write_ico('tray.ico', [16, 20, 24, 32, 40, 48, 64])
    icon(256).save('app-256.png')
    icon(64).save('logo.png')
    icon(16).save('app-16-preview.png')
    icon(32).save('app-32-preview.png')
    print('icons written')
