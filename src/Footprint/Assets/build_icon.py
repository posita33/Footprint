"""Generate Footprint.ico with Pillow: python src/Footprint/Assets/build_icon.py."""
from pathlib import Path
from PIL import Image, ImageDraw

scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((32, 32, 992, 992), radius=192, fill="#1670B8")
for points in [[(48, 92), (76, 120), (48, 148)], [(48, 174), (96, 174)]]:
    draw.line([(x * scale, y * scale) for x, y in points], fill="white", width=16 * scale, joint="curve")
    for x, y in points:
        draw.ellipse(((x - 8) * scale, (y - 8) * scale, (x + 8) * scale, (y + 8) * scale), fill="white")
foot = Image.new("RGBA", image.size)
draw = ImageDraw.Draw(foot)
for x, y, rx, ry in [(166, 144, 28, 45), (145, 83, 14, 18), (173, 77, 11, 14), (196, 89, 9, 12)]:
    draw.ellipse(((x - rx) * scale, (y - ry) * scale, (x + rx) * scale, (y + ry) * scale), fill="white")
image.alpha_composite(foot.rotate(-24, resample=Image.Resampling.BICUBIC, center=(167 * scale, 133 * scale)))
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(Path(__file__).with_name("Footprint.ico"), sizes=[(n, n) for n in [16, 24, 32, 48, 64, 128, 256]])
