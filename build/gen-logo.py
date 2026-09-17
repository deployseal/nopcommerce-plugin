"""
Render the plugin logo (the DeploySeal wax seal, "flat" cut) as a PNG.

nopCommerce shows a plugin's logo.png in Configuration -> Local plugins. This draws the same
outlines and tones the product's SealMark component uses (packages/ui/src/components/seal-mark.tsx
in the DeploySeal monorepo: body, pressed field, die ring and the check cut into the wax),
rasterised with Pillow only, so it needs no SVG toolchain.

    python build/gen-logo.py                # writes src/DeploySeal.Nop.Widget.Shared/logo.png (128 px)
    python build/gen-logo.py out.png 256    # custom output / size

Requires: Pillow
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw

# --- Outlines, verbatim from SealMark (viewBox 0 0 200 200) ---------------------------------
PATH_BODY = (
    "M176.93,100C177.23,104.04 177.47,108.11 177.42,112.26C177.37,116.41 177.35,120.73 176.63,124.9C175.91,129.06 174.85,133.39 173.1,137.25C171.35,141.11 168.83,144.77 166.14,148.05C163.45,151.34 160.11,154.13 156.95,156.95C153.8,159.78 150.55,162.39 147.22,164.99C143.89,167.6 140.69,170.61 136.98,172.58C133.27,174.56 129.09,176.07 124.97,176.85C120.84,177.62 116.4,177.16 112.23,177.24C108.07,177.32 104.08,177.3 100,177.34C95.92,177.38 91.83,177.77 87.72,177.5C83.62,177.24 79.41,176.77 75.39,175.75C71.37,174.74 67.38,173.21 63.6,171.43C59.83,169.65 56.07,167.56 52.73,165.06C49.39,162.57 46.13,159.69 43.56,156.44C40.99,153.19 38.95,149.33 37.3,145.55C35.66,141.78 34.81,137.62 33.68,133.79C32.56,129.96 31.84,126.25 30.53,122.57C29.22,118.9 27.56,115.51 25.83,111.75C24.1,107.99 21.64,104.15 20.15,100C18.65,95.85 17,91.2 16.86,86.83C16.72,82.46 17.53,77.74 19.29,73.78C21.06,69.81 24.33,66.17 27.46,63.04C30.59,59.91 34.67,57.54 38.07,55C41.46,52.47 44.81,50.38 47.84,47.84C50.87,45.3 53.39,42.54 56.25,39.78C59.11,37.02 61.77,33.79 64.98,31.28C68.2,28.76 71.75,26.22 75.53,24.69C79.31,23.16 83.58,22.3 87.66,22.07C91.74,21.83 95.99,22.62 100,23.28C104.01,23.94 107.82,25.25 111.71,26.04C115.61,26.84 119.4,27.36 123.38,28.05C127.36,28.73 131.67,28.92 135.58,30.18C139.48,31.43 143.44,33.22 146.82,35.55C150.2,37.88 153.12,41.06 155.85,44.15C158.57,47.24 160.99,50.65 163.18,54.1C165.37,57.55 167.34,61.16 168.99,64.85C170.64,68.54 171.98,72.39 173.08,76.25C174.19,80.12 174.96,84.07 175.6,88.03C176.24,91.98 176.62,95.96 176.93,100Z"
)
PATH_RING = (
    "M163.59,100C163.14,104.13 162.08,108.2 160.96,112.12C159.84,116.05 158.49,119.87 156.86,123.55C155.24,127.23 153.42,130.88 151.19,134.21C148.97,137.53 146.35,140.7 143.49,143.49C140.63,146.28 137.37,148.73 134.04,150.94C130.71,153.15 127.16,155.06 123.51,156.77C119.87,158.47 116.09,160.04 112.17,161.17C108.25,162.3 104.11,163.27 100,163.56C95.89,163.85 91.55,163.7 87.48,162.93C83.41,162.15 79.33,160.7 75.59,158.93C71.86,157.15 68.34,154.75 65.07,152.27C61.8,149.79 58.8,146.96 55.97,144.03C53.14,141.1 50.42,138.02 48.1,134.68C45.77,131.35 43.53,127.78 42,124.03C40.46,120.27 39.35,116.16 38.88,112.16C38.4,108.15 38.72,103.97 39.16,100C39.6,96.03 40.67,92.22 41.49,88.36C42.32,84.5 43.15,80.78 44.12,76.86C45.09,72.93 45.8,68.73 47.32,64.8C48.84,60.86 50.62,56.58 53.25,53.25C55.89,49.92 59.42,46.89 63.13,44.82C66.84,42.75 71.34,41.66 75.49,40.82C79.64,39.99 83.94,39.99 88.03,39.81C92.11,39.62 96.03,39.6 100,39.71C103.97,39.83 107.93,39.87 111.83,40.51C115.73,41.14 119.7,42.09 123.4,43.52C127.1,44.94 130.65,46.94 134.04,49.06C137.42,51.19 140.62,53.65 143.73,56.27C146.83,58.9 149.97,61.66 152.65,64.82C155.34,67.97 158.02,71.46 159.85,75.21C161.68,78.97 163.01,83.21 163.63,87.34C164.25,91.47 164.03,95.87 163.59,100Z"
)
PATH_FIELD = (
    "M156.09,100C155.64,103.64 154.63,107.21 153.6,110.66C152.57,114.11 151.38,117.45 149.93,120.68C148.49,123.91 146.92,127.12 144.96,130.04C143,132.96 140.7,135.74 138.19,138.19C135.67,140.63 132.79,142.76 129.87,144.7C126.95,146.64 123.84,148.32 120.64,149.84C117.45,151.36 114.14,152.78 110.7,153.81C107.26,154.85 103.63,155.77 100,156.06C96.37,156.35 92.54,156.25 88.95,155.57C85.36,154.89 81.75,153.59 78.46,152C75.18,150.41 72.11,148.25 69.24,146.04C66.37,143.82 63.75,141.32 61.27,138.73C58.79,136.14 56.39,133.44 54.33,130.51C52.27,127.59 50.28,124.46 48.93,121.16C47.58,117.85 46.61,114.22 46.23,110.7C45.85,107.17 46.22,103.48 46.66,100C47.1,96.52 48.12,93.2 48.85,89.83C49.58,86.45 50.27,83.2 51.05,79.73C51.84,76.25 52.3,72.49 53.55,68.97C54.8,65.44 56.26,61.54 58.56,58.56C60.85,55.57 64,52.86 67.3,51.06C70.6,49.26 74.66,48.4 78.36,47.75C82.06,47.1 85.88,47.25 89.49,47.16C93.1,47.07 96.52,47.1 100,47.21C103.48,47.33 106.95,47.32 110.37,47.86C113.79,48.4 117.28,49.21 120.53,50.45C123.77,51.69 126.89,53.44 129.87,55.3C132.85,57.15 135.67,59.3 138.42,61.58C141.18,63.86 144,66.23 146.42,68.98C148.83,71.74 151.27,74.78 152.92,78.08C154.56,81.38 155.75,85.15 156.27,88.81C156.8,92.46 156.53,96.36 156.09,100Z"
)
CHECK = [(65.0, 103.0), (87.0, 127.0), (136.0, 72.0)]

TONES = {"body": "#8C3A1E", "field": "#7A2D15", "ring": "#5E2210", "check": "#F3EEE1"}

NUM = re.compile(r"-?\d*\.?\d+(?:e[-+]?\d+)?")


def flatten(d: str, segments: int = 12) -> list[tuple[float, float]]:
    """Absolute M/L/C/Z path -> polyline."""
    pts: list[tuple[float, float]] = []
    cur = (0.0, 0.0)
    for cmd, args in re.findall(r"([MLCZ])([^MLCZ]*)", d):
        nums = [float(n) for n in NUM.findall(args)]
        if cmd == "M":
            cur = (nums[0], nums[1])
            pts.append(cur)
        elif cmd == "L":
            cur = (nums[0], nums[1])
            pts.append(cur)
        elif cmd == "C":
            for i in range(0, len(nums), 6):
                p1, p2, p3 = (nums[i], nums[i + 1]), (nums[i + 2], nums[i + 3]), (nums[i + 4], nums[i + 5])
                p0 = cur
                for s in range(1, segments + 1):
                    t = s / segments
                    mt = 1 - t
                    x = mt**3 * p0[0] + 3 * mt**2 * t * p1[0] + 3 * mt * t**2 * p2[0] + t**3 * p3[0]
                    y = mt**3 * p0[1] + 3 * mt**2 * t * p1[1] + 3 * mt * t**2 * p2[1] + t**3 * p3[1]
                    pts.append((x, y))
                cur = p3
    return pts


def render(size: int, supersample: int = 8) -> Image.Image:
    big = size * supersample
    scale = big / 200.0
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    def sc(points):
        return [(x * scale, y * scale) for x, y in points]

    draw.polygon(sc(flatten(PATH_BODY)), fill=TONES["body"])
    draw.polygon(sc(flatten(PATH_FIELD)), fill=TONES["field"])

    # Die ring: 1.8 units wide at 50% opacity, so blend the tone onto the field/body colour.
    ring_layer = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(ring_layer).line(sc(flatten(PATH_RING)) + sc(flatten(PATH_RING)[:1]),
                                    fill=TONES["ring"] + "80", width=max(1, round(1.8 * scale)), joint="curve")
    img.alpha_composite(ring_layer)

    # The check: 15-unit stroke, round caps and joins.
    w = 15 * scale
    check = sc(CHECK)
    draw.line(check, fill=TONES["check"], width=round(w), joint="curve")
    for (x, y) in check:
        draw.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=TONES["check"])

    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "src" / "DeploySeal.Nop.Widget.Shared" / "logo.png"
    size = int(sys.argv[2]) if len(sys.argv) > 2 else 128
    render(size).save(out, optimize=True)
    print(f"wrote {out} ({size}x{size})")


if __name__ == "__main__":
    main()
