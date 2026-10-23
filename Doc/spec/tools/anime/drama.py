# 作品2「魔女っ子エンジニアの開発日誌」— 4コマ原作のボイス付きショートアニメ（約75秒）
# 台詞は VOICEVOX（四国めたん・九州そら）で合成し、モーラの時刻から口パクを作る。
# 背景はハッピー／ストレスの差分画像をそのまま「心情で変わる部屋」として使う。BGM は主題歌の伴奏。
#
# 秒割り
#   0.0- 6.4  4コマの紙面をカメラが渡り、①コマへ入る          （伴奏イントロ）
#   6.4-12.8  朝「今日もいい天気だし、開発作業がんばるぞ！」
#  12.8-21.2  発見「public partial が使えるの今日知った！」＋実コードの打鍵
#  21.2-30.6  修羅場「いくら作業しても終わらないよぉー」        （音楽が止まり雨）
#  30.6-43.8  ひらめき「Wpfclient 側は AI にやってもらお！」AI「了解しました」 （伴奏Bメロから）
#  43.8-56.6  モンタージュ: 実画面が次々に生成・検証される     （サビ）
#  56.6-69.4  締め「Creative Vision 10！」「2027年1月リリース予定」
#  69.4-75.0  エンドカード
import os, math, glob
import numpy as np
import engine as E, rig as R, fx, clouds, voice
from engine import prog, lerp, smooth, ease_out, ease_in, ease_io, ease_out_back, pulse
from lines2 import LINES

import core  # noqa: E402  効果音の合成

FPS = 30
SUPERSAMPLE = 2
DURATION = 76.5
SR = 48000
INST = os.path.join(E.PROMO, 'song', 'creativevision10_inst.m4a')

CUE = [  # (台詞キー, 開始秒)
    ('morning', 7.0), ('found', 13.4), ('found2', 19.25), ('stress', 23.6), ('idea', 31.0), ('idea2', 34.7),
    ('ai', 41.6), ('ai2', 44.4), ('yatta', 53.2), ('final1', 57.0), ('final2', 61.3), ('final_ai', 64.3), ('final3', 69.7),
]
SPEAKER_COL = {2: E.hexc('ff8fc8'), 0: E.hexc('ff8fc8'), 36: E.hexc('ff8fc8'), 6: E.hexc('ff8fc8'), 16: E.hexc('6fe3ff')}

C = None


class Ctx:
    pass


def setup(gl):
    global C
    C = Ctx()
    C.gl = gl
    C.ch = R.Character(gl)
    C.cl = clouds.Clouds(gl)
    C.sims = {}
    C.koma = gl.image(os.path.join(E.ASSETS4, 'dev4koma-20260710.jpg'))
    names = ['Customers', 'DailyShopBudgetQuery', 'Result_RxF', 'ShiireInputDetail', 'InventoryAllocationMatrix',
             'HachuHaibunInputDetail', 'JuchuInputDetail', 'HachuHaibunInputArrived', 'DeliveryScheduleInquiry',
             'InventoryAllocationList', 'HaibunCommitShohin', 'L03_OrderFixApplied', 'MasterYosanBrandMente',
             'ShiireTrendReport', 'SalesOrderAllocation', 'StagnationList', 'HenpinInputSearched', 'JuchuBestTable',
             'ShohinJuchuSummaryTable', 'PurchaseReceiptAllocation', 'ReservationList', 'FlowInventory', 'Processing',
             'ShopBrandBudgetVsActual', 'HachuZanCompletionSetting', 'MaterialInputDetail', 'manual_02_All',
             'L04_PlaceFailedBatch', 'ShortageResult', 'customer_b_mismatch', 'FlowSalesOrder', 'HachuInput',
             'JuchuInput', 'ShiireInput', 'HenpinInput', 'NouhinYoteiTable']
    C.screens = [gl.image(os.path.join(E.ASSETS, 'screens', p + '.jpg'), maxw=1200) for p in names
                 if os.path.exists(os.path.join(E.ASSETS, 'screens', p + '.jpg'))]
    C.forms = [gl.image(f, maxw=600) for f in sorted(glob.glob(os.path.join(E.ASSETS, 'forms', '*.jpg')))[:40]]
    C.logo = gl.image(os.path.join(E.ASSETS, 'logo_alpha.png'), maxw=1400)
    C.code = make_code(gl)
    C.badge = make_badge(gl)
    # 台詞の音声とモーラ
    C.lines = []
    for key, t0 in CUE:
        disp, text, spk, kw = LINES[key]
        a, moras = voice.say(text, spk, **kw)
        x = np.abs(a[:, 0])
        hop = 480
        n = len(x) // hop
        env = np.sqrt((x[:n * hop].reshape(n, hop) ** 2).mean(1))
        env /= np.percentile(env[env > 1e-4], 95) + 1e-9
        C.lines.append(dict(key=key, t0=t0, dur=len(a) / SR, disp=disp, spk=spk, moras=moras, env=np.clip(env, 0, 1.3), audio=a))


def make_badge(gl):
    """検証済みの印（緑の丸に白いチェック）"""
    from PIL import Image, ImageDraw
    s = 160
    im = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse([6, 6, s - 6, s - 6], fill=(34, 180, 90, 255), outline=(255, 255, 255, 255), width=8)
    d.line([(44, 84), (70, 110), (118, 54)], fill=(255, 255, 255, 255), width=16, joint='curve')
    return gl.texture(np.asarray(im), key=('badge',))


