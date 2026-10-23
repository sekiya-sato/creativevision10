# アニメ背景調の積雲を作る（球を積み重ねた高さ場から法線を求め、塊ごとに陰影が付くようにする）
# 出力は陰影の要素（R=光の当たり、G=塊の境目の暗さ、B=縁、A=形）。色は描画時にシェーダで付ける。
import os, math
import numpy as np, cv2
import engine as E

DIR = os.path.join(E.WORK, 'clouds')


def _fbm(h, w, seed, scales=(64, 32, 16, 8)):
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32)
    amp = 1.0
    for s in scales:
        g = rng.standard_normal((h // s + 3, w // s + 3)).astype(np.float32)
        out += cv2.resize(g, (w + 3 * s, h + 3 * s), interpolation=cv2.INTER_CUBIC)[:h, :w] * amp
        amp *= 0.5
    return out / 2.0


def cumulus(seed, W=1400, H=760, tower=1.0, flat=0.80):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    base = H * flat
    blobs = []
    # 底の大きな塊
    n0 = rng.integers(5, 8)
    xs = np.linspace(W * 0.16, W * 0.84, n0) + rng.normal(0, W * 0.02, n0)
    for x in xs:
        r = rng.uniform(0.13, 0.2) * H * 1.5
        blobs.append((x, base - r * rng.uniform(0.2, 0.4), r))
    # 上へ積み上げる塔
    nt = max(3, int(rng.integers(6, 11) * tower))
    cx = W * rng.uniform(0.38, 0.62)
    for i in range(nt):
        k = i / max(nt - 1, 1)
        r = lerp(H * 0.24, H * 0.1, k) * rng.uniform(0.85, 1.15)
        x = cx + rng.normal(0, W * 0.08) * (1 - 0.5 * k)
        y = base - H * 0.22 - k * H * 0.46 * tower + rng.normal(0, H * 0.03)
        blobs.append((x, y, r))
    # 表面のこぶを2段で（大きい塊の上側の縁に沿って）
    def bumps(src, cnt, rs):
        out = []
        for (bx, by, br) in src:
            for j in range(int(rng.integers(*cnt))):
                a = rng.uniform(math.pi * 1.0, math.pi * 2.0)
                rr = br * rng.uniform(*rs)
                out.append((bx + math.cos(a) * br * 0.9, by + math.sin(a) * br * 0.9, rr))
        return out
    b1 = bumps(blobs, (4, 8), (0.25, 0.42))
    b2 = bumps(b1, (2, 4), (0.3, 0.5))
    # 球の高さ場をなめらかな和（soft max）で合成し、石の継ぎ目のような折れを防ぐ
    k = 14.0
    acc = np.zeros((H, W), np.float32)
    for (bx, by, br) in blobs + b1 + b2:
        d2 = (xx - bx) ** 2 + (yy - by) ** 2
        h = np.sqrt(np.maximum(br * br - d2, 0))
        acc += np.where(d2 < br * br, np.exp(np.clip(h / k, -20, 30)), 0.0)
    inside = acc > 0
    height = np.where(inside, k * np.log(np.maximum(acc, 1e-9)), 0.0)
    height += _fbm(H, W, seed + 9, (64, 32, 12)) * 3.0 * inside    # 綿のような揺らぎ
    cutline = base + _fbm(1, W, seed + 5, (64, 16))[0] * 10
    soft_bottom = np.clip((cutline[None, :] - yy) / 26.0, 0, 1)
    hf = np.where(inside, height, 0).astype(np.float32)
    hs = cv2.GaussianBlur(hf, (0, 0), 2.5)
    gx = cv2.Sobel(hs, cv2.CV_32F, 1, 0, ksize=3) / 8
    gy = cv2.Sobel(hs, cv2.CV_32F, 0, 1, ksize=3) / 8
    n = np.stack([-gx, -gy, np.ones_like(gx) * 1.3], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    L = np.array([0.35, -0.8, 0.55], np.float32); L /= np.linalg.norm(L)
    lam = np.clip((n * L).sum(-1), 0, 1)
    crease = np.clip((cv2.GaussianBlur(hf, (0, 0), 8) - hf) / 22.0, 0, 1)
    vert = np.clip((yy - (base - H * 0.3)) / (H * 0.3), 0, 1)
    lam = lam * (1 - 0.5 * vert)
    a = inside.astype(np.float32) * soft_bottom
    a = cv2.GaussianBlur(a, (0, 0), 1.0)
    edge = np.clip(1 - cv2.GaussianBlur(a, (0, 0), 6), 0, 1) * a
    out = np.stack([lam, crease, edge, a], -1)
    ys, xs = np.where(a > 0.01)
    y0, y1 = max(ys.min() - 8, 0), min(ys.max() + 8, H)
    x0, x1 = max(xs.min() - 8, 0), min(xs.max() + 8, W)
    return out[y0:y1, x0:x1]


def stratus(seed, W=2400, H=260):
    """地平線付近の薄く横に長い雲"""
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    n = _fbm(H, W, seed, (128, 48, 16, 6))
    prof = np.exp(-((yy - H * 0.5) / (H * 0.28)) ** 2)
    d = n * 0.9 + prof * 1.1 - 0.95
    a = np.clip(d * 6, 0, 1)
    lam = np.clip(0.55 + (n - cv2.GaussianBlur(n, (0, 0), 3)) * 3 - (yy / H - 0.4) * 0.8, 0, 1)
    crease = np.zeros_like(a)
    edge = np.clip(1 - cv2.GaussianBlur(a, (0, 0), 3), 0, 1) * a
    return np.stack([lam, crease, edge, a], -1)


def lerp(a, b, k):
    return a + (b - a) * k


def build():
    os.makedirs(DIR, exist_ok=True)
    for i in range(8):
        p = os.path.join(DIR, f'cu{i}.png')
        if not os.path.exists(p):
            c = cumulus(100 + i, tower=0.6 + 0.15 * (i % 4))
            cv2.imwrite(p, (np.clip(c[..., [2, 1, 0, 3]], 0, 1) * 65535).astype(np.uint16))
    for i in range(3):
        p = os.path.join(DIR, f'st{i}.png')
        if not os.path.exists(p):
            c = stratus(200 + i)
            cv2.imwrite(p, (np.clip(c[..., [2, 1, 0, 3]], 0, 1) * 65535).astype(np.uint16))


CLOUD_FS = E.HEADER + r'''
uniform sampler2D tex; uniform vec3 cShade, cMid, cLit, cRim; uniform float alpha, rimAmt, haze; uniform vec3 hazeCol;
void main(){
    vec4 s = texture(tex, uv);
    float a = s.a;
    if(a <= 0.001){ f = vec4(0); return; }
    float l = s.r;
    // 3階調をなめらかにつないだアニメ調の陰影
    vec3 col = mix(cShade, cMid, smoothstep(0.18, 0.32, l));
    col = mix(col, cLit, smoothstep(0.55, 0.7, l));
    col *= 1.0 - 0.35*s.g;
    col += cRim*s.b*rimAmt*smoothstep(0.3, 0.8, l + 0.3);
    col = mix(col, hazeCol, haze);
    f = vec4(col*a, a)*alpha;
}
'''


class Clouds:
    def __init__(self, gl):
        build()
        self.gl = gl
        self.cu = [self._load(f'cu{i}.png') for i in range(8)]
        self.st = [self._load(f'st{i}.png') for i in range(3)]

    def _load(self, name):
        m = cv2.imread(os.path.join(DIR, name), cv2.IMREAD_UNCHANGED)[..., [2, 1, 0, 3]].astype(np.float32) / 65535
        return self.gl.texture(m, key=('cloud', name), premul=False)

    def draw(self, tex, Hm, pal, alpha=1.0, haze=0.0):
        if Hm is None:
            return
        self.gl.draw(CLOUD_FS, Hm, dict(cShade=pal['shade'], cMid=pal['mid'], cLit=pal['lit'], cRim=pal['rim'],
                                        rimAmt=pal.get('rimAmt', 1.0), alpha=alpha, haze=haze, hazeCol=pal['haze']),
                     dict(tex=tex))


PALETTES = {
    'dawn': dict(shade=E.hexc('5b4a8a'), mid=E.hexc('b9789e'), lit=E.hexc('ffd1b3'), rim=E.hexc('fff0d0'), haze=E.hexc('d79aa8')),
    'day': dict(shade=E.hexc('7393cc'), mid=E.hexc('c3d4ee'), lit=E.hexc('fbfdff'), rim=E.hexc('ffffff'), haze=E.hexc('a9cdf5'), rimAmt=0.4),
    'sunrise': dict(shade=E.hexc('6e4f96'), mid=E.hexc('d9819f'), lit=E.hexc('ffd6b0'), rim=E.hexc('fff3d6'), haze=E.hexc('e8909a')),
    'night': dict(shade=E.hexc('141a3e'), mid=E.hexc('2b3772'), lit=E.hexc('5f73b8'), rim=E.hexc('b8c8ff'), haze=E.hexc('1c2550')),
}
