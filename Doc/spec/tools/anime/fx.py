# 演出用シェーダ集（空と雲、星、魔法陣、粒子、集中線、放射の背景、光条、魔法ウィンドウ、ワイプ）
import numpy as np
import engine as E

NOISE = r'''
float h21(vec2 p){ p = fract(p*vec2(123.34, 456.21)); p += dot(p, p+45.32); return fract(p.x*p.y); }
float vnoise(vec2 p){
    vec2 i = floor(p), f = fract(p);
    vec2 u = f*f*(3.0-2.0*f);
    return mix(mix(h21(i), h21(i+vec2(1,0)), u.x), mix(h21(i+vec2(0,1)), h21(i+vec2(1,1)), u.x), u.y);
}
float fbm(vec2 p){
    float s = 0.0, a = 0.5;
    mat2 m = mat2(1.6, 1.2, -1.2, 1.6);
    for(int i=0;i<6;i++){ s += a*vnoise(p); p = m*p; a *= 0.5; }
    return s;
}
'''

# ---------------------------------------------------------------- 空（アニメ調のグラデーションと雲）
SKY_FS = E.HEADER + NOISE + r'''
uniform vec3 cTop, cMid, cHor; uniform float horizon;   // 地平線の画面上の高さ（0 上 .. 1 下）
uniform vec2 sun; uniform vec3 sunCol; uniform float sunSize, sunGlow;
uniform float stars, cloudAmt, cloudY, cloudScroll, cloudScale;
uniform vec3 cloudLit, cloudShade, cloudRim;
uniform vec2 camOff;   // 雲の視差用のカメラ移動
uniform float aspect;
float cloudField(vec2 p, float layer){
    float d = fbm(p) + 0.5*fbm(p*2.3 + layer*7.1) - 0.62;
    return d;
}
void main(){
    vec2 q = suv;
    float y = q.y;
    // 空のグラデーション
    float k1 = smoothstep(0.0, horizon, y);
    vec3 col = mix(cTop, cMid, smoothstep(0.0, 0.75, k1));
    col = mix(col, cHor, smoothstep(0.55, 1.0, k1));
    col = mix(col, cHor*0.92, smoothstep(horizon, horizon+0.4, y));
    vec2 sp = vec2((q.x - sun.x)*aspect, q.y - sun.y);
    float sd = length(sp);
    col += sunCol*(0.35*sunGlow*exp(-sd*3.0) + 0.5*sunGlow*exp(-sd*16.0));
    col = mix(col, sunCol*1.6, smoothstep(sunSize, sunSize*0.85, sd));
    // 星
    if(stars > 0.0){
        vec2 g = q*vec2(aspect, 1.0)*180.0 + camOff*40.0;
        vec2 id = floor(g); vec2 fp = fract(g) - 0.5;
        float h = h21(id);
        if(h > 0.965){
            vec2 o = vec2(h21(id+3.1), h21(id+7.7)) - 0.5;
            float tw = 0.6 + 0.4*sin(time*(2.0+h*5.0) + h*40.0);
            float st = exp(-length(fp - o*0.6)*22.0)*tw*(h-0.965)/0.035;
            col += vec3(0.9, 0.95, 1.0)*st*stars*smoothstep(horizon, 0.0, y)*1.6;
        }
    }
    // 雲（3層の視差、輪郭がくっきりしたアニメ調、太陽側の縁を光らせる）
    if(cloudAmt > 0.0){
        for(int L=0; L<3; L++){
            float fl = float(L);
            float sc = cloudScale*(1.0 + fl*0.55);
            float yy = (y - cloudY - fl*0.09);
            vec2 p = vec2(q.x*aspect*sc*1.3 + cloudScroll*(0.6+fl*0.5) + camOff.x*(0.3+fl*0.4) + fl*13.7,
                          yy*sc*3.2 + camOff.y*(0.3+fl*0.3));
            float band = exp(-pow(yy*3.2, 2.0));
            float d = cloudField(p, fl)*band + (cloudAmt - 1.0)*0.25;
            float a = smoothstep(0.0, 0.025, d);
            if(a <= 0.0) continue;
            vec2 toSun = normalize(vec2((sun.x - q.x)*aspect, sun.y - q.y) + 1e-4);
            float d2 = cloudField(p + toSun*vec2(0.08, -0.08)*vec2(1.0, 3.2), fl)*band + (cloudAmt - 1.0)*0.25;
            float lit = clamp(0.5 + (d - d2)*9.0, 0.0, 1.0);
            lit = smoothstep(0.35, 0.65, lit);          // 2階調に近い陰影
            vec3 cc = mix(cloudShade, cloudLit, lit);
            float rim = smoothstep(0.06, 0.0, d)*clamp(dot(toSun, vec2(0,-1))*0.5+0.5, 0.0, 1.0);
            cc += cloudRim*rim*(1.4 - sd);
            cc = mix(cc, cHor, 0.25*fl*smoothstep(0.0, 1.0, 1.0-y));   // 遠い層ほど空気遠近
            col = mix(col, cc, a*(0.85 + 0.15*fl/2.0));
        }
    }
    f = vec4(col, 1.0);
}
'''

