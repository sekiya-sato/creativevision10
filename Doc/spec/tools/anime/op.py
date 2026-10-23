# 作品1「明日へ飛べ」— アニメのオープニング風（主題歌 1番＋エンディングの一撃、約68秒）
# 主人公の一枚絵を rig.py で動かし（まばたき・呼吸・髪/帽子/イヤリングの揺れ・表情・歌の口パク）、
# 空・魔法陣・実画面・コミット履歴などの演出を GLSL で重ねる。動画生成AIは使わない。
#
# 秒割り（音源の時刻。0.2秒の無音の後、150BPM・1小節1.6秒）
#   0.0- 3.4  夜明け前の空                      イントロ前半（和楽器）
#   3.4- 6.6  魔法陣が描かれる
#   6.6- 9.8  逆光のシルエット→光が当たり、目を開く   バンドが入る
#   9.8-11.4  拍ごとのカット（リボン・イヤリング・瞳・手）
#  11.4-13.0  タイトル
#  13.0-16.2  冬の夜、最初のコミット                 Aメロ「たった30のファイル」
#  16.2-19.4  画面の光とコードが顔に映る             「冬のはじまりだったね」
#  19.4-22.6  コミットが積み上がる塔                 「ひとつひとつと重ねて」
#  22.6-25.8  光のかけらが集まる                     「夢のかけらを集めて」
#  25.8-29.0  CV.net → CV10 のバトン                 Bメロ「受け継いだバトン」
#  29.0-32.2  200の灯籠                              「200社の笑顔」
#  32.2-35.4  灯籠の中で歌う                         「今はわたしがつなぐ」
#  35.4-38.6  拍ごとのカットで溜める                 「ここから始まる」
#  38.6-41.8  タイトルの文字が拍で落ちる             サビ「Creative Vision 10！」
#  41.8-45.0  雲の中を飛ぶ                           「明日へ 今 飛び出せ」
#  45.0-48.2  実画面が翼になる                       「重ねた日々が 翼になる」
#  48.2-49.8  いこう！                               掛け声
#  49.8-54.6  3階層の扉を昇って開く                  「まだ見ぬ明日の扉 / 開けよう 今すぐに」
#  54.6-61.0  雲海の日の出で歌う                     「目指すはひとつ あの未来へ」
#  61.0-64.2  ロゴ
#  64.2-67.6  エンディング（ドラムソロ→全員の一撃→ロゴ）  音源 160.2 秒以降をつなぐ
import os, math, glob, subprocess
import numpy as np
import engine as E, rig as R, fx, clouds, songsync as SS
from engine import prog, lerp, smooth, ease_out, ease_in, ease_io, ease_out_back, pulse, clamp

FPS = 30
SUPERSAMPLE = 2
T_CUT = SS.bar(40)            # 64.2: サビ1の終わり
SRC_END = SS.bar(100)          # 160.2: エンディングの頭
SONG = os.path.join(SS.SONG_DIR, 'creativevision10_full_metan.m4a')
DURATION = T_CUT + (163.55 - SRC_END) + 0.25


def remap(t):
    return t if t < T_CUT else t - T_CUT + SRC_END


C = None   # 実行時の共有物


class Ctx:
    pass


# ---------------------------------------------------------------- 準備
def setup(gl):
    global C
    C = Ctx()
    C.gl = gl
    C.ch = R.Character(gl)
    C.mg = fx.Magic(gl)
    C.cl = clouds.Clouds(gl)
    C.vocal = SS.Vocal('metan', remap=remap)
    C.sims = {}
    pick = ['Customers', 'DailyShopBudgetQuery', 'Result_RxF', 'ShiireInputDetail', 'InventoryAllocationMatrix',
            'HachuHaibunInputDetail', 'JuchuInputDetail', 'HachuHaibunInputArrived', 'DeliveryScheduleInquiry',
            'InventoryAllocationList', 'HaibunCommitShohin', 'L03_OrderFixApplied', 'MasterYosanBrandMente',
            'ShiireTrendReport', 'SalesOrderAllocation', 'StagnationList', 'HenpinInputSearched', 'JuchuBestTable',
            'ShohinJuchuSummaryTable', 'PurchaseReceiptAllocation', 'ReservationList', 'FlowInventory', 'Processing',
            'ShopBrandBudgetVsActual', 'HachuZanCompletionSetting', 'MaterialInputDetail', 'manual_02_All',
            'L04_PlaceFailedBatch', 'ShortageResult', 'customer_b_mismatch']
    C.screens = [gl.image(os.path.join(E.ASSETS, 'screens', p + '.jpg'), maxw=1400) for p in pick
                 if os.path.exists(os.path.join(E.ASSETS, 'screens', p + '.jpg'))]
    logo = os.path.join(E.ASSETS, 'logo_alpha.png')
    C.logo = gl.image(logo, maxw=1400)
    # 実コミットの件名（古い順）
    try:
        p = subprocess.run(['git', '-C', E.REPO, 'log', '--reverse', '--format=%ad|%s', '--date=short'],
                           stdout=subprocess.PIPE, check=True, encoding='utf8')
        C.commits = [l.split('|', 1) for l in p.stdout.strip().split('\n') if '|' in l]
    except Exception:
        C.commits = [('2025-11-25', '初期リポジトリ')]
    C.n_commits = len(C.commits)
    # 画面に映り込むコード（実ソース）
    C.code = make_code_tex(gl)
    C.oldcv = make_oldcv(gl)
    C.lyrics = SS.lines({'Aメロ1', 'Bメロ1', 'サビ1'})
    C.rng = np.random.default_rng(7)


def make_code_tex(gl):
    src = os.path.join(E.REPO, 'CvBase')
    lines = []
    for f in sorted(glob.glob(os.path.join(src, '**', '*.cs'), recursive=True))[:40]:
        try:
            with open(f, encoding='utf8', errors='ignore') as fp:
                for l in fp:
                    l = l.rstrip()
                    if 8 < len(l.strip()) < 70 and not l.strip().startswith('//'):
                        lines.append(l.replace('\t', '    '))
        except Exception:
            pass
        if len(lines) > 160:
            break
    lines = lines[:120] or ['public partial class ViewModel : ObservableObject']
    from PIL import Image, ImageDraw
    f = E.font('mono', 26)
    W, Hh = 1400, 34 * len(lines) + 20
    im = Image.new('L', (W, Hh), 0)
    d = ImageDraw.Draw(im)
    for i, l in enumerate(lines):
        d.text((20, 10 + i * 34), l, font=f, fill=255)
    a = np.asarray(im, np.float32) / 255
    rgba = np.zeros(a.shape + (4,), np.float32)
    rgba[..., 0] = 0.55; rgba[..., 1] = 0.85; rgba[..., 2] = 1.0; rgba[..., 3] = a
    t = gl.texture(rgba, key=('code',), repeat=True)
    return t


def make_oldcv(gl):
    """開発構想 PDF の「現行 CV.net の構成（参考）」の画面を切り出す（7ページ）"""
    out = os.path.join(E.WORK, 'oldcv.png')
    if not os.path.exists(out):
        pdf = os.path.join(E.ASSETS4, '開発構想CV10.pdf')
        tmp = os.path.join(E.WORK, 'pdf7')
        subprocess.run(['pdftoppm', '-f', '7', '-l', '7', '-r', '300', '-png', pdf, tmp], check=True)
        from PIL import Image
        src = glob.glob(tmp + '*.png')[0]
        im = Image.open(src)
        W_, H_ = im.size
        # ページ幅 1102 基準で (572,372)-(735,492) 付近
        k = W_ / 1102
        im.crop((int(572 * k), int(372 * k), int(735 * k), int(490 * k))).save(out)
        new = Image.open(src)
        k2 = W_ / 1102
        new.crop((int(163 * k2), int(372 * k2), int(305 * k2), int(478 * k2))).save(os.path.join(E.WORK, 'newcv.png'))
    return gl.image(out), gl.image(os.path.join(E.WORK, 'newcv.png'))


# ---------------------------------------------------------------- 共通の道具
def blink_at(t, seed=0, period=3.4):
    """自然なまばたき（周期に揺らぎ、1回 0.16 秒）"""
    k = math.floor((t + seed * 1.37) / period)
    ph = E.hash11(k + seed * 31) * (period - 0.4)
    tb = k * period - seed * 1.37 + ph + 0.2
    d = t - tb
    if 0 <= d < 0.16:
        x = d / 0.16
        return math.sin(math.pi * x) ** 0.7
    return 0.0


def char_H(cx, cy, zoom, sx=960, sy=540, rot=0.0):
    return E.m3_translate(sx, sy) @ E.m3_rot(rot) @ E.m3_scale(zoom) @ E.m3_translate(-cx, -cy) @ E.m3_scale(2048)


def sim_for(key, pose_fn, t0):
    s = C.sims.get(key)
    if s is None:
        s = R.Sim(pose_fn, FPS, 4, t0)
        C.sims[key] = s
    return s


