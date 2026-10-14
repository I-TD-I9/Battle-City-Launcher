"""Draw the launcher's icon: the plaza clock in gold, against a summer sky.

    python tools/make_icon.py

writes src/BattleCity.Launcher/Assets/BattleCity.ico, 16 to 256 pixels. Needs Pillow. The drawing
is made here from plain shapes, so the repository holds no artwork it would need a licence for.
The small sizes are drawn on their own, with a bigger dial and heavier lines, so that they still
read as a clock.
"""
import math
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, 'src', 'BattleCity.Launcher', 'Assets')

SCALE = 8                       # each size is drawn this many times larger, then scaled down

SKY_TOP = (36, 112, 200)
SKY_BOTTOM = (159, 211, 244)
INK = (28, 34, 48)
FACE = (255, 253, 244)
CLOUD = (255, 255, 255)
CLOUD_SHADE = (207, 227, 245)
# Gold round a rod or a drum, lit from the left: dark at both edges, brightest a third across.
GOLD_ROUND = ((0.0, (138, 100, 16)), (0.32, (255, 229, 140)), (0.6, (240, 192, 64)),
              (0.85, (184, 134, 26)), (1.0, (138, 100, 16)))
# The dial's rim, lit from the top.
GOLD_RIM = ((0.0, (255, 229, 140)), (0.45, (240, 192, 64)), (0.8, (184, 134, 26)), (1.0, (138, 100, 16)))
GOLD_TOP = (255, 229, 140)

# Where things sit, as fractions of the icon's width. The big icon has a cloud, a drum at the
# clock's foot and every hour marked; the small one is the dial and its rod, and little else.
BIG = dict(corner=0.22, dial=(0.5, 0.335), rim=0.215, face=0.157, rod=0.062, foot=0.84,
           drum=(0.81, 0.925, 0.14, 0.19, 0.034), cloud=True, hours=12, ink=0.02,
           hands=(0.087, 0.124, 0.03, 0.021))
SMALL = dict(corner=0.2, dial=(0.5, 0.42), rim=0.37, face=0.255, rod=0.15, foot=1.0,
             drum=None, cloud=False, hours=0, ink=0.055, hands=(0.13, 0.19, 0.085, 0.07))


def blend(stops, t):
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            f = 0 if t1 == t0 else (t - t0) / (t1 - t0)
            return tuple(round(a + (b - a) * f) for a, b in zip(c0, c1))
    return stops[-1][1]


def gradient(size, stops, start, end, across):
    """A square picture shaded from `start` to `end` along x (across) or y, flat beyond them."""
    strip = Image.new('RGB', (size, 1) if across else (1, size))
    for i in range(size):
        t = min(1, max(0, (i - start) / (end - start)))
        strip.putpixel((i, 0) if across else (0, i), blend(stops, t))
    return strip.resize((size, size))


def paint(picture, shapes, fill):
    """Fills the union of `shapes`, each a function drawing on a mask, with a colour or a picture."""
    mask = Image.new('L', picture.size, 0)
    pen = ImageDraw.Draw(mask)
    for shape in shapes:
        shape(pen)
    if isinstance(fill, tuple):
        fill = Image.new('RGB', picture.size, fill)
    picture.paste(fill, (0, 0), mask)


