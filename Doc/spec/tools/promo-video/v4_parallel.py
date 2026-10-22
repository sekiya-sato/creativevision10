# 動画4「GENESIS 3D」平行法版: v1 を裸眼立体視（平行法の左右並び）にした版
#  0.0-10.0  立体視ガイド（重ねる目印・回転する球と輪・手順）
# 10.0-62.0  v1 本編（52 秒）を左右の目ごとに描画
#
# 平行法は左右画像の中心間隔を目の幅より狭くする必要があるため、片目の画像は幅 160px（約 4cm）が上限。
# 24 型 1920x1080・視距離 60cm のテスト（stereo_test.py）で、間隔 163px と 181px が正しく立体に見えたため 172px にした。
# 片目の画像は v1 の描画（1920x1080）の中央を縦長に切り出して縮小し、v1 の小さな文字は消して下に字幕を付ける。
# 全画面・等倍（拡大縮小なし）で再生する前提で、1920x1080 の中央に小さく置く。
import os, math, datetime
import numpy as np
from PIL import Image
import core
from core import *
import v1

OFFSET = 10.0
DURATION = OFFSET + v1.DURATION
SEP = 172                      # 左右画像の中心間隔 px（出力）
PW, IH = 160, 284              # 片目の画像 幅・高さ px（出力、9:16）
CAP_H = 76                     # 字幕の高さ px
PH = IH + CAP_H
CW = int(round(H * PW / IH))   # 描画からの切り出し幅（1080 高のまま）
SC = H / IH                    # 描画 px → 出力 px の縮小率
X0 = (W - SEP - PW) // 2
Y0 = (H - PH) // 2
DFAR, DNEAR = 20 * SC, 24 * SC   # 最大視差（出力で奥 20px・手前 24px。テストの範囲内）

EYE = 0      # -1: 左目 / +1: 右目
POP = 0.0    # 2D 要素の視差（描画 px、負＝手前）

def disparity(z, zc):
    """奥行き z → 視差 px（正＝画面より奥）。収束距離 zc で 0、奥は DFAR へ漸近、手前は DNEAR で頭打ち"""
    z = np.where(np.isfinite(z) & (z > 0.05), z, np.inf)
    r = zc / z
    far = DFAR * (1 - r)
    near = -DNEAR * np.tanh((r - 1) * DFAR / DNEAR)
    return np.where(r <= 1, far, near)

class StereoCamera(Camera):
    def __init__(self, base, eye, zc):
        self.pos, self.R, self.f, self.cx = base.pos, base.R, base.f, base.cx
        self.eye, self.zc = eye, zc

    def project(self, P):
        x, y, z = Camera.project(self, P)
        return x + self.eye * disparity(z, self.zc) / 2, y, z

# ---------------------------------------------------------------- v1 の差し替え
_camera = v1.camera
ORIGIN = np.zeros((1, 3), np.float32)

def _stereo_camera(t):
    base = _camera(t)
    if t > 13:
        # v1 は文字を置くため被写体を左右に寄せている。縦長に切り出すので原点を中央へ戻す
        x0 = base.project(ORIGIN)[0][0]
        if np.isfinite(x0):
            base.cx += (W / 2 - x0) * smooth(prog(t, 13.0, 15.0))
    zc = float(np.linalg.norm(base.pos)) if t > 13 else 14.0
    return StereoCamera(base, EYE, zc)

def _blit(dst, src, x, y, alpha=1.0, anchor='center', mode='over', tint=None):
    core.blit(dst, src, x + EYE * POP / 2, y, alpha, anchor, mode, tint)

v1.camera = _stereo_camera
v1.blit = _blit
v1.draw_label = lambda *a, **k: None      # v1 の文字は縮小後に読めないため字幕へ置き換える
v1.fill_rect = lambda *a, **k: None
v1.letterbox = lambda *a, **k: None
v1.PBOOST = 2.6 * 1.6                     # 縮小で粒子が暗くなる分を持ち上げる

