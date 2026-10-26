# 作品1 v3「明日へ飛べ」ポップ版（主題歌 v2 の1番＋エンディングの一撃、約68秒）
# op2 と同じ歌・歌詞・実データ・主人公の素材を使い、画づくりを明るいポップな OP 調に作り替える。
# 参考にしたのは演出の型だけ（白地の紙吹雪とロゴ、見上げた夜の街、渦、文字が1つずつ弾む見出し、拍で色が変わる水玉と同心円の背景、
# 大きな薄い英字、傾いたカード、舞台のフレア、夕焼けの筋雲、ハートの噴き上がり、ネオンのロゴ）。参考動画の映像・音声は使わない。
# 背景・文字・図形はすべて pop.py のシェーダで描き、主人公は op2 と同じ素材（6表情のリグ・立ち絵・連続コマ・5人の色違い）で合成する。
# 基本は動画生成AIを使わない。人物の全身の動作（連続コマ）だけ動画生成AI（Wan 2.2）で作る（op2.py と同じ）。
#
# 秒割り（音源の時刻。0.2秒の無音の後、150BPM・1拍0.4秒・1小節1.6秒）
#    0.0- 3.4  白地に紙吹雪、ロゴの板が拍で弾む → 円で閉じる              イントロ
#    3.4- 6.6  見上げた夜の街が回る、主人公が横切って飛ぶ
#    6.6- 9.8  街が渦に変わり、主人公が渦の中心へ飛び込む
#    9.8-13.0  渦の上に「明日へ」「飛べ！」が1文字ずつ弾み、タイトルの札
#   13.0-16.2  青の水玉と同心円、gRPC の呼び出し名と並んで飛ぶ            Aメロ1「風より速く 届くよ」
#   16.2-19.4  黄〜赤、立ち絵が拍で踊り、技術のシールが貼られていく        「オープンな夢 広がる」
#   19.4-22.6  舞台のフレアとミニキャラの客席、AI と共同の実コミット数      「AIと手を つないで」
#   22.6-25.8  夕焼けの筋雲の顔の寄り → 大きな太陽を見る後ろ姿            「新しい朝 つくろう」
#   25.8-29.0  拍ごとに色が変わる地に、実画面のカードが傾いて並ぶ（実画面数） Bメロ1「いくつもの画面」
#   29.0-32.2  実テスト名の札に PASS の判が押され、困り顔→笑顔（実テスト数） 「何度も確かめ」
#   32.2-35.4  毎日のコミットのタイルの道を歩いていく（実データ）           「毎日 前へ進む」
#   35.4-38.6  拍ごとの寄り（リボン・イヤリング・瞳）→ 星とハートが噴き上がる 「今 輝くとき」
#   38.6-41.8  渦の上にタイトルの文字が歌に合わせて弾む                    サビ1「Creative Vision 10！」
#   41.8-45.0  見上げた街の上を右上へ飛ぶ                                   「明日へ 今 飛び出せ」
#   45.0-48.2  実画面が渦を巻いて吸い込まれ、主人公も中心へ飛び込む         「広がる世界 飛び込んでく」
#   48.2-49.8  いこう！（ハイテンション）
#   49.8-54.6  青空の雲の上、光の扉へ手を伸ばすと扉が開く                   「まだ見ぬ明日の扉」
#   54.6-57.8  黄色の水玉とハートの中で歌う                                 「開けよう 今すぐに」
#   57.8-61.0  5人の色違いが並んで弾む                                     「目指すはひとつ あの未来へ」
#   61.0-64.2  ピンクの水玉、立ち絵とネオンのロゴ
#   64.2-67.6  エンディング（ドラムソロ: 色の地のカード → 一撃でクレジットと QR）
#   python film.py op3                      四国めたん版 → out/op3_metan.mp4
#   python film.py op3 --variant zundamon   歌い手の版（opfull.py の SINGERS）
import os, math, glob
import numpy as np
import op2 as P2                # 歌詞 v2・素材・道具（op2 の読み込みで op / opfull もショートの切り方になる）
import engine as E, rig_p as RP, fx, pop
import songsync as SS
import op as O
import opfull as F
from engine import prog, lerp, smooth, ease_out, ease_in, ease_io, ease_out_back, pulse, clamp

FPS = 30
SUPERSAMPLE = 2
SINGER = P2.SINGER
OUT_NAME = f'op3_{SINGER}'
T_CUT = P2.T_CUT
DURATION = P2.DURATION
C = None
H = E.hexc
INK = H('2a1a5e')               # 文字の縁取り（濃い紫）
BEAT = SS.BEAT


def beat(t):
    """拍の番号（小数）"""
    return (t - SS.LEAD) / BEAT


def bounce(t, k=1.0):
    """拍で跳ねる 0..1（拍の頭で 0、拍の中ほどで 1）"""
    return abs(math.sin(math.pi * beat(t))) * k


# ---------------------------------------------------------------- 準備
def setup(gl):
    global C
    P2.setup(gl)
    C = P2.C
    C.stand = {k: RP.Standee(gl, k) for k in ('fullbody3d', 'fullbody-back', 'fullbody')}
    C.lineup = gl.image(os.path.join(E.ASSETS4, 'dance-parts-v1', 'cast-lineup.png'), key=('op3', 'lineup'))
    C.screen_names = [os.path.basename(p)[:-4] for p in sorted(glob.glob(os.path.join(E.ASSETS, 'screens', '*.jpg')))]
    # 立ち絵・表情から線画のカード（エンディングのドラムソロ）
    wd = os.path.join(E.WORK, 'op3')
    src = [(os.path.join(E.WORK, 'stand', 'fullbody3d.png'), 'MAGICIAN'),
           (os.path.join(E.ASSETS4, 'github-logo-onlyperson-smile-open.png'), 'SMILE'),
           (os.path.join(E.WORK, 'stand', 'fullbody-back.png'), 'BACK'),
           (os.path.join(E.ASSETS4, 'github-logo-onlyperson-hightention.png'), 'IKOU!'),
           (os.path.join(E.WORK, 'stand', 'fullbody-smile.png'), 'WORK'),
           (os.path.join(E.ASSETS4, 'github-logo-onlyperson-worried.png'), 'DEBUG')]
    C.inks, C.inks_color = [], []
    for p, label in src:
        out = pop.lineart(p, os.path.join(wd, 'ink_' + os.path.basename(p)))
        C.inks.append((gl.image(out, key=('op3', 'ink', out)), label))
        C.inks_color.append(gl.image(out[:-4] + '_color.png', key=('op3', 'inkc', out)))


def stand_look(**kw):
    d = dict(outlineW=0.0)
    d.update(kw)
    return d


def face(key, t, t0, pose_fn, cx, cy, zoom, sx, sy, rot=0.0, look=None):
    """主人公の顔の寄り（白い縁取りのシール風）"""
    lk = dict(cropBottom=1600, outlineW=10.0, outlineCol=(1, 1, 1))
    if look:
        lk.update(look)
    Hm = O.char_H(cx, cy, zoom, sx=sx, sy=sy, rot=rot)
    O.draw_char(key, t, t0, pose_fn, Hm, lk)
    return Hm


def sing(seed, expr=(0, 1, 0, 0), wind=10.0, head=0.0):
    return lambda tt: O.base_pose(tt, wind=wind, head=head, expr=expr, sing=True, seed=seed)


def giant_type(t, words, y, size=300, col=(1, 1, 1), alpha=0.32, speed=90.0, angle=-0.22, fam='impact'):
    """背景に流れる大きな薄い英字"""
    gl = C.gl
    tex = gl.text(words, size, fam=fam, weight=None, color=col)
    w, h = gl.vsize(tex)
    x0 = (-t * speed) % (w + 120)
    c, s = math.cos(angle), math.sin(angle)
    for k in range(-1, 3):
        x = x0 + k * (w + 120) - 300
        gl.sprite(tex, 960 + (x - 960) * c, y + (x - 960) * s, angle=angle, alpha=alpha, anchor=(0, 0.5))


