# v3 のポップな演出（op3.py 用）
# 明るい配色の水玉と同心円の背景、渦、ローアングルの夜の街、紙吹雪・星・ハート、厚みのある文字、
# ネオンの筆記体、円形のワイプ、夕焼けの筋雲、立ち絵の線画化をまとめる。すべてコードで描く（既成の映像素材は使わない）。
import os, math
import numpy as np
import engine as E
import fx
from engine import prog, lerp, clamp, ease_out, ease_out_back, smooth

E._FONTFILES.update(impact='impact.ttf', comic='comicbd.ttf', script='segoescb.ttf', courier='courbd.ttf')

SW, SH = 1920.0, 1080.0


# ---------------------------------------------------------------- 背景: グラデーション＋同心円（スポットライト）＋水玉
POPBG_FS = E.HEADER + r'''
uniform vec3 c0, c1, circCol, dotCol; uniform float gAng, dotA, dotGap, dotR; uniform vec2 dotOff;
uniform vec4 ci0, ci1, ci2, ci3, ci4, ci5;
float circ(vec2 px, vec4 c){ return smoothstep(1.5, -1.5, length(px - c.xy) - c.z)*c.w; }
vec3 scr(vec3 a, vec3 b){ return 1.0 - (1.0 - a)*(1.0 - b); }
void main(){
    vec2 px = suv*vec2(1920.0, 1080.0);
    vec2 dir = vec2(cos(gAng), sin(gAng));
    float k = clamp(dot(px - vec2(960.0, 540.0), dir)/1150.0 + 0.5, 0.0, 1.0);
    vec3 col = mix(c0, c1, smoothstep(0.0, 1.0, k));
    float a = 0.0;
    a = max(a, circ(px, ci0)); a = max(a, circ(px, ci1)); a = max(a, circ(px, ci2));
    a = max(a, circ(px, ci3)); a = max(a, circ(px, ci4)); a = max(a, circ(px, ci5));
    col = mix(col, scr(col, circCol), a);
    // 水玉（1段ごとに半分ずらす）
    vec2 g = (px + dotOff)/dotGap;
    g.x += 0.5*mod(floor(g.y), 2.0);
    vec2 fp = fract(g) - 0.5;
    float dd = length(fp)*dotGap - dotR;
    col = mix(col, dotCol, smoothstep(1.0, -1.0, dd)*dotA);
    f = vec4(col, 1.0);
}
'''


def popbg(gl, c0, c1, ang=0.6, circles=(), circ_col=(1, 1, 1), dot_col=(1, 1, 1), dot_a=0.55, gap=46.0, dot_r=3.2, dot_off=(0, 0)):
    """circles: (x, y, r, α) の並び（最大6）"""
    u = dict(c0=c0, c1=c1, gAng=ang, circCol=circ_col, dotCol=dot_col, dotA=dot_a, dotGap=gap, dotR=dot_r, dotOff=dot_off)
    cs = list(circles)[:6] + [(0, 0, 0, 0)] * (6 - min(len(circles), 6))
    for i, c in enumerate(cs):
        u[f'ci{i}'] = c
    gl.fullscreen(POPBG_FS, u, blend=None)


def spot_circles(t, seed=0, n=4, big=1.0, alpha=0.55):
    """ゆっくり漂う大きな円（スポットライト）"""
    rng = np.random.default_rng(seed)
    out = []
    for i in range(n):
        x0, y0 = rng.uniform(100, 1820), rng.uniform(80, 1000)
        r = rng.uniform(260, 520) * big
        sp = rng.uniform(0.15, 0.35)
        ph = rng.uniform(0, 6.3)
        out.append((x0 + 160 * math.sin(t * sp + ph), y0 + 90 * math.cos(t * sp * 1.3 + ph), r, alpha))
    return out


