# GPU 合成エンジン（ModernGL + GLSL、動画生成AI不使用）
# 2D/3D の板（ホモグラフィ）描画、独自シェーダの板、全画面パス、ブルーム・色調・粒状感の仕上げ、
# 文字の画像化、ffmpeg への書き出しをまとめる。各作品の台本（op.py 等）から使う。
import os, sys, math, functools, subprocess
import numpy as np
import moderngl
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), 'promo-video'))
# 共通の補間・ffmpeg・WAV 書き出しは既存の動画エンジン（promo-video/core.py）を使う
from core import REPO, clamp, prog, lerp, ffmpeg_exe, write_wav, rot_x, rot_y, rot_z  # noqa: E402,F401  台本から E.xxx で使う

PROMO = os.environ.get('PROMO_DIR', os.path.join(REPO, 'Doc', 'promo'))
# 素材の加工結果・確認用静止画・完成動画は .gitignore 対象の Doc/promo/anime に置く（ANIME_DIR で変更可）
ANIME = os.environ.get('ANIME_DIR', os.path.join(PROMO, 'anime'))
WORK = os.path.join(ANIME, 'work')
OUT = os.path.join(ANIME, 'out')
ASSETS = os.path.join(PROMO, 'assets')
ASSETS4 = os.path.join(PROMO, 'assets4anime')
FONTS = 'C:/Windows/Fonts/'

# ---------------------------------------------------------------- 補間（core にない形）
def smooth(k): k = clamp(k); return k * k * (3 - 2 * k)
def smoother(k): k = clamp(k); return k * k * k * (k * (k * 6 - 15) + 10)
def ease_out(k, p=3): k = clamp(k); return 1 - (1 - k) ** p
def ease_in(k, p=3): k = clamp(k); return k ** p
def ease_io(k, p=3):
    k = clamp(k)
    return 0.5 * (2 * k) ** p if k < .5 else 1 - 0.5 * (2 - 2 * k) ** p
def ease_out_expo(k): k = clamp(k); return 1.0 if k >= 1 else 1 - 2 ** (-10 * k)
def ease_in_expo(k): k = clamp(k); return 0.0 if k <= 0 else 2 ** (10 * k - 10)
def ease_out_back(k, s=1.70158): k = clamp(k); return 1 + (s + 1) * (k - 1) ** 3 + s * (k - 1) ** 2
def spring(k, f=4.5, d=6.0):
    if k <= 0: return 0.0
    return 1 - math.exp(-d * k) * math.cos(f * math.pi * k)
def pulse(t, t0, a=0.02, r=0.35):
    """t0 で立ち上がり、指数で減衰する 0..1"""
    if t < t0 - a: return 0.0
    if t < t0: return (t - (t0 - a)) / a
    return math.exp(-(t - t0) / r)

def hexc(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))

def hash11(i):
    x = math.sin(i * 127.1 + 311.7) * 43758.5453
    return x - math.floor(x)

# ---------------------------------------------------------------- 行列
def m3_translate(x, y): return np.array([[1, 0, x], [0, 1, y], [0, 0, 1]], np.float64)
def m3_scale(sx, sy=None): return np.diag([sx, sx if sy is None else sy, 1.0])
def m3_rot(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]], np.float64)

def homography(src, dst):
    """4点対応 src→dst の 3x3 射影行列"""
    A = []
    for (x, y), (u, v) in zip(src, dst):
        A.append([x, y, 1, 0, 0, 0, -u * x, -u * y, -u])
        A.append([0, 0, 0, x, y, 1, -v * x, -v * y, -v])
    _, _, vt = np.linalg.svd(np.array(A, np.float64))
    h = vt[-1].reshape(3, 3)
    return h / h[2, 2]