def sky(gl, preset='dawn', t=0.0, cam=(0.0, 0.0), **kw):
    P = {
        'dawn': dict(cTop=E.hexc('1b1a4a'), cMid=E.hexc('6b4b9a'), cHor=E.hexc('ffb48a'), horizon=0.78,
                     sun=(0.62, 0.86), sunCol=E.hexc('ffcf8a'), sunSize=0.045, sunGlow=0.8, stars=0.6,
                     cloudAmt=0.0, cloudY=0.62, cloudScale=1.4, cloudLit=E.hexc('ffd2b0'), cloudShade=E.hexc('6a5590'),
                     cloudRim=E.hexc('fff1c8')),
        'night': dict(cTop=E.hexc('050818'), cMid=E.hexc('121a40'), cHor=E.hexc('2a3a70'), horizon=0.9,
                      sun=(0.8, 0.15), sunCol=E.hexc('a8c4ff'), sunSize=0.02, sunGlow=0.25, stars=1.0,
                      cloudAmt=0.0, cloudY=0.72, cloudScale=1.2, cloudLit=E.hexc('5a6aa8'), cloudShade=E.hexc('1a2250'),
                      cloudRim=E.hexc('a0b8ff')),
        'day': dict(cTop=E.hexc('1f63c8'), cMid=E.hexc('4f9bea'), cHor=E.hexc('bfe2ff'), horizon=0.85,
                    sun=(0.78, 0.12), sunCol=E.hexc('fff6d8'), sunSize=0.03, sunGlow=0.9, stars=0.0,
                    cloudAmt=0.0, cloudY=0.55, cloudScale=1.1, cloudLit=E.hexc('ffffff'), cloudShade=E.hexc('a9bfe0'),
                    cloudRim=E.hexc('ffffff')),
        'sunrise': dict(cTop=E.hexc('23347e'), cMid=E.hexc('b0609a'), cHor=E.hexc('ffa86e'), horizon=0.8,
                        sun=(0.5, 0.78), sunCol=E.hexc('fff0c0'), sunSize=0.05, sunGlow=1.0, stars=0.0,
                        cloudAmt=0.0, cloudY=0.66, cloudScale=1.3, cloudLit=E.hexc('fff0dc'), cloudShade=E.hexc('c9849e'),
                        cloudRim=E.hexc('fffbe8')),
    }[preset]
    P.update(kw)
    P.update(cloudScroll=t * 0.02 + P.get('cloudScroll', 0.0), camOff=cam, aspect=gl.VW / gl.VH)
    gl.fullscreen(SKY_FS, P, blend=None)


