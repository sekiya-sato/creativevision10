# 動画3「REAL SCREENS」: 実画面・実帳票を 3D 空間に配置したフライスルー
#  0.0- 7.2  実画面の壁（110 枚）を斜めに滑空
#  7.2-24.0  業務フロー 6 画面を順に巡る（床面反射つき）
# 24.0-33.6  帳票の螺旋
# 33.6-38.4  4 種 DB
# 38.4-48.0  実画面サムネイルでロゴのフォトモザイク
import os, glob, math, functools
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from core import *

DURATION = 48.0
BEAT = 0.6
BAR = 2.4
NAVY = hexc('#0B0A1A'); PUR = hexc('#7C4DFF'); TEAL = hexc('#5FD6D8'); WARM = hexc('#FFE9C7')
RNG = np.random.default_rng(7)

SCREENS = sorted(glob.glob(os.path.join(ASSETS, 'screens', '*.jpg')))
FORMS = sorted(glob.glob(os.path.join(ASSETS, 'forms', '*.jpg')))

@functools.lru_cache(maxsize=256)
def card(path, w):
    im = Image.open(path).convert('RGBA')
    h = int(im.height * w / im.width)
    im = im.resize((w, h), Image.LANCZOS)
    m = Image.new('L', (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, w - 1, h - 1), max(4, w // 60), fill=255)
    im.putalpha(m)
    return im

def card_aspect(path):
    im = card(path, 64)
    return im.height / im.width

def draw_card(frame, im, center, w, h, R, cam, alpha=1.0, fog=(25, 90), mode='over', reflect_y=None, tint=None):
    """3D 空間の板（中心・幅・高さ・回転行列）を射影して描く"""
    c = np.asarray(center, np.float32)
    hw, hh = w / 2, h / 2
    loc = np.array([[-hw, hh, 0], [hw, hh, 0], [hw, -hh, 0], [-hw, -hh, 0]], np.float32)
    P = loc @ np.asarray(R, np.float32).T + c
    x, y, z = cam.project(P)
    if np.any(~np.isfinite(x)) or np.any(z < 0.5):
        return
    dist = float(np.mean(z))
    fa = 1 - prog(dist, fog[0], fog[1])
    a = alpha * fa
    if a < 0.01:
        return
    # 画面上の大きさに合わせて元画像の解像度を選ぶ
    span = max(math.hypot(x[1] - x[0], y[1] - y[0]), 2)
    res = int(min(1400, max(64, 2 ** math.ceil(math.log2(span)))))
    src = im if isinstance(im, Image.Image) else None
    if src is None:
        src = card(im, res)
    r = quad_perspective(src, list(zip(x, y)))
    if r is None:
        return
    arr, x0, y0 = r
    if tint is not None:
        arr = arr.copy(); arr[..., :3] *= tint
    blit(frame, arr, x0, y0, a, 'topleft', mode)
    if reflect_y is not None:
        Pm = P.copy(); Pm[:, 1] = 2 * reflect_y - Pm[:, 1]
        xm, ym, zm = cam.project(Pm)
        if np.all(np.isfinite(xm)) and np.all(zm > 0.5):
            r2 = quad_perspective(src.transpose(Image.FLIP_TOP_BOTTOM), [(xm[3], ym[3]), (xm[2], ym[2]), (xm[1], ym[1]), (xm[0], ym[0])][::-1][::-1] if False else list(zip(xm, ym)))
            if r2 is not None:
                arr2, rx, ry = r2
                hgt = arr2.shape[0]
                g = np.linspace(0.22, 0.0, hgt, dtype=np.float32)[:, None]
                arr2 = arr2.copy(); arr2[..., 3] *= g
                blit(frame, arr2, rx, ry, a, 'topleft')

def background(t):
    f = new_frame(NAVY)
    yy, xx = grid()
    g = np.exp(-(((xx - W * 0.5) / (W * 0.6)) ** 2 + ((yy - H * 0.35) / (H * 0.7)) ** 2))
    f += g[..., None] * np.asarray((0.07, 0.04, 0.16), np.float32)
    return f

def floor_grid(frame, cam, y=-3.2, alpha=0.5, t=0.0):
    pts_x, pts_y = [], []
    for k in range(-30, 31, 2):
        P = np.array([[k, y, z] for z in np.linspace(-10, 120, 300)], np.float32)
        x, yv, z = cam.project(P)
        ok = np.isfinite(x)
        pts_x.append(x[ok]); pts_y.append(yv[ok])
    for z0 in range(-10, 121, 4):
        P = np.array([[xx, y, z0] for xx in np.linspace(-30, 30, 300)], np.float32)
        x, yv, z = cam.project(P)
        ok = np.isfinite(x)
        pts_x.append(x[ok]); pts_y.append(yv[ok])
    layer = np.zeros((H, W, 3), np.float32)
    splat_fast(layer, np.concatenate(pts_x), np.concatenate(pts_y), PUR * 0.5, None)
    frame += blur(layer, 0.6) * alpha

def text(frame, s, x, y, size, fam='noto', weight='Light', color=(255, 255, 255), alpha=1.0, tracking=0.1, anchor='left'):
    if alpha > 0.01:
        blit(frame, text_arr(s, size, fam=fam, weight=weight, color=color, tracking=tracking), x, y, alpha, anchor)

# ---------------------------------------------------------------- 1. 実画面の壁
WALL_COLS, WALL_ROWS = 16, 6
@functools.lru_cache(maxsize=1)
def wall_layout():
    items = []
    k = 0
    for r in range(WALL_ROWS):
        for c in range(WALL_COLS):
            p = SCREENS[(k * 37) % len(SCREENS)]; k += 1
            items.append((p, c, r, RNG.uniform(0, 1)))
    return items

def scene_wall(t, f):
    cam_x = lerp(-14, 10, ease_in_out_cubic(prog(t, 0, 7.6)))
    cam = Camera((cam_x, 1.2, -9.0 + 2.5 * prog(t, 0, 7.6)), fov=48).look_at((cam_x + 6, 0.2, 6))
    cam.R = (rot_z(-0.06) @ cam.R).astype(np.float32)
    Rw = rot_y(-0.0)
    cw, ch = 3.2, 2.1
    for p, c, r, ph in wall_layout():
        cx = (c - WALL_COLS / 2) * (cw + 0.25)
        cy = (WALL_ROWS / 2 - r - 0.5) * (ch + 0.25)
        cz = 6.0 + 0.0 * c
        appear = prog(t, 0.2 + ph * 1.8, 0.8 + ph * 1.8)
        if appear <= 0:
            continue
        zoff = (1 - ease_out_expo(appear)) * -4
        hl = 0.8 + 0.2 * math.exp(-((t * 1.7 + ph * 10) % 6.0))
        draw_card(f, p, (cx, cy, cz + zoff), cw, cw * card_aspect(p), Rw, cam, alpha=appear * 0.95, fog=(8, 40), tint=hl)
    a = prog(t, 1.2, 2.2) * (1 - prog(t, 6.4, 7.2))
    # 文字の背後に暗幕
    if a > 0:
        yy, xx = grid()
        f *= (1 - 0.65 * a * np.clip(1 - xx / 1100, 0, 1) ** 1.5)[..., None]
    text(f, 'REAL SCREENS', 150, 410, 26, 'segoesl', None, (170, 200, 255), a, 0.5)
    text(f, '196の業務画面。', 146, 500, 96, 'noto', 'Bold', (255, 255, 255), a, 0.04)
    text(f, 'すべて、実際に動いている画面です。', 150, 600, 34, 'noto', 'Light', (230, 230, 255), a * prog(t, 2.0, 2.8), 0.12)

# ---------------------------------------------------------------- 2. 業務フロー
FLOW = [('HachuHaibunInputArrived', '発注・入荷配分', '発注から入荷、店舗への配分まで一気通貫'),
        ('ShiireInputDetail', '仕入入力', '色・サイズ単位の明細、税・金額を自動計算'),
        ('HaibunCommitShohin', '配分確定', '出荷売上・移動伝票を同一トランザクションで生成'),
        ('ReservationList', '取置', '直営店の在庫を顧客向けに確保、期限で自動解除'),
        ('L04_Correcting', '物流連携', 'WMS と出荷指示・入荷予定・在庫を連携'),
        ('DailyShopBudgetQuery', '売上・予算分析', '店別・日別の予算実績を瞬時に')]
F0, FSTEP = 7.2, 2.8

def flow_pose(i):
    """i 番目の画面の位置と向き"""
    z = i * 9.0
    x = math.sin(i * 1.1) * 4.0
    yaw = math.sin(i * 1.1 + 0.6) * 0.35
    return np.array([x, 0.0, z], np.float32), yaw

def flow_cam(t):
    s = (t - F0) / FSTEP
    i = int(clamp(math.floor(s), 0, len(FLOW) - 1))
    u = s - i
    def cpose(j):
        c, yaw = flow_pose(j)
        R = rot_y(yaw)
        n = R @ np.array([0, 0, -1], np.float32)
        return c + n * 7.4 + np.array([0.8, 0.6, 0], np.float32), c + np.array([0.0, -0.1, 0], np.float32)
    if t < F0:
        return cpose(0)
    p0, t0 = cpose(i)
    if i + 1 < len(FLOW) and u > 0.62:
        k = ease_in_out_cubic(prog(u, 0.62, 1.0))
        p1, t1 = cpose(i + 1)
        p = p0 * (1 - k) + p1 * k + np.array([0, 1.2, -2.5], np.float32) * math.sin(math.pi * k)
        tg = t0 * (1 - k) + t1 * k
        return p, tg
    drift = np.array([0.25, 0.05, 0.6], np.float32) * (u - 0.3)
    return p0 + drift, t0

def scene_flow(t, f):
    pos, tgt = flow_cam(t)
    # 壁からの移行: 冒頭 0.6 秒は前方から寄る
    k_in = ease_out_expo(prog(t, F0, F0 + 1.0))
    pos = pos + np.array([0, 2.0, -10.0], np.float32) * (1 - k_in)
    cam = Camera(pos, fov=46).look_at(tgt)
    cam.cx = W / 2 + 300
    floor_grid(f, cam, -2.6, 0.35 * k_in)
    # 背景の遠景画面
    for k in range(18):
        p = SCREENS[(k * 13 + 5) % len(SCREENS)]
        cx = (k % 6 - 2.5) * 9 + 3; cz = 70 + (k // 6) * 8; cy = 2 + (k // 6) * 4.5
        draw_card(f, p, (cx, cy, cz), 7, 7 * card_aspect(p), rot_y(0), cam, alpha=0.35, fog=(40, 110))
    # 経路の光の線
    pts = []
    for i in range(len(FLOW) - 1):
        a, _ = flow_pose(i); b, _ = flow_pose(i + 1)
        for s in np.linspace(0, 1, 120):
            p = a * (1 - s) + b * s
            p = p + np.array([0, -2.6 + 0.02, 0], np.float32)
            pts.append(p)
    pts = np.array(pts, np.float32)
    x, y, z = cam.project(pts)
    lay = np.zeros((H, W, 3), np.float32)
    splat_fast(lay, x, y, TEAL, np.full(len(x), 1.2, np.float32))
    # パケット
    for q in range(5):
        ph = ((t * 0.18 + q / 5) % 1.0) * (len(FLOW) - 1)
        j = int(ph); s = ph - j
        a, _ = flow_pose(j); b, _ = flow_pose(j + 1)
        tr = np.array([a * (1 - ss) + b * ss for ss in np.clip(np.linspace(s - 0.06, s, 20), 0, 1)], np.float32) + np.array([0, -2.58, 0], np.float32)
        px, py, pz = cam.project(tr)
        splat_fast(lay, px, py, WARM, np.linspace(0, 6, 20).astype(np.float32))
    f += glow_layer(lay, 6, 2.0)
    # 画面（奥から）
    order = sorted(range(len(FLOW)), key=lambda i: -np.linalg.norm(flow_pose(i)[0] - pos))
    s_now = (t - F0) / FSTEP
    for i in order:
        c, yaw = flow_pose(i)
        p = os.path.join(ASSETS, 'screens', FLOW[i][0] + '.jpg')
        w = 6.0
        hgt = w * card_aspect(p)
        focus = clamp(1 - abs(s_now - i - 0.3) * 1.2)
        tint = 0.5 + 0.42 * focus
        draw_card(f, p, (c[0], c[1] + hgt / 2 - 2.6 + 0.15, c[2]), w, hgt, rot_y(yaw), cam, alpha=1.0, fog=(14, 60),
                  reflect_y=-2.6, tint=tint)
    # 文字
    i = int(clamp(math.floor(s_now), 0, len(FLOW) - 1)); u = s_now - i
    a = prog(u, 0.08, 0.22) * (1 - prog(u, 0.6, 0.72)) * prog(t, F0 + 0.4, F0 + 0.8)
    if i == len(FLOW) - 1:
        a = prog(u, 0.08, 0.22) * (1 - prog(t, 23.4, 24.0))
    name, ja, desc = FLOW[i]
    text(f, f'STEP {i + 1:02d} / {len(FLOW):02d}', 130, 150, 22, 'segoesl', None, (170, 200, 255), a, 0.45)
    text(f, ja, 126, 225, 70, 'noto', 'Bold', (255, 255, 255), a, 0.04)
    text(f, desc, 130, 300, 26, 'noto', 'Light', (225, 225, 245), a * 0.9, 0.08)
    fill_rect(f, 130, 340, 130 + 90 * ease_out_expo(prog(u, 0.1, 0.4)), 343, TEAL, a)
    # 進捗ドット
    for j in range(len(FLOW)):
        fill_rect(f, 130 + j * 34, H - 120, 130 + j * 34 + 24, H - 116, TEAL if j <= i else (1, 1, 1), (0.95 if j <= i else 0.25) * prog(t, F0 + 0.4, F0 + 0.8) * (1 - prog(t, 23.4, 24.0)))

# ---------------------------------------------------------------- 3. 帳票の螺旋
FS0 = 24.0
def scene_forms(t, f):
    lt = t - FS0
    ang = 0.35 * lt + 0.8
    cam_pos = np.array([math.sin(ang) * 17, 2 + lt * 1.1, -math.cos(ang) * 17], np.float32)
    cam = Camera(cam_pos, fov=50).look_at((0, 1 + lt * 1.0, 0))
    cam.cx = W / 2 + 330
    n = 46
    items = []
    for k in range(n):
        a = k * 0.42
        y = -6 + k * 0.42
        c = np.array([math.cos(a) * 6.5, y, math.sin(a) * 6.5], np.float32)
        items.append((k, a, c))
    items.sort(key=lambda it: -np.linalg.norm(it[2] - cam_pos))
    for k, a, c in items:
        p = FORMS[(k * 7) % len(FORMS)]
        st = k * 0.05
        ap = ease_out_expo(prog(lt, st, st + 0.9))
        if ap <= 0:
            continue
        R = rot_y(-a + math.pi / 2) @ rot_x(-0.1)
        cc = c * (0.4 + 0.6 * ap) + np.array([0, -4 * (1 - ap), 0], np.float32)
        draw_card(f, p, cc, 2.4, 2.4 * card_aspect(p), R, cam, alpha=ap, fog=(10, 45), tint=0.88)
    a = prog(lt, 0.8, 1.6) * (1 - prog(t, 33.0, 33.6))
    yy, xx = grid()
    if a > 0:
        f *= (1 - 0.6 * a * np.clip(1 - xx / 1000, 0, 1) ** 1.5)[..., None]
    text(f, 'PRINT FORMS', 150, 400, 26, 'segoesl', None, (170, 200, 255), a, 0.5)
    text(f, '130の帳票。', 146, 490, 96, 'noto', 'Bold', (255, 255, 255), a, 0.04)
    text(f, '伝票・一覧・元帳・分析を、そのまま PDF へ。', 150, 590, 32, 'noto', 'Light', (230, 230, 255), a * prog(lt, 1.6, 2.4), 0.1)

# ---------------------------------------------------------------- 4. DB
DB0 = 33.6
@functools.lru_cache(maxsize=8)
def db_card(name, sub, col):
    w, h = 720, 440
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, w - 1, h - 1), 36, fill=(22, 18, 44, 235), outline=col + (255,), width=4)
    # DB アイコン（円柱）
    cx, cy = w // 2, 150
    for k in range(3):
        y = cy - 50 + k * 42
        d.ellipse((cx - 70, y - 18, cx + 70, y + 18), outline=col + (255,), width=5)
    d.line((cx - 70, cy - 50, cx - 70, cy + 34), fill=col + (255,), width=5)
    d.line((cx + 70, cy - 50, cx + 70, cy + 34), fill=col + (255,), width=5)
    t1 = text_img(name, 64, 'segoel', None, (255, 255, 255), 0.02)
    t2 = text_img(sub, 28, 'noto', 'Light', col, 0.1)
    im.alpha_composite(t1, ((w - t1.width) // 2, 270))
    im.alpha_composite(t2, ((w - t2.width) // 2, 360))
    return im

DBS = [('SQLite', '正典の方言', (95, 214, 216)), ('PostgreSQL', '16+', (160, 140, 255)),
       ('MariaDB', '10.11 LTS', (255, 170, 120)), ('Oracle', 'MCP 参照', (255, 120, 140))]
def scene_db(t, f):
    lt = t - DB0
    cam = Camera((0, 0.5, -16 + lt * 0.4), fov=44).look_at((0, 0, 0))
    for i, (n, s, col) in enumerate(DBS):
        st = i * BEAT * 0.5
        k = prog(lt, st, st + 0.7)
        if k <= 0:
            continue
        flip = (1 - ease_out_back(k)) * math.pi * 0.9
        x = (i - 1.5) * 4.6
        R = rot_y(flip) @ rot_x(0.05)
        draw_card(f, db_card(n, s, col), (x, -0.2, 0), 4.0, 4.0 * 440 / 720, R, cam, alpha=prog(lt, st, st + 0.15), fog=(30, 60))
    a = prog(lt, 0.9, 1.5) * (1 - prog(t, 38.0, 38.4))
    text(f, 'MULTI DATABASE', W / 2, 220, 24, 'segoesl', None, (170, 200, 255), a, 0.5, 'center')
    text(f, 'SQLite 方言を正典に、実行時変換。', W / 2, 300, 60, 'noto', 'Bold', (255, 255, 255), a, 0.04, 'center')
    text(f, '同じ業務ロジックで、規模に合わせて DB を選べる。', W / 2, H - 230, 30, 'noto', 'Light', (225, 225, 245), a * prog(lt, 1.6, 2.2), 0.1, 'center')

# ---------------------------------------------------------------- 5. フォトモザイク（寄りから引いてロゴが現れる）
MZ0 = 38.4
GW = 56           # 横のタイル数
TS = 20           # 等倍時のタイル幅(px)
UP = 5            # 高解像度倍率
@functools.lru_cache(maxsize=1)
def mosaic():
    logo = Image.open(os.path.join(ASSETS, 'logo_alpha.png')).convert('RGBA')
    tw, th = TS * UP, int(TS * UP * 0.66)
    gap = 3 * UP // 2
    gh = int(GW * (TS) * logo.height / logo.width / (TS * 0.66 + 1.5))
    small = np.asarray(logo.resize((GW, gh), Image.LANCZOS), np.float32) / 255
    thumbs = [Image.open(p).convert('RGB').resize((tw, th), Image.LANCZOS) for p in SCREENS]
    BW, BH = GW * (tw + gap), gh * (th + gap)
    raw = Image.new('RGBA', (BW, BH), (0, 0, 0, 0))
    tin = Image.new('RGBA', (BW, BH), (0, 0, 0, 0))
    n = 0
    for r in range(gh):
        for c in range(GW):
            col = small[r, c]
            if col[3] < 0.3:
                continue
            th_ = thumbs[(r * GW + c) * 7 % len(thumbs)]
            a = np.asarray(th_, np.float32) / 255
            lum = a.mean(2, keepdims=True)
            cc = col[:3] / max(col[:3].max(), 1e-3)
            rgb = np.clip(cc * (0.25 + 0.95 * lum) + a * 0.15, 0, 1)
            x, y = c * (tw + gap), r * (th + gap)
            raw.paste(th_.convert('RGBA'), (x, y))
            tin.paste(Image.fromarray((rgb * 255).astype(np.uint8)).convert('RGBA'), (x, y))
            n += 1
    lv = {}
    for k in (1, 2, 5):
        lv[k] = (raw.resize((BW * k // UP, BH * k // UP), Image.LANCZOS), tin.resize((BW * k // UP, BH * k // UP), Image.LANCZOS))
    return lv, BW / UP, BH / UP, n

MZ_CY = 400
def scene_mosaic(t, f):
    lt = t - MZ0
    lv, w1, h1, n = mosaic()
    ox, oy = W / 2 - w1 / 2, MZ_CY - h1 / 2
    fx, fy = W / 2 + 40, MZ_CY + 10       # ズーム中心（画面座標）
    z = 1 + 7.0 * (1 - ease_in_out_cubic(prog(lt, 0.3, 4.2)))
    level = 5 if z > 2.4 else (2 if z > 1.25 else 1)
    raw, tin = lv[level]
    sc = level  # 1x 座標 → 該当レベル画素
    # 画面の四隅を 1x 座標へ逆写像
    x0 = (fx + (0 - fx) / z - ox) * sc; x1 = (fx + (W - fx) / z - ox) * sc
    y0 = (fy + (0 - fy) / z - oy) * sc; y1 = (fy + (H - fy) / z - oy) * sc
    kt = prog(z, 1.2, 4.5)  # 寄りでは生の画面、引くほどロゴ色
    out = []
    for im in (raw, tin):
        out.append(pil_to_rgba(im.transform((W, H), Image.EXTENT, (x0, y0, x1, y1), Image.BILINEAR)))
    arr = out[0] * kt + out[1] * (1 - kt)
    blit(f, arr, 0, 0, prog(lt, 0.0, 0.4), 'topleft')
    done = prog(lt, 4.0, 4.6)
    if done > 0:
        sweep = prog(lt, 4.1, 5.3)
        if 0 < sweep < 1:
            yy, xx = grid()
            band = np.exp(-((xx + yy * 0.4 - sweep * (W + H * 0.4) * 1.1) / 90) ** 2)
            f += band[..., None] * 0.22 * np.asarray((0.8, 0.9, 1.0), np.float32)
    a0 = prog(lt, 0.5, 1.0) * (1 - prog(lt, 2.4, 2.9))
    if a0 > 0:
        yy, xx = grid()
        f *= (1 - 0.8 * a0 * np.clip((yy - (H - 330)) / 200, 0, 1) * np.clip(1 - xx / 1300, 0, 1))[..., None]
    text(f, '寄ってみると——', 150, H - 200, 40, 'noto', 'Light', (255, 255, 255), a0, 0.2)
    text(f, 'これは全部、実際の業務画面。', 150, H - 140, 30, 'noto', 'Light', (220, 220, 255), a0 * prog(lt, 1.0, 1.4), 0.15)
    text(f, 'Creative Vision 10', W / 2, 800, 70, 'segoel', None, (255, 255, 255), prog(lt, 4.4, 5.2), 0.08, 'center')
    text(f, '画面も、帳票も、すべてオープンソース。', W / 2, 885, 32, 'noto', 'Light', (230, 230, 255), prog(lt, 5.0, 5.8), 0.2, 'center')
    text(f, 'github.com/sekiya-sato/creativevision10   ·   2027.01 RELEASE', W / 2, 950, 20, 'segoel', None, (170, 200, 255), prog(lt, 5.6, 6.2) * 0.9, 0.15, 'center')
    text(f, f'このロゴは {n:,} 枚の実画面サムネイルでできています', W / 2, 1010, 16, 'noto', 'Light', (150, 150, 190), prog(lt, 6.2, 7.0) * 0.8, 0.1, 'center')

# ---------------------------------------------------------------- 合成
SCENES = [(0.0, 7.6, scene_wall), (7.2, 24.4, scene_flow), (24.0, 33.9, scene_forms), (33.6, 38.6, scene_db), (38.4, 48.0, scene_mosaic)]
XF = 0.4

def render(t):
    f = background(t)
    active = [(s, e, fn) for s, e, fn in SCENES if s <= t < e]
    if len(active) == 1:
        active[0][2](t, f)
    else:
        a = background(t); active[0][2](t, a)
        b = background(t); active[1][2](t, b)
        k = smooth(prog(t, active[1][0], active[0][1]))
        # 白い光を挟んでクロス
        f = a * (1 - k) + b * k + math.sin(math.pi * k) * 0.12
    bloom(f, 0.93, 0.25)
    vignette(f, 0.4)
    f = tonemap(f)
    grain(f, t, 0.014)
    return f * prog(t, 0, 0.6) * (1 - prog(t, 47.2, 48.0))

# ---------------------------------------------------------------- 音（100BPM、ピアノ的プラック + パッド）
def audio():
    D = DURATION
    m = Mix(D)
    chords = [[48, 55, 59, 64], [45, 52, 55, 60], [41, 48, 52, 57], [43, 50, 55, 59]]  # Cmaj7 Am7 Fmaj7 G
    nbars = int(D / BAR) + 1
    for bar in range(nbars):
        tb = bar * BAR
        ch = chords[bar % 4]
        m.add(tb, pad([n + 12 for n in ch], BAR + 0.6, cutoff=1500 if bar < 3 else 2400, attack=0.6, release=0.6, seed=bar), 0.12)
        # アルペジオ
        pat = [0, 2, 1, 3, 2, 1, 3, 2]
        for k in range(8):
            n = ch[pat[k]] + 24
            if bar >= 3 or k % 2 == 0:
                m.add(tb + k * BEAT / 2, pluck(n, 0.5, 4000, 0.18), 0.07, pan=math.sin(k * 0.9) * 0.5)
        if 3 <= bar < 19:
            for b in range(4):
                tt = tb + b * BEAT
                m.add(tt, kick(0.4, 130, 45, 0.25, 0.3), 0.45)
                m.add(tt + BEAT / 2, hat(0.1, False, seed=b), 0.1, pan=0.3)
                if b % 2 == 1 and bar >= 4:
                    m.add(tt, snare(0.3), 0.12)
            bn = ch[0] - 12
            for h in range(4):
                bs = lowpass(osc(midi(bn), BEAT * 0.9, 'saw'), 400) * env_adsr(int(BEAT * 0.9 * SR), 0.005, 0.1, 0.7, 0.05)
                m.add(tb + h * BEAT, bs, 0.18)
    # 場面転換
    for ts in (7.2, 24.0, 33.6):
        m.add(ts - 0.8, riser(0.8, 600, 9000, int(ts)), 0.25)
        m.add(ts, sub_boom(1.5, 90, 38, 0.5), 0.5)
        m.add(ts - 0.3, whoosh(0.6, 500, 6000, int(ts) + 3), 0.25)
    # フローのステップ
    for i in range(1, 6):
        m.add(F0 + i * FSTEP - FSTEP * 0.38, whoosh(1.0, 300, 3000, 40 + i), 0.18, pan=0.4)
    # 帳票: 紙めくりっぽいノイズ
    for k in range(46):
        st = FS0 + k * 0.05
        m.add(st, bandpass(noise(0.05, k), 2000, 8000) * env_exp(int(0.05 * SR), 0.012), 0.08, pan=math.sin(k) * 0.7)
    # DB カード
    for i in range(4):
        m.add(DB0 + i * BEAT * 0.5, tick(1500 + i * 400, 0.06), 0.15)
    # モザイク: タイル着地のきらめき
    rng = np.random.default_rng(3)
    for k in range(70):
        st = MZ0 + 0.4 + (k / 70) ** 1.5 * 3.8
        m.add(st, pluck(84 + int(rng.choice([0, 2, 4, 7, 9, 12])), 0.3, 6000, 0.06), 0.035, pan=float(rng.uniform(-0.8, 0.8)))
    m.add(MZ0 + 1.0, riser(3.0, 200, 10000, 77), 0.35)
    m.add(MZ0 + 4.0, sub_boom(4.0, 70, 30, 1.4), 0.8)
    m.add(MZ0 + 4.0, pad([48, 55, 60, 64, 67, 72], 5.6, cutoff=3000, attack=0.02, release=3.0, seed=99), 0.25)
    x = m.master(D + 1)
    x = reverb(x, 2.4, 0.25)
    x = x[:int(D * SR)]
    pk = np.max(np.abs(x)) + 1e-9
    x = np.tanh(x / pk * 1.3) / np.tanh(1.3) * 0.9
    fo = int(1.0 * SR)
    x[-fo:] *= np.linspace(1, 0, fo)[:, None]
    return x