# ---------------------------------------------------------------- 渦
SWIRL_FS = E.HEADER + r'''
uniform vec2 c; uniform float rot, arms, tight, aspect, hole, lineA, holeA;
uniform vec3 cA, cB, cC, lineCol, holeCol;
float h21(vec2 p){ p = fract(p*vec2(123.34, 456.21)); p += dot(p, p+45.32); return fract(p.x*p.y); }
void main(){
    vec2 p = (suv - c)*vec2(aspect, 1.0);
    float r = length(p) + 1e-4;
    float a = atan(p.y, p.x);
    float s = a/6.2831853*arms + log(r)*tight - rot;
    float b = fract(s);
    float w = fwidth(s)*1.2;
    vec3 col = cA;
    col = mix(col, cB, smoothstep(0.333 - w, 0.333 + w, b));
    col = mix(col, cC, smoothstep(0.666 - w, 0.666 + w, b));
    float e = min(min(b, 1.0 - b), min(abs(b - 0.333), abs(b - 0.666)));
    col = mix(col, lineCol, smoothstep(w*2.5, w*0.5, e)*lineA);
    // 中心の穴（夜空と星）
    float hk = hole > 0.001 ? smoothstep(hole*1.08, hole*0.92, r)*holeA : 0.0;
    if(hk > 0.0){
        vec3 sky = mix(holeCol*1.6, holeCol, smoothstep(0.0, hole, r));
        vec2 g = p*260.0; vec2 id = floor(g); float hh = h21(id);
        sky += vec3(1.0, 0.95, 0.6)*step(0.985, hh)*smoothstep(0.5, 0.1, length(fract(g) - 0.5));
        col = mix(col, sky, hk);
    }
    f = vec4(col, 1.0);
}
'''


def swirl(gl, rot, c=(0.5, 0.5), arms=2.0, tight=1.6, cols=('9fb4ec', 'c4d0f5', '7f97e0'), line=(1, 1, 1), line_a=0.55,
          hole=0.0, hole_col='1a1f5e', hole_a=1.0):
    h = E.hexc
    gl.fullscreen(SWIRL_FS, dict(c=c, rot=rot, arms=arms, tight=tight, aspect=gl.VW / gl.VH, cA=h(cols[0]), cB=h(cols[1]),
                                 cC=h(cols[2]), lineCol=line, lineA=line_a, hole=hole, holeCol=h(hole_col), holeA=hole_a), blend=None)


# ---------------------------------------------------------------- 円形のワイプ
WIPE_FS = E.HEADER + r'''
uniform vec2 c; uniform float r, inside; uniform vec4 col;
void main(){
    vec2 px = suv*vec2(1920.0, 1080.0);
    float d = length(px - c) - r;
    float a = inside > 0.5 ? smoothstep(1.5, -1.5, d) : smoothstep(-1.5, 1.5, d);
    a *= col.a;
    f = vec4(col.rgb*a, a);
}
'''


def wipe(gl, x, y, r, col=(1, 1, 1), inside=True, alpha=1.0):
    """inside=True で円の中を、False で円の外を塗る"""
    gl.fullscreen(WIPE_FS, dict(c=(x, y), r=max(r, 0.0), inside=1.0 if inside else 0.0, col=(*col, alpha)))


# ---------------------------------------------------------------- 夕焼けの筋雲
SUNSET_FS = E.HEADER + fx.NOISE + r'''
uniform float drift, aspect; uniform vec3 cTop, cMid, cBot, cl1, cl2, edge;
void main(){
    vec2 q = suv;
    vec3 col = mix(cTop, cMid, smoothstep(0.0, 0.55, q.y));
    col = mix(col, cBot, smoothstep(0.55, 1.0, q.y));
    // 斜めに流れる筋雲（2階調＋濃い縁）
    float ang = -0.32;
    vec2 p = vec2(q.x*aspect, q.y);
    p = vec2(cos(ang)*p.x - sin(ang)*p.y, sin(ang)*p.x + cos(ang)*p.y);
    vec2 sp = vec2(p.x*1.3 + drift, p.y*11.0);
    float n = fbm(sp) + 0.35*fbm(sp*vec2(3.0, 1.0) + 4.0);
    float a1 = smoothstep(0.66, 0.68, n);
    float a2 = smoothstep(0.78, 0.80, n);
    float ed = smoothstep(0.62, 0.66, n)*(1.0 - a1);
    col = mix(col, edge, ed*0.9);
    col = mix(col, cl1, a1);
    col = mix(col, cl2, a2);
    f = vec4(col, 1.0);
}
'''


SUNSET_PAL = dict(
    sunset=('d81e2c', 'f2571b', 'ffb02a', 'ff8a1e', 'ffd04a', 'a8122a'),
    dawn=('5a3aa8', 'e86aa0', 'ffc46a', 'ff9ac0', 'fff0a8', '7a2a8a'),
)