def draw_char(key, t, t0, pose_fn, Hm, look=None):
    s = sim_for(key, pose_fn, t0)
    st = s.state(t)
    u = C.ch.uniforms(st['pose'], t, st)
    C.ch.draw(Hm, u, look)


def base_pose(t, wind=0.0, head=0.0, expr=(1, 0, 0, 0), sing=False, seed=0, typing=0.0, blink=None):
    p = R.Pose()
    p.head = head + 1.2 * math.sin(t * 1.1 + seed)
    p.head_shift = (4 * math.sin(t * 0.7 + seed), 2.5 * math.sin(t * 1.3 + seed))
    p.breath = 0.005 * math.sin(t * 2 * math.pi / 3.4 + seed)
    p.blink = blink_at(t, seed) if blink is None else blink
    p.wind = wind
    p.expr_full = expr
    p.expr_face = expr
    if sing:
        op, wd = C.vocal.mouth(t)
        p.mouth, p.wide = op, wd
    if typing > 0:
        p.handL = (0, -7 * typing * max(0, math.sin(t * 19)) ** 3)
        p.handR = (0, -7 * typing * max(0, math.sin(t * 16 + 1.3)) ** 3)
    return p


def sky_layer(t, preset, cam=(0, 0), cloud_y=860, scroll=12.0, n=6, scale=0.55, haze=0.35, seed=0, drift=1.0, alpha=1.0):
    """空＋積雲を奥から手前へ。cam は視差用の画面移動（px）"""
    gl = C.gl
    fx.sky(gl, preset, t=t, cam=(cam[0] / 1920, cam[1] / 1080))
    pal = clouds.PALETTES[preset]
    rng = np.random.default_rng(seed)
    for i in range(n):
        depth = 0.25 + 0.75 * i / max(n - 1, 1)
        tex = C.cl.cu[(i + seed) % len(C.cl.cu)]
        w, h = tex.size
        s = scale * (0.5 + 0.9 * depth)
        x = (rng.uniform(-200, 2100) + t * scroll * depth * drift + cam[0] * depth) % 2600 - 350
        y = cloud_y + 120 * depth + cam[1] * depth
        C.cl.draw(tex, gl.rect_h(x, y, w * s, h * s, anchor=(0.5, 1.0)), pal, haze=haze * (1 - depth), alpha=alpha)


def fog_bottom(col=(1, 1, 1), alpha=1.0, y0=820, y1=1080):
    fx.grad(C.gl, 960, (y0 + y1) / 2, 2200, y1 - y0, (*col, 0.0), (*col, alpha))


def karaoke(t):
    """歌詞（歌った部分の色が変わる）"""
    gl = C.gl
    ts = remap(t)
    started = [i for i, l in enumerate(C.lyrics) if l['start'] - 0.35 <= ts]
    cur_i = started[-1] if started else -1
    for li, ln in enumerate(C.lyrics):
        if li != cur_i:
            continue
        if ln['start'] - 0.35 <= ts <= ln['end'] + 0.45:
            if ln['text'] in ('（いこう！）',):
                continue
            a = min(prog(ts, ln['start'] - 0.35, ln['start'] - 0.1), 1 - prog(ts, ln['end'] + 0.15, ln['end'] + 0.45))
            done = sum(1 for m in ln['moras'] if m['t'] <= ts)
            cur = [m for m in ln['moras'] if m['t'] <= ts]
            frac = 0.0
            if cur:
                m = cur[-1]
                frac = (done - 1 + clamp((ts - m['t']) / max(m['dur'], 0.05))) / len(ln['moras'])
            base = gl.text(ln['text'], 46, fam='noto', weight='Black', color=(1, 1, 1), stroke=5, stroke_color=E.hexc('3a1a5e'))
            sung = gl.text(ln['text'], 46, fam='noto', weight='Black', color=E.hexc('ffd6f0'), stroke=5, stroke_color=E.hexc('c2185b'))
            w, h = gl.vsize(base)
            Hm = gl.rect_h(960, 1010, w, h)
            gl.draw(KARAOKE_FS, Hm, dict(frac=frac, alpha=a), dict(t0=base, t1=sung))


KARAOKE_FS = E.HEADER + r'''
uniform sampler2D t0, t1; uniform float frac, alpha;
void main(){
    vec4 a = texture(t0, uv), b = texture(t1, uv);
    float k = smoothstep(frac + 0.004, frac - 0.004, uv.x);
    f = mix(a, b, k)*alpha;
}
'''


CREDITS = [
    # (開始, 終了, 行, 位置)
    (16.6, 19.2, ['主題歌', '「Creative Vision 10！ ～明日へ飛べ～」'], 'lt'),
    (19.8, 22.4, ['歌', 'VOICEVOX:四国めたん'], 'lt'),
    (29.4, 32.0, ['コーラス', 'VOICEVOX:波音リツ　VOICEVOX:小夜/SAYO　VOICEVOX:九州そら'], 'lt'),
    (32.6, 35.2, ['作画・撮影・編集', 'Python + GLSL（すべてコードで描画・動画生成AI不使用）'], 'lt'),
]


def credits(t):
    gl = C.gl
    for (a, b, lines, pos) in CREDITS:
        if a <= t <= b:
            al = min(prog(t, a, a + 0.4), 1 - prog(t, b - 0.4, b))
            y = 70 if pos[1] == 't' else 860
            for i, s in enumerate(lines):
                sz = 22 if i == 0 else 32
                tex = gl.text(s, sz, fam='noto', weight='Bold', color=(1, 1, 1), stroke=3, stroke_color=(0.1, 0.05, 0.2))
                w, h = gl.vsize(tex)
                x = 70 if pos[0] == 'l' else 1850 - w
                gl.sprite(tex, x, y + i * 40, anchor=(0, 0), alpha=al * (0.85 if i == 0 else 1.0))


def flash(t, t0, dur=0.25, col=(1, 1, 1)):
    k = pulse(t, t0, 0.02, dur)
    return dict(flash=k, flashCol=col)


# ---------------------------------------------------------------- ショット
def s_predawn(t, lt, d):
    gl = C.gl
    cam_y = lerp(-140, 40, ease_io(lt / d))
    sky_layer(t, 'night', cam=(0, cam_y), cloud_y=980, scroll=10, n=6, scale=0.6, haze=0.3, seed=3)
    # 地平線の白み
    fx.grad(gl, 960, 900, 2200, 500, (0.9, 0.5, 0.6, 0.0), (1.0, 0.65, 0.55, 0.35 * prog(lt, 0, d)), blend='add')
    # 流れ星
    k = prog(lt, 1.6, 2.4)
    if 0 < k < 1:
        x, y = lerp(1500, 600, k), lerp(120, 380, k)
        for j in range(24):
            q = j / 24
            fx.parts(gl, [x + 900 * q * 0.09], [y - 260 * q * 0.09], [10 * (1 - q) + 3], (0.8, 0.9, 1.0), [(1 - q) * (1 - k) * 1.2], kind=0)
    rng = np.random.default_rng(5)
    n = 70
    xs = rng.uniform(0, 1920, n); ys = (rng.uniform(0, 1080, n) - t * 14) % 1080
    fx.parts(gl, xs, ys, rng.uniform(3, 9, n), (0.8, 0.85, 1.0), rng.uniform(0.2, 0.7, n) * (0.5 + 0.5 * np.sin(t * 3 + xs)), kind=0)
    title = gl.text('CREATIVE VISION 10  PROMOTION ANIME', 22, fam='segoe', weight=None, color=(0.85, 0.85, 1.0), tracking=0.4)
    gl.sprite(title, 960, 560, alpha=min(prog(lt, 0.6, 1.3), 1 - prog(lt, 2.6, 3.2)) * 0.9)
    return dict(fade=1 - prog(lt, 0, 0.8), bloomAmt=0.5, vign=0.45)


def s_circle(t, lt, d):
    gl = C.gl
    gl.use(gl.main)
    fx.sky(gl, 'night', t=t, cam=(0, 0.1))
    fx.vignette(gl, 0.7, inner=0.2)
    p = ease_out(prog(lt, 0.0, 2.7), 2)
    cam = E.Cam3(eye=(0, 3.2 + 0.4 * lt, -6.5 + 0.5 * lt), target=(0, 0, 0), fov=42, roll=0.05 * math.sin(lt))
    spin = lt * 0.05 + p * 0.15
    R3 = E.rot_y(lt * 0.25)
    H_, z = cam.quad_h((0, 0, 0), R3 @ np.array([1, 0, 0.0]), R3 @ np.array([0, 0, -1.0]), 7.5, 7.5)
    glow = 1.0 + 3.0 * prog(lt, 2.6, 3.2) ** 2
    C.mg.draw(H_, prog=p, spin=spin, glow=glow)
    # 描き進む先端から火花
    a = p * 2 * math.pi - math.pi / 2
    tip = cam.project(np.array([math.cos(a) * 3.67, 0, -math.sin(a) * 3.67]) @ np.eye(3) @ R3.T)[0]
    rng = np.random.default_rng(int(t * 30))
    if p < 0.99:
        n = 40
        fx.parts(gl, tip[0] + rng.normal(0, 30, n), tip[1] + rng.normal(0, 30, n) - rng.uniform(0, 60, n),
                 rng.uniform(4, 14, n), (0.8, 0.7, 1.0), rng.uniform(0.3, 1.0, n), kind=1)
    # 浮き上がる光の粒
    rng = np.random.default_rng(11)
    n = 160
    ang = rng.uniform(0, 2 * math.pi, n); rad = rng.uniform(0.5, 3.7, n)
    hgt = (rng.uniform(0, 3, n) + lt * rng.uniform(0.2, 0.6, n)) % 3.0
    pts = np.stack([np.cos(ang) * rad, hgt, np.sin(ang) * rad], -1) @ R3.T
    scr, zz = cam.project(pts)
    fx.parts(gl, scr[:, 0], scr[:, 1], 40 / zz * 6, (0.75, 0.6, 1.0), (1 - hgt / 3) * p * 0.8, kind=0)
    return dict(bloomAmt=0.9, vign=0.4, **flash(t, 6.6 - 0.001, 0.0))


