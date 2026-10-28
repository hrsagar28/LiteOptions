from PIL import Image, ImageDraw
def make(size):
    S = size * 8
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = int(S * 0.14)
    body = [pad + int(S * 0.04), int(S * 0.06), S - pad - int(S * 0.04), S - int(S * 0.06)]
    grad = Image.new("RGBA", (S, S)); gd = ImageDraw.Draw(grad)
    top, bot = (59, 130, 246), (29, 78, 216)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=tuple(int(top[i] * (1 - t) + bot[i] * t) for i in range(3)) + (255,))
    mask = Image.new("L", (S, S), 0); ImageDraw.Draw(mask).ellipse(body, fill=255)
    img.paste(grad, (0, 0), mask)
    cx = S // 2
    d.line([(cx, body[1] + int(S * 0.02)), (cx, body[1] + int(S * 0.36))], fill=(255, 255, 255, 200), width=max(int(S * 0.035), 2))
    ww, wh = int(S * 0.11), int(S * 0.2); wy = body[1] + int(S * 0.12)
    d.rounded_rectangle([cx - ww // 2, wy, cx + ww // 2, wy + wh], radius=ww // 2, fill=(255, 255, 255, 255))
    return img.resize((size, size), Image.LANCZOS)


import struct, io

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

def write_ico(path, sizes):
    blobs = []
    for s in sizes:
        img = make(s)
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

write_ico('app.ico', [16, 20, 24, 32, 40, 48, 64, 128, 256])
write_ico('tray.ico', [16, 20, 24, 32, 40, 48, 64])
make(256).save('app-256.png')
make(64).save('logo.png')
print('icons written')
