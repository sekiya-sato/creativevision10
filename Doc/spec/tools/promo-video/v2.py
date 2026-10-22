# 動画2「十の利点」: 120BPM に完全同期したキネティック・タイポグラフィ
#  0- 6  イントロ「アパレルの基幹を、書き換える。」→「十」へズームスルー
#  6-26  十の利点（1 項目 = 1 小節 = 2 秒）
# 26-32  実画面モンタージュ（拍ごとカット）
# 32-40  アウトロとロゴ
import os, math, functools
import numpy as np
from PIL import Image, ImageDraw
from core import *

DURATION = 40.0
BPM = 120
BEAT = 60 / BPM
BAR = BEAT * 4

CREAM = hexc('#F5EFE4'); INK = hexc('#17122B'); PUR = hexc('#673AB7'); VIO = hexc('#8E6BFF')
WHITE = hexc('#FFFFFF'); LOGOBG = hexc('#F7F6FC'); TEAL = hexc('#2BA7B0'); ORANGE = hexc('#FF8A3D')

def rgb255(c):
    return tuple(int(round(v * 255)) for v in c)

MERITS = [
    ('壱', 'ライセンスコストを、', '大幅に削減。', 'Oracle・Windows Server・Biz/Browser への依存を減らす', 'cost'),
    ('弐', 'オープンな', '技術基盤へ。', '.NET 10・SQLite・MariaDB・GitHub', 'chips'),
    ('参', '将来にわたり、', '保守しやすい。', '特定ベンダー・旧来技術への依存から自由に', 'gear'),
    ('肆', 'HTTP/2 + gRPC で、', '通信が速い。', '高速で効率のよい通信が、全体の応答性を上げる', 'speed'),
    ('伍', '画面の操作性と', '表現力。', 'WPF + MVVM で、自由で速い画面', 'screen'),
    ('陸', '既存ユーザーが、', '移行しやすい。', 'CV.net の実績を踏まえた再設計', 'migrate'),
    ('漆', '業務機能が、', '充実。', 'アパレル販売管理の実務を、標準機能で', 'grid'),
    ('捌', '大規模運用の', '実績。', '大規模データ・多店舗運用・長年の導入実績', 'scale'),
    ('玖', '長く使える', '基幹システムへ。', '製品終了リスクを下げ、より広く、より長く', 'timeline'),
    ('拾', 'SaaS 展開と', '相性がよい。', '月額課金モデルとも親和性が高い構成', 'saas'),
]
# 背景・文字色
SCHEME = [(PUR, WHITE, CREAM), (CREAM, INK, PUR), (INK, WHITE, VIO), (WHITE, INK, PUR), (PUR, WHITE, CREAM),
          (CREAM, INK, TEAL), (INK, WHITE, ORANGE), (WHITE, INK, PUR), (TEAL, WHITE, INK), (PUR, WHITE, CREAM)]
M0 = 6.0

# ---------------------------------------------------------------- 描画ヘルパ
@functools.lru_cache(maxsize=1024)
def T(text, size, fam='noto', weight='Black', color=(255, 255, 255), tracking=0.0):
    return text_img(text, size, fam=fam, weight=weight, color=color, tracking=tracking)

def put(frame, im, x, y, scale=1.0, alpha=1.0, anchor='center', angle=0.0, clip=None, reveal=None):
    """im: PIL RGBA。reveal=('left'|'up', k) でマスク表示"""
    if alpha <= 0.002 or scale <= 0.01:
        return
    if abs(scale - 1) > 1e-3 or angle:
        arr = pil_scaled(im, scale, angle)
    else:
        arr = _arr_cache(im)
    if reveal:
        mode, k = reveal
        arr = arr.copy()
        h, w = arr.shape[:2]
        if mode == 'left':
            arr[:, int(w * clamp(k)):, 3] = 0
        elif mode == 'right':
            arr[:, :int(w * (1 - clamp(k))), 3] = 0
        elif mode == 'up':
            # 下から持ち上がる文字（行枠でクリップ）
            off = int(h * (1 - ease_out_expo(clamp(k))))
            sh = np.zeros_like(arr)
            if off < h:
                sh[off:] = arr[:h - off]
            arr = sh
    blit(frame, arr, x, y, alpha, anchor)