def _pop(p):
    global POP
    POP = p

def main_eye(t):
    frame = v1.background(t)
    cam = v1.camera(t)
    if t < 4.6:
        beat = sum(math.exp(-max(0, t - b) / 0.25) * (t >= b) for b in (1.0, 2.2, 3.4))
        r = 0.6 + 2.2 * beat + 6 * prog(t, 4.0, 4.5) ** 3
        dot = circle_rgba(max(1, int(r * 4)) / 2 + 0.5, (1, 1, 1))
        layer = np.zeros((H, W, 3), np.float32)
        _pop(-(8 * beat + 14 * prog(t, 3.6, 4.5) ** 2) * SC)   # 鼓動ごとに手前へ
        _blit(layer, dot, W / 2, H / 2, 1.0, mode='add')
        frame += glow_layer(layer, 24, 3.0 + 4 * beat) * (0.5 + 0.8 * beat + prog(t, 0.0, 1.0) * 0.5)
    v1.render_particles(t, cam, frame)
    _pop(0); v1.arch_overlay(frame, t, cam)
    _pop(-14 * SC); v1.stats_overlay(frame, t)
    _pop(0)
    v1.flash(frame, t)
    bloom(frame, 0.7, 0.55)
    frame = tonemap(frame)
    return frame * (1 - prog(t, 51.2, 52.0))

# ---------------------------------------------------------------- ガイド
def _fib(n, r):
    i = np.arange(n) + 0.5
    phi = np.arccos(1 - 2 * i / n)
    th = math.pi * (1 + 5 ** 0.5) * i
    return np.stack([np.cos(th) * np.sin(phi), np.cos(phi), np.sin(th) * np.sin(phi)], 1).astype(np.float32) * r

G_SPHERE = _fib(2600, 2.6)
_a = np.linspace(0, 2 * math.pi, 2400, endpoint=False)
G_RING = np.stack([np.cos(_a) * 4.2, np.zeros_like(_a), np.sin(_a) * 4.2], 1).astype(np.float32)

def guide_eye(t):
    frame = v1.background(10.0)
    base = Camera((0, 0.4, -14), fov=50).look_at((0, -0.6, 0))
    cam = StereoCamera(base, EYE, 14.0)
    layer = np.zeros((H, W, 3), np.float32)
    S = G_SPHERE @ (rot_x(0.35) @ rot_y(t * 0.45)).T
    Rg = G_RING @ (rot_z(-0.35) @ rot_x(0.5) @ rot_y(-t * 0.3)).T
    for P, col, wt in ((S, v1.C_TEAL, 3.0), (Rg, v1.C_WARM, 3.0)):
        x, y, z = cam.project(P)
        k = np.clip(15.0 / z, 0.5, 2.0)
        shade = 0.35 + 0.65 * np.clip((16.5 - z) / 5.0, 0, 1)   # 手前ほど明るく
        splat_fast(layer, x, y, col, wt * k * shade)
    frame += glow_layer(blur(layer, 1.5), 8, 1.6)
    bloom(frame, 0.7, 0.5)
    return tonemap(frame)

