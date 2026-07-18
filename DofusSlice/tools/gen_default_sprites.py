"""
Generate the ORIGINAL default sprite set for DofusSlice.

These are simple, stylized geometric figures authored from scratch for this project
(a red-tunic warrior, generic creature blobs, grass/rock tiles). They are NOT derived
from Dofus or any third-party art — genre-evocative only. Output is committed to
DofusSlice.Game/assets-default/ and used as the baseline look; anything a user drops
into the (gitignored) assets/ folder overrides it.

Run:  python3 tools/gen_default_sprites.py
"""
import math, os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "DofusSlice.Game", "assets-default")
OUT = os.path.abspath(OUT)
os.makedirs(OUT, exist_ok=True)
FW, FH = 48, 64
OUTLINE = (24, 22, 30)


def shade(color, f):
    return tuple(max(0, min(255, int(c * f))) for c in color)


def outlined_ellipse(d, box, fill):
    d.ellipse([box[0] - 1, box[1] - 1, box[2] + 1, box[3] + 1], fill=OUTLINE + (255,))
    d.ellipse(box, fill=fill + (255,))


def outlined_rrect(d, box, r, fill):
    d.rounded_rectangle([box[0] - 1, box[1] - 1, box[2] + 1, box[3] + 1], radius=r + 1, fill=OUTLINE + (255,))
    d.rounded_rectangle(box, radius=r, fill=fill + (255,))


def warrior(tunic, skin, hair, leg=0.0, arm=0.0, lean=0.0, fall=0.0, flash=False):
    img = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = FW // 2
    if flash:
        tunic = tuple(min(255, c + 70) for c in tunic)
    if fall > 0:
        a = int(255 * (1 - 0.5 * fall))
        yb = 58 - int(4 * fall)
        d.ellipse([cx - 17, yb - 8, cx + 17, yb], fill=OUTLINE + (a,))
        d.ellipse([cx - 15, yb - 7, cx + 15, yb - 1], fill=tunic + (a,))
        d.ellipse([cx - 9, yb - 6, cx - 1, yb - 1], fill=skin + (a,))
        return img
    dx = int(lean * 5)
    lo = int(math.sin(leg) * 5)
    # legs (with a darker boot)
    outlined_rrect(d, [cx - 8, 44, cx - 2, 60 - max(0, lo)], 2, shade(tunic, 0.55))
    outlined_rrect(d, [cx + 2, 44, cx + 8, 60 - max(0, -lo)], 2, shade(tunic, 0.55))
    # torso with a lighter top / darker bottom for volume
    outlined_rrect(d, [cx - 11 + dx, 25, cx + 11 + dx, 47], 5, tunic)
    d.rounded_rectangle([cx - 9 + dx, 26, cx + 9 + dx, 34], radius=4, fill=shade(tunic, 1.18) + (200,))
    d.rounded_rectangle([cx - 10 + dx, 42, cx + 10 + dx, 47], radius=3, fill=shade(tunic, 0.72) + (220,))
    # arms
    ay = 30 - int(arm * 14)
    at, ab = min(28, ay), max(28, ay + 9)
    outlined_rrect(d, [cx - 14 + dx, at, cx - 9 + dx, ab], 2, shade(tunic, 0.9))
    outlined_rrect(d, [cx + 9 + dx, at, cx + 14 + dx, ab], 2, shade(tunic, 0.9))
    if arm > 0.5:
        outlined_ellipse(d, [cx - 7 + dx, ay - 12, cx + 7 + dx, ay + 2], (255, 224, 130))
        d.ellipse([cx - 4 + dx, ay - 9, cx + 4 + dx, ay - 1], fill=(255, 248, 210, 230))
    # head + hair
    outlined_ellipse(d, [cx - 8 + dx, 11, cx + 8 + dx, 27], skin)
    d.chord([cx - 8 + dx, 9, cx + 8 + dx, 25], 180, 360, fill=hair + (255,))
    d.ellipse([cx - 4 + dx, 18, cx - 1 + dx, 21], fill=OUTLINE + (255,))
    d.ellipse([cx + 1 + dx, 18, cx + 4 + dx, 21], fill=OUTLINE + (255,))
    return img


