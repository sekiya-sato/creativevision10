# プロモーション動画用の最小レンダリングエンジン（numpy + Pillow + ffmpeg、動画生成AI不使用）
# 使い方は同じフォルダの README.md を参照
import os, math, wave, functools, subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage

W, H, FPS = 1920, 1080, 30
SR = 48000
HERE = os.path.dirname(os.path.abspath(__file__))


def _find_repo(d):
    while not os.path.exists(os.path.join(d, 'creativevision10.slnx')):
        p = os.path.dirname(d)
        if p == d:
            raise RuntimeError('creativevision10.slnx が見つかりません')
        d = p
    return d

REPO = _find_repo(HERE)
# 素材・出力は容量が大きいため .gitignore 対象の Doc/promo 配下に置く（PROMO_DIR で変更可）
PROMO = os.environ.get('PROMO_DIR', os.path.join(REPO, 'Doc', 'promo'))
ASSETS = os.path.join(PROMO, 'assets')
OUT = os.path.join(PROMO, 'out')
FONTS = 'C:/Windows/Fonts/'

# ---------------------------------------------------------------- 時間・補間
def clamp(x, a=0.0, b=1.0):
    return a if x < a else b if x > b else x

def prog(t, a, b):
    """t が a→b の間で 0→1"""
    if b <= a:
        return 1.0 if t >= b else 0.0
    return clamp((t - a) / (b - a))

def lerp(a, b, k):
    return a + (b - a) * k

def ease_out_cubic(k): return 1 - (1 - k) ** 3
def ease_in_cubic(k): return k ** 3
def ease_in_out_cubic(k): return 4 * k ** 3 if k < .5 else 1 - (-2 * k + 2) ** 3 / 2
def ease_out_expo(k): return 1.0 if k >= 1 else 1 - 2 ** (-10 * k)
def ease_in_expo(k): return 0.0 if k <= 0 else 2 ** (10 * k - 10)
def ease_in_out_expo(k):
    if k <= 0: return 0.0
    if k >= 1: return 1.0
    return 2 ** (20 * k - 10) / 2 if k < .5 else (2 - 2 ** (-20 * k + 10)) / 2
def ease_out_back(k, s=1.70158): return 1 + (s + 1) * (k - 1) ** 3 + s * (k - 1) ** 2
def smooth(k): return k * k * (3 - 2 * k)
def spring(k, f=4.5, d=6.0):
    """オーバーシュートして収まるばね"""
    if k <= 0: return 0.0
    return 1 - math.exp(-d * k) * math.cos(f * math.pi * k)

def hexc(h, a=None):
    h = h.lstrip('#')
    c = np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], np.float32)
    return c

# ---------------------------------------------------------------- フォント・文字
_FONTFILES = {
    'noto': 'NotoSansJP-VF.ttf', 'mincho': 'yumindb.ttf', 'minchol': 'yuminl.ttf',
    'segoe': 'segoeui.ttf', 'segoel': 'segoeuil.ttf', 'segoesl': 'segoeuisl.ttf', 'segoeb': 'segoeuib.ttf',
    'mono': 'CascadiaMono.ttf', 'bahn': 'bahnschrift.ttf', 'yugo': 'YuGothB.ttc', 'yugol': 'YuGothL.ttc',
}

@functools.lru_cache(maxsize=256)
def font(fam, size, weight=None):
    f = ImageFont.truetype(FONTS + _FONTFILES[fam], int(size))
    if weight:
        try:
            f.set_variation_by_name(weight)
        except Exception:
            pass
    return f

