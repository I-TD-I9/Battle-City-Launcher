"""Draw the launcher's icon: a gold city skyline under a rising sun, on a dark rounded square.

    python tools/make_icon.py

writes src/BattleCity.Launcher/Assets/BattleCity.ico (16 to 256 pixels) and BattleCity.png (the
256-pixel picture the window's header shows). Needs Pillow. The drawing is made here from plain
shapes, so the repository holds no artwork it would need a licence for.
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, 'src', 'BattleCity.Launcher', 'Assets')

SCALE = 4                       # draw at 1024 pixels and scale down, for smooth edges
SIZE = 256 * SCALE

TOP = (43, 35, 80)              # the sky, from indigo at the top
BOTTOM = (18, 18, 32)           # to the window's own background at the bottom
GOLD = (232, 182, 76)
SUN = (240, 190, 90, 96)
WINDOW = (18, 18, 32)

# (left, width, height) of each building, in 256-pixel units, standing on the ground line.
BUILDINGS = ((34, 30, 64), (66, 34, 104), (102, 24, 80), (128, 42, 138), (172, 28, 92),
             (202, 22, 118))
GROUND = 206


def px(v):
    return int(round(v * SCALE))


def draw():
    sky = Image.new('RGBA', (SIZE, SIZE))
    for y in range(SIZE):
        t = y / (SIZE - 1)
        colour = tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM)) + (255,)
        ImageDraw.Draw(sky).line([(0, y), (SIZE, y)], fill=colour)

    art = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
    pen = ImageDraw.Draw(art)
    pen.ellipse([px(70), px(58), px(186), px(174)], fill=SUN)
    for left, width, height in BUILDINGS:
        pen.rectangle([px(left), px(GROUND - height), px(left + width), px(GROUND)], fill=GOLD)
        # Rows of lit windows, which only the larger sizes keep.
        for row in range(px(GROUND - height + 10), px(GROUND - 12), px(16)):
            for col in range(px(left + 6), px(left + width - 10), px(10)):
                pen.rectangle([col, row, col + px(5), row + px(7)], fill=WINDOW)
    # The tallest tower's spire.
    pen.rectangle([px(147), px(GROUND - 138 - 24), px(151), px(GROUND - 138)], fill=GOLD)
    pen.rectangle([px(24), px(GROUND), px(232), px(GROUND + 6)], fill=GOLD)

    picture = Image.alpha_composite(sky, art)
    mask = Image.new('L', (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, SIZE - 1, SIZE - 1], radius=px(52), fill=255)
    picture.putalpha(mask)
    return picture.resize((256, 256), Image.LANCZOS)


def main():
    os.makedirs(ASSETS, exist_ok=True)
    picture = draw()
    picture.save(os.path.join(ASSETS, 'BattleCity.png'))
    picture.save(os.path.join(ASSETS, 'BattleCity.ico'),
                 sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print('wrote', ASSETS)


if __name__ == '__main__':
    main()
