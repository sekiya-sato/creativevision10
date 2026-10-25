# 主人公のリグ（一枚絵を Live2D 風に動かす）
# 頭の傾き・帽子とリボンの追従・髪の揺れ・イヤリングの振り子・呼吸・手のタイピング・まばたき・口パク・表情の差し替えを
# 1枚の GLSL シェーダで行う。素材は prep.py が work/rig に作る。
import os, math
import numpy as np
from PIL import Image
import engine as E

RIG = os.path.join(E.WORK, 'rig')
S = 2048.0

CHAR_FS = E.HEADER + r'''
uniform sampler2D tN, tHP, tST, tHT, tNm, tHPm, tSTm, tNe, tHPe, tSTe, m1, m2, m3, m4;
uniform vec4 exprFull, exprFace;
uniform float headRot; uniform vec2 headPivot; uniform vec2 headShift;
uniform float hatRot; uniform vec2 hatPivot;
uniform float bowRot; uniform vec2 bowPivot;
uniform vec2 hairL, hairR; uniform float hairWave, bangAmp, phase;
uniform float earRot; uniform vec2 earPivot;
uniform float breath; uniform vec2 bodyShift;
uniform vec2 handL, handR;
uniform float blinkL, blinkR, eyeBotShift;
uniform float mouthOpen, mouthWide; uniform vec2 mouthC;
uniform float outlineW; uniform vec3 outlineCol; uniform float outlineA;
uniform vec3 rimCol; uniform vec2 rimDir; uniform float rimW;
uniform vec3 tintMul; uniform vec3 tintAdd; uniform float silhouette; uniform vec3 silCol;
uniform float opacity; uniform float cropBottom; uniform float alphaAll;
uniform sampler2D tProj; uniform float projAmt; uniform vec3 projCol; uniform vec4 projXf;
const float SZ = 2048.0;

vec4 T(sampler2D t, vec2 p){ return texture(t, p/SZ); }
vec2 rotInv(vec2 p, vec2 c, float a){ float cs=cos(-a), sn=sin(-a); vec2 d=p-c; return c+vec2(cs*d.x-sn*d.y, sn*d.x+cs*d.y); }

vec2 warp(vec2 p){
    vec4 a = T(m1,p), b = T(m2,p), c = T(m3,p);
    vec2 s = p;
    vec2 d = vec2(0);
    d += a.g*(rotInv(p, headPivot, headRot) - headShift - p);
    d += a.b*(rotInv(p, hatPivot, hatRot) - p);
    d += a.a*(rotInv(p, bowPivot, bowRot) - p);
    d -= b.r*(hairL + vec2(hairWave*sin(phase*2.1 - p.y*0.011), 0.0));
    d -= b.g*(hairR + vec2(hairWave*sin(phase*2.3 - p.y*0.011 + 1.3), 0.0));
    d -= c.a*vec2(bangAmp*sin(phase*1.7 - p.x*0.008), 0.0);
    d += b.b*(rotInv(p, earPivot, earRot) - p);
    d -= b.a*(vec2(0.0, (p.y-1950.0)*breath) + bodyShift);
    d -= c.r*handL + c.g*handR;
    return s + d;
}

// まばたき: 目を消した肌プレートで上から覆い、元のまつげの帯を列ごとに下ろす
uniform sampler2D eyeLut;
vec2 blinkMap(vec2 s, float bl, out float skinK){
    skinK = 0.0;
    if(bl <= 0.001) return s;
    vec4 e = texelFetch(eyeLut, ivec2(clamp(int(s.x), 0, 2047), 0), 0);
    if(e.w <= 0.0) return s;
    float top = e.x, bot = e.y, T_ = e.z;
    float Tc = T_*(1.0 - 0.35*bl);              // 閉じるほどまつげの帯を細く
    float d = bl*max(bot - top - Tc, 0.0)*e.w;
    if(s.y < top - 6.0 || s.y > bot) return s;
    if(s.y < top + d){ skinK = smoothstep(top - 6.0, top, s.y)*e.w; return s; }
    if(s.y < top + d + Tc) return vec2(s.x, top + (s.y - top - d)*T_/Tc);
    skinK = smoothstep(0.55, 0.95, bl)*smoothstep(top + d + Tc, top + d + Tc + 2.0, s.y);
    return s;
}

vec3 faceTex(vec2 s, vec4 w){
    vec3 c = vec3(0);
    if(w.x>0.0) c += w.x*T(tN,s).rgb;
    if(w.y>0.0) c += w.y*T(tHP,s).rgb;
    if(w.z>0.0) c += w.z*T(tST,s).rgb;
    if(w.w>0.0) c += w.w*T(tHT,s).rgb;
    return c;
}

vec4 shade(vec2 p){
    vec2 s = warp(p);
    float a = T(m1, s).r;
    vec3 col = faceTex(s, exprFull);
    float fm = T(m3, s).b;
    if(fm > 0.001){
        float blw = 1.0 - exprFace.w;   // ハイテンション顔は目を閉じているので無効
        float skinK;
        float blk = s.x < 1010.0 ? blinkL : blinkR;
        vec2 e = blinkMap(s, blk*blw, skinK);
        vec3 fc = faceTex(e, exprFace);
        if(skinK > 0.0){
            vec3 sk = exprFace.x*T(tNe,s).rgb + exprFace.y*T(tHPe,s).rgb + exprFace.z*T(tSTe,s).rgb + exprFace.w*T(tHT,s).rgb;
            fc = mix(fc, sk, skinK);
        }
        // 口: 閉じ口（通常・ストレス）と、開いた口（ハッピーの口を拡縮）
        float noHT = 1.0 - exprFace.w;
        float open = clamp(mouthOpen, 0.0, 1.2);
        // 閉じ口と開いた口は、開いた口が細い線のうちに短い幅で入れ替える。
        // 長く重ねると、位置の違う閉じ口が開いた口の下に透けて見える
        float openK = smoothstep(0.05, 0.065, open);
        float closedK = noHT*(1.0 - openK);
        vec4 mm = T(m4, s);
        float wn = exprFace.x + exprFace.y, ws = exprFace.z;
        if(closedK > 0.0){
            fc = mix(fc, T(tNm, s).rgb, mm.r*wn*closedK/(wn+ws+1e-4)*(wn+ws));
            fc = mix(fc, T(tSTm, s).rgb, mm.b*ws*closedK);
        }
        if(openK > 0.0 && noHT > 0.0){
            // 開きは左右の口角を結ぶ線へ向けて縮める（上端の水平線へ縮めると、低い位置の左の口角が持ち上がる）。
            // 元の口は右の口角が高く、そのままでは歪んで見えるため傾きを浅くする。小さく開くほど閉じ口に近い水平な口にする
            const vec2 MC0 = vec2(1088.5, 1076.0);   // ハッピーの口の左右の口角の中点
            const float MK0 = -0.0914;               // 口角を結ぶ線の傾き（左 1001,1084 → 右 1176,1068）
            vec2 sc = vec2(mix(0.55, 1.0, mouthWide), max(open, 0.05));
            float kd = MK0*mix(0.15, 0.7, clamp(open, 0.0, 1.0));
            float sx = MC0.x + (s.x - mouthC.x)/sc.x;
            float ld = mouthC.y + kd*(s.x - mouthC.x);
            vec2 sm = vec2(sx, MC0.y + MK0*(sx - MC0.x) + (s.y - ld)/sc.y);
            float ma = T(m4, sm).g*noHT*openK;
            fc = mix(fc, T(tHPm, sm).rgb, ma);
        }
        col = mix(col, fc, fm);
    }
    // 縁取り（切り抜きを膨らませた輪郭）
    float o = 0.0;
    if(outlineW > 0.0){
        for(int i=0;i<16;i++){
            float an = 6.2831853*float(i)/16.0;
            o = max(o, T(m1, s + vec2(cos(an), sin(an))*outlineW).r);
        }
        o = clamp(o - a, 0.0, 1.0)*outlineA;
    }
    col = col*tintMul + tintAdd;
    if(projAmt > 0.0){
        vec3 pj = texture(tProj, suv*projXf.xy + projXf.zw).rgb;
        col += pj*projCol*projAmt;
    }
    if(rimW > 0.0){
        float r = a*(1.0 - T(m1, s + rimDir*rimW).r);
        col += rimCol*r;
    }
    col = mix(col, silCol, silhouette);
    float bottomFade = cropBottom > 0.0 ? smoothstep(cropBottom, cropBottom-120.0, p.y) : 1.0;
    a *= bottomFade; o *= bottomFade;
    float A = a + o*(1.0-a);
    vec3 C = (col*a + outlineCol*o*(1.0-a));
    return vec4(C, A)*opacity;
}

void main(){
    vec2 p = uv*SZ;
    f = shade(p);
}
'''

