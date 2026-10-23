# 作品1 フルバージョン「明日へ飛べ」— 主題歌のフルコーラス（2分44秒）に合わせたアニメ
# 0〜61秒は op.py のショットをそのまま使い、間奏から先（2番・サビ2・ブリッジ・ラストサビ・アウトロ）を追加する。
# 追加の素材（prep_full.py が work/full に用意）:
#   富士山と天の川の夜景・四季の里のパノラマ（主人公と同じく静止画の生成画像。透かしの入る隅は切り出しで外す）
#   主人公のミニキャラ（Codex のペット: 走る・手を振る・跳ぶ）
# 動画生成AIは使わない。動き・光・天候・朝焼け・花火・描画の演出はすべて GLSL とコードで付ける。
#
# 秒割り（音源の時刻そのまま。150BPM・1小節1.6秒）
#    0.0- 61.0  op.py と同じ（イントロ〜サビ1）
#   61.0- 62.6  雲海の日の出で歌い終える
#   62.6- 64.2  空を見上げて夜へ
#   64.2- 70.6  富士山と天の川、流れ星、星座の「10」          間奏
#   70.6- 73.8  風より速く: 通信の光が追い越していく             Aメロ2「風より速く 届くよ」
#   73.8- 77.0  扉が開いて四季の里、オープンな技術が広がる        「オープンな夢 広がる」
#   77.0- 80.2  AI の光とミニキャラ、AI と共同の実コミット数      「AIと手を つないで」
#   80.2- 83.4  四季の里の朝日                                   「新しい朝 つくろう」
#   83.4- 86.6  雨の夜、迷った日（修正の実コミット）               Bメロ2「迷った日だって」
#   86.6- 89.8  実テストが次々に通る                              「何度も確かめ」
#   89.8- 93.0  毎日のコミットの草の上をミニキャラが走る           「毎日 前へ進む」
#   93.0- 96.2  光の柱→拍ごとのカット                             「今 輝くとき」
#   96.2- 99.4  タイトルの文字が飛び込む                          サビ2「Creative Vision 10！」
#   99.4-102.6  4つの声の光がひとつの輪になる                    「声を合わせ ひとつに」
#  102.6-105.8  涙が光になる（ストレス顔→笑顔、雨が光の粒に）     「涙もぜんぶ 光になる」
#  105.8-107.4  いこう！
#  107.4-112.2  富士山の夜明け（赤富士・湖面の朝日）              「ほら 夜明けの空 晴れた」
#  112.2-115.4  ミニキャラが四季の里を駆け抜ける                  「ためらわず走ろう」
#  115.4-121.8  線で描いた実画面が色づいてロゴになる              「描いた夢が 未来になる」
#  121.8-128.2  全コミットの銀河（AI と共同のコミットは桃色）     ブリッジ
#  128.2-131.4  富士の湖に花火、金のタイトル                      ラストサビ「Creative Vision 10！」
#  131.4-134.6  四季の里を筆で塗っていく                          「今こそ 夢を描け」
#  134.6-137.8  ミニキャラの群れと笑顔                            「みんなの笑顔 あふれ出すよ」
#  137.8-139.4  いこう！
#  139.4-146.6  3階層の扉（op の扉を引き延ばし、灯籠が扉へ集まる） 「まだ見ぬ明日の扉 / みんなで開こうよ」
#  146.6-153.8  夜明けの富士を背に瞳から全景へ                    「目指すはひとつ あの未来へ」
#  153.8-157.2  スタッフロール（実数値）                          アウトロ
#  157.2-160.2  タイトル                                          「Creative Vision 10！」
#  160.2-163.8  エンディング（op と同じ）
import os, re, glob, math, subprocess
import numpy as np
import engine as E, rig as R, fx, clouds, songsync as SS, prep_full
import op as O
from engine import prog, lerp, smooth, ease_out, ease_in, ease_io, ease_out_back, pulse, clamp

FPS = 30
SUPERSAMPLE = 2

# 歌い手の版（Doc/promo/song/README.md 1・2章）。名前: (メイン, コーラス)
SINGERS = {
    'metan': ('四国めたん', ['波音リツ', '小夜/SAYO', '九州そら']),
    'nana': ('春歌ナナ', ['波音リツ', '小夜/SAYO', '九州そら']),
    'miko': ('櫻歌ミコ', ['波音リツ', '小夜/SAYO', '九州そら']),
    'takehiro': ('玄野武宏', ['波音リツ', '小夜/SAYO', '九州そら']),
    'zunko': ('東北ずん子', ['波音リツ', '小夜/SAYO', '九州そら']),
    'zundamon': ('ずんだもん', ['波音リツ', '小夜/SAYO', '九州そら']),
    'ritsu': ('波音リツ', ['猫使アル', '小夜/SAYO', '九州そら']),
    'al': ('猫使アル', ['波音リツ', '小夜/SAYO', '九州そら']),
    'whitecul': ('WhiteCUL', ['波音リツ', '小夜/SAYO', '九州そら']),
    'hau': ('雨晴はう', ['波音リツ', '小夜/SAYO', '九州そら']),
    'usagi': ('中国うさぎ', ['波音リツ', '小夜/SAYO', '九州そら']),
}
SINGER = os.environ.get('FILM_VARIANT') or 'metan'
if SINGER not in SINGERS:
    raise SystemExit(f'歌い手の版がない: {SINGER}（{", ".join(SINGERS)}）')
MAIN, CHORUS = SINGERS[SINGER]
OUT_NAME = f'opfull_{SINGER}'
SONG = os.path.join(SS.SONG_DIR, f'creativevision10_full_{SINGER}.m4a')
VX = lambda n: f'VOICEVOX:{n}'
SONG_LEN = 163.55
DURATION = SONG_LEN + 0.25

# op.py のショットを音源の時刻そのままで使う（間奏の手前で切らない）
O.remap = lambda t: t
O.T_CUT = SS.bar(100)          # エンディングの一撃の基準（音源 160.2 秒）
O.DURATION = DURATION
O.SONG = SONG
# 歌い手の表示（op.py の冒頭のクレジットとエンディング）
O.CREDITS = [c if c[2][0] not in ('歌', 'コーラス') else
             (c[0], c[1], ['歌', VX(MAIN)] if c[2][0] == '歌' else ['コーラス', '　'.join(VX(n) for n in CHORUS)], c[3]) for c in O.CREDITS]
O.VOICES = '　'.join(VX(n) for n in [MAIN] + CHORUS)

C = None
SB = SS.BEAT
PET_ROWS = dict(idle=(0, 6), run=(1, 8), wave=(3, 4), jump=(4, 5), wait=(6, 6), review=(8, 6))


# ---------------------------------------------------------------- 準備
def setup(gl):
    global C
    O.setup(gl)
    C = O.C
    C.vocal = SS.Vocal(SINGER)      # 口パクはこの版の歌だけの音源の音量で
    C.lyrics = SS.lines({s['name'] for s in SS.timeline()})
    paths = prep_full.build()
    C.fuji = gl.image(paths['fuji'], key=('full', 'fuji'))
    C.fuji_m = gl.image(paths['fuji_mask'], key=('full', 'fujim'))
    C.seasons = gl.image(paths['seasons'], key=('full', 'seasons'))
    C.pet = gl.image(paths['pet'], key=('full', 'pet'))
    C.pet_wh = (1536, 1872)
    C.fuji_wh = (2400, 1536)
    C.seasons_wh = (2752, 1240)
    repo_facts()
    C.heat = gl.texture(C.heat_arr, key=('full', 'heat'), mipmap=False, premul=False)
    make_galaxy()


def git(*args):
    p = subprocess.run(['git', '-C', E.REPO] + list(args), stdout=subprocess.PIPE, check=True, encoding='utf8', errors='ignore')
    return p.stdout


def repo_facts():
    """台本に出す実数値と実データ（実行時に repo から数える）"""
    # コミット: 古い順。本文に Co-Authored-By があれば AI と共同（共同作者はすべて AI エージェント）
    raw = git('log', '--reverse', '--date=short', '--format=%x1e%ad%x1f%s%x1f%b')
    C.clog = []
    for rec in raw.split('\x1e'):
        if '\x1f' not in rec:
            continue
        d, s, b = (rec.split('\x1f') + ['', ''])[:3]
        C.clog.append((d.strip(), s.strip(), 'co-authored-by' in b.lower()))
    C.n_ai = sum(1 for c in C.clog if c[2])
    fam = {}
    for line in git('log', '--format=%b').splitlines():
        m = re.match(r'\s*co-authored-by:\s*([A-Za-z]+)', line, re.I)
        if m:
            k = m.group(1).capitalize()
            if k in ('Claude', 'Codex', 'Opencode', 'Github', 'Sisyphus'):
                k = {'Opencode': 'OpenCode', 'Github': 'GitHub Copilot'}.get(k, k)
                fam[k] = fam.get(k, 0) + 1
    C.ai_fam = sorted(fam.items(), key=lambda kv: -kv[1])
    # 修正のコミット（迷った日）
    C.fixes = [s for d, s, a in C.clog if ('修正' in s or s.lower().startswith('fix')) and 6 < len(s) < 34]
    # 日ごとのコミット数（週 × 曜日の表）
    import datetime as dt
    days = {}
    for d, s, a in C.clog:
        days[d] = days.get(d, 0) + 1
    C.n_days = len(days)
    d0 = dt.date.fromisoformat(C.clog[0][0]); d1 = dt.date.fromisoformat(C.clog[-1][0])
    start = d0 - dt.timedelta(days=(d0.weekday() + 1) % 7)       # 日曜はじまり
    weeks = (d1 - start).days // 7 + 1
    arr = np.zeros((7, weeks, 4), np.float32)
    C.heat_weeks = []
    for w in range(weeks):
        C.heat_weeks.append((start + dt.timedelta(days=w * 7)).isoformat())
        for k in range(7):
            day = start + dt.timedelta(days=w * 7 + k)
            n = days.get(day.isoformat(), 0)
            v = 0.0 if n == 0 else (0.25 if n < 3 else 0.5 if n < 8 else 0.75 if n < 16 else 1.0)
            arr[k, w] = (v, 1.0 if d0 <= day <= d1 else 0.0, 0, 1)
    C.heat_arr = arr
    C.heat_dims = (weeks, 7)
    # テスト（属性の数と、実際のテスト名）
    n_attr = 0; names = []
    for f in glob.glob(os.path.join(E.REPO, 'Tests', '**', '*.cs'), recursive=True):
        try:
            src = open(f, encoding='utf8', errors='ignore').read()
        except Exception:
            continue
        n_attr += len(re.findall(r'\[(Fact|TestMethod|Test)\]', src))
        names += re.findall(r'\[(?:Fact|TestMethod|Test)\][^\n]*\n(?:\s*\[[^\n]*\n)*\s*public\s+(?:async\s+)?(?:void|Task)\s+(\w+)', src)
    C.n_tests = n_attr
    rng = np.random.default_rng(3)
    C.test_names = [names[i] for i in rng.permutation(len(names))][:160] or ['Save_Validates']
    # 画面・帳票（README と同じ数え方）
    C.n_views = len([p for p in glob.glob(os.path.join(E.REPO, 'CvWpfclient', 'Views', '**', '*View.xaml'), recursive=True)
                     if os.sep + 'Sub' + os.sep not in p])
    C.n_forms = len(glob.glob(os.path.join(E.REPO, 'printform', '*.qfm')))
    # gRPC の呼び出し名（共有インタフェース）
    calls = []
    for f in glob.glob(os.path.join(E.REPO, 'CodeShare', '**', 'I*Service.cs'), recursive=True):
        src = open(f, encoding='utf8', errors='ignore').read()
        svc = os.path.basename(f)[:-3]
        calls += [f'{svc}.{m}' for m in re.findall(r'\b(\w+Async)\s*\(', src)]
    C.grpc = sorted(set(calls)) or ['ICoreService.QueryMsgAsync']


def make_galaxy():
    """全コミットを渦巻きの星にする（中心が最初のコミット、外側ほど新しい）"""
    n = len(C.clog)
    rng = np.random.default_rng(9)
    i = np.arange(n)
    q = i / max(n - 1, 1)
    r = 0.35 + 6.2 * q ** 0.75
    arm = (i % 2) * math.pi
    a = arm + r * 1.15 + rng.normal(0, 0.18, n) * (0.4 + q)
    y = rng.normal(0, 0.12, n) * (1.2 - q)
    C.gal = np.stack([np.cos(a) * r, y, np.sin(a) * r], -1)
    C.gal_ai = np.array([c[2] for c in C.clog])
    C.gal_dust = np.stack([np.cos(a + rng.normal(0, 0.5, n)) * r * rng.uniform(0.8, 1.2, n), rng.normal(0, 0.25, n),
                           np.sin(a + rng.normal(0, 0.5, n)) * r * rng.uniform(0.8, 1.2, n)], -1)