# ---------------------------------------------------------------- 魔法陣
MAGIC_FS = E.HEADER + NOISE + r'''
uniform sampler2D runes;        // 横長の文字帯（円周に沿わせる）
uniform sampler2D runes2;
uniform float prog;             // 描き進み 0..1
uniform float spin;
uniform vec3 c1, c2;
uniform float glow;
uniform float alpha;
const float PI = 3.14159265;
float ring(float r, float r0, float w){ return smoothstep(w, 0.0, abs(r - r0)); }
float seg(vec2 p, vec2 a, vec2 b, float w){
    vec2 pa = p-a, ba = b-a; float h = clamp(dot(pa,ba)/dot(ba,ba), 0.0, 1.0);
    return smoothstep(w, 0.0, length(pa - ba*h));
}
void main(){
    vec2 p = uv*2.0 - 1.0;
    float r = length(p);
    float an = atan(p.y, p.x);
    float a01 = fract((an + PI)/(2.0*PI) + 0.25);
    float px = 2.0/ (res.x*0.5);   // おおよその線幅単位
    float shown = step(a01, prog*1.02);
    float v = 0.0;
    float w = 0.006;
    // 同心円
    v += ring(r, 0.98, w)*step(a01, prog);
    v += ring(r, 0.93, w*0.7)*step(a01, prog);
    v += ring(r, 0.70, w)*step(a01, prog*1.1);
    v += ring(r, 0.66, w*0.6)*step(a01, prog*1.1);
    v += ring(r, 0.36, w)*step(a01, prog*1.25);
    // 目盛り
    float tk = abs(fract(a01*120.0) - 0.5);
    v += smoothstep(0.08, 0.0, tk)*step(0.935, r)*step(r, 0.975)*step(a01, prog);
    // 文字帯（外周・内周）
    float aa = fract(a01 + spin);
    if(r > 0.72 && r < 0.92){
        vec2 tuv = vec2(fract(aa*3.0), (0.92 - r)/0.2);
        v += texture(runes, tuv).a*step(a01, prog)*0.9;
    }
    float ab = fract(a01 - spin*1.6);
    if(r > 0.42 && r < 0.6){
        vec2 tuv = vec2(fract(ab*2.0), (0.6 - r)/0.18);
        v += texture(runes2, tuv).a*step(a01, prog*1.1)*0.8;
    }
    // 六芒星（2つの三角形）
    float star = 0.0;
    float rot = spin*2.0*PI;
    for(int k=0;k<6;k++){
        float a0 = rot + float(k)*PI/3.0;
        float a1 = a0 + 2.0*PI/3.0;
        vec2 A = 0.66*vec2(cos(a0), sin(a0)), B = 0.66*vec2(cos(a1), sin(a1));
        float lp = clamp(prog*6.0 - float(k)*0.6, 0.0, 1.0);
        star += seg(p, A, mix(A, B, lp), w*0.8);
    }
    v += star;
    // 中心の小円群
    for(int k=0;k<6;k++){
        float a0 = -rot*1.5 + float(k)*PI/3.0;
        vec2 c = 0.52*vec2(cos(a0), sin(a0));
        v += ring(length(p - c), 0.06, w*0.6)*step(0.3, prog);
    }
    v = clamp(v, 0.0, 1.5);
    vec3 col = mix(c1, c2, smoothstep(0.2, 1.0, r));
    float halo = (exp(-abs(r - 0.82)*7.0)*0.25 + exp(-r*3.0)*0.15)*smoothstep(1.0, 0.92, r);
    vec3 o = col*v*1.6 + col*halo*glow*prog;
    float A = clamp(v + halo*glow*prog*0.5, 0.0, 1.0);
    f = vec4(o, A*0.0)*alpha;      // 加算で描く（α は使わない）
}
'''