@functools.lru_cache(maxsize=2048)
def text_img(text, size, fam='noto', weight='Bold', color=(255, 255, 255), tracking=0.0, stroke=0):
    """文字列を RGBA 画像化（tracking は em 比の字間）"""
    f = font(fam, size, weight)
    asc, desc = f.getmetrics()
    tr = tracking * size
    widths = [f.getlength(ch) for ch in text]
    tw = int(sum(widths) + tr * max(len(text) - 1, 0) + size * 0.2 + stroke * 2)
    th = int(asc + desc + size * 0.1 + stroke * 2)
    im = Image.new('L', (max(tw, 1), max(th, 1)), 0)
    d = ImageDraw.Draw(im)
    x = size * 0.1 + stroke
    for ch, w in zip(text, widths):
        d.text((x, stroke), ch, font=f, fill=255, stroke_width=stroke, stroke_fill=255)
        x += w + tr
    bbox = im.getbbox() or (0, 0, 1, 1)
    # 縦方向はベースライン基準のまま、横だけ詰める
    im = im.crop((bbox[0], 0, bbox[2], th))
    rgba = Image.new('RGBA', im.size, tuple(color) + (0,))
    rgba.putalpha(im)
    return rgba

def text_arr(text, size, **kw):
    return pil_to_rgba(text_img(text, size, **kw))

# ---------------------------------------------------------------- 画像処理
def pil_to_rgba(im):
    a = np.asarray(im.convert('RGBA'), np.float32) / 255.0
    return a

@functools.lru_cache(maxsize=512)
def load_image(path, maxw=None):
    im = Image.open(path).convert('RGBA')
    if maxw and im.width > maxw:
        im = im.resize((maxw, int(im.height * maxw / im.width)), Image.LANCZOS)
    return im

def new_frame(color=(0, 0, 0)):
    f = np.empty((H, W, 3), np.float32)
    f[:] = np.asarray(color, np.float32)
    return f

def blit(dst, src, x, y, alpha=1.0, anchor='center', mode='over', tint=None):
    """src: (h,w,4) float RGBA（ストレート α）。x,y は anchor 基準の位置"""
    if alpha <= 0.001:
        return
    h, w = src.shape[:2]
    if anchor == 'center':
        x0, y0 = int(round(x - w / 2)), int(round(y - h / 2))
    elif anchor == 'left':
        x0, y0 = int(round(x)), int(round(y - h / 2))
    elif anchor == 'right':
        x0, y0 = int(round(x - w)), int(round(y - h / 2))
    else:  # topleft
        x0, y0 = int(round(x)), int(round(y))
    dh, dw = dst.shape[:2]
    sx0, sy0 = max(0, -x0), max(0, -y0)
    ex, ey = min(w, dw - x0), min(h, dh - y0)
    if ex <= sx0 or ey <= sy0:
        return
    s = src[sy0:ey, sx0:ex]
    rgb = s[..., :3] if tint is None else np.broadcast_to(np.asarray(tint, np.float32), s[..., :3].shape)
    a = s[..., 3:4] * alpha
    region = dst[y0 + sy0:y0 + ey, x0 + sx0:x0 + ex]
    if mode == 'add':
        region += rgb * a
    elif mode == 'screen':
        region[:] = 1 - (1 - region) * (1 - rgb * a)
    else:
        region *= (1 - a)
        region += rgb * a

def resize_rgba(arr, scale):
    if abs(scale - 1) < 1e-3:
        return arr
    h, w = arr.shape[:2]
    nw, nh = max(1, int(w * scale)), max(1, int(h * scale))
    im = Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8), 'RGBA')
    return pil_to_rgba(im.resize((nw, nh), Image.LANCZOS if scale < 1 else Image.BICUBIC))

def pil_scaled(im, scale, angle=0.0):
    """PIL RGBA を拡大縮小・回転して float RGBA に"""
    nw, nh = max(1, int(im.width * scale)), max(1, int(im.height * scale))
    im2 = im.resize((nw, nh), Image.LANCZOS if scale < 1 else Image.BICUBIC)
    if angle:
        im2 = im2.rotate(angle, Image.BICUBIC, expand=True)
    return pil_to_rgba(im2)

def blur(arr, sigma):
    if sigma <= 0.3:
        return arr
    return ndimage.gaussian_filter(arr, (sigma, sigma, 0) if arr.ndim == 3 else sigma, mode='constant')

