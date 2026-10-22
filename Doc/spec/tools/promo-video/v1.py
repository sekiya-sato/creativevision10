# 動画1「GENESIS」: 実コミット履歴 1,801 点から始まる粒子のシネマティック
#  0.0- 4.5  闇の中の一点（initial commit）
#  4.5-14.0  コミット二重螺旋を飛翔（実タイムスタンプ）
# 14.0-24.0  3 層アーキテクチャへ変形、gRPC パケットが往来
# 24.0-34.0  業務領域の地球儀
# 34.0-42.0  数字（銀河を背景に）
# 42.0-52.0  ロゴへ収束
import os, math, functools
import numpy as np
from PIL import Image
from core import *

DURATION = 52.0
PBOOST = 2.6
N = 24000
NC = 1801
RNG = np.random.default_rng(42)

C_VIO = hexc('#7C5CFF'); C_PUR = hexc('#9B6BFF'); C_TEAL = hexc('#5FD6D8'); C_WARM = hexc('#FFE9C7'); C_WHITE = np.ones(3, np.float32)
C_PINK = hexc('#FF7AD9')

# ---------------------------------------------------------------- 編成（各粒子の目標座標と色）
def _commit_u():
    ts = np.loadtxt(os.path.join(ASSETS, 'commits.txt'), dtype=np.float64)
    ts = np.sort(ts)
    u = (ts - ts[0]) / (ts[-1] - ts[0])
    return u.astype(np.float32), ts

CU, CTS = _commit_u()
HELIX_L = 70.0
STRAND_U = np.full(N, 2.0, np.float32)
RUNG_U = np.full(N, 2.0, np.float32)

def form_helix():
    P = np.zeros((N, 3), np.float32); C = np.zeros((N, 3), np.float32); B = np.zeros(N, np.float32)
    u = CU
    strand = (np.arange(NC) % 2).astype(np.float32)
    ang = u * 2 * math.pi * 9.0 + strand * math.pi + RNG.normal(0, 0.05, NC)
    r = 3.2 + RNG.normal(0, 0.12, NC)
    P[:NC, 0] = np.cos(ang) * r
    P[:NC, 1] = np.sin(ang) * r
    P[:NC, 2] = (u - 0.5) * HELIX_L + RNG.normal(0, 0.08, NC)
    C[:NC] = np.where(strand[:, None] > 0, C_TEAL, C_PUR)
    B[:NC] = 1.0
    # 横木（ラング）
    nr = 5000
    k = RNG.uniform(0, 1, nr).astype(np.float32)
    ru = (np.floor(RNG.uniform(0, 1, nr) * 220) / 220).astype(np.float32)
    ra = ru * 2 * math.pi * 9.0
    P[NC:NC + nr, 0] = np.cos(ra) * 3.2 * (2 * k - 1)
    P[NC:NC + nr, 1] = np.sin(ra) * 3.2 * (2 * k - 1)
    P[NC:NC + nr, 2] = (ru - 0.5) * HELIX_L
    C[NC:NC + nr] = C_VIO * 0.8 + C_TEAL * 0.2
    B[NC:NC + nr] = 0.18
    # ストランドの連続光
    nst = 7000
    su = RNG.uniform(0, 1, nst).astype(np.float32)
    sst = RNG.integers(0, 2, nst)
    sa = su * 2 * math.pi * 9.0 + sst * math.pi
    sr = 3.2 + RNG.normal(0, 0.18, nst)
    o = NC + nr
    P[o:o + nst, 0] = np.cos(sa) * sr; P[o:o + nst, 1] = np.sin(sa) * sr; P[o:o + nst, 2] = (su - 0.5) * HELIX_L
    C[o:o + nst] = np.where(sst[:, None] > 0, C_TEAL, C_PUR)
    B[o:o + nst] = 0.3
    STRAND_U[:] = 2.0
    STRAND_U[o:o + nst] = su
    RUNG_U[:] = 2.0
    RUNG_U[NC:NC + nr] = ru
    # 星雲
    s0 = NC + nr + nst
    ns = N - s0
    nh = ns // 2
    rr = np.abs(RNG.normal(0, 7, nh)) + 4
    aa = RNG.uniform(0, 2 * math.pi, nh)
    P[s0:s0 + nh, 0] = np.cos(aa) * rr
    P[s0:s0 + nh, 1] = np.sin(aa) * rr
    P[s0:s0 + nh, 2] = RNG.uniform(-0.5, 0.5, nh) * HELIX_L * 1.2
    C[s0:s0 + nh] = np.where(RNG.uniform(0, 1, (nh, 1)) < 0.5, C_VIO, C_TEAL * 0.7 + C_VIO * 0.3)
    B[s0:s0 + nh] = 0.12
    v = RNG.normal(0, 1, (N - s0 - nh, 3)); v /= np.linalg.norm(v, axis=1, keepdims=True)
    P[s0 + nh:] = v * RNG.uniform(60, 140, (len(v), 1))
    C[s0 + nh:] = C_WARM
    B[s0 + nh:] = 0.35
    return P, C, B