SCHEMES = {
    'blue': (H('1d3fc8'), H('6fd2ff'), H('bfe8ff')),
    'teal': (H('0f6fa8'), H('54e0d0'), H('d0fff6')),
    'yellow': (H('ffe14a'), H('ff8a1e'), H('fff2b0')),
    'red': (H('ff9a1e'), H('e8232e'), H('ffd27a')),
    'green': (H('d4f03a'), H('2f9e3a'), H('f6ffb0')),
    'purple': (H('6a3cf0'), H('e3b6ff'), H('f2e0ff')),
    'pink': (H('ff6f9c'), H('ffc2d8'), H('fff0f6')),
    'lemon': (H('fff06a'), H('ffd23f'), H('ffffff')),
}


def dance_bg(t, scheme, seed=0, dots=0.5, circles=True, dot_col=(1, 1, 1)):
    c0, c1, cc = SCHEMES[scheme]
    pop.popbg(C.gl, c0, c1, ang=0.75, circles=pop.spot_circles(t, seed, 4, 1.0, 0.6) if circles else (), circ_col=tuple(0.55 * np.array(cc)),
              dot_col=dot_col, dot_a=dots, gap=44, dot_r=3.0, dot_off=(t * 12, t * 6))


def flash(t, t0, dur=0.25, col=(1, 1, 1)):
    """カットの頭の閃光（カットが多いので op より弱く短く）"""
    return dict(flash=0.55 * pulse(t, t0, 0.02, dur * 0.6), flashCol=col)


def bar_flash(t, t0, col=(1, 1, 1)):
    """小節の頭の白い円のワイプ（広がって抜ける）"""
    k = prog(t, t0, t0 + 0.28)
    if 0 < k < 1:
        pop.wipe(C.gl, 960, 540, 1300 * ease_out(k, 2), col, inside=True, alpha=1 - k)


def stars_bg(t, n=60, seed=3, col=H('ffe36a'), area=(0, 0, 1920, 700), size=(8, 22)):
    rng = np.random.default_rng(seed)
    xs = rng.uniform(area[0], area[2], n); ys = rng.uniform(area[1], area[3], n)
    tw = 0.6 + 0.4 * np.sin(t * rng.uniform(2, 5, n) + rng.uniform(0, 6, n))
    pop.parts(C.gl, xs, ys, rng.uniform(*size, n) * tw, col, kind=3, angle=rng.uniform(-0.3, 0.3, n))


def star_trail(xs, ys, t, n=26, seed=0, size=36):
    """飛ぶ主人公の後ろに残る星とハートの帯"""
    rng = np.random.default_rng(seed + int(t * 30))
    q = np.linspace(0, 1, n)
    pal = pop.CONF_PAL[np.arange(n) % len(pop.CONF_PAL)]
    jitter = rng.normal(0, 18, (n, 2))
    pop.parts(C.gl, xs(q) + jitter[:, 0], ys(q) + jitter[:, 1], size * (1 - q) + 6, pal, kind=3, alpha=1 - q * 0.8,
              angle=q * 4 + t * 3)


# ---------------------------------------------------------------- イントロ
def s_open(t, lt, d):
    """白地に紙吹雪、ロゴの板が拍で弾む"""
    gl = C.gl
    pop.popbg(gl, H('ffffff'), H('fff6fb'), dot_a=0.0)
    pop.confetti(gl, t, n=110, seed=2, fall=55)
    k = ease_out_back(prog(lt, 0.25, 0.75), 2.2)
    if k > 0:
        b = 1 + 0.035 * (1 - prog(beat(t) % 1.0, 0, 0.5)) * (lt > 0.8)
        x, y, ang = 960, 520 + 8 * math.sin(lt * 2.2), -0.06
        sc = k * b
        # 立体の板（ずらした影＋角の丸い板）
        pop.parts(gl, [x + 26 * sc], [y + 30 * sc], [820 * sc], H('8a6be0'), kind=5, ratio=0.5, angle=[ang])
        pop.parts(gl, [x], [y], [820 * sc], H('6fe0b4'), kind=5, ratio=0.5, angle=[ang], outline=0.04)
        pop.chunk(gl, 'Creative Vision', x - 10 * sc, y - 70 * sc, 92, fill=(1, 1, 1), edge=H('1f7a63'), shadow=H('1f7a63'),
                  fam='comic', weight=None, scale=sc, angle=ang)
        pop.chunk(gl, '10', x + 240 * sc, y + 70 * sc, 150, fill=H('ffd23f'), edge=INK, shadow=INK, fam='impact', weight=None,
                  scale=sc, angle=ang + 0.08)
        pop.chunk(gl, '明日へ飛べ', x - 120 * sc, y + 80 * sc, 70, fill=H('ff6f9c'), edge=INK, shadow=INK, scale=sc, angle=ang)
    sub = gl.text('PROMOTION ANIME  v3', 26, fam='segoeb', weight=None, color=INK, tracking=0.3)
    gl.sprite(sub, 960, 820, alpha=prog(lt, 1.0, 1.4) * 0.85)
    # 円で閉じる（夜の街へ）
    kw = prog(lt, d - 0.4, d)
    if kw > 0:
        pop.wipe(gl, 960, 540, 1250 * ease_in(kw, 2), H('141a52'), inside=True)
    return dict(bloomAmt=0.2, vign=0.0, fade=1 - prog(lt, 0, 0.3), grainAmt=0.01)


def night_sky(t):
    pop.popbg(C.gl, H('101548'), H('2c3a96'), ang=1.57, dot_a=0.0)
    stars_bg(t, n=70, seed=4, area=(0, 0, 1920, 1080))


def s_city(t, lt, d):
    """見上げた夜の街が回り、主人公が横切って飛ぶ"""
    gl = C.gl
    night_sky(t)
    k = ease_io(lt / d, 2)
    roll = lerp(0.32, -0.08, k)
    pop.city(gl, t, vp=(960 + 260 * math.sin(lt * 0.6), lerp(-1700, -1300, k)), roll=roll, scroll=-lt * 140, seed=5)
    # 横切って飛ぶ（右下 → 左上）
    f = prog(lt, 1.0, 2.9)
    if 0 < f < 1:
        x = lerp(2200, -300, f); y = lerp(820, 260, f) + 30 * math.sin(f * 6)
        star_trail(lambda q: x + q * 700, lambda q: y + q * 200, t, seed=1)
        fb = C.fb['flight']
        fb.draw(fb.H(x, y, 520, angle=0.25, flip=True, by='width'), t, P2.fb_at('flight', lt), breath=0.0,
                look=stand_look(outlineW=6.0, outlineCol=(1, 1, 1)))
    return dict(bloomAmt=0.35, vign=0.2, **flash(t, 3.4, 0.2, (1, 1, 1)))


def s_swirl_in(t, lt, d):
    """街が渦に変わり、主人公が渦の中心へ飛び込む"""
    gl = C.gl
    rot = lt * 0.9
    hole = lerp(0.42, 0.06, ease_out(prog(lt, 0.0, 1.8), 2))
    pop.swirl(gl, rot, c=(0.5, 0.48), hole=hole, hole_col='1a1f5e')
    # 漂う星・三日月・水玉
    rng = np.random.default_rng(9)
    n = 40
    a0 = rng.uniform(0, 6.3, n); r0 = rng.uniform(80, 900, n)
    r = (r0 + lt * 160) % 1000 + 40
    a = a0 - lt * 0.6
    xs = 960 + np.cos(a) * r * 1.2; ys = 518 + np.sin(a) * r * 0.8
    cols = pop.CONF_PAL[np.arange(n) % len(pop.CONF_PAL)]
    pop.parts(gl, xs, ys, 18 + r * 0.03, cols, kind=3, angle=a)
    pop.parts(gl, xs[::2] + 40, ys[::2] - 30, 10 + r[::2] * 0.015, (1, 1, 1), kind=2)
    # 中心へ渦を描いて飛び込む
    f = ease_in(prog(lt, 0.2, d - 0.2), 1.6)
    ang = -0.5 + f * 5.0
    rr = lerp(820, 10, f)
    x = 960 + math.cos(ang) * rr * 1.1; y = 518 + math.sin(ang) * rr * 0.7
    size = lerp(560, 40, f)
    if size > 45:
        fb = C.fb['flight']
        fb.draw(fb.H(x, y, size, angle=ang + 1.4, by='width'), t, P2.fb_at('flight', lt), breath=0.0,
                look=stand_look(outlineW=5.0, outlineCol=(1, 1, 1)))
    return dict(bloomAmt=0.3, vign=0.15, **flash(t, 6.6, 0.25))