def downsample(arr, k):
    h, w = arr.shape[:2]
    return arr[:h - h % k, :w - w % k].reshape(h // k, k, w // k, k, -1).mean((1, 3))

def upsample(arr, size):
    h, w = size
    im = [Image.fromarray(arr[..., c].astype(np.float32), 'F').resize((w, h), Image.BILINEAR) for c in range(arr.shape[2])]
    return np.stack([np.asarray(i, np.float32) for i in im], -1)

def bloom(frame, threshold=0.6, strength=0.8, radii=(4, 12, 32)):
    """明部抽出 → 縮小ガウス多段 → 加算"""
    bright = np.clip(frame - threshold, 0, None)
    small = downsample(bright, 4)
    acc = np.zeros_like(small)
    for r in radii:
        acc += blur(small, r / 4)
    frame += upsample(acc, frame.shape[:2]) * (strength / len(radii)) * 2.2
    return frame

def glow_layer(layer, sigma=10, strength=1.0):
    """layer（加算用 RGB）をぼかして足し戻す"""
    small = downsample(layer, 2)
    return layer + upsample(blur(small, sigma / 2), layer.shape[:2]) * strength

def vignette(frame, amount=0.45, power=2.2):
    yy, xx = grid()
    d = np.sqrt(((xx - W / 2) / (W / 2)) ** 2 + ((yy - H / 2) / (H / 2)) ** 2) / math.sqrt(2)
    frame *= (1 - amount * d ** power)[..., None]
    return frame

@functools.lru_cache(maxsize=1)
def grid():
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    return yy, xx

def chroma(frame, px=2.0):
    """色収差: R と B を中心から逆方向へずらす"""
    if px < 0.3:
        return frame
    out = frame.copy()
    s = int(round(px))
    out[:, s:, 0] = frame[:, :-s, 0]
    out[:, :-s, 2] = frame[:, s:, 2]
    return out

def grain(frame, t, amount=0.025):
    rng = np.random.default_rng(int(t * 1000) + 7)
    n = rng.standard_normal((H // 2, W // 2), dtype=np.float32)
    n = np.repeat(np.repeat(n, 2, 0), 2, 1)
    frame += n[..., None] * amount
    return frame

def tonemap(frame):
    """ハイライトを柔らかく丸める（白飛び防止）"""
    return np.where(frame < 0.8, frame, 0.8 + 0.2 * np.tanh((frame - 0.8) / 0.2))

def to_u8(frame):
    return (np.clip(frame, 0, 1) * 255 + 0.5).astype(np.uint8)

# ---------------------------------------------------------------- 点描（アンチエイリアス付き加算スプラット）
def splat(layer, xs, ys, cols, weights=None):
    """xs,ys: float 座標, cols: (N,3) or (3,), layer に双線形で加算"""
    h, w = layer.shape[:2]
    m = (xs >= 0) & (xs < w - 1) & (ys >= 0) & (ys < h - 1)
    xs, ys = xs[m], ys[m]
    c = np.broadcast_to(np.asarray(cols, np.float32), (len(m), 3))[m] if np.ndim(cols) > 1 else np.tile(np.asarray(cols, np.float32), (len(xs), 1))
    if weights is not None:
        c = c * np.asarray(weights, np.float32)[m][:, None]
    x0, y0 = xs.astype(np.int32), ys.astype(np.int32)
    fx, fy = (xs - x0)[:, None], (ys - y0)[:, None]
    flat = layer.reshape(-1, 3)
    for dx, dy, wt in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
        idx = (y0 + dy) * w + (x0 + dx)
        np.add.at(flat, idx, c * wt)
    return layer

def splat_fast(layer, xs, ys, cols, weights=None):
    """bincount 版（大量点向け）"""
    h, w = layer.shape[:2]
    xs = np.asarray(xs, np.float32); ys = np.asarray(ys, np.float32)
    m = (xs >= 0) & (xs < w - 1) & (ys >= 0) & (ys < h - 1)
    xs, ys = xs[m], ys[m]
    n = len(xs)
    if n == 0:
        return layer
    cols = np.asarray(cols, np.float32)
    c = cols[m] if cols.ndim == 2 else np.broadcast_to(cols, (n, 3))
    if weights is not None:
        c = c * np.asarray(weights, np.float32)[m][:, None]
    x0, y0 = xs.astype(np.int32), ys.astype(np.int32)
    fx, fy = xs - x0, ys - y0
    size = h * w
    for dx, dy, wt in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
        idx = (y0 + dy) * w + (x0 + dx)
        for ch in range(3):
            layer[..., ch] += np.bincount(idx, weights=c[:, ch] * wt, minlength=size).reshape(h, w)
    return layer

def line_points(p0, p1, step=0.7):
    """2D 線分を点列化"""
    d = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
    n = max(2, int(d / step))
    k = np.linspace(0, 1, n, dtype=np.float32)
    return p0[0] + (p1[0] - p0[0]) * k, p0[1] + (p1[1] - p0[1]) * k

# ---------------------------------------------------------------- 3D
def rot_y(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]], np.float32)

def rot_x(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]], np.float32)

