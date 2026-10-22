# 裸眼立体視の見え方を確かめるテスト画像（1920x1080、等倍表示前提）
#   python stereo_test.py  → Doc/promo/out/stereo_test/<方式>_<記号>.png（1 枚に 1 組）
# 画像ごとに左右画像の中心間隔を変え、どの間隔で楽に重なるかと、図形の奥行き順が正しく見えるかを確かめる。
# 同じ形が横に並ぶと別の図形同士で誤って重なるため、形・色の違う 3 図形を縦に並べる。
# 視差は px 指定（60cm から 24 型 1920 幅で 1° ≒ 38px）。正＝画面より奥。
import os, glob
import numpy as np
from PIL import Image, ImageDraw
from core import W, H, OUT, font

PX_CM = 1920 / 53.13          # 24 型 16:9 の表示幅 53.13cm
D = 24                         # 図形の視差 px（約 0.6°）
PH = 400                       # 左右画像の高さ px（約 11cm）
BG = (14, 12, 30)
OUTDIR = os.path.join(OUT, 'stereo_test')
SHAPES = {'1': ('circle', (95, 214, 216)), '2': ('square', (255, 200, 120)), '3': ('triangle', (186, 140, 255))}

def ftext(d, xy, s, size, fill, fam='noto', weight='Regular', anchor='mm'):
    d.text(xy, s, font=font(fam, size, weight), fill=fill, anchor=anchor)

def shape(d, kind, x, y, r, col):
    w = max(2, int(r / 7))
    if kind == 'circle':
        d.ellipse((x - r, y - r, x + r, y + r), outline=col, width=w)
    elif kind == 'square':
        d.rectangle((x - r * 0.9, y - r * 0.9, x + r * 0.9, y + r * 0.9), outline=col, width=w)
    else:
        d.polygon([(x, y - r), (x + r, y + r * 0.8), (x - r, y + r * 0.8)], outline=col, width=w)

def panel(w, h, eye, depths):
    """1 目分の画像。eye: -1 左目 / +1 右目。depths: 図形 '1','2','3' の視差 px"""
    im = Image.new('RGB', (w, h), BG)
    d = ImageDraw.Draw(im)
    sh = lambda disp: eye * disp / 2
    # 奥の星（視差 +1.5D）
    rs = np.random.default_rng(w * 7 + h)
    for _ in range(int(w * h / 700)):
        x, y = rs.uniform(0, w), rs.uniform(0, h)
        r = rs.uniform(0.6, 1.6)
        x += sh(D * 1.5)
        c = int(rs.uniform(90, 200))
        d.ellipse((x - r, y - r, x + r, y + r), fill=(c, c, min(255, c + 40)))
    # 枠と目印の十字（視差 0）
    d.rectangle((1, 1, w - 2, h - 2), outline=(170, 170, 210), width=3)
    cx, cy = w / 2, 26
    d.line((cx - 12, cy, cx + 12, cy), fill=(255, 255, 255), width=3)
    d.line((cx, cy - 12, cx, cy + 12), fill=(255, 255, 255), width=3)
    # 図形を縦に並べる（横位置も少しずらして左右の取り違えを防ぐ）
    r = min(w * 0.2, 30)
    for i, k in enumerate('123'):
        kind, col = SHAPES[k]
        x = w * (0.38, 0.62, 0.45)[i] + sh(depths[k])
        y = h * (0.3 + 0.24 * i)
        shape(d, kind, x, y, r, col)
        ftext(d, (x, y + (r * 0.15 if kind == 'triangle' else 0)), k, int(r * 0.95), col, 'segoeb', None)
    return im

def sheet(name, cross, sep_cm, depths):
    sep = int(round(sep_cm * PX_CM))
    pw = sep - max(10, int(sep * 0.08))
    L, R = panel(pw, PH, -1, depths), panel(pw, PH, +1, depths)
    a, b = (R, L) if cross else (L, R)
    im = Image.new('RGB', (W, H), (0, 0, 0))
    d = ImageDraw.Draw(im)
    x0 = (W - sep - pw) // 2
    y0 = (H - PH) // 2 + 20
    im.paste(a, (x0, y0)); im.paste(b, (x0 + sep, y0))
    if cross:
        title = f'交差法（寄り目）テスト {name[-1]}  —  左右の間隔 {sep_cm:g}cm'
        steps = ['画面中央と目の間（顔から20〜30cm）に人差し指を立て、指先を見つめます',
                 '指先の向こうで左右の画像が近づき、上の白い十字が1つに重なったら、指を下ろしてそのまま中央を見ます']
    else:
        title = f'平行法（遠くを見る）テスト {name[-1]}  —  左右の間隔 {sep_cm:g}cm'
        steps = ['画面の向こう側を眺めるように目の力を抜きます（画面に鼻を近づけてから、ゆっくり離すと見やすくなります）',
                 '上の白い十字が1つに重なり、十字が3つ並んで見えたら、そのまま中央を見ます']
    ftext(d, (W / 2, 50), title, 34, (255, 255, 255), weight='Medium')
    for i, s in enumerate(steps):
        ftext(d, (W / 2, 104 + i * 34), s, 22, (205, 210, 240), weight='Light')
    ftext(d, (W / 2, H - 120), '重なったら、○□△ を手前から順に答えてください（例: 2 → 1 → 3）', 24, (255, 235, 200), weight='Light')
    # 10cm 定規（拡大縮小の確認用）
    xs, ys = 60, H - 40
    xe = xs + 10 * PX_CM
    d.line((xs, ys, xe, ys), fill=(255, 255, 255), width=2)
    for k in range(11):
        x = xs + k * PX_CM
        d.line((x, ys - (12 if k % 5 == 0 else 6), x, ys), fill=(255, 255, 255), width=2)
    ftext(d, (xe + 16, ys - 6), '← この線が定規で 10cm なら等倍表示です', 18, (200, 200, 220), weight='Light', anchor='lm')
    fn = os.path.join(OUTDIR, name + '.png')
    im.save(fn)
    print(fn)

os.makedirs(OUTDIR, exist_ok=True)
for f in glob.glob(os.path.join(OUTDIR, '*.png')):
    os.remove(f)
# 画像ごとに奥行き順を変えて答え合わせする（負＝手前）
ORDERS = [{'1': -D, '2': 0, '3': D}, {'1': D, '2': -D, '3': 0}, {'1': 0, '2': D, '3': -D}]
for i, cm in enumerate((4.0, 4.5, 5.0)):
    sheet(f'parallel_{"ABC"[i]}', False, cm, ORDERS[i])
for i, cm in enumerate((4.5, 6.0, 7.5)):
    sheet(f'cross_{"ABC"[i]}', True, cm, ORDERS[(i + 1) % 3])