def s_title(t, lt, d):
    """渦の上に「明日へ」「飛べ！」が1文字ずつ弾み、最後にタイトルの札"""
    gl = C.gl
    if lt < 2.7:
        pop.swirl(gl, 0.9 * (t - 6.6), c=(0.5, 0.48), hole=0.06)
        b = beat(t)
        pop.pop_word(gl, t, '明日へ', [9.8, 10.0, 10.2], 700, 330, 250, [H('ff6f9c')], edge=INK, angle=-0.1, jump=14, beat=b, white=8)
        pop.pop_word(gl, t, '飛べ！', [10.6, 10.8, 11.0], 1260, 600, 290, [H('ffd23f')], edge=INK, angle=0.06, jump=16, beat=b + 0.5,
                     white=8)
        word = 'CREATIVE VISION 10'
        times = np.linspace(11.4, 12.2, len(word))
        pop.pop_word(gl, t, word, times, 960, 880, 104, [H('6fd8ff'), H('b6f23c'), H('ff8a3d')], edge=INK, fam='impact',
                     weight=None, kern=0.95, wobble=0.08, white=6)
        if lt < 0.12:
            pop.wipe(gl, 960, 518, 60 + 2000 * lt, (1, 1, 1), inside=True, alpha=1 - lt / 0.12)
        return dict(bloomAmt=0.2, vign=0.1)
    # タイトルの札（白地にピンクの水玉）
    lt2 = lt - 2.7
    pop.popbg(gl, H('ffffff'), H('fff3f8'), dot_col=H('ff9cc0'), dot_a=1.0, gap=40, dot_r=4.0, dot_off=(lt2 * 30, 0))
    k = ease_out_back(prog(lt2, 0.0, 0.3), 2.0)
    gl.sprite(C.logo, 600, 520, scale=0.42 * k, alpha=0.9, tint=(0.35, 0.3, 0.45), add=(0.55, 0.4, 0.8))
    pop.chunk(gl, '明日へ飛べ', 1120, 430, 150, fill=H('ff6f9c'), edge=INK, scale=k, angle=-0.05, white=8)
    pop.chunk(gl, 'Creative Vision 10', 1120, 610, 110, fill=H('ffd23f'), edge=INK, fam='comic', weight=None, scale=k, angle=-0.05,
              white=8)
    return dict(bloomAmt=0.2, vign=0.0, **flash(t, 9.8 + 2.7, 0.15))


# ---------------------------------------------------------------- Aメロ1
def s_wind(t, lt, d):
    """風より速く: 青の水玉と同心円、gRPC の呼び出し名と並んで飛ぶ"""
    gl = C.gl
    scheme = 'blue' if lt < 1.6 else 'teal'
    dance_bg(t, scheme, seed=1)
    giant_type(t, 'gRPC  HTTP/2  gRPC  HTTP/2', 330, alpha=0.28, speed=160)
    giant_type(t + 7, 'FAST  FAST  FAST  FAST', 860, alpha=0.22, speed=-120)
    # 呼び出し名の札が右から左へ追い越していく
    rng = np.random.default_rng(70)
    for j in range(9):
        sp = rng.uniform(900, 1500); ph = rng.uniform(0, 3); y = rng.uniform(260, 900)
        x = 2300 - ((lt + ph) * sp) % 2900
        name = C.grpc[j % len(C.grpc)]
        tex = gl.text(name, 30, fam='mono', weight=None, color=INK)
        w, h = gl.vsize(tex)
        fx.rect(gl, x + w / 2, y, w + 34, h + 12, (1, 1, 1), alpha=0.92)
        gl.sprite(tex, x, y, anchor=(0, 0.5))
        pop.parts(gl, x + w + 40 + np.arange(5) * 34, np.full(5, y), 26 - np.arange(5) * 4, (1, 1, 1), kind=3, alpha=0.9)
    x = 1180 + 40 * math.sin(lt * 1.7); y = 560 + 26 * math.sin(lt * 3.1)
    star_trail(lambda q: x - 250 - q * 900, lambda q: y + 60 + 50 * np.sin(q * 7 + lt * 6), t, seed=3, size=40)
    fb = C.fb['flight']
    fb.draw(fb.H(x, y, 1150, angle=-0.05 + 0.03 * math.sin(lt * 2.3), by='width'), t, P2.fb_at('flight', lt), breath=0.0,
            look=stand_look(outlineW=8.0, outlineCol=(1, 1, 1)))
    bar_flash(t, 14.6)
    return dict(bloomAmt=0.25, vign=0.1, **flash(t, 13.0, 0.2))


TECH = [('.NET 10', 'ff4f7b'), ('WPF', '3fd0ff'), ('gRPC', 'b6f23c'), ('SQLite', 'ffd23f'), ('PostgreSQL', '7b8cff'),
        ('MariaDB', 'ff8a3d'), ('MVVM', 'f7a6c8'), ('protobuf', '7be0a0'), ('GitHub', 'ffffff')]


def s_open2(t, lt, d):
    """オープンな夢: 立ち絵が拍で踊り、技術のシールが貼られていく"""
    gl = C.gl
    scheme = 'yellow' if lt < 1.6 else 'red'
    dance_bg(t, scheme, seed=2)
    giant_type(t, 'OPEN SOURCE  OPEN SOURCE', 300, alpha=0.3, speed=110)
    giant_type(t + 3, 'OPEN  DREAM  OPEN  DREAM', 900, alpha=0.24, speed=-90)
    # シール（拍ごとに1枚）
    rng = np.random.default_rng(12)
    spots = [(330, 300), (1560, 260), (250, 720), (1650, 640), (520, 520), (1420, 860), (380, 930), (1760, 420), (700, 230)]
    for i, (nm, col) in enumerate(TECH):
        t0 = 16.2 + i * BEAT * 0.9
        k = ease_out_back(prog(t, t0, t0 + 0.25), 3.0)
        if k <= 0:
            continue
        x, y = spots[i]
        ang = rng.uniform(-0.25, 0.25)
        tex = gl.text(nm, 46, fam='segoeb', weight=None, color=INK)
        w, h = gl.vsize(tex)
        sz = max(w + 60, 150)
        pop.parts(gl, [x + 8], [y + 10], [sz * k], INK, kind=2, alpha=0.35)
        pop.parts(gl, [x], [y], [sz * k], H(col), kind=2, outline=0.05)
        gl.sprite(tex, x, y, scale=k * min(1.0, (sz - 40) / w), angle=ang)
    # 立ち絵が踊る（拍で揺れて跳ねる）
    b = beat(t)
    sway = 0.07 * math.sin(math.pi * b)
    hop = 34 * abs(math.sin(math.pi * b))
    st = C.stand['fullbody3d']
    st.draw(st.H(960, 1130 - hop, 1000), t, wind=10 * math.sin(t * 2), hem=6.0, sway=sway, breath=0.02 * abs(math.sin(math.pi * b)),
            look=stand_look(outlineW=8.0, outlineCol=(1, 1, 1)))
    bar_flash(t, 17.8)
    return dict(bloomAmt=0.2, vign=0.1, **flash(t, 16.2, 0.2))