def make_code(gl):
    """実ソース（CvWpfclient/Helpers/ViewModels/BaseKinInputViewModel.cs 38〜42行）を色付きで1行ずつ画像化"""
    from PIL import Image, ImageDraw
    rows = [
        [('[', 'p'), ('ObservableProperty', 't'), (']', 'p')],
        [('public ', 'k'), ('partial ', 'k'), ('ObservableCollection', 't'), ('<', 'p'), ('TranKinMeisai', 't'), ('> ', 'p'),
         ('EditMeisai', 'n'), (' { ', 'p'), ('get', 'k'), ('; ', 'p'), ('set', 'k'), ('; } = [];', 'p')],
        [],
        [('[', 'p'), ('ObservableProperty', 't'), (']', 'p')],
        [('public ', 'k'), ('partial ', 'k'), ('TranKinMeisai', 't'), ('? ', 'p'), ('SelectedMeisai', 'n'), (' { ', 'p'),
         ('get', 'k'), ('; ', 'p'), ('set', 'k'), ('; }', 'p')],
    ]
    col = {'p': (212, 212, 212), 't': (78, 201, 176), 'k': (86, 156, 214), 'n': (220, 220, 170)}
    f = E.font('mono', 30 * gl.ss)
    texs = []
    for r in rows:
        w = int(sum(f.getlength(s) for s, _ in r)) + 8 if r else 8
        im = Image.new('RGBA', (max(w, 8), int(44 * gl.ss)), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        x = 0
        for s, k in r:
            d.text((x, 4), s, font=f, fill=col[k] + (255,))
            x += f.getlength(s)
        t = gl.texture(np.asarray(im), key=('code2', len(texs)))
        t.extra = 1.0 / gl.ss
        texs.append(t)
    return texs


# ---------------------------------------------------------------- 口パク・字幕
SHAPES = {'a': (0.95, 0.85), 'i': (0.38, 1.0), 'u': (0.34, 0.12), 'e': (0.62, 0.9), 'o': (0.72, 0.28), 'n': (0.06, 0.55), 'q': (0.0, 0.6)}


def active_line(t, spk_filter=None):
    for ln in C.lines:
        if ln['t0'] <= t < ln['t0'] + ln['dur']:
            if spk_filter is None or ln['spk'] in spk_filter:
                return ln
    return None


def mouth(t):
    ln = active_line(t, {0, 2, 6, 36})
    if not ln:
        return 0.0, 0.6
    lt = t - ln['t0']
    i = int((lt + 0.02) * 100)
    lv = float(ln['env'][i]) if 0 <= i < len(ln['env']) else 0.0
    if lv < 0.07:
        return 0.0, 0.6
    cur = None
    for (a, b, v, txt) in ln['moras']:
        if a - 0.04 <= lt < b + 0.01:
            cur = (a, b, v)
    if cur is None:
        return 0.15 * lv, 0.6
    op, wd = SHAPES.get(cur[2], (0.6, 0.7))
    return op * min(1.0, 0.35 + lv * 0.85), wd


def subtitle(t):
    gl = C.gl
    for ln in C.lines:
        a, b = ln['t0'] - 0.1, ln['t0'] + ln['dur'] + 0.35
        if a <= t <= b:
            al = min(prog(t, a, a + 0.12), 1 - prog(t, b - 0.2, b))
            col = SPEAKER_COL.get(ln['spk'], (1, 1, 1))
            tex = gl.text(ln['disp'], 44, fam='noto', weight='Black', color=(1, 1, 1), stroke=6, stroke_color=tuple(np.array(col) * 0.55))
            gl.sprite(tex, 960, 1005, alpha=al)


# ---------------------------------------------------------------- 道具
def char_H(cx, cy, zoom, sx=960, sy=540, rot=0.0):
    return E.m3_translate(sx, sy) @ E.m3_rot(rot) @ E.m3_scale(zoom) @ E.m3_translate(-cx, -cy) @ E.m3_scale(2048)


def blink_at(t, seed=0, period=3.2):
    k = math.floor((t + seed * 1.37) / period)
    ph = E.hash11(k + seed * 31) * (period - 0.4)
    tb = k * period - seed * 1.37 + ph + 0.2
    d = t - tb
    if 0 <= d < 0.16:
        return math.sin(math.pi * d / 0.16) ** 0.7
    return 0.0


def pose(t, expr_face=(1, 0, 0, 0), expr_full=None, head=0.0, wind=0.0, typing=0.0, seed=0, talk=True, nod=True, blink=None):
    p = R.Pose()
    m = mouth(t) if talk else (0.0, 0.6)
    p.mouth, p.wide = m
    # 話すと小さくうなずく
    nodv = 0.0
    if nod and talk:
        nodv = 6.0 * m[0] * math.sin(t * 9.0) ** 2
    p.head = head + 1.0 * math.sin(t * 1.1 + seed)
    p.head_shift = (3 * math.sin(t * 0.7 + seed), 2 * math.sin(t * 1.3 + seed) + nodv)
    p.breath = 0.005 * math.sin(t * 2 * math.pi / 3.4 + seed)
    p.blink = blink_at(t, seed) if blink is None else blink
    p.wind = wind
    p.expr_face = expr_face
    p.expr_full = expr_full if expr_full is not None else expr_face
    if typing > 0:
        p.handL = (0, -7 * typing * max(0, math.sin(t * 19)) ** 3)
        p.handR = (0, -7 * typing * max(0, math.sin(t * 16 + 1.3)) ** 3)
    return p


def draw_char(key, t, t0, pose_fn, Hm, look=None):
    s = C.sims.get(key)
    if s is None:
        s = R.Sim(pose_fn, FPS, 4, t0)
        C.sims[key] = s
    st = s.state(t)
    C.ch.draw(Hm, C.ch.uniforms(st['pose'], t, st), look)


def room(t, key, t0, pose_fn, zoom, cx, cy, plate='N', sx=960, sy=540, rot=0.0, look=None, plate_tint=(1, 1, 1, 1)):
    Hm = char_H(cx, cy, zoom, sx, sy, rot)
    C.ch.plate(Hm, plate, tint=plate_tint)
    draw_char(key, t, t0, pose_fn, Hm, look)
    return Hm


def mix_expr(a, b, k):
    return tuple(lerp(x, y, k) for x, y in zip(a, b))


N_, HP_, ST_, HT_ = (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)


# ---------------------------------------------------------------- シーン
def sc_opening(t, lt, d):
    gl = C.gl
    fx.grad(gl, 960, 540, 1920, 1080, (*E.hexc('2a1850'), 1), (*E.hexc('6a3d8f'), 1))
    rng = np.random.default_rng(2)
    n = 40
    fx.parts(gl, rng.uniform(0, 1920, n), rng.uniform(0, 1080, n), rng.uniform(60, 200, n), (1, 0.8, 1), rng.uniform(0.05, 0.15, n), kind=0)
    # 紙面（1024px）を3Dのカードとして置き、各コマの中心を順に見る
    panels = [(256, 253), (771, 253), (256, 771), (771, 771)]
    keys = [(0.0, (512, 470), 3.4), (1.6, (512, 470), 3.1), (2.6, panels[0], 1.25), (3.4, panels[1], 1.25), (4.2, panels[2], 1.25), (5.0, panels[3], 1.25),
            (5.7, panels[0], 1.25), (6.4, panels[0], 0.95)]
    # 補間
    for i in range(len(keys) - 1):
        if keys[i][0] <= lt < keys[i + 1][0] or i == len(keys) - 2:
            a, b = keys[i], keys[i + 1]
            k = ease_io(prog(lt, a[0], b[0]))
            break
    px = lerp(a[1][0], b[1][0], k); py = lerp(a[1][1], b[1][1], k); dist = lerp(a[2], b[2], k)
    W = 4.0  # 紙の幅（ワールド単位）
    tx, ty = (px / 1024 - 0.5) * W, -(py / 1024 - 0.5) * W
    tilt = 0.35 * (1 - smooth(prog(lt, 0.0, 1.6)))
    cam = E.Cam3(eye=(tx + 0.4 * tilt, ty - 2.2 * tilt, -dist * 2.0), target=(tx, ty, 0), fov=42, roll=0.05 * math.sin(lt * 0.7))
    H_, z = cam.quad_h(np.array([0, 0, 0]), np.array([1.0, 0, 0]), np.array([0, -1.0, 0]), W, W)
    gl.draw(PAPER_FS, H_, dict(alpha=1.0), dict(tex=C.koma))
    # 題字
    k1 = prog(lt, 0.4, 0.9) * (1 - prog(lt, 2.2, 2.7))
    fx.parts(gl, [960], [200], [1500], E.hexc('1a0c38'), [0.7 * k1], kind=0, blend='over')
    t1 = gl.text('魔女っ子エンジニアの開発日誌', 76, fam='noto', weight='Black', color=(1, 1, 1), stroke=8, stroke_color=E.hexc('5a2a8a'))
    gl.sprite(t1, 960, 160, alpha=k1, scale=0.96 + 0.04 * ease_out(prog(lt, 0.5, 1.4)))
    t2 = gl.text('～ Creative Vision 10 ができるまで ～', 40, fam='noto', weight='Bold', color=E.hexc('fff1b8'), stroke=5, stroke_color=E.hexc('5a2a8a'))
    gl.sprite(t2, 960, 250, alpha=k1)
    # 最後に①コマへ入る白い光
    return dict(bloomAmt=0.4, vign=0.35, flash=smooth(prog(lt, 5.9, 6.4)), flashCol=(1, 1, 1), fade=1 - prog(lt, 0, 0.5))


PAPER_FS = E.HEADER + fx.NOISE + r'''
uniform sampler2D tex; uniform float alpha;
void main(){
    vec3 c = texture(tex, uv).rgb;
    float g = (vnoise(uv*400.0) - 0.5)*0.04;
    vec2 b = min(uv, 1.0 - uv);
    float e = smoothstep(0.0, 0.004, min(b.x, b.y));
    f = vec4((c + g)*e, e)*alpha;
}
'''


def sc_morning(t, lt, d):
    gl = C.gl
    z = lerp(1.08, 1.16, ease_io(lt / d))

    def pf(tt):
        typing = smooth(prog(tt, 11.0, 11.4))
        return pose(tt, HP_, head=-1.5, seed=1, typing=typing)
    room(t, 'morning', 6.4 - 0.5, pf, z, 1010, 1000, plate='HP', look=dict(tintAdd=(0.05, 0.03, 0.0)))
    # 朝の光
    fx.grad(gl, 300, 300, 1400, 1100, (1.0, 0.9, 0.6, 0.25), (1.0, 0.9, 0.6, 0.0), vertical=False, angle=0.5, blend='add')
    rng = np.random.default_rng(3)
    n = 50
    fx.parts(gl, (rng.uniform(0, 1920, n) + lt * 12) % 1920, (rng.uniform(0, 1080, n) + np.sin(lt + np.arange(n)) * 20), rng.uniform(4, 10, n),
             (1, 0.95, 0.8), rng.uniform(0.2, 0.6, n), kind=0)
    return dict(bloomAmt=0.4, vign=0.2, flash=1 - smooth(prog(lt, 0, 0.5)), flashCol=(1, 1, 1))


def sc_found(t, lt, d):
    gl = C.gl
    # 前半は通常顔、早速対応しよ！でハッピー
    def pf(tt):
        k = smooth(prog(tt, 19.0, 19.4))
        return pose(tt, mix_expr(N_, HP_, k), expr_full=N_, head=1.5, seed=2)
    z = lerp(1.2, 1.3, ease_io(lt / d))
    room(t, 'found', 12.8 - 0.5, pf, z, 1080, 980, plate='N', sx=1250, sy=560,
         look=dict(rimCol=(0.4, 0.7, 1.2), rimDir=(-1, 0.2), rimW=7))
    # コードの窓（左）
    k = ease_out_back(prog(lt, 0.2, 0.7), 1.4)
    x0, y0, w, h = 70, 210, 1060, 430
    Hm = gl.rect_h(x0 + w / 2, y0 + h / 2, w * k, h * k)
    gl.draw(EDITOR_FS, Hm, dict(alpha=prog(lt, 0.2, 0.4), aspectR=w / h), blend='over')
    title = gl.text('BaseKinInputViewModel.cs', 26, fam='segoe', weight=None, color=(0.85, 0.85, 0.9))
    gl.sprite(title, x0 + 30, y0 + 28, anchor=(0, 0.5), alpha=prog(lt, 0.5, 0.7))
    badge = gl.text('MVVM Toolkit  ·  C# 14', 24, fam='segoeb', weight=None, color=E.hexc('c9a7ff'))
    gl.sprite(badge, x0 + w - 30, y0 + 28, anchor=(1, 0.5), alpha=prog(lt, 0.5, 0.7))
    # 1行ずつ打鍵（台詞の「public partial」に合わせて2行目が出る）
    starts = [0.9, 1.5, 9e9, 3.9, 4.4]
    for i, tex in enumerate(C.code):
        if i == 2:
            continue
        kk = prog(lt, starts[i], starts[i] + 0.9)
        if kk <= 0:
            continue
        ww, hh = gl.vsize(tex)
        Hl = gl.rect_h(x0 + 40 + ww / 2, y0 + 100 + i * 62, ww, hh)
        gl.draw(CLIP_FS, Hl, dict(frac=kk, alpha=1.0), dict(tex=tex))
        if kk < 1:
            cx = x0 + 40 + ww * kk
            fx.rect(gl, cx + 4, y0 + 100 + i * 62, 3, 34, (0.9, 0.9, 1.0), alpha=0.9)
    # 「public partial」を光で強調
    hl = smooth(prog(lt, 2.4, 2.8)) * (1 - smooth(prog(lt, 5.2, 5.8)))
    if hl > 0:
        fx.rect(gl, x0 + 40 + 150, y0 + 162, 300, 46, (1.0, 0.85, 0.3), alpha=0.25 * hl, blend='add')
    return dict(bloomAmt=0.5, vign=0.3)


EDITOR_FS = E.HEADER + r'''
uniform float alpha, aspectR;
void main(){
    vec2 p = uv*2.0 - 1.0;
    vec2 q = abs(p*vec2(aspectR, 1.0)) - vec2(aspectR - 0.08, 0.92);
    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.08;
    float body = smoothstep(0.01, -0.01, d);
    float bar = step(uv.y, 0.12);
    vec3 c = mix(vec3(0.12, 0.12, 0.17), vec3(0.18, 0.16, 0.26), bar);
    float glow = exp(-max(d, 0.0)*18.0)*(1.0 - body);
    vec3 o = c*body + vec3(0.55, 0.4, 1.0)*glow*0.6;
    f = vec4(o, body*0.94 + glow*0.3)*alpha;
}
'''

CLIP_FS = E.HEADER + r'''
uniform sampler2D tex; uniform float frac, alpha;
void main(){ f = texture(tex, uv)*step(uv.x, frac)*alpha; }
'''


def sc_stress(t, lt, d):
    gl = C.gl
    shake = (math.sin(t * 37) * 3 * prog(lt, 1.0, 2.0), math.cos(t * 41) * 3 * prog(lt, 1.0, 2.0))
    z = lerp(1.0, 1.12, ease_io(lt / d))

    def pf(tt):
        return pose(tt, ST_, head=-2.0 + 1.5 * math.sin(tt * 0.6), seed=3, typing=0.6)
    room(t, 'stress', 21.2 - 0.5, pf, z, 1024, 1060, plate='ST', sx=960 + shake[0], sy=540 + shake[1],
         look=dict(tintMul=(0.85, 0.85, 0.95)), plate_tint=(0.75, 0.75, 0.85, 1))
    # 降り積もる帳票と画面（終わらない作業）
    rng = np.random.default_rng(7)
    n = 46
    for i in range(n):
        s0 = 0.4 + i * 0.17 + rng.uniform(0, 0.1)
        if lt < s0:
            continue
        isform = i % 3 != 0
        tex = C.forms[i % len(C.forms)] if isform else C.screens[i % len(C.screens)]
        x = rng.uniform(80, 700) if i % 2 else rng.uniform(1300, 1840)
        if i in (9, 23):
            x = rng.uniform(850, 1150)   # たまに顔の前へ（笑い）
        rest = 1080 - 60 - (i // 6) * 22 + rng.uniform(-15, 15)
        tt = lt - s0
        y = -200 + 0.5 * 2600 * tt * tt
        ang = rng.uniform(-0.6, 0.6)
        if y > rest:
            y = rest
            spin = ang
        else:
            spin = ang + (1 - tt) * rng.uniform(-3, 3)
        sc = (230 if isform else 330) / tex.size[0]
        gl.sprite(tex, x, y, scale=sc, angle=spin, alpha=0.95, tint=(0.9, 0.9, 0.95))
    # 雨
    m = 300
    xs = rng.uniform(0, 1920, m); ys = (rng.uniform(0, 1080, m) + lt * 1400) % 1100 - 10
    for k in range(2):
        fx.parts(gl, xs + k * 4, ys + k * 14, np.full(m, 5.0), (0.7, 0.75, 0.9), np.full(m, 0.35), kind=0)
    # 汗
    sw = (lt * 1.3) % 1.0
    fx.parts(gl, [1290], [480 + sw * 120], [26], (0.7, 0.85, 1.0), [1 - sw], kind=0)
    tex = gl.text('残 タスク', 30, fam='noto', weight='Black', color=(1, 1, 1), stroke=4, stroke_color=(0.2, 0.1, 0.3))
    cnt = int(lerp(12, 999, ease_in(prog(lt, 1.0, 8.0), 2)))
    tex2 = gl.text(f'{cnt}', 80, fam='segoeb', weight=None, color=E.hexc('ff6b6b'), stroke=5, stroke_color=(0.2, 0.05, 0.1))
    gl.sprite(tex, 1780, 80, anchor=(1, 0.5), alpha=prog(lt, 0.8, 1.2))
    gl.sprite(tex2, 1780, 150, anchor=(1, 0.5), alpha=prog(lt, 0.8, 1.2), scale=1 + 0.08 * math.sin(lt * 12))
    return dict(bloomAmt=0.3, vign=0.6, sat=0.75, contrast=1.05)


def ai_panel(t, x, y, w, h, alpha, text=None):
    """AI アシスタントのホログラム窓"""
    gl = C.gl
    Hm = gl.rect_h(x, y, w, h)
    gl.draw(HOLO_FS, Hm, dict(alpha=alpha, aspectR=w / h), blend='add')
    tex = gl.text('AI Assistant', 30, fam='segoeb', weight=None, color=E.hexc('a8f0ff'))
    gl.sprite(tex, x - w / 2 + 90, y - h / 2 + 40, anchor=(0, 0.5), alpha=alpha)
    # 浮かぶ球
    r = 26 + 3 * math.sin(t * 4)
    fx.parts(gl, [x - w / 2 + 50], [y - h / 2 + 40], [r * 3], (0.4, 0.9, 1.0), [alpha], kind=0)
    if text is None:
        for i in range(3):
            a = 0.3 + 0.7 * max(0, math.sin(t * 6 - i * 0.8))
            fx.parts(gl, [x - 40 + i * 40], [y + 20], [26], (0.7, 0.95, 1.0), [a * alpha], kind=0)
    else:
        tx = gl.text(text, 32, fam='noto', weight='Bold', color=(0.9, 0.98, 1.0))
        gl.sprite(tx, x - w / 2 + 40, y + 25, anchor=(0, 0.5), alpha=alpha)


HOLO_FS = E.HEADER + r'''
uniform float alpha, aspectR;
void main(){
    vec2 p = uv*2.0 - 1.0;
    vec2 q = abs(p*vec2(aspectR, 1.0)) - vec2(aspectR - 0.12, 0.88);
    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.12;
    float body = smoothstep(0.01, -0.01, d);
    float edge = smoothstep(0.03, 0.0, abs(d));
    float scan = 0.85 + 0.15*sin(uv.y*120.0 - time*6.0);
    float glow = exp(-max(d, 0.0)*10.0)*(1.0 - body);
    vec3 c = vec3(0.1, 0.45, 0.7)*body*0.35*scan + vec3(0.4, 0.95, 1.2)*edge*1.2 + vec3(0.2, 0.7, 1.0)*glow*0.4;
    f = vec4(c*alpha, 0.0);
}
'''


def sc_idea(t, lt, d):
    gl = C.gl
    # 背景: ストレス → 通常 → ハッピー
    k1 = smooth(prog(t, 30.9, 31.6)); k2 = smooth(prog(t, 34.6, 35.0))

    def pf(tt):
        a = smooth(prog(tt, 30.9, 31.6)); b = smooth(prog(tt, 34.6, 35.0))
        fe = mix_expr(mix_expr(ST_, N_, a), HP_, b)
        return pose(tt, fe, expr_full=fe, head=lerp(-2.0, 2.0, b), seed=4)
    z = lerp(1.05, 1.0, ease_io(lt / d))
    Hm = char_H(1010, 1040, z, 960, 540)
    C.ch.plate(Hm, 'ST', tint=(1, 1, 1, 1 - k1))
    C.ch.plate(Hm, 'N', tint=(1, 1, 1, k1 * (1 - k2)))
    C.ch.plate(Hm, 'HP', tint=(1, 1, 1, k2))
    draw_char('idea', t, 30.6 - 0.5, pf, Hm)
    # ひらめきの光
    ping = pulse(t, 31.0, 0.02, 0.6)
    if ping > 0:
        fx.parts(gl, [1380], [260], [500 * (1.3 - ping)], (1.0, 0.95, 0.6), [ping], kind=1)
        tex = gl.text('！', 140, fam='noto', weight='Black', color=E.hexc('ffd84d'), stroke=8, stroke_color=E.hexc('8a4a00'))
        gl.sprite(tex, 1380, 260, scale=ease_out_back(prog(t, 31.0, 31.25), 2.5))
    # AI の窓が開く
    ka = ease_out_back(prog(t, 40.6, 41.2), 1.3)
    if ka > 0:
        txt = '了解しました。' if t >= 41.6 else None
        ai_panel(t, 470, 330, 760 * ka, 210 * ka, min(1, ka), txt)
    return dict(bloomAmt=0.45, vign=0.25, sat=lerp(0.75, 1.0, k1))


def sc_montage(t, lt, d):
    """サビ: 実画面が次々に生成され、検証の印が付く"""
    gl = C.gl
    fx.grad(gl, 960, 540, 1920, 1080, (*E.hexc('0b1030'), 1), (*E.hexc('2b1660'), 1))
    ang = lerp(-0.35, 0.35, ease_io(lt / d))
    cam = E.Cam3(eye=(math.sin(ang) * 10, 0.6 + 0.3 * math.sin(lt * 0.5), -math.cos(ang) * 10), target=(0, 0.3, 0), fov=50)
    cols, rows = 8, 4
    items = []
    for r_ in range(rows):
        for c_ in range(cols):
            i = r_ * cols + c_
            a = (c_ - (cols - 1) / 2) * 0.24
            R_ = 7.5
            pos = np.array([math.sin(a) * R_, (rows / 2 - r_ - 0.5) * 1.55, math.cos(a) * R_ - R_ + 1.5])
            right = np.array([math.cos(a), 0, -math.sin(a)])
            items.append((i, pos, right))
    items.sort(key=lambda q: -cam.to_cam(q[1])[2])
    order = np.random.default_rng(5).permutation(cols * rows)
    rank = {int(v): k for k, v in enumerate(order)}
    for i, pos, right in items:
        s0 = 0.2 + rank[i] * 0.22
        ap = prog(lt, s0, s0 + 0.5)
        if ap <= 0:
            continue
        tex = C.screens[i % len(C.screens)]
        w, h = tex.size
        ww = 2.05
        H_, z = cam.quad_h(pos, right, np.array([0, -1.0, 0]), ww, ww * h / w)
        fx.window(gl, tex, H_, appear=ap, alpha=0.95, seed=i, edge=(0.6, 0.85, 1.0), bright=0.85)
        ok = prog(lt, s0 + 1.2, s0 + 1.4)
        if ok > 0:
            p, zz = cam.project(pos + right * ww * 0.38 + np.array([0, ww * h / w * 0.32, 0]))
            gl.sprite(C.badge, p[0], p[1], scale=ease_out_back(ok, 3) * 3.2 / zz, alpha=min(1, ok * 2))
    # 数字（実件数）
    stats = [('199', '画面（View）'), ('130', '帳票'), ('1,219', 'テスト'), ('1,807', 'コミット')]
    for j, (num, lab) in enumerate(stats):
        k = prog(lt, 1.5 + j * 1.6, 2.0 + j * 1.6)
        if k <= 0:
            continue
        x = 300 + j * 440
        t1 = gl.text(num, 84, fam='segoeb', weight=None, color=(1, 1, 1), stroke=5, stroke_color=E.hexc('1b2a6e'))
        t2 = gl.text(lab, 30, fam='noto', weight='Bold', color=E.hexc('b8d8ff'), stroke=3, stroke_color=E.hexc('1b2a6e'))
        gl.sprite(t1, x, 830 - 30 * (1 - ease_out(k)), alpha=k)
        gl.sprite(t2, x, 900, alpha=k)
    # 主人公（右下で喜ぶ）
    kc = smooth(prog(t, 52.6, 53.0))
    if kc > 0:
        def pf(tt):
            p = pose(tt, HT_, expr_full=HP_, seed=6, wind=10)
            return p
        Hm = char_H(1050, 960, 0.55, sx=lerp(2200, 1650, ease_out(kc)), sy=700)
        draw_char('montage', t, 52.0, pf, Hm, dict(outlineW=8.0, outlineCol=(1, 1, 1), cropBottom=1600))
        tex = gl.text('やったー！', 90, fam='noto', weight='Black', color=(1, 1, 1), stroke=9, stroke_color=E.hexc('e53935'))
        gl.sprite(tex, 1600, 250, scale=ease_out_back(prog(t, 53.2, 53.5), 2.5), angle=-0.1)
    ai_panel(t, 330, 110, 520, 130, 0.9, None if active_line(t, {16}) else None)
    return dict(bloomAmt=0.6, vign=0.3, flash=pulse(t, 43.8, 0.02, 0.3), flashCol=(1, 1, 1))


def sc_finale(t, lt, d):
    gl = C.gl
    fx.burst(gl, 0.7, 0.5, rot=t * 0.15, n=26, c1=E.hexc('8e3fbf'), c2=E.hexc('b45fd6'), c3=E.hexc('ffd9f0'))
    fx.vignette(gl, 0.5, col=E.hexc('2a1050'), inner=0.25)
    rng = np.random.default_rng(9)
    n = 160
    xs = (rng.uniform(0, 1920, n) + np.sin(lt * 2 + np.arange(n)) * 30)
    ys = (rng.uniform(-200, 1080, n) + lt * rng.uniform(80, 200, n)) % 1300 - 100
    cols = np.array([E.hexc(c) for c in ('ff7ab8', 'ffd84d', '7ad7ff', 'b48cff', '7dffb0')])

    def confetti():
        for ci in range(5):
            sel = np.arange(n) % 5 == ci
            fx.parts(gl, xs[sel], ys[sel], rng.uniform(14, 26, sel.sum()), cols[ci], np.full(sel.sum(), 0.95), kind=3,
                     angle=rng.uniform(0, 6, sel.sum()) + lt * 3, blend='over')

    def pf(tt):
        if tt >= 69.6:
            return pose(tt, HT_, expr_full=HP_, seed=7, wind=12, head=4.0, nod=False)
        return pose(tt, HP_, seed=7, wind=12, head=1.5)
    if t < 61.3:
        # 寄りで語る
        z = lerp(1.15, 1.32, ease_io(prog(t, 56.6, 61.3)))
        Hm = char_H(1060, 940, z, sx=980, sy=560)
        draw_char('finale', t, 56.6 - 0.5, pf, Hm, dict(outlineW=8.0, outlineCol=(1, 1, 1), cropBottom=1600))
        confetti()
        return dict(bloomAmt=0.35, vign=0.2)
    if t < 69.6:
        k_logo = ease_out_back(prog(t, 61.3, 61.8), 1.8)
        z = lerp(0.62, 0.68, prog(t, 61.3, 69.6))
        Hm = char_H(1050, 960, z, sx=1360, sy=650)
        draw_char('finale', t, 56.6 - 0.5, pf, Hm, dict(outlineW=8.0, outlineCol=(1, 1, 1), cropBottom=1600))
        confetti()
        fx.parts(gl, [560], [480], [1300], E.hexc('1a0c38'), [0.6 * min(1, k_logo)], kind=0, blend='over')
        gl.sprite(C.logo, 560, 330, scale=0.55 * k_logo * 1.05, alpha=0.7, tint=(0, 0, 0), add=(1.6, 1.0, 2.2), blend='add')
        gl.sprite(C.logo, 560, 330, scale=0.55 * k_logo, tint=(0.35, 0.3, 0.45), add=(0.75, 0.72, 0.9))
        tt = gl.text('Creative Vision 10', 92, fam='segoeb', weight=None, color=(1, 1, 1), stroke=7, stroke_color=E.hexc('5a2a8a'))
        gl.sprite(tt, 560, 580, alpha=prog(t, 61.5, 61.9))
        kr = prog(t, 64.3, 64.8)
        if kr > 0:
            tt = gl.text('2027年1月 リリース予定', 56, fam='noto', weight='Black', color=E.hexc('fff3c4'), stroke=6, stroke_color=E.hexc('5a2a8a'))
            gl.sprite(tt, 560, 690, alpha=kr, scale=0.9 + 0.1 * ease_out_back(kr))
        ka = ease_out_back(prog(t, 63.9, 64.3), 1.3)
        if ka > 0:
            ai_panel(t, 560, 860, 640 * ka, 150 * ka, min(1, ka), None)
        return dict(bloomAmt=0.35, vign=0.15, flash=pulse(t, 61.3, 0.02, 0.25) * 0.7, flashCol=(1, 1, 1))
    # 「お楽しみに！」で飛び出すアップ
    k = ease_out_back(prog(t, 69.6, 69.9), 2.0)
    fx.burst(gl, 0.5, 0.5, rot=-t * 0.6, n=20, c1=E.hexc('ffd84d'), c2=E.hexc('ffe98a'), c3=(1, 1, 0.92))
    Hm = char_H(1050, 960, lerp(1.4, 0.95, k), sx=960, sy=600, rot=0.08 * (1 - k))
    draw_char('finale2', t, 69.1, pf, Hm, dict(outlineW=12.0, outlineCol=(1, 1, 1), cropBottom=1600))
    fx.focus_lines(gl, inner=0.45, seed=int(t * 15), amount=0.6, col=E.hexc('ff9a3c'))
    confetti()
    return dict(bloomAmt=0.3, vign=0.1)


def sc_end(t, lt, d):
    gl = C.gl
    fx.grad(gl, 960, 540, 1920, 1080, (*E.hexc('2a1850'), 1), (*E.hexc('6a3d8f'), 1))
    z = lerp(1.6, 1.0, ease_io(prog(lt, 0, 2.5)))
    cx = lerp(771, 512, ease_io(prog(lt, 0, 2.5))); cy = lerp(771, 512, ease_io(prog(lt, 0, 2.5)))
    s = 900 / 1024 * z
    Hm = E.m3_translate(960, 540) @ E.m3_scale(s) @ E.m3_translate(-cx, -cy) @ E.m3_scale(1024)
    gl.draw(PAPER_FS, Hm, dict(alpha=1.0), dict(tex=C.koma))
    k = ease_out_back(prog(lt, 2.2, 2.6), 2.0)
    if k > 0:
        tex = gl.text('おしまい', 120, fam='mincho', weight=None, color=E.hexc('d81b60'), stroke=6, stroke_color=(1, 1, 1))
        gl.sprite(tex, 1500, 860, scale=k, angle=-0.15)
    tex = gl.text('VOICEVOX:四国めたん　VOICEVOX:九州そら　（BGM 主題歌「Creative Vision 10！ ～明日へ飛べ～」伴奏）', 22, fam='noto',
                  weight='Medium', color=(1, 1, 1), stroke=2, stroke_color=(0.15, 0.05, 0.25))
    gl.sprite(tex, 960, 1050, alpha=prog(lt, 1.0, 1.5))
    tex = gl.text('映像はすべてコードで描画（動画生成AI不使用）', 22, fam='noto', weight='Medium', color=(1, 1, 1), stroke=2, stroke_color=(0.15, 0.05, 0.25))
    gl.sprite(tex, 960, 1020, alpha=prog(lt, 1.0, 1.5))
    return dict(bloomAmt=0.3, vign=0.35, fade=prog(t, DURATION - 1.0, DURATION - 0.05))


SCENES = [
    (0.0, 6.4, sc_opening),
    (6.4, 12.8, sc_morning),
    (12.8, 21.2, sc_found),
    (21.2, 30.6, sc_stress),
    (30.6, 43.8, sc_idea),
    (43.8, 56.6, sc_montage),
    (56.6, 71.0, sc_finale),
    (71.0, 1e9, sc_end),
]

MB = 3
SHUTTER = 0.5


def frame(gl, t):
    post = {}
    for k in range(MB):
        ts = t + ((k + 0.5) / MB - 0.5) * SHUTTER / FPS
        gl.time = ts
        gl.use(gl.main, clear=(0, 0, 0, 1))
        for a, b, fn in SCENES:
            if a <= ts < b:
                p = fn(ts, ts - a, b - a) or {}
                break
        if k == MB // 2:
            post = p
        gl.use(gl.main)
        subtitle(t)
        gl.accumulate(1.0 / MB, k == 0)
    pp = dict(grainAmt=0.015, vign=0.25)
    pp.update(post)
    pp['src'] = gl.acc
    gl.time = t
    return gl.finish(pp)


# ---------------------------------------------------------------- 音
def audio():
    global C
    if C is None:
        # 音声だけ作るときも台詞の読み込みが要る
        C = Ctx()
        C.lines = []
        for key, t0 in CUE:
            disp, text, spk, kw = LINES[key]
            a, moras = voice.say(text, spk, **kw)
            C.lines.append(dict(key=key, t0=t0, dur=len(a) / SR, spk=spk, audio=a))
    n = int(DURATION * SR)
    inst = E.read_audio(INST)
    bgm = np.zeros((n, 2), np.float32)

    def place(src_t0, dst_t0, dst_t1, fade_in=0.05, fade_out=0.15):
        a, b = int(dst_t0 * SR), int(dst_t1 * SR)
        s = int(src_t0 * SR)
        seg = inst[s:s + (b - a)].copy()
        m = len(seg)
        fi, fo = int(fade_in * SR), int(fade_out * SR)
        if fi:
            seg[:fi] *= np.linspace(0, 1, fi)[:, None]
        if fo:
            seg[-fo:] *= np.linspace(1, 0, fo)[:, None]
        bgm[a:a + m] += seg
    place(0.0, 0.0, 21.25, 0.0, 0.12)                # イントロ〜Aメロ
    place(25.8, 31.0, 76.5, 0.6, 0.05)               # Bメロ〜サビ〜間奏
    # 最後をフェードアウト
    fo0, fo1 = int(73.0 * SR), int(76.3 * SR)
    bgm[fo0:fo1] *= np.linspace(1, 0, fo1 - fo0)[:, None]
    bgm[fo1:] = 0
    # 台詞
    vox = np.zeros((n, 2), np.float32)
    for ln in C.lines:
        a = int(ln['t0'] * SR)
        x = ln['audio']
        if ln['spk'] == 16:
            # AI は少し機械的に（帯域を絞り、短いディレイを重ねる）
            y = core.bandpass(x[:, 0], 250, 7000)
            d = int(0.012 * SR)
            y2 = np.zeros_like(y); y2[d:] = y[:-d] * 0.35
            y = (y + y2) * 1.1
            x = np.stack([y, y], 1)
        m = min(len(x), n - a)
        vox[a:a + m] += x[:m] * 0.9
    # 伴奏を台詞の下で下げる（サイドチェイン）
    env = np.abs(vox[:, 0])
    k = int(0.02 * SR)
    env = np.convolve(env, np.ones(k) / k, 'same')
    from scipy.ndimage import maximum_filter1d
    env = maximum_filter1d(env, int(0.25 * SR))
    duck = 1.0 - 0.6 * np.clip(env / 0.05, 0, 1)
    bgm *= (0.55 * duck)[:, None]
    # 効果音
    sfx = np.zeros((n, 2), np.float32)

    def add(sig, t0, gain=1.0, pan=0.0):
        s = np.asarray(sig, np.float32)
        if s.ndim == 1:
            s = np.stack([s * (1 - max(0, pan)), s * (1 + min(0, pan))], 1)
        a = int(t0 * SR)
        m = min(len(s), n - a)
        if m > 0:
            sfx[a:a + m] += s[:m] * gain
    add(core.whoosh(0.7, 300, 5000), 5.8, 0.35)
    # 朝のタイピング
    rng = np.random.default_rng(1)
    for tt in np.arange(11.0, 12.8, 0.11):
        add(core.tick(rng.uniform(2500, 4000), 0.02), tt + rng.uniform(0, 0.03), 0.12, rng.uniform(-0.3, 0.3))
    # コードの打鍵
    for i, s0 in enumerate([0.9, 1.5, 3.9, 4.4]):
        for tt in np.arange(0, 0.9, 0.05):
            add(core.tick(rng.uniform(2800, 4200), 0.015), 12.8 + s0 + tt, 0.08)
    # 音楽が止まる（レコードの減速）
    t_ = np.arange(int(0.5 * SR)) / SR
    stop = np.sin(2 * np.pi * np.cumsum(np.linspace(220, 30, len(t_))) / SR) * np.exp(-t_ / 0.25)
    add(stop, 21.15, 0.25)
    # 雨とうなり
    rain = core.lowpass(core.highpass(core.noise(9.4, 3), 800), 6000) * 0.08
    rain[:int(0.5 * SR)] *= np.linspace(0, 1, int(0.5 * SR))
    rain[-int(1.0 * SR):] *= np.linspace(1, 0, int(1.0 * SR))
    add(np.stack([rain, np.roll(rain, 300)], 1), 21.2, 1.0)
    drone = core.pad([45, 52], 9.0, cutoff=500, attack=1.0, release=2.0) * 0.12
    add(drone, 21.5, 1.0)
    # 帳票が落ちる音
    for i in range(46):
        add(core.whoosh(0.18, 800, 2500, seed=i), 21.2 + 0.4 + i * 0.17 + 0.25, 0.05, rng.uniform(-0.6, 0.6))
    # ひらめき
    bell = sum(core.pluck(n_, 1.2, bright=8000, decay=0.4) * g for n_, g in ((88, 0.5), (95, 0.35), (100, 0.25)))
    add(bell, 31.0, 0.25)
    # AI の窓
    add(core.whoosh(0.4, 1000, 6000, seed=5), 40.6, 0.2)
    for i in range(32):
        add(core.pluck(84 + (i % 5) * 2, 0.25, bright=6000, decay=0.08), 43.8 + 0.2 + i * 0.22 + 0.1, 0.06, rng.uniform(-0.5, 0.5))
    add(core.whoosh(0.6, 400, 6000, seed=9), 61.0, 0.25)
    mix = bgm + vox + sfx
    # 全体の音量: 平均 -17dBFS を目安に持ち上げ、ピークは柔らかく -1dB に収める
    rms = np.sqrt((mix ** 2).mean())
    mix *= min(10 ** (-17 / 20) / (rms + 1e-9), 4.0)
    lim = 10 ** (-1.5 / 20)
    mix = np.where(np.abs(mix) > 0.7 * lim, np.sign(mix) * (0.7 * lim + 0.3 * lim * np.tanh((np.abs(mix) - 0.7 * lim) / (0.3 * lim))), mix)
    return mix.astype(np.float32)