class Magic:
    def __init__(self, gl):
        self.gl = gl
        words1 = '  CREATIVE · VISION · 10 · WPF · gRPC · .NET 10 · SQLite · PostgreSQL · MariaDB · MVVM · HTTP/2 · OPEN SOURCE ·'
        words2 = '  受注 · 発注 · 仕入 · 売上 · 在庫 · 配分 · 入金 · 支払 · 帳票 · 予算 ·'
        self.r1 = self._strip(words1, 'segoeb', 96)
        self.r2 = self._strip(words2, 'noto', 96)

    def _strip(self, text, fam, size):
        arr = E.text_rgba(text, size, fam=fam, weight='Bold', color=(1, 1, 1), pad=4)
        return self.gl.texture(arr, key=('rune', text), mipmap=True)

    def draw(self, Hm, prog=1.0, spin=0.0, c1=(0.7, 0.45, 1.0), c2=(0.35, 0.9, 1.0), glow=1.0, alpha=1.0):
        if Hm is None:
            return
        self.gl.draw(MAGIC_FS, Hm, dict(prog=prog, spin=spin, c1=c1, c2=c2, glow=glow, alpha=alpha),
                     dict(runes=self.r1, runes2=self.r2), blend='add')


# ---------------------------------------------------------------- 粒子の形
PART_FS = r'''
#version 430
in vec2 uv; in vec4 col; in vec4 aux; out vec4 f;
uniform float time;
void main(){
    vec2 p = uv*2.0 - 1.0;
    float r = length(p);
    float kind = aux.x;
    float a = 0.0;
    if(kind < 0.5){            // 光の玉
        a = exp(-r*r*5.0) + 0.6*smoothstep(0.35, 0.0, r);
    } else if(kind < 1.5){     // 4 芒のきらめき
        float s = max(smoothstep(0.12, 0.0, abs(p.x))*smoothstep(1.0, 0.0, abs(p.y)),
                      smoothstep(0.12, 0.0, abs(p.y))*smoothstep(1.0, 0.0, abs(p.x)));
        a = s + exp(-r*r*14.0);
    } else if(kind < 2.5){     // 雪
        a = smoothstep(1.0, 0.45, r);
    } else if(kind < 3.5){     // 花びら
        vec2 q = vec2(p.x*1.7, p.y);
        a = smoothstep(1.0, 0.85, length(q)) * smoothstep(-0.9, -0.3, p.y + abs(p.x)*0.6) ;
    } else if(kind < 4.5){     // 灯籠（角の丸い四角と光の滲み）
        vec2 q = abs(p) - vec2(0.42, 0.55);
        float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.08;
        float body = smoothstep(0.03, -0.03, d);
        float stripe = 1.0 - 0.25*smoothstep(0.04, 0.0, abs(p.y + 0.42)) - 0.25*smoothstep(0.04, 0.0, abs(p.y - 0.45));
        a = body*stripe*(0.75 + 0.25*smoothstep(0.6, -0.2, p.y)) + 0.35*exp(-r*r*2.5);
    } else {                   // ぼけ（六角形）
        float an = atan(p.y, p.x);
        float hx = cos(3.14159/6.0)/cos(mod(an, 3.14159/3.0) - 3.14159/6.0);
        a = smoothstep(1.0*hx, 0.92*hx, r)*(0.55 + 0.45*smoothstep(0.6, 1.0, r/hx));
    }
    f = vec4(col.rgb*a*col.a, a*col.a*aux.y);
}
'''

def parts(gl, xs, ys, size, color, alpha, kind=0, angle=None, occl=0.0, blend='add'):
    n = len(xs)
    pos = np.zeros((n, 4), np.float32)
    pos[:, 0] = xs; pos[:, 1] = ys; pos[:, 2] = size
    pos[:, 3] = 0 if angle is None else angle
    col = np.zeros((n, 4), np.float32)
    col[:, :3] = color
    col[:, 3] = alpha
    aux = np.zeros((n, 4), np.float32)
    aux[:, 0] = kind; aux[:, 1] = occl
    gl.particles(PART_FS, pos, col, aux, blend=blend)


