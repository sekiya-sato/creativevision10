# 3D の描画（飛び出す絵本用）: メッシュ、影（シャドウマップ・PCF）、紙の質感、ボケ（被写界深度）
import math
import numpy as np
import moderngl

# ---------------------------------------------------------------- 行列（列優先で GL へ渡す）
def perspective(fov_deg, aspect, near, far):
    f = 1 / math.tan(math.radians(fov_deg) / 2)
    m = np.zeros((4, 4))
    m[0, 0] = f / aspect; m[1, 1] = f
    m[2, 2] = (far + near) / (near - far); m[2, 3] = 2 * far * near / (near - far)
    m[3, 2] = -1
    return m


def ortho(l, r, b, t, n, f):
    m = np.eye(4)
    m[0, 0] = 2 / (r - l); m[1, 1] = 2 / (t - b); m[2, 2] = -2 / (f - n)
    m[0, 3] = -(r + l) / (r - l); m[1, 3] = -(t + b) / (t - b); m[2, 3] = -(f + n) / (f - n)
    return m


def look_at(eye, target, up=(0, 1, 0)):
    eye, target, up = map(lambda v: np.asarray(v, float), (eye, target, up))
    f = target - eye; f /= np.linalg.norm(f)
    s = np.cross(f, up); s /= np.linalg.norm(s)
    u = np.cross(s, f)
    m = np.eye(4)
    m[0, :3] = s; m[1, :3] = u; m[2, :3] = -f
    m[:3, 3] = -m[:3, :3] @ eye
    return m


def translate(x, y, z):
    m = np.eye(4); m[:3, 3] = (x, y, z); return m


def rotx(a):
    c, s = math.cos(a), math.sin(a); m = np.eye(4); m[1, 1] = c; m[1, 2] = -s; m[2, 1] = s; m[2, 2] = c; return m


def roty(a):
    c, s = math.cos(a), math.sin(a); m = np.eye(4); m[0, 0] = c; m[0, 2] = s; m[2, 0] = -s; m[2, 2] = c; return m


def rotz(a):
    c, s = math.cos(a), math.sin(a); m = np.eye(4); m[0, 0] = c; m[0, 1] = -s; m[1, 0] = s; m[1, 1] = c; return m


def scale(x, y=None, z=None):
    return np.diag([x, x if y is None else y, x if z is None else z, 1.0])


def mat_bytes(m):
    return np.asarray(m, 'f4').T.copy().tobytes()


# ---------------------------------------------------------------- メッシュ
class Mesh:
    def __init__(self, pos, nrm, uv, idx):
        self.pos = np.asarray(pos, 'f4'); self.nrm = np.asarray(nrm, 'f4'); self.uv = np.asarray(uv, 'f4')
        self.idx = np.asarray(idx, 'i4')

    def data(self):
        return np.concatenate([self.pos, self.nrm, self.uv], 1).astype('f4')


def grid(nx, ny, fn):
    """fn(u, v) -> (pos, normal)。u,v は 0..1。uv は (u, v)"""
    us = np.linspace(0, 1, nx + 1); vs = np.linspace(0, 1, ny + 1)
    U, V = np.meshgrid(us, vs)
    P, Nn = fn(U, V)
    pos = P.reshape(-1, 3); nrm = Nn.reshape(-1, 3)
    uv = np.stack([U.ravel(), V.ravel()], 1)
    idx = []
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            idx += [a, a + 1, a + nx + 2, a, a + nx + 2, a + nx + 1]
    return Mesh(pos, nrm, uv, idx)


def quad(w, h):
    """xy 平面の板（中心原点、法線 +z、uv は左上 (0,0)）"""
    pos = [(-w / 2, h / 2, 0), (w / 2, h / 2, 0), (w / 2, -h / 2, 0), (-w / 2, -h / 2, 0)]
    nrm = [(0, 0, 1)] * 4
    uv = [(0, 0), (1, 0), (1, 1), (0, 1)]
    return Mesh(pos, nrm, uv, [0, 2, 1, 0, 3, 2])


def box(w, h, d, uv_front=True):
    """箱（中心原点）。各面の uv は 0..1"""
    P, N_, U, I = [], [], [], []
    faces = [((0, 0, 1), (1, 0, 0), (0, 1, 0)), ((0, 0, -1), (-1, 0, 0), (0, 1, 0)), ((1, 0, 0), (0, 0, -1), (0, 1, 0)),
             ((-1, 0, 0), (0, 0, 1), (0, 1, 0)), ((0, 1, 0), (1, 0, 0), (0, 0, -1)), ((0, -1, 0), (1, 0, 0), (0, 0, 1))]
    half = np.array([w, h, d]) / 2
    for n, r, u in faces:
        n, r, u = map(np.array, (n, r, u))
        c = n * half
        rr = r * np.abs(r @ half); uu = u * np.abs(u @ half)
        base = len(P)
        for (a, b), (s, t) in zip([(-1, 1), (1, 1), (1, -1), (-1, -1)], [(0, 0), (1, 0), (1, 1), (0, 1)]):
            P.append(c + rr * a + uu * b); N_.append(n); U.append((s, t))
        I += [base, base + 2, base + 1, base, base + 3, base + 2]
    return Mesh(P, N_, U, I)


