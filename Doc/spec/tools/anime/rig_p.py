# 背景なしの主人公のリグ（onlyperson の6表情）と立ち絵
# rig.py の Character と同じ使い方（uniforms / draw）で、座標も旧リグ（2048px の一枚絵の画素）のまま使える。
# 素材は prep_person.py が work/rig_p・work/stand に作る。
#   表情: (通常, ハッピー, ストレス, ハイテンション, 目を閉じた笑顔, 困り顔)。4つ組を渡すと後ろ2つは 0
import os, math, json
import numpy as np
from PIL import Image
import engine as E
from rig import Spring, Pose, Sim, lerp  # noqa: F401  台本から R.Pose 等として使う

RIG = os.path.join(E.WORK, 'rig_p')
STAND = os.path.join(E.WORK, 'stand')

CHAR_FS = E.HEADER + r'''
uniform sampler2D tN, tHP, tST, tHT, tCE, tWR, tNm, tSTm, tWRm, tCEm, tHPm, tNe, tHPe, tSTe, tWRe, m1, m2, m3, m4;
uniform vec4 exprFull, exprFace; uniform vec2 exprFull2, exprFace2;
uniform vec2 org; uniform float SZ, eyeSplit, breathY; uniform vec2 MC0; uniform float MK0;
uniform float headRot; uniform vec2 headPivot; uniform vec2 headShift;
uniform float hatRot; uniform vec2 hatPivot;
uniform float bowRot; uniform vec2 bowPivot;
uniform vec2 hairL, hairR; uniform float hairWave, bangAmp, phase;
uniform float earRot; uniform vec2 earPivot;
uniform float breath; uniform vec2 bodyShift;
uniform vec2 handL, handR;
uniform float blinkL, blinkR;
uniform float mouthOpen, mouthWide; uniform vec2 mouthC;
uniform float outlineW; uniform vec3 outlineCol; uniform float outlineA;
uniform vec3 rimCol; uniform vec2 rimDir; uniform float rimW;
uniform vec3 tintMul; uniform vec3 tintAdd; uniform float silhouette; uniform vec3 silCol;
uniform float opacity; uniform float cropBottom;
uniform sampler2D tProj; uniform float projAmt; uniform vec3 projCol; uniform vec4 projXf;

vec4 T(sampler2D t, vec2 p){ return texture(t, (p - org)/SZ); }
vec2 rotInv(vec2 p, vec2 c, float a){ float cs=cos(-a), sn=sin(-a); vec2 d=p-c; return c+vec2(cs*d.x-sn*d.y, sn*d.x+cs*d.y); }

vec2 warp(vec2 p){
    vec4 a = T(m1,p), b = T(m2,p), c = T(m3,p);
    vec2 d = vec2(0);
    d += a.g*(rotInv(p, headPivot, headRot) - headShift - p);
    d += a.b*(rotInv(p, hatPivot, hatRot) - p);
    d += a.a*(rotInv(p, bowPivot, bowRot) - p);
    d -= b.r*(hairL + vec2(hairWave*sin(phase*2.1 - p.y*0.011), 0.0));
    d -= b.g*(hairR + vec2(hairWave*sin(phase*2.3 - p.y*0.011 + 1.3), 0.0));
    d -= c.a*vec2(bangAmp*sin(phase*1.7 - p.x*0.008), 0.0);
    d += b.b*(rotInv(p, earPivot, earRot) - p);
    d -= b.a*(vec2(0.0, (p.y-breathY)*breath) + bodyShift);
    d -= c.r*handL + c.g*handR;
    return p + d;
}

uniform sampler2D eyeLut;
vec2 blinkMap(vec2 s, float bl, out float skinK){
    skinK = 0.0;
    if(bl <= 0.001) return s;
    vec4 e = texelFetch(eyeLut, ivec2(clamp(int(s.x - org.x), 0, int(SZ) - 1), 0), 0);
    if(e.w <= 0.0) return s;
    float top = e.x, bot = e.y, T_ = e.z;
    float Tc = T_*(1.0 - 0.35*bl);
    float d = bl*max(bot - top - Tc, 0.0)*e.w;
    if(s.y < top - 6.0 || s.y > bot) return s;
    if(s.y < top + d){ skinK = smoothstep(top - 6.0, top, s.y)*e.w; return s; }
    if(s.y < top + d + Tc) return vec2(s.x, top + (s.y - top - d)*T_/Tc);
    skinK = smoothstep(0.55, 0.95, bl)*smoothstep(top + d + Tc, top + d + Tc + 2.0, s.y);
    return s;
}

// 口なしの顔（基本）
vec3 faceTex(vec2 s, vec4 w, vec2 w2){
    vec3 c = vec3(0);
    if(w.x>0.0) c += w.x*T(tN,s).rgb;
    if(w.y>0.0) c += w.y*T(tHP,s).rgb;
    if(w.z>0.0) c += w.z*T(tST,s).rgb;
    if(w.w>0.0) c += w.w*T(tHT,s).rgb;
    if(w2.x>0.0) c += w2.x*T(tCE,s).rgb;
    if(w2.y>0.0) c += w2.y*T(tWR,s).rgb;
    return c;
}

vec4 shade(vec2 p){
    vec2 s = warp(p);
    float a = T(m1, s).r;
    vec3 col = faceTex(s, exprFull, exprFull2);
    float fm = T(m3, s).b;
    if(fm > 0.001){
        // ハイテンション・目を閉じた笑顔は目を閉じている。ハッピー顔は目を閉じた笑顔の目へ差し替えてまばたきする
        float wsum0 = dot(exprFace, vec4(1)) + exprFace2.x + exprFace2.y + 1e-4;
        float hpK = exprFace.y/wsum0;
        float blw = 1.0 - (exprFace.w + exprFace2.x + exprFace.y)/wsum0;
        float skinK;
        float blk = s.x < eyeSplit ? blinkL : blinkR;
        vec2 e = blinkMap(s, blk*blw, skinK);
        vec3 fc = faceTex(e, exprFace, exprFace2);
        float ceK = hpK*smoothstep(0.25, 0.75, blk)*T(m4, s).b;
        fc = mix(fc, T(tCE, s).rgb, ceK);
        if(skinK > 0.0){
            vec3 sk = exprFace.x*T(tNe,s).rgb + exprFace.y*T(tHPe,s).rgb + exprFace.z*T(tSTe,s).rgb + exprFace2.y*T(tWRe,s).rgb
                    + exprFace.w*T(tHT,s).rgb + exprFace2.x*T(tCE,s).rgb;
            fc = mix(fc, sk, skinK);
        }
        float noHT = 1.0 - exprFace.w;
        float open = clamp(mouthOpen, 0.0, 1.2);
        float closedK = noHT*smoothstep(0.16, 0.04, open);
        vec4 mm = T(m4, s);
        if(closedK > 0.0){
            // 閉じ口: 各表情の元の口（ハッピーは目を閉じた笑顔の閉じ口）
            float wsum = exprFace.x + exprFace.y + exprFace.z + exprFace2.x + exprFace2.y + 1e-4;
            vec3 cm = (exprFace.x*T(tNm,s).rgb + (exprFace.y + exprFace2.x)*T(tCEm,s).rgb + exprFace.z*T(tSTm,s).rgb
                     + exprFace2.y*T(tWRm,s).rgb)/wsum;
            fc = mix(fc, cm, mm.r*closedK*min(wsum, 1.0));
        }
        if(open > 0.04 && noHT > 0.0){
            vec2 sc = vec2(mix(0.55, 1.0, mouthWide), max(open, 0.05));
            float kd = MK0*mix(0.15, 0.7, clamp(open, 0.0, 1.0));
            float sx = MC0.x + (s.x - mouthC.x)/sc.x;
            float ld = mouthC.y + kd*(s.x - mouthC.x);
            vec2 sm = vec2(sx, MC0.y + MK0*(sx - MC0.x) + (s.y - ld)/sc.y);
            float ma = T(m4, sm).g*noHT*smoothstep(0.04, 0.12, open);
            fc = mix(fc, T(tHPm, sm).rgb, ma);
        }
        col = mix(col, fc, fm);
    }
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
    f = shade(org + uv*SZ);
}
'''