# ---------------------------------------------------------------- 字幕（左右の画像の下に置き、一緒に重ねて見る）
def captions(t):
    """本編時刻 t → [(文字, 大きさ, 色, 不透明度)]"""
    tm = t
    out = []
    def put(a, lines):
        for s, size, col in lines:
            out.append((s, size, col, a))
    if 0.6 < tm < 5.2:
        put(prog(tm, 0.8, 1.6) * (1 - prog(tm, 4.4, 5.2)), [('2025.11.25', 17, (255, 255, 255)), ('initial commit', 13, (200, 205, 255))])
    if 4.8 < tm < 11.2:
        u = v1.cursor_u(tm)
        d = datetime.datetime.fromtimestamp(v1.CTS[0] + (v1.CTS[-1] - v1.CTS[0]) * u)
        cnt = int(np.searchsorted(v1.CU, u, side='right'))
        put(prog(tm, 4.8, 5.6) * (1 - prog(tm, 10.4, 11.2)), [(d.strftime('%Y.%m.%d'), 17, (255, 255, 255)), (f'COMMITS {cnt:,d}', 13, (170, 200, 255))])
    if 10.8 < tm < 14.4:
        put(prog(tm, 10.8, 11.6) * (1 - prog(tm, 13.6, 14.4)), [('11ヶ月、1,801回の', 15, (255, 255, 255)), ('積み重ね。', 15, (255, 255, 255))])
    if 16.8 < tm < 23.8:
        put(prog(tm, 16.8, 17.8) * (1 - prog(tm, 22.8, 23.8)), [('3層アーキテクチャ', 15, (255, 255, 255)), ('HTTP/2 + gRPC', 13, (190, 210, 255))])
    if 26.0 < tm < 33.4:
        put(prog(tm, 26.0, 27.0) * (1 - prog(tm, 32.6, 33.4)), [('アパレル販売管理の、', 15, (255, 255, 255)), ('すべてを。', 15, (255, 255, 255)), ('17 domains', 12, (190, 210, 255))])
    for i, (s, v, en, ja) in enumerate(v1.STATS):
        e = s + 1.9 if i < 3 else 42.3
        if s <= tm < e + 0.3:
            put(prog(tm, s, s + 0.25) * (1 - prog(tm, e - 0.2, e + 0.2)), [(ja, 16, (255, 255, 255)), (en, 12, (170, 210, 255))])
    if tm > 46.2:
        fo = 1 - prog(tm, 50.6, 52.0)
        put(prog(tm, 46.2, 47.4) * fo, [('Creative Vision 10', 16, (255, 255, 255)), ('アパレル販売管理を、', 13, (225, 225, 255)), ('オープンに。', 13, (225, 225, 255))])
    return out

CAP_POP = -4   # 字幕の視差（出力 px、少し手前）