# ---------------------------------------------------------------- シェーダ
FUJI_FS = E.HEADER + fx.NOISE + r'''
uniform sampler2D tex, msk;
uniform float dawn, ripple, twinkle, lod, alpha, aspect;
uniform vec2 sun;
float lum(vec3 c){ return dot(c, vec3(0.3, 0.59, 0.11)); }
void main(){
    vec2 p = uv;
    vec4 m = texture(msk, p);
    float land = m.r, snow = m.g, lake = m.b;
    // 湖面のゆらぎ（映り込みを横に揺らす）
    vec2 q = p;
    float w = lake*ripple;
    q.x += w*0.0009*sin(p.y*140.0 - time*1.6 + 2.0*sin(p.x*12.0 + time*0.4));
    q.y += w*0.0003*sin(p.y*220.0 + time*2.2);
    vec3 c = textureLod(tex, q, lod).rgb;
    // 星のまたたき: 空の細かい明点だけを明滅させる
    if(twinkle > 0.0){
        vec3 cb = textureLod(tex, p, 2.5).rgb;
        float pk = max(lum(c - cb), 0.0);
        vec2 cell = floor(p*vec2(700.0, 448.0));
        float h = h21(cell);
        float tw = sin(time*(2.0 + 6.0*h) + h*50.0);
        c += (1.0 - land)*pk*twinkle*tw*1.3*vec3(0.9, 0.95, 1.0);
    }
    if(dawn > 0.0){
        float sk = 1.0 - land;
        float y = p.y;
        vec3 top = vec3(0.13, 0.17, 0.44), mid = vec3(0.74, 0.42, 0.58), hor = vec3(0.96, 0.62, 0.40);
        vec3 g = mix(top, mid, smoothstep(0.02, 0.42, y));
        g = mix(g, hor, smoothstep(0.36, 0.64, y));
        vec2 d = (p - sun)*vec2(aspect, 1.0);
        float sd = length(d);
        g += vec3(1.0, 0.78, 0.50)*(0.14*exp(-sd*6.0) + 0.45*exp(-sd*26.0));
        g = mix(g, vec3(1.25, 1.1, 0.9), smoothstep(0.034, 0.028, sd));
        vec3 skyc = g + c*0.35*(1.0 - 0.7*dawn);            // 天の川と星はうすく残して消えていく
        c = mix(c, skyc, sk*dawn);
        vec3 lit = c*vec3(1.3, 1.12, 1.05) + vec3(0.12, 0.06, 0.09)*(0.4 + 0.6*smoothstep(0.95, 0.55, y));
        c = mix(c, lit, land*dawn);
        // 赤富士: 雪の笠が朝焼けに染まる
        c = mix(c, c*vec3(1.35, 0.92, 0.92) + vec3(0.30, 0.10, 0.14), snow*dawn);
        // 稜線の縁の光（太陽に近いほど強い）
        float above = texture(msk, p - vec2(0.0, 0.007)).r;
        c += vec3(1.0, 0.72, 0.45)*land*(1.0 - above)*dawn*1.4*exp(-sd*2.2);
        // 湖面: 空の色と太陽の光の道
        float road = exp(-abs(d.x)*26.0)*(0.55 + 0.45*sin(p.y*900.0 + time*4.0 + sin(p.x*200.0)));
        c += lake*dawn*(vec3(0.50, 0.30, 0.30)*0.55 + vec3(1.0, 0.72, 0.45)*road*0.9);
    }
    f = vec4(c*alpha, alpha);
}
'''

SEASON_FS = E.HEADER + fx.NOISE + r'''
uniform sampler2D tex; uniform vec2 texel;
uniform float paint, lod, alpha, warm;
float lum(vec3 c){ return dot(c, vec3(0.3, 0.59, 0.11)); }
void main(){
    vec2 p = uv;
    vec3 c = textureLod(tex, p, lod).rgb;
    if(paint < 1.5){
        // 線画（輝度の輪郭）を紙の上に描き、筆の通ったところから色が付く
        vec2 o = texel*1.6;
        float tl = lum(texture(tex, p + vec2(-o.x, -o.y)).rgb), tc = lum(texture(tex, p + vec2(0.0, -o.y)).rgb), tr = lum(texture(tex, p + vec2(o.x, -o.y)).rgb);
        float ml = lum(texture(tex, p + vec2(-o.x, 0.0)).rgb), mr = lum(texture(tex, p + vec2(o.x, 0.0)).rgb);
        float bl = lum(texture(tex, p + vec2(-o.x, o.y)).rgb), bc = lum(texture(tex, p + vec2(0.0, o.y)).rgb), br = lum(texture(tex, p + vec2(o.x, o.y)).rgb);
        float gx = (tr + 2.0*mr + br) - (tl + 2.0*ml + bl);
        float gy = (bl + 2.0*bc + br) - (tl + 2.0*tc + tr);
        float e = clamp(length(vec2(gx, gy))*2.4 - 0.10, 0.0, 1.0);
        vec3 paper = vec3(0.98, 0.96, 0.93)*(0.94 + 0.06*vnoise(p*vec2(900.0, 400.0)));
        vec3 la = mix(paper, vec3(0.30, 0.22, 0.40), e);
        float n = fbm(p*vec2(5.0, 2.5));
        float front = paint + (n - 0.5)*0.10 + 0.02*sin(p.y*16.0 + time*3.0);
        float k = smoothstep(front + 0.012, front - 0.012, p.x);
        // 塗りたては少し鮮やかに
        vec3 fresh = c*(1.0 + 0.35*exp(-max(front - p.x, 0.0)*18.0));
        c = mix(la, fresh, k);
        c += vec3(1.0, 0.78, 0.95)*exp(-abs(p.x - front)*140.0)*0.55;
    }
    c *= mix(vec3(1.0), vec3(1.08, 1.0, 0.92), warm);
    f = vec4(c*alpha, alpha);
}
'''

SPRITE_FS = E.HEADER + r'''
uniform sampler2D tex; uniform vec4 cell; uniform vec4 tint; uniform vec3 add; uniform float flipX, holo;
void main(){
    vec2 q = vec2(flipX > 0.5 ? 1.0 - uv.x : uv.x, uv.y);
    vec4 c = texture(tex, cell.xy + q*cell.zw);
    c.rgb = c.rgb*tint.rgb + add*c.a;
    if(holo > 0.0){
        // ホログラムで現れる（走査線と下からの出現）
        float scan = 0.75 + 0.25*sin(uv.y*260.0 - time*12.0);
        float show = smoothstep(1.0 - holo*1.07, 1.07 - holo*1.07, uv.y);
        c.rgb = mix(c.rgb, vec3(0.55, 0.9, 1.3)*c.a*scan, (1.0 - smoothstep(0.85, 1.0, holo))*0.7);
        c *= show;
    }
    f = c*tint.a;
}
'''

ORB_FS = E.HEADER + fx.NOISE + r'''
uniform vec3 col; uniform float alpha;
void main(){
    vec2 p = uv*2.0 - 1.0;
    float r = length(p);
    float a = atan(p.y, p.x);
    float core = exp(-r*r*7.0);
    float shell = smoothstep(0.6, 0.57, r)*(0.2 + 0.8*smoothstep(0.25, 0.58, r));
    float flow = 0.55 + 0.45*vnoise(p*7.0 + vec2(time*0.7, -time*0.4));
    float ring1 = smoothstep(0.018, 0.0, abs(r - 0.76))*(0.45 + 0.55*sin(a*6.0 - time*3.0));
    float ring2 = smoothstep(0.02, 0.0, abs(length(vec2(p.x, p.y*2.8)) - 0.92))*0.9;
    float ring3 = smoothstep(0.012, 0.0, abs(r - 0.88))*step(0.5, fract(a*3.0/3.14159 + time*0.3));
    vec3 c = col*(core*1.6 + shell*0.7*flow + ring1 + ring2 + ring3*0.6) + vec3(1.0)*core*0.6;
    f = vec4(c*alpha, 0.0);
}
'''

RAIN_FS = E.HEADER + fx.NOISE + r'''
uniform float amount, speed, aspect, up; uniform vec3 col;
void main(){
    vec2 q = suv*vec2(aspect, 1.0);
    float a = 0.0;
    for(int L=0; L<3; L++){
        float fl = float(L);
        vec2 g = q*vec2(60.0 + fl*45.0, 2.6 + fl*1.4);
        g.y += (up > 0.5 ? 1.0 : -1.0)*time*speed*(1.0 + fl*0.45);
        g.x += g.y*0.06;
        vec2 id = floor(g); vec2 fp = fract(g);
        float h = h21(id + fl*13.0);
        float s = step(0.78, h)*smoothstep(0.09, 0.0, abs(fp.x - 0.5) - 0.015);
        s *= up > 0.5 ? smoothstep(1.0, 0.7, fp.y)*smoothstep(0.0, 0.5, fp.y) : smoothstep(0.0, 0.3, fp.y)*smoothstep(1.0, 0.6, fp.y);
        a += s*(0.45 + 0.3*fl);
    }
    f = vec4(col*a*amount, 0.0);
}
'''

GRID_FS = E.HEADER + r'''
uniform sampler2D dat; uniform vec2 dims; uniform float reveal, alpha, u0, u1;
void main(){
    vec2 g = vec2(mix(u0, u1, uv.x), uv.y)*dims;
    vec2 id = floor(g); vec2 fp = fract(g) - 0.5;
    vec4 v = texelFetch(dat, ivec2(clamp(id, vec2(0.0), dims - 1.0)), 0);
    float age = reveal*dims.x - id.x;
    float on = clamp(age, 0.0, 1.0);
    float d = max(abs(fp.x), abs(fp.y)) - 0.36;
    float box = smoothstep(0.04, -0.01, d)*(0.25 + 0.75*v.g);
    vec3 empty = vec3(0.13, 0.11, 0.28);
    vec3 lv = v.r <= 0.0 ? empty*1.3 : mix(vec3(0.45, 0.22, 0.75), vec3(1.25, 0.62, 1.05), v.r);
    float fresh = age > 0.0 ? exp(-age*0.7) : 0.0;
    vec3 c = mix(empty, lv*(1.0 + 1.8*fresh), on);
    f = vec4(c*box*alpha, box*alpha);
}
'''

DRAW_FS = E.HEADER + fx.NOISE + r'''
uniform sampler2D tex; uniform vec2 texel; uniform float lineK, colK, alpha; uniform vec3 lineCol;
float lum(vec3 c){ return dot(c, vec3(0.3, 0.59, 0.11)); }
void main(){
    vec2 p = uv;
    vec2 o = texel*1.3;
    float gx = lum(texture(tex, p + vec2(o.x, 0.0)).rgb) - lum(texture(tex, p - vec2(o.x, 0.0)).rgb);
    float gy = lum(texture(tex, p + vec2(0.0, o.y)).rgb) - lum(texture(tex, p - vec2(0.0, o.y)).rgb);
    float e = clamp(length(vec2(gx, gy))*5.0 - 0.06, 0.0, 1.0);
    float s = p.x*0.72 + p.y*0.28 + (vnoise(p*14.0) - 0.5)*0.05;
    float L = smoothstep(lineK, lineK - 0.025, s);
    float K = smoothstep(colK, colK - 0.10, s);
    vec3 col = texture(tex, p).rgb;
    vec3 c = lineCol*e*L*1.7 + vec3(0.03, 0.03, 0.10)*L;
    c += lineCol*exp(-abs(s - lineK)*70.0)*0.9*(1.0 - K)*step(0.0, lineK);
    c = mix(c, col*(1.0 + 0.5*exp(-max(colK - s, 0.0)*12.0)), K);
    vec2 b = min(p, 1.0 - p);
    float frame = smoothstep(0.012, 0.0, min(b.x*0.6, b.y))*L;
    c += lineCol*frame*1.2;
    float A = max(L*0.82, K);
    f = vec4(c*alpha, A*alpha);
}
'''


# ---------------------------------------------------------------- 道具
def img_H(wh, cx, cy, zoom, sx=960, sy=540, rot=0.0):
    """画像の (cx,cy) 画素を画面 (sx,sy) に置き、1画素を zoom 画面px にする"""
    w, h = wh
    return E.m3_translate(sx, sy) @ E.m3_rot(rot) @ E.m3_scale(zoom) @ E.m3_translate(-cx, -cy) @ E.m3_scale(w, h)


def fuji(Hm, dawn=0.0, ripple=1.0, twinkle=1.0, sun=(0.79, 0.64), lod=0.0, alpha=1.0):
    C.gl.draw(FUJI_FS, Hm, dict(dawn=dawn, ripple=ripple, twinkle=twinkle, sun=sun, lod=lod, alpha=alpha,
                                aspect=C.fuji_wh[0] / C.fuji_wh[1]), dict(tex=C.fuji, msk=C.fuji_m))