PLATE_FS = E.HEADER + r'''
uniform sampler2D tex; uniform vec4 tint; uniform vec3 add; uniform float blur;
void main(){
    vec4 c = blur > 0.0 ? textureLod(tex, uv, blur) : texture(tex, uv);
    c.rgb = c.rgb*tint.rgb + add*c.a;
    f = c*tint.a;
}
'''


class Spring:
    """減衰ばね（二次の追従）。target へ x を引く"""
    def __init__(self, k=60.0, c=6.0, x=0.0):
        self.k, self.c, self.x, self.v = k, c, x, 0.0
    def step(self, target, dt):
        a = self.k * (target - self.x) - self.c * self.v
        self.v += a * dt
        self.x += self.v * dt
        return self.x


class Pose:
    """1コマぶんのリグ入力"""
    def __init__(self, **kw):
        self.head = 0.0          # 頭の傾き（度、+ で時計回り）
        self.head_shift = (0.0, 0.0)
        self.breath = 0.0
        self.blink = 0.0
        self.blinkL = None; self.blinkR = None
        self.mouth = 0.0          # 0 閉じ .. 1 全開
        self.wide = 0.7
        self.expr_full = (1, 0, 0, 0)  # 通常, ハッピー, ストレス, ハイテンション
        self.expr_face = None
        self.handL = (0.0, 0.0); self.handR = (0.0, 0.0)
        self.wind = 0.0           # 風（髪を横へ流す px）
        self.body = (0.0, 0.0)
        for k, v in kw.items():
            setattr(self, k, v)