def s_stage(t, lt, d):
    """AIと手を: 暗い舞台、フレア、ミニキャラの客席。AI と共同の実コミット数"""
    gl = C.gl
    pop.popbg(gl, H('120e36'), H('2a1b6e'), ang=1.57, circles=pop.spot_circles(t, 7, 3, 0.9, 0.25), circ_col=(0.35, 0.3, 0.6),
              dot_col=H('8f7bff'), dot_a=0.25)
    # 照明の帯
    for j, (x0, sp) in enumerate(((420, 0.9), (1500, -0.8))):
        ang = 0.35 * math.sin(t * sp + j)
        fx.rect(gl, x0, 0, 260, 2600, (0.55, 0.6, 1.0), alpha=0.16, angle=ang, anchor=(0.5, 0.0), blend='add')
    hx = face('stage', t, 19.4 - 0.5, sing(22, expr=(0, 1, 0, 0), wind=8), 1050, 980, 0.72, 760, 560,
              look=dict(rimCol=(0.7, 0.9, 1.6), rimDir=(1, 0.1), rimW=8))
    # 客席（ミニキャラの影）
    F.chibi_crowd(t, 26, 960, 1120, 5, states=('wave', 'jump'), xr=(-60, 1980), jump_h=40)
    fx.grad(gl, 960, 1000, 2200, 200, (0.05, 0.03, 0.15, 0.0), (0.05, 0.03, 0.15, 0.7))
    pop.lens_flare(gl, 300 + 120 * math.sin(t * 0.7), 150, t, 1.0, (0.5, 1.0, 0.8), 0.9)
    pop.lens_flare(gl, 1650 + 90 * math.sin(t * 0.9 + 1), 120, t, 0.8, (0.7, 0.7, 1.2), 0.8)
    # 実数
    kc = ease_out(prog(lt, 0.6, 2.0), 2)
    if kc > 0:
        pop.chunk(gl, f'{int(C.n_ai * kc):,}', 1480, 400, 190, fill=H('ffd23f'), edge=INK, fam='impact', weight=None,
                  scale=ease_out_back(prog(lt, 0.6, 0.9), 2), angle=-0.06, white=6)
        pop.chunk(gl, 'commits with AI', 1480, 540, 54, fill=(1, 1, 1), edge=INK, fam='segoeb', weight=None, angle=-0.06,
                  alpha=prog(lt, 0.9, 1.2))
        for i, (nm, cnt) in enumerate(C.ai_fam[:4]):
            t0 = 1.2 + i * 0.25
            k = ease_out_back(prog(lt, t0, t0 + 0.25), 2.5)
            x = 1190 + i * 200; y = 700 + 12 * math.sin(t * 4 + i)
            pop.parts(gl, [x], [y], [180 * k], H(TECH[i][1]), kind=5, ratio=0.42, outline=0.05, angle=[0.1 * (i % 2 - 0.5)])
            tex = gl.text(nm, 30, fam='segoeb', weight=None, color=INK)
            gl.sprite(tex, x, y, scale=k * min(1.0, 150 / gl.vsize(tex)[0]), angle=0.1 * (i % 2 - 0.5))
    return dict(bloomAmt=0.8, vign=0.35, **flash(t, 19.4, 0.2))


def s_sunset(t, lt, d):
    """新しい朝: 夕焼けの筋雲。顔の寄り → 大きな太陽を見る後ろ姿"""
    gl = C.gl
    pop.sunset(gl, t)
    if lt < 1.6:
        def pose(tt):
            p = O.base_pose(tt, wind=22 + 6 * math.sin(tt * 1.7), seed=23, sing=True)
            e = smooth(prog(tt, 23.2, 23.6))
            p.expr_full = p.expr_face = (1 - e, e, 0, 0)
            return p
        z = lerp(0.95, 1.05, lt / 1.6)
        face('sunset', t, 22.6 - 0.5, pose, 1120, 900, z, 760, 560, look=dict(outlineW=0.0, rimCol=(2.0, 1.2, 0.5), rimDir=(1, -0.1),
                                                                                rimW=10, tintAdd=(0.08, 0.02, 0.0)))
        return dict(bloomAmt=0.4, vign=0.2, **flash(t, 22.6, 0.2))
    lt2 = lt - 1.6
    k = ease_out(lt2 / 1.6, 2)
    pop.parts(gl, [1300], [lerp(760, 680, k)], [760], H('fff1a8'), kind=2)
    fx.parts(gl, [1300], [lerp(760, 680, k)], [1400], (1.0, 0.7, 0.3), [0.6], kind=0)
    # 地平の街のシルエット
    rng = np.random.default_rng(4)
    x = -40
    while x < 1960:
        w = rng.uniform(60, 160); h = rng.uniform(60, 220)
        fx.rect(gl, x + w / 2, 1080 - h / 2, w, h, H('6a0f2a'))
        x += w - 4
    st = C.stand['fullbody-back']
    st.draw(st.H(lerp(700, 660, k), 1180, 900), t, wind=14, hem=4.0, look=stand_look(silhouette=0.55, silCol=H('4a0a24'),
                                                                                 rimCol=(2.0, 1.3, 0.5), rimDir=(1.0, 0.0), rimW=6))
    return dict(bloomAmt=0.6, vign=0.25, **flash(t, 24.2, 0.12))


# ---------------------------------------------------------------- Bメロ1
WEDGE = ['e53935', '1e5bd8', '7b2fe0', 'f39a1f', '1fa83a', 'ff5d9e', '14b8d8', 'ffc61a']


def wedge_bg(i, lt_beat):
    """白地に斜めの色の帯（拍ごとに色が変わる）"""
    gl = C.gl
    pop.popbg(gl, H('f7f5ee'), H('ffffff'), dot_a=0.0)
    col = H(WEDGE[i % len(WEDGE)])
    fx.rect(gl, 960 + 60 * (i % 2 - 0.5), 540, 760, 1800, col, angle=0.28 * (1 if i % 2 else -1))
    fx.rect(gl, 960 + 520 * (1 if i % 2 else -1), 540, 140, 1800, col, alpha=0.6, angle=0.28 * (1 if i % 2 else -1))


def s_screens(t, lt, d):
    """いくつもの画面: 実画面のカードが拍ごとに傾いて並ぶ。画面の数は実数"""
    gl = C.gl
    i = int(lt / BEAT)
    lb = (lt % BEAT) / BEAT
    wedge_bg(i, lb)
    n = len(C.screens_all)
    for side in (0, 1):
        j = (i * 7 + side * 31) % n
        sgn = -1 if side == 0 else 1
        enter = ease_out_back(prog(lb, 0.0, 0.35), 1.8)
        x = 960 + sgn * 450 + sgn * 160 * (1 - enter) - 30 * lb * sgn
        y = 530 + (40 if side else -30)
        ang = sgn * (0.09 + 0.03 * (i % 3))
        pop.card(gl, x, y, 820, 600, ang, tex=C.screens_all[j], label=C.screen_names[j], label_size=30)
    k = ease_out(prog(lt, 0.2, d - 0.4), 2)
    pop.chunk(gl, f'{int(C.n_views * k):,}', 1700, 930, 150, fill=H('ffd23f'), edge=INK, fam='impact', weight=None, angle=-0.08,
              white=6)
    pop.chunk(gl, 'screens', 1700, 1010, 44, fill=(1, 1, 1), edge=INK, fam='segoeb', weight=None, angle=-0.08)
    return dict(bloomAmt=0.15, vign=0.05, flash=pulse(lt % BEAT, 0.0, 0.01, 0.05) * 0.35)