def s_reveal(t, lt, d):
    gl = C.gl
    k_light = smooth(prog(lt, 1.2, 2.2))
    cam_y = lerp(1120, 900, ease_io(lt / d))
    zoom = lerp(0.62, 0.72, ease_io(lt / d))
    sky_layer(t, 'sunrise', cam=(0, 60 - 40 * lt / d), cloud_y=980, scroll=20, n=7, scale=0.65, haze=0.4, seed=1)
    # 太陽の光芒
    fx.god_rays(gl, 0.72, 0.62, amount=1.0 * k_light + 0.4, decay=0.975)

    def pose(tt):
        p = base_pose(tt, wind=26 + 8 * math.sin(tt * 1.7), head=-2.0, seed=2, blink=1.0 if tt < 9.0 else None)
        if tt >= 9.0:
            p.blink = max(0.0, 1 - smooth((tt - 9.0) / 0.35))
        return p
    Hm = char_H(1040, cam_y, zoom, sx=820, sy=560)
    sil = 1 - k_light
    look = dict(silhouette=1.0 * sil, silCol=E.hexc('1c0f2e'), rimCol=(2.2, 1.5, 0.8), rimDir=(-1.0, 0.25),
                rimW=6.0 + 6 * sil, cropBottom=1650, outlineW=0.0, tintMul=(0.95, 0.9, 0.92))
    draw_char('reveal', t, 6.6 - 0.5, pose, Hm, look)
    fog_bottom(E.hexc('c86f86'), 0.9, 780, 1080)
    # 光のフレア
    fx.parts(gl, [1380], [670], [900 * (0.4 + k_light)], (1.0, 0.8, 0.6), [0.35 + 0.4 * k_light], kind=0)
    rng = np.random.default_rng(3)
    n = 50
    fx.parts(gl, (rng.uniform(0, 1920, n) + t * 60) % 1920, rng.uniform(0, 1080, n), rng.uniform(4, 12, n),
             (1, 0.9, 0.8), rng.uniform(0.2, 0.7, n), kind=0)
    return dict(bloomAmt=0.8, vign=0.35, **flash(t, 6.6, 0.35))


def s_beats(t, lt, d):
    """拍ごとの寄りカット"""
    gl = C.gl
    i = int(lt / 0.4)
    k = (lt % 0.4) / 0.4

    def pose(tt):
        return base_pose(tt, wind=30, seed=4, blink=0.0)
    if i == 0:   # リボン
        sky_layer(t, 'sunrise', cam=(0, 0), cloud_y=1000, n=4, seed=2)
        Hm = char_H(470 + 30 * k, 330, 1.25 + 0.1 * k, rot=0.06)
        draw_char('beats', t, 9.8 - 0.4, pose, Hm)
    elif i == 1:  # イヤリング
        fx.burst(gl, 0.7, 0.5, rot=t * 0.3, n=18, c1=E.hexc('f6c9e6'), c2=E.hexc('fbe3f2'), c3=(1, 1, 1))
        Hm = char_H(1460, 1000, 2.0 + 0.15 * k, sx=1100, rot=-0.1)
        draw_char('beats', t, 9.8 - 0.4, pose, Hm)
    elif i == 2:  # 瞳
        Hm = char_H(1180 - 20 * k, 860, 2.6 + 0.2 * k, sx=960)
        draw_char('beats', t, 9.8 - 0.4, pose, Hm)
        fx.parts(gl, [1060 + 40 * k], [700], [220 * (0.5 + pulse(lt, 0.85, 0.02, 0.2))], (1, 1, 1), [1.0], kind=1)
    else:        # 手元
        fx.sky(gl, 'night')
        Hm = char_H(980, 1620, 1.25 + 0.1 * k, sx=960, sy=520)
        C.ch.plate(Hm, 'N', tint=(0.55, 0.6, 0.9, 1))
        draw_char('beats', t, 9.8 - 0.4, lambda tt: base_pose(tt, typing=1.6, seed=4), Hm, dict(tintMul=(0.75, 0.8, 1.05), rimCol=(0.6, 0.8, 1.2), rimDir=(0, -1), rimW=6))
        fx.focus_lines(gl, inner=0.42, seed=int(t * 12), amount=0.5)
    return dict(bloomAmt=0.5, vign=0.3, **flash(t, 9.8 + i * 0.4, 0.12))


def title_card(t, lt, d, y=0.0, alpha=1.0):
    gl = C.gl
    fx.parts(gl, [960], [600 + y], [1500], E.hexc('1a0c38'), [0.55 * alpha], kind=0, blend='over')
    # エンブレム（暗い配色のロゴを明るく起こして白い光を添える）
    ka = ease_out_back(prog(lt, 0.0, 0.5))
    gl.sprite(C.logo, 960, 400 + y, scale=0.66 * (0.6 + 0.4 * ka), alpha=alpha * prog(lt, 0, 0.2) * 0.8, tint=(0, 0, 0), add=(1.6, 1.0, 2.2), blend='add')
    gl.sprite(C.logo, 960, 400 + y, scale=0.62 * (0.6 + 0.4 * ka), alpha=alpha * prog(lt, 0, 0.2), tint=(0.35, 0.3, 0.45), add=(0.75, 0.72, 0.9))
    tt = gl.text('Creative Vision 10', 120, fam='segoeb', weight=None, color=(1, 1, 1), stroke=6, stroke_color=E.hexc('3b1a6e'))
    gl.sprite(tt, 960, 690 + y + 40 * (1 - ease_out(prog(lt, 0.15, 0.55))), alpha=alpha * prog(lt, 0.15, 0.4))
    st = gl.text('～ 明日へ飛べ ～', 60, fam='noto', weight='Black', color=(1, 1, 1), stroke=7, stroke_color=E.hexc('4a1a6a'))
    gl.sprite(st, 960, 800 + y, alpha=alpha * prog(lt, 0.5, 0.9))


def s_title(t, lt, d):
    gl = C.gl
    sky_layer(t, 'dawn', cam=(0, 0), cloud_y=1180, scroll=25, n=6, scale=0.7, haze=0.3, seed=6)
    fx.god_rays(gl, 0.62, 0.86, amount=0.8)
    rng = np.random.default_rng(4)
    n = 90
    fx.parts(gl, rng.uniform(0, 1920, n), (rng.uniform(0, 1080, n) - lt * 120) % 1080, rng.uniform(6, 26, n), (1, 0.92, 0.75),
             rng.uniform(0.3, 1.0, n), kind=1)
    title_card(t, lt, d)
    return dict(bloomAmt=0.7, vign=0.3, **flash(t, 11.4, 0.3))


def night_room(t, lt, d, zoom, cx, cy, sx=960, sy=540, key='room', sing=False, look_extra=None, typing=1.0):
    gl = C.gl
    fx.sky(gl, 'night')
    Hm = char_H(cx, cy, zoom, sx, sy)
    C.ch.plate(Hm, 'N', tint=(0.42, 0.48, 0.78, 1.0))
    look = dict(tintMul=(0.62, 0.68, 0.95), tintAdd=(0.0, 0.02, 0.06), rimCol=(0.5, 0.75, 1.3), rimDir=(-1, 0.4), rimW=8)
    if look_extra:
        look.update(look_extra)
    draw_char(key, t, t - lt - 0.5, lambda tt: base_pose(tt, typing=typing, seed=5, sing=sing), Hm, look)
    return Hm


