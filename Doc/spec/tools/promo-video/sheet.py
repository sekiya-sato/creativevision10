# 静止画の確認用コンタクトシート: python sheet.py v1 [枚数] → 直近の out/stills/v1_<時刻>/sheet_N.jpg
import os, sys, glob
from PIL import Image, ImageDraw
import core

name = sys.argv[1]
per = int(sys.argv[2]) if len(sys.argv) > 2 else 6
sd = sorted(glob.glob(os.path.join(core.OUT, 'stills', f'{name}_[0-9]*')), key=os.path.getmtime)[-1]
fs = sorted(glob.glob(os.path.join(sd, f'{name}_[0-9]*.jpg')))
tw, th = 960, 540
for si in range(0, len(fs), per):
    chunk = fs[si:si + per]
    rows = (len(chunk) + 1) // 2
    sheet = Image.new('RGB', (tw * 2 + 6, th * rows + 6 * (rows - 1)), (60, 60, 60))
    for i, f in enumerate(chunk):
        im = Image.open(f).resize((tw, th), Image.LANCZOS)
        ImageDraw.Draw(im).text((10, 10), os.path.basename(f), fill=(255, 255, 0))
        sheet.paste(im, ((i % 2) * (tw + 6), (i // 2) * (th + 6)))
    out = os.path.join(sd, f'sheet_{si // per}.jpg')
    sheet.save(out, quality=85)
    print(out)
