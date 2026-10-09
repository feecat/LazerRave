"""Generate Windows and menu assets; requires Pillow and resvg-py."""

from io import BytesIO
from pathlib import Path

from PIL import Image
import resvg_py


root = Path(__file__).resolve().parents[1]
branding = root / "res" / "branding"
png = resvg_py.svg_to_bytes(svg_path=str(branding / "logo.svg"), width=1024, height=1024)
(branding / "logo.png").write_bytes(png)
with Image.open(BytesIO(png)) as image:
    image.save(branding / "logo.ico", sizes=[(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print("Generated logo.png and logo.ico from res/branding/logo.svg.")
