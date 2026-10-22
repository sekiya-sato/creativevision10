# 動画4「GENESIS 3D」: v1 を裸眼立体視（左右並び SBS）にした版
#  0.0-10.0  立体視ガイド（寄り目の練習用の点・回転する球と輪・手順）
# 10.0-62.0  v1 本編（52 秒）を左右の目ごとに描画
#
# 出力は 1920x1080 の中に 960x540 の左右画像を並べ、上下の黒帯にガイド文を置く。
# 既定は交差法（左の枠に右目用の画像）。平行法は環境変数 V4_STEREO=parallel。
# 視差は 3D 点の奥行き z から直接決める（収束距離 Zc で 0、奥は DFAR へ漸近、手前は DNEAR で頭打ち）。
# 文字など 2D 要素は blit / fill_rect を差し替えて、場面ごとの POP（負＝手前）だけ左右にずらす。
import os, math
import numpy as np
import core
from core import *
import v1

MODE = os.environ.get('V4_STEREO', 'cross')
OFFSET = 10.0
DURATION = OFFSET + v1.DURATION
DFAR, DNEAR = 56.0, 72.0   # 描画解像度（1920 幅）での最大視差 px。出力は半分
PW, PH = W // 2, H // 2
PY = (H - PH) // 2

EYE = 0      # -1: 左目 / +1: 右目
POP = 0.0    # 2D 要素の視差（負＝手前）

def disparity(z, zc):
    """奥行き z → 視差 px（正＝画面より奥）"""
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

def _stereo_camera(t):
    base = _camera(t)
    zc = float(np.linalg.norm(base.pos)) if t > 13 else 14.0
    return StereoCamera(base, EYE, zc)

def _blit(dst, src, x, y, alpha=1.0, anchor='center', mode='over', tint=None):
    core.blit(dst, src, x + EYE * POP / 2, y, alpha, anchor, mode, tint)

def _fill_rect(frame, x0, y0, x1, y1, color, alpha=1.0):
    s = EYE * POP / 2
    core.fill_rect(frame, x0 + s, y0, x1 + s, y1, color, alpha)

_draw_label = v1.draw_label

def _label(frame, text, x, y, size=26, *a, **kw):
    # 出力では各目が半分の大きさになるため、小さい注記だけ少し大きくする
    return _draw_label(frame, text, x, y, int(size * 1.2) if size <= 24 else size, *a, **kw)

v1.camera = _stereo_camera
v1.blit = _blit
v1.fill_rect = _fill_rect
v1.draw_label = _label

def _pop(p):
    global POP
    POP = p

def main_eye(t):
    """v1.render と同じ合成順で、場面ごとに 2D 要素の浮き出し量を変える"""
    frame = v1.background(t)
    cam = v1.camera(t)
    if t < 4.6:
        beat = sum(math.exp(-max(0, t - b) / 0.25) * (t >= b) for b in (1.0, 2.2, 3.4))
        r = 0.6 + 2.2 * beat + 6 * prog(t, 4.0, 4.5) ** 3
        dot = circle_rgba(max(1, int(r * 2)) / 2 + 0.5, (1, 1, 1))
        layer = np.zeros((H, W, 3), np.float32)
        _pop(-30 * beat - 60 * prog(t, 3.6, 4.5) ** 2)   # 鼓動ごとに手前へ
        _blit(layer, dot, W / 2, H / 2, 1.0, mode='add')
        frame += glow_layer(layer, 18, 3.0 + 4 * beat) * (0.5 + 0.8 * beat + prog(t, 0.0, 1.0) * 0.5)
    v1.render_particles(t, cam, frame)
    _pop(-14); v1.arch_overlay(frame, t, cam)
    _pop(-14); v1.globe_overlay(frame, t, cam)
    _pop(-48 + 20 * ease_out_cubic(prog(t % 1.9, 0, 0.6)) if 34 <= t < 42.3 else -36); v1.stats_overlay(frame, t)
    _pop(-22); v1.logo_overlay(frame, t)
    _pop(-24); v1.hud(frame, t)
    _pop(0)
    v1.flash(frame, t)
    bloom(frame, 0.7, 0.55)
    v1.anamorphic(frame, 0.35)
    frame = chroma(frame, 1.5 + 4 * sum(math.exp(-max(0, t - s) / 0.15) * (t >= s) for s in (4.5, 14.0, 45.6)))
    vignette(frame, 0.5)
    frame = tonemap(frame)
    v1.letterbox(frame, t)
    grain(frame, t, 0.012)
    return frame * (1 - prog(t, 51.2, 52.0))

# ---------------------------------------------------------------- ガイド
def _fib(n, r):
    i = np.arange(n) + 0.5
    phi = np.arccos(1 - 2 * i / n)
    th = math.pi * (1 + 5 ** 0.5) * i
    return np.stack([np.cos(th) * np.sin(phi), np.cos(phi), np.sin(th) * np.sin(phi)], 1).astype(np.float32) * r

G_SPHERE = _fib(2600, 3.0)
_a = np.linspace(0, 2 * math.pi, 2400, endpoint=False)
G_RING = np.stack([np.cos(_a) * 6.2, np.zeros_like(_a), np.sin(_a) * 6.2], 1).astype(np.float32)

