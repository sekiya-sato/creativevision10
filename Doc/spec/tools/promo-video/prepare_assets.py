# 動画素材の準備（Doc/promo/assets へ出力。再実行で上書き）
#   screens/  Doc/test 配下の実画面スクリーンショット（同名は最大サイズ版を採用、最大1400px）
#   forms/    帳票PDFのレンダリング画像（render/ や PDF 配下）
#   commits.txt     git の全コミット時刻（UNIX秒、古い順）
#   logo_alpha.png  Doc/cv10logo01.png の白背景を透過化したもの
import os, re, subprocess
import numpy as np
from PIL import Image
import core

ROOT = os.path.join(core.REPO, 'Doc', 'test')
OUT = core.ASSETS


def collect_images():
    screens, forms = {}, {}
    for d, _, fs in os.walk(ROOT):
        for f in fs:
            if not f.lower().endswith(('.png', '.jpg')):
                continue
            p = os.path.join(d, f)
            low = p.replace(os.sep, '/').lower()
            if 'minimum' in low or '_min' in low:
                continue
            key = re.sub(r'^\d+_', '', os.path.splitext(f)[0])
            tgt = forms if ('/render/' in low or '/pdf' in low) else screens
            sz = os.path.getsize(p)
            if key not in tgt or tgt[key][1] < sz:
                tgt[key] = (p, sz)
    for name, tgt in (('screens', screens), ('forms', forms)):
        od = os.path.join(OUT, name)
        os.makedirs(od, exist_ok=True)
        n = 0
        for k, (p, _) in sorted(tgt.items()):
            im = Image.open(p).convert('RGB')
            if im.width < 600 or im.height < 400:
                continue
            if name == 'screens' and im.height > im.width * 1.2:
                continue
            im.thumbnail((1400, 1400), Image.LANCZOS)
            im.save(os.path.join(od, k + '.jpg'), quality=92)
            n += 1
        print(name, n)


def export_commits():
    ts = subprocess.run(['git', '-C', core.REPO, 'log', '--format=%at', '--reverse'],
                        capture_output=True, text=True, check=True).stdout
    with open(os.path.join(OUT, 'commits.txt'), 'w', encoding='utf-8') as f:
        f.write(ts)
    print('commits', len(ts.split()))


def make_logo_alpha():
    # 白を透過へ（color-to-alpha）。淡い背景のムラは 6% 未満を切り捨てる
    im = np.asarray(Image.open(os.path.join(core.REPO, 'Doc', 'cv10logo01.png')).convert('RGB'), np.float32) / 255
    a = np.clip(((1 - im).max(2) - 0.06) / 0.94, 0, 1)
    rgb = np.where(a[..., None] > 1e-3, (im - (1 - a[..., None])) / np.maximum(a[..., None], 1e-3), 0)
    out = np.dstack([np.clip(rgb, 0, 1), a])
    Image.fromarray((out * 255).astype(np.uint8), 'RGBA').save(os.path.join(OUT, 'logo_alpha.png'))
    print('logo_alpha.png')


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    collect_images()
    export_commits()
    make_logo_alpha()