class Character:
    def __init__(self, gl):
        self.gl = gl
        def tex(name, mip=True):
            im = np.asarray(Image.open(os.path.join(RIG, name)).convert('RGB'))
            return gl.texture(im, key=('rig', name), mipmap=mip)
        self.t = {
            'tN': tex('img_N.png'), 'tHP': tex('img_HP.png'), 'tST': tex('img_ST.png'), 'tHT': tex('img_HT.png'),
            'tNm': tex('img_Nm.png'), 'tHPm': tex('img_HPm.png'), 'tSTm': tex('img_STm.png'),
            'tNe': tex('img_Ne.png'), 'tHPe': tex('img_HPe.png'), 'tSTe': tex('img_STe.png'),
        }
        lut = np.load(os.path.join(RIG, 'eye_lut.npy'))
        self.t['eyeLut'] = gl.texture(lut, key=('rig', 'eyelut'), mipmap=False, premul=False)
        for k in ('m1', 'm2', 'm3', 'm4'):
            import cv2
            m = cv2.imread(os.path.join(RIG, k + '.png'), cv2.IMREAD_UNCHANGED)[..., [2, 1, 0, 3]].astype(np.float32) / 65535
            self.t[k] = gl.texture(m, key=('rig', k), mipmap=True, premul=False)
        self.plates = {k: gl.texture(np.asarray(Image.open(os.path.join(RIG, f'plate_{k}.png')).convert('RGB')),
                                     key=('plate', k)) for k in ('N', 'HP', 'ST')}
        self.black = gl.texture(np.zeros((2, 2, 4), np.float32), key=('black',), mipmap=False)
        self.full = {k: self.t[v] for k, v in (('N', 'tNm'), ('HP', 'tHPm'), ('ST', 'tSTm'), ('HT', 'tHT'))}

    def uniforms(self, pose, t, sim):
        ef = tuple(pose.expr_full)
        fa = tuple(pose.expr_face) if pose.expr_face is not None else ef
        happy = fa[1] / max(sum(fa), 1e-6)
        # 開いた口の口角の中点: 通常顔は閉じ口の位置、ハッピー顔は元の口の位置
        mc = np.array([1084.0, 1101.0]) * (1 - happy) + np.array([1088.5, 1076.0]) * happy
        bl = pose.blink
        return dict(
            exprFull=ef, exprFace=fa,
            headRot=math.radians(sim['head']), headPivot=(1060.0, 1260.0), headShift=sim['head_shift'],
            hatRot=math.radians(sim['hat']), hatPivot=(1050.0, 560.0),
            bowRot=math.radians(sim['bow']), bowPivot=(700.0, 300.0),
            hairL=(sim['hairL'], 0.0), hairR=(sim['hairR'], 0.0), hairWave=sim['wave'], bangAmp=sim['bang'], phase=t,
            earRot=math.radians(sim['ear']), earPivot=(1462.0, 940.0),
            breath=pose.breath, bodyShift=pose.body,
            handL=pose.handL, handR=pose.handR,
            blinkL=bl if pose.blinkL is None else pose.blinkL, blinkR=bl if pose.blinkR is None else pose.blinkR,
            eyeBotShift=12.0 * happy, mouthOpen=pose.mouth, mouthWide=pose.wide, mouthC=tuple(mc),
        )

    def draw(self, Hm, uniforms, look=None):
        u = dict(outlineW=0.0, outlineCol=(1, 1, 1), outlineA=1.0, rimCol=(0, 0, 0), rimDir=(1, 0), rimW=0.0,
                 tintMul=(1, 1, 1), tintAdd=(0, 0, 0), silhouette=0.0, silCol=(0, 0, 0), opacity=1.0, cropBottom=0.0,
                 projAmt=0.0, projCol=(1, 1, 1), projXf=(1, 1, 0, 0))
        u.update(uniforms)
        tex = dict(self.t)
        tex['tProj'] = self.black
        if look:
            look = dict(look)
            if 'tProj' in look:
                tex['tProj'] = look.pop('tProj')
            u.update(look)
        self.gl.draw(CHAR_FS, Hm, u, tex)

    def plate(self, Hm, which='N', tint=(1, 1, 1, 1), add=(0, 0, 0), blur=0.0):
        self.gl.draw(PLATE_FS, Hm, dict(tint=tint, add=add, blur=blur), dict(tex=self.plates[which]))