def s_tests(t, lt, d):
    """何度も確かめ: 実テスト名の札に PASS の判が押され、困り顔 → 笑顔"""
    gl = C.gl
    dance_bg(t, 'green', seed=4, dots=0.45)
    giant_type(t, 'TEST  PASS  TEST  PASS', 520, alpha=0.25, speed=80, angle=-0.18)
    t_smile = 31.0

    def pose(tt):
        p = O.base_pose(tt, wind=6, seed=25, sing=True)
        e = smooth(prog(tt, t_smile, t_smile + 0.4))
        p.expr_full = p.expr_face = (0, e, 0, 0, 0, 1 - e)
        return p
    face('tests3', t, 29.0 - 0.5, pose, 1050, 980, 0.7, 1420, 600)
    # 札（8分音符ごとに1枚、下から積み上がる）
    rate = 1 / (BEAT / 2)
    n_show = int(lt * rate) + 1
    for j in range(7):
        i = n_show - 1 - j
        if i < 0:
            break
        y = 860 - j * 86
        name = C.test_names[i % len(C.test_names)]
        if len(name) > 30:
            name = name[:29] + '…'
        k = ease_out_back(prog(lt, i / rate, i / rate + 0.12), 2) if j == 0 else 1.0
        tex = gl.text(name, 30, fam='noto', weight='Bold', color=INK)
        w, h = gl.vsize(tex)
        x = 120 + 30 * (i % 2)
        ang = 0.03 * (1 if i % 2 else -1)
        fx.rect(gl, x + (w + 40) / 2 + 6, y + 8, w + 40, 64, INK, alpha=0.4, angle=ang)
        fx.rect(gl, x + (w + 40) / 2, y, w + 40, 64, (1, 1, 1), alpha=1.0, angle=ang)
        gl.sprite(tex, x + 20, y, anchor=(0, 0.5), scale=1.0, alpha=min(1, k), angle=ang)
        # PASS の判（少し遅れて押す）
        ks = prog(lt, i / rate + 0.08, i / rate + 0.16)
        if ks > 0:
            sc = lerp(2.2, 1.0, ease_out(ks, 3))
            sx = x + w + 90
            pop.parts(gl, [sx], [y], [110 * sc], H('e8232e'), kind=6, alpha=0.9 * ks, angle=[0.0])
            pop.chunk(gl, 'PASS', sx, y, 34, fill=H('e8232e'), edge=(1, 1, 1), shadow=(1, 1, 1), fam='impact', weight=None,
                      scale=sc, angle=-0.25, alpha=ks, depth=0.0, stroke=3)
    k = ease_in(prog(lt, 0.0, d - 0.3), 1.6)
    pop.chunk(gl, f'{int(C.n_tests * k):,}', 560, 150, 120, fill=H('ffd23f'), edge=INK, fam='impact', weight=None, angle=-0.05, white=6)
    pop.chunk(gl, 'tests passed', 560, 235, 40, fill=(1, 1, 1), edge=INK, fam='segoeb', weight=None, angle=-0.05)
    if t >= t_smile:
        rng = np.random.default_rng(2)
        q = prog(t, t_smile, t_smile + 1.0)
        n = 24
        a = rng.uniform(0, 6.3, n)
        r = ease_out(q, 2) * rng.uniform(200, 520, n)
        pop.parts(gl, 1420 + np.cos(a) * r, 520 + np.sin(a) * r * 0.8, rng.uniform(20, 46, n) * (1 - q * 0.5),
                  pop.CONF_PAL[np.arange(n) % 7], kind=3, alpha=1 - q * 0.6, angle=a)
    return dict(bloomAmt=0.2, vign=0.1, **flash(t, 29.0, 0.2))


TILE_PAL = [H('e9f1ff'), H('9be7ff'), H('ffd23f'), H('ff8a3d'), H('ff4f7b')]


def s_days(t, lt, d):
    """毎日前へ: 毎日のコミットのタイルの道を歩いていく（実データ）"""
    gl = C.gl
    O.sky_layer(t, 'day', cam=(0, -60), cloud_y=760, scroll=-20, n=7, scale=0.6, haze=0.3, seed=21)
    weeks, rows = C.heat_dims
    rev = ease_io(prog(lt, 0.0, d), 1.3)
    travel = lerp(weeks * 0.4, weeks - 6, rev)     # 速く進むと残像が重なって見えるので、後半の週だけを歩く
    cam = E.Cam3(eye=(0, 1.9, travel - 4.0), target=(0, 0.2, travel + 10.0), fov=52)
    pts, cols, sizes, ratios, al = [], [], [], [], []
    for w in range(max(0, int(travel) - 4), min(weeks, int(travel) + 28)):
        for k in range(rows):
            v, inr = C.heat_arr[k, w, 0], C.heat_arr[k, w, 1]
            if inr <= 0:
                continue
            c = np.array([(k - 3) * 1.05, 0.0, w * 1.05])
            p, z = cam.project(np.array([c, c + [0, 0, 0.45], c + [0.45, 0, 0]]))
            if z[0] < 0.6:
                continue
            wd = abs(p[2, 0] - p[0, 0]) * 2
            ht = abs(p[1, 1] - p[0, 1]) * 2
            age = travel + 6 - w
            pk = ease_out_back(clamp(age * 1.5), 2.0)
            pts.append(p[0]); sizes.append(wd * pk); ratios.append(clamp(ht / max(wd, 1e-3), 0.05, 1.0))
            li = 0 if v <= 0 else min(4, int(v * 4 + 0.01))
            cols.append(TILE_PAL[li]); al.append(clamp((40 - z[0]) / 10))
    if pts:
        pts = np.array(pts)
        pop.parts(gl, pts[:, 0], pts[:, 1], np.array(sizes), np.array(cols), kind=5, alpha=np.array(al), ratio=np.array(ratios),
                  outline=0.06)
    # 後ろ姿が歩く
    fb = C.fb['walk']
    fb.draw(fb.H(960, 1180, 900), t, P2.fb_at('walk', lt), breath=0.0, look=stand_look(outlineW=6.0, outlineCol=(1, 1, 1)))
    wi = min(int(travel + 2), weeks - 1)
    pop.chunk(gl, C.heat_weeks[wi].replace('-', '.'), 1820, 120, 80, fill=H('ffd23f'), edge=INK, fam='impact', weight=None,
              anchor=(1, 0.5), white=5)
    pop.chunk(gl, f'{C.n_days} days · {len(C.clog):,} commits', 1820, 200, 36, fill=(1, 1, 1), edge=INK, fam='segoeb', weight=None,
              anchor=(1, 0.5))
    return dict(bloomAmt=0.2, vign=0.15, **flash(t, 32.2, 0.2))


