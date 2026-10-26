# 作品1 v3 フルバージョン「明日へ飛べ」ポップ版 — 主題歌 v2 のフルコーラス（2分44秒）
# 0〜57.8秒は op3.py（v3 のショート版）のショットをそのまま使い、その先を歌詞 v2 に合わせてポップな画づくりで新しく作る。
# 背景・文字・図形は pop.py、主人公は op2 と同じ素材（6表情のリグ・立ち絵・連続コマ・5人の色違い）。動画生成AIは使わない。
#
# 秒割り（音源の時刻そのまま。150BPM・1拍0.4秒・1小節1.6秒）
#    0.0- 57.8  op3.py と同じ（イントロ〜サビ1「開けよう 今すぐに」）
#   57.8- 62.6  5人の色違いが並んで弾む                                   「目指すはひとつ あの未来へ」
#   62.6- 64.2  空を見上げると夜になる
#   64.2- 70.6  三日月と流れ星、星をつないで「10」、月の前を飛ぶ          間奏
#   70.6- 73.8  雪の中、最初のコミットの実ファイルが1枚ずつ並ぶ（29ファイル） Aメロ2「たった30のファイル」
#   73.8- 77.0  雪の水玉、最初のコミットの日付                            「冬のはじまりだったね」
#   77.0- 80.2  実コミットの件名のブロックが積み上がる                    「ひとつひとつと 重ねて」
#   80.2- 83.4  星のかけらが集まって大きな星になる                        「夢のかけらを 集めて」
#   83.4- 86.6  CV.net の画面から CV10 の画面へバトンが渡る               Bメロ2「受け継いだバトン」
#   86.6- 89.8  200の笑顔のシールが並ぶ                                   「200社の笑顔」
#   89.8- 93.0  実画面とつながる光の線の中心で歌う                        「今はわたしがつなぐ」
#   93.0- 96.2  拍ごとの思い出のカード（線画・実画面・最初のころのコミット）「あの日があるから」
#   96.2- 99.4  渦の上でタイトルの文字が弾む（水色）                      サビ2「Creative Vision 10！」
#   99.4-102.6  毎日のコミットのタイルが集まって光になる                  「重ねた日々 力に」
#  102.6-105.8  雨の困り顔 → 円で開いて晴れの笑顔、雨粒が星になる        「涙もぜんぶ 光になる」
#  105.8-107.4  いこう！（水色、左右反転）
#  107.4-112.2  夜明けの筋雲と昇る太陽を見渡す後ろ姿                      「ほら 夜明けの空 晴れた」
#  112.2-115.4  横に流れる街をミニキャラが駆け抜ける                      「ためらわず走ろう」
#  115.4-121.8  線画のカードに色が付いていき、ロゴになる                  「描いた夢が 未来になる」
#  121.8-128.2  全コミットの星の渦（AI と共同のコミットは桃色）           ブリッジ
#  128.2-131.4  金色のタイトルと花火                                      ラストサビ「Creative Vision 10！」
#  131.4-134.6  4つの声の光が回って1つの輪になる                          「声を合わせ ひとつに」
#  134.6-137.8  5人の色違いとミニキャラの群れ、ハート                     「みんなの笑顔 あふれ出すよ」
#  137.8-139.4  いこう！（桃色）
#  139.4-146.6  光の扉へ手を伸ばし、ミニキャラが扉へ駆けていく           「まだ見ぬ明日の扉 / みんなで開こうよ」
#  146.6-153.8  ハートの中で歌い → 渦と花火の中の5人                       「目指すはひとつ あの未来へ」
#  153.8-160.2  スタッフロール（実数値・プロデューサー・リポジトリと QR）→ 金色のタイトル  アウトロ「Creative Vision 10！」
#  160.2-163.8  エンディング（op3 と同じ: ドラムソロのカード → クレジット）
#   python film.py opfull3                       四国めたん版 → out/opfull3_metan.mp4
#   python film.py opfull3 --variant zundamon    歌い手の版（opfull.py の SINGERS）
import os, math, subprocess
import numpy as np
import opfull2 as F2            # 音源の時刻そのまま・フル版の尺・歌詞 v2（op2 / op / opfull の設定もここで済む）
import op3 as P3                # op3 の読み込みは opfull2 の後（T_CUT・DURATION をフル版の値で受け取る）
import op2 as O2
import engine as E, fx, pop
import op as O
import opfull as F
from engine import prog, lerp, smooth, ease_out, ease_in, ease_io, ease_out_back, pulse, clamp
from op3 import H, INK, BEAT, beat, bounce, face, sing, giant_type, dance_bg, stand_look, stars_bg, star_trail, wedge_bg, flash

FPS = 30
SUPERSAMPLE = 2
SINGER = F2.SINGER
OUT_NAME = f'opfull3_{SINGER}'
DURATION = F2.DURATION
C = None


def setup(gl):
    global C
    P3.setup(gl)
    C = P3.C
    # 最初のコミットのファイル（実データ）
    try:
        first = F.git('rev-list', '--max-parents=0', 'HEAD').split()[-1]
        C.first_files = [l for l in F.git('show', '--name-only', '--format=', first).splitlines() if l.strip()]
    except Exception:
        C.first_files = ['readme.md']
    C.first_date = C.clog[0][0]
    C.first_msg = C.clog[0][1]


def sh(fn, shift):
    """別の時刻（その歌詞の位置）のショットを描く。口パクは v2 の歌のまま"""
    def g(t, lt, d):
        return fn(t + shift, lt, d)
    g.shift = shift
    return g


SNOW = H('ffffff')


def snow(t, n=110, seed=7, alpha=0.95):
    rng = np.random.default_rng(seed)
    xs = rng.uniform(-40, 1960, n) + 30 * np.sin(t * rng.uniform(0.6, 1.4, n) + rng.uniform(0, 6, n))
    ys = (rng.uniform(0, 1160, n) + t * rng.uniform(40, 110, n)) % 1160 - 40
    pop.parts(C.gl, xs, ys, rng.uniform(8, 22, n), SNOW, kind=2, alpha=alpha)


# ---------------------------------------------------------------- サビ1の終わり〜間奏
def s_lineup_long(t, lt, d):
    return P3.s_lineup(t, lt, d)


def s_to_night(t, lt, d):
    """空を見上げると夜になる"""
    gl = C.gl
    k = ease_io(lt / d, 2)
    c0 = np.array(H('ff7aa8')) * (1 - k) + np.array(H('141a52')) * k
    c1 = np.array(H('ffd0e0')) * (1 - k) + np.array(H('3a2a8a')) * k
    pop.popbg(gl, tuple(c0), tuple(c1), ang=1.57, dot_a=0.0)
    stars_bg(t, n=int(70 * k) + 1, seed=4, area=(0, 0, 1920, 900))
    st = C.stand['fullbody-back']
    st.draw(st.H(960, 1180 + 220 * k, lerp(900, 1100, k)), t, wind=10, hem=3.0, look=stand_look(outlineW=6.0, outlineCol=(1, 1, 1)))
    return dict(bloomAmt=0.3, vign=0.15)