def draw(size, plan):
    n = size * SCALE

    def at(v):
        return v * n

    picture = gradient(n, ((0, SKY_TOP), (1, SKY_BOTTOM)), 0, n, across=False).convert('RGBA')
    pen = ImageDraw.Draw(picture)
    # Every gold shape is inked by drawing it first in ink, this much bigger all round.
    w = at(plan['ink'])

    if plan['cloud']:
        puffs = ((0.6, 0.66, 0.1, 0.06), (0.7, 0.61, 0.11, 0.085), (0.81, 0.655, 0.1, 0.06),
                 (0.71, 0.7, 0.18, 0.05), (0.14, 0.2, 0.08, 0.05), (0.21, 0.165, 0.08, 0.065),
                 (0.17, 0.225, 0.12, 0.04))
        for dx, dy, colour in ((0.006, 0.012, CLOUD_SHADE), (0, 0, CLOUD)):
            for x, y, rx, ry in puffs:
                pen.ellipse([at(x - rx + dx), at(y - ry + dy), at(x + rx + dx), at(y + ry + dy)], fill=colour)

    cx, cy = at(plan['dial'][0]), at(plan['dial'][1])

    # The rod, from behind the dial to its foot, and the drum it stands in.
    half = at(plan['rod']) / 2
    foot = at(plan['foot'])

    def rod(grow):
        return lambda d: d.rectangle([cx - half - grow, cy, cx + half + grow, foot + grow], fill=255)

    shapes = [rod]
    if plan['drum']:
        top, bottom, top_half, bottom_half, lip = (at(v) for v in plan['drum'])

        def body(grow):
            return lambda d: d.polygon([(cx - top_half - grow, top), (cx + top_half + grow, top),
                                        (cx + bottom_half + grow, bottom), (cx - bottom_half - grow, bottom)], fill=255)

        def base(grow):
            return lambda d: d.ellipse([cx - bottom_half - grow, bottom - lip - grow,
                                        cx + bottom_half + grow, bottom + lip + grow], fill=255)

        def crown(grow):
            return lambda d: d.ellipse([cx - top_half - grow, top - lip * 0.7 - grow,
                                        cx + top_half + grow, top + lip * 0.7 + grow], fill=255)

        shapes += [body, base, crown]
    paint(picture, [shape(w) for shape in shapes], INK)
    width = at(plan['drum'][3]) if plan['drum'] else half
    paint(picture, [shape(0) for shape in shapes], gradient(n, GOLD_ROUND, cx - width, cx + width, across=True))
    if plan['drum']:
        paint(picture, [crown(0)], GOLD_TOP)
        pen.ellipse([cx - top_half, top - lip * 0.7, cx + top_half, top + lip * 0.7], outline=INK, width=round(w * 0.6))
        # The rod goes into the drum, so its foot shows on the drum's top.
        pen.ellipse([cx - half, top - lip * 0.3, cx + half, top + lip * 0.3], fill=blend(GOLD_ROUND, 0.85))

    # The dial: a thick gold rim round a white face.
    rim, face = at(plan['rim']), at(plan['face'])
    pen.ellipse([cx - rim - w, cy - rim - w, cx + rim + w, cy + rim + w], fill=INK)
    paint(picture, [lambda d: d.ellipse([cx - rim, cy - rim, cx + rim, cy + rim], fill=255)],
          gradient(n, GOLD_RIM, cy - rim, cy + rim, across=False))
    pen.ellipse([cx - face - w * 0.7, cy - face - w * 0.7, cx + face + w * 0.7, cy + face + w * 0.7], fill=INK)
    pen.ellipse([cx - face, cy - face, cx + face, cy + face], fill=FACE)
    for hour in range(plan['hours']):
        a = hour * math.pi / 6
        quarter = hour % 3 == 0
        r0, r1 = face * (0.66 if quarter else 0.76), face * 0.9
        pen.line([cx + r0 * math.sin(a), cy - r0 * math.cos(a), cx + r1 * math.sin(a), cy - r1 * math.cos(a)],
                 fill=INK, width=max(1, round(w * (1.0 if quarter else 0.55))))

    # Ten past ten, as clocks are always shown.
    hour_length, minute_length, hour_width, minute_width = plan['hands']
    for angle, length, thickness in ((math.radians(305), hour_length, hour_width),
                                     (math.radians(60), minute_length, minute_width)):
        end = (cx + at(length) * math.sin(angle), cy - at(length) * math.cos(angle))
        pen.line([cx, cy, *end], fill=INK, width=max(1, round(at(thickness))))
        r = at(thickness) / 2
        pen.ellipse([end[0] - r, end[1] - r, end[0] + r, end[1] + r], fill=INK)
    cap = at(plan['hands'][2]) * 0.75
    pen.ellipse([cx - cap - w * 0.5, cy - cap - w * 0.5, cx + cap + w * 0.5, cy + cap + w * 0.5], fill=INK)
    pen.ellipse([cx - cap, cy - cap, cx + cap, cy + cap], fill=blend(GOLD_RIM, 0.4))

    # Rounded off, with a dark rim so it holds its own on a light background.
    corner = at(plan['corner'])
    edge = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rounded_rectangle([0, 0, n - 1, n - 1], radius=corner, outline=INK + (110,),
                                           width=max(1, round(n / 64)))
    picture = Image.alpha_composite(picture, edge)
    mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=corner, fill=255)
    picture.putalpha(mask)
    return picture.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(ASSETS, exist_ok=True)
    sizes = (16, 24, 32, 48, 64, 128, 256)
    pictures = [draw(size, SMALL if size < 48 else BIG) for size in sizes]
    pictures[-1].save(os.path.join(ASSETS, 'BattleCity.ico'), sizes=[(s, s) for s in sizes],
                      append_images=pictures[:-1])
    print('wrote', os.path.join(ASSETS, 'BattleCity.ico'))


if __name__ == '__main__':
    main()