def s_shine(t, lt, d):
    """今 輝くとき: 拍ごとの寄り → 星とハートが噴き上がる"""
    gl = C.gl
    i = int(lt / 0.8)
    k = (lt % 0.8) / 0.8
    pose = sing(30, expr=(0, 1, 0, 0), wind=14)
    if i == 0:      # リボン
        fx.burst(gl, 0.4, 0.4, rot=t * 0.5, n=22, c1=H('ff8fb8'), c2=H('ffc2d8'), c3=(1, 1, 1))
        face('shine', t, 35.4 - 0.5, pose, 520 + 40 * k, 360, 1.3 + 0.08 * k, 960, 540, rot=0.06, look=dict(outlineW=14.0))
    elif i == 1:    # イヤリング
        dance_bg(t, 'teal', seed=6)
        face('shine', t, 35.4 - 0.5, pose, 1460, 1000, 2.0 + 0.12 * k, 1060, 540, rot=-0.1, look=dict(outlineW=16.0))
    elif i == 2:    # 瞳
        dance_bg(t, 'lemon', seed=8, dot_col=H('ffffff'))
        face('shine', t, 35.4 - 0.5, pose, 1180 - 20 * k, 860, 2.5 + 0.2 * k, 960, 540, look=dict(outlineW=16.0))
        pop.parts(gl, [1060 + 40 * k], [700], [220 * (0.5 + 0.5 * math.sin(k * 3.14))], (1, 1, 1), kind=3)
    else:           # 噴き上がる星とハート
        pop.popbg(gl, H('fff04a'), H('ffd23f'), dot_col=(1, 1, 1), dot_a=0.8, gap=40, dot_r=4.0, dot_off=(0, -lt * 40))
        lt3 = lt - 2.4
        rng = np.random.default_rng(31)
        n = 70
        sp = rng.uniform(500, 1100, n); ph = rng.uniform(0, 0.4, n)
        tt = np.maximum(lt3 - ph, 0)
        xs = 960 + rng.normal(0, 120, n) + rng.uniform(-1, 1, n) * tt * 500
        ys = 820 - sp * tt + 300 * tt * tt
        kind = np.where(np.arange(n) % 3 == 0, 3, 4)
        cols = np.where((kind == 4)[:, None], np.array(H('ff5d8a')), pop.CONF_PAL[np.arange(n) % 7])
        for kk in (3, 4):
            m = (kind == kk) & (tt > 0)
            pop.parts(gl, xs[m], ys[m], rng.uniform(40, 90, n)[m], cols[m], kind=kk, outline=0.08, angle=rng.uniform(-0.4, 0.4, n)[m])
        pose2 = lambda tt_: O.base_pose(tt_, wind=12, seed=31, expr=(0, 0, 0, 0, 1, 0), sing=True)
        face('shine2', t, 37.8 - 0.5, pose2, 1050, 1000, lerp(0.62, 0.66, k), 960, 640, look=dict(outlineW=12.0))
    return dict(bloomAmt=0.2, vign=0.05, **flash(t, 35.4 + i * 0.8, 0.12))


# ---------------------------------------------------------------- サビ1
def s_chorus(t, lt, d):
    """Creative Vision 10！: 渦の上でタイトルの文字が歌のモーラに合わせて弾む"""
    return chorus_title(t, lt, d, 38.6)


def chorus_title(t, lt, d, t0, cols=('ffb3d1', 'ffd6e8', 'f59ac2'), fills=None, gold=False):
    """タイトルの文字の見出し。t0 はショットの頭（その時刻の「Creative Vision 10！」の行に合わせる）"""
    gl = C.gl
    pop.swirl(gl, -lt * 0.7, c=(0.5, 0.55), cols=cols, line_a=0.7, arms=3.0, tight=1.3)
    pop.confetti(gl, t, n=60, seed=6, fall=90)
    ln = F.line_at(t0 + 0.2, 'Creative')
    ms = ln['moras']
    word1, word2, word3 = 'CREATIVE', 'VISION', '10!'
    tm = np.linspace(ms[0]['t'] - 0.05, ms[-1]['t'] - 0.2, len(word1) + len(word2) + len(word3))
    b = beat(t)
    fills = fills or [H('ff6f9c'), H('ffd23f'), H('6fd8ff'), H('b6f23c'), H('ff8a3d')]
    if gold:
        F.fireworks(t, [(t0 + 0.5, 330, 300, (1.0, 0.8, 0.35), 260), (t0 + 1.2, 1600, 260, (1.0, 0.45, 0.7), 240),
                        (t0 + 2.0, 1450, 820, (0.6, 0.85, 1.0), 220), (t0 + 2.6, 400, 800, (0.8, 1.0, 0.6), 230)])
    pop.pop_word(gl, t, word1, tm[:8], 900, 210, 190, fills, edge=INK, fam='impact', weight=None, kern=0.96, jump=12, beat=b, white=8,
                 angle=-0.06)
    pop.pop_word(gl, t, word2, tm[8:14], 820, 450, 210, fills[2:] + fills[:2], edge=INK, fam='impact', weight=None, kern=0.96, jump=12,
                 beat=b + 0.3, white=8, angle=-0.06)
    pop.pop_word(gl, t, word3, tm[14:], 1360, 780, 300, [H('ffd23f'), H('ffd23f'), H('ff4f7b')], edge=INK, fam='impact', weight=None,
                 kern=0.96, jump=18, beat=b + 0.6, white=10, angle=-0.06)
    k = prog(lt, d - 0.22, d)
    if k > 0:
        pop.wipe(gl, 960, 540, 1250 * ease_in(k, 2), H('201a6a'), inside=True)
    return dict(bloomAmt=0.45 if gold else 0.2, vign=0.05, **flash(t, t0, 0.25))


def s_cityfly(t, lt, d):
    """明日へ 今 飛び出せ: 見上げた街の上を右上へ飛ぶ"""
    gl = C.gl
    pop.popbg(gl, H('3a1a8a'), H('ff7aa8'), ang=1.57, dot_a=0.0)
    stars_bg(t, n=50, seed=8, area=(0, 0, 1920, 600))
    k = ease_io(lt / d, 2)
    pop.city(gl, t, vp=(960 + 200 * math.sin(lt), -1500), roll=lerp(-0.2, 0.12, k), scroll=lt * 260, seed=11)
    enter = ease_out(prog(lt, 0.0, 0.9), 3)
    x = lerp(150, 980, enter) + 30 * math.sin(lt * 1.7)
    y = lerp(1150, 520, enter) + 20 * math.sin(lt * 3.0)
    ang = lerp(-0.4, -0.15, enter)
    star_trail(lambda q: x - 200 - q * 900 * math.cos(ang), lambda q: y + 80 + q * 900 * math.sin(-ang) * 0.9, t, n=34, seed=5, size=48)
    fb = C.fb['flight']
    fb.draw(fb.H(x, y, lerp(900, 1200, enter), angle=ang, by='width'), t, P2.fb_at('flight', 1.0 + lt), breath=0.0,
            look=stand_look(outlineW=8.0, outlineCol=(1, 1, 1)))
    return dict(bloomAmt=0.3, vign=0.15, ca=2.0, **flash(t, 41.8, 0.3))


def s_vortex(t, lt, d):
    """広がる世界 飛び込んでく: 実画面が渦を巻いて吸い込まれ、主人公も中心へ"""
    gl = C.gl
    pop.swirl(gl, lt * 1.6, c=(0.5, 0.48), cols=('7f97e0', 'a9bcf2', '5f78d0'), hole=lerp(0.05, 0.12, lt / d), hole_col='2a1458')
    n = 26
    items = []
    for j in range(n):
        z = (j / n - lt * 0.32) % 1.0          # 1 外 → 0 中心
        items.append((z, j))
    for z, j in sorted(items, key=lambda q: -q[0]):
        a = j * 2.39996 + (1 - z) * 4.0 + lt * 0.8
        r = 40 + 1200 * z ** 1.6
        x = 960 + math.cos(a) * r * 1.15; y = 518 + math.sin(a) * r * 0.72
        tex = C.screens_all[(j * 5) % len(C.screens_all)]
        s = 20 + 440 * z ** 1.3
        al = smooth(prog(z, 0.02, 0.15)) * (1 - smooth(prog(z, 0.92, 1.0)))
        fx.rect(gl, x, y, s + 12, s * tex.size[1] / tex.size[0] + 12, (1, 1, 1), alpha=al, angle=a * 0.3)
        gl.sprite(tex, x, y, scale=s / tex.size[0], alpha=al, angle=a * 0.3)
    k = ease_in(prog(lt, 0.0, d), 2.0)
    fb = C.fb['dive']
    if k < 0.97:
        fb.draw(fb.H(960 + 120 * math.sin(lt * 2) * (1 - k), lerp(1500, 560, k), lerp(1300, 60, k), angle=lerp(0.0, -0.6, k)),
                t, P2.fb_at('dive', lt), breath=0.0, look=stand_look(outlineW=6.0 * (1 - k), outlineCol=(1, 1, 1)))
    return dict(bloomAmt=0.35, vign=0.15, flash=smooth(prog(lt, d - 0.2, d)) * 0.9, flashCol=(1, 1, 1))


def s_ikou(t, lt, d):
    """いこう！: 黄色の放射とハイテンションの顔、文字が1つずつ弾む"""
    return ikou(t, lt, d, 48.2)