TEN = [(0.0, 0.0), (0.0, 1.0), (0.0, 2.0), (0.0, 3.0),                                       # 1
       (1.2, 0.3), (1.6, 0.0), (2.2, 0.0), (2.6, 0.3), (2.7, 1.5), (2.6, 2.7), (2.2, 3.0), (1.6, 3.0), (1.2, 2.7), (1.1, 1.5)]   # 0


def s_interlude(t, lt, d):
    """間奏: 三日月と流れ星、星をつないで「10」、月の前を飛ぶ"""
    gl = C.gl
    pop.popbg(gl, H('0e1240'), H('2a2a7a'), ang=1.57, dot_col=H('5a5ab8'), dot_a=0.35, gap=48, dot_r=2.6, dot_off=(lt * 10, 0))
    stars_bg(t, n=90, seed=12, area=(0, 0, 1920, 1080), size=(6, 18))
    # 三日月（大きな円を夜空の色の円で欠く）
    mx, my = 1450, 330
    fx.parts(gl, [mx], [my], [1100], (1.0, 0.95, 0.6), [0.35], kind=0)
    pop.parts(gl, [mx], [my], [520], H('fff1a0'), kind=2, outline=0.0)
    pop.parts(gl, [mx + 120], [my - 70], [470], H('1a1c56'), kind=2)
    # 星をつないで「10」（拍ごとに1つ）
    ox, oy, sc = 300, 230, 150
    pts = [(ox + x * sc, oy + y * sc) for x, y in TEN]
    nshow = int(prog(lt, 0.2, 4.0) * len(pts)) + 0
    seq1, seq0 = list(range(0, 4)), list(range(4, 14)) + [4]
    for seq in (seq1, seq0):
        for a, b in zip(seq[:-1], seq[1:]):
            if max(a, b) >= nshow or a >= nshow:
                continue
            (x0, y0), (x1, y1) = pts[a], pts[b]
            L = math.hypot(x1 - x0, y1 - y0)
            fx.rect(gl, (x0 + x1) / 2, (y0 + y1) / 2, L, 5, (1.0, 0.9, 0.5), alpha=0.8, angle=math.atan2(y1 - y0, x1 - x0), blend='add')
    for i, (x, y) in enumerate(pts):
        k = ease_out_back(prog(lt, 0.2 + i * 3.8 / len(pts), 0.45 + i * 3.8 / len(pts)), 3)
        if k > 0:
            pop.parts(gl, [x], [y], [60 * k], H('ffe36a'), kind=3, outline=0.08, angle=[0.2 * math.sin(t * 3 + i)])
    # 流れ星（拍の頭に）
    for j, t0 in enumerate((0.4, 1.6, 2.8, 4.4, 5.6)):
        f = prog(lt, t0, t0 + 0.7)
        if 0 < f < 1:
            x = lerp(1900 - j * 150, 900 - j * 150, f); y = lerp(80 + j * 60, 520 + j * 60, f)
            star_trail(lambda q: x + q * 420, lambda q: y - q * 190, t, n=16, seed=j, size=34)
    # 月の前を飛ぶ
    f = prog(lt, 3.0, 5.6)
    if 0 < f < 1:
        x = lerp(2100, -200, f); y = my + 40 + 60 * math.sin(f * 6)
        fb = C.fb['flight']
        fb.draw(fb.H(x, y, 560, angle=0.1, flip=True, by='width'), t, P3.P2.fb_at('flight', lt), breath=0.0,
                look=stand_look(silhouette=0.9, silCol=H('1a1240'), outlineW=0.0, rimCol=(1.4, 1.3, 0.8), rimDir=(0, -1), rimW=4))
    if lt > d - 0.6:
        snow(t, n=int(110 * prog(lt, d - 0.6, d)))
    return dict(bloomAmt=0.5, vign=0.2, **flash(t, 64.2, 0.2))


# ---------------------------------------------------------------- Aメロ2
def s_files(t, lt, d):
    """たった30のファイル: 雪の中、最初のコミットの実ファイルが1枚ずつ並ぶ"""
    gl = C.gl
    dance_bg(t, 'blue', seed=21, dots=0.35)
    snow(t)
    files = C.first_files
    n = len(files)
    cols = 3
    rows = (n + cols - 1) // cols
    span = 2.5
    for i, f in enumerate(files):
        t0 = 0.15 + i * span / n
        k = ease_out_back(prog(lt, t0, t0 + 0.2), 2.5)
        if k <= 0:
            continue
        c, r = i % cols, i // cols
        x = 300 + c * 470; y = 140 + r * (820 / rows)
        name = f if len(f) < 34 else '…' + f[-32:]
        tex = gl.text(name, 22, fam='mono', weight=None, color=INK)
        w, h = gl.vsize(tex)
        ang = 0.04 * math.sin(i * 1.7)
        fx.rect(gl, x + 6, y + 7, 430 * k, 58 * k, INK, alpha=0.3, angle=ang)
        fx.rect(gl, x, y, 430 * k, 58 * k, (1, 1, 1), angle=ang)
        pop.parts(gl, [x - 190 * k], [y], [30 * k], pop.CONF_PAL[i % 7], kind=5, ratio=1.2)
        gl.sprite(tex, x - 165 * k, y, anchor=(0, 0.5), scale=k * min(1.0, 360 / w), angle=ang)
    cnt = int(n * prog(lt, 0.15, 0.15 + span))
    pop.chunk(gl, f'{cnt}', 1700, 760, 200, fill=H('ffd23f'), edge=INK, fam='impact', weight=None, angle=-0.08, white=8)
    pop.chunk(gl, 'files', 1700, 900, 60, fill=(1, 1, 1), edge=INK, fam='segoeb', weight=None, angle=-0.08)
    return dict(bloomAmt=0.2, vign=0.1, **flash(t, 70.6, 0.2))