def draw_captions(panel, items, eye):
    """panel: (PH, PW, 3)。同じ時刻の字幕は縦に並べる"""
    if not items:
        return
    y = IH + 8
    tot = sum(int(sz * 1.45) for _, sz, _, _ in items)
    y += max(0, (CAP_H - 8 - tot) // 2)
    for s, sz, col, a in items:
        im = text_arr(s, sz, fam='noto', weight='Regular', color=col, tracking=0.02)
        if im.shape[1] > PW - 4:
            im = resize_rgba(im, (PW - 4) / im.shape[1])
        core.blit(panel, im, PW / 2 + eye * CAP_POP / 2, y + int(sz * 0.72), a)
        y += int(sz * 1.45)

# ---------------------------------------------------------------- 合成
def eye_image(t, eye):
    global EYE
    EYE = eye
    try:
        f = guide_eye(t) if t < OFFSET else main_eye(t - OFFSET)
    finally:
        EYE = 0
        _pop(0)
    x0 = (W - CW) // 2
    crop = (np.clip(f[:, x0:x0 + CW], 0, 1) * 255 + 0.5).astype(np.uint8)
    im = Image.fromarray(crop, 'RGB').resize((PW, IH), Image.LANCZOS)
    panel = np.zeros((PH, PW, 3), np.float32)
    panel[:IH] = np.asarray(im, np.float32) / 255
    return panel

def guide_marks(panel, a):
    """重ねる目印（十字）と枠。視差 0"""
    cx, cy = PW // 2, 18
    panel[cy - 1:cy + 2, cx - 9:cx + 10] = panel[cy - 1:cy + 2, cx - 9:cx + 10] * (1 - a) + a
    panel[cy - 9:cy + 10, cx - 1:cx + 2] = panel[cy - 9:cy + 10, cx - 1:cx + 2] * (1 - a) + a
    c = 0.62 * a
    for sl in ((slice(0, 2), slice(None)), (slice(IH - 2, IH), slice(None)), (slice(0, IH), slice(0, 2)), (slice(0, IH), slice(PW - 2, PW))):
        panel[sl] = panel[sl] * (1 - c) + np.asarray((0.7, 0.7, 0.85), np.float32) * c

def guide_text(out, t, a):
    title = '立体視ガイド ―― 平行法（遠くを見るように）'
    steps = ['1. 全画面・等倍で再生し、画面から約60cm離れて中央の2枚の映像の正面に座ります',
             '2. 画面の向こう側を眺めるように目の力を抜き、上の白い十字が1つに重なるのを待ちます',
             '3. 十字が3つ並んで見え、中央の球と輪が浮き上がって見えたら、そのまま中央を見続けてください']
    core.blit(out, text_arr(title, 34, fam='noto', weight='Medium', tracking=0.1), W / 2, 70, a)
    for i, s in enumerate(steps):
        core.blit(out, text_arr(s, 22, fam='noto', weight='Light', color=(225, 230, 255), tracking=0.03), W / 2, 130 + i * 38, a)
    hint = 'ヒント: 画面に鼻を近づけてから、ゆっくり顔を離すと重なりやすくなります。疲れたら目を休めてください'
    core.blit(out, text_arr(hint, 20, fam='noto', weight='Light', color=(190, 200, 230), tracking=0.03), W / 2, Y0 + PH + 70, a * 0.9)
    left = OFFSET - t
    if left <= 5.0:
        n = int(math.ceil(left - 1e-6))
        if n >= 1:
            core.blit(out, text_arr(f'START  {n}', 36, fam='segoesl', weight=None, color=(170, 230, 235), tracking=0.3),
                      W / 2, Y0 + PH + 150, a * (0.6 + 0.4 * (left % 1.0)))
    core.blit(out, text_arr('Creative Vision 10  ·  3D STEREO EDITION  ·  PARALLEL-VIEW', 18, fam='segoel', weight=None,
                            color=(150, 160, 200), tracking=0.25), W / 2, H - 50, a * 0.8)

def render(t):
    out = new_frame((0, 0, 0))
    guide = t < OFFSET
    items = [] if guide else captions(t - OFFSET)
    if guide:
        items = [('立体に見えたら', 15, (255, 255, 255), 1.0), ('準備完了', 15, (255, 255, 255), 1.0)]
    for i, eye in enumerate((-1, +1)):
        p = eye_image(t, eye)
        draw_captions(p, items, eye)
        if guide:
            k = prog(t, 0.0, 0.8) * (1 - prog(t, OFFSET - 0.8, OFFSET))
            guide_marks(p, 1.0)
            p *= k
        x = X0 + i * SEP
        out[Y0:Y0 + PH, x:x + PW] = p
    if guide:
        guide_text(out, t, prog(t, 0.2, 1.0) * (1 - prog(t, OFFSET - 0.6, OFFSET)))
    return out

# ---------------------------------------------------------------- 音
def audio():
    D = DURATION
    m = Mix(OFFSET + 1)
    m.add(0.0, pad([50, 57, 62, 65], OFFSET, cutoff=700, attack=2.0, release=2.5, seed=31), 0.5)
    m.add(0.0, pad([38, 45], OFFSET, cutoff=300, attack=2.0, release=2.5, seed=32), 0.4)
    for k in range(5, 0, -1):
        m.add(OFFSET - k, tick(1600, 0.05), 0.35)
    g = m.buf[:int(OFFSET * SR)]
    g = g / (np.max(np.abs(g)) + 1e-9) * 0.32
    main = v1.audio()
    x = np.zeros((int(D * SR), 2), np.float32)
    x[:len(g)] += g
    i = int(OFFSET * SR)
    n = min(len(main), len(x) - i)
    x[i:i + n] += main[:n]
    return np.clip(x, -1, 1)