def rot_z(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]], np.float32)

class Camera:
    def __init__(self, pos=(0, 0, -10), yaw=0.0, pitch=0.0, roll=0.0, fov=50.0):
        self.pos = np.asarray(pos, np.float32)
        self.R = (rot_z(roll) @ rot_x(pitch) @ rot_y(yaw)).astype(np.float32)
        self.f = (H / 2) / math.tan(math.radians(fov) / 2)
        self.cx = W / 2

    def look_at(self, target, up=(0, 1, 0)):
        fwd = np.asarray(target, np.float32) - self.pos
        fwd /= np.linalg.norm(fwd)
        right = np.cross(np.asarray(up, np.float32), fwd); right /= np.linalg.norm(right)
        upv = np.cross(fwd, right)
        self.R = np.stack([right, upv, fwd]).astype(np.float32)
        return self

    def project(self, P):
        """P: (N,3) → x,y,z（z はカメラ前方距離）。y は上向き正→画面下向きへ反転"""
        c = (np.asarray(P, np.float32) - self.pos) @ self.R.T
        z = c[:, 2]
        zs = np.where(z > 0.05, z, np.nan)
        x = self.cx + c[:, 0] * self.f / zs
        y = H / 2 - c[:, 1] * self.f / zs
        return x, y, z

def quad_perspective(im, quad, alpha=1.0):
    """PIL RGBA im を画面上の四角形 quad[(x,y)x4: 左上,右上,右下,左下] へ射影変換した (arr, x0, y0)"""
    q = np.asarray(quad, np.float64)
    if not np.all(np.isfinite(q)):
        return None
    x0, y0 = np.floor(q.min(0)).astype(int)
    x1, y1 = np.ceil(q.max(0)).astype(int)
    x0c, y0c, x1c, y1c = max(x0, -2), max(y0, -2), min(x1, W + 2), min(y1, H + 2)
    if x1c - x0c < 2 or y1c - y0c < 2 or (x1 - x0) > W * 6:
        return None
    src = [(0, 0), (im.width, 0), (im.width, im.height), (0, im.height)]
    dst = [(px - x0c, py - y0c) for px, py in q]
    A, B = [], []
    for (sx, sy), (dx, dy) in zip(src, dst):
        A.append([dx, dy, 1, 0, 0, 0, -sx * dx, -sx * dy]); B.append(sx)
        A.append([0, 0, 0, dx, dy, 1, -sy * dx, -sy * dy]); B.append(sy)
    try:
        coeffs = np.linalg.solve(np.array(A), np.array(B))
    except np.linalg.LinAlgError:
        return None
    out = im.transform((x1c - x0c, y1c - y0c), Image.PERSPECTIVE, tuple(coeffs), Image.BILINEAR)
    return pil_to_rgba(out), x0c, y0c