def ikou(t, lt, d, t0, c=('ffd84d', 'fff06a', 'ff7a00'), side=1):
    """いこう！の本体。t0 はショットの頭、c は放射の2色と集中線の色、side=-1 で左右を入れ替える"""
    gl = C.gl
    fx.burst(gl, 0.5, 0.55, rot=-t * 0.8 * side, n=22, c1=H(c[0]), c2=H(c[1]), c3=(1, 1, 0.92))
    z = lerp(1.25, 0.92, ease_out(prog(lt, 0, 0.3), 4))
    face(f'ikou3_{t0}', t, t0 - 0.5, O.ikou_pose, 1040, 980, z, 960 - 140 * side, 620, look=dict(outlineW=14.0))
    fx.focus_lines(gl, inner=0.42, seed=int(t * 15), amount=0.8, col=H(c[2]))
    pop.pop_word(gl, t, 'いこう！', [t0 + 0.05, t0 + 0.15, t0 + 0.25, t0 + 0.35], 960 + 440 * side, 330, 190,
                 [H('ff4f7b'), H('ff8a3d'), H('ff4f7b'), H('ffd23f')], edge=INK, angle=-0.14 * side, jump=16, beat=beat(t), white=10)
    inv = 1.0 if 0.0 <= lt < 0.067 else 0.0
    return dict(bloomAmt=0.2, vign=0.05, invert=inv, mono=inv)


def s_door(t, lt, d, ft=None):
    """まだ見ぬ明日の扉: 青空の雲の上、光の扉へ手を伸ばすと扉が開く"""
    gl = C.gl
    push = ease_io(prog(lt, 0.0, d), 2)
    O.sky_layer(t, 'day', cam=(0, 40 * push), cloud_y=860, scroll=-30, n=8, scale=0.66, haze=0.3, seed=27)
    open_k = ease_io(prog(lt, 3.3, 4.5))
    sc = lerp(0.7, 1.35, push)
    dx, dy = 960, lerp(470, 430, push)
    dw, dh = 360 * sc, 560 * sc
    # 扉の向こうの光
    fx.parts(gl, [dx], [dy], [dh * (2.2 + 2.0 * open_k)], (1.0, 0.95, 0.75), [0.45 + 0.4 * open_k], kind=0)
    fx.rect(gl, dx + 14, dy + 16, dw + 36, dh + 36, INK, alpha=0.35)
    fx.rect(gl, dx, dy, dw + 36, dh + 36, H('ffd23f'))
    fx.rect(gl, dx, dy, dw + 20, dh + 20, INK)
    fx.rect(gl, dx, dy, dw, dh, (1.4, 1.35, 1.2))
    # 両開きの扉（蝶番へ向けて細くなる）
    for side in (-1, 1):
        pw = dw / 2 * (1 - open_k)
        if pw < 1:
            continue
        px = dx + side * (dw / 2 - pw / 2)
        fx.rect(gl, px, dy, pw, dh, H('7b3fd0'))
        fx.rect(gl, px, dy, pw * 0.7, dh * 0.86, H('9b62f0'))
        pop.parts(gl, [px], [dy - dh * 0.1], [max(pw * 0.5, 1)], H('ffd23f'), kind=3, ratio=1.0)
    if open_k > 0:
        fx.god_rays(gl, dx / 1920, dy / 1080, amount=0.9 * open_k, decay=0.975)
    k = smooth(prog(lt, 2.2, 3.0))
    if k > 0:
        fb = C.fb['gate']
        fb.draw(fb.H(960, lerp(1500, 1180, k), 900), t, P2.fb_at('gate', lt - 2.4 if ft is None else ft),
                look=stand_look(outlineW=7.0, outlineCol=(1, 1, 1), rimCol=(1.0 + 1.5 * open_k, 0.9 + 1.4 * open_k, 0.6), rimDir=(0.0, -1.0),
                                rimW=4.0 + 3 * open_k, opacity=k))
    # 雲の間を舞う星
    rng = np.random.default_rng(14)
    n = 30
    xs = rng.uniform(0, 1920, n); ys = (rng.uniform(0, 1080, n) - lt * 60) % 1080
    pop.parts(gl, xs, ys, rng.uniform(14, 30, n), pop.CONF_PAL[np.arange(n) % 7], kind=3, alpha=0.9, angle=xs * 0.01 + t)
    return dict(bloomAmt=0.45, vign=0.1, **flash(t, t - lt, 0.25))


def s_hearts(t, lt, d):
    """開けよう 今すぐに: 黄色の水玉、羽ばたくハートの中で歌う"""
    gl = C.gl
    pop.popbg(gl, H('fff04a'), H('ffe14a'), dot_col=(1, 1, 1), dot_a=0.9, gap=42, dot_r=4.2, dot_off=(lt * 25, lt * 10))
    rng = np.random.default_rng(55)
    n = 60
    sp = rng.uniform(140, 320, n); ph = rng.uniform(0, 10, n)
    xs = rng.uniform(-40, 1960, n) + 40 * np.sin(t * 2 + ph)
    ys = (rng.uniform(0, 1300, n) - lt * sp) % 1300 - 110
    sz = rng.uniform(40, 110, n)
    flap = np.abs(np.sin(t * 9 + ph))
    pop.parts(gl, xs - sz * 0.55, ys - sz * 0.1, sz * 0.55 * (0.6 + 0.4 * flap), (1, 1, 1), kind=2, outline=0.1, ratio=1.0)
    pop.parts(gl, xs + sz * 0.55, ys - sz * 0.1, sz * 0.55 * (0.6 + 0.4 * flap), (1, 1, 1), kind=2, outline=0.1, ratio=1.0)
    pop.parts(gl, xs, ys, sz, np.where((np.arange(n) % 4 == 0)[:, None], np.array(H('ff8fb8')), np.array(H('ff4f7b'))), kind=4,
              outline=0.08, angle=0.2 * np.sin(t * 3 + ph))
    b = bounce(t)
    face('hearts', t, 54.6 - 0.5, sing(33, expr=(0, 1, 0, 0), wind=12), 1050, 980, 0.78, 960, 620 - 14 * b, look=dict(outlineW=12.0))
    return dict(bloomAmt=0.15, vign=0.05, **flash(t, 54.6, 0.35))


def s_lineup(t, lt, d):
    """目指すはひとつ: 5人の色違いが並んで拍で弾む"""
    gl = C.gl
    dance_bg(t, 'pink', seed=9, dots=0.6)
    giant_type(t, 'CREATIVE VISION 10  ', 300, alpha=0.4, speed=120, fam='impact')
    giant_type(t + 5, 'TOMORROW  TOMORROW  ', 860, alpha=0.35, speed=-100, fam='impact')
    pop.confetti(gl, t, n=50, seed=8, fall=70)
    b = beat(t)
    hop = 30 * abs(math.sin(math.pi * b))
    sq = 1 + 0.025 * math.cos(2 * math.pi * b)
    tex = C.lineup
    w, h = tex.size
    k = ease_out_back(prog(lt, 0.0, 0.35), 2.0)
    sc = 1780 / w * k
    Hm = gl.rect_h(960, 1100 - hop, w * sc, h * sc / sq, angle=0.03 * math.sin(math.pi * b), anchor=(0.5, 1.0))
    # 白い縁取りは少し大きく白で塗った影で付ける
    gl.quad(tex, gl.rect_h(960 + 14, 1100 - hop + 16, w * sc, h * sc / sq, angle=0.03 * math.sin(math.pi * b), anchor=(0.5, 1.0)),
            alpha=0.35, tint=(0.3, 0.1, 0.3))
    gl.quad(tex, Hm)
    return dict(bloomAmt=0.2, vign=0.05, **flash(t, 57.8, 0.2))