def sunset(gl, t, drift_speed=0.06, pal='sunset'):
    h = E.hexc
    c = SUNSET_PAL[pal]
    gl.fullscreen(SUNSET_FS, dict(drift=t * drift_speed, aspect=gl.VW / gl.VH, cTop=h(c[0]), cMid=h(c[1]), cBot=h(c[2]),
                                  cl1=h(c[3]), cl2=h(c[4]), edge=h(c[5])), blend=None)


# ---------------------------------------------------------------- ローアングルの街（平塗りのビル）
BLD_FS = E.HEADER + r'''
uniform vec3 base, winOn, winOff, lineCol; uniform vec2 grid; uniform float seed, lit, stripe, outline, shade, tw;
float h21(vec2 p){ p = fract(p*vec2(123.34, 456.21)); p += dot(p, p+45.32); return fract(p.x*p.y); }
void main(){
    vec3 col = base*shade;
    if(stripe > 0.5){
        // 横縞のビル（帯ごとに明暗）
        float band = fract(uv.y*grid.y);
        col = mix(col, base*shade*1.25, step(0.55, band));
        float hb = h21(vec2(floor(uv.y*grid.y), seed));
        col = mix(col, winOn, step(0.5, band)*step(band, 0.8)*step(0.72, hb));
    } else {
        vec2 g = uv*grid;
        vec2 id = floor(g); vec2 fp = fract(g);
        float m = step(0.22, fp.x)*step(fp.x, 0.78)*step(0.2, fp.y)*step(fp.y, 0.8);
        float h = h21(id + seed*7.1);
        float on = step(1.0 - lit, h);
        float flick = 0.85 + 0.15*sin(time*(1.0 + h*3.0) + h*30.0)*tw;
        vec3 w = mix(winOff, winOn*flick, on);
        // 窓の斜めの光
        w = mix(w, vec3(1.0, 1.0, 0.92), on*step(abs(fp.x - fp.y*0.7 - 0.12), 0.06)*0.7);
        col = mix(col, w*shade, m);
    }
    // 漫画風の縁取り
    vec2 e = min(uv, 1.0 - uv)/fwidth(uv);
    float o = smoothstep(outline + 1.0, outline, min(e.x, e.y));
    col = mix(col, lineCol, o);
    f = vec4(col, 1.0);
}
'''

CITY_PAL = ['f05a5a', '5b8ff2', '6fbf7a', 'e8c95a', '9b7be6', '5fd0e0', 'f09ad0', 'f2a65a']