class Cam3:
    """ピンホールカメラ。eye から target を見る。fov は縦の画角（度）"""
    def __init__(self, eye, target, fov=40, roll=0.0, W=1920, H=1080, cx=None, cy=None):
        self.eye = np.asarray(eye, np.float64)
        f = np.asarray(target, np.float64) - self.eye; f /= np.linalg.norm(f)
        up = np.array([0, 1, 0.0])
        r = np.cross(up, f); r /= np.linalg.norm(r)     # 画面の右
        u = np.cross(f, r)                                # 画面の上
        if roll:
            c, s = math.cos(roll), math.sin(roll)
            r, u = r * c + u * s, -r * s + u * c
        self.R = np.stack([r, -u, f])  # 画面 x 右、y 下、z 奥
        self.f = 0.5 * H / math.tan(math.radians(fov) / 2)
        self.cx = W / 2 if cx is None else cx
        self.cy = H / 2 if cy is None else cy

    def to_cam(self, p):
        return (np.asarray(p, np.float64) - self.eye) @ self.R.T

    def project(self, p):
        c = self.to_cam(p)
        z = np.maximum(c[..., 2], 1e-3)
        return np.stack([self.cx + self.f * c[..., 0] / z, self.cy + self.f * c[..., 1] / z], -1), c[..., 2]

    def quad_h(self, center, right, down, w, h):
        """3D の板（中心・右方向・下方向の単位ベクトル・幅・高さ）を画面へ写す射影行列（uv 0..1→画面）"""
        c = np.asarray(center, np.float64); r = np.asarray(right, np.float64); d = np.asarray(down, np.float64)
        pts = [c - r * w / 2 - d * h / 2, c + r * w / 2 - d * h / 2, c + r * w / 2 + d * h / 2, c - r * w / 2 + d * h / 2]
        cc = self.to_cam(np.array(pts))
        if (cc[:, 2] < 1).any():
            return None, cc[:, 2].mean()
        scr, z = self.project(np.array(pts))
        return homography([(0, 0), (1, 0), (1, 1), (0, 1)], scr), float(z.mean())

# ---------------------------------------------------------------- 文字
_FONTFILES = {
    'noto': 'NotoSansJP-VF.ttf', 'serif': 'NotoSerifJP-VF.ttf', 'mincho': 'yumindb.ttf', 'minchol': 'yuminl.ttf',
    'segoe': 'segoeui.ttf', 'segoeb': 'segoeuib.ttf', 'segoel': 'segoeuil.ttf', 'mono': 'CascadiaMono.ttf',
    'bahn': 'bahnschrift.ttf', 'sym': 'seguisym.ttf', 'yugo': 'YuGothB.ttc', 'yugol': 'YuGothL.ttc', 'meiryo': 'meiryob.ttc',
}

@functools.lru_cache(maxsize=256)
def font(fam, size, weight=None):
    path = FONTS + _FONTFILES[fam]
    if not os.path.exists(path):
        path = FONTS + _FONTFILES['noto']
    f = ImageFont.truetype(path, int(size))
    if weight:
        try:
            f.set_variation_by_name(weight)
        except Exception:
            pass
    return f

def text_rgba(text, size, fam='noto', weight='Black', color=(1, 1, 1), stroke=0, stroke_color=(0, 0, 0),
              tracking=0.0, shadow=0, pad=None):
    """文字列を RGBA float 画像（ストレートα）にする"""
    f = font(fam, size, weight)
    asc, desc = f.getmetrics()
    tr = tracking * size
    ws = [f.getlength(ch) for ch in text]
    pad = int(stroke * 2 + size * 0.15 + shadow * 2) if pad is None else pad
    tw = int(sum(ws) + tr * max(len(text) - 1, 0)) + pad * 2
    th = int(asc + desc) + pad * 2
    fill = Image.new('L', (max(tw, 1), max(th, 1)), 0)
    edge = Image.new('L', fill.size, 0)
    d1, d2 = ImageDraw.Draw(fill), ImageDraw.Draw(edge)
    x = pad
    for ch, w in zip(text, ws):
        d1.text((x, pad), ch, font=f, fill=255)
        if stroke:
            d2.text((x, pad), ch, font=f, fill=255, stroke_width=stroke, stroke_fill=255)
        x += w + tr
    a = np.asarray(fill, np.float32) / 255
    out = np.zeros((th, tw, 4), np.float32)
    out[..., :3] = color
    out[..., 3] = a
    if stroke:
        e = np.asarray(edge, np.float32) / 255
        col = np.where(a[..., None] > 0, out[..., :3], 0) * a[..., None] + np.array(stroke_color, np.float32) * (1 - a[..., None])
        out[..., :3] = col
        out[..., 3] = np.maximum(a, e)
    return out