# ---------------------------------------------------------------- 集中線・スピード線・放射背景
FOCUS_FS = E.HEADER + NOISE + r'''
uniform vec2 c; uniform float inner, amount, seed, aspect; uniform vec3 col; uniform float mode;
void main(){
    vec2 p = (suv - c)*vec2(aspect, 1.0);
    float r = length(p);
    float a = atan(p.y, p.x);
    float v = 0.0;
    if(mode < 0.5){
        // 集中線: 角度方向に細い線をランダムに
        float n = 260.0;
        float id = floor((a/6.2831853 + 0.5)*n);
        float h = h21(vec2(id, seed));
        float fa = fract((a/6.2831853 + 0.5)*n) - 0.5;
        float wid = 0.08 + 0.35*h;
        float len0 = inner*(0.85 + 0.6*h21(vec2(id, seed+3.0)));
        v = smoothstep(wid, 0.0, abs(fa))*smoothstep(len0, len0 + 0.2, r)*step(0.35, h);
    } else {
        // 横方向のスピード線
        float row = floor(suv.y*180.0);
        float h = h21(vec2(row, seed));
        float x = fract(suv.x*0.6 + h*7.0 + seed*0.37);
        v = step(0.55, h)*smoothstep(0.0, 0.3, x)*smoothstep(1.0, 0.6, x)*smoothstep(0.5, 0.0, abs(fract(suv.y*180.0) - 0.5) - 0.1);
        v *= smoothstep(inner*0.6, inner, abs(suv.y - c.y)*2.0 + abs(suv.x - c.x)*0.3);
    }
    f = vec4(col*v*amount, v*amount);
}
'''

def focus_lines(gl, cx=0.5, cy=0.5, inner=0.35, amount=1.0, seed=0.0, col=(1, 1, 1), mode=0, blend='over'):
    gl.fullscreen(FOCUS_FS, dict(c=(cx, cy), inner=inner, amount=amount, seed=seed, col=col, mode=mode,
                                 aspect=gl.VW / gl.VH), blend=blend)

BURST_FS = E.HEADER + r'''
uniform vec2 c; uniform float rot, n, aspect; uniform vec3 c1, c2, c3; uniform float alpha;
void main(){
    vec2 p = (suv - c)*vec2(aspect, 1.0);
    float a = atan(p.y, p.x) + rot;
    float s = step(0.5, fract(a/6.2831853*n));
    float r = length(p);
    vec3 col = mix(c1, c2, s);
    col = mix(col, c3, smoothstep(0.9, 0.0, r)*0.85);
    f = vec4(col*alpha, alpha);
}
'''

def burst(gl, cx=0.5, cy=0.5, rot=0.0, n=24, c1=(1, .8, .9), c2=(1, .9, .95), c3=(1, 1, 1), alpha=1.0):
    gl.fullscreen(BURST_FS, dict(c=(cx, cy), rot=rot, n=n, c1=c1, c2=c2, c3=c3, alpha=alpha, aspect=gl.VW / gl.VH))


# ---------------------------------------------------------------- 光条（明部を光源へ向けて放射状にぼかして加算）
RAYS_FS = E.HEADER + r'''
uniform sampler2D tex; uniform vec2 c; uniform float amount, decay, thr, focusK, aspect; uniform vec3 tintc;
void main(){
    vec2 uv0 = vec2(suv.x, 1.0 - suv.y);
    vec2 cc = vec2(c.x, 1.0 - c.y);
    vec2 d = (uv0 - cc)/64.0;
    vec2 p = uv0;
    vec3 acc = vec3(0); float w = 1.0;
    for(int i=0;i<64;i++){
        p -= d;
        vec3 s = texture(tex, p).rgb;
        float b = max(max(s.r, s.g), s.b);
        float dd = length((p - cc)*vec2(aspect, 1.0));
        acc += s*smoothstep(thr, thr + 0.2, b)*w*exp(-dd*focusK);
        w *= decay;
    }
    acc /= 64.0;
    f = vec4(acc*tintc*amount, 0.0);
}
'''