PLANE_Y = (6.5, 0.0, -6.5)
def form_arch():
    P = np.zeros((N, 3), np.float32); C = np.zeros((N, 3), np.float32); B = np.zeros(N, np.float32)
    cols = (C_PUR, C_TEAL, C_VIO * 0.6 + C_WARM * 0.4)
    nper = 5200
    idx = 0
    for pi_, y in enumerate(PLANE_Y):
        g = 52
        gx, gz = np.meshgrid(np.linspace(-11, 11, g), np.linspace(-11, 11, g))
        gx, gz = gx.ravel(), gz.ravel()
        n = min(nper, len(gx))
        sel = RNG.permutation(len(gx))[:n]
        P[idx:idx + n, 0] = gx[sel]; P[idx:idx + n, 2] = gz[sel]; P[idx:idx + n, 1] = y
        # 縁取り
        C[idx:idx + n] = cols[pi_]
        edge = (np.abs(gx[sel]) > 10.5) | (np.abs(gz[sel]) > 10.5)
        B[idx:idx + n] = np.where(edge, 1.4, 0.45)
        idx += n
    # 柱（ビーム）
    beams = [(-6, -6), (6, -6), (-6, 6), (6, 6), (0, 0)]
    nb = 700
    for bx, bz in beams:
        P[idx:idx + nb, 0] = bx + RNG.normal(0, 0.05, nb)
        P[idx:idx + nb, 2] = bz + RNG.normal(0, 0.05, nb)
        P[idx:idx + nb, 1] = RNG.uniform(PLANE_Y[2], PLANE_Y[0], nb)
        C[idx:idx + nb] = C_TEAL * 0.6 + C_WHITE * 0.4
        B[idx:idx + nb] = 0.3
        idx += nb
    rest = N - idx
    v = RNG.normal(0, 1, (rest, 3)); v /= np.linalg.norm(v, axis=1, keepdims=True)
    P[idx:] = v * RNG.uniform(40, 120, (rest, 1))
    C[idx:] = C_WARM; B[idx:] = 0.3
    return P, C, B
BEAMS = [(-6, -6), (6, -6), (-6, 6), (6, 6), (0, 0)]

GLOBE_R = 9.0
DOMAINS = ['システム', 'マスタ', '予算', '発注', '受注', '仕入', '売上', '配分', '在庫', '売上分析',
           '卸分析', 'AI照会', 'HHT', '月次', '顧客', '店舗', '物流']
def form_globe():
    P = np.zeros((N, 3), np.float32); C = np.zeros((N, 3), np.float32); B = np.zeros(N, np.float32)
    idx = 0
    # 緯線・経線
    nlat, nlon = 8, 12
    per = 520
    for i in range(1, nlat + 1):
        th = math.pi * i / (nlat + 1)
        a = np.linspace(0, 2 * math.pi, per, endpoint=False)
        P[idx:idx + per] = np.stack([np.cos(a) * math.sin(th), np.full(per, math.cos(th)), np.sin(a) * math.sin(th)], 1) * GLOBE_R
        idx += per
    for j in range(nlon):
        ph = math.pi * j / nlon
        a = np.linspace(0, 2 * math.pi, per, endpoint=False)
        P[idx:idx + per] = np.stack([np.cos(ph) * np.sin(a), np.cos(a), np.sin(ph) * np.sin(a)], 1) * GLOBE_R
        idx += per
    n_grid = idx
    yy = P[:n_grid, 1] / GLOBE_R
    C[:n_grid] = C_TEAL[None] * ((yy + 1) / 2)[:, None] + C_PUR[None] * ((1 - yy) / 2)[:, None]
    B[:n_grid] = 0.45
    # 軌道リング（ロゴの軌道を意識）
    nr = 2600
    a = np.linspace(0, 2 * math.pi, nr, endpoint=False)
    ring = np.stack([np.cos(a) * 13.5, np.zeros(nr), np.sin(a) * 13.5], 1) @ (rot_x(1.15) @ rot_z(-0.5)).T
    P[idx:idx + nr] = ring + RNG.normal(0, 0.06, (nr, 3))
    C[idx:idx + nr] = C_TEAL * 0.8 + C_WHITE * 0.2; B[idx:idx + nr] = 0.4
    idx += nr
    # 領域ノード（光の塊）
    for k in range(len(DOMAINS)):
        n = 120
        P[idx:idx + n] = DOMAIN_POS[k] + RNG.normal(0, 0.18, (n, 3))
        C[idx:idx + n] = C_WARM; B[idx:idx + n] = 0.7
        idx += n
    rest = N - idx
    v = RNG.normal(0, 1, (rest, 3)); v /= np.linalg.norm(v, axis=1, keepdims=True)
    P[idx:] = v * RNG.uniform(30, 120, (rest, 1))
    C[idx:] = np.where(RNG.uniform(0, 1, (rest, 1)) < 0.3, C_VIO, C_WARM); B[idx:] = 0.3
    return P, C, B

