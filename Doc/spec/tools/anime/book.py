# 作品3「まほうのしごと絵本」— 3D の飛び出す絵本（約60秒）
# 机の上の絵本がひらき、ページをめくるたびに紙細工（主人公・雲・実画面・3階層の城・ロゴ）が立ち上がる。
# 影付きの3D描画（シャドウマップ）と被写界深度。主人公の紙細工はリグで描いた絵を毎コマ貼るので、本の上でもまばたきして動く。
# 音楽は主題歌の旋律をオルゴールに編曲して合成、語りは VOICEVOX（九州そら）。
#
# 秒割り（112BPM、1小節 2.143 秒）
#   0.0- 4.3  閉じた本（表紙）にカメラが寄る              オルゴールの前奏
#   4.3- 7.0  表紙がひらく
#   7.0-12.9  見開き1: タイトルと主人公                  「これは、ひとりの魔女っ子エンジニアと…」
#  12.9-21.4  見開き2: 冬、30のファイル（雪・最初の画面）   「2025年の冬。はじまりは…」
#  21.4-30.0  見開き3: 3階層の城                        「データ、ロジック、画面…」
#  30.0-40.0  見開き4: AIと手をつないで、画面の翼         「AIと手をつないで…」
#  40.0-60.0  見開き5: ロゴと 2027年1月                  「そして2027年1月…」→ 引き
import os, sys, math
import numpy as np
import cv2
from PIL import Image, ImageDraw
import engine as E, rig as R, fx, clouds, voice, pop3d as P3
from engine import prog, lerp, ease_io, clamp, spring

import core  # noqa: E402

FPS = 30
SUPERSAMPLE = 2
SR = 48000
BPM = 112
BEAT = 60 / BPM
BAR = BEAT * 4
DURATION = 60.0

PW, PH = 3.0, 4.0          # 1ページの幅（x）と奥行き（z）
BASE_Y = 0.16              # 開いたページ面の高さ

C = None


class Ctx:
    pass