def s_winter(t, lt, d):
    """冬のはじまりだったね: 雪の水玉、最初のコミットの日付"""
    gl = C.gl
    pop.popbg(gl, H('9cc8ff'), H('dff0ff'), ang=1.2, circles=pop.spot_circles(t, 22, 3, 1.0, 0.5), circ_col=(0.5, 0.5, 0.5),
              dot_col=(1, 1, 1), dot_a=0.8, gap=44, dot_r=3.5, dot_off=(0, -lt * 30))
    snow(t, n=80, seed=9)

    def pose(tt):
        p = O.base_pose(tt, wind=8, seed=44, sing=True)
        e = smooth(prog(tt, 75.4, 75.8))
        p.expr_full = p.expr_face = (1 - e, e, 0, 0)
        return p
    face('winter', t, 73.8 - 0.5, pose, 1050, 980, 0.74, 640, 600)
    k = ease_out_back(prog(lt, 0.3, 0.6), 2.2)
    pop.parts(gl, [1440 + 10], [460 + 12], [620 * k], INK, kind=2, alpha=0.3)
    pop.parts(gl, [1440], [460], [620 * k], H('ffffff'), kind=2, outline=0.035)
    pop.chunk(gl, C.first_date.replace('-', '.'), 1440, 410, 92, fill=H('5b8ff2'), edge=INK, fam='impact', weight=None, scale=k, angle=-0.06)
    pop.chunk(gl, C.first_msg, 1440, 520, 60, fill=H('ff6f9c'), edge=INK, scale=k, angle=-0.06)
    pop.chunk(gl, 'first commit', 1440, 600, 34, fill=INK, edge=(1, 1, 1), fam='segoeb', weight=None, scale=k, angle=-0.06, depth=0.0)
    return dict(bloomAmt=0.2, vign=0.1, **flash(t, 73.8, 0.2))


def s_stack(t, lt, d):
    """ひとつひとつと 重ねて: 実コミットの件名のブロックが積み上がる"""
    gl = C.gl
    dance_bg(t, 'purple', seed=23, dots=0.4)
    giant_type(t, 'COMMIT  COMMIT  COMMIT', 540, alpha=0.22, speed=70, angle=-0.15)
    n_all = len(C.clog)
    step = 0.2
    nb = int(lt / step) + 1
    bh = 74
    camy = max(0.0, (nb - 9) * bh)
    for i in range(nb):
        t0 = i * step
        k = prog(lt, t0, t0 + 0.18)
        y_rest = 920 - i * bh + camy
        y = lerp(y_rest - 700, y_rest, ease_in(k, 2)) if k < 1 else y_rest + 8 * pulse(lt, t0 + 0.18, 0.01, 0.12)
        if y < -100 or y > 1200:
            continue
        idx = int(i * (n_all - 1) / 15)
        dd, subj, ai = C.clog[min(idx, n_all - 1)]
        if len(subj) > 22:
            subj = subj[:21] + '…'
        x = 760 + 26 * math.sin(i * 1.9)
        col = pop.CONF_PAL[i % 7]
        pop.parts(gl, [x + 8], [y + 10], [700], INK, kind=5, ratio=bh / 700 * 0.95, alpha=0.35)
        pop.parts(gl, [x], [y], [700], col, kind=5, ratio=bh / 700 * 0.95, outline=0.012)
        tex = gl.text(subj, 30, fam='noto', weight='Black', color=INK)
        gl.sprite(tex, x, y, scale=min(1.0, 560 / gl.vsize(tex)[0]))
    # 塔のてっぺんで跳ねるミニキャラ
    top = 920 - (nb - 1) * bh + camy - bh / 2
    F.pet('jump', t, 760, top, scale=1.1, fps=10)
    cnt = int(n_all * min(1.0, nb / 16))
    pop.chunk(gl, f'{cnt:,}', 1560, 300, 150, fill=H('ffd23f'), edge=INK, fam='impact', weight=None, angle=-0.06, white=6)
    pop.chunk(gl, 'commits', 1560, 410, 50, fill=(1, 1, 1), edge=INK, fam='segoeb', weight=None, angle=-0.06)
    return dict(bloomAmt=0.2, vign=0.1, **flash(t, 77.0, 0.2))


def s_gather(t, lt, d):
    """夢のかけらを 集めて: 星のかけらが集まって大きな星になる"""
    gl = C.gl
    dance_bg(t, 'pink', seed=25, dots=0.5)
    cx, cy = 560, 470
    rng = np.random.default_rng(80)
    n = 60
    a = rng.uniform(0, 6.3, n)
    r0 = rng.uniform(700, 1300, n)
    st = rng.uniform(0, 1.8, n)
    q = np.clip((lt - st) / 0.9, 0, 1)
    live = (q > 0) & (q < 1)
    r = r0 * (1 - q * q)
    xs = cx + np.cos(a + q * 2) * r; ys = cy + np.sin(a + q * 2) * r * 0.75
    pop.parts(gl, xs[live], ys[live], rng.uniform(24, 50, n)[live], pop.CONF_PAL[np.arange(n) % 7][live], kind=3, outline=0.08,
              angle=a[live] + t * 2)
    g = np.clip((lt - st - 0.9) / 0.1, 0, 1).sum() / n
    big = ease_out_back(clamp(g * 1.2), 1.8) * (1 + 0.08 * pulse(lt, 2.6, 0.02, 0.3))
    if big > 0:
        fx.parts(gl, [cx], [cy], [900 * big], (1.0, 0.9, 0.5), [0.5], kind=0)
        pop.parts(gl, [cx + 14], [cy + 16], [520 * big], INK, kind=3, alpha=0.3)
        pop.parts(gl, [cx], [cy], [520 * big], H('ffe14a'), kind=3, outline=0.03, angle=[0.15 * math.sin(t * 2)])
    face('gather', t, 80.2 - 0.5, sing(46, expr=(0, 1, 0, 0), wind=10), 1050, 980, 0.72, 1360, 600)
    return dict(bloomAmt=0.45, vign=0.1, **flash(t, 80.2, 0.2))


# ---------------------------------------------------------------- Bメロ2
def s_baton(t, lt, d):
    """受け継いだバトン: CV.net の画面から CV10 の画面へバトンが渡る"""
    gl = C.gl
    i = int(lt / 1.6)
    wedge_bg(2 + i, 0)
    old, new = C.oldcv
    k1 = ease_out_back(prog(lt, 0.0, 0.3), 1.8)
    k2 = ease_out_back(prog(lt, 1.2, 1.5), 1.8)
    pop.card(gl, 520, 470 + 200 * (1 - k1), 700, 560, -0.08, tex=old, label='CV.net', label_size=36, alpha=min(1, k1 * 2))
    if k2 > 0:
        pop.card(gl, 1400, 520 + 200 * (1 - k2), 700, 560, 0.07, tex=new, label='CV10', label_size=36, alpha=min(1, k2 * 2))
    # バトン（縞の棒）が弧を描いて渡る
    f = ease_io(prog(lt, 0.6, 1.8), 2)
    bx = lerp(620, 1300, f); by = lerp(420, 470, f) - math.sin(math.pi * f) * 330
    ang = f * 6.3 + 0.5
    for j in range(4):
        off = (j - 1.5) * 34
        fx.rect(gl, bx + math.cos(ang) * off, by + math.sin(ang) * off, 36, 38, H('ff4f7b') if j % 2 else (1, 1, 1), angle=ang)
    pop.parts(gl, [bx], [by], [70], H('ffd23f'), kind=3, outline=0.1, angle=[t * 3])
    star_trail(lambda q: bx - (q * 300) * (1 if f < 1 else 0), lambda q: by + q * 60, t, n=12, seed=9, size=28)
    pop.chunk(gl, '延べ200社の CV.net から', 520, 900, 46, fill=(1, 1, 1), edge=INK, alpha=prog(lt, 0.3, 0.6), white=4)
    pop.chunk(gl, 'CV10 へ', 1400, 940, 70, fill=H('ffd23f'), edge=INK, alpha=prog(lt, 1.5, 1.8), white=5)
    return dict(bloomAmt=0.2, vign=0.05, **flash(t, 83.4, 0.2))