def _fib_sphere(n, r):
    i = np.arange(n) + 0.5
    phi = np.arccos(1 - 2 * i / n)
    th = math.pi * (1 + 5 ** 0.5) * i
    return np.stack([np.cos(th) * np.sin(phi), np.cos(phi), np.sin(th) * np.sin(phi)], 1).astype(np.float32) * r
DOMAIN_POS = _fib_sphere(len(DOMAINS), GLOBE_R)

def form_galaxy():
    P = np.zeros((N, 3), np.float32); C = np.zeros((N, 3), np.float32); B = np.zeros(N, np.float32)
    arms = 3
    r = RNG.exponential(9, N).astype(np.float32) + 0.5
    arm = RNG.integers(0, arms, N)
    a = arm * 2 * math.pi / arms + r * 0.32 + RNG.normal(0, 0.25, N) * (1 + 6 / (r + 1)) * 0.4
    P[:, 0] = np.cos(a) * r; P[:, 2] = np.sin(a) * r
    P[:, 1] = RNG.normal(0, 0.35, N) * np.exp(-r / 15) * 2
    k = np.clip(r / 30, 0, 1)[:, None]
    C[:] = C_WARM * (1 - k) * 0.9 + (C_PUR * 0.6 + C_TEAL * 0.4) * k
    B[:] = 0.5 + 0.35 * np.exp(-r / 4)
    return P, C, B

LOGO_SCALE = 0.0215
def form_logo():
    im = Image.open(os.path.join(REPO, 'Doc', 'cv10logo01.png')).convert('RGB')
    a = np.asarray(im, np.float32) / 255
    lum = a.mean(2)
    sat = a.max(2) - a.min(2)
    ink = np.clip((0.93 - lum) * 4, 0, 1) * np.clip(sat * 4 + (lum < 0.6), 0, 1)
    ys, xs = np.nonzero(ink > 0.25)
    w = ink[ys, xs]
    sel = RNG.choice(len(xs), N, p=w / w.sum())
    jx = RNG.uniform(-0.5, 0.5, N); jy = RNG.uniform(-0.5, 0.5, N)
    P = np.zeros((N, 3), np.float32)
    cx, cy = im.width / 2, im.height / 2
    P[:, 0] = (xs[sel] + jx - cx) * LOGO_SCALE
    P[:, 1] = -(ys[sel] + jy - cy) * LOGO_SCALE + 3.9
    P[:, 2] = RNG.normal(0, 0.05, N)
    col = a[ys[sel], xs[sel]]
    # 暗背景向けに発光色へ持ち上げ
    col = np.clip(col / (col.max(1, keepdims=True) + 1e-6), 0, 1)
    col = col * 0.55 + 0.45
    C = col.astype(np.float32)
    B = np.full(N, 0.75, np.float32)
    return P, C, B

@functools.lru_cache(maxsize=1)
def forms():
    return {'helix': form_helix(), 'arch': form_arch(), 'globe': form_globe(), 'galaxy': form_galaxy(), 'logo': form_logo()}

@functools.lru_cache(maxsize=1)
def per_particle():
    delay = RNG.uniform(0, 1, N).astype(np.float32)
    swirl = RNG.normal(0, 1, (N, 3)).astype(np.float32)
    tw = RNG.uniform(0, 2 * math.pi, N).astype(np.float32)
    return delay, swirl, tw

# 変形スケジュール: (開始, 長さ, from, to)
MORPHS = [(13.6, 2.6, 'helix', 'arch'), (23.4, 2.6, 'arch', 'globe'), (33.2, 2.4, 'globe', 'galaxy'), (42.0, 3.4, 'galaxy', 'logo')]

def rot_formation(name, P, t):
    """編成ごとの自転"""
    if name == 'globe':
        return P @ rot_y(t * 0.22).T
    if name == 'galaxy':
        return P @ rot_y(-t * 0.12).T
    if name == 'arch':
        return P @ rot_y(math.sin(t * 0.3) * 0.15).T
    return P

def particles(t):
    F = forms()
    delay, swirl, tw = per_particle()
    cur = 'helix'
    for (s, d, a, b) in MORPHS:
        if t >= s:
            cur = b
    for (s, d, a, b) in MORPHS:
        if s <= t < s + d + 1.0:
            Pa, Ca, Ba = F[a]; Pb, Cb, Bb = F[b]
            Pa = rot_formation(a, Pa, t); Pb = rot_formation(b, Pb, t)
            k = np.clip((t - s - delay * d * 0.6) / (d * 0.4), 0, 1)
            e = k * k * (3 - 2 * k)
            e = e[:, None]
            arc = np.sin(np.pi * e) * 3.2 * (1.8 if b == 'logo' else 1.0)
            P = Pa * (1 - e) + Pb * e + swirl * arc
            C = Ca * (1 - e) + Cb * e
            Bv = Ba * (1 - e[:, 0]) + Bb * e[:, 0] + np.sin(np.pi * e[:, 0]) * 0.25
            return P, C, Bv, (a, b, float(np.mean(e)))
    P, C, Bv = F[cur]
    return rot_formation(cur, P, t), C, Bv, (cur, cur, 1.0)