def s_firstcommit(t, lt, d):
    gl = C.gl
    z = lerp(0.96, 1.04, ease_io(lt / d))
    night_room(t, lt, d, z, 1010, 1130, key='fc')
    # 雪
    rng = np.random.default_rng(8)
    n = 90
    xs = (rng.uniform(0, 1920, n) + np.sin(t * 0.8 + rng.uniform(0, 6, n)) * 30) % 1920
    ys = (rng.uniform(0, 1080, n) + t * rng.uniform(40, 120, n)) % 1100 - 10
    sz = rng.uniform(6, 30, n)
    fx.parts(gl, xs, ys, sz, (0.8, 0.88, 1.0), 0.18 + 0.25 * (sz / 30), kind=0, blend='add')
    # 端末の文字（最初のコミット）
    date, subj = C.commits[0]
    lines = ['$ git init', f'$ git commit -m "{subj}"', f'  {date}   30 files changed']
    for i, s in enumerate(lines):
        k = prog(lt, 0.3 + i * 0.7, 0.9 + i * 0.7)
        if k <= 0:
            continue
        n_ch = int(len(s) * k)
        tex = gl.text(s[:n_ch] + ('▌' if k < 1 else ''), 34, fam='mono', weight=None, color=E.hexc('9fe6ff'))
        gl.sprite(tex, 120, 150 + i * 56, anchor=(0, 0.5), alpha=0.95)
    tex = gl.text('2025.11.25', 92, fam='segoeb', weight=None, color=(1, 1, 1))
    gl.sprite(tex, 1800, 150, anchor=(1, 0.5), alpha=prog(lt, 1.6, 2.2) * 0.9)
    return dict(bloomAmt=0.55, vign=0.5, lift=(0.02, 0.02, 0.06))


def s_codeface(t, lt, d):
    gl = C.gl
    z = lerp(1.15, 1.25, lt / d)
    look = dict(tProj=C.code, projAmt=0.55, projCol=(0.6, 0.9, 1.2), projXf=(1.3, 0.55, 0.0, lt * 0.08))
    night_room(t, lt, d, z, 1080, 980, sx=1060, sy=560, key='cf', sing=True, look_extra=look, typing=0.0)
    rng = np.random.default_rng(12)
    n = 26
    fx.parts(gl, (rng.uniform(-200, 2100, n) + t * 25) % 2300 - 200, rng.uniform(0, 1080, n), rng.uniform(60, 180, n),
             (0.7, 0.8, 1.0), rng.uniform(0.04, 0.12, n), kind=5)
    return dict(bloomAmt=0.6, vign=0.55, lift=(0.02, 0.02, 0.07), ca=2.0)


def s_tower(t, lt, d):
    """実コミットの件名が螺旋に積み上がる。上端が最新、数字は実件数"""
    gl = C.gl
    fx.sky(gl, 'night', t=t, cam=(0, -lt * 0.05))
    n_show = max(1, int(lerp(1, C.n_commits, ease_in(prog(lt, 0.15, d - 0.25), 2.2))))
    ang = 0.6 + lt * 0.32
    cam = E.Cam3(eye=(math.sin(ang) * 7.2, 1.2, -math.cos(ang) * 7.2), target=(0, -0.6, 0), fov=50, roll=0.04)
    K = 64
    items = []
    for j in range(max(0, n_show - K), n_show):
        age = n_show - 1 - j               # 0 が最新
        y = -age * 0.16
        a = j * 0.45
        r = 3.0
        c = np.array([math.cos(a) * r, y, math.sin(a) * r])
        right = np.array([-math.sin(a), 0, math.cos(a)])
        items.append((cam.to_cam(c)[2], j, age, c, right))
    for zc, j, age, c, right in sorted(items, key=lambda q: -q[0]):
        date, subj = C.commits[j]
        tex = gl.text(f'{date}  {subj[:24]}', 30, fam='noto', weight='Bold', color=(0.92, 0.97, 1.0))
        w, h = gl.vsize(tex)
        # 文字は常にカメラの右向きへ（少しだけ螺旋の接線へ傾けて立体感を出す）
        rr = cam.R[0] + 0.35 * right * np.sign(np.dot(right, cam.R[0]) + 1e-6)
        rr = rr / np.linalg.norm(rr)
        H_, z = cam.quad_h(c, rr, np.array([0, -1.0, 0]), w / 150, h / 150)
        if H_ is None:
            continue
        fade = clamp(1.0 - age / K) ** 0.8
        new = math.exp(-age * 0.6)
        gl.draw(BRICK_FS, H_, dict(alpha=fade, glow=new), dict(tex=tex), blend='add')
    # 中心の光の柱と上へ昇る粒
    ys = np.linspace(-11, 0.5, 60)
    scr, zz = cam.project(np.stack([np.zeros_like(ys), ys, np.zeros_like(ys)], -1))
    fx.parts(gl, scr[:, 0], scr[:, 1], 260 / zz * 3, (0.45, 0.55, 1.0), np.full(60, 0.1), kind=0)
    rng = np.random.default_rng(3)
    n = 120
    pa = rng.uniform(0, 2 * math.pi, n); pr = rng.uniform(0.3, 3.6, n); py = (rng.uniform(-10, 1, n) + lt * 2.0) % 11 - 10
    scr, zz = cam.project(np.stack([np.cos(pa) * pr, py, np.sin(pa) * pr], -1))
    fx.parts(gl, scr[:, 0], scr[:, 1], 60 / zz * 2, (0.7, 0.8, 1.0), np.full(n, 0.6), kind=1)
    cnt = gl.text(f'{n_show:,}', 130, fam='segoeb', weight=None, color=(1, 1, 1), stroke=4, stroke_color=E.hexc('1b2a6e'))
    gl.sprite(cnt, 1830, 800, anchor=(1, 0.5))
    lab = gl.text('commits  since 2025.11.25', 34, fam='segoe', weight=None, color=E.hexc('b8c8ff'))
    gl.sprite(lab, 1830, 880, anchor=(1, 0.5))
    return dict(bloomAmt=0.8, bloomThr=0.9, vign=0.45)


def s_gather(t, lt, d):
    gl = C.gl
    sky_layer(t, 'night', cam=(0, 0), cloud_y=1080, n=5, seed=4, scroll=8)
    z = lerp(0.78, 0.86, ease_io(lt / d))

    def pose(tt):
        p = base_pose(tt, wind=10, head=-3.0 * smooth(prog(tt, 23.6, 24.4)), seed=6, sing=True)
        k = smooth(prog(tt, 23.4, 24.0))
        p.expr_face = (1 - k, k, 0, 0); p.expr_full = p.expr_face
        return p
    Hm = char_H(1050, 980, z, sx=900, sy=560)
    glow = smooth(prog(lt, 0.3, 2.5))
    look = dict(tintMul=(0.75 + 0.25 * glow, 0.75 + 0.2 * glow, 1.0), rimCol=(0.9, 0.8, 1.4), rimDir=(0.3, 1.0), rimW=9,
                tintAdd=(0.05 * glow, 0.03 * glow, 0.08 * glow), cropBottom=1720)
    draw_char('gather', t, 22.6 - 0.5, pose, Hm, look)
    fog_bottom((0.15, 0.12, 0.35), 0.95, 820, 1080)
    # 光のかけらが四方から手元へ
    rng = np.random.default_rng(21)
    n = 400
    a0 = rng.uniform(0, 2 * math.pi, n)
    r0 = rng.uniform(700, 1300, n)
    ph = (rng.uniform(0, 1, n) + lt * 0.45) % 1.0
    r = r0 * (1 - ph ** 2)
    a = a0 + ph * 2.0
    cx, cy = 900, 930
    xs, ys = cx + np.cos(a) * r, cy + np.sin(a) * r * 0.6
    fx.parts(gl, xs, ys, rng.uniform(6, 22, n) * (0.4 + ph), E.hexc('ffd9f5'), (0.3 + 0.7 * ph) * glow, kind=1)
    fx.parts(gl, [cx], [cy], [500 * glow + 50], (1.0, 0.8, 1.0), [0.5 * glow], kind=0)
    return dict(bloomAmt=0.9, vign=0.4)