def s_logo(t, lt, d):
    """ピンクの水玉、立ち絵とネオンのロゴ"""
    gl = C.gl
    pop.popbg(gl, H('f27aa0'), H('f59ab6'), dot_col=(1, 1, 1), dot_a=0.75, gap=42, dot_r=4.0, dot_off=(lt * 20, 0))
    b = beat(t)
    st = C.work
    k = ease_out_back(prog(lt, 0.0, 0.4), 1.6)
    st.draw(st.H(520, 1140 + 80 * (1 - k), 1060), t, wind=6 * math.sin(t), hem=3.0, sway=0.03 * math.sin(math.pi * b),
            look=stand_look(outlineW=8.0, outlineCol=(1, 1, 1)))
    rng = np.random.default_rng(3)
    n = 30
    tw = np.abs(np.sin(t * 3 + np.arange(n)))
    pop.parts(gl, rng.uniform(900, 1900, n), rng.uniform(80, 1000, n), 30 * tw + 6, (1, 1, 1), kind=3, alpha=0.9)
    # ネオンのロゴ（点灯してちらつく）
    on = prog(lt, 0.4, 0.5)
    fl = 1.0 if lt > 1.0 else (0.3 + 0.7 * (math.sin(lt * 70) > -0.2))
    gl.sprite(C.logo, 1300, 250, scale=0.36 * ease_out_back(prog(lt, 0.2, 0.5), 2), tint=(0.5, 0.4, 0.6), add=(0.9, 0.55, 1.0))
    # ネオンの看板（暗い板の上で光らせる）
    kb = ease_out_back(prog(lt, 0.1, 0.4), 1.8)
    pop.parts(gl, [1314], [538], [1460 * kb], INK, kind=5, ratio=0.19, alpha=0.4)
    pop.parts(gl, [1300], [520], [1460 * kb], H('2a1458'), kind=5, ratio=0.19, outline=0.018)
    pop.neon(gl, 'Creative Vision 10', 1300, 515, 104, col=(1.0, 0.3, 0.75), core=(1.0, 0.85, 0.95), alpha=on, flicker=fl)
    pop.chunk(gl, '～ 明日へ飛べ ～', 1300, 690, 70, fill=(1, 1, 1), edge=INK, alpha=prog(lt, 0.8, 1.1), white=5)
    pop.chunk(gl, '2027年1月 リリース予定', 1300, 830, 64, fill=H('ffd23f'), edge=INK, alpha=prog(lt, 1.3, 1.6), white=5,
              scale=ease_out_back(prog(lt, 1.3, 1.6), 2))
    return dict(bloomAmt=0.7, vign=0.05, **flash(t, 61.0, 0.2))


def s_ending(t, lt, d):
    """エンディング: ドラムソロのカード → 一撃でクレジットと QR"""
    gl = C.gl
    hit_t = T_CUT + 1.6
    if t < hit_t:
        i = int(lt / 0.2)
        wedge_bg(i + 3, 0)
        if i % 2 == 0:
            j = (i * 11) % len(C.screens_all)
            pop.card(gl, 960, 540, 900, 660, 0.08 * (1 if i % 4 else -1), tex=C.screens_all[j], label=C.screen_names[j], label_size=30)
        else:
            ink, label = C.inks[(i // 2) % len(C.inks)]
            pop.card(gl, 960, 540, 620, 820, -0.07 * (1 if i % 4 == 1 else -1), ink=ink, label=label)
        return dict(bloomAmt=0.1, vign=0.0, flash=pulse(lt % 0.2, 0.0, 0.01, 0.05) * 0.4)
    lt2 = t - hit_t
    dance_bg(t, 'purple', seed=12, dots=0.5)
    pop.confetti(gl, t, n=70, seed=12, fall=60)
    pop_k = ease_out_back(prog(lt2, 0.0, 0.45), 1.6)
    st = C.work
    st.draw(st.H(1480, 1130 + 60 * (1 - pop_k), 1060), t, wind=6 * math.sin(t), hem=3.0, look=stand_look(outlineW=8.0, outlineCol=(1, 1, 1)))
    sc = 0.42 * (0.9 + 0.1 * ease_out_back(prog(lt2, 0, 0.4)))
    gl.sprite(C.logo, 640, 330, scale=sc, tint=(0.35, 0.3, 0.45), add=(0.6, 0.45, 0.85))
    pop.chunk(gl, 'Creative Vision 10', 640, 560, 100, fill=(1, 1, 1), edge=INK, fam='comic', weight=None, white=6, scale=pop_k)
    pop.chunk(gl, '2027年1月 リリース予定', 640, 690, 50, fill=H('ffd23f'), edge=INK, white=5)
    P2.credit_footer(590, 845, 1150, 870, 170, alpha=prog(lt2, 0.0, 0.25))
    tex = gl.text(O.VOICES, 22, fam='noto', weight='Medium', color=(1, 1, 1), stroke=3, stroke_color=INK)
    gl.sprite(tex, 960, 1045, alpha=0.95)
    return dict(bloomAmt=0.3, vign=0.1, flash=pulse(t, hit_t, 0.01, 0.4), fade=prog(t, DURATION - 0.9, DURATION - 0.05))


SHOTS = [
    (0.0, 3.4, s_open),
    (3.4, 6.6, s_city),
    (6.6, 9.8, s_swirl_in),
    (9.8, 13.0, s_title),
    (13.0, 16.2, s_wind),
    (16.2, 19.4, s_open2),
    (19.4, 22.6, s_stage),
    (22.6, 25.8, s_sunset),
    (25.8, 29.0, s_screens),
    (29.0, 32.2, s_tests),
    (32.2, 35.4, s_days),
    (35.4, 38.6, s_shine),
    (38.6, 41.8, s_chorus),
    (41.8, 45.0, s_cityfly),
    (45.0, 48.2, s_vortex),
    (48.2, 49.8, s_ikou),
    (49.8, 54.6, s_door),
    (54.6, 57.8, s_hearts),
    (57.8, 61.0, s_lineup),
    (61.0, T_CUT, s_logo),
    (T_CUT, 1e9, s_ending),
]

NO_KARAOKE = [(38.6, 41.8), (48.2, 49.8)]


CREDITS = P2.CREDITS[:4] + [
    (32.6, 35.0, ['キャラクター原画', '表情差分・口・立ち絵・5人の色違い（静止画の生成画像）', '人物動作: Wan 2.2（動画生成AI）　ミニキャラ: Codex のペット'], 'lt'),
]


def credits(t):
    gl = C.gl
    for (a, b, lines, pos) in CREDITS:
        if a <= t <= b:
            al = min(prog(t, a, a + 0.4), 1 - prog(t, b - 0.4, b))
            for i, s in enumerate(lines):
                tex = gl.text(s, 22 if i == 0 else 32 if i == 1 else 26, fam='noto', weight='Bold', color=(1, 1, 1), stroke=3, stroke_color=(0.1, 0.05, 0.2))
                gl.sprite(tex, 70, 70 + i * 40, anchor=(0, 0), alpha=al * (0.85 if i == 0 else 1.0))


def frame(gl, t):
    post = {}
    for k in range(O.MB):
        ts = t + ((k + 0.5) / O.MB - 0.5) * O.SHUTTER / FPS if O.MB > 1 else t
        gl.time = ts
        gl.use(gl.main, clear=(0, 0, 0, 1))
        p = {}
        for a, b, fn in SHOTS:
            if a <= ts < b:
                p = fn(ts, ts - a, b - a) or {}
                break
        if k == O.MB // 2:
            post = p
        gl.use(gl.main)
        if 13.0 <= t < T_CUT and not any(a <= t < b for a, b in NO_KARAOKE):
            O.karaoke(t)
        credits(t)
        if O.MB > 1:
            gl.accumulate(1.0 / O.MB, k == 0)
    pp = dict(grainAmt=0.012, vign=0.1)
    pp.update(post)
    gl.time = t
    if O.MB > 1:
        pp['src'] = gl.acc
    return gl.finish(pp)


def audio():
    return P2.audio()