# ---------------------------------------------------------------- GL
VS_QUAD = '''
#version 430
uniform mat3 Hm;      // uv(0..1) → 画面px（同次）
uniform vec2 res;
in vec2 in_uv;
out vec2 uv;
out vec2 suv;
void main(){
    vec3 p = Hm * vec3(in_uv, 1.0);
    uv = in_uv;
    gl_Position = vec4(2.0*p.x/res.x - p.z, p.z - 2.0*p.y/res.y, 0.0, p.z);
    suv = vec2(p.x/p.z/res.x, p.y/p.z/res.y);
}
'''
VS_FULL = '''
#version 430
in vec2 in_uv;
out vec2 uv;
void main(){ uv = in_uv; gl_Position = vec4(in_uv*2.0-1.0, 0.0, 1.0); }
'''
FS_TEX = '''
#version 430
uniform sampler2D tex;
uniform vec4 tint;      // rgb 乗算、a 不透明度
uniform vec3 add;       // 加算色（α内）
uniform float flipY;
in vec2 uv; out vec4 f;
void main(){
    vec2 u = vec2(uv.x, flipY > 0.5 ? 1.0-uv.y : uv.y);
    vec4 c = texture(tex, u);       // 前乗算済み
    c.rgb = c.rgb*tint.rgb + add*c.a;
    f = c*tint.a;
}
'''
HEADER = '#version 430\nin vec2 uv; in vec2 suv; out vec4 f;\nuniform vec2 res; uniform float time;\n'


class Target:
    def __init__(self, ctx, w, h, dtype='f2', comps=4, samples=0):
        self.w, self.h = w, h
        self.tex = ctx.texture((w, h), comps, dtype=dtype)
        self.tex.filter = (moderngl.LINEAR, moderngl.LINEAR)
        self.tex.repeat_x = self.tex.repeat_y = False
        self.fbo = ctx.framebuffer([self.tex])