# ---------------------------------------------------------------- 図形
def rect_rgba(w, h, color, radius=0, alpha=1.0, outline=None, width=2):
    im = Image.new('RGBA', (int(w) * 2, int(h) * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    col = tuple(int(c * 255) for c in color) + (int(alpha * 255),)
    if outline:
        d.rounded_rectangle((0, 0, im.width - 1, im.height - 1), radius * 2, outline=col, width=width * 2)
    else:
        d.rounded_rectangle((0, 0, im.width - 1, im.height - 1), radius * 2, fill=col)
    return pil_to_rgba(im.resize((int(w), int(h)), Image.LANCZOS))

@functools.lru_cache(maxsize=64)
def circle_rgba(r, color=(1, 1, 1), ring=0):
    s = int(r * 2 + 4)
    im = Image.new('L', (s * 4, s * 4), 0)
    d = ImageDraw.Draw(im)
    c = s * 2
    if ring:
        d.ellipse((c - r * 4, c - r * 4, c + r * 4, c + r * 4), outline=255, width=int(ring * 4))
    else:
        d.ellipse((c - r * 4, c - r * 4, c + r * 4, c + r * 4), fill=255)
    im = im.resize((s, s), Image.LANCZOS)
    a = np.asarray(im, np.float32) / 255
    out = np.zeros((s, s, 4), np.float32)
    out[..., :3] = color
    out[..., 3] = a
    return out

def fill_rect(frame, x0, y0, x1, y1, color, alpha=1.0):
    x0, y0 = max(0, int(x0)), max(0, int(y0))
    x1, y1 = min(W, int(x1)), min(H, int(y1))
    if x1 <= x0 or y1 <= y0:
        return
    frame[y0:y1, x0:x1] = frame[y0:y1, x0:x1] * (1 - alpha) + np.asarray(color, np.float32) * alpha

def poly_mask(points, ss=2):
    """多角形のアンチエイリアス付きマスク (H,W) float"""
    im = Image.new('L', (W * ss // 2, H * ss // 2), 0)
    d = ImageDraw.Draw(im)
    k = ss / 2
    d.polygon([(x * k, y * k) for x, y in points], fill=255)
    if ss != 2:
        pass
    im = im.resize((W, H), Image.BILINEAR)
    return np.asarray(im, np.float32) / 255

# ---------------------------------------------------------------- 音声合成
def t_axis(dur):
    return np.arange(int(dur * SR), dtype=np.float32) / SR

def osc(freq, dur, kind='sine', phase=0.0):
    t = t_axis(dur)
    if callable(freq):
        ph = 2 * np.pi * np.cumsum(freq(t)) / SR + phase
    else:
        ph = 2 * np.pi * freq * t + phase
    if kind == 'sine':
        return np.sin(ph)
    if kind == 'saw':
        return 2 * ((ph / (2 * np.pi)) % 1.0) - 1
    if kind == 'square':
        return np.sign(np.sin(ph))
    if kind == 'tri':
        return 2 * np.abs(2 * ((ph / (2 * np.pi)) % 1.0) - 1) - 1
    raise ValueError(kind)

def env_adsr(n, a=0.01, d=0.1, s=0.7, r=0.2, sr=SR):
    e = np.ones(n, np.float32) * s
    na, nd, nr = int(a * sr), int(d * sr), int(r * sr)
    na = min(na, n); e[:na] = np.linspace(0, 1, na, endpoint=False)
    nd = min(nd, n - na); e[na:na + nd] = np.linspace(1, s, nd, endpoint=False)
    if nr > 0 and nr < n:
        e[n - nr:] *= np.linspace(1, 0, nr)
    return e

def env_exp(n, decay, sr=SR):
    return np.exp(-np.arange(n, dtype=np.float32) / sr / decay)

def noise(dur, seed=0):
    return np.random.default_rng(seed).standard_normal(int(dur * SR)).astype(np.float32)

from scipy import signal as _sig

def lowpass(x, fc, order=2):
    b, a = _sig.butter(order, min(fc / (SR / 2), 0.99), 'low')
    return _sig.lfilter(b, a, x).astype(np.float32)

def highpass(x, fc, order=2):
    b, a = _sig.butter(order, min(fc / (SR / 2), 0.99), 'high')
    return _sig.lfilter(b, a, x).astype(np.float32)

def bandpass(x, lo, hi, order=2):
    b, a = _sig.butter(order, [lo / (SR / 2), min(hi / (SR / 2), 0.99)], 'band')
    return _sig.lfilter(b, a, x).astype(np.float32)

def sweep_lowpass(x, f0, f1, block=256):
    """時間変化するローパス（ブロック単位の状態保持 1 次 x2）"""
    out = np.empty_like(x)
    n = len(x)
    y1 = y2 = 0.0
    for i in range(0, n, block):
        k = i / max(n - 1, 1)
        fc = f0 * (f1 / f0) ** k
        a = 1 - math.exp(-2 * math.pi * fc / SR)
        seg = x[i:i + block]
        b = [a], [1, -(1 - a)]
        s1, _ = _sig.lfilter(b[0], b[1], seg, zi=[y1 * (1 - a)])
        y1 = s1[-1] if len(s1) else y1
        s2, _ = _sig.lfilter(b[0], b[1], s1, zi=[y2 * (1 - a)])
        y2 = s2[-1] if len(s2) else y2
        out[i:i + block] = s2
    return out

@functools.lru_cache(maxsize=4)
def _reverb_ir(seconds, seed, damp):
    n = int(seconds * SR)
    rng = np.random.default_rng(seed)
    ir = rng.standard_normal((n, 2)).astype(np.float32)
    tt = np.arange(n, dtype=np.float32) / SR
    ir *= np.exp(-tt * (6.9 / seconds))[:, None]
    ir[:, 0] = lowpass(ir[:, 0], damp); ir[:, 1] = lowpass(ir[:, 1], damp)
    ir[: int(0.008 * SR)] *= np.linspace(0, 1, int(0.008 * SR))[:, None]
    return ir / np.sqrt((ir ** 2).sum(0))

def reverb(stereo, seconds=2.5, wet=0.3, damp=6000, seed=3):
    ir = _reverb_ir(seconds, seed, damp)
    out = np.stack([_sig.fftconvolve(stereo[:, c], ir[:, c])[:len(stereo)] for c in range(2)], 1)
    return stereo * (1 - wet) + out.astype(np.float32) * wet * 2.5

class Mix:
    def __init__(self, dur):
        self.buf = np.zeros((int(dur * SR) + SR, 2), np.float32)

    def add(self, t, sig, gain=1.0, pan=0.0):
        if t < 0:
            sig = sig[int(-t * SR):]; t = 0
        i = int(t * SR)
        if sig.ndim == 1:
            l, r = math.cos((pan + 1) * math.pi / 4), math.sin((pan + 1) * math.pi / 4)
            sig = np.stack([sig * l * 1.414, sig * r * 1.414], 1)
        n = min(len(sig), len(self.buf) - i)
        if n > 0:
            self.buf[i:i + n] += sig[:n] * gain

    def master(self, dur, limit=0.92):
        x = self.buf[:int(dur * SR)]
        x = x - x.mean(0)
        # ソフトリミッター
        peak = np.max(np.abs(x)) + 1e-9
        x = x / peak * 1.25
        x = np.tanh(x) / np.tanh(1.25) * limit
        fo = int(0.02 * SR)
        x[:fo] *= np.linspace(0, 1, fo)[:, None]
        return x

def write_wav(path, stereo):
    data = (np.clip(stereo, -1, 1) * 32767).astype('<i2')
    with wave.open(path, 'wb') as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(data.tobytes())

# 楽器
def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)

def kick(dur=0.5, f0=150, f1=42, decay=0.32, click=0.6):
    t = t_axis(dur)
    f = f1 + (f0 - f1) * np.exp(-t / 0.035)
    ph = 2 * np.pi * np.cumsum(f) / SR
    s = np.sin(ph) * np.exp(-t / decay)
    s[:int(0.004 * SR)] += noise(0.004, 1) * click * np.linspace(1, 0, int(0.004 * SR))
    return np.tanh(s * 1.6)

def snare(dur=0.3, seed=2):
    t = t_axis(dur)
    body = np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.05) * 0.6
    nz = bandpass(noise(dur, seed), 1200, 9000) * np.exp(-t / 0.09)
    return body + nz * 1.2

def clap(dur=0.35, seed=4):
    t = t_axis(dur)
    nz = bandpass(noise(dur, seed), 900, 6000)
    e = np.zeros_like(t)
    for k, off in enumerate((0, 0.011, 0.022)):
        i = int(off * SR)
        e[i:] += np.exp(-(t[i:] - off) / (0.01 if k < 2 else 0.12))
    return nz * e * 1.4

def hat(dur=0.08, open_=False, seed=5):
    t = t_axis(dur if not open_ else 0.35)
    nz = highpass(noise(len(t) / SR, seed), 7000)
    return nz * np.exp(-t / (0.025 if not open_ else 0.12)) * 0.7

def pluck(note, dur=0.6, bright=4000, decay=0.25, detune=0.004):
    f = midi(note)
    s = osc(f, dur, 'saw') + osc(f * (1 + detune), dur, 'saw') * 0.7 + osc(f * 2, dur, 'sine') * 0.25
    t = t_axis(dur)
    fc_env = 200 + bright * np.exp(-t / (decay * 0.5))
    # 近似: 2 段の固定ローパスをエンベロープでクロスフェード
    lo, hi = lowpass(s, 400), lowpass(s, bright)
    mixk = (fc_env - 200) / bright
    out = lo * (1 - mixk) + hi * mixk
    return out * np.exp(-t / decay) * env_adsr(len(t), 0.003, 0.0, 1.0, 0.03)

def pad(notes, dur, cutoff=1800, attack=1.5, release=1.5, voices=3, seed=0):
    out = np.zeros(int(dur * SR), np.float32)
    rng = np.random.default_rng(seed)
    for n in notes:
        for v in range(voices):
            det = (v - (voices - 1) / 2) * 0.006
            out += osc(midi(n) * (1 + det), dur, 'saw', rng.uniform(0, 6.28))
    out = lowpass(out / (len(notes) * voices), cutoff, 2)
    return out * env_adsr(len(out), attack, 0.0, 1.0, release)

def sub_boom(dur=3.0, f0=70, f1=28, decay=1.0):
    t = t_axis(dur)
    f = f1 + (f0 - f1) * np.exp(-t / 0.25)
    s = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / decay)
    nz = lowpass(noise(dur, 9), 300) * np.exp(-t / 0.3) * 0.6
    return np.tanh((s + nz) * 1.5)

def riser(dur=3.0, f0=200, f1=4000, seed=11):
    t = t_axis(dur)
    nz = noise(dur, seed)
    s = sweep_lowpass(nz, f0, f1)
    s = s / (np.max(np.abs(s)) + 1e-9)
    tone = osc(lambda tt: 110 * 2 ** (tt / dur * 2), dur, 'saw') * 0.15
    e = (t / dur) ** 2
    return (s + lowpass(tone, 3000)) * e

def whoosh(dur=0.8, f0=300, f1=3000, seed=13):
    nz = noise(dur, seed)
    s = sweep_lowpass(nz, f0, f1)
    s /= (np.max(np.abs(s)) + 1e-9)
    t = t_axis(dur)
    e = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 2
    return s * e

def tick(f=3200, dur=0.03):
    t = t_axis(dur)
    return np.sin(2 * np.pi * f * t) * np.exp(-t / 0.006)

def glitch(dur=0.25, seed=17):
    rng = np.random.default_rng(seed)
    n = int(dur * SR)
    out = np.zeros(n, np.float32)
    i = 0
    while i < n:
        L = int(rng.uniform(0.004, 0.03) * SR)
        f = rng.uniform(200, 4000)
        seg = osc(f, L / SR, rng.choice(['square', 'saw']))[:n - i]
        out[i:i + len(seg)] = seg * rng.uniform(0.2, 1)
        i += L
    return out * 0.5

# ---------------------------------------------------------------- 出力
def ffmpeg_exe():
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()