_ac = {}
def _arr_cache(im):
    k = id(im)
    if k not in _ac:
        _ac[k] = pil_to_rgba(im)
    return _ac[k]

def beat_hit(t, start, decay=0.12):
    """start 以降、拍頭ごとの減衰パルス"""
    if t < start:
        return 0.0
    ph = (t - start) % BEAT
    return math.exp(-ph / decay)

def solid(color):
    f = np.empty((H, W, 3), np.float32); f[:] = color
    return f

@functools.lru_cache(maxsize=64)
def gear_img(r, teeth, color, hole=0.35):
    s = int(r * 2 + 8)
    ss = 4
    im = Image.new('L', (s * ss, s * ss), 0)
    d = ImageDraw.Draw(im)
    c = s * ss / 2
    pts = []
    for i in range(teeth * 4):
        a = 2 * math.pi * i / (teeth * 4)
        rr = r * ss * (1.0 if (i % 4) in (1, 2) else 0.82)
        pts.append((c + math.cos(a) * rr, c + math.sin(a) * rr))
    d.polygon(pts, fill=255)
    hr = r * ss * hole
    d.ellipse((c - hr, c - hr, c + hr, c + hr), fill=0)
    im = im.resize((s, s), Image.LANCZOS)
    out = Image.new('RGBA', im.size, color + (0,))
    out.putalpha(im)
    return out

@functools.lru_cache(maxsize=64)
def chip_img(text, fg, bg, size=34):
    t = T(text, size, 'segoesl', None, fg, 0.02)
    w, h = t.width + size * 1.2, int(size * 1.9)
    im = Image.new('RGBA', (int(w) * 2, h * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, im.width - 1, im.height - 1), h, fill=bg + (255,))
    im = im.resize((int(w), h), Image.LANCZOS)
    im.alpha_composite(t, (int((w - t.width) / 2), int((h - t.height) / 2)))
    return im