# ---------------------------------------------------------------- シェーダ
VS = '''
#version 430
uniform mat4 M, V, P, LVP;
in vec3 in_pos; in vec3 in_nrm; in vec2 in_uv;
out vec3 wpos; out vec3 wn; out vec2 uv; out vec4 lpos;
void main(){
    vec4 w = M*vec4(in_pos, 1.0);
    wpos = w.xyz;
    wn = normalize(mat3(M)*in_nrm);
    uv = in_uv;
    lpos = LVP*w;
    gl_Position = P*V*w;
}
'''
SHADOW_VS = '''
#version 430
uniform mat4 M, LVP;
in vec3 in_pos; in vec3 in_nrm; in vec2 in_uv;
out vec2 uv;
void main(){ uv = in_uv; gl_Position = LVP*M*vec4(in_pos, 1.0); }
'''
SHADOW_FS = '''
#version 430
uniform sampler2D tex; uniform float alphaTest; uniform float premul;
in vec2 uv; out vec4 f;
void main(){
    float a = texture(tex, uv).a;
    if(a < alphaTest) discard;
    f = vec4(1);
}
'''
FS = '''
#version 430
uniform sampler2D tex; uniform sampler2D shadow; uniform sampler2D tex2; uniform float useTex2;
uniform vec3 eye, Ldir, Lcol, skyCol, groundCol;
uniform float alphaTest, premul, emis, paper, mode, backWhite, specAmt, shadowSoft;
uniform vec3 tint, backCol;
uniform vec2 uvScale;
in vec3 wpos; in vec3 wn; in vec2 uv; in vec4 lpos;
out vec4 f;
float h21(vec2 p){ p = fract(p*vec2(123.34, 456.21)); p += dot(p, p+45.32); return fract(p.x*p.y); }
float vnoise(vec2 p){ vec2 i=floor(p), q=fract(p); vec2 u=q*q*(3.0-2.0*q);
    return mix(mix(h21(i),h21(i+vec2(1,0)),u.x), mix(h21(i+vec2(0,1)),h21(i+vec2(1,1)),u.x), u.y); }
float fbm(vec2 p){ float s=0.0, a=0.5; for(int i=0;i<5;i++){ s+=a*vnoise(p); p*=2.03; a*=0.5; } return s; }
float shade(vec3 n){
    vec3 p = lpos.xyz/lpos.w*0.5 + 0.5;
    if(p.x<0.0||p.x>1.0||p.y<0.0||p.y>1.0) return 1.0;
    float bias = 0.0006 + 0.0015*(1.0 - abs(dot(n, Ldir)));
    float s = 0.0;
    vec2 ts = 1.0/vec2(textureSize(shadow, 0));
    float ang = h21(gl_FragCoord.xy)*6.2831;
    mat2 R = mat2(cos(ang), -sin(ang), sin(ang), cos(ang));
    for(int i=0;i<16;i++){
        float r = sqrt((float(i)+0.5)/16.0);
        float th = float(i)*2.39996;
        vec2 o = R*vec2(cos(th), sin(th))*r*shadowSoft;
        float d = texture(shadow, p.xy + o*ts).r;
        s += (p.z - bias > d) ? 0.0 : 1.0;
    }
    return s/16.0;
}
void main(){
    vec4 c;
    vec3 n = normalize(wn);
    // 表裏は三角形の向きではなく、法線が視点を向くかで決める（両面の紙）
    bool front = dot(n, eye - wpos) > 0.0;
    if(!front) n = -n;
    if(mode > 0.5){
        // 木の机（x 方向に長い板、板ごとに色と木目を変える）
        float plank = floor(wpos.z/1.35);
        float pr = h21(vec2(plank, 7.0));
        vec2 q = vec2(wpos.x*0.35 + pr*13.0, wpos.z*9.0);
        float grain = sin(q.y*2.0 + fbm(q*vec2(1.0, 0.25))*7.0 + fbm(q*vec2(4.0, 1.0))*1.5)*0.5 + 0.5;
        float fine = fbm(vec2(wpos.x*2.0, wpos.z*60.0));
        vec3 w1 = vec3(0.42, 0.27, 0.15), w2 = vec3(0.58, 0.40, 0.24);
        vec3 wc = mix(w1, w2, grain*0.55 + fine*0.45)*(0.88 + 0.24*pr);
        float seam = smoothstep(0.012, 0.0, abs(fract(wpos.z/1.35) - 0.0)*1.35);
        wc *= 1.0 - 0.45*seam;
        c = vec4(wc, 1.0);
    } else {
        c = (!front && useTex2 > 0.5) ? texture(tex2, vec2(1.0 - uv.x, uv.y)) : texture(tex, uv*uvScale);
        if(premul > 0.5 && c.a > 0.0) c.rgb /= c.a;
        if(c.a < alphaTest) discard;
        if(!front && backWhite > 0.5 && useTex2 < 0.5) c.rgb = backCol;
        c.rgb *= tint;
    }
    if(paper > 0.0){
        float fib = fbm(wpos.xz*180.0 + wpos.y*40.0) - 0.5;
        c.rgb *= 1.0 + fib*0.06*paper;
    }
    float sh = shade(n);
    float lam = max(dot(n, Ldir), 0.0);
    vec3 amb = mix(groundCol, skyCol, n.y*0.5 + 0.5);
    vec3 V = normalize(eye - wpos);
    vec3 Hh = normalize(Ldir + V);
    float spec = pow(max(dot(n, Hh), 0.0), 40.0)*specAmt;
    vec3 col = c.rgb*(amb + Lcol*lam*sh) + Lcol*spec*sh + c.rgb*emis;
    f = vec4(col, 1.0);
}
'''