def fuji_pt(Hm, u, v):
    p = Hm @ np.array([u, v, 1.0])
    return p[0] / p[2], p[1] / p[2]


def seasons(Hm, paint=2.0, lod=0.0, alpha=1.0, warm=0.0):
    w, h = C.seasons_wh
    C.gl.draw(SEASON_FS, Hm, dict(paint=paint, lod=lod, alpha=alpha, warm=warm, texel=(1 / w, 1 / h)), dict(tex=C.seasons))


def pet(state, t, x, y, scale=1.0, alpha=1.0, flip=False, fps=10.0, phase=0.0, holo=0.0, tint=(1, 1, 1), add=(0, 0, 0), angle=0.0):
    """ミニキャラ（足元 x,y 基準）"""
    row, n = PET_ROWS[state]
    i = int(math.floor(t * fps + phase)) % n
    W, H = C.pet_wh
    cw, ch = 192, 208
    cell = (i * cw / W, row * ch / H, cw / W, ch / H)
    Hm = C.gl.rect_h(x, y, cw * scale, ch * scale, angle=angle, anchor=(0.5, 0.97))
    C.gl.draw(SPRITE_FS, Hm, dict(cell=cell, tint=(*tint, alpha), add=add, flipX=1.0 if flip else 0.0, holo=holo), dict(tex=C.pet))


def rig_pt(Hm, x, y):
    """リグ画像（2048px）の点を画面座標へ"""
    p = Hm @ np.array([x / 2048, y / 2048, 1.0])
    return p[0] / p[2], p[1] / p[2]


def sing_pose(seed, expr=(1, 0, 0, 0), wind=12.0, head=0.0, typing=0.0, blink=None):
    return lambda tt: O.base_pose(tt, wind=wind, head=head, expr=expr, sing=True, seed=seed, typing=typing, blink=blink)


def rain(amount=1.0, speed=1.6, col=(0.55, 0.65, 0.95), up=False):
    C.gl.fullscreen(RAIN_FS, dict(amount=amount, speed=speed, col=col, aspect=C.gl.VW / C.gl.VH, up=1.0 if up else 0.0), blend='add')


def fireworks(t, shells, gl=None):
    """花火。shells: (破裂時刻, x, y, 色, 大きさ)。打ち上げの尾・破裂の閃光・尾を引く星・重力と減衰"""
    gl = gl or C.gl
    for j, (t0, x, y, col, size) in enumerate(shells):
        dt = t - t0
        if -0.6 < dt < 0:          # 打ち上げ
            k = 1 + dt / 0.6
            yy = lerp(1150, y, ease_out(k, 2))
            fx.parts(gl, [x + 6 * math.sin(dt * 30)], [yy], [16], (1.0, 0.85, 0.6), [0.9], kind=0)
            ys = yy + np.arange(10) * 14
            fx.parts(gl, np.full(10, x), ys, np.linspace(10, 3, 10), (1.0, 0.7, 0.4), np.linspace(0.6, 0.0, 10), kind=0)
            continue
        if not (0 <= dt < 2.4):
            continue
        rng = np.random.default_rng(100 + j)
        n = 140
        ang = rng.uniform(0, 2 * math.pi, n)
        el = rng.uniform(-1, 1, n)
        sp = size * (0.85 + 0.15 * rng.uniform(0, 1, n))
        vx = np.cos(ang) * np.sqrt(1 - el ** 2) * sp; vy = np.sin(ang) * np.sqrt(1 - el ** 2) * sp
        fade = clamp(1 - dt / 2.3) ** 1.6
        for k in range(6):
            tk = max(dt - k * 0.045, 0.0)
            reach = 1 - math.exp(-tk * 2.6)
            px = x + vx * reach
            py = y + vy * reach + 90 * tk * tk
            a = fade * (1 - k / 6) * (0.9 if k == 0 else 0.5)
            fx.parts(gl, px, py, np.full(n, 18 - k * 2.2), np.array(col) * 1.6, np.full(n, min(1.0, a * 1.4)), kind=0)
        if dt > 0.4:   # 終わり際のきらめき
            tw = (np.sin(dt * 40 + np.arange(n)) > 0.3) * fade
            reach = 1 - math.exp(-dt * 2.6)
            fx.parts(gl, x + vx * reach, y + vy * reach + 90 * dt * dt, np.full(n, 22), (1, 1, 1), tw * 0.9, kind=1)
        if dt < 0.25:
            fx.parts(gl, [x], [y], [size * 3.5 * (1 - dt * 3)], col, [0.6 * (1 - dt * 4)], kind=0)
        fx.parts(gl, [x], [y], [size * 4.0], col, [0.12 * fade], kind=0)


def glow_text(s, x, y, size, alpha=1.0, color=(1, 1, 1), stroke_color=E.hexc('3b1a6e'), fam='noto', weight='Black', stroke=5,
              halo=None, scale=1.0, angle=0.0, anchor=(0.5, 0.5)):
    gl = C.gl
    tex = gl.text(s, size, fam=fam, weight=weight, color=color, stroke=stroke, stroke_color=stroke_color)
    if halo is not None:
        w, h = gl.vsize(tex)
        fx.parts(gl, [x], [y], [max(w, h) * 1.3 * scale], halo, [0.35 * alpha], kind=0)
    gl.sprite(tex, x, y, scale=scale, alpha=alpha, angle=angle, anchor=anchor)
    return tex


def line_at(t, prefix):
    """時刻 t に近い歌詞の行（prefix で始まるもの）"""
    cands = [l for l in C.lyrics if l['text'].startswith(prefix)]
    return min(cands, key=lambda l: abs(l['start'] - t))


def drop_letters(t, line, mode='drop', color=(1, 1, 1), stroke_color=E.hexc('7b1fa2'), y=150, size=124):
    """タイトルの文字を歌のモーラに合わせて1文字ずつ出す（drop: 上から落ちる / zoom: 手前から飛び込む）"""
    gl = C.gl
    word = 'CREATIVE VISION 10'
    ms = line['moras']
    times = np.linspace(ms[0]['t'] - 0.05, ms[-1]['t'] + 0.1, len(word))
    texs = [gl.text(ch, size, fam='segoeb', weight=None, color=color, stroke=8, stroke_color=stroke_color) if ch != ' ' else None
            for ch in word]
    widths = [gl.vsize(tx)[0] * 0.78 if tx else 40 for tx in texs]
    x = 960 - sum(widths) / 2
    for i, (tx, w) in enumerate(zip(texs, widths)):
        cxl = x + w / 2
        x += w
        if tx is None:
            continue
        k = prog(t, times[i], times[i] + (0.28 if mode == 'drop' else 0.22))
        if k <= 0:
            continue
        if mode == 'drop':
            yy = y - 300 * (1 - ease_out_back(k, 2.0))
            gl.sprite(tx, cxl, yy + 6 * pulse(t, times[i] + 0.12, 0.01, 0.15), angle=(1 - k) * 0.5 * (1 if i % 2 else -1))
        else:
            sc = lerp(4.0, 1.0, ease_out(k, 3))
            gl.sprite(tx, lerp(960, cxl, ease_out(k, 2)), y, scale=sc, alpha=min(1.0, k * 2.0))
            r = pulse(t, times[i] + 0.22, 0.01, 0.3)
            if r > 0:
                fx.parts(gl, [cxl], [y], [260 * (1.4 - r)], (0.7, 0.95, 1.0), [0.5 * r], kind=0)


def chibi_crowd(t, n, y0, y1, seed, states=('wave', 'jump'), alpha=1.0, xr=(-60, 1980), jump_h=60):
    """ミニキャラの群れ（奥ほど小さく暗い）"""
    rng = np.random.default_rng(seed)
    depth = np.sort(rng.uniform(0, 1, n))
    for k in range(n):
        dpt = depth[k]
        st = states[rng.integers(len(states))]
        x = rng.uniform(*xr)
        y = lerp(y0, y1, dpt)
        sc = lerp(0.55, 1.35, dpt)
        ph = rng.uniform(0, 10)
        hop = 0.0
        if st == 'jump':
            cyc = (t * 1.25 + ph) % 1.0
            hop = jump_h * sc * math.sin(math.pi * clamp((cyc - 0.15) / 0.7)) ** 1.2
        shade = lerp(0.62, 1.0, dpt)
        pet(st, t, x, y - hop, scale=sc, alpha=alpha, flip=bool(rng.integers(2)), fps=8 + 4 * rng.uniform(), phase=ph,
            tint=(shade, shade, min(1.0, shade + 0.08)))


CREDITS2 = [
    (65.0, 68.4, ['背景原画（富士山と天の川・四季の里）', '静止画の生成画像に、光・天候・朝焼けをコードで付加'], 'lt'),
    (77.6, 80.0, ['ミニキャラ', 'Codex のペット（走る・手を振る・跳ぶ）'], 'lt'),
]


def credits2(t):
    gl = C.gl
    for (a, b, lines, pos) in CREDITS2:
        if a <= t <= b:
            al = min(prog(t, a, a + 0.4), 1 - prog(t, b - 0.4, b))
            y = 70 if pos[1] == 't' else 860
            for i, s in enumerate(lines):
                tex = gl.text(s, 22 if i == 0 else 30, fam='noto', weight='Bold', color=(1, 1, 1), stroke=3, stroke_color=(0.1, 0.05, 0.2))
                w, h = gl.vsize(tex)
                x = 70 if pos[0] == 'l' else 1850 - w
                gl.sprite(tex, x, y + i * 40, anchor=(0, 0), alpha=al * (0.85 if i == 0 else 1.0))


# ---------------------------------------------------------------- 追加のショット
def f_to_night(t, lt, d):
    """日の出の空を見上げると、天の川の夜空へ"""
    gl = C.gl
    k = ease_io(prog(lt, 0.0, d), 2)
    O.sky_layer(t, 'sunrise', cam=(0, 160 + 900 * k), cloud_y=960, scroll=10, n=8, scale=0.7, haze=0.45, seed=14, drift=0.6)
    fx.god_rays(gl, 0.66, 0.62 + 0.5 * k, amount=0.7 * (1 - k), decay=0.975)
    a = smooth(prog(lt, 0.35, d))
    Hm = img_H(C.fuji_wh, 1250, lerp(820, 560, k), 1.05)      # 見上げるにつれて星空が上から下りてくる
    fuji(Hm, alpha=a)
    return dict(bloomAmt=0.7, vign=0.3)


def constellation(t, t0, cx, cy, s, gl=None):
    """星座の「10」: 星が順に結ばれていく"""
    gl = gl or C.gl
    one = [(-1.15, -0.55), (-0.8, -0.9), (-0.8, 0.9)]
    zero = [(0.65 + 0.62 * math.sin(a), 0.92 * -math.cos(a)) for a in np.linspace(0, 2 * math.pi, 10)]
    paths = [one, zero]
    stars = []
    seg_t = 0.11
    tt = t - t0
    idx = 0
    for path in paths:
        pts = [(cx + px * s, cy + py * s) for px, py in path]
        for i in range(len(pts)):
            st0 = idx * seg_t
            a = prog(tt, st0, st0 + 0.15)
            if a > 0:
                stars.append((pts[i], a))
            if i + 1 < len(pts):
                k = prog(tt, st0, st0 + seg_t)
                if k > 0:
                    (x0, y0), (x1, y1) = pts[i], pts[i + 1]
                    L = math.hypot(x1 - x0, y1 - y0)
                    m = max(2, int(L * k / 7))
                    q = np.linspace(0, k, m)
                    fx.parts(gl, x0 + (x1 - x0) * q, y0 + (y1 - y0) * q, np.full(m, 7.0), (0.75, 0.85, 1.0), np.full(m, 0.55), kind=0)
            idx += 1
    for (x, y), a in stars:
        fx.parts(gl, [x], [y], [48 * a], (0.9, 0.95, 1.0), [a], kind=1)
        fx.parts(gl, [x], [y], [90], (0.6, 0.7, 1.0), [0.35 * a], kind=0)


def shooting_star(t, t0, x0, y0, x1, y1, dur=0.55):
    k = prog(t, t0, t0 + dur)
    if not 0 < k < 1:
        return
    x, y = lerp(x0, x1, ease_in(k, 1.4)), lerp(y0, y1, ease_in(k, 1.4))
    n = 26
    q = np.arange(n) / n
    fx.parts(C.gl, x - (x1 - x0) * 0.16 * q, y - (y1 - y0) * 0.16 * q, 11 * (1 - q) + 2, (0.85, 0.92, 1.0), (1 - q) * math.sin(math.pi * k) * 1.3, kind=0)