def s_baton(t, lt, d):
    """CV.net（古いフィルム調）から CV10 へ、光のバトンが渡る"""
    gl = C.gl
    old, new = C.oldcv
    cam = E.Cam3(eye=(lerp(-0.6, 0.6, ease_io(lt / d)), 0.2, -6.0), target=(0, 0, 0), fov=42)
    fx.rect(gl, 960, 540, 1920, 1080, E.hexc('140b26'))
    # 旧（左奥）と新（右手前）を立体に置く
    jit = (E.hash11(int(t * 24)) - 0.5) * 0.01
    Ho, zo = cam.quad_h(np.array([-2.15, 0.25 + jit, 0.8]), np.array([0.94, 0, 0.34]), np.array([0, -1.0, 0]), 3.6, 3.6 * old.size[1] / old.size[0])
    gl.draw(OLDFILM_FS, Ho, dict(alpha=1.0, seed=int(t * 24)), dict(tex=old))
    reveal = smooth(prog(lt, 1.5, 2.3))
    Hn, zn = cam.quad_h(np.array([2.0, 0.25, -0.3]), np.array([0.94, 0, -0.34]), np.array([0, -1.0, 0]), 3.8, 3.8 * new.size[1] / new.size[0])
    fx.window(gl, new, Hn, appear=reveal, alpha=1.0, seed=3, edge=(0.8, 0.6, 1.0), bright=1.05)
    for i, s in enumerate(['CV.net', 'Biz/Browser V  ·  .NET 2.0  ·  Oracle 11g']):
        tex = gl.text(s, 60 if i == 0 else 28, fam='segoeb' if i == 0 else 'segoe', weight=None, color=E.hexc('f3dcb0'))
        gl.sprite(tex, 520, 820 + i * 60, alpha=0.9)
    for i, s in enumerate(['Creative Vision 10', 'WPF  ·  .NET 10  ·  gRPC  ·  SQLite']):
        tex = gl.text(s, 60 if i == 0 else 28, fam='segoeb' if i == 0 else 'segoe', weight=None, color=(1, 1, 1))
        gl.sprite(tex, 1420, 820 + i * 60, alpha=reveal)
    # 光のバトン（彗星）: 旧画面から新画面へ弧を描いて渡る
    kb = ease_io(prog(lt, 0.5, 1.7))
    p0, _ = cam.project(np.array([-2.15, 0.25, 0.8])); p1, _ = cam.project(np.array([2.0, 0.25, -0.3]))
    if 0 < kb <= 1:
        trail = 40
        qs = np.clip(kb - np.arange(trail) * 0.012, 0, 1)
        xs = p0[0] + (p1[0] - p0[0]) * qs
        ys = p0[1] + (p1[1] - p0[1]) * qs - np.sin(qs * math.pi) * 260
        fx.parts(gl, xs, ys, 90 * (1 - np.arange(trail) / trail) + 12, (1.0, 0.82, 0.45), (1 - np.arange(trail) / trail) * 1.2, kind=0)
        fx.parts(gl, xs[:1], ys[:1], [260], (1.0, 0.9, 0.7), [1.0], kind=1)
    hit = pulse(lt, 1.7, 0.01, 0.5)
    if hit > 0:
        fx.parts(gl, [p1[0]], [p1[1]], [1400 * (1.2 - hit)], (1.0, 0.85, 0.6), [hit], kind=0)
    rng = np.random.default_rng(5)
    n = 60
    fx.parts(gl, rng.uniform(1000, 1920, n), (rng.uniform(0, 1080, n) - lt * 80) % 1080, rng.uniform(5, 16, n), (0.9, 0.8, 1.0),
             rng.uniform(0.2, 0.8, n) * reveal, kind=1)
    return dict(bloomAmt=0.7, vign=0.45)


def lanterns(t, n=200, cam_tilt=0.0, alpha=1.0, seed=31, blur=False):
    gl = C.gl
    rng = np.random.default_rng(seed)
    depth = rng.uniform(0.15, 1.0, n)
    x = rng.uniform(-100, 2020, n)
    y0 = rng.uniform(300, 1500, n)
    sp = rng.uniform(20, 55, n)
    y = y0 - t * sp * depth - cam_tilt * depth
    y = (y + 400) % 1900 - 400
    x = x + np.sin(t * 0.5 + y0) * 15 * depth
    size = 70 * depth ** 1.5 + 6
    col = np.array(E.hexc('ffb35c'))
    order = np.argsort(depth)
    if blur:
        fx.parts(gl, x[order], y[order], size[order] * 2.6, col, (0.25 + 0.3 * depth[order]) * alpha, kind=0)
    else:
        fx.parts(gl, x[order], y[order], size[order], col, (0.55 + 0.45 * depth[order]) * alpha, kind=4)
    fx.parts(gl, x[order], y[order], size[order] * 4, (1.0, 0.55, 0.2), 0.12 * alpha * depth[order], kind=0)


def s_lanterns(t, lt, d):
    gl = C.gl
    tilt = lerp(0, 380, ease_io(lt / d))
    fx.sky(gl, 'night', t=t, cam=(0, -tilt / 1080))
    fx.grad(gl, 960, 900 + tilt * 0.3, 2200, 600, (0.2, 0.1, 0.25, 0.0), (0.6, 0.25, 0.2, 0.6), blend='add')
    lanterns(t, 200, tilt)
    k = prog(lt, 0.6, 1.4) * (1 - prog(lt, 2.7, 3.1))
    tex = gl.text('200', 200, fam='mincho', weight=None, color=E.hexc('ffe7b8'))
    gl.sprite(tex, 960, 470, alpha=k, scale=1 + 0.05 * lt)
    tex = gl.text('社の笑顔とともに — CV.net 導入実績（延べ）', 34, fam='mincho', weight=None, color=E.hexc('ffe7b8'))
    gl.sprite(tex, 960, 610, alpha=k * 0.9)
    return dict(bloomAmt=0.9, vign=0.5)


def s_connect(t, lt, d):
    gl = C.gl
    roll = 0.07 * math.sin(lt * 0.8) - 0.05
    fx.sky(gl, 'night', t=t, cam=(0, -0.3))
    lanterns(t + 3.2, 120, 380, alpha=0.9, blur=True)
    z = lerp(0.95, 1.12, ease_io(lt / d))
    Hm = char_H(1050, 960, z, sx=980, sy=590, rot=roll)
    look = dict(tintMul=(0.85, 0.78, 0.8), tintAdd=(0.06, 0.03, 0.0), rimCol=(1.4, 0.8, 0.4), rimDir=(0.2, 1.0), rimW=8, cropBottom=1560)
    draw_char('connect', t, 32.2 - 0.5, lambda tt: base_pose(tt, wind=8, seed=7, sing=True, head=-1.5), Hm, look)
    fog_bottom((0.25, 0.1, 0.12), 0.95, 860, 1080)
    lanterns(t + 7.7, 14, 380, alpha=0.6, seed=77, blur=True)
    return dict(bloomAmt=0.8, vign=0.45)


def s_build(t, lt, d):
    gl = C.gl
    i = int(lt / 0.4)
    k = (lt % 0.4) / 0.4
    shake = (math.sin(t * 90) * 6, math.cos(t * 77) * 6) if i >= 6 else (0, 0)
    if i in (0, 4):
        fx.sky(gl, 'night')
        cam = E.Cam3(eye=(0, 4.5, -5.0), target=(0, 0, 0), fov=50, roll=0.2 * i)
        H_, z = cam.quad_h((0, 0, 0), np.array([math.cos(t * 2), 0, math.sin(t * 2)]), np.array([-math.sin(t * 2), 0, math.cos(t * 2)]), 7, 7)
        C.mg.draw(H_, prog=1.0, spin=t * 0.4, glow=2.0)
    elif i in (1, 5):
        sky_layer(t, 'dawn', n=4, seed=9)
        Hm = char_H(1180 if i == 1 else 860, 880, 2.4 + 0.3 * k, sx=960 + shake[0], sy=540 + shake[1])
        draw_char('build', t, 35.4 - 0.5, lambda tt: base_pose(tt, wind=20, seed=8, blink=0.0), Hm)
    elif i == 2:
        sky_layer(t, 'dawn', n=4, seed=9)
        Hm = char_H(560, 300, 1.4 + 0.2 * k, rot=-0.08)
        draw_char('build', t, 35.4 - 0.5, lambda tt: base_pose(tt, wind=40, seed=8), Hm)
    elif i == 3:
        fx.rect(gl, 960, 540, 1920, 1080, E.hexc('0b0f2a'))
        for j in range(5):
            s = C.screens[(j + int(t * 10)) % len(C.screens)]
            gl.sprite(s, 200 + j * 380 + k * 80, 540 + (j % 2) * 60 - 30, scale=360 / s.size[0], alpha=0.9)
        fx.focus_lines(gl, mode=1, seed=int(t * 20), amount=0.8)
    else:
        fx.burst(gl, 0.5, 0.5, rot=t, n=30, c1=E.hexc('2a1060'), c2=E.hexc('3c1a80'), c3=E.hexc('b07cff'))
        z = lerp(1.0, 3.2, ease_in(prog(lt, 2.4, 3.2), 3))
        Hm = char_H(lerp(1050, 1180, ease_in(prog(lt, 2.4, 3.2), 3)), lerp(980, 860, ease_in(prog(lt, 2.4, 3.2), 3)), z,
                    sx=960 + shake[0], sy=560 + shake[1])
        draw_char('build2', t, 37.8 - 0.5, lambda tt: base_pose(tt, wind=35, seed=8, blink=0.0), Hm, dict(cropBottom=1700))
        fx.focus_lines(gl, inner=0.3, seed=int(t * 15), amount=0.9, col=(1, 1, 1))
    wh = smooth(prog(lt, 2.9, 3.2))
    return dict(bloomAmt=0.6, vign=0.3, flash=max(wh, pulse(t, 35.4 + i * 0.4, 0.01, 0.08) * 0.6), flashCol=(1, 1, 1))