DOF_FS = '''
#version 430
uniform sampler2D tex; uniform sampler2D depth; uniform vec2 res;
uniform float focus, range, maxR, near, far;
in vec2 uv; out vec4 f;
float lin(float d){ float z = d*2.0 - 1.0; return 2.0*near*far/(far + near - z*(far - near)); }
void main(){
    float zc = lin(texture(depth, uv).r);
    float coc = clamp(abs(zc - focus)/range, 0.0, 1.0)*maxR;
    vec3 acc = vec3(0); float wsum = 0.0;
    for(int i=0;i<48;i++){
        float r = sqrt((float(i)+0.5)/48.0);
        float th = float(i)*2.39996;
        vec2 o = vec2(cos(th), sin(th))*r*coc/res;
        vec2 q = uv + o;
        float zs = lin(texture(depth, q).r);
        float cs = clamp(abs(zs - focus)/range, 0.0, 1.0)*maxR;
        float w = (zs < zc) ? smoothstep(0.0, 1.0, cs/(r*coc + 1e-3)) : 1.0;   // 手前のボケは背後へにじむ
        w = max(w, 0.05);
        acc += texture(tex, q).rgb*w; wsum += w;
    }
    f = vec4(acc/wsum, 1.0);
}
'''


class Item:
    __slots__ = ('mesh', 'M', 'tex', 'tex2', 'mat', 'shadow')