def city(gl, t, vp=(960, -1500), roll=0.0, scroll=0.0, seed=5, n=26, ground=1180, zoom=1.0, hearts=True, night=True):
    """見上げた街。vp は消失点（画面の上の外）、roll は画面の傾き、scroll は横の移動（px）"""
    rng = np.random.default_rng(seed)
    items = []
    for layer in range(2):
        for i in range(n // 2):
            span = 3600
            xb = (rng.uniform(0, span) + scroll * (0.6 + 0.6 * layer)) % span - (span - 1920) / 2
            w = rng.uniform(170, 330) * (0.75 + 0.45 * layer) * zoom
            h = rng.uniform(0.55, 0.8) if layer == 0 else rng.uniform(0.35, 0.62)
            items.append((layer, xb, w, h, i))
    cx, cy = 960, 540
    R = E.m3_translate(cx, cy) @ E.m3_rot(roll) @ E.m3_translate(-cx, -cy)

    def tp(p):
        v = R @ np.array([p[0], p[1], 1.0])
        return (v[0] / v[2], v[1] / v[2])
    vpx, vpy = vp
    tops = []
    for layer, xb, w, h, i in sorted(items, key=lambda q: q[0]):
        yb = ground + 60 * layer
        bl = np.array([xb - w / 2, yb]); br = np.array([xb + w / 2, yb])
        V = np.array([vpx, vpy])
        tl = bl + (V - bl) * h; tr = br + (V - br) * h
        # 側面（中心寄りの面が見える）
        side = 1 if xb < vpx else -1
        sd = w * 0.28 * side
        if side > 0:
            s0, s1 = br, br + np.array([sd, -30])
            s2, s3 = s1 + (V - s1) * (h * 0.985), tr
            quad_side = [s3, s2, s1, s0]
        else:
            s0, s1 = bl, bl + np.array([sd, -30])
            s2, s3 = s1 + (V - s1) * (h * 0.985), tl
            quad_side = [s2, s3, s0, s1]
        col = E.hexc(CITY_PAL[(i * 3 + layer * 5 + seed) % len(CITY_PAL)])
        stripe = 1.0 if (i + layer) % 5 == 2 else 0.0
        grid = (rng.integers(3, 6), rng.integers(9, 16))
        common = dict(winOn=E.hexc('ffe36a'), winOff=E.hexc('34306e') if night else E.hexc('a8c8f0'), lineCol=E.hexc('1d1640'),
                      grid=grid, seed=float(i + 31 * layer), lit=0.45, stripe=stripe, outline=2.2, tw=1.0)
        Hs = E.homography([(0, 0), (1, 0), (1, 1), (0, 1)], [tp(p) for p in quad_side])
        gl.draw(BLD_FS, Hs, dict(common, base=col, shade=0.62, grid=(max(2, grid[0] - 2), grid[1])), blend=None)
        Hf = E.homography([(0, 0), (1, 0), (1, 1), (0, 1)], [tp(tl), tp(tr), tp(br), tp(bl)])
        gl.draw(BLD_FS, Hf, dict(common, base=col, shade=1.0), blend=None)
        tops.append((tp((tl + tr) / 2), np.linalg.norm(tr - tl), i, layer))
    if hearts:
        for (x, y), w, i, layer in tops:
            if i % 4 == 1 and layer == 1:
                sz = max(36.0, w * 0.45)
                bob = 6 * math.sin(t * 3 + i)
                parts(gl, [x], [y - sz * 0.45 + bob], [sz], [E.hexc('ff5d9e')], kind=4, outline=0.12, angle=[-roll])
    return tops


# ---------------------------------------------------------------- ポップな粒子（紙吹雪・三角・水玉・星・ハート・タイル・輪）
POPPART_FS = r'''
#version 430
in vec2 uv; in vec4 col; in vec4 aux; out vec4 f;
float dot2(vec2 v){ return dot(v, v); }
float sdBox(vec2 p, vec2 b, float r){ vec2 q = abs(p) - b + r; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r; }
float sdTri(vec2 p){
    const float k = sqrt(3.0);
    p.x = abs(p.x) - 0.8; p.y = p.y + 0.8/k;
    if(p.x + k*p.y > 0.0) p = vec2(p.x - k*p.y, -k*p.x - p.y)/2.0;
    p.x -= clamp(p.x, -1.6, 0.0);
    return -length(p)*sign(p.y);
}
float sdStar5(vec2 p, float r, float rf){
    const vec2 k1 = vec2(0.809016994375, -0.587785252292);
    const vec2 k2 = vec2(-k1.x, k1.y);
    p.x = abs(p.x);
    p -= 2.0*max(dot(k1, p), 0.0)*k1;
    p -= 2.0*max(dot(k2, p), 0.0)*k2;
    p.x = abs(p.x);
    p.y -= r;
    vec2 ba = rf*vec2(-k1.y, k1.x) - vec2(0, 1);
    float h = clamp(dot(p, ba)/dot(ba, ba), 0.0, r);
    return length(p - ba*h)*sign(p.y*ba.x - p.x*ba.y);
}
float sdHeart(vec2 p){
    p.x = abs(p.x);
    if(p.y + p.x > 1.0) return sqrt(dot2(p - vec2(0.25, 0.75))) - sqrt(2.0)/4.0;
    return sqrt(min(dot2(p - vec2(0.0, 1.0)), dot2(p - 0.5*max(p.x + p.y, 0.0))))*sign(p.x - p.y);
}
void main(){
    vec2 p = uv*2.0 - 1.0;
    vec2 py = vec2(p.x, -p.y);          // y 上向き
    float kind = aux.x;
    float d;
    if(kind < 0.5)      d = sdBox(p, vec2(0.9*aux.z, 0.9), 0.08);           // 紙吹雪の帯（aux.z 幅の比）
    else if(kind < 1.5) d = sdTri(py*1.05) - 0.03;                            // 三角
    else if(kind < 2.5) d = length(p) - 0.9;                                  // 水玉
    else if(kind < 3.5) d = sdStar5(py*1.05 + vec2(0.0, 0.06), 0.9, 0.45) - 0.04;   // 星
    else if(kind < 4.5) d = sdHeart(py*0.62 + vec2(0.0, 0.52))/0.62;          // ハート
    else if(kind < 5.5) d = sdBox(p, vec2(0.86, 0.86*aux.z), 0.12);           // タイル（aux.z 縦の比）
    else                d = abs(length(p) - 0.72) - 0.14;                     // 輪
    float e = fwidth(d)*1.2 + 1e-4;
    float ow = aux.w;                    // 縁取りの幅（0 でなし）
    float fill = smoothstep(e, -e, d + ow);
    float all_ = smoothstep(e, -e, d);
    vec3 c = mix(vec3(0.11, 0.07, 0.22), col.rgb, fill);
    float a = (ow > 0.0 ? all_ : fill)*col.a;
    f = vec4(c*a, a);
}
'''


def parts(gl, xs, ys, size, colors, kind=0, alpha=1.0, angle=None, ratio=1.0, outline=0.0, blend='over'):
    """colors は (n,3) か (3,)。kind: 0 紙吹雪 1 三角 2 水玉 3 星 4 ハート 5 タイル 6 輪"""
    n = len(xs)
    if n == 0:
        return
    pos = np.zeros((n, 4), np.float32)
    pos[:, 0] = xs; pos[:, 1] = ys; pos[:, 2] = size
    pos[:, 3] = 0 if angle is None else angle
    col = np.zeros((n, 4), np.float32)
    col[:, :3] = colors
    col[:, 3] = alpha
    aux = np.zeros((n, 4), np.float32)
    aux[:, 0] = kind; aux[:, 2] = ratio; aux[:, 3] = outline
    gl.particles(POPPART_FS, pos, col, aux, blend=blend)


CONF_PAL = np.array([E.hexc(h) for h in ('ff4f7b', 'ffd23f', 'b6f23c', '3fd0ff', 'ff8a3d', 'f7a6c8', '7be0a0')])


def confetti(gl, t, n=90, seed=1, fall=60.0, alpha=1.0, area=(0, 0, 1920, 1080), size=(14, 46), spin=1.0):
    """紙吹雪（帯・三角・水玉）。ゆっくり落ちて回る"""
    rng = np.random.default_rng(seed)
    x0, y0, x1, y1 = area
    xs = rng.uniform(x0 - 40, x1 + 40, n) + 18 * np.sin(t * rng.uniform(0.5, 1.5, n) + rng.uniform(0, 6, n))
    ys = (rng.uniform(y0, y1 + 120, n) + t * fall * rng.uniform(0.6, 1.4, n)) % (y1 - y0 + 160) + y0 - 80
    kinds = rng.choice([0, 0, 0, 1, 2], n)
    sz = rng.uniform(*size, n)
    sz = np.where(kinds == 2, sz * 0.35, sz)
    ang = rng.uniform(0, 6.3, n) + t * rng.uniform(-1.5, 1.5, n) * spin
    cols = CONF_PAL[rng.integers(len(CONF_PAL), size=n)]
    for k in (0, 1, 2):
        m = kinds == k
        parts(gl, xs[m], ys[m], sz[m], cols[m], kind=k, alpha=alpha, angle=ang[m], ratio=0.22 if k == 0 else 1.0)


# ---------------------------------------------------------------- 文字
def chunk(gl, s, x, y, size, fill=(1, 1, 1), edge=E.hexc('3b1a6e'), shadow=E.hexc('3b1a6e'), depth=0.09, fam='noto', weight='Black',
          scale=1.0, angle=0.0, alpha=1.0, anchor=(0.5, 0.5), stroke=None, white=0.0):
    """厚みのある文字（下右へ押し出した影＋縁取り）。white>0 で外側に白い縁"""
    if alpha <= 0.002 or scale <= 0.001:
        return None
    st = stroke if stroke is not None else max(3, size * 0.075)
    top = gl.text(s, size, fam=fam, weight=weight, color=fill, stroke=st, stroke_color=edge)
    sh = gl.text(s, size, fam=fam, weight=weight, color=shadow, stroke=st, stroke_color=shadow)
    if white > 0:
        wt = gl.text(s, size, fam=fam, weight=weight, color=(1, 1, 1), stroke=st + white, stroke_color=(1, 1, 1))
    steps = 7
    dd = size * depth * scale
    c, sn = math.cos(angle), math.sin(angle)
    if white > 0:
        for i in range(steps + 1):
            o = dd * i / steps
            gl.sprite(wt, x + o * (c - sn) * 0.7, y + o * (sn + c) * 0.9, scale=scale, angle=angle, alpha=alpha, anchor=anchor)
    for i in range(steps, 0, -1):
        o = dd * i / steps
        gl.sprite(sh, x + o * (c - sn) * 0.7, y + o * (sn + c) * 0.9, scale=scale, angle=angle, alpha=alpha, anchor=anchor)
    gl.sprite(top, x, y, scale=scale, angle=angle, alpha=alpha, anchor=anchor)
    return top


def letter_layout(gl, word, size, fam='noto', weight='Black', kern=0.92):
    """1文字ずつの中心 x（中央揃えの相対位置）と送り幅"""
    ws = []
    for ch in word:
        if ch == ' ':
            ws.append(size * 0.35)
        else:
            tx = gl.text(ch, size, fam=fam, weight=weight)
            ws.append(gl.vsize(tx)[0] * kern)
    tot = sum(ws)
    xs, x = [], -tot / 2
    for w in ws:
        xs.append(x + w / 2); x += w
    return xs, tot


def pop_word(gl, t, word, times, x, y, size, fills, edge=E.hexc('3b1a6e'), shadow=None, fam='noto', weight='Black', kern=0.92,
             wobble=0.12, jump=0.0, beat=None, angle=0.0, white=0.0, scale=1.0, alpha=1.0, depth=0.09):
    """文字が times[i] に1つずつ弾んで出る。fills は文字ごとの色（循環）"""
    xs, tot = letter_layout(gl, word, size, fam, weight, kern)
    c, sn = math.cos(angle), math.sin(angle)
    for i, ch in enumerate(word):
        if ch == ' ':
            continue
        k = prog(t, times[i], times[i] + 0.3)
        if k <= 0:
            continue
        sc = ease_out_back(k, 2.6)
        rot = angle + wobble * (1 if i % 2 else -1) * (1 - k) * 3 + wobble * 0.35 * (1 if i % 2 else -1)
        yy = 0.0
        if beat is not None:
            yy = -jump * abs(math.sin(math.pi * (beat + i * 0.13)))
        lx = xs[i] * scale
        px, py = x + lx * c, y + lx * sn + yy
        fill = fills[i % len(fills)]
        chunk(gl, ch, px, py, size, fill=fill, edge=edge, shadow=shadow or edge, fam=fam, weight=weight, scale=sc * scale, angle=rot,
              alpha=alpha, white=white, depth=depth)
    return tot


def neon(gl, s, x, y, size, col=(1.0, 0.35, 0.75), core=(1, 1, 1), alpha=1.0, flicker=1.0, fam='script', scale=1.0, angle=0.0):
    """ネオン管の文字（色の光＋白い芯。ブルームで滲ませる）"""
    glow = gl.text(s, size, fam=fam, weight=None, color=col, stroke=size * 0.06, stroke_color=col)
    tube = gl.text(s, size, fam=fam, weight=None, color=core, stroke=size * 0.02, stroke_color=col)
    a = alpha * flicker
    gl.sprite(glow, x, y, scale=scale * 1.0, angle=angle, alpha=a * 0.8, tint=(1.3, 1.3, 1.3), blend='add')
    gl.sprite(tube, x, y, scale=scale, angle=angle, alpha=a, tint=(1.15, 1.15, 1.15))


# ---------------------------------------------------------------- 線画（立ち絵から）
def lineart(path, out, maxh=1400):
    """透過 PNG の絵を、黒い線だけの RGBA にする（DoG で線を拾い、輪郭は太めに）。結果は out にキャッシュ。
    同じ切り抜き・大きさの色の絵を out の名前に _color を付けて保存する（線画から色が付く演出に重ねて使う）"""
    import cv2
    from PIL import Image
    out_c = out[:-4] + '_color.png'
    if os.path.exists(out_c) and os.path.getmtime(out_c) >= os.path.getmtime(path):
        return out
    im = Image.open(path).convert('RGBA')
    bb = im.getchannel('A').point(lambda v: 255 if v > 40 else 0).getbbox()
    if bb:
        pad = 24
        im = im.crop((max(0, bb[0] - pad), max(0, bb[1] - pad), min(im.width, bb[2] + pad), min(im.height, bb[3] + pad)))
    if im.height > maxh:
        im = im.resize((int(im.width * maxh / im.height), maxh), Image.LANCZOS)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    im.save(out_c)
    a = np.asarray(im, np.float32) / 255
    rgb = a[..., :3] * a[..., 3:4] + (1 - a[..., 3:4])
    g = cv2.cvtColor((rgb * 255).astype(np.uint8), cv2.COLOR_RGB2GRAY).astype(np.float32) / 255
    g1 = cv2.GaussianBlur(g, (0, 0), 1.0)
    g2 = cv2.GaussianBlur(g, (0, 0), 2.6)
    dog = g1 - g2
    line = np.clip((-dog - 0.012) / 0.03, 0, 1)
    dark = np.clip((0.22 - g1) / 0.12, 0, 1) * 0.8           # 黒く塗られた部分（瞳・まつげ）
    alpha = (a[..., 3] > 0.15).astype(np.uint8) * 255
    edge = cv2.morphologyEx(alpha, cv2.MORPH_GRADIENT, np.ones((5, 5), np.uint8)).astype(np.float32) / 255
    ink = np.clip(np.maximum(np.maximum(line, dark), edge), 0, 1)
    ink = cv2.GaussianBlur(ink, (0, 0), 0.6)
    outa = np.zeros(ink.shape + (4,), np.uint8)
    outa[..., :3] = (25, 18, 40)
    outa[..., 3] = (ink * 255).astype(np.uint8)
    Image.fromarray(outa).save(out)
    return out


def card(gl, x, y, w, h, angle, ink=None, label=None, tex=None, alpha=1.0, label_size=40, fit='height'):
    """白い紙のカード（黒い縁取り）。ink は線画、tex は写真（実画面）、label は左上のタイプ文字"""
    import fx as _fx
    _fx.rect(gl, x + 10, y + 14, w + 10, h + 10, (0.08, 0.04, 0.16), alpha=0.35 * alpha, angle=angle)
    _fx.rect(gl, x, y, w + 10, h + 10, E.hexc('1d1640'), alpha=alpha, angle=angle)
    _fx.rect(gl, x, y, w, h, E.hexc('fbfaf6'), alpha=alpha, angle=angle)
    c, s = math.cos(angle), math.sin(angle)
    if tex is not None:
        tw, th = gl.vsize(tex)
        k = min((w - 40) / tw, (h - 110) / th)
        gl.sprite(tex, x - s * 30, y + c * 30, scale=k, angle=angle, alpha=alpha)
    if ink is not None:
        tw, th = ink.size
        k = (h - 40) / th if fit == 'height' else (w - 40) / tw
        gl.sprite(ink, x, y + c * 10, scale=k, angle=angle, alpha=alpha)
    if label:
        lt = gl.text(label, label_size, fam='courier', weight=None, color=E.hexc('1d1640'))
        lw, lh = gl.vsize(lt)
        ox, oy = -w / 2 + 30, -h / 2 + 22
        gl.sprite(lt, x + ox * c - oy * s, y + ox * s + oy * c, angle=angle, alpha=alpha, anchor=(0, 0))


# ---------------------------------------------------------------- 光
def lens_flare(gl, x, y, t, size=1.0, col=(0.6, 0.8, 1.0), alpha=1.0):
    """舞台の照明のフレア（中心の光・横の光芒・六角形のゴースト）"""
    fx.parts(gl, [x], [y], [520 * size], col, [0.55 * alpha], kind=0)
    fx.parts(gl, [x], [y], [220 * size], (1.4, 1.4, 1.5), [0.9 * alpha], kind=1, angle=[t * 0.4])
    fx.rect(gl, x, y, 1500 * size, 5 * size, (0.6, 1.0, 0.9), alpha=0.45 * alpha, blend='add')
    cx, cy = 960, 540
    for j, (k, s) in enumerate(((0.35, 90), (0.6, 50), (1.25, 140), (1.6, 70))):
        gx, gy = x + (cx - x) * k * 2, y + (cy - y) * k * 2
        fx.parts(gl, [gx], [gy], [s * size], (0.4 + 0.2 * j, 0.6, 1.0), [0.18 * alpha], kind=5)