def lerp(a, b, k):
    return a + (b - a) * k


class Sim:
    """髪・帽子・リボン・イヤリングの二次運動。台本の pose(t) を時間順に積分する"""
    def __init__(self, pose_fn, fps=30, sub=4, t0=0.0):
        self.pose_fn, self.fps, self.sub, self.t0 = pose_fn, fps, sub, t0
        self.cache = {}
        self.reset()

    def reset(self):
        self.t = self.t0
        self.s = dict(hat=Spring(70, 7), bow=Spring(45, 3.2), ear=Spring(28, 1.6), hl=Spring(30, 3.0),
                      hr=Spring(26, 2.6), bang=Spring(40, 4.0))
        self.prev_head = None
        self.prev_shift = None

    def state(self, t):
        f = int(round(t * self.fps))
        if f in self.cache:
            return self.cache[f]
        if t < self.t - 1e-9:
            self.reset()
        dt = 1.0 / (self.fps * self.sub)
        out = None
        while self.t <= t + 1e-9:
            p = self.pose_fn(self.t)
            head = p.head
            if self.prev_head is None:
                self.prev_head = head; self.prev_shift = np.array(p.head_shift, float)
            vel = (head - self.prev_head) / dt
            sv = (np.array(p.head_shift, float) - self.prev_shift) / dt
            self.prev_head = head; self.prev_shift = np.array(p.head_shift, float)
            s = self.s
            # 頭の角速度と移動に対して、帽子・リボン・髪は遅れて追従する
            hat = s['hat'].step(head * 0.25 - vel * 0.02, dt)
            bow = s['bow'].step(-vel * 0.06 - sv[0] * 0.01 + p.wind * 0.02, dt)
            ear = s['ear'].step(-head * 0.9 - vel * 0.12 - sv[0] * 0.05 + p.wind * 0.08, dt)
            hl = s['hl'].step(p.wind * 1.0 - vel * 0.9 - sv[0] * 0.25, dt)
            hr = s['hr'].step(p.wind * 1.0 - vel * 0.9 - sv[0] * 0.25, dt)
            bang = s['bang'].step(p.wind * 0.12, dt)
            self.t += dt
            fr = int(round(self.t * self.fps))
            out = dict(head=head, head_shift=tuple(p.head_shift), hat=hat, bow=bow, ear=ear, hairL=hl, hairR=hr,
                       wave=1.2 + abs(p.wind) * 0.12, bang=0.6 + abs(bang), pose=p)
            self.cache[fr] = out
        return self.cache.get(f) or out
