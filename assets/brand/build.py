# Regenerates every raster asset from the SVG sources in this folder:
#   python assets/brand/build.py
# Rasterizes with headless Microsoft Edge (ships with Windows) and packs the .ico with Pillow (pip install pillow).
import pathlib, shutil, subprocess, sys, tempfile
from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent.parent
WWW = ROOT / "wwwroot"
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
FONT = '"Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif'
TAGLINE = "Temperaturas e fans da sua placa-mãe Gigabyte, no celular."


def screenshot(html, out, w, h, scale=1):
    """Renders an HTML string at w x h CSS pixels (times scale) with a transparent background."""
    work = pathlib.Path(tempfile.mkdtemp(prefix="fanpilot-brand"))
    try:
        page = work / "page.html"
        page.write_text(html, encoding="utf-8")
        for svg in HERE.glob("*.svg"):
            shutil.copy(svg, work / svg.name)
        subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars", f"--user-data-dir={work / 'profile'}",
                        "--default-background-color=00000000", f"--force-device-scale-factor={scale}",
                        f"--window-size={w},{h}", f"--screenshot={out}", page.as_uri()], capture_output=True, timeout=90)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    if not pathlib.Path(out).exists():
        sys.exit("falha ao renderizar " + str(out))
    out = pathlib.Path(out)
    if out.is_relative_to(ROOT):
        print("ok", out.relative_to(ROOT))


def svg_png(svg, out, size):
    screenshot(f'<style>html,body{{margin:0}}img{{display:block;width:{size}px;height:{size}px}}</style><img src="{svg}">',
               out, size, size)


def banner(out, ink, muted, mark):
    # Wordmark: "Fan" in the text color, "Pilot" in the dial gradient. Transparent so it sits on GitHub's light/dark page.
    html = f"""<style>
  html,body{{margin:0;background:transparent}}
  .b{{display:flex;align-items:center;gap:26px;height:160px;padding:0 28px;font-family:{FONT}}}
  img{{width:104px;height:104px}}
  h1{{margin:0;font-size:64px;font-weight:700;letter-spacing:-1.5px;line-height:1;color:{ink}}}
  h1 span{{background:linear-gradient(90deg,#3987e5,#9b6fd8 50%,#eb6834);-webkit-background-clip:text;color:transparent}}
  p{{margin:10px 0 0;font-size:19px;color:{muted}}}
</style><div class="b"><img src="{mark}"><div><h1>Fan<span>Pilot</span></h1><p>{TAGLINE}</p></div></div>"""
    screenshot(html, out, 760, 160, scale=2)


def social(out):
    # GitHub "social preview" (Settings > General): 1280x640.
    html = f"""<style>
  html,body{{margin:0}}
  .s{{width:1280px;height:640px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;
      background:radial-gradient(circle at 50% 35%,#1d3150,#0b1320 70%);font-family:{FONT};color:#f0efec}}
  img{{width:220px;height:220px}}
  h1{{margin:0;font-size:104px;font-weight:700;letter-spacing:-2.5px;line-height:1}}
  h1 span{{background:linear-gradient(90deg,#3987e5,#9b6fd8 50%,#eb6834);-webkit-background-clip:text;color:transparent}}
  p{{margin:14px 0 0;font-size:34px;color:#c3c2b7}}
</style><div class="s"><img src="fanpilot-mark-dark.svg"><h1>Fan<span>Pilot</span></h1><p>{TAGLINE}</p></div>"""
    screenshot(html, out, 1280, 640)


def ico(out):
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    tmp = pathlib.Path(tempfile.mkdtemp(prefix="fanpilot-ico"))
    try:
        frames = []
        for s in sizes:  # each size rendered on its own, so small icons stay sharp
            svg_png("fanpilot-icon.svg", tmp / f"{s}.png", s)
            frames.append(Image.open(tmp / f"{s}.png").convert("RGBA"))
        frames[-1].save(out, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
        for f in frames:
            f.close()
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print("ok", pathlib.Path(out).relative_to(ROOT))


if __name__ == "__main__":
    shutil.copy(HERE / "fanpilot-icon.svg", WWW / "favicon.svg")
    svg_png("fanpilot-icon.svg", WWW / "icon-192.png", 192)
    svg_png("fanpilot-icon.svg", WWW / "icon-512.png", 512)
    svg_png("fanpilot-icon-square.svg", WWW / "icon-maskable-512.png", 512)
    svg_png("fanpilot-icon-square.svg", WWW / "apple-touch-icon.png", 180)
    ico(HERE / "fanpilot.ico")
    banner(HERE / "banner-light.png", "#0b0b0b", "#52514e", "fanpilot-mark-light.svg")
    banner(HERE / "banner-dark.png", "#f0efec", "#9a998f", "fanpilot-mark-dark.svg")
    social(HERE / "social-preview.png")