# ---------------------------------------------------------------- カメラ
def catmull(keys, t):
    """keys: [(t, vec)]"""
    ts = [k[0] for k in keys]
    if t <= ts[0]: return np.asarray(keys[0][1], np.float32)
    if t >= ts[-1]: return np.asarray(keys[-1][1], np.float32)
    i = max(j for j in range(len(ts)) if ts[j] <= t)
    p0 = np.asarray(keys[max(i - 1, 0)][1], np.float32); p1 = np.asarray(keys[i][1], np.float32)
    p2 = np.asarray(keys[i + 1][1], np.float32); p3 = np.asarray(keys[min(i + 2, len(keys) - 1)][1], np.float32)
    u = smooth((t - ts[i]) / (ts[i + 1] - ts[i]))
    u2, u3 = u * u, u * u * u
    return 0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3)

def cursor_u(t):
    return ease_in_out_cubic(prog(t, 5.0, 12.6))

def camera(t):
    if t < 13.0:
        u = cursor_u(t)
        zc = (u - 0.5) * HELIX_L
        a = t * 0.25
        r = 1.2 + 0.6 * prog(t, 4.5, 8)
        pos = np.array([math.cos(a) * r, math.sin(a) * r, zc - 11 + 3 * prog(t, 0, 4.5)], np.float32)
        tgt = np.array([0, 0, zc + 6], np.float32)
        k = smooth(prog(t, 11.4, 13.0))
        pos2 = np.array([46, 14, 4], np.float32); tgt2 = np.array([0, 0, 2], np.float32)
        pos = pos * (1 - k) + pos2 * k; tgt = tgt * (1 - k) + tgt2 * k
        roll = 0.15 * math.sin(t * 0.4) * (1 - k)
        fov = 58 - 8 * k
    else:
        keys_p = [(13.0, (46, 14, 4)), (15.5, (30, 18, -24)), (19.5, (-12, 9, -30)), (23.5, (-26, 3, -14)),
                  (26.0, (-10, 3, -34)), (30.0, (12, 5, -33)), (33.5, (4, 16, -30)),
                  (36.0, (0, 22, -30)), (41.5, (-6, 14, -32)), (44.5, (0, 1.5, -26)), (52.0, (0, 1.8, -23.5))]
        keys_t = [(13.0, (0, 0, 2)), (15.5, (0, 0, 0)), (23.5, (0, 0, 0)), (26.0, (-5, 1.5, 0)), (31.0, (-5, 1.5, 0)), (34.5, (0, 3, 0)), (41.5, (0, 3, 0)),
                  (44.5, (0, 1.6, 0)), (52.0, (0, 1.8, 0))]
        pos = catmull(keys_p, t); tgt = catmull(keys_t, t)
        roll = 0.0
        fov = 50
    cam = Camera(pos, fov=fov).look_at(tgt)
    if roll:
        cam.R = (rot_z(roll) @ cam.R).astype(np.float32)
    return cam