def f_fuji_night(t, lt, d):
    gl = C.gl
    k = ease_io(prog(lt, 0.0, d), 2)
    Hm = img_H(C.fuji_wh, lerp(1250, 1200, k), lerp(560, 870, k), lerp(1.05, 0.82, k))
    fuji(Hm, twinkle=1.0, ripple=1.0)
    shooting_star(t, 65.0, 1500, 120, 1050, 330)
    shooting_star(t, 66.6, 400, 90, 120, 260, 0.45)
    shooting_star(t, 68.2, 1750, 200, 1350, 420, 0.5)
    # 湖から昇る灯籠（200の灯籠の再登場）
    rng = np.random.default_rng(64)
    n = 36
    ys = 1040 - ((lt * rng.uniform(20, 45, n) + rng.uniform(0, 420, n)) % 520)
    xs = rng.uniform(300, 1600, n) + np.sin(lt + np.arange(n)) * 12
    sz = rng.uniform(8, 22, n)
    a = smooth(prog(lt, 0.5, 2.0)) * np.clip((ys - 560) / 300, 0, 1)
    fx.parts(gl, xs, ys, sz, E.hexc('ffb35c'), 0.8 * a, kind=4)
    fx.parts(gl, xs, ys, sz * 4, (1.0, 0.55, 0.2), 0.14 * a, kind=0)
    constellation(t, 68.6, 1340, 250, 150)
    return dict(bloomAmt=0.8, vign=0.4, flash=smooth(prog(lt, d - 0.25, d)) * 0.7, flashCol=(0.8, 0.9, 1.0))


def f_wind(t, lt, d):
    """風より速く: 通信（gRPC）の光が追い越していく"""
    gl = C.gl
    O.sky_layer(t, 'day', cam=(0, 0), cloud_y=980, scroll=-260, n=8, scale=0.62, haze=0.35, seed=21)
    fx.focus_lines(gl, inner=0.5, seed=int(t * 15), amount=0.45, col=(1, 1, 1), mode=1)
    rng = np.random.default_rng(70)
    pk = []
    for j in range(12):
        sp = rng.uniform(1300, 2300)
        ph = rng.uniform(0, 3)
        y = rng.uniform(120, 960)
        x = 2200 - ((lt + ph) * sp) % 2900
        pk.append((x, y, sp, j))
    def packets(front):
        for x, y, sp, j in pk:
            if (j % 3 == 0) != front:
                continue
            n = 30
            q = np.arange(n) / n
            fx.parts(gl, x + q * 520, np.full(n, y), 40 * (1 - q) + 6, (0.45, 0.8, 1.2), (1 - q) * 0.9, kind=0)
            fx.parts(gl, [x], [y], [130], (0.9, 0.97, 1.0), [1.0], kind=1)
            name = C.grpc[j % len(C.grpc)]
            tex = gl.text(name, 30, fam='mono', weight=None, color=(1, 1, 1), stroke=3, stroke_color=E.hexc('1b4a8e'))
            gl.sprite(tex, x + 40, y - 36, anchor=(0, 0.5), alpha=0.95)
    packets(False)
    roll = 0.05 * math.sin(lt * 1.3)
    Hm = O.char_H(1050, 960, 0.72 + 0.03 * lt / d, sx=1180 + 10 * math.sin(lt * 2), sy=620 + 8 * math.sin(lt * 3.1), rot=roll)
    look = dict(cropBottom=1600, rimCol=(1.0, 1.1, 1.3), rimDir=(-0.6, -1), rimW=6)
    O.draw_char('wind', t, 70.6 - 0.5, sing_pose(21, wind=75 + 15 * math.sin(lt * 5), head=-2.0), Hm, look)
    O.fog_bottom(E.hexc('cfe4ff'), 0.95, 880, 1080)
    packets(True)
    tex = gl.text('gRPC · HTTP/2', 30, fam='segoeb', weight=None, color=(1, 1, 1), stroke=3, stroke_color=E.hexc('1b4a8e'))
    gl.sprite(tex, 1840, 120, anchor=(1, 0.5), alpha=prog(lt, 0.4, 0.9) * 0.9)
    return dict(bloomAmt=0.45, vign=0.15, ca=2.5, **O.flash(t, 70.6, 0.25, (0.8, 0.9, 1.0)))


def f_open(t, lt, d):
    """扉が左右に開き、四季の里が広がる。オープンな技術の名前が四方へ"""
    gl = C.gl
    k = ease_out(prog(lt, 0.15, d), 2)
    z = lerp(1.55, 0.93, k)
    Hm = img_H(C.seasons_wh, lerp(1150, 1330, k), lerp(560, 640, k), z)
    seasons(Hm)
    # 花びら（春の側）
    rng = np.random.default_rng(73)
    n = 60
    xs = (rng.uniform(-200, 1200, n) + lt * 140) % 2200 - 200
    ys = (rng.uniform(0, 1080, n) + lt * rng.uniform(60, 130, n)) % 1120 - 20
    fx.parts(gl, xs, ys, rng.uniform(14, 30, n), E.hexc('ffc4dd'), rng.uniform(0.5, 0.9, n), kind=3, angle=lt * 2 + np.arange(n))
    # オープンな技術
    words = ['SQLite', 'PostgreSQL', 'MariaDB', '.NET 10', 'gRPC', 'WPF', 'MVVM', 'C# 14', 'protobuf', 'GitHub']
    for i, w in enumerate(words):
        a0 = i / len(words) * 2 * math.pi + 0.3
        kk = ease_out(prog(lt, 0.35 + i * 0.12, 1.9 + i * 0.12), 3)
        if kk <= 0:
            continue
        r = lerp(40, 520 + 80 * (i % 3), kk)
        x = 960 + math.cos(a0) * r * 1.55
        y = 520 + math.sin(a0) * r * 0.75 - 30 * lt
        glow_text(w, x, y, 46, alpha=min(1.0, kk * 2) * (1 - prog(lt, d - 0.5, d)), fam='segoeb', weight=None, stroke=4,
                  stroke_color=E.hexc('5a2a8a'), halo=(0.9, 0.7, 1.0))
    # 左右に開く光の扉
    o = ease_io(prog(lt, 0.0, 0.7), 3)
    if o < 1:
        for side in (-1, 1):
            x = 960 + side * (480 + 960 * o)
            fx.rect(gl, x, 540, 960, 1080, E.hexc('f6e9ff'), alpha=1.0)
            fx.rect(gl, 960 + side * 960 * o, 540, 24, 1080, (1.0, 0.95, 0.8), alpha=1.0, blend='add')
        fx.parts(gl, [960], [540], [2400 * (1 - o) + 300], (1.0, 0.95, 0.85), [0.8 * (1 - o)], kind=0)
    return dict(bloomAmt=0.5, vign=0.25)


def f_ai(t, lt, d):
    """AI と手をつなぐ: 光の球からミニキャラが現れ、手を振る。AI と共同のコミット数"""
    gl = C.gl
    fx.sky(gl, 'dawn', t=t, cam=(0, 0.2), stars=1.0, cHor=E.hexc('c27bb0'))
    fx.burst(gl, 0.72, 0.42, rot=t * 0.15, n=24, c1=E.hexc('2a1458'), c2=E.hexc('34196a'), c3=E.hexc('6b3aa8'), alpha=0.5)
    Hm = O.char_H(1050, 980, 0.74, sx=560, sy=600)
    look = dict(cropBottom=1600, rimCol=(0.6, 0.9, 1.4), rimDir=(1, 0.1), rimW=8, outlineW=4.0, outlineCol=(1, 0.95, 1.0), outlineA=0.7)
    O.draw_char('ai', t, 77.0 - 0.5, sing_pose(22, expr=(0, 1, 0, 0), wind=10), Hm, look)
    O.fog_bottom((0.2, 0.1, 0.35), 0.9, 900, 1080)
    ox, oy = 1380, 430
    pop = ease_out_back(prog(lt, 0.0, 0.6), 1.6)
    gl.draw(ORB_FS, gl.rect_h(ox, oy, 560 * pop, 560 * pop), dict(col=(0.28, 0.5, 1.0), alpha=0.6), blend='add')
    # つながる光の糸（主人公の胸元 → 光の球）
    hx, hy = rig_pt(Hm, 1150, 1500)
    k = ease_io(prog(lt, 0.3, 1.3))
    n = 90
    q = np.linspace(0, k, n)
    bx = lerp(hx, ox - 180, q); by = lerp(hy, oy + 60, q) - np.sin(q * math.pi) * 160
    fl = (q * 6 - lt * 3) % 1.0
    fx.parts(gl, bx, by, 10 + 10 * (fl < 0.2), (1.0, 0.75, 0.95), 0.55 + 0.45 * (fl < 0.2), kind=0)
    # ミニキャラがホログラムで現れて手を振る
    h = prog(lt, 1.0, 1.9)
    if h > 0:
        pet('wave' if h >= 1 else 'idle', t, ox, oy + 470, scale=1.55, holo=h if h < 1 else 0.0)
        fx.parts(gl, [ox], [oy + 470], [380], (0.5, 0.85, 1.2), [0.25], kind=0)
    # 実数: AI と共同のコミット
    kc = ease_out(prog(lt, 1.4, 2.6), 2)
    if kc > 0:
        glow_text(f'{int(C.n_ai * kc):,}', 1700, 130, 96, fam='segoeb', weight=None, stroke=5, stroke_color=E.hexc('2a1458'), alpha=min(1, kc * 3))
        glow_text('commits  with AI', 1700, 205, 30, fam='segoe', weight=None, stroke=2, stroke_color=E.hexc('2a1458'), alpha=min(1, kc * 3))
        for i, (nm, cnt) in enumerate(C.ai_fam[:4]):
            a0 = i * 1.6 + lt * 0.6
            glow_text(nm, ox + math.cos(a0) * 330, oy + math.sin(a0) * 120 - 10, 26, fam='segoe', weight=None, stroke=2,
                      stroke_color=E.hexc('1b2a6e'), alpha=0.85 * min(1, kc * 3))
    return dict(bloomAmt=0.7, vign=0.3)


def f_morning(t, lt, d):
    """四季の里の朝日（背景はぼかして主人公の手前に光）"""
    gl = C.gl
    k = ease_io(prog(lt, 0.0, d), 2)
    Hm = img_H(C.seasons_wh, lerp(1760, 1840, k), lerp(520, 470, k), lerp(1.0, 1.12, k))
    seasons(Hm, lod=1.6, warm=0.5)
    sx, sy = fuji_pt(Hm, 0.69, 0.24)
    rise = smooth(prog(lt, 0.0, 2.2))
    fx.parts(gl, [sx], [sy + 60 * (1 - rise)], [900 * (0.5 + 0.5 * rise)], (1.0, 0.82, 0.55), [0.55 * rise], kind=0)
    fx.god_rays(gl, sx / 1920, sy / 1080, amount=0.9 * rise, decay=0.976)
    Hm2 = O.char_H(1050, 980, 0.78, sx=620, sy=590)

    def pose(tt):
        p = O.base_pose(tt, wind=16, seed=23, sing=True)
        e = smooth(prog(tt, 81.6, 82.2))
        p.expr_full = p.expr_face = (1 - e, e, 0, 0)
        return p
    look = dict(cropBottom=1620, rimCol=(1.9, 1.3, 0.8), rimDir=(1.0, -0.2), rimW=9, tintAdd=(0.05, 0.03, 0.0))
    O.draw_char('morning', t, 80.2 - 0.5, pose, Hm2, look)
    O.fog_bottom(E.hexc('e3a27e'), 0.85, 880, 1080)
    rng = np.random.default_rng(80)
    n = 50
    xs = (rng.uniform(0, 2200, n) - lt * 90) % 2200 - 100
    ys = (rng.uniform(0, 1080, n) + lt * rng.uniform(50, 110, n)) % 1120 - 20
    cols = np.where((np.arange(n) % 3 == 0)[:, None], np.array(E.hexc('ff9a4a')), np.array(E.hexc('ffc4dd')))
    fx.parts(gl, xs, ys, rng.uniform(14, 28, n), cols, rng.uniform(0.5, 0.9, n), kind=3, angle=lt * 2 + np.arange(n))
    return dict(bloomAmt=0.75, vign=0.3)


def room(t, plate='N', tint=(0.42, 0.48, 0.78, 1.0), zoom=1.0, cx=1010, cy=1100, sx=960, sy=540):
    gl = C.gl
    fx.sky(gl, 'night')
    Hm = O.char_H(cx, cy, zoom, sx, sy)
    C.ch.plate(Hm, plate, tint=tint)
    return Hm


