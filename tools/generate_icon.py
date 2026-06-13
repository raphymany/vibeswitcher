"""
Regenerates the VibeSwitcher app icon (vs-icon.ico + vs-icon-{16,32,64,256}.png)
into VibeSwitcher/Resources/Icons, from the logo's vector geometry on an 80x80 grid
(see Controls/NavLogoIcon.xaml):

  - dark rounded-square background  #13131E, corner radius 17
  - full wrapping orange border      #F5820A
  - bold V chevron  (15,20)->(40,55)->(65,20), stroke 5.4, round caps/joins
  - 5 equalizer bars (heights 6/10/14/10/6, opacity .62/.82/1/.82/.62)

Each output size is rendered individually with heavy supersampling so the small
frames stay crisp (instead of being blurred downscales of the 256 master), and
the .ico embeds a real per-size frame for every entry.

Run:  python tools/generate_icon.py   (requires Pillow)
"""
import os, struct, io
from PIL import Image, ImageDraw, ImageFilter

ORANGE = (245, 130, 10)
BG     = (19, 19, 30)
U      = 80.0  # design grid units

def lerp(a, b, t): return tuple(int(round(a[i] + (b[i]-a[i])*t)) for i in range(len(a)))

def render(size):
    ss = max(4, min(16, 1024 // size))   # per-size supersample factor
    R = int(size * ss)
    s = R / U                            # grid-units -> pixels
    img = Image.new("RGBA", (R, R), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Rounded-square background.
    radius = 17 * s
    d.rounded_rectangle([0, 0, R-1, R-1], radius=radius, fill=BG + (255,))
    # Subtle vertical gradient overlay (top darker -> bottom slightly warmer).
    grad = Image.new("RGBA", (R, R), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    for y in range(R):
        t = y / R
        gd.line([(0, y), (R, y)], fill=lerp((0, 0, 0), (22, 12, 6), t) + (int(70 * t),))
    mask = Image.new("L", (R, R), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, R-1, R-1], radius=radius, fill=255)
    img.paste(Image.alpha_composite(img, grad), (0, 0))
    img.putalpha(mask)  # keep rounded transparency after the composite

    # Warm radial glow, bottom-center.
    glow = Image.new("RGBA", (R, R), (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(glow)
    gx, gy, gr = 40 * s, 58 * s, 34 * s
    gdraw.ellipse([gx-gr, gy-gr, gx+gr, gy+gr], fill=ORANGE + (60,))
    glow = glow.filter(ImageFilter.GaussianBlur(radius=gr * 0.45))
    glow.putalpha(Image.eval(glow.split()[3], lambda a: int(a * 0.55)))
    img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (R, R), (0,0,0,0)), mask))

    d = ImageDraw.Draw(img)

    # Full wrapping orange border — inset so the rounded outer corners stay clean.
    bw = max(2.0, 2.6 * s)
    inset = bw / 2 + 0.5 * s
    d.rounded_rectangle([inset, inset, R-1-inset, R-1-inset],
                        radius=radius - inset, outline=ORANGE + (235,), width=int(round(bw)))

    # V chevron with round caps/joins — large and bold so it fills the tile.
    pts = [(15*s, 20*s), (40*s, 55*s), (65*s, 20*s)]
    vw = 5.4 * s
    d.line([pts[0], pts[1]], fill=ORANGE + (255,), width=int(round(vw)))
    d.line([pts[1], pts[2]], fill=ORANGE + (255,), width=int(round(vw)))
    r = vw / 2
    for (px, py) in pts:
        d.ellipse([px-r, py-r, px+r, py+r], fill=ORANGE + (255,))

    # Equalizer bars (left, top, height, opacity) in grid units; bottoms aligned at y=74.
    BW = 5.5
    bars = [(20.25, 68, 6, 0.62), (28.75, 64, 10, 0.82), (37.25, 60, 14, 1.0),
            (45.75, 64, 10, 0.82), (54.25, 68, 6, 0.62)]
    for (bx, by, bh, op) in bars:
        x0, y0 = bx * s, by * s
        x1, y1 = (bx + BW) * s, (by + bh) * s
        d.rounded_rectangle([x0, y0, x1, y1], radius=2.5 * s, fill=ORANGE + (int(255 * op),))

    return img.resize((size, size), Image.LANCZOS)

SIZES = [16, 24, 32, 48, 64, 128, 256]
frames = {sz: render(sz) for sz in SIZES}

OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                    "..", "VibeSwitcher", "Resources", "Icons"))
for sz in (16, 32, 64, 256):
    frames[sz].save(os.path.join(OUT, f"vs-icon-{sz}.png"))

# Assemble a real multi-frame .ico with a PNG-encoded frame per size.
def png_bytes(im):
    b = io.BytesIO(); im.save(b, format="PNG"); return b.getvalue()

entries = [(sz, png_bytes(frames[sz])) for sz in SIZES]
header = struct.pack("<HHH", 0, 1, len(entries))
offset = 6 + 16 * len(entries)
dir_blob, img_blob = b"", b""
for sz, data in entries:
    w = h = 0 if sz == 256 else sz
    dir_blob += struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset)
    img_blob += data
    offset += len(data)
with open(os.path.join(OUT, "vs-icon.ico"), "wb") as f:
    f.write(header + dir_blob + img_blob)

print("done:", [f"{sz}px" for sz in SIZES], "->", OUT)