def s_smiles200(t, lt, d):
    """200社の笑顔: 200の笑顔のシールが波のように並ぶ"""
    gl = C.gl
    dance_bg(t, 'yellow', seed=27, dots=0.5)
    cols, rows = 20, 10
    xs, ys, ks, cs = [], [], [], []
    for i in range(200):
        c, r = i % cols, i // cols
        t0 = 0.1 + (c + r) * 0.07
        k = ease_out_back(prog(lt, t0, t0 + 0.25), 2.5)
        if k <= 0:
            continue
        hop = 10 * abs(math.sin(math.pi * (beat(t) + (c + r) * 0.1)))
        xs.append(105 + c * 90); ys.append(110 + r * 86 - hop); ks.append(k); cs.append(pop.CONF_PAL[(c * 3 + r) % 7])
    if xs:
        xs = np.array(xs); ys = np.array(ys); ks = np.array(ks)
        pop.parts(gl, xs, ys, 78 * ks, np.array(cs), kind=2, outline=0.08)
        pop.parts(gl, xs - 12 * ks, ys - 8 * ks, 11 * ks, INK, kind=2)
        pop.parts(gl, xs + 12 * ks, ys - 8 * ks, 11 * ks, INK, kind=2)
        pop.parts(gl, xs, ys + 14 * ks, 26 * ks, INK, kind=5, ratio=0.28)
    cnt = int(200 * prog(lt, 0.1, 2.3))
    pop.parts(gl, [960], [540], [760], (1, 1, 1), kind=5, ratio=0.42, outline=0.02, alpha=prog(lt, 0.2, 0.5))
    pop.chunk(gl, f'{cnt}社', 960, 510, 170, fill=H('ff4f7b'), edge=INK, angle=-0.05, alpha=prog(lt, 0.2, 0.5), white=6)
    pop.chunk(gl, 'の笑顔', 1180, 610, 60, fill=H('ffd23f'), edge=INK, angle=-0.05, alpha=prog(lt, 0.5, 0.8))
    return dict(bloomAmt=0.2, vign=0.05, **flash(t, 86.6, 0.2))


def s_connect(t, lt, d):
    """今はわたしがつなぐ: 実画面とつながる光の線の中心で歌う"""
    gl = C.gl
    dance_bg(t, 'purple', seed=29, dots=0.45)
    cx, cy = 960, 520
    n = 10
    for j in range(n):
        a = j / n * 2 * math.pi + lt * 0.25
        x = cx + math.cos(a) * 760; y = cy + math.sin(a) * 400
        k = ease_out_back(prog(lt, 0.1 + j * 0.12, 0.35 + j * 0.12), 2)
        if k <= 0:
            continue
        # 線（主人公へ向かって光が流れる）
        L = math.hypot(x - cx, y - cy); ang = math.atan2(y - cy, x - cx)
        fx.rect(gl, (x + cx) / 2, (y + cy) / 2, L * k, 8, (1, 1, 1), alpha=0.9, angle=ang)
        q = ((lt * 1.2 + j * 0.37) % 1.0)
        pop.parts(gl, [x + (cx - x) * q], [y + (cy - y) * q], [34], H('ffd23f'), kind=3, outline=0.1, angle=[t * 3])
        tex = C.screens_all[(j * 9) % len(C.screens_all)]
        s = 300 * k
        fx.rect(gl, x, y, s + 14, s * tex.size[1] / tex.size[0] + 14, INK, angle=0.05 * math.sin(j))
        gl.sprite(tex, x, y, scale=s / tex.size[0], angle=0.05 * math.sin(j))
    face('connect', t, 89.8 - 0.5, sing(48, expr=(1, 0, 0, 0), wind=8), 1050, 980, 0.62, cx, cy + 70)
    return dict(bloomAmt=0.2, vign=0.1, **flash(t, 89.8, 0.2))