def f_lost(t, lt, d):
    """迷った日: 雨の夜、ストレス顔。窓に修正のコミットがにじむ"""
    gl = C.gl
    z = lerp(1.0, 1.1, ease_io(lt / d))
    Hm = room(t, 'ST', tint=(0.34, 0.38, 0.62, 1.0), zoom=z, cx=1020, cy=1080)
    rain(0.9, 1.4, (0.5, 0.6, 0.95))
    for i in range(5):
        s = C.fixes[(i * 37 + 11) % max(len(C.fixes), 1)] if C.fixes else '修正'
        a = math.sin(math.pi * clamp((lt - i * 0.5) / 1.9)) * 0.5
        if a <= 0:
            continue
        tex = gl.text(s, 28, fam='noto', weight='Medium', color=E.hexc('a9c3ff'))
        gl.sprite(tex, 150 + (i % 2) * 1150 + 20 * math.sin(lt + i), 180 + i * 150 + lt * 30, anchor=(0, 0.5), alpha=a)
    look = dict(tintMul=(0.55, 0.6, 0.88), tintAdd=(0.0, 0.02, 0.07), rimCol=(0.45, 0.65, 1.2), rimDir=(-1, 0.3), rimW=7)
    O.draw_char('lost', t, 83.4 - 0.5, sing_pose(24, expr=(0, 0, 1, 0), wind=0, head=-3.0), Hm, look)
    rain(0.35, 2.2, (0.6, 0.7, 1.0))
    thunder = pulse(t, 85.0, 0.01, 0.25) + 0.5 * pulse(t, 85.25, 0.01, 0.2)
    return dict(bloomAmt=0.5, vign=0.55, sat=0.8, lift=(0.0, 0.01, 0.05), flash=0.35 * thunder, flashCol=(0.75, 0.8, 1.0))


def f_tests(t, lt, d):
    """何度も確かめ: 実テストが次々に通る"""
    gl = C.gl
    Hm = room(t, 'N', tint=(0.40, 0.46, 0.80, 1.0), zoom=1.02, cx=1010, cy=1100, sx=1280)
    look = dict(tintMul=(0.68, 0.74, 1.0), tintAdd=(0.0, 0.03, 0.06), rimCol=(0.45, 1.1, 0.75), rimDir=(-1, 0.2), rimW=8)
    O.draw_char('tests', t, 86.6 - 0.5, sing_pose(25, wind=0, typing=1.2), Hm, look)
    # 端末のパネル
    px, py, pw, ph = 80, 120, 900, 760
    fx.rect(gl, px + pw / 2, py + ph / 2, pw, ph, (0.03, 0.05, 0.12), alpha=0.86)
    fx.rect(gl, px + pw / 2, py + 2, pw, 4, (0.35, 1.0, 0.6), alpha=0.9, blend='add')
    rate = 9.0
    n_show = int(lt * rate) + 1
    for j in range(13):
        i = n_show - 1 - j
        if i < 0:
            break
        y = py + ph - 90 - j * 50
        name = C.test_names[i % len(C.test_names)]
        if len(name) > 34:
            name = name[:33] + '…'
        a = 1.0 - j / 13
        ck = gl.text('✓', 30, fam='sym', weight=None, color=E.hexc('5dff9a'))
        gl.sprite(ck, px + 40, y, anchor=(0, 0.5), alpha=a)
        tex = gl.text(name, 26, fam='noto', weight='Medium', color=E.hexc('d6ffe6') if j == 0 else E.hexc('9fd8b8'))
        gl.sprite(tex, px + 86, y, anchor=(0, 0.5), alpha=a)
        if j == 0:
            fl = 1 - ((lt * rate) % 1.0)
            fx.parts(gl, [px + 56], [y], [90 * fl], (0.4, 1.0, 0.6), [fl], kind=1)
    k = ease_in(prog(lt, 0.0, d - 0.3), 1.6)
    cnt = int(C.n_tests * k)
    glow_text(f'{cnt:,}', px + pw - 30, py + 60, 72, fam='segoeb', weight=None, stroke=4, stroke_color=E.hexc('0b3a24'), anchor=(1, 0.5))
    glow_text('tests passed', px + pw - 30, py + 120, 28, fam='segoe', weight=None, stroke=2, stroke_color=E.hexc('0b3a24'), anchor=(1, 0.5))
    fx.rect(gl, px + 40 + (pw - 80) * k / 2, py + ph - 30, (pw - 80) * k, 10, (0.35, 1.0, 0.6), alpha=0.9, blend='add')
    return dict(bloomAmt=0.6, vign=0.45, lift=(0.0, 0.02, 0.05))


def f_days(t, lt, d):
    """毎日前へ: 日ごとのコミットの表（実データ）の上をミニキャラが走る"""
    gl = C.gl
    fx.sky(gl, 'night', t=t, cam=(0, 0.1), sun=(0.12, 0.14))
    fx.grad(gl, 960, 900, 2200, 500, (0.5, 0.25, 0.6, 0.0), (0.7, 0.35, 0.65, 0.45), blend='add')
    weeks, rows = C.heat_dims
    rev = ease_io(prog(lt, 0.1, d - 0.2), 1.4)
    cell = 0.32
    W, Hh = weeks * cell, rows * cell
    xf = -W / 2 + W * rev
    cam = E.Cam3(eye=(xf - 1.4, 1.1, -6.4), target=(xf + 1.2, 0.0, 0), fov=46, roll=-0.03)
    # カメラの後ろに回る部分で板が消えないよう、4週ずつに分けて描く
    for w0 in range(0, weeks, 4):
        w1 = min(w0 + 4, weeks)
        cxw = -W / 2 + (w0 + w1) / 2 * cell
        H_, z = cam.quad_h(np.array([cxw, 0.0, 0.0]), np.array([1.0, 0, 0]), np.array([0, -1.0, 0]), (w1 - w0) * cell, Hh)
        if H_ is not None:
            gl.draw(GRID_FS, H_, dict(dims=(weeks, rows), reveal=rev, alpha=1.0, u0=w0 / weeks, u1=w1 / weeks), dict(dat=C.heat))
    # 先頭の光
    p, zz = cam.project(np.array([[xf, 0.0, 0.0]]))
    fx.parts(gl, [p[0, 0]], [p[0, 1]], [900 / zz[0]], (1.0, 0.7, 1.0), [0.45], kind=0)
    # ミニキャラ: 表の上端を走る
    top, zt = cam.project(np.array([[xf + 0.15, Hh / 2 + 0.02, 0.0]]))
    sc = 5.5 / zt[0]
    pet('run', t, top[0, 0], top[0, 1], scale=sc, fps=14)
    rng = np.random.default_rng(int(t * 30))
    fx.parts(gl, top[0, 0] - 60 * sc - rng.uniform(0, 80, 8) * sc, top[0, 1] - rng.uniform(0, 30, 8), rng.uniform(8, 20, 8), (1.0, 0.8, 1.0),
             rng.uniform(0.3, 0.8, 8), kind=1)
    wi = min(int(rev * weeks), weeks - 1)
    glow_text(C.heat_weeks[wi].replace('-', '.'), 1830, 120, 64, fam='segoeb', weight=None, stroke=4, stroke_color=E.hexc('2a1458'), anchor=(1, 0.5))
    glow_text(f'{C.n_days} days  ·  {len(C.clog):,} commits', 1830, 190, 30, fam='segoe', weight=None, stroke=2,
              stroke_color=E.hexc('2a1458'), anchor=(1, 0.5), alpha=prog(lt, 0.4, 0.9))
    return dict(bloomAmt=0.7, vign=0.35)


def f_shine(t, lt, d):
    """今 輝くとき: 魔法陣の光の柱で昇り、最後の小節は拍ごとのカット"""
    gl = C.gl
    if lt < 1.6:
        fx.sky(gl, 'night', t=t)
        cam = E.Cam3(eye=(0, 2.4, -7.0), target=(0, 0.6, 0), fov=48)
        R3 = E.rot_y(lt * 0.6)
        H_, z = cam.quad_h((0, -0.6, 0), R3 @ np.array([1, 0, 0.0]), R3 @ np.array([0, 0, -1.0]), 6.5, 6.5)
        C.mg.draw(H_, prog=1.0, spin=lt * 0.4, glow=1.6 + lt)
        fx.grad(gl, 960, 540, 520 + 200 * lt, 1080, (0.7, 0.5, 1.0, 0.0), (0.9, 0.7, 1.0, 0.5), vertical=False, blend='add')
        fx.parts(gl, [960], [700], [1200], (0.7, 0.5, 1.0), [0.4], kind=0)

        def pose(tt):
            p = O.base_pose(tt, wind=-30, seed=26, sing=True)
            e = smooth(prog(tt, 93.8, 94.3))
            p.expr_full = p.expr_face = (1 - e, e, 0, 0)
            return p
        Hm = O.char_H(1050, 980, 0.7, sx=960, sy=lerp(760, 580, ease_out(prog(lt, 0, 1.6), 2)))
        O.draw_char('shine', t, 93.0 - 0.5, pose, Hm, dict(cropBottom=1600, rimCol=(1.2, 1.0, 1.6), rimDir=(0, 1), rimW=9))
        O.fog_bottom((0.25, 0.15, 0.45), 0.9, 900, 1080)
        rng = np.random.default_rng(93)
        n = 200
        a = rng.uniform(0, 2 * math.pi, n) + lt * 2
        h = (rng.uniform(0, 1, n) + lt * 0.8) % 1.0
        fx.parts(gl, 960 + np.cos(a) * 300 * (1 - h * 0.5), 1050 - h * 1000, rng.uniform(6, 16, n), (0.9, 0.8, 1.0), (1 - h), kind=1)
        return dict(bloomAmt=0.9, vign=0.35)
    i = int((lt - 1.6) / 0.4)
    k = ((lt - 1.6) % 0.4) / 0.4
    pose = lambda tt: O.base_pose(tt, wind=30, seed=27, expr=(0, 1, 0, 0), blink=0.0)
    if i == 0:      # 瞳
        fx.burst(gl, 0.5, 0.5, rot=t, n=24, c1=E.hexc('4a2a9a'), c2=E.hexc('5b36b0'), c3=E.hexc('b08cff'))
        Hm = O.char_H(1180 - 20 * k, 860, 2.6 + 0.2 * k)
        O.draw_char('shine2', t, 94.6 - 0.5, pose, Hm)
        fx.parts(gl, [1060], [700], [260 * (0.6 + pulse(k, 0.4, 0.02, 0.4))], (1, 1, 1), [1.0], kind=1)
    elif i == 1:    # ミニキャラが跳ぶ
        fx.burst(gl, 0.5, 0.6, rot=-t, n=20, c1=E.hexc('ffd84d'), c2=E.hexc('ffe98a'), c3=(1, 1, 0.92))
        pet('jump', t, 960, 900 - 160 * math.sin(math.pi * k), scale=2.6, fps=12)
    elif i == 2:    # 実画面
        fx.rect(gl, 960, 540, 1920, 1080, E.hexc('0b0f2a'))
        for j in range(4):
            s = C.screens[(j * 3 + 5) % len(C.screens)]
            gl.sprite(s, 240 + j * 480 - k * 60, 540 + (j % 2) * 70 - 35, scale=440 / s.size[0], alpha=0.92)
        fx.focus_lines(gl, mode=1, seed=int(t * 20), amount=0.8)
    else:           # 寄って白へ
        fx.burst(gl, 0.5, 0.5, rot=t, n=30, c1=E.hexc('2a1060'), c2=E.hexc('3c1a80'), c3=E.hexc('b07cff'))
        z = lerp(1.0, 2.8, ease_in(k, 3))
        Hm = O.char_H(lerp(1050, 1170, ease_in(k, 3)), lerp(980, 860, ease_in(k, 3)), z, sy=560)
        O.draw_char('shine3', t, 95.8 - 0.5, pose, Hm, dict(cropBottom=1700))
        fx.focus_lines(gl, inner=0.3, seed=int(t * 15), amount=0.9)
    wh = smooth(prog(lt, d - 0.25, d))
    return dict(bloomAmt=0.6, vign=0.3, flash=max(wh, pulse(t, 94.6 + i * 0.4, 0.01, 0.08) * 0.6))


def f_title2(t, lt, d):
    gl = C.gl
    fx.burst(gl, 0.5, 0.48, rot=-t * 0.3, n=28, c1=E.hexc('3fa9f5'), c2=E.hexc('7cc8ff'), c3=E.hexc('eaf6ff'))
    bump = 18 * pulse(t, 96.2, 0.01, 0.2)
    Hm = O.char_H(1050, 960, 0.82 + 0.03 * math.sin(lt * 2), sx=960, sy=700 + bump)
    O.draw_char('t2', t, 96.2 - 0.5, sing_pose(28, expr=(0, 1, 0, 0), wind=18), Hm, dict(outlineW=10.0, outlineCol=(1, 1, 1), cropBottom=1600))
    rng = np.random.default_rng(96)
    n = 140
    cols = np.array([E.hexc(c) for c in ('ffd84d', 'ff7ad9', '7cc8ff', 'b08cff', 'ffffff')])[np.arange(n) % 5]
    fx.parts(gl, (rng.uniform(0, 1920, n) + np.sin(lt * 2 + np.arange(n)) * 40), (rng.uniform(-200, 1080, n) + lt * rng.uniform(120, 260, n)) % 1280 - 100,
             rng.uniform(14, 26, n), cols, 0.9, kind=3, angle=lt * 4 + np.arange(n), blend='over')
    O.fog_bottom(E.hexc('a9d8ff'), 0.95, 900, 1080)
    drop_letters(t, line_at(96.4, 'Creative'), mode='zoom', stroke_color=E.hexc('0d3c8c'))
    return dict(bloomAmt=0.45, vign=0.15, **O.flash(t, 96.2, 0.4))