def guide_eye(t):
    frame = v1.background(10.0)
    base = Camera((0, 0.6, -14), fov=50).look_at((0, -0.3, 0))
    cam = StereoCamera(base, EYE, 14.0)
    layer = np.zeros((H, W, 3), np.float32)
    S = G_SPHERE @ (rot_x(0.35) @ rot_y(t * 0.45)).T
    Rg = G_RING @ (rot_z(-0.25) @ rot_x(0.42) @ rot_y(-t * 0.3)).T
    for P, col, wt in ((S, v1.C_TEAL, 1.6), (Rg, v1.C_WARM, 1.4)):
        x, y, z = cam.project(P)
        k = np.clip(15.0 / z, 0.5, 2.0)
        shade = 0.35 + 0.65 * np.clip((16.5 - z) / 5.0, 0, 1)   # 手前ほど明るく
        splat_fast(layer, x, y, col, wt * k * shade)
    frame += glow_layer(layer, 6, 1.4)
    # 寄り目の目印（画面上の点、視差 0）
    mk = np.zeros((H, W, 3), np.float32)
    blit(mk, circle_rgba(9, (1, 1, 1)), W / 2, 120, 1.0, mode='add')
    blit(mk, circle_rgba(20, (1, 1, 1), ring=2), W / 2, 120, 0.7, mode='add')
    frame += glow_layer(mk, 8, 1.2)
    # 枠（視差 0）
    for x0, y0, x1, y1 in ((40, 40, W - 40, 43), (40, H - 43, W - 40, H - 40), (40, 40, 43, H - 40), (W - 43, 40, W - 40, H - 40)):
        fill_rect(frame, x0, y0, x1, y1, (1, 1, 1), 0.35)
    _pop(-28)
    _label(frame, '立体に見えたら準備完了', W / 2, H - 150, 52, fam='noto', weight='Light', color=(255, 255, 255), tracking=0.18)
    _pop(0)
    bloom(frame, 0.7, 0.5)
    vignette(frame, 0.45)
    return tonemap(frame)

def guide_text(out, t, a):
    if MODE == 'parallel':
        title = '立体視ガイド ―― 平行法（遠くを見るように）'
        steps = ['1. 画面から40〜60cm離れ、左右2枚の映像の正面に座ります',
                 '2. 遠くを眺めるように目の力を抜き、上の2つの白い点が重なって1つになるのを待ちます',
                 '3. 白い点が3つに見え、中央の球と輪が浮き上がって見えたら成功です']
        hint = 'ヒント: 映像が大きすぎると重なりません。ウィンドウを小さくして左右の点の間隔を6cm以下にしてください'
        sub = 'Creative Vision 10  ·  3D STEREO EDITION  ·  PARALLEL-VIEW SBS'
    else:
        title = '立体視ガイド ―― 交差法（寄り目）'
        steps = ['1. 画面から40〜60cm離れ、左右2枚の映像の境目の正面に座ります',
                 '2. 寄り目にして上の白い点を左右から近づけ、真ん中で1つに重ねます',
                 '3. 白い点が3つに見え、中央の球と輪が浮き上がって見えたら成功です']
        hint = 'ヒント: 画面と目の間に指先を立てて見つめると寄り目にしやすくなります。疲れたら目を休めてください'
        sub = 'Creative Vision 10  ·  3D STEREO EDITION  ·  CROSS-EYED SBS'
    core.blit(out, text_arr(title, 40, fam='noto', weight='Medium', tracking=0.12), W / 2, 70, a)
    for i, s in enumerate(steps):
        core.blit(out, text_arr(s, 25, fam='noto', weight='Light', color=(225, 230, 255), tracking=0.04), 470, 140 + i * 42, a, 'left')
    core.blit(out, text_arr(hint, 21, fam='noto', weight='Light', color=(190, 200, 230), tracking=0.03), W / 2, PY + PH + 50, a * 0.9)
    left = OFFSET - t
    if left <= 5.0:
        n = int(math.ceil(left - 1e-6))
        if n >= 1:
            core.blit(out, text_arr(f'START  {n}', 40, fam='segoesl', weight=None, color=(170, 230, 235), tracking=0.3),
                      W / 2, PY + PH + 125, a * (0.6 + 0.4 * (left % 1.0)))
    core.blit(out, text_arr(sub, 18, fam='segoel', weight=None, color=(150, 160, 200), tracking=0.25), W / 2, H - 50, a * 0.8)

# ---------------------------------------------------------------- 合成
def eye_frame(t, eye):
    global EYE
    EYE = eye
    try:
        return guide_eye(t) if t < OFFSET else main_eye(t - OFFSET)
    finally:
        EYE = 0
        _pop(0)

def render(t):
    out = new_frame((0, 0, 0))
    L = downsample(eye_frame(t, -1), 2)
    R = downsample(eye_frame(t, +1), 2)
    a, b = (R, L) if MODE != 'parallel' else (L, R)
    out[PY:PY + PH, :PW] = a
    out[PY:PY + PH, PW:PW * 2] = b
    if t < OFFSET:
        k = prog(t, 0.0, 0.8) * (1 - prog(t, OFFSET - 0.8, OFFSET))
        out[PY:PY + PH] *= k
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