def s_chorus_title(t, lt, d):
    gl = C.gl
    fx.burst(gl, 0.5, 0.48, rot=t * 0.25, n=28, c1=E.hexc('f27bb8'), c2=E.hexc('f9a6d1'), c3=E.hexc('fff1f8'))
    z = 0.82 + 0.03 * math.sin(lt * 2)
    bump = 18 * pulse(t, 38.6, 0.01, 0.2)
    Hm = char_H(1050, 960, z, sx=960, sy=700 + bump)
    look = dict(outlineW=10.0, outlineCol=(1, 1, 1), cropBottom=1600)
    draw_char('ct', t, 38.6 - 0.5, lambda tt: base_pose(tt, wind=18, seed=9, sing=True, expr=(0, 1, 0, 0)), Hm, look)
    rng = np.random.default_rng(13)
    n = 120
    fx.parts(gl, rng.uniform(0, 1920, n), rng.uniform(0, 1080, n), rng.uniform(10, 40, n) * (0.6 + 0.4 * np.sin(t * 6 + np.arange(n))), (1, 1, 1),
             rng.uniform(0.5, 1, n), kind=1)
    fog_bottom(E.hexc('f3a9cf'), 0.95, 900, 1080)
    # 歌の各モーラに合わせて1文字ずつ落ちてくる（キャラより手前）
    word = 'CREATIVE VISION 10'
    ts = remap(t)
    ln = [l for l in C.lyrics if l['text'].startswith('Creative')][0]
    ms = ln['moras']
    letters = list(word)
    times = np.linspace(ms[0]['t'] - 0.05, ms[-1]['t'] + 0.1, len(letters))
    texs = [gl.text(ch, 124, fam='segoeb', weight=None, color=(1, 1, 1), stroke=8, stroke_color=E.hexc('7b1fa2')) if ch != ' ' else None
            for ch in letters]
    widths = [gl.vsize(tx)[0] * 0.78 if tx else 40 for tx in texs]
    x = 960 - sum(widths) / 2
    for i, (ch, tx, w) in enumerate(zip(letters, texs, widths)):
        cxl = x + w / 2
        x += w
        if tx is None:
            continue
        k = prog(ts, times[i], times[i] + 0.28)
        if k <= 0:
            continue
        yy = 150 - 300 * (1 - ease_out_back(k, 2.0))
        sh = 6 * pulse(ts, times[i] + 0.12, 0.01, 0.15)
        gl.sprite(tx, cxl, yy + sh, scale=1.0, angle=(1 - k) * 0.5 * (1 if i % 2 else -1))
    return dict(bloomAmt=0.45, vign=0.15, **flash(t, 38.6, 0.4))


def s_fly(t, lt, d):
    gl = C.gl
    roll = 0.10 * math.sin(lt * 1.1 + 0.4)
    fx.sky(gl, 'day', t=t, cam=(0, roll))
    rng = np.random.default_rng(17)
    pal = clouds.PALETTES['day']
    items = []
    for j in range(30):
        z0 = rng.uniform(0, 1)
        z = (z0 - lt * 0.45) % 1.0          # 1 遠 → 0 手前
        x = rng.uniform(-1.1, 1.1); y = rng.uniform(-0.3, 0.9)
        items.append((z, x, y, j))
    def cloud_at(z, x, y, j, alpha_k=1.0):
        s = 0.25 / (z + 0.08)
        tex = C.cl.cu[j % len(C.cl.cu)]
        w, h = tex.size
        sx = 960 + x * 900 / (z + 0.15)
        sy = 600 + y * 500 / (z + 0.15)
        a = smooth(prog(1 - z, 0.0, 0.25)) * (1 - smooth(prog(1 - z, 0.88, 1.0))) * alpha_k
        Hm = E.m3_translate(960, 540) @ E.m3_rot(roll) @ E.m3_translate(-960, -540) @ gl.rect_h(sx, sy, w * s * 0.5, h * s * 0.5)
        C.cl.draw(tex, Hm, pal, haze=0.5 * z, alpha=a)
    for z, x, y, j in sorted(items, key=lambda q: -q[0]):
        if z > 0.22:
            cloud_at(z, x, y, j)
    fx.focus_lines(gl, inner=0.55, seed=int(t * 15), amount=0.35, col=(1, 1, 1))
    # 左下から飛び込んで中央で浮かぶ
    enter = ease_out(prog(lt, 0.0, 0.9), 3)
    bob = 14 * math.sin(lt * 3.0)
    sx = lerp(560, 980, enter) + 16 * math.sin(lt * 1.7)
    sy = lerp(900, 610, enter) + bob
    Hm = char_H(1050, 960, lerp(0.95, 0.8, enter), sx=sx, sy=sy, rot=roll * 0.6 + 0.18 * (1 - enter))
    look = dict(cropBottom=1600, rimCol=(1.2, 1.2, 1.3), rimDir=(0.5, -1), rimW=6)
    draw_char('fly', t, 41.8 - 0.5, lambda tt: base_pose(tt, wind=-60 + 12 * math.sin(tt * 4), seed=10, sing=True, expr=(0, 1, 0, 0)), Hm, look)
    fog_bottom(E.hexc('cfe4ff'), 0.95, 880, 1080)
    # 手前を横切る雲（キャラにかぶさる）
    for z, x, y, j in sorted(items, key=lambda q: -q[0]):
        if z <= 0.22:
            cloud_at(z, x, y, j, 0.9)
    return dict(bloomAmt=0.4, vign=0.15, ca=3.0)



BRICK_FS = E.HEADER + r'''
uniform sampler2D tex; uniform float alpha, glow;
void main(){
    vec4 c = texture(tex, uv);
    vec2 b = min(uv, 1.0 - uv);
    float frame = smoothstep(0.03, 0.0, min(b.x*0.25, b.y));
    vec3 bg = vec3(0.12, 0.2, 0.55)*(0.35 + 0.65*glow);
    vec3 col = c.rgb*(1.0 + glow*0.8) + bg*0.5 + vec3(0.5, 0.7, 1.0)*frame*0.8 + vec3(0.9,0.95,1.0)*glow*0.25;
    f = vec4(col*alpha, 0.0);
}
'''

def s_wings(t, lt, d):
    gl = C.gl
    sky_layer(t, 'sunrise', cloud_y=1060, n=6, seed=12, scroll=14)
    fx.god_rays(gl, 0.5, 0.78, amount=0.8)
    ang = lerp(-0.18, 0.18, ease_io(lt / d))
    cam = E.Cam3(eye=(math.sin(ang) * 9, 0.4, -math.cos(ang) * 9), target=(0, 0.4, 0), fov=44)
    quads = []
    for side in (-1, 1):
        for row, (nper, Lmax, a0, a1, sz) in enumerate([(9, 5.6, -0.25, 1.05, 1.9), (8, 4.0, -0.55, 0.75, 1.5)]):
            for j in range(nper):
                q = j / (nper - 1)
                a = lerp(a0, a1, q)
                L = lerp(1.6, Lmax, 0.3 + 0.7 * math.sin(q * math.pi / 2))
                spread = smooth(prog(lt, 0.05 + q * 1.1 + row * 0.25, 0.55 + q * 1.1 + row * 0.25))
                flap = 0.22 * math.sin(lt * 2.6 + q * 1.2) * q
                base = np.array([side * 0.5, 0.3, 1.2 + row * 0.4])
                dirv = np.array([side * math.cos(a), math.sin(a), 0.0])
                c = base + dirv * L * spread + np.array([0, flap, 0.15 * q])
                right = np.array([side * math.cos(a + 0.5), math.sin(a + 0.5) * 0.6, 0.15 * side])
                right /= np.linalg.norm(right)
                if right[0] < 0:
                    right = -right       # 画面が鏡にならないよう常に右向き
                down = np.array([0, -1.0, 0.05]); down -= right * np.dot(down, right); down /= np.linalg.norm(down)
                quads.append((c, right, down, j * 2 + row * 17 + (0 if side < 0 else 40), spread, q, sz * (1 - 0.3 * q)))
    quads.sort(key=lambda r: -cam.to_cam(r[0])[2])
    for c, right, down, j, spread, q, ww in quads:
        if spread <= 0.01:
            continue
        tex = C.screens[j % len(C.screens)]
        w, h = tex.size
        H_, z = cam.quad_h(c, right, down, ww, ww * h / w)
        fx.window(gl, tex, H_, appear=spread, alpha=0.95, seed=j, edge=(1.0, 0.75, 0.95), bright=1.0)
    Hm = char_H(1050, 960, 0.62, sx=960, sy=640)
    look = dict(cropBottom=1600, outlineW=6.0, outlineCol=(1, 0.95, 1.0), outlineA=0.8)
    draw_char('wings', t, 45.0 - 0.5, lambda tt: base_pose(tt, wind=22, seed=11, sing=True, expr=(0, 1, 0, 0)), Hm, look)
    fog_bottom(E.hexc('d9819f'), 0.9, 860, 1080)
    rng = np.random.default_rng(18)
    n = 100
    fx.parts(gl, rng.uniform(0, 1920, n), (rng.uniform(0, 1080, n) + t * 50) % 1080, rng.uniform(6, 20, n), (1, 0.95, 1.0),
             rng.uniform(0.3, 0.9, n), kind=1)
    return dict(bloomAmt=0.6, vign=0.2)