# ---------------------------------------------------------------- 素材
def paper_tex(seed, wash=(0.85, 0.8, 1.0), text_lines=None, page_no=None, size=(1200, 1600), illus=None, left=True):
    """クリーム色の紙に水彩のにじみ、飾り罫、物語の文"""
    W_, H_ = size
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:H_, 0:W_].astype(np.float32)
    base = np.array([0.965, 0.94, 0.88], np.float32)
    img = np.broadcast_to(base, (H_, W_, 3)).copy()
    # 水彩のにじみ（低周波ノイズ）
    n = np.zeros((H_, W_), np.float32)
    amp = 1.0
    for s in (400, 200, 90, 40):
        g = rng.standard_normal((H_ // s + 3, W_ // s + 3)).astype(np.float32)
        n += cv2.resize(g, (W_ + 3 * s, H_ + 3 * s), interpolation=cv2.INTER_CUBIC)[:H_, :W_] * amp
        amp *= 0.5
    m = np.clip(0.5 + n * 0.35, 0, 1) ** 1.5
    w = np.array(wash, np.float32)
    img = img * (1 - 0.35 * m[..., None]) + (img * w) * (0.35 * m[..., None])
    # 紙の繊維
    fib = rng.standard_normal((H_, W_)).astype(np.float32)
    fib = cv2.GaussianBlur(fib, (0, 0), 0.8)
    img *= (1 + fib[..., None] * 0.015)
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
    d = ImageDraw.Draw(im)
    # 飾り罫
    c = (150, 120, 170)
    d.rectangle([60, 60, W_ - 60, H_ - 60], outline=c, width=3)
    d.rectangle([75, 75, W_ - 75, H_ - 75], outline=c, width=1)
    for (x, y) in [(60, 60), (W_ - 60, 60), (60, H_ - 60), (W_ - 60, H_ - 60)]:
        d.ellipse([x - 12, y - 12, x + 12, y + 12], outline=c, width=3)
    if illus is not None:
        ill = illus.convert('RGBA')
        ill.thumbnail((W_ - 260, H_ - 700))
        x0 = (W_ - ill.width) // 2
        im.paste(ill, (x0, 200), ill)
        d.rectangle([x0 - 6, 194, x0 + ill.width + 6, 200 + ill.height + 6], outline=(120, 90, 140), width=3)
    if text_lines:
        f = E.font('mincho', 64)
        y = H_ - 520 if illus is not None else 380
        for ln in text_lines:
            tw = f.getlength(ln)
            d.text(((W_ - tw) / 2, y), ln, font=f, fill=(60, 40, 70))
            y += 100
    if page_no is not None:
        f = E.font('mincho', 40)
        s = f'— {page_no} —'
        d.text(((W_ - f.getlength(s)) / 2, H_ - 150), s, font=f, fill=(110, 90, 120))
    return np.asarray(im)


def diecut(rgba, border=14, color=(250, 248, 240)):
    """切り抜きの外側に白い紙の縁を付ける（型抜きの紙細工らしく）"""
    a = rgba[..., 3].astype(np.float32) / 255 if rgba.dtype == np.uint8 else rgba[..., 3]
    rgb = rgba[..., :3].astype(np.float32) / 255 if rgba.dtype == np.uint8 else rgba[..., :3]
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (border * 2 + 1, border * 2 + 1))
    big = cv2.dilate((a > 0.5).astype(np.uint8), k).astype(np.float32)
    big = cv2.GaussianBlur(big, (0, 0), 1.0)
    out = np.zeros(rgba.shape[:2] + (4,), np.float32)
    out[..., :3] = rgb * a[..., None] + np.array(color, np.float32) / 255 * (1 - a[..., None])
    out[..., 3] = np.maximum(big, a)
    return out


def pad_img(rgba, p):
    return np.pad(rgba, ((p, p), (p, p), (0, 0)))


def cloud_card(cl_idx, palette='day', border=16):
    """clouds.py の雲を色付けして紙の切り抜きに"""
    m = cv2.imread(os.path.join(clouds.DIR, f'cu{cl_idx}.png'), cv2.IMREAD_UNCHANGED)[..., [2, 1, 0, 3]].astype(np.float32) / 65535
    pal = clouds.PALETTES[palette]
    l = m[..., 0]
    sh, mi, li = map(np.array, (pal['shade'], pal['mid'], pal['lit']))
    k1 = np.clip((l - 0.12) / 0.28, 0, 1)[..., None]; k2 = np.clip((l - 0.5) / 0.28, 0, 1)[..., None]
    col = sh * (1 - k1) + mi * k1
    col = col * (1 - k2) + li * k2
    col *= (1 - 0.3 * m[..., 1:2])
    rgba = np.concatenate([col, m[..., 3:4]], -1)
    rgba = cv2.resize(rgba, (rgba.shape[1] // 2, rgba.shape[0] // 2), interpolation=cv2.INTER_AREA)
    return diecut(pad_img(rgba, border + 4), border)


def text_card(text, size, fam='segoeb', color=(0.45, 0.25, 0.7), border=10, weight=None):
    a = E.text_rgba(text, size, fam=fam, weight=weight, color=color, pad=border + 6)
    return diecut(a, border)


def img_card(path, maxw=900, border=12, frame=True):
    im = Image.open(path).convert('RGBA')
    im.thumbnail((maxw, maxw))
    a = np.asarray(im).astype(np.float32) / 255
    if frame:
        a = np.pad(a, ((border, border), (border, border), (0, 0)), constant_values=1.0)
        return a
    return diecut(pad_img(a, border + 4), border)


def setup(gl):
    global C
    C = Ctx()
    C.gl = gl
    C.r3 = P3.R3(gl, 4096)
    C.ch = R.Character(gl)
    C.sims = {}
    # 主人公の紙細工用のオフスクリーン（毎コマ描き直す）
    C.char_t = E.Target(gl.ctx, 1600, 1600, dtype='f2')
    C.char_t.tex.build_mipmaps()
    import moderngl
    C.char_t.tex.filter = (moderngl.LINEAR_MIPMAP_LINEAR, moderngl.LINEAR)
    T = lambda arr, key, premul=False: gl.texture(arr, key=key, premul=premul)
    shots = os.path.join(E.ASSETS, 'screens')
    koma = Image.open(os.path.join(E.ASSETS4, 'dev4koma-20260710.jpg'))
    # ページ（左: 物語の文、右: 絵の地）
    C.pages = []
    texts = [
        (['これは、ひとりの', '魔女っ子エンジニアと', 'Creative Vision 10 の', 'おはなし。'], (0.9, 0.8, 1.0), None),
        (['2025年の冬。', 'はじまりは、', 'たった30のファイル', 'でした。'], (0.75, 0.85, 1.0), koma.crop((0, 0, 506, 506))),
        (['データ、ロジック、画面。', '3つの層を、', 'ひとつずつ', '積み上げて。'], (0.85, 0.95, 0.85), None),
        (['AIと手をつないで、', '画面も帳票もテストも', 'どんどん', '増えていきました。'], (1.0, 0.85, 0.9), koma.crop((518, 518, 1024, 1024))),
        (['そして 2027年1月。', 'あたらしい', '業務の扉が', 'ひらきます。'], (1.0, 0.92, 0.75), None),
    ]
    for i, (lines, wash, ill) in enumerate(texts):
        left = paper_tex(10 + i, wash, lines, page_no=i * 2 + 1, illus=ill)
        right = paper_tex(40 + i, wash, None, page_no=i * 2 + 2)
        C.pages.append((T(left, ('pgL', i)), T(right, ('pgR', i))))
    # 表紙
    cov = np.zeros((1600, 1200, 3), np.float32)
    yy, xx = np.mgrid[0:1600, 0:1200].astype(np.float32)
    cov[:] = np.array(E.hexc('3a1f6e'))
    cov *= (0.85 + 0.15 * np.sin(xx / 1200 * math.pi))[..., None]
    im = Image.fromarray((cov * 255).astype(np.uint8))
    d = ImageDraw.Draw(im)
    gold = (232, 196, 110)
    d.rectangle([70, 70, 1130, 1530], outline=gold, width=8)
    d.rectangle([95, 95, 1105, 1505], outline=gold, width=2)
    f1 = E.font('segoeb', 120); f2 = E.font('mincho', 72); f3 = E.font('mincho', 52)
    for s, f, y in [('Creative', f1, 420), ('Vision 10', f1, 560), ('まほうのしごと絵本', f2, 860), ('～ 明日へ飛べ ～', f3, 980)]:
        d.text(((1200 - f.getlength(s)) / 2, y), s, font=f, fill=gold)
    logo = Image.open(os.path.join(E.ASSETS, 'logo_alpha.png')).convert('RGBA')
    la = np.asarray(logo).astype(np.float32) / 255
    lg = np.zeros_like(la); lg[..., :3] = np.array(gold) / 255; lg[..., 3] = la[..., 3]
    lgi = Image.fromarray((lg * 255).astype(np.uint8)); lgi.thumbnail((420, 420))
    im.paste(lgi, ((1200 - lgi.width) // 2, 1150), lgi)
    C.cover = T(np.asarray(im), ('cover',))
    C.cover_in = T(paper_tex(99, (0.8, 0.75, 0.95)), ('coverin',))
    # 紙細工
    C.clouds = [T(cloud_card(i, 'day'), ('ccl', i)) for i in range(6)]
    C.clouds_n = [T(cloud_card(i, 'sunrise'), ('ccs', i)) for i in range(6)]
    names = ['Customers', 'DailyShopBudgetQuery', 'Result_RxF', 'ShiireInputDetail', 'InventoryAllocationMatrix',
             'HachuHaibunInputDetail', 'JuchuInputDetail', 'HachuHaibunInputArrived', 'DeliveryScheduleInquiry',
             'InventoryAllocationList', 'HaibunCommitShohin', 'L03_OrderFixApplied', 'MasterYosanBrandMente',
             'ShiireTrendReport', 'SalesOrderAllocation', 'StagnationList', 'HenpinInputSearched', 'JuchuBestTable']
    C.screens = []
    for n_ in names:
        p = os.path.join(shots, n_ + '.jpg')
        if os.path.exists(p):
            a = img_card(p, 700)
            C.screens.append((T(a, ('scr', n_)), a.shape[1] / a.shape[0]))
    lg = np.asarray(logo).astype(np.float32) / 255
    lg = cv2.resize(lg, (lg.shape[1], lg.shape[0]))
    C.logo = T(diecut(pad_img(lg, 30), 22), ('logoc',))
    C.logo_ar = (lg.shape[1] + 60) / (lg.shape[0] + 60)
    C.title = T(text_card('Creative Vision 10', 150), ('ttl',))
    C.title_ar = C.title.size[0] / C.title.size[1]
    C.rel = T(text_card('2027.1  RELEASE', 120, color=(0.85, 0.45, 0.2)), ('rel',))
    C.rel_ar = C.rel.size[0] / C.rel.size[1]
    C.snow = T(text_card('❄', 200, fam='sym', color=(0.55, 0.7, 0.95), border=8), ('snow',))
    C.star = T(text_card('★', 200, fam='sym', color=(0.98, 0.78, 0.25), border=8), ('star',))
    tiers = [('DATA', 'SQLite / PostgreSQL / MariaDB', (0.55, 0.42, 0.95)), ('APPLICATION', '.NET 10 / gRPC', (0.25, 0.7, 0.95)),
             ('PRESENTATION', 'WPF / MVVM', (0.95, 0.45, 0.75))]
    C.tiers = []
    for name, sub, col in tiers:
        im = Image.new('RGB', (1200, 300), tuple(int(c * 255) for c in col))
        d = ImageDraw.Draw(im)
        f = E.font('segoeb', 110); g = E.font('noto', 54, 'Bold')
        d.text(((1200 - f.getlength(name)) / 2, 30), name, font=f, fill=(255, 255, 255))
        d.text(((1200 - g.getlength(sub)) / 2, 185), sub, font=g, fill=(255, 255, 255))
        d.rectangle([8, 8, 1192, 292], outline=(255, 255, 255), width=8)
        C.tiers.append((T(np.asarray(im), ('tier', name)), col))
    C.orb = T(orb_tex(), ('orb',))
    C.table_mesh = P3.quad(40, 40)
    C.card = P3.quad(1.0, 1.0)
    C.boxes = {}
    # 語り
    C.narr = []
    for key, t0, text, disp in NARR:
        a, moras = voice.say(text, 16, speed=1.18, intonation=1.15, pitch=-0.01, pause=0.45)
        C.narr.append(dict(t0=t0, dur=len(a) / SR, audio=a, disp=disp))


def orb_tex():
    s = 512
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32)
    r = np.hypot(xx - s / 2, yy - s / 2) / (s / 2)
    a = np.clip((1 - r) * 8, 0, 1)
    col = np.zeros((s, s, 4), np.float32)
    g = np.clip(1 - r, 0, 1)
    col[..., 0] = 0.4 + 0.6 * g ** 3; col[..., 1] = 0.8 + 0.2 * g; col[..., 2] = 1.0
    ring = np.exp(-((r - 0.7) / 0.03) ** 2)
    col[..., :3] = col[..., :3] * (1 - ring[..., None]) + ring[..., None]
    col[..., 3] = a
    return diecut(col, 14)


NARR = [
    ('n1', 5.6, 'これは、魔女っ子エンジニアと、クリエイティブ ビジョン テンの、おはなし。',
     'これは、魔女っ子エンジニアと Creative Vision 10 のおはなし。'),
    ('n2', 14.0, '二千二十五年の冬。はじまりは、たった、さんじゅうのファイルでした。', '2025年の冬。はじまりは、たった30のファイルでした。'),
    ('n3', 22.4, 'データ、ロジック、画面。みっつの層を、ひとつずつ、積み上げて。', 'データ、ロジック、画面。3つの層を、ひとつずつ積み上げて。'),
    ('n4', 31.0, 'エーアイと手をつないで、画面も、帳票も、テストも、どんどん、増えていきました。', 'AIと手をつないで、画面も帳票もテストも、どんどん増えていきました。'),
    ('n5', 41.0, 'そして、二千二十七年、一月。あたらしい業務の扉が、ひらきます。', 'そして2027年1月。あたらしい業務の扉が、ひらきます。'),
    ('n6', 50.5, 'クリエイティブ、ビジョン、テン。', 'Creative Vision 10'),
]


# ---------------------------------------------------------------- 本の形
def page_surface(side, U, V, lift=0.0):
    """開いたページ面。side=+1 右, -1 左。U: 綴じ側 0 → 小口 1"""
    x = side * U * PW
    z = (V - 0.5) * PH
    y = BASE_Y - 0.11 * np.exp(-U * PW / 0.28) + lift
    pos = np.stack([x, y, z], -1)
    # 法線（数値微分）
    dy = 0.11 / 0.28 * np.exp(-U * PW / 0.28) * side
    n = np.stack([-dy * 1.0, np.ones_like(U), np.zeros_like(U)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return pos, n


def turning_page(phi, curl=0.9, lift=0.0, nx=40):
    """綴じ目を軸にめくれるページ（phi: 0 右に伏せる → π 左に伏せる）。紙がしなるように先ほど遅れる"""
    us = np.linspace(0, 1, nx + 1)
    s = math.sin(phi)
    th = phi + curl * s * (us ** 1.5) * (1 if phi < math.pi / 2 else 1)
    th = np.clip(th, 0, math.pi)
    du = PW / nx
    xs = np.concatenate([[0], np.cumsum(np.cos(th[:-1]) * du)])
    ys = np.concatenate([[0], np.cumsum(np.sin(th[:-1]) * du)]) + BASE_Y + 0.004 + lift
    def fn(U, V):
        i = np.clip((U * nx).round().astype(int), 0, nx)
        x = xs[i]; y = ys[i]
        z = (V - 0.5) * PH
        pos = np.stack([x, y, z], -1)
        t_ = th[i]
        nrm = np.stack([-np.sin(t_), np.cos(t_), np.zeros_like(t_)], -1)
        return pos, nrm
    return P3.grid(nx, 6, fn)


_static = {}


def static_page(side):
    if side not in _static:
        m = P3.grid(40, 6, lambda U, V: page_surface(side, U, V))
        if side < 0:
            m.uv[:, 0] = 1 - m.uv[:, 0]     # 左ページは綴じ目が右端
        _static[side] = m
    return _static[side]


def get_box(w, h, d):
    k = (round(w, 3), round(h, 3), round(d, 3))
    if k not in C.boxes:
        C.boxes[k] = P3.box(w, h, d)
    return C.boxes[k]


def standing(tex, aspect, x, z, h, rise, yaw=0.0, lean=0.0, y0=None, **mat):
    """ページに立つ紙細工。rise 0 で伏せ、1 で直立。底辺の中心が (x, z)"""
    if rise <= 0.001:
        return
    w = h * aspect
    y = BASE_Y + 0.002 if y0 is None else y0
    M = P3.translate(x, y, z) @ P3.roty(yaw) @ P3.rotx(-(1 - rise) * math.pi / 2 + lean) @ P3.translate(0, h / 2, 0) @ P3.scale(w, h, 1)
    C.r3.add(C.card, M, tex, **mat)


# ---------------------------------------------------------------- 主人公（紙細工に毎コマ描く）
def blink_at(t, seed=0, period=3.0):
    k = math.floor((t + seed * 1.37) / period)
    ph = E.hash11(k + seed * 31) * (period - 0.4)
    tb = k * period - seed * 1.37 + ph + 0.2
    d = t - tb
    if 0 <= d < 0.16:
        return math.sin(math.pi * d / 0.16) ** 0.7
    return 0.0


def char_texture(t, expr=(0, 1, 0, 0), key='c', wind=6.0, head=0.0):
    gl = C.gl

    def pf(tt):
        p = R.Pose()
        p.head = head + 2.0 * math.sin(tt * 1.2)
        p.head_shift = (4 * math.sin(tt * 0.8), 3 * math.sin(tt * 1.5))
        p.breath = 0.006 * math.sin(tt * 2.0)
        p.blink = blink_at(tt)
        p.wind = wind * math.sin(tt * 0.9)
        p.expr_face = expr; p.expr_full = (0, 1, 0, 0) if expr[3] > 0 else expr
        return p
    s = C.sims.get(key)
    if s is None:
        s = R.Sim(pf, FPS, 4, t - 0.5)
        C.sims[key] = s
    st = s.state(t)
    u = C.ch.uniforms(st['pose'], t, st)
    prev = getattr(gl, 'cur', gl.main)
    gl.use(C.char_t, clear=(0, 0, 0, 0))
    # uv(0..1) → 目標全体（2048 の正方形の上部 0..1650 を使う）。draw は仮想→内部を cur.w/VW 倍するので横幅 VW 基準。
    # FBO は下の行から並ぶので上下を反転して描く（板に貼ると正立する）
    Hm = E.m3_translate(0, gl.VW) @ E.m3_scale(gl.VW, -gl.VW * 2048 / 1650)
    C.ch.draw(Hm, u, dict(outlineW=26.0, outlineCol=(0.98, 0.97, 0.94), cropBottom=1650))
    C.char_t.tex.build_mipmaps()
    gl.use(prev)
    return C.char_t.tex


# ---------------------------------------------------------------- 場面の組み立て
def spread_items(i, rise, t):
    """見開き i の紙細工を r3 へ積む。rise は 0..1（めくりで立ち上がる）"""
    def r(k0, k1=None):
        # 要素ごとに少しずつ遅れて立つ（ばね）
        a = clamp((rise - k0) / (1 - k0)) if k1 is None else clamp((rise - k0) / (k1 - k0))
        return clamp(spring(a, 2.2, 5.0) if a < 1 else 1.0, 0, 1.08)
    if i == 0:
        ct = char_texture(t, (0, 1, 0, 0), 'c0')
        standing(ct, 1.0 / (1650 / 2048), 1.45, 0.35, 2.4, r(0.15), premul=1.0, alphaTest=0.5)
        standing(C.clouds[0], 2.0, 0.8, -0.9, 1.1, r(0.0, 0.6))
        standing(C.clouds[2], 2.0, 2.3, -0.5, 0.9, r(0.05, 0.65))
        standing(C.star, 1.0, 2.5, 1.2, 0.45, r(0.3), yaw=0.3 * math.sin(t))
        standing(C.title, C.title_ar, -1.5, 1.4, 0.5, r(0.2), lean=-0.15)
    elif i == 1:
        ct = char_texture(t, (1, 0, 0, 0), 'c1', wind=3.0)
        standing(ct, 1.0 / (1650 / 2048), 1.6, 0.6, 2.2, r(0.2), premul=1.0)
        for k in range(3):
            s, ar = C.screens[k]
            standing(s, ar, 0.7 + k * 0.85, -1.2 + 0.15 * k, 0.75, r(0.05 + 0.1 * k), yaw=-0.25 + 0.25 * k)
        for k in range(7):
            x = 0.4 + (k * 0.37) % 2.4
            hh = 1.3 + 0.6 * math.sin(k * 2.1)
            yy = BASE_Y + hh * r(0.3) + 0.08 * math.sin(t * 1.5 + k)
            M = P3.translate(x, yy, -0.2 + (k % 3) * 0.6) @ P3.roty(t * 0.8 + k) @ P3.scale(0.28)
            C.r3.add(C.card, M, C.snow)
            # 糸
            M2 = P3.translate(x, yy + 0.14 + 1.5, -0.2 + (k % 3) * 0.6) @ P3.scale(0.006, 3.0, 0.006)
            C.r3.add(get_box(1, 1, 1), M2, None, tint=(0.9, 0.9, 0.9), cast=False)
    elif i == 2:
        # 3階層の城: 下から Data / Application / Presentation の紙の台
        for k, (tex, col) in enumerate(C.tiers):
            rk = r(0.1 + 0.2 * k, 0.5 + 0.2 * k)
            if rk <= 0:
                continue
            w, d_, h = 2.5 - 0.35 * k, 1.6 - 0.25 * k, 0.42
            y = BASE_Y + (h * k + h / 2) * rk
            M = P3.translate(1.5, y, -0.2) @ P3.scale(1, rk, 1)
            C.r3.add(get_box(w, h, d_), M, None, tint=tuple(0.55 + 0.45 * np.array(col)))
            # ラベルは前面だけに貼る
            Ml = P3.translate(1.5, y, -0.2 + d_ / 2 + 0.004) @ P3.scale(w * 0.96, h * 0.92 * rk, 1)
            C.r3.add(C.card, Ml, tex, cast=False)
            # 台の上に画面
            s, ar = C.screens[3 + k * 2]
            standing(s, ar, 1.5 + (k - 1) * 0.5, -0.2 - d_ / 2 + 0.1, 0.55, r(0.4 + 0.15 * k), y0=BASE_Y + h * (k + 1) * rk)
        ct = char_texture(t, (0, 1, 0, 0), 'c2')
        standing(ct, 1.0 / (1650 / 2048), -0.2, 1.05, 1.7, r(0.5), yaw=0.35, premul=1.0)
        standing(C.clouds[3], 2.0, 2.6, 1.3, 0.6, r(0.6))
    elif i == 3:
        ct = char_texture(t, (0, 1, 0, 0), 'c3')
        standing(ct, 1.0 / (1650 / 2048), 1.3, 0.5, 2.2, r(0.2), premul=1.0)
        # AI の光の玉
        oy = BASE_Y + 1.6 * r(0.3) + 0.1 * math.sin(t * 2)
        M = P3.translate(2.35, oy, 0.7) @ P3.roty(math.sin(t) * 0.4) @ P3.scale(0.6)
        C.r3.add(C.card, M, C.orb, emis=0.45)
        # 画面の翼
        for k in range(10):
            s, ar = C.screens[(k + 5) % len(C.screens)]
            side = -1 if k % 2 == 0 else 1
            j = k // 2
            ang = side * (0.35 + 0.25 * j)
            ax = 1.3 + side * (0.45 + 0.42 * j)
            ay = BASE_Y + 0.9 + 0.32 * j * r(0.2 + 0.08 * j) + 0.05 * math.sin(t * 2 + k)
            rk = r(0.25 + 0.07 * j)
            if rk <= 0:
                continue
            M = P3.translate(ax, ay, -0.25 - 0.1 * j) @ P3.rotz(-ang * rk) @ P3.roty(-side * 0.35) @ P3.scale(0.62 * ar * rk, 0.62 * rk, 1)
            C.r3.add(C.card, M, s, emis=0.15)
    elif i == 4:
        standing(C.logo, C.logo_ar, 1.5, -0.3, 1.7, r(0.1))
        standing(C.title, C.title_ar, 1.5, 0.55, 0.42, r(0.3))
        standing(C.rel, C.rel_ar, 1.5, 1.15, 0.36, r(0.45))
        ct = char_texture(t, (0, 1, 0, 0), 'c4')
        standing(ct, 1.0 / (1650 / 2048), 2.55, 0.9, 1.5, r(0.5), yaw=-0.35, premul=1.0)
        for k in range(4):
            standing(C.clouds_n[k], 2.0, 0.4 + k * 0.8, -1.5, 0.7, r(0.0, 0.5), yaw=0.1 * (k - 1.5))
        for k in range(6):
            yy = BASE_Y + 1.9 + 0.3 * math.sin(t * 1.3 + k)
            M = P3.translate(0.3 + k * 0.5, yy * r(0.6), -0.9 + 0.25 * (k % 2)) @ P3.roty(t * 1.2 + k) @ P3.scale(0.22)
            C.r3.add(C.card, M, C.star, emis=0.3)


# ---------------------------------------------------------------- 時間割
OPEN = (4.3, 7.0)              # 表紙がひらく
TURNS = [12.9, 21.4, 30.0, 40.0]   # 見開きをめくる開始
TURN_DUR = 1.6


def state(t):
    """(見開き番号, そのページの立ち上がり, めくり中のページ角, めくり元/先)"""
    if t < OPEN[1]:
        return None
    sp = 0
    for k, tt in enumerate(TURNS):
        if t >= tt:
            sp = k + 1
    return sp


def frame(gl, t):
    gl.time = t
    r3 = C.r3
    r3.clear()
    # 机
    r3.add(C.table_mesh, P3.rotx(-math.pi / 2) @ P3.scale(1), None, mode=1.0, paper=0.0, specAmt=0.15, cast=False)
    # 表紙・裏表紙・本の厚み
    k_open = ease_io(prog(t, *OPEN))
    thick = 0.12
    # 右側の本体（ページの束）
    r3.add(get_box(PW, thick, PH), P3.translate(PW / 2, thick / 2, 0), None, tint=(0.95, 0.92, 0.85), paper=1.0)
    r3.add(get_box(PW + 0.08, 0.03, PH + 0.1), P3.translate(PW / 2, 0.015, 0), None, tint=E.hexc('3a1f6e'), paper=0.3)
    # 左側（開いた表紙と束）
    if k_open > 0:
        r3.add(get_box(PW, thick, PH), P3.translate(-PW / 2, thick / 2, 0) @ P3.scale(1, k_open, 1), None, tint=(0.95, 0.92, 0.85))
    # 表紙（綴じ目を軸に開く）
    ang = k_open * math.pi
    Mcov = P3.translate(0, thick + 0.015, 0) @ P3.rotz(ang) @ P3.translate(PW / 2 + 0.04, 0, 0) @ P3.rotx(-math.pi / 2)
    r3.add(C.card, Mcov @ P3.scale(PW + 0.08, PH + 0.1, 1), C.cover, backWhite=0.0, paper=0.4, specAmt=0.2)
    # 表紙の裏（内側）
    r3.add(C.card, Mcov @ P3.translate(0, 0, -0.006) @ P3.rotx(math.pi) @ P3.scale(PW + 0.08, PH + 0.1, 1), C.cover_in)
    sp = state(t)
    if sp is not None:
        # 開いている見開き
        L, Rt = C.pages[sp]
        r3.add(static_page(-1), np.eye(4), L, backWhite=0.0)
        r3.add(static_page(1), np.eye(4), Rt, backWhite=0.0)
        # 見開きの紙細工が立ち上がる
        t_start = OPEN[1] - 0.6 if sp == 0 else TURNS[sp - 1] + TURN_DUR * 0.55
        rise = prog(t, t_start, t_start + 1.6)
        # 次のめくりの前に伏せる
        if sp < len(TURNS) and t > TURNS[sp] - 0.6:
            rise = 1 - prog(t, TURNS[sp] - 0.6, TURNS[sp] + 0.3)
        spread_items(sp, rise, t)
        # めくれているページ
        for k, tt in enumerate(TURNS):
            if tt <= t < tt + TURN_DUR:
                phi = ease_io(prog(t, tt, tt + TURN_DUR)) * math.pi
                mesh = turning_page(phi)
                # 表は前の見開きの右、裏は次の見開きの左
                # 表は前の見開きの右ページ、裏は次の見開きの左ページ
                r3.add(mesh, np.eye(4), C.pages[k][1], tex2=C.pages[k + 1][0], backWhite=0.0)
    # カメラ
    eye, target, fov, focus = camera(t)
    r3.render(eye, target, fov, light_dir=(-0.55, 1.0, 0.45), light_col=(0.92, 0.82, 0.7), sky=(0.36, 0.37, 0.46),
              ground=(0.32, 0.25, 0.2), light_box=9.0, shadow_soft=3.0, clear=(0.04, 0.03, 0.05))
    r3.dof(focus, 3.2, 11.0)
    # 光の粒（窓から差す光の埃）
    rng = np.random.default_rng(3)
    n = 60
    fx.parts(gl, (rng.uniform(0, 1920, n) + t * 10) % 1920, (rng.uniform(0, 1080, n) + np.sin(t * 0.5 + np.arange(n)) * 30), rng.uniform(3, 9, n),
             (1, 0.9, 0.7), rng.uniform(0.1, 0.4, n), kind=0)
    fx.vignette(gl, 0.55, inner=0.3)
    subtitle(t)
    return gl.finish(dict(bloomAmt=0.5, bloomThr=1.0, vign=0.3, grainAmt=0.02, fade=max(1 - prog(t, 0, 1.0), prog(t, DURATION - 1.2, DURATION - 0.05))))


def camera(t):
    """(eye, target, fov, focus 距離)"""
    keys = [
        (0.0, (0.6, 9.0, 9.5), (0.6, 0.0, 0.0), 30),
        (4.3, (0.4, 7.0, 7.4), (0.4, 0.0, 0.0), 32),
        (7.5, (1.4, 4.2, 6.4), (1.0, 0.9, -0.1), 34),
        (12.5, (2.2, 3.6, 5.8), (1.3, 0.9, 0.0), 34),
        (14.5, (0.6, 3.8, 6.3), (1.1, 0.8, -0.2), 34),
        (21.0, (1.9, 3.4, 5.6), (1.3, 0.8, 0.0), 34),
        (23.0, (3.4, 3.0, 5.4), (1.4, 0.9, -0.2), 34),
        (29.6, (1.0, 3.9, 6.2), (1.4, 1.0, 0.0), 34),
        (32.0, (0.2, 3.3, 5.6), (1.4, 1.2, -0.1), 36),
        (39.6, (2.4, 3.4, 5.8), (1.4, 1.1, 0.0), 34),
        (43.0, (1.5, 3.0, 5.2), (1.5, 1.0, -0.1), 34),
        (50.0, (1.0, 5.0, 7.2), (0.9, 0.7, 0.0), 34),
        (60.0, (0.4, 9.0, 10.0), (0.4, 0.3, 0.0), 32),
    ]
    for i in range(len(keys) - 1):
        if keys[i][0] <= t < keys[i + 1][0] or i == len(keys) - 2:
            a, b = keys[i], keys[i + 1]
            break
    k = ease_io(prog(t, a[0], b[0]))
    eye = np.array(a[1]) + (np.array(b[1]) - np.array(a[1])) * k
    tgt = np.array(a[2]) + (np.array(b[2]) - np.array(a[2])) * k
    fov = lerp(a[3], b[3], k)
    # 手持ちの揺れ
    eye = eye + np.array([math.sin(t * 0.7) * 0.05, math.sin(t * 0.9) * 0.04, 0])
    focus = float(np.linalg.norm(eye - tgt))
    return eye, tgt, fov, focus


def subtitle(t):
    gl = C.gl
    started = [n for n in C.narr if n['t0'] - 0.1 <= t]
    for n in started[-1:]:
        a, b = n['t0'] - 0.1, n['t0'] + n['dur'] + 0.4
        if a <= t <= b:
            al = min(prog(t, a, a + 0.25), 1 - prog(t, b - 0.3, b))
            tex = gl.text(n['disp'], 40, fam='mincho', weight=None, color=(1, 0.97, 0.9), stroke=5, stroke_color=(0.15, 0.08, 0.2))
            gl.sprite(tex, 960, 1000, alpha=al)


# ---------------------------------------------------------------- 音（オルゴール編曲と語り）
def music_box():
    """主題歌の Bメロ＋サビの旋律を 112BPM のオルゴールで（伴奏はコードの分散和音）"""
    sys.path.insert(0, os.path.join(E.REPO, 'Doc', 'spec', 'tools', 'promo-song'))
    import song
    tl, total = song.build()
    n = int(DURATION * SR)
    out = np.zeros((n, 2), np.float32)
    ratio = (60 / 150) / BEAT         # 元の拍 → 新しい拍
    lead_in = 2 * BAR                 # 前奏 2 小節
    t_axis = np.arange(int(3.0 * SR)) / SR

    def bell(f, amp, dur=2.6, bright=1.0):
        m = int(min(dur, 3.0) * SR)
        tt = t_axis[:m]
        mod = np.sin(2 * np.pi * f * 3.5 * tt) * np.exp(-tt / 0.08) * 2.2 * bright
        s = np.sin(2 * np.pi * f * tt + mod) * np.exp(-tt / (0.9 if f < 800 else 0.6))
        s += 0.25 * np.sin(2 * np.pi * f * 2.0 * tt) * np.exp(-tt / 0.3)
        a = int(0.002 * SR)
        s[:a] *= np.linspace(0, 1, a)
        return s * amp

    def put(sig, t0, pan=0.0):
        a = int(t0 * SR)
        if a >= n:
            return
        m = min(len(sig), n - a)
        out[a:a + m, 0] += sig[:m] * (1 - max(0, pan))
        out[a:a + m, 1] += sig[:m] * (1 + min(0, pan))
    secs = {s['sec']['name']: s for s in tl}
    order = ['Bメロ1', 'サビ1']
    cur = lead_in
    for name in order:
        s = secs[name]
        for e in s['events']:
            if e['kind'] != 'vocal':
                continue
            tt = cur + (e['t'] - s['t']) * ratio
            f = 440 * 2 ** ((e['note'] + 12 - 69) / 12)
            put(bell(f, 0.22), tt, 0.1)
        # 伴奏: 各小節のコードを分散和音で
        for bi, ch in enumerate(s['chords']):
            tones = song.chord_tones(ch, s['shift'], lo=55)
            for k in range(4):
                for j, nn in enumerate(tones[:3]):
                    tt = cur + bi * BAR + k * BEAT + j * BEAT / 3
                    f = 440 * 2 ** ((nn - 69) / 12)
                    put(bell(f, 0.06 + 0.02 * (k == 0)), tt, -0.25 + 0.25 * j)
            root = tones[0] - 12
            put(bell(440 * 2 ** ((root - 69) / 12), 0.09, 2.0, 0.5), cur + bi * BAR, 0.0)
        cur += s['bars'] * BAR
    # 前奏: サビ頭の旋律をゆっくり
    for k, nn in enumerate([76, 79, 84, 83, 79, 76, 74, 72]):
        put(bell(440 * 2 ** ((nn - 69) / 12), 0.18), 0.3 + k * BEAT, 0.0)
    # 終わり（最後の和音を長く）
    for j, nn in enumerate([60, 64, 67, 72, 76]):
        put(bell(440 * 2 ** ((nn - 69) / 12), 0.15, 3.0), cur + j * 0.08, -0.3 + 0.15 * j)
    out = core.reverb(out, seconds=2.8, wet=0.35, damp=7000)
    return out, cur


def audio():
    global C
    if C is None:
        C = Ctx()
        C.narr = []
        for key, t0, text, disp in NARR:
            a, moras = voice.say(text, 16, speed=1.18, intonation=1.15, pitch=-0.01, pause=0.45)
            C.narr.append(dict(t0=t0, dur=len(a) / SR, audio=a, disp=disp))
    n = int(DURATION * SR)
    mb, end_t = music_box()
    mb = mb[:n]
    vox = np.zeros((n, 2), np.float32)
    for nr in C.narr:
        a = int(nr['t0'] * SR)
        x = nr['audio']
        m = min(len(x), n - a)
        vox[a:a + m] += x[:m]
    vox = core.reverb(vox, seconds=1.2, wet=0.12, damp=5000)
    env = np.abs(vox[:, 0])
    from scipy.ndimage import maximum_filter1d, uniform_filter1d
    env = maximum_filter1d(uniform_filter1d(env, 960), int(0.3 * SR))
    duck = 1 - 0.45 * np.clip(env / 0.05, 0, 1)
    sfx = np.zeros((n, 2), np.float32)
    # ページをめくる音（紙のこすれ）
    for tt in [OPEN[0]] + TURNS:
        w = core.bandpass(core.noise(0.9, int(tt * 10)), 1500, 9000) * np.hanning(int(0.9 * SR)) * 0.25
        a = int((tt + 0.1) * SR)
        sfx[a:a + len(w), 0] += w; sfx[a:a + len(w), 1] += w * 0.8
    mix = mb * duck[:, None] * 0.9 + vox * 1.0 + sfx
    rms = np.sqrt((mix ** 2).mean())
    mix *= min(10 ** (-18 / 20) / (rms + 1e-9), 6.0)
    lim = 10 ** (-1.5 / 20)
    mix = np.where(np.abs(mix) > 0.7 * lim, np.sign(mix) * (0.7 * lim + 0.3 * lim * np.tanh((np.abs(mix) - 0.7 * lim) / (0.3 * lim))), mix)
    fo = int(1.5 * SR)
    mix[-fo:] *= np.linspace(1, 0, fo)[:, None]
    return mix.astype(np.float32)