class R3:
    def __init__(self, gl, shadow_size=4096):
        self.gl = gl
        ctx = gl.ctx
        self.ctx = ctx
        IW, IH = gl.main.w, gl.main.h
        self.depth = ctx.depth_texture((IW, IH))
        self.fbo = ctx.framebuffer(color_attachments=[gl.main.tex], depth_attachment=self.depth)
        self.sm = ctx.depth_texture((shadow_size, shadow_size))
        self.sm.repeat_x = self.sm.repeat_y = False
        self.smfbo = ctx.framebuffer(depth_attachment=self.sm)
        self.prog = ctx.program(vertex_shader=VS, fragment_shader=FS)
        self.sprog = ctx.program(vertex_shader=SHADOW_VS, fragment_shader=SHADOW_FS)
        self.white = gl.texture(np.ones((4, 4, 4), np.float32), key=('white3d',), mipmap=False, premul=False)
        self.items = []
        self._cache = {}

    def clear(self):
        self.items = []

    def add(self, mesh, M, tex=None, tex2=None, **mat):
        it = Item()
        it.mesh, it.M, it.tex = mesh, np.asarray(M, float), tex or self.white
        it.tex2 = tex2
        m = dict(alphaTest=0.5, premul=0.0, emis=0.0, paper=1.0, mode=0.0, backWhite=1.0, specAmt=0.05,
                 tint=(1, 1, 1), backCol=(0.95, 0.93, 0.88), uvScale=(1, 1), useTex2=1.0 if tex2 is not None else 0.0)
        m.update(mat)
        it.mat = m
        it.shadow = mat.get('cast', True)
        it.mat.pop('cast', None)
        self.items.append(it)

    def _vao(self, prog, mesh):
        key = (id(prog), id(mesh))
        ent = self._cache.get(key)
        if ent is None or ent[0] is not mesh:
            vbo = self.ctx.buffer(mesh.data().tobytes())
            ibo = self.ctx.buffer(mesh.idx.tobytes())
            fmt, names = [], []
            for nm, f in (('in_pos', '3f'), ('in_nrm', '3f'), ('in_uv', '2f')):
                if nm in prog:
                    fmt.append(f); names.append(nm)
                else:
                    fmt.append({'3f': '12x', '2f': '8x'}[f])
            vao = self.ctx.vertex_array(prog, [(vbo, ' '.join(fmt), *names)], ibo)
            ent = (mesh, vao, vbo, ibo)
            if len(self._cache) > 400:
                for k, v in list(self._cache.items())[:100]:
                    v[1].release(); v[2].release(); v[3].release(); del self._cache[k]
            self._cache[key] = ent
        return ent[1]

    def render(self, eye, target, fov, light_dir, light_col=(1.0, 0.92, 0.8), sky=(0.32, 0.34, 0.45), ground=(0.25, 0.18, 0.12),
               light_box=8.0, near=0.05, far=60.0, shadow_soft=2.5, clear=(0.05, 0.04, 0.06), up=(0, 1, 0)):
        gl, ctx = self.gl, self.ctx
        Ld = np.asarray(light_dir, float); Ld /= np.linalg.norm(Ld)
        LV = look_at(np.asarray(target, float) + Ld * 20, target, (0, 1, 0) if abs(Ld[1]) < 0.95 else (0, 0, 1))
        LP = ortho(-light_box, light_box, -light_box, light_box, 1, 45)
        LVP = LP @ LV
        # 影
        self.smfbo.use(); self.smfbo.clear(depth=1.0)
        ctx.enable(moderngl.DEPTH_TEST); ctx.disable(moderngl.BLEND); ctx.disable(moderngl.CULL_FACE)
        self.sprog['LVP'].write(mat_bytes(LVP))
        for it in self.items:
            if not it.shadow:
                continue
            self.sprog['M'].write(mat_bytes(it.M))
            it.tex.use(0); self.sprog['tex'].value = 0
            self.sprog['alphaTest'].value = it.mat['alphaTest']
            self._vao(self.sprog, it.mesh).render()
        # 本描画
        self.fbo.use()
        self.fbo.clear(*clear, 1.0, depth=1.0)
        ctx.enable(moderngl.DEPTH_TEST)
        aspect = gl.main.w / gl.main.h
        P = perspective(fov, aspect, near, far)
        V = look_at(eye, target, up)
        p = self.prog
        p['P'].write(mat_bytes(P)); p['V'].write(mat_bytes(V)); p['LVP'].write(mat_bytes(LVP))
        p['eye'].value = tuple(map(float, eye)); p['Ldir'].value = tuple(map(float, Ld)); p['Lcol'].value = tuple(light_col)
        p['skyCol'].value = tuple(sky); p['groundCol'].value = tuple(ground); p['shadowSoft'].value = shadow_soft
        self.sm.use(1); p['shadow'].value = 1
        for it in self.items:
            p['M'].write(mat_bytes(it.M))
            it.tex.use(0); p['tex'].value = 0
            (it.tex2 or it.tex).use(2); p['tex2'].value = 2
            for k, v in it.mat.items():
                if k in p:
                    p[k].value = tuple(map(float, v)) if isinstance(v, (tuple, list)) else float(v)
            self._vao(p, it.mesh).render()
        ctx.disable(moderngl.DEPTH_TEST)
        self.near, self.far = near, far
        self.V = V; self.P = P
        gl.use(gl.main)

    def project(self, pts):
        """ワールド座標 → 仮想画面 px（1920x1080）"""
        pts = np.atleast_2d(np.asarray(pts, float))
        h = np.concatenate([pts, np.ones((len(pts), 1))], 1) @ (self.P @ self.V).T
        ndc = h[:, :3] / h[:, 3:4]
        x = (ndc[:, 0] * 0.5 + 0.5) * self.gl.VW
        y = (1 - (ndc[:, 1] * 0.5 + 0.5)) * self.gl.VH
        return np.stack([x, y], 1), h[:, 3]

    def dof(self, focus, range_, max_r=14.0):
        gl = self.gl
        src = gl.copy_main()
        gl._pass(DOF_FS, gl.tmp[1], dict(focus=focus, range=range_, maxR=max_r * gl.ss, near=self.near, far=self.far),
                 dict(tex=src, depth=self.depth))
        gl.ctx.copy_framebuffer(gl.main.fbo, gl.tmp[1].fbo)
        gl.use(gl.main)