def ikou_pose(tt):
    p = base_pose(tt, wind=30, seed=12)
    p.expr_full = (0, 1, 0, 0); p.expr_face = (0, 0, 0, 1)
    return p


def s_ikou(t, lt, d):
    gl = C.gl
    fx.burst(gl, 0.5, 0.55, rot=-t * 0.6, n=20, c1=E.hexc('ffd84d'), c2=E.hexc('ffe98a'), c3=(1, 1, 0.92))
    z = lerp(1.3, 0.95, ease_out(prog(lt, 0, 0.35), 4))
    Hm = char_H(1040, 980, z, sx=960, sy=600)
    look = dict(outlineW=12.0, outlineCol=(1, 1, 1), cropBottom=1600)
    draw_char('ikou', t, 48.2 - 0.5, lambda tt: ikou_pose(tt), Hm, look)
    fx.focus_lines(gl, inner=0.42, seed=int(t * 15), amount=0.9, col=E.hexc('ff7a00'))
    k = ease_out_back(prog(lt, 0.05, 0.35), 2.5)
    tex = gl.text('いこう！', 170, fam='noto', weight='Black', color=(1, 1, 1), stroke=12, stroke_color=E.hexc('e53935'))
    gl.sprite(tex, 1430, 300, scale=k, angle=-0.12)
    inv = 1.0 if 0.0 <= lt < 0.067 else 0.0
    return dict(bloomAmt=0.3, vign=0.1, invert=inv, mono=inv)


def s_gate(t, lt, d):
    """3階層（Data → Application → Presentation）を下から昇り、最上層の扉が開く"""
    gl = C.gl
    fx.sky(gl, 'night', t=t)
    fx.grad(gl, 960, 540, 1920, 1080, (0.05, 0.02, 0.15, 0.0), (0.35, 0.15, 0.55, 0.8), blend='add')
    climb = ease_io(prog(lt, 0.0, 3.6))
    cy = lerp(-2.8, 4.6, climb)
    look_up = lerp(1.6, 3.2, smooth(prog(lt, 2.6, 3.8)))
    cam = E.Cam3(eye=(1.2 * math.sin(lt * 0.6), cy, -8.5 + 2.0 * smooth(prog(lt, 3.4, 4.8))), target=(0, cy + look_up, 0), fov=56,
                 roll=0.06 * math.sin(lt * 0.9))
    layers = [('DATA', 'SQLite · PostgreSQL · MariaDB', E.hexc('8c6cff'), 0.0),
              ('APPLICATION', '.NET 10 · gRPC', E.hexc('35c8ff'), 3.0),
              ('PRESENTATION', 'WPF · MVVM', E.hexc('ff7ad9'), 6.0)]
    open_k = ease_io(prog(lt, 3.3, 4.5))
    # 扉の向こうの光（開く前から隙間に漏れる）
    p, z = cam.project(np.array([0, 6.0, 0]))
    leak = 0.15 + 0.85 * open_k
    for li, (name, sub, col, y) in enumerate(layers):
        top = li == 2
        halves = (-1, 1) if top else (0,)
        for hv in halves:
            w = 5.0 if hv else 10.0
            cx = hv * (2.5 + 5.0 * open_k) if hv else 0.0
            H_, z = cam.quad_h(np.array([cx, y, 0.0]), np.array([1.0, 0, 0]), np.array([0, 0, -1.0]), w, 7.0)
            if H_ is not None:
                gl.draw(LAYER_FS, H_, dict(col=col, alpha=1.3 + (0.6 if top else 0), halfk=float(hv), seed=y), blend='add')
        tex = gl.text(name, 120, fam='segoeb', weight=None, color=(1, 1, 1))
        w, h = gl.vsize(tex)
        lab_a = 1.0 - (open_k if top else 0)
        H_, z = cam.quad_h(np.array([-1.4, y + 0.55, -3.4]), np.array([1.0, 0, 0]), np.array([0, -1.0, 0]), w / 300, h / 300)
        if H_ is not None:
            gl.quad(tex, H_, alpha=0.95 * lab_a, tint=tuple(0.6 + 0.4 * np.array(col)))
        tex2 = gl.text(sub, 50, fam='noto', weight='Bold', color=(1, 1, 1))
        w2, h2 = gl.vsize(tex2)
        H_, z = cam.quad_h(np.array([-1.4 + w / 600 + w2 / 600 + 0.4, y + 0.42, -3.4]), np.array([1.0, 0, 0]), np.array([0, -1.0, 0]), w2 / 300, h2 / 300)
        if H_ is not None:
            gl.quad(tex2, H_, alpha=0.85 * lab_a)
        if not top:
            for j in range(3):
                tx = C.screens[(j * 5 + li * 7) % len(C.screens)]
                ww, hh = tx.size
                cc = np.array([-3.0 + j * 3.0, y + 1.0, 1.4 + (j % 2) * 0.8])
                H_, z = cam.quad_h(cc, np.array([1.0, 0, 0]), np.array([0, -1.0, 0]), 2.4, 2.4 * hh / ww)
                fx.window(gl, tx, H_, appear=1.0, alpha=0.85, seed=j + li, edge=tuple(col))
    rng = np.random.default_rng(4)
    n = 260
    px = rng.uniform(-4.5, 4.5, n); pz = rng.uniform(-3, 3, n)
    py = (rng.uniform(-1, 7, n) + lt * 3.0) % 8.0 - 1.0
    scr, zz = cam.project(np.stack([px, py, pz], -1))
    ok = zz > 0.5
    fx.parts(gl, scr[ok, 0], scr[ok, 1], 50 / zz[ok] * 3, (0.75, 0.85, 1.0), np.full(ok.sum(), 0.7), kind=0)
    # 扉の隙間から差す光
    p, z = cam.project(np.array([0, 6.2, 0]))
    fx.parts(gl, [p[0]], [p[1]], [500 + 2200 * open_k], (1.0, 0.92, 0.8), [0.4 * leak + 0.5 * open_k], kind=0)
    if open_k > 0:
        fx.god_rays(gl, p[0] / 1920, p[1] / 1080, amount=1.2 * open_k, decay=0.98, thr=0.7, focus=1.5)
    return dict(bloomAmt=0.8, bloomThr=0.9, vign=0.35, flash=smooth(prog(lt, 4.4, 4.8)) * 0.95, flashCol=(1, 1, 1))


LAYER_FS = E.HEADER + fx.NOISE + r'''
uniform vec3 col; uniform float alpha, halfk, seed;
void main(){
    vec2 p = uv;
    vec2 g = abs(fract(p*vec2(20.0, 14.0)) - 0.5);
    float grid = smoothstep(0.46, 0.5, max(g.x, g.y));
    vec2 b = min(p, 1.0 - p);
    float border = smoothstep(0.012, 0.0, min(b.x, b.y));
    float flow = smoothstep(0.9, 1.0, fract(p.x*4.0 - time*0.5 + vnoise(p*5.0 + seed)*0.8));
    float body = 0.2 + 0.1*vnoise(p*8.0 + time*0.2);
    float fade = smoothstep(0.0, 0.25, min(b.x, b.y)*3.0) * 0.6 + 0.4;
    vec3 c = col*(body*fade + grid*0.5 + border*2.0 + flow*0.35);
    f = vec4(c*alpha, 0.0);
}
'''


def s_sunrise(t, lt, d):
    """瞳の寄りから雲海の全景まで一気に引く"""
    gl = C.gl
    pull = ease_io(prog(lt, 0.6, 5.2), 3)
    sky_layer(t, 'sunrise', cam=(0, 160 * pull), cloud_y=900 + 60 * pull, scroll=10, n=9, scale=0.7, haze=0.45, seed=14, drift=0.6)
    fx.god_rays(gl, 0.66, 0.62, amount=1.2, decay=0.978)
    z = math.exp(lerp(math.log(1.9), math.log(0.36), pull))
    cx = lerp(1150, 1050, pull); cy = lerp(880, 1000, pull)
    sx = lerp(980, 700, pull); sy = lerp(560, 600, pull)
    Hm = char_H(cx, cy, z, sx=sx, sy=sy, rot=lerp(0.06, 0.0, pull))

    def pose(tt):
        p = base_pose(tt, wind=22 + 6 * math.sin(tt), seed=13, sing=True, expr=(0, 1, 0, 0))
        hold = smooth(prog(tt, 58.6, 58.9)) * (1 - smooth(prog(tt, 60.3, 60.6)))
        if hold > 0:
            p.blink = max(p.blink, hold)
        return p
    look = dict(cropBottom=1650, rimCol=(1.8, 1.3, 0.8), rimDir=(1.0, 0.1), rimW=8, tintAdd=(0.06, 0.03, 0.0),
                outlineW=4.0 * pull, outlineCol=(1, 0.95, 0.9), outlineA=0.6)
    draw_char('sunrise', t, 54.6 - 0.5, pose, Hm, look)
    # 引くにつれて手前の雲が下半身を覆う
    pal = clouds.PALETTES['sunrise']
    rng = np.random.default_rng(41)
    for j in range(5):
        tex = C.cl.cu[(j + 3) % len(C.cl.cu)]
        w, h = tex.size
        x = sx - 700 + j * 360 + rng.uniform(-60, 60) + lt * 12
        y = lerp(1500, 1180, pull) + rng.uniform(-30, 30)
        s = lerp(1.4, 0.9, pull)
        C.cl.draw(tex, gl.rect_h(x, y, w * s, h * s, anchor=(0.5, 1.0)), pal, alpha=smooth(prog(pull, 0.2, 0.6)))
    fog_bottom(E.hexc('d98a8a'), 0.9 * smooth(prog(pull, 0.0, 0.4)) + 0.3, 900, 1080)
    rng = np.random.default_rng(19)
    n = 80
    fx.parts(gl, rng.uniform(0, 1920, n), (rng.uniform(0, 1080, n) - t * 20) % 1080, rng.uniform(5, 16, n), (1, 0.95, 0.8),
             rng.uniform(0.3, 0.8, n) * (0.5 + 0.5 * np.sin(t * 2 + np.arange(n))), kind=1)
    return dict(bloomAmt=0.75, vign=0.25)