def f_voices(t, lt, d):
    """声を合わせ ひとつに: 4つの声の光が回って1つの輪になる。輪は歌の音量で脈打つ"""
    gl = C.gl
    O.sky_layer(t, 'dawn', cloud_y=1060, n=6, seed=31, scroll=12)
    cx, cy = 960, 470
    merge = smooth(prog(lt, 1.2, 2.6))
    cols = [E.hexc('ff7ad9'), E.hexc('ff6b6b'), E.hexc('7cc8ff'), E.hexc('ffd84d')]

    def ribbons(front):
        for v in range(4):
            n = 70
            q = np.arange(n) / n
            a = lt * 2.4 + v * math.pi / 2 - q * 1.4
            r = lerp(640 + 60 * math.sin(lt * 1.3 + v), 600, merge)
            tilt = lerp(0.42 + 0.08 * v, 0.5, merge)
            xs = cx + np.cos(a) * r; ys = cy + 170 + np.sin(a) * r * tilt + lerp(80 * (v - 1.5), 0, merge)
            col = np.array(cols[v]) * (1 - merge) + np.array((1.0, 0.92, 0.75)) * merge
            m = (np.sin(a) > 0) == front
            fx.parts(gl, xs[m], ys[m], (44 * (1 - q) + 8)[m], col, ((1 - q) * 0.85)[m], kind=0)
            fx.parts(gl, xs[m][::3], ys[m][::3], (40 * (1 - q[m][::3]) + 10), (1, 1, 1), ((1 - q[m][::3]) * 0.8), kind=1)
            if m[0]:
                fx.parts(gl, xs[:1], ys[:1], [90], col, [1.0], kind=1)
    ribbons(False)
    Hm = O.char_H(1050, 960, 0.66, sx=960, sy=640)
    O.draw_char('voices', t, 99.4 - 0.5, sing_pose(29, expr=(0, 1, 0, 0), wind=20), Hm, dict(cropBottom=1600, outlineW=5.0, outlineCol=(1, 0.95, 1.0), outlineA=0.7))
    # 歌の音量で脈打つ輪（前半分はキャラより手前）
    lv = C.vocal.level(t)
    m = 120
    a = np.linspace(0, 2 * math.pi, m, endpoint=False)
    hist = np.array([C.vocal.level(t - 0.01 * j) for j in range(m // 2)])
    amp = np.concatenate([hist, hist[::-1]])
    rr = 600 + 60 * amp * merge
    xs, ys = cx + np.cos(a) * rr, cy + 170 + np.sin(a) * rr * 0.5
    front = np.sin(a) > 0
    fx.parts(gl, xs[front], ys[front], 16 + 14 * amp[front], (1.0, 0.92, 0.75), (0.4 + 0.6 * amp[front]) * merge, kind=0)
    O.fog_bottom(E.hexc('b9789e'), 0.9, 880, 1080)
    ribbons(True)
    return dict(bloomAmt=0.75 + 0.3 * lv * merge, vign=0.2)


def f_tears(t, lt, d):
    """涙もぜんぶ 光になる: ストレス顔の涙が光になり、雨が光の粒になって昇る"""
    gl = C.gl
    turn = smooth(prog(lt, 1.3, 2.0))
    warm = smooth(prog(lt, 1.4, 2.6))
    fx.sky(gl, 'night', t=t)
    fx.grad(gl, 960, 540, 1920, 1080, (1.0, 0.6, 0.45, 0.0), (1.0, 0.7, 0.5, 0.9 * warm), blend='add')
    rain(0.7 * (1 - warm), 1.5, (0.5, 0.6, 0.95))
    if warm > 0:
        rng = np.random.default_rng(102)
        n = 260
        xs = rng.uniform(0, 1920, n)
        ys = 1100 - ((lt - 1.4) * rng.uniform(150, 420, n) + rng.uniform(0, 900, n) * 0.2) % 1250
        fx.parts(gl, xs, ys, rng.uniform(6, 18, n), (1.0, 0.85, 0.6), rng.uniform(0.3, 0.9, n) * warm, kind=1)
    z = math.exp(lerp(math.log(1.55), math.log(0.8), ease_io(prog(lt, 1.2, 3.0))))
    Hm = O.char_H(lerp(1080, 1050, turn), lerp(900, 960, turn), z, sx=960, sy=lerp(560, 620, turn))

    def pose(tt):
        p = O.base_pose(tt, wind=10, seed=30, sing=True, head=-2.0 * (1 - smooth(prog(tt, 103.9, 104.6))))
        e = smooth(prog(tt, 103.9, 104.6))
        p.expr_full = p.expr_face = (0, e, 1 - e, 0)
        return p
    look = dict(cropBottom=1620, tintMul=(lerp(0.7, 1.0, warm), lerp(0.75, 0.96, warm), lerp(1.0, 0.92, warm)),
                rimCol=(lerp(0.5, 1.8, warm), lerp(0.7, 1.2, warm), lerp(1.2, 0.7, warm)), rimDir=(-1, 0.3), rimW=8)
    O.draw_char('tears', t, 102.6 - 0.5, pose, Hm, look)
    O.fog_bottom((lerp(0.15, 0.9, warm), lerp(0.18, 0.6, warm), lerp(0.4, 0.45, warm)), 0.9, 900, 1080)
    # 涙: 右目の下から落ち、光に変わって昇る
    ex, ey = rig_pt(Hm, 1190, 935)
    k1 = prog(lt, 0.2, 1.3)
    if 0 < k1 < 1:
        fx.parts(gl, [ex - 6 * k1], [ey + 180 * ease_in(k1, 2)], [26], (0.75, 0.85, 1.0), [0.9], kind=0)
        fx.parts(gl, [ex - 6 * k1], [ey + 180 * ease_in(k1, 2)], [50], (1, 1, 1), [0.6], kind=1)
    k2 = prog(lt, 1.3, 3.0)
    if 0 < k2 < 1:
        x = ex - 6 + 120 * math.sin(k2 * 3)
        y = ey + 180 - 900 * ease_in(k2, 1.5)
        fx.parts(gl, [x], [y], [140 * (1 - k2) + 40], (1.0, 0.9, 0.6), [1.0], kind=1)
        fx.parts(gl, [x], [y], [400], (1.0, 0.8, 0.5), [0.35], kind=0)
    return dict(bloomAmt=0.6 + 0.4 * warm, vign=0.4 - 0.15 * warm, sat=lerp(0.8, 1.05, warm))


def f_ikou(t, lt, d, t0, c1, c2, c3, lines_col, text_col, side=1, pets=False, shells=None):
    gl = C.gl
    fx.burst(gl, 0.5, 0.55, rot=t * 0.6 * side, n=20, c1=c1, c2=c2, c3=c3)
    z = lerp(1.3, 0.95, ease_out(prog(lt, 0, 0.35), 4))
    Hm = O.char_H(1040, 980, z, sx=960, sy=600)
    O.draw_char(f'ikou{int(t0)}', t, t0 - 0.5, O.ikou_pose, Hm, dict(outlineW=12.0, outlineCol=(1, 1, 1), cropBottom=1600))
    fx.focus_lines(gl, inner=0.42, seed=int(t * 15), amount=0.9, col=lines_col)
    if shells:
        fireworks(t, shells)
    if pets:
        for j, x in enumerate((260, 520, 1400, 1660)):
            ph = j * 0.23
            cyc = ((lt + ph) * 1.6) % 1.0
            pet('jump', t, x, 1040 - 150 * math.sin(math.pi * cyc), scale=1.3, fps=10, phase=j * 2, flip=x > 960)
    k = ease_out_back(prog(lt, 0.05, 0.35), 2.5)
    tex = gl.text('いこう！', 170, fam='noto', weight='Black', color=(1, 1, 1), stroke=12, stroke_color=text_col)
    gl.sprite(tex, 960 + 470 * side, 300, scale=k, angle=-0.12 * side)
    inv = 1.0 if 0.0 <= lt < 0.067 else 0.0
    return dict(bloomAmt=0.3, vign=0.1, invert=inv, mono=inv)


def f_ikou2(t, lt, d):
    return f_ikou(t, lt, d, 105.8, E.hexc('3fa9f5'), E.hexc('7cc8ff'), (0.92, 0.97, 1.0), E.hexc('0091ea'), E.hexc('1565c0'), side=-1, pets=True)


def f_ikou3(t, lt, d):
    shells = [(137.9, 380, 300, (1.0, 0.8, 0.35), 260), (138.3, 1560, 260, (1.0, 0.45, 0.7), 240), (138.7, 960, 180, (0.6, 0.85, 1.0), 280)]
    return f_ikou(t, lt, d, 137.8, E.hexc('ffb300'), E.hexc('ffd54f'), (1, 0.98, 0.9), E.hexc('ff6d00'), E.hexc('bf360c'), side=1, pets=True, shells=shells)


def f_fuji_dawn(t, lt, d):
    """ほら 夜明けの空 晴れた: 富士山の夜が明け、赤富士と湖面の光の道"""
    gl = C.gl
    k = smooth(prog(lt, 0.4, 4.2))
    cz = ease_io(prog(lt, 0.0, d), 2)
    Hm = img_H(C.fuji_wh, lerp(1200, 1330, cz), lerp(900, 880, cz), lerp(0.82, 0.98, cz))
    sun = (0.80, lerp(0.67, 0.53, smooth(prog(lt, 1.2, d))))
    fuji(Hm, dawn=k, twinkle=1.0 - k, sun=sun)
    sx, sy = fuji_pt(Hm, *sun)
    up = smooth(prog(lt, 2.0, d))
    if up > 0:
        fx.god_rays(gl, sx / 1920, sy / 1080, amount=0.7 * up, decay=0.972, thr=0.95, focus=2.5)
        # レンズの光（太陽→画面中心の反対側へ）
        for j, (q, s, c) in enumerate([(0.4, 90, (1.0, 0.7, 0.5)), (0.75, 50, (0.6, 0.8, 1.0)), (1.25, 140, (1.0, 0.5, 0.7))]):
            fx.parts(gl, [sx + (960 - sx) * q * 2], [sy + (540 - sy) * q * 2], [s], c, [0.22 * up], kind=5)
    # 主人公が下から昇ってくる
    a = ease_out(prog(lt, 1.0, 2.6), 3)
    if a > 0:
        Hm2 = O.char_H(1050, 980, 0.5, sx=430, sy=lerp(1300, 650, a))
        look = dict(cropBottom=1620, rimCol=(2.0, 1.3, 0.8), rimDir=(1.0, 0.0), rimW=8, tintMul=(lerp(0.6, 1.0, k), lerp(0.62, 0.95, k), lerp(0.9, 0.95, k)),
                    tintAdd=(0.05 * k, 0.02 * k, 0.0))
        O.draw_char('fdawn', t, 108.4 - 0.5, sing_pose(32, expr=(0, 1, 0, 0), wind=-20), Hm2, look)
        O.fog_bottom(tuple(np.array((0.25, 0.25, 0.45)) * (1 - k) + np.array((0.95, 0.7, 0.6)) * k), 0.85 * a, 920, 1080)
    return dict(bloomAmt=0.55, vign=0.3)


def f_run(t, lt, d):
    """ためらわず走ろう: ミニキャラが四季の里を駆け抜ける（春→夏→秋→冬）"""
    gl = C.gl
    k = ease_io(prog(lt, 0.0, d), 1.3)
    z = 1.12
    cx = lerp(850, 1940, k)
    Hm = img_H(C.seasons_wh, cx, 700 + 10 * math.sin(lt * 9), z)
    seasons(Hm)
    fx.focus_lines(gl, inner=0.55, seed=int(t * 15), amount=0.35, mode=1)
    rng = np.random.default_rng(112)
    n = 70
    xs = (rng.uniform(0, 2200, n) - lt * 900) % 2200 - 140
    ys = (rng.uniform(0, 1080, n) + lt * rng.uniform(80, 160, n)) % 1120 - 20
    season = cx / C.seasons_wh[0]
    if season < 0.42:
        fx.parts(gl, xs, ys, rng.uniform(14, 28, n), E.hexc('ffc4dd'), 0.85, kind=3, angle=lt * 3 + np.arange(n))
    elif season < 0.62:
        fx.parts(gl, xs, ys, rng.uniform(6, 12, n), (1.0, 1.0, 0.7), 0.5 * (np.sin(lt * 7 + np.arange(n)) > 0), kind=0)
    elif season < 0.74:
        fx.parts(gl, xs, ys, rng.uniform(16, 30, n), E.hexc('ff8a3d'), 0.9, kind=3, angle=lt * 3 + np.arange(n))
    else:
        fx.parts(gl, xs, ys, rng.uniform(8, 18, n), (1, 1, 1), 0.85, kind=2)
    bx, by = 760 + 40 * math.sin(lt * 1.5), 930
    pet('run', t, bx, by, scale=2.0, fps=15)
    rng = np.random.default_rng(int(t * 30))
    fx.parts(gl, bx - 120 - rng.uniform(0, 140, 10), by - rng.uniform(0, 40, 10), rng.uniform(10, 26, 10), (1.0, 0.95, 0.85), rng.uniform(0.2, 0.5, 10), kind=0)
    return dict(bloomAmt=0.45, vign=0.2, ca=1.5)


def f_draw(t, lt, d):
    """描いた夢が 未来になる: 実画面を光の線で描き、色が付き、ロゴに集まる"""
    gl = C.gl
    fx.sky(gl, 'dawn', t=t, cam=(0, 0.15))
    cam = E.Cam3(eye=(0.6 * math.sin(lt * 0.3), 0.3, -9.0), target=(0.6, 0.3, 0), fov=44)
    gather = ease_in(prog(lt, 3.6, 4.6), 2)
    quads = []
    for j in range(6):
        a = lerp(-0.55, 0.75, j / 5)
        row = j % 2
        c = np.array([2.0 + math.sin(a) * 3.0, 1.3 - row * 2.2 + 0.25 * math.sin(lt + j), 1.0 - math.cos(a) * 1.2])
        c = c * (1 - gather) + np.array([0.6, 0.3, 0.0]) * gather
        right = np.array([math.cos(a * 0.5), 0, math.sin(a * 0.5)])
        quads.append((c, right, j))
    quads.sort(key=lambda r: -cam.to_cam(r[0])[2])
    for c, right, j in quads:
        tex = C.screens[(j * 4 + 2) % len(C.screens)]
        w, h = tex.size
        ww = 2.6 * (1 - 0.7 * gather)
        H_, z = cam.quad_h(c, right, np.array([0, -1.0, 0]), ww, ww * h / w)
        if H_ is None:
            continue
        lk = lerp(-0.05, 1.1, prog(lt, 0.1 + j * 0.25, 1.4 + j * 0.25))
        ck = lerp(-0.12, 1.15, prog(lt, 1.0 + j * 0.25, 2.4 + j * 0.25))
        gl.draw(DRAW_FS, H_, dict(lineK=lk, colK=ck, alpha=1.0 - gather, lineCol=(0.55, 0.85, 1.2), texel=(1 / w, 1 / h)), dict(tex=tex))
        if 0 < lk < 1.05:
            # 描く筆先
            u = clamp(lk / 0.72); v = 0.5 + 0.45 * math.sin(lt * 9 + j)
            pc = c + right * (u - 0.5) * ww + np.array([0, -1.0, 0]) * (v - 0.5) * ww * h / w
            pp, zz = cam.project(pc[None])
            fx.parts(gl, [pp[0, 0]], [pp[0, 1]], [90], (0.8, 0.95, 1.0), [1.0], kind=1)
    # ロゴに集まる
    lg = smooth(prog(lt, 4.2, 4.9))
    if lg > 0:
        p, zz = cam.project(np.array([[0.6, 0.3, 0.0]]))
        fx.parts(gl, [p[0, 0]], [p[0, 1]], [1600 * lg], (0.8, 0.7, 1.0), [0.5 * lg], kind=0)
        sc = 0.55 * (0.7 + 0.3 * ease_out_back(lg))
        gl.sprite(C.logo, p[0, 0], p[0, 1] - 40, scale=sc * 1.06, alpha=0.8 * lg, tint=(0, 0, 0), add=(1.6, 1.0, 2.2), blend='add')
        gl.sprite(C.logo, p[0, 0], p[0, 1] - 40, scale=sc, alpha=lg, tint=(0.35, 0.3, 0.45), add=(0.75, 0.72, 0.9))
    Hm = O.char_H(1050, 980, 0.62, sx=420, sy=650)
    O.draw_char('draw', t, 115.4 - 0.5, sing_pose(33, expr=(0, 1, 0, 0), wind=14), Hm, dict(cropBottom=1600, outlineW=5.0, outlineCol=(1, 1, 1), outlineA=0.8))
    O.fog_bottom(E.hexc('8a5a9a'), 0.9, 900, 1080)
    # 描いた夢が星になって散る（ブリッジの銀河へ）
    end = smooth(prog(lt, d - 1.6, d))
    if end > 0:
        fx.rect(gl, 960, 540, 1920, 1080, E.hexc('070a1e'), alpha=end)
        rng = np.random.default_rng(118)
        n = 400
        xs = rng.uniform(0, 1920, n); ys = rng.uniform(0, 1080, n)
        fx.parts(gl, xs, ys - end * 120 * rng.uniform(0.5, 1, n), rng.uniform(3, 9, n), (0.8, 0.85, 1.0), rng.uniform(0.3, 1.0, n) * end, kind=0)
    return dict(bloomAmt=0.7, vign=0.3)


def f_galaxy(t, lt, d):
    """ブリッジ: 全コミットの銀河。中心が最初のコミット、外ほど新しい。AI と共同のコミットは桃色"""
    gl = C.gl
    fx.rect(gl, 960, 540, 1920, 1080, E.hexc('050716'))
    rng = np.random.default_rng(121)
    n = 300
    fx.parts(gl, rng.uniform(0, 1920, n), rng.uniform(0, 1080, n), rng.uniform(2, 5, n), (0.8, 0.85, 1.0), rng.uniform(0.2, 0.7, n), kind=0)
    N = len(C.gal)
    grow = ease_out(prog(lt, 0.0, 3.6), 2)
    col_k = smooth(prog(lt, 1.0, 2.0))
    fall = ease_in(prog(lt, d - 1.2, d - 0.1), 3)
    ang = lt * 0.18
    dist = lerp(15.0, 7.2, ease_io(prog(lt, 0.0, d - 1.0)))
    elev = lerp(1.3, 0.38, ease_io(prog(lt, 0.0, d - 1.0)))
    eye = np.array([math.sin(ang) * math.cos(elev), math.sin(elev), -math.cos(ang) * math.cos(elev)]) * dist
    cam = E.Cam3(eye=tuple(eye), target=(0, 0, 0), fov=50, roll=0.1 * math.sin(lt * 0.4))
    shown = int(N * grow)
    P = C.gal[:max(shown, 1)] * (1 - fall)
    D = C.gal_dust[:max(shown, 1)] * (1 - fall)
    scr, zz = cam.project(D)
    ok = zz > 0.3
    fx.parts(gl, scr[ok, 0], scr[ok, 1], 140 / zz[ok], (0.45, 0.4, 0.9), np.full(ok.sum(), 0.05), kind=0)
    scr, zz = cam.project(P)
    ok = zz > 0.3
    ai = C.gal_ai[:len(P)]
    cols = np.where(ai[:, None], np.array((1.0, 0.45, 0.85)) * col_k + np.array((0.8, 0.85, 1.0)) * (1 - col_k), np.array((0.7, 0.85, 1.0)))
    fresh = np.exp(-(shown - 1 - np.arange(len(P))) / 25.0)
    fx.parts(gl, scr[ok, 0], scr[ok, 1], (28 + 50 * fresh[ok]) / zz[ok] * 3, cols[ok], (0.55 + 0.45 * fresh[ok]), kind=0)
    # 中心（最初のコミット）
    p0, z0 = cam.project(np.zeros((1, 3)))
    fx.parts(gl, [p0[0, 0]], [p0[0, 1]], [900 / z0[0] * (1 + 3 * fall)], (1.0, 0.85, 0.95), [0.6 + 0.4 * fall], kind=0)
    # 今いちばん外のコミットの日付と件名
    lq = math.floor(lt / 0.3) * 0.3
    jq = int(N * ease_out(prog(lq, 0.0, 3.6), 2)) - 1
    if 0 <= jq < N - 1 and fall <= 0:
        j = jq
        dte, subj, a = C.clog[j]
        pj, zj = cam.project(C.gal[j:j + 1])
        pp = pj[0] if zj[0] > 0.3 else None
        if pp is not None:
            glow_text(f'{dte}  {subj[:22]}', pp[0] + 20, pp[1] - 30, 24, fam='noto', weight='Bold', stroke=2,
                      stroke_color=E.hexc('1b1a4a'), anchor=(0, 0.5), alpha=0.85)
    glow_text(f'{shown:,}', 1830, 820, 120, fam='segoeb', weight=None, stroke=4, stroke_color=E.hexc('1b2a6e'), anchor=(1, 0.5), alpha=1 - fall)
    glow_text(f'commits   うち AI と共同 {int(C.n_ai * grow * col_k):,}', 1830, 900, 32, fam='noto', weight='Bold', stroke=3,
              stroke_color=E.hexc('1b2a6e'), anchor=(1, 0.5), alpha=(1 - fall) * prog(lt, 1.0, 1.5), color=(1.0, 0.85, 0.95))
    return dict(bloomAmt=0.9, bloomThr=0.9, vign=0.4, flash=smooth(prog(lt, d - 0.2, d)), flashCol=(1, 0.95, 1.0))


def f_title3(t, lt, d):
    """ラストサビ: 富士の湖に花火。金のタイトル"""
    gl = C.gl
    Hm = img_H(C.fuji_wh, 1200, 870, 0.84 + 0.02 * lt / d)
    fuji(Hm, twinkle=0.6)
    shells = []
    rng = np.random.default_rng(128)
    for k in range(9):
        t0 = 128.2 + k * 0.4 + 0.2
        shells.append((t0, rng.uniform(250, 1670), rng.uniform(140, 420), tuple(rng.choice([(1.0, 0.8, 0.35), (1.0, 0.45, 0.7), (0.6, 0.85, 1.0), (0.8, 1.0, 0.6)])), rng.uniform(200, 300)))
    fireworks(t, shells)
    # 湖面に映る花火の光
    fl = sum(pulse(t, s[0], 0.02, 0.8) for s in shells)
    fx.grad(gl, 960, 990, 1920, 180, (1.0, 0.7, 0.5, 0.0), (1.0, 0.7, 0.6, 0.25 * min(fl, 1.5)), blend='add')
    O.lanterns(t, 60, 0, alpha=0.6, seed=129)
    bump = 18 * pulse(t, 128.2, 0.01, 0.2)
    Hm2 = O.char_H(1050, 960, 0.56, sx=960, sy=740 + bump)
    O.draw_char('t3', t, 128.2 - 0.5, sing_pose(34, expr=(0, 1, 0, 0), wind=18), Hm2, dict(outlineW=10.0, outlineCol=(1, 0.95, 0.8), cropBottom=1600,
                                                                                          rimCol=(1.4, 1.0, 0.6), rimDir=(0, -1), rimW=6))
    O.fog_bottom(E.hexc('3a2a5a'), 0.95, 900, 1080)
    drop_letters(t, line_at(128.4, 'Creative'), mode='drop', color=E.hexc('fff1b8'), stroke_color=E.hexc('a86b00'))
    return dict(bloomAmt=0.8, vign=0.3, **O.flash(t, 128.2, 0.5, (1.0, 0.95, 0.8)))


def f_paint(t, lt, d):
    """今こそ 夢を描け: 線画の四季の里に、筆の通ったところから色が付く"""
    gl = C.gl
    k = ease_io(prog(lt, 0.0, d), 1.6)
    z = 0.95
    cx = lerp(1000, 1752, k)
    Hm = img_H(C.seasons_wh, cx, 620, z)
    # 画面上の筆先の位置を画像の uv にする
    px = lerp(-150, 2070, ease_io(prog(lt, 0.1, d - 0.2), 1.3))
    u = (cx + (px - 960) / z) / C.seasons_wh[0]
    seasons(Hm, paint=u)
    by = 540 + 330 * math.sin(lt * 4.2)
    n = 40
    q = np.arange(n) / n
    tx = px - q * 260; ty = 540 + 330 * np.sin((lt - q * 0.18) * 4.2)
    hue = (q * 0.8 + lt * 0.3) % 1.0
    cols = np.stack([0.6 + 0.4 * np.cos(6.283 * hue), 0.6 + 0.4 * np.cos(6.283 * (hue - 0.33)), 0.6 + 0.4 * np.cos(6.283 * (hue - 0.67))], -1)
    fx.parts(gl, tx, ty, 50 * (1 - q) + 10, cols, (1 - q) * 0.9, kind=0)
    fx.parts(gl, [px], [by], [160], (1, 1, 1), [1.0], kind=1)
    pet('jump', t, px + 30, by - 40, scale=0.9, fps=10)
    return dict(bloomAmt=0.55, vign=0.25)


def f_smiles(t, lt, d):
    """みんなの笑顔 あふれ出すよ"""
    gl = C.gl
    O.sky_layer(t, 'sunrise', cloud_y=880, n=6, seed=35, scroll=12)
    fx.god_rays(gl, 0.5, 0.75, amount=0.8)
    O.lanterns(t, 140, 0, alpha=0.7, seed=135)
    Hm = O.char_H(1050, 960, 0.66, sx=960, sy=560)
    O.draw_char('smiles', t, 134.6 - 0.5, sing_pose(35, expr=(0, 1, 0, 0), wind=22), Hm, dict(cropBottom=1600, outlineW=6.0, outlineCol=(1, 1, 1)))
    O.fog_bottom(E.hexc('e7a0a8'), 0.9, 760, 1000)
    chibi_crowd(t, 24, 870, 1120, seed=136)
    rng = np.random.default_rng(137)
    n = 120
    burst_k = prog(lt, 0.0, d)
    a = rng.uniform(0, 2 * math.pi, n)
    r = (rng.uniform(0, 1, n) + lt * 0.5) % 1.0
    fx.parts(gl, 960 + np.cos(a) * r * 1100, 520 + np.sin(a) * r * 650, rng.uniform(12, 26, n), E.hexc('ffc4dd'), (1 - r) * 0.9, kind=3, angle=a + lt)
    fx.parts(gl, 960 + np.cos(a + 1) * r * 900, 520 + np.sin(a + 1) * r * 500, rng.uniform(10, 30, n), (1, 0.95, 0.8), (1 - r) * 0.8, kind=1)
    return dict(bloomAmt=0.6, vign=0.2)


def f_gate(t, lt, d):
    """op の3階層の扉を引き延ばす。みんな（灯籠）が扉へ集まる"""
    gl = C.gl
    s = 4.8 / d
    post = O.s_gate(t, lt * s, 4.8)
    k = smooth(prog(lt, 4.6, d))
    if k > 0:
        rng = np.random.default_rng(140)
        n = 160
        q = (rng.uniform(0, 1, n) + lt * 0.35) % 1.0
        x0 = rng.uniform(-100, 2020, n)
        xs = lerp(x0, 960, q ** 1.5)
        ys = lerp(1150, 380, q)
        fx.parts(gl, xs, ys, rng.uniform(10, 26, n) * (1 - 0.5 * q), E.hexc('ffb35c'), (0.4 + 0.6 * q) * k, kind=4)
        fx.parts(gl, xs, ys, rng.uniform(40, 90, n), (1.0, 0.55, 0.2), 0.1 * k, kind=0)
        chibi_crowd(t, 10, 980, 1100, seed=141, states=('run',), alpha=k, xr=(100, 1820))
    return post


def f_finale(t, lt, d):
    """目指すはひとつ あの未来へ: 夜明けの富士を背に、瞳の寄りから全景へ"""
    gl = C.gl
    pull = ease_io(prog(lt, 0.6, 5.6), 3)
    Hm_bg = img_H(C.fuji_wh, lerp(1150, 1120, pull), lerp(760, 840, pull), lerp(1.25, 0.86, pull))   # 画面の左端まで覆う範囲で富士を右へ
    fuji(Hm_bg, dawn=1.0, twinkle=0.0, sun=(0.80, 0.50), lod=lerp(2.0, 0.4, pull))
    sx, sy = fuji_pt(Hm_bg, 0.80, 0.50)
    fx.god_rays(gl, sx / 1920, sy / 1080, amount=0.6, decay=0.972, thr=0.95, focus=2.5)
    O.lanterns(t, 80, 0, alpha=0.5 * pull, seed=146)
    z = math.exp(lerp(math.log(1.9), math.log(0.42), pull))
    cx = lerp(1150, 1050, pull); cy = lerp(880, 1000, pull)
    csx = lerp(980, 640, pull); csy = lerp(560, 560, pull)
    Hm = O.char_H(cx, cy, z, sx=csx, sy=csy, rot=lerp(0.06, 0.0, pull))

    def pose(tt):
        p = O.base_pose(tt, wind=22 + 6 * math.sin(tt), seed=36, sing=True, expr=(0, 1, 0, 0))
        hold = smooth(prog(tt, 150.6, 150.9)) * (1 - smooth(prog(tt, 152.3, 152.6)))
        if hold > 0:
            p.blink = max(p.blink, hold)
        return p
    look = dict(cropBottom=1650, rimCol=(1.9, 1.3, 0.8), rimDir=(1.0, -0.1), rimW=8, tintAdd=(0.06, 0.03, 0.0),
                outlineW=4.0 * pull, outlineCol=(1, 0.95, 0.9), outlineA=0.6)
    O.draw_char('finale', t, 146.6 - 0.5, pose, Hm, look)
    # 朝霧の雲が下半身を覆う
    pal = clouds.PALETTES['sunrise']
    rng = np.random.default_rng(147)
    for j in range(6):
        tex = C.cl.cu[(j + 2) % len(C.cl.cu)]
        w, h = tex.size
        x = csx - 900 + j * 380 + rng.uniform(-60, 60) + lt * 14
        y = lerp(1500, 1260, pull) + rng.uniform(-30, 30)
        s = lerp(1.4, 0.8, pull)
        C.cl.draw(tex, gl.rect_h(x, y, w * s, h * s, anchor=(0.5, 1.0)), pal, alpha=smooth(prog(pull, 0.2, 0.6)) * 0.95)
    O.fog_bottom(E.hexc('e09a8a'), 0.9 * smooth(prog(pull, 0.0, 0.4)) + 0.3, 920, 1080)
    rng = np.random.default_rng(148)
    n = 90
    fx.parts(gl, rng.uniform(0, 1920, n), (rng.uniform(0, 1080, n) - t * 20) % 1080, rng.uniform(5, 16, n), (1, 0.95, 0.8),
             rng.uniform(0.3, 0.8, n) * (0.5 + 0.5 * np.sin(t * 2 + np.arange(n))), kind=1)
    wh = smooth(prog(lt, d - 0.5, d))
    return dict(bloomAmt=0.8, vign=0.25, flash=wh * 0.9, flashCol=(1.0, 0.97, 0.9))


def staff_lines():
    return [
        ('主題歌', '「Creative Vision 10！ ～明日へ飛べ～」'),
        ('歌', VX(MAIN)),
        ('コーラス', '　'.join(VX(n) for n in CHORUS)),
        ('作画・撮影・編集', 'Python + GLSL（すべてコードで描画・動画生成AI不使用）'),
        ('背景原画', '富士山と天の川・四季の里（静止画の生成画像）'),
        ('ミニキャラ', 'Codex のペット'),
        ('', ''),
        ('コミット', f'{len(C.clog):,}（うち AI と共同 {C.n_ai:,}）'),
        ('開発した日', f'{C.n_days} 日'),
        ('画面', f'{C.n_views}'),
        ('帳票', f'{C.n_forms}'),
        ('テスト', f'{C.n_tests:,}'),
        ('', ''),
        ('Creative Vision 10', '2027年1月 リリース予定'),
    ]


def f_outro(t, lt, d):
    gl = C.gl
    O.sky_layer(t, 'dawn', cam=(0, 120), cloud_y=1000 + 160 * (1 - prog(t, 156.9, 157.3)), scroll=10, n=8, scale=0.7, haze=0.45, seed=14, drift=0.6)
    fx.god_rays(gl, 0.62, 0.86, amount=0.8)
    if t < 157.2:
        # スタッフ（2ページ。各 1.7 秒）
        fx.parts(gl, [960], [520], [2000], E.hexc('140828'), [0.62], kind=0, blend='over')
        rows = [r for r in staff_lines() if r[0]]
        pages = [rows[:6], rows[6:]]
        for pi, page in enumerate(pages):
            al = min(prog(lt, pi * 1.7 + 0.05, pi * 1.7 + 0.35), 1 - prog(lt, pi * 1.7 + 1.45, pi * 1.7 + 1.7)) if pi == 0 else prog(lt, 1.75, 2.05)
            if al <= 0:
                continue
            for i, (a, b) in enumerate(page):
                y = 110 + i * 112 - 10 * (1 - ease_out(al))
                tex = gl.text(a, 24, fam='noto', weight='Bold', color=E.hexc('f3dcf0'), stroke=3, stroke_color=E.hexc('2a1240'))
                gl.sprite(tex, 960, y, alpha=al)
                tex = gl.text(b, 36, fam='noto', weight='Black', color=(1, 1, 1), stroke=4, stroke_color=E.hexc('2a1240'))
                gl.sprite(tex, 960, y + 44, alpha=al)
        for j, x in enumerate((210, 1710)):
            pet('wave', t, x, 1040, scale=1.4, fps=7, phase=j * 2, flip=j == 1)
        return dict(bloomAmt=0.6, vign=0.25)
    lt2 = t - 157.2
    O.title_card(t, lt2, 3.0, y=-40)
    k = prog(lt2, 0.8, 1.4)
    tex = gl.text('2027年1月 リリース予定', 44, fam='noto', weight='Bold', color=(1, 1, 1), stroke=4, stroke_color=E.hexc('6a2a6a'))
    gl.sprite(tex, 960, 880, alpha=k)
    shells = [(157.3, 330, 260, (1.0, 0.8, 0.35), 240), (157.9, 1590, 230, (1.0, 0.45, 0.7), 230), (158.6, 520, 160, (0.6, 0.85, 1.0), 220),
              (159.2, 1420, 140, (0.8, 1.0, 0.6), 230)]
    fireworks(t, shells)
    return dict(bloomAmt=0.6, vign=0.25, **O.flash(t, 157.2, 0.4))


SHOTS = [(a, b, fn) for (a, b, fn) in O.SHOTS if b <= 54.6] + [
    (54.6, 62.6, O.s_sunrise),
    (62.6, 64.2, f_to_night),
    (64.2, 70.6, f_fuji_night),
    (70.6, 73.8, f_wind),
    (73.8, 77.0, f_open),
    (77.0, 80.2, f_ai),
    (80.2, 83.4, f_morning),
    (83.4, 86.6, f_lost),
    (86.6, 89.8, f_tests),
    (89.8, 93.0, f_days),
    (93.0, 96.2, f_shine),
    (96.2, 99.4, f_title2),
    (99.4, 102.6, f_voices),
    (102.6, 105.8, f_tears),
    (105.8, 107.4, f_ikou2),
    (107.4, 112.2, f_fuji_dawn),
    (112.2, 115.4, f_run),
    (115.4, 121.8, f_draw),
    (121.8, 128.2, f_galaxy),
    (128.2, 131.4, f_title3),
    (131.4, 134.6, f_paint),
    (134.6, 137.8, f_smiles),
    (137.8, 139.4, f_ikou3),
    (139.4, 146.6, f_gate),
    (146.6, 153.8, f_finale),
    (153.8, 160.2, f_outro),
    (160.2, 1e9, O.s_ending),
]

NO_KARAOKE = [(38.6, 41.8), (96.2, 99.4), (128.2, 131.4), (153.8, 1e9)]


def frame(gl, t):
    post = {}
    MB = O.MB
    for k in range(MB):
        ts = t + ((k + 0.5) / MB - 0.5) * O.SHUTTER / FPS if MB > 1 else t
        gl.time = ts
        gl.use(gl.main, clear=(0, 0, 0, 1))
        p = {}
        for a, b, fn in SHOTS:
            if a <= ts < b:
                p = fn(ts, ts - a, b - a) or {}
                break
        if k == MB // 2:
            post = p
        gl.use(gl.main)
        if 13.0 <= t and not any(a <= t < b for a, b in NO_KARAOKE):
            O.karaoke(t)
        O.credits(t)
        credits2(t)
        if MB > 1:
            gl.accumulate(1.0 / MB, k == 0)
    pp = dict(grainAmt=0.018, vign=0.25)
    pp.update(post)
    gl.time = t
    if MB > 1:
        pp['src'] = gl.acc
    return gl.finish(pp)


def audio():
    a = E.read_audio(SONG)
    n = int(DURATION * 48000)
    if len(a) < n:
        a = np.concatenate([a, np.zeros((n - len(a), 2), np.float32)])
    out = a[:n].copy()
    fade = int(0.25 * 48000)
    out[-fade:] *= np.linspace(1, 0, fade)[:, None]
    return out