# ---------------------------------------------------------------- 描画
def render_particles(t, cam, frame):
    P, C, Bv, (fa, fb, e) = particles(t)
    delay, swirl, tw = per_particle()
    Bv = Bv.copy()
    # コミット点火
    if t < 16.0:
        u = cursor_u(t)
        lit = CU <= u
        since = np.where(lit, (u - CU) * 7.6, 99)
        flare = np.exp(-since / 0.05) * 3.0
        base = np.where(lit, 0.85, 0.06)
        if t < 4.6:
            base = base * 0
        k_on = 1 - prog(t, 13.6, 16.0)
        Bv[:NC] = (base + flare) * k_on + Bv[:NC] * (1 - k_on)
        if t >= 4.5:
            litS = np.clip((u - STRAND_U[NC:]) * 40, 0, 1)
            Bv[NC:] = Bv[NC:] * (1 - k_on) + Bv[NC:] * k_on * np.where(STRAND_U[NC:] < 1.5, 0.3 + 0.7 * litS, 1.0)
        if t < 4.5:
            # 最初の一点以外は闇
            Bv[NC:] *= prog(t, 4.3, 4.6)
        else:
            Bv[NC:] *= 0.4 + 0.6 * prog(t, 4.5, 7.0)
    Bv *= 0.85 + 0.15 * np.sin(t * 3.0 + tw)
    x, y, z = cam.project(P)
    ok = np.isfinite(x) & (z > 0.3)
    x, y, z, C, Bv = x[ok], y[ok], z[ok], C[ok], Bv[ok]
    att = np.clip(26.0 / (z + 4.0), 0.15, 4.0)
    wgt = Bv * att * PBOOST
    focus = np.linalg.norm(cam.pos) if t > 13 else 14.0
    dz = np.abs(z - focus) / focus
    sharp = dz < 0.35
    layer = np.zeros((H, W, 3), np.float32)
    splat_fast(layer, x[sharp], y[sharp], C[sharp], wgt[sharp] * 0.9)
    half = np.zeros((H // 2, W // 2, 3), np.float32)
    splat_fast(half, x[~sharp] / 2, y[~sharp] / 2, C[~sharp], wgt[~sharp] * 0.9)
    half = blur(half, 1.6)
    layer += upsample(half, (H, W)) * 1.0
    layer = glow_layer(layer, sigma=6, strength=1.6)
    frame += layer
    return P

def draw_label(frame, text, x, y, size=26, fam='noto', weight='Light', color=(255, 255, 255), alpha=1.0, tracking=0.12, anchor='center'):
    blit(frame, text_arr(text, size, fam=fam, weight=weight, color=color, tracking=tracking), x, y, alpha, anchor)

def hud(frame, t):
    # 冒頭タイプライター
    if 0.6 < t < 5.2:
        s = '2025.11.25  initial commit  —  30 files'
        n = int(clamp((t - 0.8) / 2.2) * len(s))
        a = 1 - prog(t, 4.4, 5.2)
        if n:
            draw_label(frame, s[:n] + ('_' if int(t * 4) % 2 == 0 else ''), 160, H - 220, 28, 'mono', None, (220, 220, 255), a * 0.85, 0.02, 'left')
    # 日付とコミット数
    if 4.8 < t < 14.2:
        u = cursor_u(t)
        ts = CTS[0] + (CTS[-1] - CTS[0]) * u
        import datetime
        d = datetime.datetime.fromtimestamp(ts)
        cnt = int(np.searchsorted(CU, u, side='right'))
        a = prog(t, 4.8, 5.6) * (1 - prog(t, 13.4, 14.2))
        draw_label(frame, d.strftime('%Y.%m.%d'), 160, H - 250, 64, 'segoel', None, (255, 255, 255), a, 0.06, 'left')
        draw_label(frame, f'COMMITS  {cnt:5,d}', 166, H - 190, 26, 'mono', None, (170, 200, 255), a * 0.9, 0.08, 'left')
        bw = 520
        fill_rect(frame, 166, H - 160, 166 + bw, H - 158, (1, 1, 1), 0.18 * a)
        fill_rect(frame, 166, H - 160, 166 + bw * u, H - 158, C_TEAL, 0.9 * a)
    if 10.6 < t < 14.4:
        a = prog(t, 10.6, 11.6) * (1 - prog(t, 13.6, 14.4))
        draw_label(frame, '11ヶ月、1,801回の積み重ね。', W - 170, H - 230, 46, 'noto', 'Light', (255, 255, 255), a, 0.14, 'right')
        draw_label(frame, 'Built in the open. Commit by commit.', W - 170, H - 168, 22, 'segoel', None, (190, 200, 255), a * 0.8, 0.2, 'right')

def arch_overlay(frame, t, cam):
    a = prog(t, 15.6, 16.8) * (1 - prog(t, 22.8, 23.8))
    if a <= 0:
        return
    # パケット
    pk = np.zeros((H, W, 3), np.float32)
    R = rot_y(math.sin(t * 0.3) * 0.15)
    for bi, (bx, bz) in enumerate(BEAMS):
        for k in range(6):
            ph = (t * 0.55 + k / 6 + bi * 0.13) % 1.0
            up = (k + bi) % 2 == 0
            yv = PLANE_Y[2] + (PLANE_Y[0] - PLANE_Y[2]) * (ph if up else 1 - ph)
            trail = np.linspace(0, 0.06, 14)
            ys = yv - (trail if up else -trail) * (PLANE_Y[0] - PLANE_Y[2])
            pts = np.stack([np.full(14, bx), ys, np.full(14, bz)], 1) @ R.T
            x, y, z = cam.project(pts)
            col = C_TEAL if up else C_PINK
            wts = np.linspace(2.2, 0.0, 14) * 14 / np.maximum(z, 1)
            splat_fast(pk, x, y, col, wts)
    frame += glow_layer(blur(pk, 0.8), 8, 2.0) * a
    labels = [('CLIENT', 'WPF  ·  MVVM  ·  CommunityToolkit', PLANE_Y[0]),
              ('SERVER', '.NET 10  ·  HTTP/2  ·  gRPC', PLANE_Y[1]),
              ('DATABASE', 'SQLite  ·  PostgreSQL  ·  MariaDB  ·  Oracle', PLANE_Y[2])]
    for i, (h1, h2, yv) in enumerate(labels):
        ai = a * prog(t, 17.0 + i * 0.5, 17.9 + i * 0.5)
        p = np.array([[11.5, yv, -11.0]], np.float32) @ R.T
        x, y, z = cam.project(p)
        if not np.isfinite(x[0]):
            continue
        lx, ly = x[0] + 40, y[0]
        tw2 = text_arr(h2, 20, fam='segoel', weight=None, color=(180, 220, 230), tracking=0.08).shape[1]
        if lx + tw2 > W - 90:
            lx = W - 90 - tw2
        fill_rect(frame, x[0], ly - 0.5, lx - 6, ly + 0.5, (1, 1, 1), 0.6 * ai)
        draw_label(frame, h1, lx, ly - 18, 30, 'segoesl', None, (255, 255, 255), ai, 0.35, 'left')
        draw_label(frame, h2, lx + 2, ly + 20, 20, 'segoel', None, (180, 220, 230), ai * 0.85, 0.08, 'left')
    a2 = prog(t, 17.5, 18.5) * (1 - prog(t, 22.6, 23.4))
    draw_label(frame, '3層アーキテクチャ', 150, 175, 50, 'noto', 'Light', (255, 255, 255), a2, 0.2, 'left')
    draw_label(frame, 'HTTP/2 + gRPC で、速く、安全に。', 152, 238, 26, 'noto', 'Light', (190, 210, 255), a2 * 0.85, 0.12, 'left')

def globe_overlay(frame, t, cam):
    a = prog(t, 25.4, 26.6) * (1 - prog(t, 32.6, 33.4))
    if a <= 0:
        return
    pos = DOMAIN_POS @ rot_y(t * 0.22).T
    x, y, z = cam.project(pos)
    cd = np.linalg.norm(cam.pos)
    order = np.argsort(-z)
    for i in order:
        if not np.isfinite(x[i]):
            continue
        front = clamp((cd - z[i]) / GLOBE_R * 0.9 + 0.55)
        ai = a * (0.15 + 0.85 * front) * prog(t, 25.4 + i * 0.07, 26.4 + i * 0.07)
        sz = int(20 + 16 * front)
        draw_label(frame, DOMAINS[i], x[i], y[i] - sz * 0.9, sz, 'noto', 'Medium' if front > 0.6 else 'Light', (255, 246, 230), ai, 0.1)
    a2 = prog(t, 26.6, 27.8) * (1 - prog(t, 32.4, 33.2))
    draw_label(frame, 'アパレル販売管理の、すべてを。', 150, 175, 50, 'noto', 'Light', (255, 255, 255), a2, 0.2, 'left')
    draw_label(frame, '17 domains  ·  196 screens  ·  130 print forms', 152, 238, 22, 'segoel', None, (190, 210, 255), a2 * 0.85, 0.2, 'left')

STATS = [(34.4, 1801, 'COMMITS', 'コミット'), (36.3, 196, 'SCREENS', '業務画面'), (38.2, 130, 'PRINT FORMS', '帳票'), (40.1, 1219, 'TESTS', '自動テスト')]
def stats_overlay(frame, t):
    sa = prog(t, 34.0, 34.6) * (1 - prog(t, 42.0, 43.0))
    if sa > 0:
        yy, xx = grid()
        d = ((xx - W / 2) / 700) ** 2 + ((yy - H / 2 + 20) / 300) ** 2
        frame *= (1 - 0.75 * sa * np.exp(-d))[..., None]
    for i, (s, v, en, ja) in enumerate(STATS):
        e = s + 1.9 if i < 3 else 42.3
        if not (s <= t < e + 0.3):
            continue
        k = ease_out_expo(prog(t, s, s + 0.9))
        a = prog(t, s, s + 0.25) * (1 - prog(t, e - 0.2, e + 0.2))
        val = int(round(v * k))
        txt = f'{val:,d}'
        sc = 1.0 + 0.25 * (1 - ease_out_cubic(prog(t, s, s + 0.6)))
        num = text_arr(txt, int(250 * sc), fam='segoel', weight=None, color=(255, 255, 255), tracking=0.02)
        dx = (1 - ease_out_expo(prog(t, s, s + 0.7))) * 80
        blit(frame, num, W / 2 + dx, H / 2 - 40, a)
        # 光の走査
        sweep = prog(t, s + 0.2, s + 1.0)
        if 0 < sweep < 1:
            g = num.copy()
            hgt, wid = g.shape[:2]
            xx = np.arange(wid)[None, :]
            band = np.exp(-((xx - sweep * wid * 1.3 + wid * 0.15) / (wid * 0.06)) ** 2)
            g[..., 3] *= band
            blit(frame, g, W / 2 + dx, H / 2 - 40, a * 1.2, mode='add', tint=(0.6, 0.9, 1.0))
        draw_label(frame, en, W / 2, H / 2 + 120, 28, 'segoesl', None, (170, 210, 255), a * 0.9, 0.6)
        draw_label(frame, ja, W / 2, H / 2 + 172, 30, 'noto', 'Light', (255, 255, 255), a * 0.85, 0.3)

def logo_overlay(frame, t):
    a1 = prog(t, 46.2, 47.6)
    if a1 <= 0:
        return
    fo = 1 - prog(t, 50.6, 52.0)
    e = ease_out_expo(prog(t, 46.2, 48.0))
    draw_label(frame, 'Creative Vision 10', W / 2, 808 - 16 * (1 - e), 72, 'segoel', None, (255, 255, 255), a1 * fo, 0.08 + 0.1 * (1 - e))
    a2 = prog(t, 47.2, 48.4) * fo
    draw_label(frame, 'アパレル販売管理を、オープンに。', W / 2, 888, 32, 'noto', 'Light', (255, 255, 255), a2 * 0.92, 0.32)
    a3 = prog(t, 48.0, 49.0) * fo
    draw_label(frame, 'full opensource package   ·   .NET 10   ·   gRPC   ·   2027.01 RELEASE', W / 2, 950, 20, 'segoel', None, (170, 200, 255), a3 * 0.85, 0.22)

def flash(frame, t):
    for s, amp, dur in ((4.5, 0.9, 0.45), (14.0, 0.2, 0.5), (45.6, 0.35, 0.8)):
        if s <= t < s + dur:
            k = 1 - (t - s) / dur
            frame += np.asarray((0.85, 0.85, 1.0), np.float32) * amp * k ** 3

def anamorphic(frame, strength=0.6):
    bright = np.clip(frame - 0.85, 0, None)
    small = downsample(bright, 4)
    st = ndimage.gaussian_filter(small, (0.6, 40, 0))
    frame += upsample(st, (H, W)) * np.asarray((0.5, 0.7, 1.4), np.float32) * strength * 3
    return frame

def letterbox(frame, t):
    k = 1 - ease_in_out_cubic(prog(t, 33.6, 35.0))
    k = max(k, ease_in_out_cubic(prog(t, 41.5, 43.0)) * 0.0)
    bar = int(110 * k)
    if bar > 0:
        frame[:bar] = 0; frame[H - bar:] = 0

def background(t):
    f = new_frame((0.0, 0.0, 0.0))
    yy, xx = grid()
    # ごく淡いグラデーション（中心に紫）
    k = 0.4 + 0.6 * prog(t, 4.5, 8)
    d = ((xx - W / 2) ** 2 + (yy - H * 0.55) ** 2) / (W * W * 0.35)
    g = np.exp(-d)[..., None] * np.asarray((0.05, 0.03, 0.11), np.float32) * k
    return f + g

def render(t):
    frame = background(t)
    cam = camera(t)
    if t < 4.6:
        # 一点の鼓動
        beat = sum(math.exp(-max(0, t - b) / 0.25) * (t >= b) for b in (1.0, 2.2, 3.4))
        r = 0.6 + 2.2 * beat + 6 * prog(t, 4.0, 4.5) ** 3
        dot = circle_rgba(max(1, int(r * 2)) / 2 + 0.5, (1, 1, 1))
        layer = np.zeros((H, W, 3), np.float32)
        blit(layer, dot, W / 2, H / 2, 1.0, mode='add')
        frame += glow_layer(layer, 18, 3.0 + 4 * beat) * (0.5 + 0.8 * beat + prog(t, 0.0, 1.0) * 0.5)
    render_particles(t, cam, frame)
    arch_overlay(frame, t, cam)
    globe_overlay(frame, t, cam)
    stats_overlay(frame, t)
    logo_overlay(frame, t)
    hud(frame, t)
    flash(frame, t)
    bloom(frame, 0.7, 0.55)
    anamorphic(frame, 0.35)
    frame = chroma(frame, 1.5 + 4 * sum(math.exp(-max(0, t - s) / 0.15) * (t >= s) for s in (4.5, 14.0, 45.6)))
    vignette(frame, 0.5)
    frame = tonemap(frame)
    letterbox(frame, t)
    grain(frame, t, 0.018)
    fin = 1 - prog(t, 51.2, 52.0)
    return frame * fin

# ---------------------------------------------------------------- 音
def audio():
    D = DURATION
    m = Mix(D)
    # ドローン
    m.add(0.0, pad([38, 45, 50], 15, cutoff=500, attack=4, release=3, seed=1), 0.35)
    for b in (1.0, 2.2, 3.4):
        m.add(b, kick(0.6, 90, 35, 0.25, 0.1), 0.5)
    # タイプ音
    s = '2025.11.25  initial commit  —  30 files'
    for i in range(len(s)):
        if s[i] != ' ':
            m.add(0.8 + 2.2 * i / len(s), tick(2400 + (i % 3) * 300, 0.02), 0.08, pan=-0.4)
    m.add(3.0, riser(1.5, 300, 6000), 0.25)
    m.add(4.5, sub_boom(4.0, 80, 30, 1.4), 0.9)
    m.add(4.5, whoosh(1.6, 4000, 300), 0.3)
    # コミットの雨（ペンタトニック）
    penta = [62, 64, 67, 69, 71, 74, 76, 79, 81, 83, 86]
    rng = np.random.default_rng(5)
    last = 0
    for i in range(NC):
        # cursor_u の逆写像を数値的に
        pass
    tt = np.linspace(5.0, 12.6, 4000)
    uu = np.array([cursor_u(x) for x in tt])
    times = np.interp(CU, uu, tt)
    for tm in times:
        if tm - last > 0.045:
            n = penta[rng.integers(0, len(penta))]
            m.add(tm, pluck(n, 0.35, 5000, 0.08), 0.05, pan=float(rng.uniform(-0.7, 0.7)))
            last = tm
    m.add(5.0, pad([50, 57, 62, 65], 9, cutoff=900, attack=3, release=2, seed=2), 0.22)
    m.add(10.8, riser(3.2, 200, 9000), 0.35)
    m.add(13.9, sub_boom(3.0, 70, 32, 1.0), 0.8)
    m.add(13.9, whoosh(1.2, 5000, 400, 7), 0.3)
    # ビート（120BPM）14→42
    bpm = 120; beat = 60 / bpm
    prog_ch = [[38, 45, 50, 53], [34, 41, 46, 50], [41, 48, 53, 57], [36, 43, 48, 55]]  # Dm Bb F C
    t0 = 14.0
    for bar in range(14):
        tb = t0 + bar * 4 * beat
        ch = prog_ch[bar % 4]
        if tb > 42:
            break
        m.add(tb, pad([n + 12 for n in ch], 4 * beat + 0.4, cutoff=1400 + bar * 60, attack=0.4, release=0.5, seed=bar), 0.16)
        for b in range(4):
            tt_ = tb + b * beat
            if tt_ >= 42.0 or (tt_ < 16.0):
                continue
            m.add(tt_, kick(), 0.55)
            # ベース（8分）
            for h in range(2):
                bt = tt_ + h * beat / 2
                bass = lowpass(osc(midi(ch[0]), beat / 2 * 0.9, 'saw'), 500) * env_adsr(int(beat / 2 * 0.9 * SR), 0.005, 0.1, 0.6, 0.05)
                m.add(bt + 0.0, bass, 0.22)
            if tt_ >= 24.0:
                m.add(tt_ + beat / 2, hat(), 0.18, pan=0.3)
            if tt_ >= 26.0 and b % 2 == 1:
                m.add(tt_, clap(), 0.25)
        # アルペジオ
        if tb >= 24.0:
            for k in range(16):
                n = ch[k % 4] + 24 + (12 if k % 8 >= 6 else 0)
                m.add(tb + k * beat / 4, pluck(n, 0.25, 3500, 0.07), 0.05, pan=math.sin(k) * 0.5)
    # パケット音
    for k in range(40):
        m.add(16.5 + k * 0.16, tick(1800 + (k % 5) * 400, 0.03), 0.04, pan=float(np.sin(k * 1.7)) * 0.8)
    m.add(22.3, riser(1.6, 400, 7000, 21), 0.2)
    m.add(23.4, whoosh(1.8, 600, 5000, 31), 0.35)
    m.add(32.0, riser(2.0, 300, 8000, 23), 0.25)
    # 数字の打撃
    for s, *_ in STATS:
        m.add(s, sub_boom(1.8, 90, 40, 0.5), 0.55)
        m.add(s, snare(0.4), 0.18)
        m.add(s, whoosh(0.5, 6000, 800, int(s)), 0.2)
    # ロゴへ
    m.add(42.0, whoosh(3.4, 300, 6000, 41), 0.4)
    m.add(42.2, riser(3.4, 150, 12000, 43), 0.5)
    m.add(45.6, sub_boom(6.0, 75, 28, 2.0), 1.0)
    m.add(45.6, snare(0.5, 9), 0.3)
    m.add(45.6, pad([50, 57, 62, 66, 69, 74], 6.4, cutoff=2600, attack=0.05, release=3.0, seed=9), 0.3)
    m.add(45.6, pad([26, 38], 6.4, cutoff=300, attack=0.05, release=3.0, seed=10), 0.4)
    mix = m.master(D + 1)
    mix = reverb(mix, 3.2, 0.28)
    return m_master2(mix, D)

def m_master2(x, D):
    x = x[:int(D * SR)]
    peak = np.max(np.abs(x)) + 1e-9
    x = np.tanh(x / peak * 1.3) / np.tanh(1.3) * 0.9
    fo = int(1.2 * SR)
    x[-fo:] *= np.linspace(1, 0, fo)[:, None]
    return x