def s_logo(t, lt, d):
    gl = C.gl
    sky_layer(t, 'dawn', cam=(0, 120), cloud_y=1000, scroll=10, n=8, scale=0.7, haze=0.45, seed=14, drift=0.6)
    fx.god_rays(gl, 0.62, 0.86, amount=0.8)
    title_card(t, lt, d, y=-40)
    k = prog(lt, 1.0, 1.6)
    tex = gl.text('2027年1月 リリース予定', 44, fam='noto', weight='Bold', color=(1, 1, 1), stroke=4, stroke_color=E.hexc('6a2a6a'))
    gl.sprite(tex, 960, 880, alpha=k)
    tex = gl.text('オープンな技術基盤で進化する業務プラットフォーム', 32, fam='noto', weight='Medium', color=(1, 1, 1), stroke=3, stroke_color=E.hexc('6a2a6a'))
    gl.sprite(tex, 960, 945, alpha=prog(lt, 1.4, 2.0))
    rng = np.random.default_rng(23)
    n = 140
    fx.parts(gl, rng.uniform(0, 1920, n), (rng.uniform(0, 1080, n) - lt * 60) % 1080, rng.uniform(6, 22, n), (1, 0.95, 0.85),
             rng.uniform(0.3, 1.0, n), kind=1)
    return dict(bloomAmt=0.6, vign=0.25)


def s_ending(t, lt, d):
    gl = C.gl
    hit_t = T_CUT + 1.6     # エンディング2小節の2小節目頭（音源 161.8 秒）に全員の一撃
    if t < hit_t:
        i = int(lt / 0.2)
        fx.rect(gl, 960, 540, 1920, 1080, E.hexc('120a24'))
        s = C.screens[(i * 7) % len(C.screens)]
        gl.sprite(s, 960 + (i % 3 - 1) * 200, 540, scale=900 / s.size[0] * (1 + 0.1 * ((lt % 0.2) / 0.2)), alpha=0.9, angle=(i % 2 - 0.5) * 0.1)
        fx.focus_lines(gl, inner=0.3, seed=i, amount=0.7)
        return dict(bloomAmt=0.4, vign=0.4, flash=pulse(lt % 0.2, 0.0, 0.01, 0.06) * 0.5)
    lt2 = t - hit_t
    sky_layer(t, 'dawn', cam=(0, 120), cloud_y=1000, scroll=10, n=8, scale=0.7, haze=0.45, seed=14, drift=0.6)
    fx.god_rays(gl, 0.62, 0.86, amount=0.8)
    Hm = char_H(1050, 960, 0.52, sx=1480, sy=640)
    look = dict(cropBottom=1650, outlineW=6.0, outlineCol=(1, 1, 1))
    draw_char('end', t, hit_t - 0.5, lambda tt: base_pose(tt, wind=12, seed=14, expr=(0, 1, 0, 0)), Hm, look)
    fog_bottom(E.hexc('a65a86'), 0.9, 900, 1080)
    # 文字の下に影を敷いて読みやすく
    fx.parts(gl, [640], [560], [1400], E.hexc('140828'), [0.75], kind=0, blend='over')
    sc = 0.5 * (0.9 + 0.1 * ease_out_back(prog(lt2, 0, 0.4)))
    gl.sprite(C.logo, 640, 400, scale=sc * 1.06, alpha=0.8, tint=(0, 0, 0), add=(1.6, 1.0, 2.2), blend='add')
    gl.sprite(C.logo, 640, 400, scale=sc, tint=(0.35, 0.3, 0.45), add=(0.75, 0.72, 0.9))
    tex = gl.text('Creative Vision 10', 100, fam='segoeb', weight=None, color=(1, 1, 1), stroke=7, stroke_color=E.hexc('3b1a6e'))
    gl.sprite(tex, 640, 660)
    tex = gl.text('2027年1月 リリース予定', 44, fam='noto', weight='Black', color=E.hexc('fff3c4'), stroke=5, stroke_color=E.hexc('4a1a6a'))
    gl.sprite(tex, 640, 765)
    tex = gl.text('VOICEVOX:四国めたん　VOICEVOX:波音リツ　VOICEVOX:小夜/SAYO　VOICEVOX:九州そら', 22, fam='noto', weight='Medium',
                  color=(1, 1, 1), stroke=2, stroke_color=(0.2, 0.1, 0.3))
    gl.sprite(tex, 960, 1045, alpha=0.9)
    return dict(bloomAmt=0.6, vign=0.25, flash=pulse(t, hit_t, 0.01, 0.4), fade=prog(t, DURATION - 0.9, DURATION - 0.05))


OLDFILM_FS = E.HEADER + fx.NOISE + r'''
uniform sampler2D tex; uniform float alpha; uniform float seed;
void main(){
    vec2 p = uv;
    vec3 c = texture(tex, p).rgb;
    float l = dot(c, vec3(0.3, 0.59, 0.11));
    vec3 sep = vec3(l*1.05, l*0.88, l*0.66);
    float scratch = step(0.995, h21(vec2(floor(p.x*300.0), seed)))*0.4;
    float flick = 0.92 + 0.08*h21(vec2(seed, 3.0));
    float vig = smoothstep(0.9, 0.3, length(p - 0.5)*1.4);
    vec3 col = sep*flick*vig + scratch + (h21(p*900.0 + seed) - 0.5)*0.08;
    vec2 b = min(p, 1.0 - p);
    float A = smoothstep(0.0, 0.01, min(b.x, b.y));
    f = vec4(col*A, A)*alpha;
}
'''


MB = 3          # モーションブラーのサブフレーム数


SHUTTER = 0.5   # シャッター開角（1コマに対する比）


SHOTS = [
    (0.0, 3.4, s_predawn),
    (3.4, 6.6, s_circle),
    (6.6, 9.8, s_reveal),
    (9.8, 11.4, s_beats),
    (11.4, 13.0, s_title),
    (13.0, 16.2, s_firstcommit),
    (16.2, 19.4, s_codeface),
    (19.4, 22.6, s_tower),
    (22.6, 25.8, s_gather),
    (25.8, 29.0, s_baton),
    (29.0, 32.2, s_lanterns),
    (32.2, 35.4, s_connect),
    (35.4, 38.6, s_build),
    (38.6, 41.8, s_chorus_title),
    (41.8, 45.0, s_fly),
    (45.0, 48.2, s_wings),
    (48.2, 49.8, s_ikou),
    (49.8, 54.6, s_gate),
    (54.6, 61.0, s_sunrise),
    (61.0, T_CUT, s_logo),
    (T_CUT, 1e9, s_ending),
]


def frame(gl, t):
    post = {}
    for k in range(MB):
        ts = t + ((k + 0.5) / MB - 0.5) * SHUTTER / FPS if MB > 1 else t
        gl.time = ts
        gl.use(gl.main, clear=(0, 0, 0, 1))
        for a, b, fn in SHOTS:
            if a <= ts < b:
                p = fn(ts, ts - a, b - a) or {}
                break
        if k == MB // 2:
            post = p
        gl.use(gl.main)
        if 13.0 <= t < T_CUT and not (38.6 <= t < 41.8):
            karaoke(t)
        credits(t)
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
    sr = 48000
    i0 = int(T_CUT * sr)
    j0 = int(SRC_END * sr)
    head = a[:i0].copy()
    tail = a[j0:].copy()
    fade = int(0.012 * sr)
    head[-fade:] *= np.linspace(1, 0, fade)[:, None]
    tail[:fade] *= np.linspace(0, 1, fade)[:, None]
    out = np.concatenate([head, tail])
    n = int(DURATION * sr)
    if len(out) < n:
        out = np.concatenate([out, np.zeros((n - len(out), 2), np.float32)])
    return out[:n]