class GL:
    def __init__(self, W=1920, H=1080, ss=2):
        self.W, self.H, self.ss = W, H, ss
        self.VW, self.VH = W, H          # 台本の座標系（仮想 1920x1080）
        self.ctx = moderngl.create_standalone_context(require=430)
        ctx = self.ctx
        self.view = m3_scale(ss)
        quad = np.array([0, 0, 1, 0, 0, 1, 1, 1], np.float32)
        self.vbo = ctx.buffer(quad.tobytes())
        self._progs = {}
        self._vaos = {}
        self._texcache = {}
        IW, IH = W * ss, H * ss
        self.main = Target(ctx, IW, IH)
        self.acc = Target(ctx, IW, IH, dtype='f4')
        self.tmp = [Target(ctx, IW, IH), Target(ctx, IW, IH)]
        self.out8 = Target(ctx, W, H, dtype='f1', comps=3)
        self.time = 0.0
        # ブルーム用の縮小段
        self.blm = []
        w, h = IW, IH
        for i in range(7):
            w, h = max(1, w // 2), max(1, h // 2)
            self.blm.append((Target(ctx, w, h), Target(ctx, w, h)))

    # ---- プログラム
    def prog(self, fs, vs=VS_QUAD):
        key = (vs, fs)
        if key not in self._progs:
            try:
                p = self.ctx.program(vertex_shader=vs, fragment_shader=fs)
            except Exception:
                lines = fs.split('\n')
                print('\n'.join(f'{i + 1:4d} {l}' for i, l in enumerate(lines)), file=sys.stderr)
                raise
            self._progs[key] = p
            self._vaos[key] = self.ctx.vertex_array(p, [(self.vbo, '2f', 'in_uv')])
        return self._progs[key], self._vaos[key]

    def _set(self, p, uniforms):
        for k, v in uniforms.items():
            if k not in p:
                continue
            u = p[k]
            if isinstance(v, np.ndarray):
                if v.shape == (3, 3):
                    u.write(v.astype('f4').T.tobytes())
                else:
                    u.value = tuple(float(x) for x in v.ravel())
            elif isinstance(v, (tuple, list)):
                u.value = tuple(float(x) for x in v)
            else:
                u.value = float(v) if not isinstance(v, bool) else v

    # ---- テクスチャ
    def texture(self, arr, key=None, mipmap=True, premul=True, repeat=False):
        """arr: (h,w,c) float(0..1) か uint8。ストレートαなら前乗算して載せる"""
        if key is not None and key in self._texcache:
            return self._texcache[key]
        a = np.asarray(arr)
        if a.ndim == 2:
            a = a[..., None]
        if a.dtype == np.uint8:
            a = a.astype(np.float32) / 255
        a = a.astype(np.float32)
        if a.shape[2] == 3:
            a = np.concatenate([a, np.ones(a.shape[:2] + (1,), np.float32)], -1)
        if premul and a.shape[2] == 4:
            a = a.copy(); a[..., :3] *= a[..., 3:4]
        h, w, c = a.shape
        t = self.ctx.texture((w, h), c, data=np.ascontiguousarray(a.astype(np.float16)).tobytes(), dtype='f2')
        if mipmap:
            t.build_mipmaps()
            t.filter = (moderngl.LINEAR_MIPMAP_LINEAR, moderngl.LINEAR)
            t.anisotropy = 16.0
        else:
            t.filter = (moderngl.LINEAR, moderngl.LINEAR)
        t.repeat_x = t.repeat_y = repeat
        if key is not None:
            self._texcache[key] = t
        return t

    def image(self, path, maxw=None, key=None):
        key = key or ('img', path, maxw)
        if key in self._texcache:
            return self._texcache[key]
        im = Image.open(path).convert('RGBA')
        if maxw and im.width > maxw:
            im = im.resize((maxw, int(im.height * maxw / im.width)), Image.LANCZOS)
        t = self.texture(np.asarray(im), key=key)
        return t

    def text(self, text, size, **kw):
        """文字を内部解像度で画像化。.extra に仮想座標での大きさ倍率（1/ss）を持つ"""
        key = ('txt', text, size, tuple(sorted((k, str(v)) for k, v in kw.items())))
        if key in self._texcache:
            return self._texcache[key]
        kw = dict(kw)
        if 'stroke' in kw:
            kw['stroke'] = int(round(kw['stroke'] * self.ss))
        arr = text_rgba(text, size * self.ss, **kw)
        t = self.texture(arr, key=key)
        t.extra = 1.0 / self.ss
        return t

    def vsize(self, tex):
        k = tex.extra if isinstance(tex.extra, float) else 1.0
        return tex.size[0] * k, tex.size[1] * k

    # ---- 描画
    def use(self, target, clear=None):
        target.fbo.use()
        if clear is not None:
            target.fbo.clear(*clear)
        self.cur = target

    def blend(self, mode):
        ctx = self.ctx
        if mode is None:
            ctx.disable(moderngl.BLEND)
            return
        ctx.enable(moderngl.BLEND)
        ctx.blend_equation = moderngl.FUNC_ADD
        if mode == 'over':
            ctx.blend_func = (moderngl.ONE, moderngl.ONE_MINUS_SRC_ALPHA)
        elif mode == 'add':
            ctx.blend_func = (moderngl.ONE, moderngl.ONE)
        elif mode == 'screen':
            ctx.blend_func = (moderngl.ONE, moderngl.ONE_MINUS_SRC_COLOR)
        elif mode == 'mul':
            ctx.blend_func = (moderngl.DST_COLOR, moderngl.ONE_MINUS_SRC_ALPHA)

    def draw(self, fs, Hm, uniforms=None, textures=None, blend='over'):
        """独自シェーダの板を描く。Hm は uv→画面 の 3x3"""
        p, vao = self.prog(fs)
        u = dict(res=(self.cur.w, self.cur.h), time=self.time, ss=float(self.cur.w) / self.VW,
                 Hm=(m3_scale(self.cur.w / self.VW) @ np.asarray(Hm, np.float64)))
        if uniforms:
            u.update(uniforms)
        self._set(p, u)
        for i, (name, tex) in enumerate((textures or {}).items()):
            tex.use(i)
            if name in p:
                p[name].value = i
        self.blend(blend)
        vao.render(moderngl.TRIANGLE_STRIP)

    def quad(self, tex, Hm, alpha=1.0, tint=(1, 1, 1), add=(0, 0, 0), blend='over'):
        if Hm is None or alpha <= 0.001:
            return
        self.draw(FS_TEX, Hm, dict(tint=(*tint, alpha), add=add, flipY=0.0), dict(tex=tex), blend)

    def rect_h(self, x, y, w, h, angle=0.0, anchor=(0.5, 0.5)):
        """画面上の矩形（中心 x,y、回転 angle）への射影行列"""
        M = m3_translate(x, y) @ m3_rot(angle) @ m3_translate(-anchor[0] * w, -anchor[1] * h) @ m3_scale(w, h)
        return M

    def sprite(self, tex, x, y, scale=1.0, angle=0.0, alpha=1.0, anchor=(0.5, 0.5), tint=(1, 1, 1), blend='over', add=(0, 0, 0)):
        w, h = self.vsize(tex)
        self.quad(tex, self.rect_h(x, y, w * scale, h * scale, angle, anchor), alpha, tint, add, blend)

    def fullscreen(self, fs, uniforms=None, textures=None, blend='over'):
        Hm = m3_scale(self.VW, self.VH)
        self.draw(fs, Hm, uniforms, textures, blend)

    # ---- 仕上げ
    def _pass(self, fs, target, uniforms, textures):
        p, vao = self.prog(fs, VS_FULL)
        self._set(p, dict(uniforms, res=(target.w, target.h), time=self.time))
        for i, (name, tex) in enumerate(textures.items()):
            tex.use(i)
            if name in p:
                p[name].value = i
        target.fbo.use()
        self.ctx.disable(moderngl.BLEND)
        vao.render(moderngl.TRIANGLE_STRIP)

    def bloom(self, src, threshold=1.0, knee=0.5):
        """src(Target) の明部を縮小段でぼかし、最上段のテクスチャを返す"""
        fs_down = '''#version 430
        uniform sampler2D tex; uniform vec2 res; uniform float thr, knee, first;
        in vec2 uv; out vec4 f;
        void main(){
            vec2 px = 1.0/res;
            vec3 c = vec3(0);
            c += texture(tex, uv+px*vec2(-1,-1)).rgb; c += texture(tex, uv+px*vec2(1,-1)).rgb;
            c += texture(tex, uv+px*vec2(-1,1)).rgb;  c += texture(tex, uv+px*vec2(1,1)).rgb;
            c *= 0.25;
            if(first>0.5){
                float b = max(c.r, max(c.g, c.b));
                float s = clamp(b-thr+knee, 0.0, 2.0*knee); s = s*s/(4.0*knee+1e-4);
                c *= max(s, b-thr)/max(b, 1e-4);
            }
            f = vec4(c, 1);
        }'''
        fs_blur = '''#version 430
        uniform sampler2D tex; uniform vec2 res; uniform vec2 dir;
        in vec2 uv; out vec4 f;
        void main(){
            vec2 px = dir/res;
            vec3 c = texture(tex, uv).rgb*0.227027;
            c += (texture(tex, uv+px*1.3846).rgb + texture(tex, uv-px*1.3846).rgb)*0.3162162;
            c += (texture(tex, uv+px*3.2308).rgb + texture(tex, uv-px*3.2308).rgb)*0.0702703;
            f = vec4(c,1);
        }'''
        fs_up = '''#version 430
        uniform sampler2D tex; uniform sampler2D low; uniform vec2 res;
        in vec2 uv; out vec4 f;
        void main(){ f = vec4(texture(tex, uv).rgb + texture(low, uv).rgb, 1); }'''
        cur = src.tex
        for i, (a, b) in enumerate(self.blm):
            self._pass(fs_down, a, dict(thr=threshold, knee=knee, first=1.0 if i == 0 else 0.0), dict(tex=cur))
            self._pass(fs_blur, b, dict(dir=(1, 0)), dict(tex=a.tex))
            self._pass(fs_blur, a, dict(dir=(0, 1)), dict(tex=b.tex))
            cur = a.tex
        for i in range(len(self.blm) - 2, -1, -1):
            a, b = self.blm[i]
            self._pass(fs_up, b, {}, dict(tex=a.tex, low=self.blm[i + 1][0].tex))
            self.blm[i] = (b, a)
        return self.blm[0][0].tex

    FS_FINAL = '''#version 430
    uniform sampler2D tex; uniform sampler2D bl; uniform vec2 res; uniform float time;
    uniform float bloomAmt, vign, grainAmt, ca, exposure, sat, contrast, flash, fade, letterbox, invert, mono;
    uniform vec3 lift, gain, flashCol;
    in vec2 uv; out vec4 f;
    float h12(vec2 p){ return fract(sin(dot(p, vec2(12.9898,78.233)))*43758.5453); }
    void main(){
        vec2 d = uv-0.5;
        vec3 c;
        if(ca>0.0){
            vec2 o = d*ca/res.x*2.0;
            c = vec3(texture(tex, uv+o).r, texture(tex, uv).g, texture(tex, uv-o).b);
        } else c = texture(tex, uv).rgb;
        c += texture(bl, uv).rgb*bloomAmt;
        c *= exposure;
        // 0.8 までは素通し、それより上をなめらかに丸める（イラストの色を変えない）
        vec3 hi = 0.8 + 0.2*(1.0 - exp(-(c - 0.8)/0.2));
        c = mix(c, hi, step(0.8, c));
        c = clamp(c, 0.0, 1.0);
        float l = dot(c, vec3(0.2126,0.7152,0.0722));
        c = mix(vec3(l), c, sat);
        c = (c-0.5)*contrast+0.5;
        c = c*gain + lift*(1.0-c);
        c *= 1.0 - vign*pow(length(d*vec2(1.0, res.y/res.x))*1.35, 2.4);
        c = mix(c, flashCol, flash);
        if(mono>0.0){ float m = dot(c, vec3(0.3,0.59,0.11)); c = mix(c, vec3(m), mono); }
        if(invert>0.0) c = mix(c, 1.0-c, invert);
        c *= 1.0-fade;
        float g = h12(uv*res + fract(time*7.13)*vec2(91.7,47.3)) - 0.5;
        c += g*grainAmt;
        float lb = letterbox*res.y;
        if(uv.y*res.y < lb || uv.y*res.y > res.y-lb) c = vec3(0);
        f = vec4(clamp(c,0.0,1.0), 1);
    }'''

    def finish(self, post=None):
        """main を仕上げて out8 へ。RGB uint8 の bytes を返す"""
        pp = dict(bloomAmt=0.6, bloomThr=1.1, vign=0.25, grainAmt=0.025, ca=0.0, exposure=1.0, sat=1.0, contrast=1.0,
                  flash=0.0, flashCol=(1, 1, 1), fade=0.0, letterbox=0.0, invert=0.0, mono=0.0,
                  lift=(0, 0, 0), gain=(1, 1, 1))
        if post:
            pp.update(post)
        src = pp.pop('src', self.main)
        bl = self.bloom(src, pp.pop('bloomThr'))
        self._pass(self.FS_FINAL, self.out8, pp, dict(tex=src.tex, bl=bl))
        data = self.out8.fbo.read(components=3, alignment=1)
        # GL は下から上の順で読むので上下反転
        arr = np.frombuffer(data, np.uint8).reshape(self.H, self.W, 3)[::-1]
        return arr

    PART_VS = '''
    #version 430
    uniform vec2 res; uniform mat3 V;
    in vec2 in_uv;
    in vec4 i_pos;     // x, y, size, angle（仮想px）
    in vec4 i_col;     // 色（前乗算しない）と α
    in vec4 i_aux;     // 形の補助値
    out vec2 uv; out vec4 col; out vec4 aux;
    void main(){
        vec2 q = (in_uv - 0.5)*i_pos.z;
        float c = cos(i_pos.w), s = sin(i_pos.w);
        q = vec2(c*q.x - s*q.y, s*q.x + c*q.y);
        vec3 p = V*vec3(i_pos.xy + q, 1.0);
        uv = in_uv; col = i_col; aux = i_aux;
        gl_Position = vec4(2.0*p.x/res.x - 1.0, 1.0 - 2.0*p.y/res.y, 0.0, 1.0);
    }'''

    def particles(self, fs, pos, col, aux=None, blend='add', uniforms=None, textures=None):
        """pos (N,4) x,y,size,angle（仮想px）、col (N,4)、aux (N,4)。fs は uv, col, aux から色を返す"""
        n = len(pos)
        if n == 0:
            return
        key = ('part', fs)
        if key not in self._progs:
            self._progs[key] = self.ctx.program(vertex_shader=self.PART_VS, fragment_shader=fs)
        p = self._progs[key]
        if aux is None:
            aux = np.zeros((n, 4), np.float32)
        inst = np.concatenate([np.asarray(pos, np.float32), np.asarray(col, np.float32), np.asarray(aux, np.float32)], 1)
        ib = self.ctx.buffer(np.ascontiguousarray(inst).tobytes())
        vao = self.ctx.vertex_array(p, [(self.vbo, '2f', 'in_uv'), (ib, '4f 4f 4f/i', 'i_pos', 'i_col', 'i_aux')])
        u = dict(res=(self.cur.w, self.cur.h), time=self.time, V=m3_scale(self.cur.w / self.VW))
        if uniforms:
            u.update(uniforms)
        self._set(p, u)
        for i, (name, tex) in enumerate((textures or {}).items()):
            tex.use(i)
            if name in p:
                p[name].value = i
        self.blend(blend)
        vao.render(moderngl.TRIANGLE_STRIP, instances=n)
        vao.release(); ib.release()

    def accumulate(self, weight, first):
        """main を acc へ重み付きで足す（モーションブラー用）"""
        fs = '''#version 430
        uniform sampler2D tex; uniform float w; in vec2 uv; out vec4 f;
        void main(){ f = texture(tex, uv)*w; }'''
        p, vao = self.prog(fs, VS_FULL)
        self.main.tex.use(0); p['tex'].value = 0; p['w'].value = weight
        self.acc.fbo.use()
        if first:
            self.acc.fbo.clear(0, 0, 0, 0)
        self.blend('add')
        vao.render(moderngl.TRIANGLE_STRIP)

    def copy_main(self):
        """main の現在内容を別テクスチャへ写す（歪み・ぼかし等の入力用）"""
        t = self.tmp[0]
        self.ctx.copy_framebuffer(t.fbo, self.main.fbo)
        return t.tex


def read_audio(path, sr=48000):
    """音声ファイルを (N,2) float32 へ"""
    p = subprocess.run([ffmpeg_exe(), '-v', 'error', '-i', path, '-f', 'f32le', '-ac', '2', '-ar', str(sr), '-'],
                       stdout=subprocess.PIPE, check=True)
    return np.frombuffer(p.stdout, np.float32).reshape(-1, 2).copy()
