"""Render original PaneSail illustration assets; requires Pillow and a sans-serif font.

The rendered GIF is an explanatory model, not footage of the app. Windows Segoe UI
is used when available; other supported hosts can pass --font and --font-bold.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
SCALE = 2
WIDTH, HEIGHT = 1200, 675
FRAME_COUNT = 106

NAVY = "#0c172a"
PANEL = "#14263b"
SCREEN = "#e7f3f1"
TEAL = "#50dfbe"
TEAL_DARK = "#127e72"
GOLD = "#ffbc68"
WHITE = "#f5fbf9"
MUTED = "#a9c6c9"
INK = "#19384a"


def color(value: str) -> tuple[int, int, int]:
    return tuple(int(value[i:i + 2], 16) for i in (1, 3, 5))


def scaled(v: float) -> int:
    return round(v * SCALE)


def box(values: tuple[float, float, float, float]) -> tuple[int, int, int, int]:
    return tuple(map(scaled, values))


def mix(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def ease(t: float) -> float:
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def font(path: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(path, scaled(size))


def gradient(size: tuple[int, int], first: str, last: str) -> Image.Image:
    w, h = size
    start, end = color(first), color(last)
    image = Image.new("RGB", size)
    draw = ImageDraw.Draw(image)
    for y in range(h):
        t = y / max(1, h - 1)
        draw.line((0, y, w, y), fill=tuple(round(mix(start[i], end[i], t)) for i in range(3)))
    return image


def centered(draw: ImageDraw.ImageDraw, x: float, y: float, text: str, face: ImageFont.FreeTypeFont, fill: str) -> None:
    draw.text((scaled(x), scaled(y)), text, font=face, fill=fill, anchor="mt")


def monitor(draw: ImageDraw.ImageDraw, left: int, top: int, width: int, height: int, label: str, face: ImageFont.FreeTypeFont) -> None:
    draw.rounded_rectangle(box((left + 8, top + 12, left + width + 8, top + height + 12)), radius=scaled(18), fill="#08101e")
    draw.rounded_rectangle(box((left, top, left + width, top + height)), radius=scaled(18), fill="#315266", outline="#8db2b6", width=scaled(2))
    draw.rounded_rectangle(box((left + 12, top + 12, left + width - 12, top + height - 12)), radius=scaled(9), fill=SCREEN)
    # Screen stand and plinth.
    draw.rounded_rectangle(box((left + width / 2 - 14, top + height, left + width / 2 + 14, top + height + 20)), radius=scaled(4), fill="#71969d")
    draw.rounded_rectangle(box((left + width / 2 - 57, top + height + 17, left + width / 2 + 57, top + height + 25)), radius=scaled(4), fill="#71969d")
    centered(draw, left + width / 2, top - 31, label, face, MUTED)


def window(draw: ImageDraw.ImageDraw, x: float, y: float, width: float, height: float, regular: ImageFont.FreeTypeFont, small: bool) -> None:
    # Interior lines reflow as the window changes shape; content is not stretched.
    draw.rounded_rectangle(box((x + 5, y + 8, x + width + 5, y + height + 8)), radius=scaled(12), fill="#9bbeb9")
    draw.rounded_rectangle(box((x, y, x + width, y + height)), radius=scaled(12), fill=WHITE, outline=INK, width=scaled(2))
    draw.rounded_rectangle(box((x + 1, y + 1, x + width - 1, y + 30)), radius=scaled(10), fill="#c5eee5")
    draw.rectangle(box((x + 1, y + 20, x + width - 1, y + 30)), fill="#c5eee5")
    for index, dot in enumerate(("#f6a88e", "#ffd083", "#72d8b9")):
        cx = x + 15 + 11 * index
        draw.ellipse(box((cx - 3, y + 12, cx + 3, y + 18)), fill=dot)
    content_y = y + 47
    if not small:
        draw.rounded_rectangle(box((x + 14, content_y, x + width - 14, content_y + 28)), radius=scaled(5), fill="#d6ebe7")
        n = max(1, min(5, int((height - 90) / 22)))
        for line in range(n):
            r = max(38, (width - 30) * (0.84 if line % 2 == 0 else 0.61))
            line_y = content_y + 44 + line * 22
            if line_y < y + height - 10:
                draw.rounded_rectangle(box((x + 15, line_y, x + 15 + r, line_y + 5)), radius=scaled(2), fill="#a8cac4")
    else:
        draw.rounded_rectangle(box((x + 14, content_y, x + width - 14, content_y + 22)), radius=scaled(5), fill="#d6ebe7")
        if height > 85:
            draw.rounded_rectangle(box((x + 15, content_y + 34, x + width * .7, content_y + 39)), radius=scaled(2), fill="#a8cac4")


def pointer(draw: ImageDraw.ImageDraw, x: float, y: float, release: bool) -> None:
    coords = [(x, y), (x + 1, y + 27), (x + 8, y + 20), (x + 14, y + 32), (x + 20, y + 29), (x + 14, y + 17), (x + 24, y + 17)]
    draw.polygon([(scaled(a), scaled(b)) for a, b in coords], fill=INK, outline=WHITE, width=scaled(2))
    if release:
        for r in (13, 20):
            draw.ellipse(box((x - r, y - r, x + r, y + r)), outline=GOLD, width=scaled(2))


def build_icon(output: Path) -> None:
    z = 3
    image = gradient((256 * z, 256 * z), "#183653", "#0b172c").convert("RGBA")
    mask = Image.new("L", image.size, 0)
    m = ImageDraw.Draw(mask)
    m.rounded_rectangle((0, 0, 256*z-1, 256*z-1), radius=55*z, fill=255)
    image.putalpha(mask)
    d = ImageDraw.Draw(image)
    d.rounded_rectangle((34*z, 44*z, 222*z, 212*z), radius=18*z, fill="#102940", outline="#e3f4f1", width=8*z)
    d.line((38*z, 80*z, 218*z, 80*z), fill="#e3f4f1", width=7*z)
    for x in (55, 69):
        d.ellipse(((x-4)*z, 59*z, (x+4)*z, 67*z), fill="#72dec8")
    d.polygon([(79*z,172*z),(126*z,97*z),(126*z,172*z)], fill="#5be1c2")
    d.polygon([(138*z,113*z),(170*z,172*z),(138*z,172*z)], fill="#c1f8e8")
    d.polygon([(72*z,182*z),(183*z,182*z),(170*z,197*z),(86*z,197*z)], fill=GOLD)
    image.resize((256, 256), Image.Resampling.LANCZOS).save(output, optimize=True)


def build_frame(index: int, fonts: tuple[ImageFont.FreeTypeFont, ...], background: Image.Image) -> Image.Image:
    heading, body, label, status = fonts
    image = background.copy()
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle(box((52, 40, 148, 73)), radius=scaled(16), fill="#1e4252")
    centered(draw, 100, 46, "PANESAIL", label, "#87f3d9")
    draw.text((scaled(55), scaled(88)), "A better fit between screens.", font=heading, fill=WHITE)
    draw.text((scaled(56), scaled(141)), "ILLUSTRATION OF INTENDED BEHAVIOR", font=label, fill=MUTED)

    monitor(draw, 65, 260, 585, 286, "LANDSCAPE SCREEN", label)
    monitor(draw, 823, 208, 269, 393, "PORTRAIT SCREEN", label)
    draw.line((scaled(681), scaled(401), scaled(774), scaled(401)), fill="#628995", width=scaled(3))
    draw.polygon([(scaled(774),scaled(393)), (scaled(788),scaled(401)), (scaled(774),scaled(409))], fill="#628995")

    if index < 60:
        if index < 13:
            phase, title, detail = 0.0, "READY TO MOVE", "A wide window starts on the landscape screen."
        elif index < 37:
            phase, title, detail = ease((index - 13) / 23), "DRAG BETWEEN SCREENS", "Drag the window toward the other screen."
        elif index < 44:
            phase, title, detail = 1.0, "RELEASE", "The window is too wide for the portrait screen."
        else:
            phase, title, detail = 1.0, "A BETTER FIT", "After release, PaneSail resizes it to fit."
        x, y = mix(155, 791, phase), mix(318, 288, phase)
        resize = ease((index - 44) / 10) if index >= 44 else 0
        x = mix(x, 885, resize)
        width, height = mix(370, 160, resize), mix(145, 201, resize)
        window(draw, x, y, width, height, body, False)
        if index < 49:
            pointer(draw, mix(306, 939, phase), mix(332, 302, phase), 37 <= index < 44)
    else:
        if index < 72:
            phase, title, detail = 0.0, "ALREADY A GOOD FIT", "This smaller window fits on the next screen."
        elif index < 94:
            phase, title, detail = ease((index - 72) / 21), "DRAG BETWEEN SCREENS", "Move it to the portrait screen."
        else:
            phase, title, detail = 1.0, "KEEP IT AS IT IS", "If it fits on release, PaneSail leaves it alone."
        x, y = mix(248, 885, phase), mix(335, 330, phase)
        window(draw, x, y, 152, 104, body, True)
        if index < 100:
            pointer(draw, mix(318, 955, phase), mix(348, 343, phase), 94 <= index < 100)

    draw.rounded_rectangle(box((52, 576, 700, 641)), radius=scaled(16), fill=PANEL, outline="#284458", width=scaled(1))
    draw.ellipse(box((73, 594, 84, 605)), fill=TEAL)
    draw.text((scaled(96), scaled(583)), title, font=label, fill=TEAL)
    draw.text((scaled(96), scaled(605)), detail, font=status, fill=WHITE)
    return image.resize((WIDTH, HEIGHT), Image.Resampling.LANCZOS)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, default=HERE)
    parser.add_argument("--font", default=r"C:\Windows\Fonts\segoeui.ttf")
    parser.add_argument("--font-bold", default=r"C:\Windows\Fonts\segoeuib.ttf")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    fonts = (font(args.font_bold, 38), font(args.font, 18), font(args.font_bold, 13), font(args.font, 17))
    background = gradient((scaled(WIDTH), scaled(HEIGHT)), NAVY, "#183848")
    frames = [build_frame(i, fonts, background) for i in range(FRAME_COUNT)]
    frames[0].save(args.out / "panesail-illustration.png", optimize=True)
    for name, number in (("preview-drop.png", 39), ("preview-fit.png", 55), ("preview-smaller.png", 101)):
        frames[number].save(args.out / name, optimize=True)
    frames[0].save(args.out / "panesail-illustration.gif", save_all=True, append_images=frames[1:],
                   duration=90, loop=0, optimize=True, disposal=2)
    build_icon(args.out / "panesail-icon.png")
    print(f"Created {FRAME_COUNT} frames at {WIDTH}x{HEIGHT}; duration {FRAME_COUNT * 90 / 1000:.2f}s; loops indefinitely.")
    for path in sorted(args.out.iterdir()):
        if path.suffix in {".gif", ".png", ".svg"}:
            print(f"{path.name}: {path.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