def pad6(e):
    e = tuple(float(x) for x in e)
    return e + (0.0,) * (6 - len(e))


class Character:
    """onlyperson のリグ。Hm は旧リグと同じく「旧リグ座標/2048 → 画面」の行列（op.char_H）を渡す"""
    def __init__(self, gl):
        import cv2
        self.gl = gl
        self.cfg = json.load(open(os.path.join(RIG, 'rig.json')))
        self.org = tuple(self.cfg['org'])
        self.SZ = float(self.cfg['size'])

        def tex(name):
            im = np.asarray(Image.open(os.path.join(RIG, name)).convert('RGB'))
            return gl.texture(im, key=('rig_p', name), mipmap=True)
        self.t = {k: tex(f'img_{k[1:]}.png') for k in
                  ('tN', 'tHP', 'tST', 'tHT', 'tCE', 'tWR', 'tNm', 'tSTm', 'tWRm', 'tCEm', 'tHPm', 'tNe', 'tHPe', 'tSTe', 'tWRe')}
        lut = np.load(os.path.join(RIG, 'eye_lut.npy'))
        self.t['eyeLut'] = gl.texture(lut, key=('rig_p', 'eyelut'), mipmap=False, premul=False)
        for k in ('m1', 'm2', 'm3', 'm4'):
            m = cv2.imread(os.path.join(RIG, k + '.png'), cv2.IMREAD_UNCHANGED)[..., [2, 1, 0, 3]].astype(np.float32) / 65535
            self.t[k] = gl.texture(m, key=('rig_p', k), mipmap=True, premul=False)
        self.black = gl.texture(np.zeros((2, 2, 4), np.float32), key=('black',), mipmap=False)

    def H(self, Hm):
        """旧リグの uv（0..1 が 2048px）→ このキャンバスの uv"""
        return Hm @ E.m3_scale(1 / 2048) @ E.m3_translate(*self.org) @ E.m3_scale(self.SZ)

    def uniforms(self, pose, t, sim):
        c = self.cfg
        ef = pad6(pose.expr_full)
        fa = pad6(pose.expr_face) if pose.expr_face is not None else ef
        happy = (fa[1] + fa[4]) / max(sum(fa), 1e-6)
        mc = np.array(c['mcN']) * (1 - happy) + np.array(c['mc0']) * happy
        bl = pose.blink
        return dict(
            exprFull=ef[:4], exprFull2=ef[4:], exprFace=fa[:4], exprFace2=fa[4:],
            org=self.org, SZ=self.SZ, eyeSplit=c['eye_split'], breathY=c['breath_y'], MC0=tuple(c['mc0']), MK0=c['mk0'],
            headRot=math.radians(sim['head']), headPivot=tuple(c['head_pivot']), headShift=sim['head_shift'],
            hatRot=math.radians(sim['hat']), hatPivot=tuple(c['hat_pivot']),
            bowRot=math.radians(sim['bow']), bowPivot=tuple(c['bow_pivot']),
            hairL=(sim['hairL'], 0.0), hairR=(sim['hairR'], 0.0), hairWave=sim['wave'], bangAmp=sim['bang'], phase=t,
            earRot=math.radians(sim['ear']), earPivot=tuple(c['ear_pivot']),
            breath=pose.breath, bodyShift=pose.body,
            handL=pose.handL, handR=pose.handR,
            blinkL=bl if pose.blinkL is None else pose.blinkL, blinkR=bl if pose.blinkR is None else pose.blinkR,
            mouthOpen=pose.mouth, mouthWide=pose.wide, mouthC=tuple(mc),
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
        self.gl.draw(CHAR_FS, self.H(Hm), u, tex)


# ---------------------------------------------------------------- 立ち絵
STAND_FS = E.HEADER + r'''
uniform sampler2D tex, wt; uniform vec2 size;
uniform float phase, wind, hatRot, hemAmp, breath, sway; uniform vec2 hatPivot, footPivot;
uniform vec3 tintMul, tintAdd, rimCol, silCol; uniform vec2 rimDir; uniform float rimW, silhouette, opacity;
uniform float outlineW; uniform vec3 outlineCol; uniform float outlineA;
vec2 rotInv(vec2 p, vec2 c, float a){ float cs=cos(-a), sn=sin(-a); vec2 d=p-c; return c+vec2(cs*d.x-sn*d.y, sn*d.x+cs*d.y); }
vec4 W(vec2 p){ return texture(wt, p/size); }
vec4 C(vec2 p){ return texture(tex, p/size); }
vec2 warp(vec2 p){
    p = rotInv(p, footPivot, sway);                                  // 足元を軸に全体が揺れる
    p.y = footPivot.y + (p.y - footPivot.y)/(1.0 + breath);           // 呼吸（縦に伸び縮み）
    vec4 w = W(p);
    vec2 d = vec2(0);
    d.x -= w.r*(wind + 6.0*sin(phase*2.2 - p.y*0.012));              // 髪: 風と波
    d.x -= w.b*hemAmp*sin(phase*3.1 - p.x*0.02);                     // 裾のはためき
    d += w.g*(rotInv(p, hatPivot, hatRot) - p);                      // 帽子の先
    return p + d;
}
void main(){
    vec2 p = uv*size;
    vec2 s = warp(p);
    vec4 c = C(s);
    float a = c.a;
    vec3 col = a > 0.0 ? c.rgb/a : vec3(0);
    col = col*tintMul + tintAdd;
    if(rimW > 0.0) col += rimCol*(1.0 - C(s + rimDir*rimW).a);
    col = mix(col, silCol, silhouette);
    float o = 0.0;
    if(outlineW > 0.0){
        for(int i=0;i<16;i++){
            float an = 6.2831853*float(i)/16.0;
            o = max(o, C(s + vec2(cos(an), sin(an))*outlineW).a);
        }
        o = clamp(o - a, 0.0, 1.0)*outlineA;
    }
    f = vec4(col*a + outlineCol*o*(1.0 - a), a + o*(1.0 - a))*opacity;
}
'''


class Standee:
    """立ち絵（正面・背面）を揺らす。足元を基準に置く"""
    def __init__(self, gl, name):
        import cv2
        self.gl = gl
        self.cfg = json.load(open(os.path.join(STAND, f'{name}.json')))
        im = np.asarray(Image.open(os.path.join(STAND, f'{name}.png')).convert('RGBA'))
        self.tex = gl.texture(im, key=('stand', name))
        w = cv2.imread(os.path.join(STAND, f'{name}_w.png'), cv2.IMREAD_UNCHANGED)[..., [2, 1, 0, 3]].astype(np.float32) / 65535
        self.wt = gl.texture(w, key=('stand_w', name), mipmap=True, premul=False)
        self.size = tuple(self.cfg['size'])
        self.s = dict(hat=Spring(40, 3.5), hair=Spring(30, 3.0))
        self.last = None

    def H(self, x, y, height, angle=0.0, flip=False):
        """足元の中心を画面 (x,y) に、高さ height px で置く"""
        w, h = self.size
        k = height / self.cfg['foot_y']
        Hm = E.m3_translate(x, y) @ E.m3_rot(angle) @ E.m3_scale(k) @ E.m3_translate(-w / 2, -self.cfg['foot_y'])
        if flip:
            Hm = Hm @ E.m3_translate(w, 0) @ E.m3_scale(-1, 1)
        return Hm @ E.m3_scale(w, h)

    def draw(self, Hm, t, wind=10.0, hem=4.0, sway=0.0, breath=None, look=None):
        if breath is None:
            breath = 0.004 * math.sin(t * 2 * math.pi / 3.4)
        w, h = self.size
        u = dict(size=(w, h), phase=t, wind=wind, hatRot=math.radians(2.5 * math.sin(t * 1.6) - wind * 0.04), hemAmp=hem,
                 breath=breath, sway=sway, hatPivot=tuple(self.cfg['hat_pivot']), footPivot=(w / 2, self.cfg['foot_y']),
                 tintMul=(1, 1, 1), tintAdd=(0, 0, 0), rimCol=(0, 0, 0), rimDir=(1, 0), rimW=0.0, silCol=(0, 0, 0),
                 silhouette=0.0, opacity=1.0, outlineW=0.0, outlineCol=(1, 1, 1), outlineA=1.0)
        if look:
            u.update(look)
        self.gl.draw(STAND_FS, Hm, u, dict(tex=self.tex, wt=self.wt))


# ---------------------------------------------------------------- 連続コマ（op2-scene-v1）
SCENE = os.path.join(E.WORK, 'scene')


class Flipbook:
    """シーン別の連続コマ（prep_person.build_scenes がコマ0に合わせて重ねたもの）を描く。
    コマは描き分けた絵なので変形はせず、呼吸・揺れ・光だけを付ける。version は op2-scene-v1 / v2"""
    def __init__(self, gl, name, version='v2'):
        self.gl = gl
        d = os.path.join(SCENE, version)
        self.cfg = json.load(open(os.path.join(d, f'{name}.json')))
        self.size = tuple(self.cfg['size'])
        self.fps = self.cfg['fps']
        self.frames = [gl.texture(np.asarray(Image.open(os.path.join(d, f'{name}-{i:04d}.png')).convert('RGBA')),
                                  key=('scene', version, name, i)) for i in range(self.cfg['frames'])]
        self.zero = gl.texture(np.zeros((4, 4, 4), np.float32), key=('zero4',), mipmap=False, premul=False)
        self.n = len(self.frames)

    def at(self, ts):
        """シーン内の時刻（秒）のコマ番号。範囲外は先頭・末尾で止める"""
        return int(min(max(math.floor(ts * self.fps + 1e-6), 0), self.n - 1))

    def H(self, x, y, size, angle=0.0, flip=False, by='height'):
        """基準点（立ち姿は足元、飛ぶ姿は体の中心）を画面 (x,y) に置く。size は体の高さ（by='width' なら幅）の px"""
        w, h = self.size
        ax, ay = self.cfg['anchor_pt']
        k = size / self.cfg[by]
        Hm = E.m3_translate(x, y) @ E.m3_rot(angle) @ E.m3_scale(k) @ E.m3_translate(-ax, -ay)
        if flip:
            Hm = Hm @ E.m3_translate(w, 0) @ E.m3_scale(-1, 1)
        return Hm @ E.m3_scale(w, h)

    def draw(self, Hm, t, frame, sway=0.0, breath=None, look=None):
        if breath is None:
            breath = 0.004 * math.sin(t * 2 * math.pi / 3.4)
        w, h = self.size
        ax, ay = self.cfg['anchor_pt']
        u = dict(size=(w, h), phase=t, wind=0.0, hatRot=0.0, hemAmp=0.0, breath=breath, sway=sway, hatPivot=(0, 0),
                 footPivot=(ax, ay), tintMul=(1, 1, 1), tintAdd=(0, 0, 0), rimCol=(0, 0, 0), rimDir=(1, 0), rimW=0.0,
                 silCol=(0, 0, 0), silhouette=0.0, opacity=1.0, outlineW=0.0, outlineCol=(1, 1, 1), outlineA=1.0)
        if look:
            u.update(look)
        self.gl.draw(STAND_FS, Hm, u, dict(tex=self.frames[min(max(int(frame), 0), self.n - 1)], wt=self.zero))