def god_rays(gl, cx, cy, amount=1.0, decay=0.97, thr=0.85, tint=(1, 0.95, 0.85), focus=3.0):
    """光源 (cx,cy) 付近の明部だけを放射状に伸ばして加算する"""
    src = gl.copy_main()
    gl.fullscreen(RAYS_FS, dict(c=(cx, cy), amount=amount, decay=decay, thr=thr, tintc=tint, focusK=focus,
                                aspect=gl.VW / gl.VH), dict(tex=src), blend='add')


# ---------------------------------------------------------------- 魔法ウィンドウ（実画面を光の枠で出現させる）
WINDOW_FS = E.HEADER + NOISE + r'''
uniform sampler2D tex; uniform float appear, alpha, glowAmt, seed; uniform vec3 edgeCol; uniform float aspectTex;
uniform float bright;
void main(){
    vec2 p = uv;
    // ノイズの閾値で溶けるように現れる
    float n = fbm(p*vec2(6.0*aspectTex, 6.0) + seed*13.0)*0.8 + p.y*0.2;
    float th = appear*1.25 - 0.15;
    float vis = smoothstep(th, th - 0.04, n);
    float edge = smoothstep(0.05, 0.0, abs(n - th))*step(0.001, appear)*step(appear, 0.999);
    vec4 c = texture(tex, p);
    vec3 col = c.rgb*bright;
    // 枠と走査線
    vec2 b = min(p, 1.0 - p)*vec2(aspectTex, 1.0);
    float border = smoothstep(0.012, 0.0, min(b.x, b.y));
    float scan = 0.96 + 0.04*sin((p.y*900.0 + time*40.0));
    col *= scan;
    col += edgeCol*border*1.2;
    col += edgeCol*edge*3.0;
    float A = vis*alpha;
    vec3 o = col*A + edgeCol*edge*alpha*1.5;
    // 外側の光
    f = vec4(o, A);
}
'''

def window(gl, tex, Hm, appear=1.0, alpha=1.0, edge=(0.55, 0.8, 1.0), seed=0.0, bright=1.0):
    if Hm is None or alpha <= 0.001:
        return
    w, h = tex.size
    gl.draw(WINDOW_FS, Hm, dict(appear=appear, alpha=alpha, edgeCol=edge, seed=seed, aspectTex=w / h, bright=bright),
            dict(tex=tex))

# ---------------------------------------------------------------- ワイプ・帯
SOLID_FS = E.HEADER + r'''
uniform vec4 col;
void main(){ f = vec4(col.rgb*col.a, col.a); }
'''

def rect(gl, x, y, w, h, col, alpha=1.0, angle=0.0, anchor=(0.5, 0.5), blend='over'):
    gl.draw(SOLID_FS, gl.rect_h(x, y, w, h, angle, anchor), dict(col=(*col, alpha)), blend=blend)

GRAD_FS = E.HEADER + r'''
uniform vec4 c0, c1; uniform float vertical;
void main(){
    float k = vertical > 0.5 ? uv.y : uv.x;
    vec4 c = mix(c0, c1, k);
    f = vec4(c.rgb*c.a, c.a);
}
'''

def grad(gl, x, y, w, h, c0, c1, vertical=True, angle=0.0, blend='over'):
    gl.draw(GRAD_FS, gl.rect_h(x, y, w, h, angle), dict(c0=c0, c1=c1, vertical=1.0 if vertical else 0.0), blend=blend)

VIGNETTE_FS = E.HEADER + r'''
uniform vec3 col; uniform float amount, inner, aspect;
void main(){
    vec2 p = (suv - 0.5)*vec2(aspect, 1.0);
    float k = smoothstep(inner, inner + 0.6, length(p));
    f = vec4(col*k*amount, k*amount);
}
'''

def vignette(gl, amount=0.6, col=(0, 0, 0), inner=0.35):
    gl.fullscreen(VIGNETTE_FS, dict(col=col, amount=amount, inner=inner, aspect=gl.VW / gl.VH))