@functools.lru_cache(maxsize=128)
def screen_img(name, w):
    im = Image.open(os.path.join(ASSETS, 'screens', name + '.jpg')).convert('RGBA')
    h = int(im.height * w / im.width)
    im = im.resize((w, h), Image.LANCZOS)
    # 角丸と影
    pad = 40
    out = Image.new('RGBA', (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    sh = Image.new('L', out.size, 0)
    ImageDraw.Draw(sh).rounded_rectangle((pad, pad + 14, pad + w, pad + h + 14), 18, fill=110)
    from PIL import ImageFilter
    sh = sh.filter(ImageFilter.GaussianBlur(18))
    shadow = Image.new('RGBA', out.size, (10, 5, 30, 0)); shadow.putalpha(sh)
    out.alpha_composite(shadow)
    m = Image.new('L', (w, h), 0); ImageDraw.Draw(m).rounded_rectangle((0, 0, w - 1, h - 1), 14, fill=255)
    im.putalpha(m)
    out.alpha_composite(im, (pad, pad))
    return out

# ---------------------------------------------------------------- 各モチーフ
def motif(frame, kind, lt, fg, ac, bgc):
    x0, y0 = 1000, 700
    fgc, acc = rgb255(fg), rgb255(ac)
    if kind == 'cost':
        full = 760
        k = ease_in_out_cubic(prog(lt, 0.5, 1.4))
        wv = full * (1 - 0.68 * k)
        fill_rect(frame, x0, y0, x0 + full, y0 + 40, fg, 0.12)
        fill_rect(frame, x0, y0, x0 + wv, y0 + 40, ac, 1.0 * prog(lt, 0.2, 0.35))
        put(frame, T('LICENSE COST', 24, 'segoesl', None, fgc, 0.3), x0, y0 - 28, alpha=prog(lt, 0.2, 0.4), anchor='left')
        if k > 0.05:
            put(frame, T('↓', 70, 'segoe', None, acc), x0 + wv + 50, y0 + 18, alpha=k)
    elif kind == 'chips':
        items = ['.NET 10', 'C# 14', 'SQLite', 'MariaDB', 'PostgreSQL', 'gRPC', 'GitHub', 'WPF']
        x, y = x0, y0 - 10
        for i, s in enumerate(items):
            st = 0.25 + i * BEAT / 4
            k = prog(lt, st, st + 0.3)
            im = chip_img(s, rgb255(bgc), acc, 30)
            if x + im.width > 1840:
                x = x0; y += 76
            put(frame, im, x + im.width / 2, y + 30 * (1 - ease_out_back(k)), scale=0.6 + 0.4 * ease_out_back(k), alpha=k)
            x += im.width + 14
    elif kind == 'gear':
        a = lt * 90
        k = ease_out_back(prog(lt, 0.2, 0.7))
        put(frame, gear_img(110, 12, acc), x0 + 140, y0 + 60, scale=k, angle=a)
        put(frame, gear_img(70, 8, fgc), x0 + 140 + 170, y0 - 30, scale=k, angle=-a * 12 / 8 + 11)
        put(frame, gear_img(52, 6, acc), x0 + 140 + 300, y0 + 90, scale=k, angle=a * 12 / 6)
    elif kind == 'speed':
        rng = np.random.default_rng(3)
        for i in range(26):
            yy = y0 - 60 + i * 9
            sp = rng.uniform(1800, 3600)
            ln = rng.uniform(80, 360)
            xx = 900 + ((lt - 0.2) * sp + rng.uniform(0, 900)) % 1300
            if lt > 0.2:
                fill_rect(frame, xx - ln, yy, xx, yy + 2 + (i % 3 == 0) * 2, ac if i % 4 else fg, 0.85)
        put(frame, T('HTTP/2  ×  gRPC', 44, 'segoesl', None, fgc, 0.12), x0, y0 + 230, alpha=prog(lt, 0.4, 0.6), anchor='left', reveal=('left', prog(lt, 0.4, 0.8)))
    elif kind == 'screen':
        k = ease_out_expo(prog(lt, 0.2, 0.9))
        im = screen_img('ReservationList', 760)
        put(frame, im, 1430, 760 + 120 * (1 - k), scale=0.92 + 0.08 * k, alpha=prog(lt, 0.2, 0.4))
    elif kind == 'migrate':
        k = ease_out_expo(prog(lt, 0.3, 0.9))
        put(frame, T('CV.net', 64, 'segoel', None, fgc, 0.02), x0, y0 + 20, alpha=prog(lt, 0.2, 0.4), anchor='left')
        fill_rect(frame, x0 + 230, y0 + 22, x0 + 230 + 200 * k, y0 + 26, ac)
        if k > 0.9:
            put(frame, T('→', 56, 'segoel', None, acc), x0 + 445, y0 + 18)
        put(frame, T('CV10', 64, 'segoeb', None, acc, 0.02), x0 + 490, y0 + 20, alpha=prog(lt, 0.7, 0.9), anchor='left', reveal=('left', prog(lt, 0.7, 1.0)))
    elif kind == 'grid':
        words = ['発注', '受注', '仕入', '売上', '配分', '在庫', '予算', '分析', '月次', '顧客', '店舗', '物流', 'HHT', '帳票', 'マスタ', '権限']
        for i, wd in enumerate(words):
            st = 0.2 + i * BEAT / 4 * 0.8
            k = prog(lt, st, st + 0.2)
            cx = x0 + (i % 8) * 102 + 45; cy = y0 - 20 + (i // 8) * 100
            hot = i % 3 == 0
            fill_rect(frame, cx - 46, cy - 40, cx + 46, cy + 40, ac if hot else fg, (0.95 if hot else 0.1) * k)
            put(frame, T(wd, 26, 'noto', 'Bold', rgb255(bgc) if hot else fgc), cx, cy, alpha=k, scale=0.7 + 0.3 * ease_out_back(k))
    elif kind == 'scale':
        k = ease_out_expo(prog(lt, 0.2, 1.2))
        put(frame, T(f'{int(200 * k)}', 170, 'segoeb', None, acc, -0.01), x0 + 250, y0 + 40, anchor='right', alpha=prog(lt, 0.15, 0.3))
        put(frame, T('社', 70, 'noto', 'Black', fgc), x0 + 270, y0 + 80, anchor='left', alpha=prog(lt, 0.4, 0.6))
        put(frame, T('CV.net 延べ導入実績', 26, 'noto', 'Medium', fgc), x0 + 400, y0 + 80, anchor='left', alpha=prog(lt, 0.6, 0.8) * 0.8)
    elif kind == 'timeline':
        k = ease_in_out_cubic(prog(lt, 0.2, 1.3))
        L = 780
        fill_rect(frame, x0, y0 + 20, x0 + L * k, y0 + 23, fg)
        for frac, lab in ((0.0, '2005'), (0.45, '2027'), (1.0, '∞')):
            if k >= frac:
                kk = prog(lt, 0.2 + 1.1 * frac, 0.4 + 1.1 * frac)
                put(frame, _dot(acc), x0 + L * frac, y0 + 21, scale=ease_out_back(kk))
                put(frame, T(lab, 38 if lab != '∞' else 64, 'segoel', None, fgc), x0 + L * frac, y0 - 30, alpha=kk)
    elif kind == 'saas':
        for i in range(12):
            st = 0.2 + i * BEAT / 4 * 0.7
            k = ease_out_back(prog(lt, st, st + 0.25))
            hh = (60 + i * 16) * k
            fill_rect(frame, x0 + i * 62, y0 + 120 - hh, x0 + i * 62 + 40, y0 + 120, ac if i == 11 else fg, 0.9 if i == 11 else 0.25)
        put(frame, T('MONTHLY', 24, 'segoesl', None, fgc, 0.3), x0, y0 + 160, anchor='left', alpha=prog(lt, 0.3, 0.5))

@functools.lru_cache(maxsize=16)
def _dot(c):
    a = circle_rgba(13, tuple(v / 255 for v in c))
    return Image.fromarray((a * 255).astype(np.uint8), 'RGBA')

def render_merit(i, t):
    lt = t - (M0 + i * BAR)
    bgc, fg, ac = SCHEME[i]
    f = solid(bgc)
    num, h1, h2, sub, kind = MERITS[i]
    fgc, acc = rgb255(fg), rgb255(ac)
    # 背景の巨大な透かし番号
    put(f, T(f'{i + 1:02d}', 520, 'segoeb', None, fgc, -0.02), 1890, 980 - 30 * lt, alpha=0.05, anchor='right')
    # 漢数字（入り方を 4 種で巡回）
    style = i % 4
    nx, ny = 470, 530
    big = T(num, 560, 'mincho', None, acc)
    k = prog(lt, 0.0, 0.45)
    if style == 0:
        put(f, big, nx, ny, reveal=('up', k))
    elif style == 1:
        s = 1 + 1.6 * (1 - ease_out_expo(k))
        put(f, big, nx, ny, scale=s, alpha=prog(lt, 0, 0.12))
    elif style == 2:
        put(f, big, nx, ny, angle=-25 * (1 - ease_out_back(k)), scale=0.6 + 0.4 * ease_out_back(k), alpha=prog(lt, 0, 0.1))
    else:
        put(f, big, nx - 140 * (1 - ease_out_expo(k)), ny, reveal=('left', ease_out_expo(k)))
    # 番号の小見出し
    put(f, T(f'MERIT  {i + 1:02d} / 10', 22, 'segoesl', None, fgc, 0.4), 1000, 250, anchor='left', alpha=prog(lt, 0.1, 0.25) * 0.8,
        reveal=('left', prog(lt, 0.1, 0.4)))
    fill_rect(f, 1000, 282, 1000 + 120 * ease_out_expo(prog(lt, 0.1, 0.5)), 286, ac)
    # 見出し 2 行（拍に合わせて 1 行ずつ）
    put(f, T(h1, 70, 'noto', 'Bold', fgc, 0.02), 1000, 360, anchor='left', reveal=('up', prog(lt, BEAT * 0.5, BEAT * 0.5 + 0.35)))
    put(f, T(h2, 92, 'noto', 'Black', acc if i not in (0, 4, 9) else fgc, 0.02), 1000, 465, anchor='left', reveal=('up', prog(lt, BEAT * 1.0, BEAT * 1.0 + 0.35)))
    put(f, T(sub, 28, 'noto', 'Medium', fgc, 0.05), 1000, 560, anchor='left', alpha=0.75 * prog(lt, BEAT * 1.5, BEAT * 1.5 + 0.3))
    motif(f, kind, lt - BEAT * 0.5, fg, ac, bgc)
    # 拍のインジケータ
    for b in range(4):
        on = lt >= b * BEAT
        fill_rect(f, 100 + b * 28, 1000, 100 + b * 28 + 18, 1004, fg, 0.9 if on else 0.2)
    return f

def wipe_mask(style, k):
    """0→1 で新画面が覆う割合のマスク"""
    yy, xx = grid()
    k = ease_in_out_cubic(clamp(k))
    if style == 0:  # 斜め
        v = (xx + yy * 0.5) / (W + H * 0.5)
        return np.clip((k * 1.3 - v) * 20, 0, 1)
    if style == 1:  # 円
        d = np.sqrt((xx - W / 2) ** 2 + (yy - H / 2) ** 2) / (math.hypot(W, H) / 2)
        return np.clip((k * 1.1 - d) * 40, 0, 1)
    if style == 2:  # ブラインド
        strip = (yy // (H / 8)).astype(np.int32)
        kk = np.clip(k * 1.6 - strip * 0.08, 0, 1)
        return ((yy % (H / 8)) / (H / 8) < kk).astype(np.float32)
    v = xx / W  # 左から
    return np.clip((k * 1.1 - v) * 30, 0, 1)

TRANS = 0.32

# ---------------------------------------------------------------- イントロ・モンタージュ・アウトロ
def render_intro(t):
    f = solid(CREAM)
    ic = rgb255(INK)
    # 0-2: 罫線と小さな語
    for i in range(5):
        k = ease_in_out_expo(prog(t, 0.1 + i * 0.12, 1.2 + i * 0.12))
        y = 180 + i * 180
        fill_rect(f, 0, y, W * k, y + 1, INK, 0.25)
    words = ['APPAREL', 'ERP', 'OPEN SOURCE', '.NET 10']
    for i, w in enumerate(words):
        if i * BEAT <= t < 2.0:
            a = beat_hit(t, i * BEAT, 0.3) if t < (i + 1) * BEAT else 0.15
            put(f, T(w, 30, 'segoesl', None, ic, 0.5), 160 + i * 420, 120, alpha=max(a, 0.15), anchor='left')
    # 2-4: アパレルの基幹を、
    parts = ['アパレルの', '基幹を、']
    if 2.0 <= t < 5.0:
        put(f, T(parts[0], 120, 'noto', 'Black', ic), 160, 420, anchor='left', reveal=('up', prog(t, 2.0, 2.3)))
        put(f, T(parts[1], 120, 'noto', 'Black', ic), 800, 420, anchor='left', reveal=('up', prog(t, 2.5, 2.8)))
    if 3.0 <= t < 5.0:
        put(f, T('Creative Vision 10', 34, 'segoel', None, rgb255(PUR), 0.3), 165, 300, anchor='left', reveal=('left', prog(t, 3.0, 3.5)))
    # 4: 書き換える。（スラム）
    if 4.0 <= t < 5.0:
        k = prog(t, 4.0, 4.25)
        put(f, T('書き換える。', 230, 'noto', 'Black', rgb255(PUR), -0.02), 160, 680, anchor='left', scale=1 + 0.5 * (1 - ease_out_expo(k)), alpha=prog(t, 4.0, 4.06))
    # 5-6: 十 のズームスルー
    if t >= 5.0:
        f = solid(CREAM)
        k = prog(t, 5.0, 6.0)
        put(f, T('十の利点。', 70, 'noto', 'Black', ic, 0.3), W / 2, 880, alpha=prog(t, 5.0, 5.15) * (1 - prog(t, 5.5, 5.6)))
        s = 1.0 + 60 * ease_in_expo(prog(t, 5.45, 6.0))
        put(f, T('十', 520, 'mincho', None, rgb255(PUR)), W / 2 + 8 * (s - 1), H / 2 - 30, scale=min(s, 40))
        if t > 5.85:
            f = f * (1 - prog(t, 5.85, 6.0)) + solid(PUR) * prog(t, 5.85, 6.0)
    return f

MONTAGE = [('HachuHaibunInputArrived', '発注'), ('JuchuInputDetail', '受注'), ('ShiireInputDetail', '仕入'), ('HaibunCommitShohin', '配分'),
           ('ReservationList', '取置'), ('L04_Correcting', '物流'), ('Result_RxF', '在庫'), ('DailyShopBudgetQuery', '予算'),
           ('Customers', '顧客'), ('DeliveryScheduleInquiry', '納期'), ('StagnationList', '滞留'), ('permission_std', '権限'),
           ('HenpinInputSearched', '返品'), ('ShortageResult', '欠品'), ('NouhinYoteiTable', '納品'), ('FlowCommit', '確定')]
MT0 = 26.0
def render_montage(t):
    lt = t - MT0
    # 前半 8 拍は 1 拍、後半は 8 分で切替
    if lt < 8 * BEAT:
        idx = int(lt / BEAT); st = idx * BEAT; dur = BEAT
    else:
        idx = 8 + int((lt - 8 * BEAT) / (BEAT / 2)); st = 8 * BEAT + (idx - 8) * BEAT / 2; dur = BEAT / 2
    idx = min(idx, len(MONTAGE) - 1)
    name, word = MONTAGE[idx]
    bg = [INK, PUR, CREAM, TEAL][idx % 4]
    f = solid(bg)
    k = (lt - st) / dur
    im = screen_img(name, 1180)
    put(f, im, W - 680 + 40 * (0.5 - k), H / 2 + 30, scale=1.0 + 0.05 * k)
    fgc = rgb255(WHITE if idx % 4 != 2 else INK)
    put(f, T(word, 230, 'noto', 'Black', fgc), 330, H / 2 + 20, scale=1 + 0.25 * (1 - ease_out_expo(clamp(k * 3))))
    put(f, T(f'{idx + 1:02d}', 26, 'segoesl', None, fgc, 0.4), 120, 100, anchor='left')
    put(f, T('REAL SCREENS — NOT MOCKUPS', 22, 'segoesl', None, fgc, 0.4), W - 120, 100, anchor='right', alpha=0.8)
    return f

def render_outro(t):
    lt = t - 32.0
    if lt < 4 * BEAT * 2 - BEAT * 0:
        pass
    if lt < 4.0:
        f = solid(INK)
        wc = rgb255(WHITE)
        put(f, T('オープンソースで、', 110, 'noto', 'Black', wc), 160, 430, anchor='left', reveal=('up', prog(lt, 0.0, 0.4)))
        k = prog(lt, 2 * BEAT * 2, 2 * BEAT * 2 + 0.25)
        put(f, T('本格基幹を。', 190, 'noto', 'Black', rgb255(VIO)), 160, 640, anchor='left', scale=1 + 0.4 * (1 - ease_out_expo(k)), alpha=prog(lt, 2.0, 2.05))
        return f
    f = solid(LOGOBG)
    k = ease_out_expo(prog(lt, 4.0, 5.0))
    logo = load_image(os.path.join(ASSETS, 'logo_alpha.png'))
    put(f, logo, W / 2, 420 - 30 * (1 - k), scale=0.72 + 0.06 * (1 - k), alpha=prog(lt, 4.0, 4.3))
    put(f, T('Creative Vision 10', 74, 'segoel', None, rgb255(INK), 0.04), W / 2, 700, alpha=prog(lt, 4.3, 4.8), reveal=('left', ease_out_expo(prog(lt, 4.3, 5.0))))
    put(f, T('アパレル販売管理の、フル・オープンソースパッケージ', 30, 'noto', 'Medium', rgb255(PUR), 0.12), W / 2, 785, alpha=prog(lt, 4.8, 5.3))
    put(f, chip_img('2027.01  RELEASE', (255, 255, 255), rgb255(PUR), 28), W / 2, 870, alpha=prog(lt, 5.3, 5.6), scale=0.8 + 0.2 * ease_out_back(prog(lt, 5.3, 5.7)))
    put(f, T('github.com/sekiya-sato/creativevision10', 24, 'segoel', None, (110, 100, 140), 0.08), W / 2, 950, alpha=prog(lt, 5.6, 6.0))
    return f

def render(t):
    if t < M0:
        f = render_intro(t)
    elif t < MT0:
        i = int((t - M0) / BAR)
        lt = t - (M0 + i * BAR)
        f = render_merit(i, t)
        if lt < TRANS and i > 0:
            m = wipe_mask((i - 1) % 4, lt / TRANS)[..., None]
            f = render_merit(i - 1, t) * (1 - m) + f * m
    elif t < 32.0:
        f = render_montage(t)
        if t - MT0 < TRANS:
            m = wipe_mask(1, (t - MT0) / TRANS)[..., None]
            f = render_merit(9, t) * (1 - m) + f * m
    else:
        f = render_outro(t)
    # 拍頭のズームパンチ
    if 2.0 <= t < 36.0:
        p = beat_hit(t, 2.0, 0.08)
        bar_down = ((t - 2.0) % BAR) < BEAT
        z = 1 + (0.018 if bar_down else 0.007) * p
        if z > 1.001:
            f = zoom(f, z)
    vignette(f, 0.18)
    grain(f, t, 0.012)
    return f * (1 - prog(t, 39.3, 40.0))

def zoom(f, z):
    h, w = f.shape[:2]
    ch, cw = int(h / z), int(w / z)
    y0, x0 = (h - ch) // 2, (w - cw) // 2
    crop = f[y0:y0 + ch, x0:x0 + cw]
    im = [Image.fromarray(crop[..., c], 'F').resize((w, h), Image.BILINEAR) for c in range(3)]
    return np.stack([np.asarray(i, np.float32) for i in im], -1)

# ---------------------------------------------------------------- 音（120BPM ハウス）
def audio():
    D = DURATION
    m = Mix(D)
    # 和音: Fm9 - Dbmaj7 - Ab - Eb（各 1 小節）
    chords = [[53, 56, 60, 63, 67], [49, 53, 56, 60, 65], [56, 60, 63, 68], [51, 55, 58, 63, 67]]
    roots = [41, 37, 44, 39]
    # イントロ: ハット + ライザー
    for i in range(8):
        m.add(i * BEAT / 2, hat(seed=i), 0.25 + 0.03 * i, pan=0.3)
    for i in range(4):
        m.add(i * BEAT, tick(1800, 0.05), 0.15)
    m.add(0.0, riser(2.0, 300, 7000, 2), 0.25)
    m.add(5.0, riser(1.0, 500, 12000, 4), 0.45)
    m.add(4.0, sub_boom(2.0, 90, 35, 0.6), 0.6)
    m.add(4.0, clap(0.4, 8), 0.4)
    # メイン 2.0 - 36.0
    for bar in range(17):
        tb = 2.0 + bar * BAR
        ch = chords[bar % 4]; rt = roots[bar % 4]
        drop = 5.0 <= tb < 6.0
        for b in range(4):
            tt = tb + b * BEAT
            if tt >= 36.0:
                break
            if 5.0 <= tt < 6.0:
                continue
            m.add(tt, kick(0.45, 160, 45, 0.28), 0.8)
            m.add(tt + BEAT / 2, hat(0.2, True, seed=b), 0.16, pan=0.25)
            m.add(tt + BEAT / 4, hat(seed=b + 9), 0.07, pan=-0.3)
            m.add(tt + BEAT * 3 / 4, hat(seed=b + 19), 0.07, pan=-0.3)
            if b % 2 == 1:
                m.add(tt, clap(), 0.45)
            # ベース（オフビート）
            bn = rt if b != 3 else rt + 7
            bs = lowpass(osc(midi(bn), BEAT * 0.45, 'saw') + osc(midi(bn - 12), BEAT * 0.45, 'sine') * 0.8, 700)
            m.add(tt + BEAT / 2, bs * env_adsr(len(bs), 0.003, 0.08, 0.6, 0.04), 0.3)
        if tb < 36.0 and not drop:
            # コードスタブ（シンコペーション）
            for off in (0.0, BEAT * 1.5, BEAT * 2.5):
                st = pad([n + 12 for n in ch], BEAT * 0.5, cutoff=3200, attack=0.005, release=0.2, voices=2, seed=bar)
                m.add(tb + off, st, 0.11)
            m.add(tb, pad(ch, BAR, cutoff=1200, attack=0.3, release=0.4, seed=bar + 50), 0.08)
    # 項目切替のアクセント
    for i in range(10):
        ts = M0 + i * BAR
        m.add(ts - 0.3, whoosh(0.35, 800, 9000, 60 + i), 0.25, pan=(-0.5 if i % 2 else 0.5))
        m.add(ts, sub_boom(0.8, 100, 45, 0.25), 0.35)
        # 漢数字の音（鐘っぽい）
        bell = (osc(midi(84 + [0, 3, 7, 10, 12][i % 5]), 1.2, 'sine') + osc(midi(84 + [0, 3, 7, 10, 12][i % 5]) * 2.76, 1.2, 'sine') * 0.3) * env_exp(int(1.2 * SR), 0.35)
        m.add(ts, bell, 0.08)
    # モンタージュ: カット音
    for k in range(16):
        ts = MT0 + (k * BEAT if k < 8 else 8 * BEAT + (k - 8) * BEAT / 2)
        m.add(ts, tick(2600 + (k % 4) * 300, 0.04), 0.12)
    m.add(MT0 - 0.4, whoosh(0.45, 600, 10000, 88), 0.3)
    m.add(30.0, riser(2.0, 400, 12000, 90), 0.4)
    # アウトロ
    m.add(32.0, sub_boom(2.5, 80, 32, 0.8), 0.6)
    m.add(34.0, sub_boom(2.5, 90, 32, 0.9), 0.7)
    m.add(34.0, clap(0.5, 3), 0.5)
    m.add(35.0, riser(1.0, 800, 14000, 92), 0.35)
    m.add(36.0, sub_boom(4.0, 70, 28, 1.6), 0.9)
    m.add(36.0, pad([53, 60, 65, 68, 72, 77], 4.0, cutoff=3000, attack=0.01, release=2.5, seed=77), 0.3)
    bell = (osc(midi(89), 3.5, 'sine') + osc(midi(89) * 2.76, 3.5, 'sine') * 0.25) * env_exp(int(3.5 * SR), 0.9)
    m.add(36.0, bell, 0.15)
    x = m.master(D + 1)
    x = reverb(x, 1.6, 0.18)
    x = x[:int(D * SR)]
    pk = np.max(np.abs(x)) + 1e-9
    x = np.tanh(x / pk * 1.4) / np.tanh(1.4) * 0.92
    fo = int(0.8 * SR)
    x[-fo:] *= np.linspace(1, 0, fo)[:, None]
    return x