def s_memories(t, lt, d):
    """あの日があるから: 拍ごとの思い出のカード（線画・実画面・最初のころのコミット）"""
    gl = C.gl
    i = int(lt / BEAT)
    lb = (lt % BEAT) / BEAT
    wedge_bg(i + 5, lb)
    kind = i % 3
    ang = 0.08 * (1 if i % 2 else -1)
    if kind == 0:
        ink, label = C.inks[(i // 3) % len(C.inks)]
        pop.card(gl, 960, 540, 620, 820, ang, ink=ink, label=label)
    elif kind == 1:
        j = (i * 13) % len(C.screens_all)
        pop.card(gl, 960, 540, 900, 660, ang, tex=C.screens_all[j], label=C.screen_names[j], label_size=30)
    else:
        dd, subj, ai = C.clog[min(len(C.clog) - 1, 1 + i * 7)]
        pop.card(gl, 960, 540, 980, 420, ang, label=dd)
        pop.chunk(gl, subj if len(subj) < 20 else subj[:19] + '…', 960, 560, 64, fill=H('ff6f9c'), edge=INK, angle=ang, depth=0.05)
    return dict(bloomAmt=0.1, vign=0.0, mono=0.25 * (1 - prog(lt, 2.4, 3.2)), flash=pulse(lt % BEAT, 0.0, 0.01, 0.05) * 0.3)


# ---------------------------------------------------------------- サビ2
def s_title2(t, lt, d):
    return P3.chorus_title(t, lt, d, 96.2, cols=('9fe6ff', 'd8f6ff', '6fd2f0'))


def s_power(t, lt, d):
    """重ねた日々 力に: 毎日のコミットのタイル（実データ）が集まって光になる"""
    gl = C.gl
    dance_bg(t, 'lemon', seed=31, dots=0.7)
    weeks, rows = C.heat_dims
    cell = 1760 / weeks
    hx, hy = 960, 560
    gather = prog(lt, 1.0, 2.2)
    xs, ys, cs, ss = [], [], [], []
    rng = np.random.default_rng(5)
    for w in range(weeks):
        for k in range(rows):
            v, inr = C.heat_arr[k, w, 0], C.heat_arr[k, w, 1]
            if inr <= 0 or v <= 0:
                continue
            x0 = 80 + (w + 0.5) * cell; y0 = 90 + (k + 0.5) * cell
            t0 = w / weeks * 0.9
            a = ease_out_back(prog(lt, t0, t0 + 0.2), 2.0)
            ph = clamp((gather - rng.uniform(0, 0.5)) / 0.5)
            q = ease_in(ph, 2)
            xs.append(lerp(x0, hx, q)); ys.append(lerp(y0, hy, q)); ss.append(cell * 0.86 * a * (1 - 0.6 * q))
            cs.append(P3.TILE_PAL[min(4, int(v * 4 + 0.01))])
    if xs:
        pop.parts(gl, np.array(xs), np.array(ys), np.array(ss), np.array(cs), kind=5, outline=0.08)
    st = C.stand['fullbody3d']
    b = beat(t)
    st.draw(st.H(960, 1150, 760), t, wind=8, hem=4.0, sway=0.04 * math.sin(math.pi * b),
            look=stand_look(outlineW=7.0, outlineCol=(1, 1, 1), tintAdd=(0.1 * gather, 0.08 * gather, 0.0)))
    hit = pulse(t, 101.6, 0.02, 0.6)
    if hit > 0:
        a = np.linspace(0, 2 * math.pi, 24, endpoint=False)
        r = 150 + 900 * (1 - hit)
        pop.parts(gl, hx + np.cos(a) * r, hy + np.sin(a) * r * 0.7, np.full(24, 60), pop.CONF_PAL[np.arange(24) % 7], kind=3,
                  alpha=hit, outline=0.1, angle=a)
    pop.chunk(gl, f'{C.n_days} days', 1700, 980, 70, fill=H('ff4f7b'), edge=INK, fam='impact', weight=None, angle=-0.06, white=5,
              alpha=prog(lt, 0.3, 0.6))
    return dict(bloomAmt=0.3 + 0.4 * hit, vign=0.05, flash=0.3 * hit, flashCol=(1, 1, 0.9))


def s_tears(t, lt, d):
    """涙もぜんぶ 光になる: 雨の困り顔 → 円で開いて晴れの笑顔、雨粒が星になる"""
    gl = C.gl
    t_open = 1.6
    k = prog(lt, t_open, t_open + 0.4)
    rng = np.random.default_rng(66)
    n = 90
    xs = rng.uniform(0, 1920, n)

    def pose(tt):
        p = O.base_pose(tt, wind=6, seed=50, sing=True)
        e = smooth(prog(tt, 102.6 + t_open, 102.6 + t_open + 0.3))
        p.expr_full = p.expr_face = (0, 0, 0, 0, e, 1 - e)
        return p
    # 雨
    pop.popbg(gl, H('3a4aa8'), H('7a8ad8'), ang=1.57, dot_col=H('9aa8f0'), dot_a=0.4)
    ys = (rng.uniform(0, 1200, n) + lt * 900) % 1200 - 60
    pop.parts(gl, xs, ys, rng.uniform(30, 60, n), H('bfe0ff'), kind=0, ratio=0.12, angle=0.15)
    face('tears', t, 102.6 - 0.5, pose, 1050, 980, 0.76, 960, 600, look=dict(tintMul=(0.85, 0.88, 1.05)))
    if k > 0:
        # 円が広がって晴れの地になり、その上に笑顔を描き直す
        if k < 1:
            pop.wipe(gl, 960, 540, 1250 * ease_out(k, 2), H('fff06a'), inside=True)
        else:
            dance_bg(t, 'lemon', seed=33, dots=0.7)
        face('tears', t, 102.6 - 0.5, pose, 1050, 980, 0.76, 960, 600)
        q = prog(lt, t_open, d)
        ys2 = 1100 - ((rng.uniform(0, 1200, n) + q * 1400) % 1200)
        pop.parts(gl, xs, ys2, rng.uniform(26, 50, n), pop.CONF_PAL[np.arange(n) % 7], kind=3, outline=0.1, angle=xs * 0.01 + t * 2,
                  alpha=min(1.0, k * 2))
    return dict(bloomAmt=0.25, vign=0.08, **flash(t, 102.6 + t_open, 0.2))


def s_ikou2(t, lt, d):
    return P3.ikou(t, lt, d, 105.8, c=('7fe0ff', 'c8f4ff', '1e7fd8'), side=-1)


def s_dawn(t, lt, d):
    """ほら 夜明けの空 晴れた: 夜明けの筋雲と昇る太陽を見渡す後ろ姿"""
    gl = C.gl
    pop.sunset(gl, t, pal='dawn')
    k = ease_out(prog(lt, 0.0, d), 2)
    sx, sy = 1250, lerp(900, 560, k)
    fx.burst(gl, sx / 1920, sy / 1080, rot=t * 0.15, n=26, c1=H('ffe3a0'), c2=H('ffc46a'), c3=H('fff6d0'), alpha=0.35 * k)
    pop.parts(gl, [sx], [sy], [560], H('fff1a8'), kind=2)
    fx.parts(gl, [sx], [sy], [1500], (1.0, 0.75, 0.5), [0.5], kind=0)
    # 雲海（ポップな丸の連なり）
    for row, (y0, col, sz) in enumerate(((930, 'ffd6e8', 260), (1010, 'ffffff', 300))):
        n = 12
        xs = (np.arange(n) * 190 + lt * (20 + 20 * row)) % 2280 - 180
        pop.parts(gl, xs, np.full(n, y0) + 20 * np.sin(np.arange(n) * 1.3), np.full(n, sz), H(col), kind=2)
    a = ease_out(prog(lt, 0.2, 1.4), 3)
    fb = C.fb['wind']
    fb.draw(fb.H(560, lerp(1400, 1200, a), 980), t, P3.P2.fb_at('wind', lt),
            look=stand_look(outlineW=6.0, outlineCol=(1, 1, 1), rimCol=(1.6, 1.1, 0.6), rimDir=(1.0, 0.0), rimW=5))
    return dict(bloomAmt=0.45, vign=0.1, **flash(t, 107.4, 0.2))


def s_run(t, lt, d):
    """ためらわず走ろう: 横に流れる街をミニキャラが駆け抜ける"""
    gl = C.gl
    pop.popbg(gl, H('3f9bff'), H('bfe6ff'), ang=1.57, dot_a=0.0)
    O.sky_layer(t, 'day', cam=(0, 0), cloud_y=520, scroll=-200, n=5, scale=0.45, haze=0.2, seed=33)
    # 平塗りのビルの列（右から左へ流れる）
    rng = np.random.default_rng(12)
    span = 3000
    for layer, (sp, yb, hs) in enumerate(((260, 900, (260, 520)), (620, 960, (160, 360)))):
        x = 0
        items = []
        while x < span:
            w = rng.uniform(150, 280); h = rng.uniform(*hs)
            items.append((x, w, h)); x += w + rng.uniform(4, 30)
        for i, (x0, w, h) in enumerate(items):
            xx = (x0 - lt * sp) % span - 300
            Hf = gl.rect_h(xx + w / 2, yb - h / 2, w, h)
            gl.draw(pop.BLD_FS, Hf, dict(base=H(pop.CITY_PAL[(i + layer * 3) % len(pop.CITY_PAL)]), winOn=H('ffe36a'), winOff=H('a8c8f0'),
                                         lineCol=INK, grid=(3, 6), seed=float(i + 50 * layer), lit=0.3, stripe=0.0, outline=2.2,
                                         shade=0.85 + 0.15 * layer, tw=0.0), blend=None)
    fx.rect(gl, 960, 1020, 1920, 120, H('f2c9a0'))
    fx.rect(gl, 960, 962, 1920, 8, INK)
    fx.focus_lines(gl, inner=0.6, seed=int(t * 15), amount=0.45, col=(1, 1, 1), mode=1)
    # ミニキャラが駆ける
    for j in range(7):
        x = 300 + j * 220 + 40 * math.sin(t * 2 + j)
        F.pet('run', t, x, 1000 - 14 * abs(math.sin(t * 9 + j)), scale=1.3, fps=14, phase=j * 1.3)
    a = ease_out(prog(lt, 0.0, 0.6), 3)
    x = lerp(-300, 1300, a) + 20 * math.sin(lt * 3)
    fb = C.fb['flight']
    fb.draw(fb.H(x, 420 + 20 * math.sin(lt * 4), 900, angle=-0.05, by='width'), t, P3.P2.fb_at('flight', lt), breath=0.0,
            look=stand_look(outlineW=7.0, outlineCol=(1, 1, 1)))
    star_trail(lambda q: x - 250 - q * 800, lambda q: 470 + q * 30 + 30 * np.sin(q * 8 + lt * 5), t, n=26, seed=12, size=40)
    return dict(bloomAmt=0.2, vign=0.05, ca=1.5, **flash(t, 112.2, 0.2))


def s_paint(t, lt, d):
    """描いた夢が 未来になる: 線画のカードに色が付いていき、ロゴになる"""
    gl = C.gl
    dance_bg(t, 'pink', seed=35, dots=0.55)
    giant_type(t, 'DRAW  DREAM  FUTURE  ', 860, alpha=0.3, speed=90)
    order = [0, 4, 2, 1]
    if lt < 4.8:
        i = int(lt / 1.6)
        lc = lt - i * 1.6
        # 前のカードは横へ退いて小さく並ぶ
        for j in range(i):
            ink_i = order[j]
            x = 220 + j * 260; y = 860
            ink, label = C.inks[ink_i]
            pop.card(gl, x, y, 220, 290, -0.1 + 0.07 * j, ink=None, label=None)
            col = C.inks_color[ink_i]
            gl.sprite(col, x, y + 10, scale=250 / col.size[1], angle=-0.1 + 0.07 * j)
        ink_i = order[i]
        ink, label = C.inks[ink_i]
        col = C.inks_color[ink_i]
        k = ease_out_back(prog(lc, 0.0, 0.3), 1.8)
        ang = 0.06 * (1 if i % 2 else -1)
        pop.card(gl, 1100, 480 + 300 * (1 - k), 600 * k, 800 * k, ang, ink=None, label=label)
        paint = smooth(prog(lc, 0.5, 1.2))
        c, s = math.cos(ang), math.sin(ang)
        sc = (800 * k - 40) / col.size[1]
        gl.sprite(col, 1100 - s * 10, 480 + 300 * (1 - k) + c * 10, scale=sc, angle=ang, alpha=paint)
        gl.sprite(ink, 1100 - s * 10, 480 + 300 * (1 - k) + c * 10, scale=sc, angle=ang, alpha=1 - 0.7 * paint)
        # 筆の先（色が付く位置）
        if 0 < paint < 1:
            pop.parts(gl, [1100 + 260 * math.sin(lc * 9)], [480 + 300 * (1 - k) - 380 + 760 * paint], [70], H('ffd23f'), kind=3,
                      outline=0.1, angle=[lc * 5])
        return dict(bloomAmt=0.2, vign=0.05, **flash(t, 115.4 + i * 1.6, 0.15))
    lt2 = lt - 4.8
    k = ease_out_back(prog(lt2, 0.0, 0.35), 2.0)
    gl.sprite(C.logo, 960, 330, scale=0.4 * k, tint=(0.5, 0.4, 0.6), add=(0.9, 0.55, 1.0))
    pop.chunk(gl, 'Creative Vision 10', 960, 600, 130, fill=(1, 1, 1), edge=INK, fam='comic', weight=None, scale=k, angle=-0.04, white=8)
    pop.chunk(gl, '～ 明日へ飛べ ～', 960, 760, 64, fill=H('ffd23f'), edge=INK, alpha=prog(lt2, 0.3, 0.6), white=5)
    pop.confetti(gl, t, n=80, seed=35, fall=80)
    return dict(bloomAmt=0.3, vign=0.05, **flash(t, 120.2, 0.25))


# ---------------------------------------------------------------- ブリッジ
def s_galaxy(t, lt, d):
    """ブリッジ: 全コミットの星の渦（中心が最初のコミット、外側ほど新しい。AI と共同は桃色）"""
    gl = C.gl
    pop.swirl(gl, lt * 0.25, c=(0.5, 0.5), cols=('1a1c56', '22266a', '161848'), line=(0.5, 0.5, 0.9), line_a=0.25, arms=2.0, tight=1.4)
    n = len(C.clog)
    shown = int(n * ease_io(prog(lt, 0.0, 4.4), 1.6))
    i = np.arange(shown)
    q = (i + 1) / n
    rng = np.random.default_rng(3)
    jit = rng.normal(0, 1, (n, 2))[:shown]
    # 2本の腕のらせん（中心が古く、外側ほど新しい）。腕のまわりへ少し散らす
    ang = (i % 2) * math.pi + 5.5 * np.sqrt(q) + jit[:, 0] * 0.22 * (1 - 0.5 * q) + lt * 0.35
    r = 40 + 760 * np.sqrt(q) * (1 + 0.06 * jit[:, 1])
    zoom = lerp(1.25, 0.95, ease_io(lt / d, 2))
    xs = 960 + np.cos(ang) * r * 1.2 * zoom; ys = 540 + np.sin(ang) * r * 0.72 * zoom
    ai = np.array([c[2] for c in C.clog[:shown]], bool)
    tw = 0.7 + 0.3 * np.sin(t * 4 + i)
    if shown:
        pop.parts(gl, xs[~ai], ys[~ai], (12 + 10 * tw[~ai]) * zoom, H('ffe36a'), kind=3, angle=ang[~ai])
        pop.parts(gl, xs[ai], ys[ai], (14 + 12 * tw[ai]) * zoom, H('ff7ab8'), kind=4, angle=ang[ai] * 0.1)
        fx.parts(gl, [xs[-1]], [ys[-1]], [260], (1.0, 0.9, 0.6), [0.6], kind=0)
    gl.sprite(C.logo, 960, 540, scale=0.22 * zoom, tint=(0.5, 0.4, 0.6), add=(0.9, 0.55, 1.0), alpha=prog(lt, 0.2, 0.8))
    n_ai = int(ai.sum())
    pop.chunk(gl, f'{shown:,}', 1700, 140, 110, fill=H('ffe36a'), edge=INK, fam='impact', weight=None, angle=-0.05, white=5)
    pop.chunk(gl, 'commits', 1700, 225, 40, fill=(1, 1, 1), edge=INK, fam='segoeb', weight=None, angle=-0.05)
    pop.chunk(gl, f'with AI {n_ai:,}', 1700, 300, 44, fill=H('ff7ab8'), edge=INK, fam='segoeb', weight=None, angle=-0.05)
    if shown:
        pop.chunk(gl, C.clog[shown - 1][0].replace('-', '.'), 220, 980, 60, fill=(1, 1, 1), edge=INK, fam='impact', weight=None, white=4)
    return dict(bloomAmt=0.8, vign=0.25, **flash(t, 121.8, 0.25))


# ---------------------------------------------------------------- ラストサビ
def s_title3(t, lt, d):
    return P3.chorus_title(t, lt, d, 128.2, cols=('ffe08a', 'fff2c4', 'ffc94a'), gold=True,
                           fills=[H('ffd23f'), H('ffb02a'), H('ffe36a'), H('ff8a3d')])


def s_voices(t, lt, d):
    """声を合わせ ひとつに: 4つの声の光が回って1つの輪になる"""
    gl = C.gl
    dance_bg(t, 'blue', seed=37, dots=0.45)
    names = [F.MAIN] + list(F.CHORUS)
    pal = ['ff6f9c', 'ffd23f', '6fd8ff', 'b6f23c']
    cx, cy = 960, 470
    merge = ease_io(prog(lt, 1.8, 2.8), 2)
    for j, nm in enumerate(names):
        a = j / len(names) * 2 * math.pi + lt * 1.4
        rx, ry = lerp(700, 330, merge), lerp(330, 150, merge)
        x = cx + math.cos(a) * rx; y = cy + math.sin(a) * ry
        k = ease_out_back(prog(lt, 0.1 + j * 0.2, 0.35 + j * 0.2), 2.2)
        if k <= 0:
            continue
        pop.parts(gl, [x], [y], [200 * k], H(pal[j]), kind=2, outline=0.05)
        tex = gl.text(nm, 30, fam='noto', weight='Black', color=INK)
        gl.sprite(tex, x, y, scale=k * min(1.0, 170 / gl.vsize(tex)[0]))
        nt = gl.text('♪', 60, fam='sym', weight=None, color=H(pal[j]), stroke=3, stroke_color=INK)
        gl.sprite(nt, x + 90, y - 110 - 20 * math.sin(t * 4 + j), alpha=k)
    if merge > 0:
        pop.parts(gl, [cx], [cy], [900 * merge], H('ffffff'), kind=6, alpha=0.9 * merge)
        fx.parts(gl, [cx], [cy], [1300 * merge], (1.0, 0.85, 1.0), [0.35 * merge], kind=0)
    face('voices', t, 131.4 - 0.5, sing(52, expr=(0, 1, 0, 0), wind=10), 1050, 980, 0.62, cx, cy + 140)
    return dict(bloomAmt=0.35, vign=0.05, **flash(t, 131.4, 0.2))


def s_smiles(t, lt, d):
    """みんなの笑顔 あふれ出すよ: 5人の色違いとミニキャラの群れ、ハート"""
    gl = C.gl
    pop.popbg(gl, H('fff04a'), H('ffd23f'), dot_col=(1, 1, 1), dot_a=0.85, gap=42, dot_r=4.2, dot_off=(lt * 25, 0))
    rng = np.random.default_rng(57)
    n = 40
    xs = rng.uniform(0, 1920, n); ys = (rng.uniform(0, 1200, n) - lt * rng.uniform(150, 300, n)) % 1200 - 60
    pop.parts(gl, xs, ys, rng.uniform(40, 90, n), H('ff4f7b'), kind=4, outline=0.08, angle=0.2 * np.sin(t * 3 + xs))
    b = beat(t)
    hop = 26 * abs(math.sin(math.pi * b))
    tex = C.lineup
    w, h = tex.size
    sc = 1500 / w
    gl.quad(tex, gl.rect_h(960, 860 - hop, w * sc, h * sc, angle=0.03 * math.sin(math.pi * b), anchor=(0.5, 1.0)))
    F.chibi_crowd(t, 22, 900, 1110, 9, states=('jump', 'wave'), xr=(-40, 1960), jump_h=60)
    pop.chunk(gl, 'SMILE!', 1640, 160, 120, fill=H('ff6f9c'), edge=INK, fam='impact', weight=None, angle=-0.1, white=6,
              scale=ease_out_back(prog(lt, 0.2, 0.5), 2.5))
    return dict(bloomAmt=0.2, vign=0.05, **flash(t, 134.6, 0.2))


def s_ikou3(t, lt, d):
    return P3.ikou(t, lt, d, 137.8, c=('ff9ac0', 'ffd0e2', 'e8236e'), side=1)


def s_door_full(t, lt, d):
    """まだ見ぬ明日の扉 / みんなで開こうよ: op3 の扉を引き延ばし、扉が開くとミニキャラが駆けていく"""
    gl = C.gl
    post = P3.s_door(t, lt * 4.8 / d, 4.8, ft=lt - 2.4 * d / 4.8)
    k = smooth(prog(lt, 4.6, d))
    if k > 0:
        F.chibi_crowd(t, 12, 960, 1100, seed=141, states=('run',), alpha=k, xr=(100, 1820))
        pop.confetti(gl, t, n=int(80 * k), seed=14, fall=80)
    return post


def s_finale(t, lt, d):
    """目指すはひとつ あの未来へ: ハートの中で歌い → 渦と花火の中の5人"""
    gl = C.gl
    if lt < 3.2:
        dance_bg(t, 'pink', seed=39, dots=0.6)
        rng = np.random.default_rng(58)
        n = 36
        xs = rng.uniform(0, 1920, n); ys = (rng.uniform(0, 1200, n) - lt * 200) % 1200 - 60
        pop.parts(gl, xs, ys, rng.uniform(40, 90, n), H('ffffff'), kind=4, outline=0.08, angle=0.2 * np.sin(t * 3 + xs))

        def pose(tt):
            p = O.base_pose(tt, wind=10, seed=60, sing=True)
            e = smooth(prog(tt, 149.0, 149.4))
            p.expr_full = p.expr_face = (0, 1 - e, 0, 0, e, 0)
            return p
        face('finale', t, 146.6 - 0.5, pose, 1050, 980, lerp(0.72, 0.8, lt / 3.2), 960, 620 - 10 * bounce(t))
        return dict(bloomAmt=0.2, vign=0.05, **flash(t, 146.6, 0.2))
    lt2 = lt - 3.2
    pop.swirl(gl, -lt2 * 0.6, c=(0.5, 0.45), cols=('ffb3d1', 'ffe08a', 'b6e0ff'), line_a=0.7, arms=3.0, tight=1.3)
    F.fireworks(t, [(149.9, 330, 260, (1.0, 0.8, 0.35), 240), (150.6, 1590, 230, (1.0, 0.45, 0.7), 230),
                    (151.4, 520, 180, (0.6, 0.85, 1.0), 220), (152.2, 1420, 160, (0.8, 1.0, 0.6), 230), (153.0, 960, 140, (1.0, 0.9, 0.5), 260)])
    pop.confetti(gl, t, n=90, seed=40, fall=90)
    b = beat(t)
    hop = 28 * abs(math.sin(math.pi * b))
    tex = C.lineup
    w, h = tex.size
    k = ease_out_back(prog(lt2, 0.0, 0.4), 2.0)
    sc = 1700 / w * k
    gl.quad(tex, gl.rect_h(960 + 12, 1100 - hop + 14, w * sc, h * sc, anchor=(0.5, 1.0)), alpha=0.35, tint=(0.3, 0.1, 0.3))
    gl.quad(tex, gl.rect_h(960, 1100 - hop, w * sc, h * sc, angle=0.02 * math.sin(math.pi * b), anchor=(0.5, 1.0)))
    return dict(bloomAmt=0.45, vign=0.05, **flash(t, 149.8, 0.25))


# ---------------------------------------------------------------- アウトロ
def staff_lines():
    return [
        ('主題歌', '「Creative Vision 10！ ～明日へ飛べ～」（v2）'),
        ('歌', F.VX(F.MAIN)),
        ('コーラス', '　'.join(F.VX(n) for n in F.CHORUS)),
        ('作画・撮影・編集', 'Python + GLSL（すべてコードで描画・動画生成AI不使用）'),
        ('キャラクター原画', '表情差分・立ち絵・連続コマ・5人の色違い（静止画の生成画像）'),
        ('ミニキャラ', 'Codex のペット'),
        ('コミット', f'{len(C.clog):,}（うち AI と共同 {C.n_ai:,}）'),
        ('開発した日', f'{C.n_days} 日'),
        ('画面', f'{C.n_views}'),
        ('帳票', f'{C.n_forms}'),
        ('テスト', f'{C.n_tests:,}'),
        ('Creative Vision 10', '2027年1月 リリース予定'),
    ]


def s_outro(t, lt, d):
    """アウトロ: スタッフロール（2ページ）→ 金色のタイトルと花火"""
    gl = C.gl
    if t >= 157.0:
        return P3.chorus_title(t, t - 157.0, 160.2 - 157.0, 157.0, cols=('ffe08a', 'fff2c4', 'ffc94a'), gold=True,
                               fills=[H('ffd23f'), H('ffb02a'), H('ffe36a'), H('ff8a3d')])
    dance_bg(t, 'purple', seed=41, dots=0.5)
    pop.confetti(gl, t, n=50, seed=41, fall=60)
    rows = staff_lines()
    pages = [rows[:6], rows[6:]]
    for pi, page in enumerate(pages):
        t0 = pi * 1.6
        al = min(prog(lt, t0 + 0.05, t0 + 0.3), 1 - prog(lt, t0 + 1.4, t0 + 1.6)) if pi == 0 else prog(lt, 1.65, 1.9)
        if al <= 0:
            continue
        fx.rect(gl, 960 + 10, 400 + 12, 1500, 680, INK, alpha=0.35 * al, angle=-0.02)
        fx.rect(gl, 960, 400, 1500, 680, (1, 1, 1), alpha=0.92 * al, angle=-0.02)
        for i, (a, b) in enumerate(page):
            y = 110 + i * 112 - 12 * (1 - ease_out(al))
            tex = gl.text(a, 24, fam='noto', weight='Bold', color=H('7b3fd0'))
            gl.sprite(tex, 960, y, alpha=al)
            tex = gl.text(b, 36, fam='noto', weight='Black', color=INK)
            gl.sprite(tex, 960, y + 42, alpha=al)
    O2.credit_footer(760, 860, 1350, 885, 170, alpha=min(prog(lt, 0.05, 0.35), 1 - prog(t, 156.8, 157.0)))
    for j, x in enumerate((180, 1740)):
        F.pet('wave', t, x, 1060, scale=1.4, fps=7, phase=j * 2, flip=j == 1)
    return dict(bloomAmt=0.2, vign=0.05, **flash(t, 153.8, 0.2))


SHOTS = [(a, b, fn) for (a, b, fn) in P3.SHOTS if b <= 57.8] + [
    (57.8, 62.6, s_lineup_long),
    (62.6, 64.2, s_to_night),
    (64.2, 70.6, s_interlude),
    (70.6, 73.8, s_files),
    (73.8, 77.0, s_winter),
    (77.0, 80.2, s_stack),
    (80.2, 83.4, s_gather),
    (83.4, 86.6, s_baton),
    (86.6, 89.8, s_smiles200),
    (89.8, 93.0, s_connect),
    (93.0, 96.2, s_memories),
    (96.2, 99.4, s_title2),
    (99.4, 102.6, s_power),
    (102.6, 105.8, s_tears),
    (105.8, 107.4, s_ikou2),
    (107.4, 112.2, s_dawn),
    (112.2, 115.4, s_run),
    (115.4, 121.8, s_paint),
    (121.8, 128.2, s_galaxy),
    (128.2, 131.4, s_title3),
    (131.4, 134.6, s_voices),
    (134.6, 137.8, s_smiles),
    (137.8, 139.4, s_ikou3),
    (139.4, 146.6, s_door_full),
    (146.6, 153.8, s_finale),
    (153.8, 160.2, s_outro),
    (160.2, 1e9, P3.s_ending),
]

NO_KARAOKE = [(38.6, 41.8), (48.2, 49.8), (96.2, 99.4), (105.8, 107.4), (128.2, 131.4), (137.8, 139.4), (153.8, 1e9)]


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
                O2.CUR_SHIFT = getattr(fn, 'shift', 0.0)
                p = fn(ts, ts - a, b - a) or {}
                O2.CUR_SHIFT = 0.0
                break
        if k == MB // 2:
            post = p
        gl.use(gl.main)
        if 13.0 <= t and not any(a <= t < b for a, b in NO_KARAOKE):
            O.karaoke(t)
        P3.credits(t)
        if MB > 1:
            gl.accumulate(1.0 / MB, k == 0)
    pp = dict(grainAmt=0.012, vign=0.1)
    pp.update(post)
    gl.time = t
    if MB > 1:
        pp['src'] = gl.acc
    return gl.finish(pp)


def audio():
    return F2.audio()