def creature(body, belly, ears=None, tusks=False, beak=False, bob=0.0):
    img = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = FW // 2
    b = int(bob)
    if ears:  # two ears behind the head
        outlined_ellipse(d, [cx - 13, 20 - b, cx - 5, 30 - b], ears)
        outlined_ellipse(d, [cx + 5, 20 - b, cx + 13, 30 - b], ears)
    # body
    outlined_ellipse(d, [cx - 16, 34 - b, cx + 16, 60 - b], body)
    d.ellipse([cx - 11, 42 - b, cx + 11, 58 - b], fill=belly + (255,))
    d.ellipse([cx - 14, 34 - b, cx + 14, 44 - b], fill=shade(body, 1.15) + (150,))
    # head
    outlined_ellipse(d, [cx - 12, 20 - b, cx + 12, 42 - b], body)
    d.ellipse([cx - 6, 26 - b, cx - 2, 31 - b], fill=OUTLINE + (255,))
    d.ellipse([cx + 2, 26 - b, cx + 6, 31 - b], fill=OUTLINE + (255,))
    d.ellipse([cx - 5, 27 - b, cx - 3, 29 - b], fill=(255, 255, 255, 220))
    d.ellipse([cx + 3, 27 - b, cx + 5, 29 - b], fill=(255, 255, 255, 220))
    if tusks:
        d.polygon([(cx - 7, 40 - b), (cx - 4, 40 - b), (cx - 6, 46 - b)], fill=(240, 236, 222, 255))
        d.polygon([(cx + 7, 40 - b), (cx + 4, 40 - b), (cx + 6, 46 - b)], fill=(240, 236, 222, 255))
    if beak:
        d.polygon([(cx - 3, 32 - b), (cx + 3, 32 - b), (cx, 38 - b)], fill=(235, 150, 60, 255))
    return img


def save(name, imgs):
    fw = imgs[0].width
    sheet = Image.new("RGBA", (fw * len(imgs), FH), (0, 0, 0, 0))
    for i, im in enumerate(imgs):
        sheet.paste(im, (i * fw, 0), im)
    suffix = f"_{len(imgs)}" if len(imgs) > 1 else ""
    sheet.save(os.path.join(OUT, f"{name}{suffix}.png"))
    print("wrote", name, len(imgs))


def iso_tile(base, edge):
    img = Image.new("RGBA", (64, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pts = [(32, 0), (63, 16), (32, 31), (0, 16)]
    d.polygon(pts, fill=base + (255,))
    d.line(pts + [pts[0]], fill=edge + (255,), width=1)
    # a few grass blades / speckles for texture
    for (px, py, s) in [(24, 12, 1), (40, 18, 1), (32, 22, 1), (30, 8, 1), (46, 12, 1)]:
        d.rectangle([px, py, px + s, py + s], fill=shade(base, 1.2) + (255,))
    return img


def rock_tile():
    img = Image.new("RGBA", (64, 56), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    base = (150, 138, 122)
    # sits on the diamond footprint (bottom 32px), mound rising above
    d.ellipse([12, 34, 52, 54], fill=OUTLINE + (255,))
    d.ellipse([13, 34, 51, 52], fill=base + (255,))
    d.ellipse([18, 20, 48, 46], fill=OUTLINE + (255,))
    d.ellipse([19, 21, 47, 45], fill=shade(base, 1.08) + (255,))
    d.ellipse([24, 22, 36, 32], fill=shade(base, 1.25) + (200,))
    return img


IOP = dict(tunic=(206, 78, 60), skin=(238, 204, 172), hair=(84, 56, 38))
IOP_NE = dict(tunic=(186, 68, 52), skin=(238, 204, 172), hair=(70, 46, 30))

# Iop, SE (SW mirrors) and NE (NW mirrors)
save("iop_idle_se", [warrior(**IOP, leg=0), warrior(**IOP, leg=0.4)])
save("iop_walk_se", [warrior(**IOP, leg=i * math.pi / 3) for i in range(6)])
save("iop_cast_se", [warrior(**IOP, arm=a) for a in (0.2, 0.65, 1.0, 0.6)])
save("iop_hurt_se", [warrior(**IOP, lean=-1, flash=True), warrior(**IOP, lean=-0.5, flash=True)])
save("iop_die_se", [warrior(**IOP, fall=f) for f in (0.0, 0.4, 0.75, 1.0)])
save("iop_idle_ne", [warrior(**IOP_NE, leg=0), warrior(**IOP_NE, leg=0.4)])
save("iop_walk_ne", [warrior(**IOP_NE, leg=i * math.pi / 3) for i in range(6)])

# Mobs
BOAR = dict(body=(150, 110, 84), belly=(178, 140, 112), ears=(120, 88, 66), tusks=True)
GOB = dict(body=(228, 224, 216), belly=(246, 244, 240), ears=(210, 206, 198))
PIOU = dict(body=(240, 210, 90), belly=(250, 236, 160), beak=True)
save("boar_idle_se", [creature(**BOAR, bob=0), creature(**BOAR, bob=2)])
save("boar_walk_se", [creature(**BOAR, bob=b) for b in (0, 3, 0, -1)])
save("gobball_idle_se", [creature(**GOB, bob=0), creature(**GOB, bob=2)])
save("gobball_walk_se", [creature(**GOB, bob=b) for b in (0, 3, 0, -1)])
save("piou_idle_se", [creature(**PIOU, bob=0), creature(**PIOU, bob=3)])

# Tiles
iso_tile((78, 120, 78), (54, 86, 56)).save(os.path.join(OUT, "tile_grass.png"))
iso_tile((92, 116, 74), (62, 84, 52)).save(os.path.join(OUT, "tile_grass2.png"))
rock_tile().save(os.path.join(OUT, "tile_rock.png"))
print("tiles written; done ->", OUT)
