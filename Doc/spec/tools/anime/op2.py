# 作品1 v2「明日へ飛べ」ショートバージョン（主題歌 v2 の1番＋エンディングの一撃、約68秒）
# 歌詞 v2 は1番が「技術と歩み（現在から未来へ）」。v1 の2番の歌詞（opfull.py の Aメロ2・Bメロ2 のショット）が1番に来るため、
# そのショットを時刻をずらして使い、主人公は背景なしの素材で合成する。顔の寄りは onlyperson の6表情のリグ（rig_p.Character）、
# 全身の動きはシーン別の連続コマ（Doc/promo/assets4anime/op2-scene-v3: 0.1 秒刻み、rig_p.Flipbook）で動かす。
# コマは素材の秒数をショットの長さに合わせず、動きが自然に見える速さ（FB_RATE）で送る。
# 動画生成AIは使わない。
#
# 秒割り（音源の時刻。0.2秒の無音の後、150BPM・1小節1.6秒）
#    0.0- 3.4  夜明け前の空                                       イントロ（op と同じ）
#    3.4- 6.6  魔法陣が描かれる
#    6.6- 8.2  朝焼けの雲の上に立つ後ろ姿（逆光、風の連続コマ）
#    8.2- 9.8  光が当たり、目を開く
#    9.8-11.4  拍ごとのカット（リボン・イヤリング・瞳・手）
#   11.4-13.0  タイトル
#   13.0-16.2  通信の光と並んで空を飛ぶ（飛ぶ連続コマ）           Aメロ1「風より速く 届くよ」
#   16.2-19.4  光の扉が開き四季の里、オープンな技術が広がる（風の連続コマ）「オープンな夢 広がる」
#   19.4-22.6  AI の光とミニキャラ、AI と共同の実コミット数        「AIと手を つないで」
#   22.6-25.8  四季の里の朝日                                     「新しい朝 つくろう」
#   25.8-29.0  実画面の回廊を歩く（歩く連続コマ）、実画面数             Bメロ1「いくつもの画面」
#   29.0-32.2  実テストが次々に通る（困り顔→笑顔）               「何度も確かめ」
#   32.2-35.4  毎日のコミットの草の上をミニキャラが走る            「毎日 前へ進む」
#   35.4-38.6  光の柱→拍ごとのカット                             「今 輝くとき」
#   38.6-41.8  タイトルの文字が拍で落ちる                         サビ1「Creative Vision 10！」
#   41.8-45.0  雲の中を右上へ飛ぶ（飛ぶ連続コマ）                 「明日へ 今 飛び出せ」
#   45.0-46.6  実画面が翼になる                                   「広がる世界
#   46.6-48.2  画面の世界へ飛び込んでいく（飛び込む連続コマ）                  飛び込んでく」
#   48.2-49.8  いこう！（ハイテンション）
#   49.8-54.6  3階層の扉を昇り、扉へ手を伸ばす（連続コマ）         「まだ見ぬ明日の扉 / 開けよう 今すぐに」
#   54.6-61.0  雲海の日の出で歌う（最後は目を閉じた笑顔）           「目指すはひとつ あの未来へ」
#   61.0-64.2  ロゴ
#   64.2-67.6  エンディング（ドラムソロ→全員の一撃→仕事着の立ち絵とロゴ）  音源 160.2 秒以降をつなぐ
#   python film.py op2                      四国めたん版 → out/op2_metan.mp4
#   python film.py op2 --variant zundamon   歌い手の版（opfull.py の SINGERS）
import os, math
import numpy as np
import songsync as SS
SS.use('v2')                    # 歌詞 v2 の譜割り・歌だけの音源（op / opfull の読み込みより前に切り替える）
import engine as E, rig as R, rig_p as RP, fx, clouds, prep_person
import op as O
import opfull as F
from engine import prog, lerp, smooth, ease_out, ease_in, ease_io, ease_out_back, pulse, clamp

FPS = 30
SUPERSAMPLE = 2
SINGER = F.SINGER
OUT_NAME = f'op2_{SINGER}'
T_CUT = SS.bar(40)              # 64.2: サビ1の終わり
SRC_END = SS.bar(100)           # 160.2: エンディングの頭
SONG = os.path.join(SS.SONG_DIR, f'{SS.prefix()}_full_{SINGER}.m4a')
DURATION = T_CUT + (163.55 - SRC_END) + 0.25
SHIFT_V1 = SS.bar(36)           # 57.6: opfull の2番（v1 の歌詞の位置）→ v2 の1番

CUR_SHIFT = 0.0                 # 描いているショットの時刻のずらし（口パクを v2 の歌に合わせるため）


def remap(t):
    """映像時刻 → 音源の時刻"""
    return t if t < T_CUT else t - T_CUT + SRC_END


# op.py をショートの切り方に戻す（opfull の読み込みで恒等になっている）
O.remap = remap
O.T_CUT = T_CUT
O.SRC_END = SRC_END
O.DURATION = DURATION
O.SONG = SONG
C = None

CREDITS = [
    (13.4, 15.9, ['主題歌', '「Creative Vision 10！ ～明日へ飛べ～」（v2）'], 'lt'),
    (16.6, 19.0, ['歌', F.VX(F.MAIN)], 'lt'),
    (19.8, 22.2, ['コーラス', '　'.join(F.VX(n) for n in F.CHORUS)], 'lt'),
    (23.0, 25.4, ['作画・撮影・編集', 'Python + GLSL（すべてコードで描画・動画生成AI不使用）'], 'lt'),
    (32.6, 35.0, ['キャラクター・背景原画', '静止画の生成画像（表情差分・立ち絵・四季の里）、ミニキャラは Codex のペット'], 'lt'),
]


# ---------------------------------------------------------------- 準備
def setup(gl):
    global C
    F.setup(gl)
    C = O.C
    prep_person.build()
    C.ch0 = C.ch                          # 旧リグ（背景つきの一枚絵。部屋の背景の板に使う）
    C.ch = RP.Character(gl)               # 背景なしの主人公（6表情）
    C.ch.plate = C.ch0.plate
    C.work = RP.Standee(gl, 'fullbody-smile')
    # シーン別の連続コマ（Doc/promo/assets4anime/op2-scene-v3: 0.1 秒刻み）
    C.fb = {k: RP.Flipbook(gl, k, prep_person.SCENE_VER) for k in prep_person.SCENES}
    C.vocal = SS.Vocal(SINGER, remap=lambda tt: remap(tt - CUR_SHIFT))
    C.lyrics = SS.lines({s['name'] for s in SS.timeline()})
    C.screens_all = [gl.image(p, maxw=900) for p in sorted(__import__('glob').glob(os.path.join(E.ASSETS, 'screens', '*.jpg')))]
    # 「何度も確かめ」: テストが通るまでは困り顔、通り切ると笑顔（opfull の f_tests の主人公の動きを差し替える）
    t0 = 86.6
    def tests_pose(tt):
        p = O.base_pose(tt, wind=0, seed=25, sing=True, typing=1.2)
        e = smooth(prog(tt, t0 + 2.2, t0 + 2.7))
        p.expr_full = p.expr_face = (0, e, 0, 0, 0, 1 - e)
        return p
    C.sims['tests'] = R.Sim(tests_pose, FPS, 4, t0 - 0.5)


# ---------------------------------------------------------------- 道具
PRODUCER = 'Sekiya Sato'
REPO_URL = 'https://github.com/sekiya-sato/creativevision10/'


def qr_tex():
    """リポジトリ URL の QR コード（OpenCV の符号化。読み取れる白い余白つき）"""
    import cv2
    q = cv2.QRCodeEncoder.create().encode(REPO_URL)
    q = cv2.resize(q, None, fx=12, fy=12, interpolation=cv2.INTER_NEAREST)
    q = cv2.copyMakeBorder(q, 48, 48, 48, 48, cv2.BORDER_CONSTANT, value=255)
    return C.gl.texture(np.dstack([q, q, q]), key=('qr', REPO_URL), mipmap=False)


def credit_footer(x, y, qx, qy, qsize=170, alpha=1.0):
    """クレジット画面の下に、プロデューサー・リポジトリと QR コードを出す。(x, y) は文字の中心、(qx, qy) は QR の中心"""
    gl = C.gl
    if alpha <= 0:
        return
    sc = dict(fam='noto', stroke=3, stroke_color=E.hexc('2a1240'))
    rows = []
    for i, (a, b) in enumerate((('プロデューサー', PRODUCER), ('リポジトリ', REPO_URL))):
        la = gl.text(a, 22, weight='Bold', color=E.hexc('f3dcf0'), **sc)
        va = gl.text(b, 28 if i == 0 else 24, weight='Black', color=(1, 1, 1), **sc)
        rows.append((la, va, gl.vsize(la)[0], gl.vsize(va)[0]))
    # 明るい雲や光の上でも読めるよう、文字の幅に合わせた暗い帯を敷く
    wmax = max(wl + 18 + wv for _, _, wl, wv in rows)
    fx.rect(gl, x, y + 22, wmax + 70, 110, E.hexc('140828'), alpha=0.6 * alpha)
    for i, (la, va, wl, wv) in enumerate(rows):
        x0 = x - (wl + 18 + wv) / 2
        gl.sprite(la, x0, y + i * 44, anchor=(0, 0.5), alpha=alpha * 0.9)
        gl.sprite(va, x0 + wl + 18, y + i * 44, anchor=(0, 0.5), alpha=alpha)
    tex = qr_tex()
    fx.rect(gl, qx, qy, qsize + 12, qsize + 12, (0.1, 0.05, 0.2), alpha=0.5 * alpha)
    gl.sprite(tex, qx, qy, scale=qsize / tex.size[0], alpha=alpha)


def stand_look(**kw):
    d = dict(outlineW=0.0)
    d.update(kw)
    return d


def packets(t, lt, front):
    """gRPC の呼び出し名をのせた通信の光（右から左へ追い越していく）"""
    gl = C.gl
    rng = np.random.default_rng(70)
    for j in range(12):
        sp = rng.uniform(1300, 2300); ph = rng.uniform(0, 3); y = rng.uniform(260, 960)    # 左上のクレジットに重ねない
        x = 2200 - ((lt + ph) * sp) % 2900
        if (j % 3 == 0) != front:
            continue
        n = 30
        q = np.arange(n) / n
        fx.parts(gl, x + q * 520, np.full(n, y), 40 * (1 - q) + 6, (0.45, 0.8, 1.2), (1 - q) * 0.9, kind=0)
        fx.parts(gl, [x], [y], [130], (0.9, 0.97, 1.0), [1.0], kind=1)
        if front:
            continue          # 手前の光は顔に掛かるので名前を付けない
        tex = gl.text(C.grpc[j % len(C.grpc)], 30, fam='mono', weight=None, color=(1, 1, 1), stroke=3, stroke_color=E.hexc('1b4a8e'))
        gl.sprite(tex, x + 40, y - 36, anchor=(0, 0.5), alpha=0.95)


def speed_trail(x, y, n=40, col=(1, 0.85, 0.95), length=600, t=0.0):
    rng = np.random.default_rng(int(t * 30))
    xs = x - rng.uniform(80, length, n); ys = y + rng.normal(0, 120, n)
    fx.parts(C.gl, xs, ys, rng.uniform(4, 14, n), col, rng.uniform(0.2, 0.8, n), kind=1)


# 連続コマの再生の速さ（素材の秒数に対する倍率）と、尺が余ったときに折り返すか。
# 再描画したコマはコマ間で細部が揺れるため、風・飛ぶ・飛び込むは少し遅く送って落ち着かせる（10fps → 8fps 前後）。
# 歩く・手を伸ばすは前へ進む動きなので折り返さない（歩くは回廊の進みに合わせた速さ）
FB_PLAY = dict(wind=(0.8, True), flight=(0.8, True), walk=(0.94, False), dive=(0.85, True), gate=(1.0, False))


def fb_at(name, ts):
    """連続コマ name の、ショット内の経過秒 ts のコマ番号"""
    rate, pp = FB_PLAY[name]
    return C.fb[name].at(ts, rate, pp)


# ---------------------------------------------------------------- ショット
def s_reveal2(t, lt, d):
    """朝焼けの雲の上に立つ後ろ姿（逆光、風の連続コマ）→ 光が当たって目を開く（op の s_reveal）"""
    if lt >= 1.6:
        return O.s_reveal(t, lt, d)
    gl = C.gl
    k = ease_io(lt / 1.6)
    O.sky_layer(t, 'sunrise', cam=(0, 40 - 30 * k), cloud_y=1000, scroll=16, n=7, scale=0.65, haze=0.4, seed=1)
    fx.god_rays(gl, 0.5, 0.55, amount=0.9, decay=0.975)
    fx.parts(gl, [960], [590], [900], (1.0, 0.75, 0.55), [0.5], kind=0)
    fb = C.fb['wind']
    Hm = fb.H(960, 1180 + 40 * k, lerp(1180, 1260, k))
    look = stand_look(silhouette=0.92 - 0.25 * k, silCol=E.hexc('1c0f2e'), rimCol=(2.2, 1.5, 0.9), rimDir=(0.0, 1.0), rimW=5.0)
    fb.draw(Hm, t, fb_at('wind', lt), look=look)
    O.fog_bottom(E.hexc('c86f86'), 0.9, 860, 1080)
    rng = np.random.default_rng(3)
    n = 50
    fx.parts(gl, (rng.uniform(0, 1920, n) + t * 60) % 1920, rng.uniform(0, 1080, n), rng.uniform(4, 12, n),
             (1, 0.9, 0.8), rng.uniform(0.2, 0.7, n), kind=0)
    return dict(bloomAmt=0.8, vign=0.35, **O.flash(t, 6.6, 0.35), fade=0.0)


def s_wind2(t, lt, d):
    """風より速く: gRPC の光と並んで空を飛ぶ（立ち絵を前傾させて飛ばす）"""
    gl = C.gl
    O.sky_layer(t, 'day', cam=(0, 0), cloud_y=980, scroll=-260, n=8, scale=0.62, haze=0.35, seed=21)
    fx.focus_lines(gl, inner=0.5, seed=int(t * 15), amount=0.45, col=(1, 1, 1), mode=1)
    packets(t, lt, False)
    enter = ease_out(prog(lt, 0.0, 0.8), 3)
    x = lerp(-500, 900, enter) + 30 * math.sin(lt * 1.7)
    y = 560 + 22 * math.sin(lt * 3.1)
    ang = -0.05 + 0.03 * math.sin(lt * 2.3)
    speed_trail(x - 200, y + 40, t=t, col=(0.85, 0.95, 1.0), length=900)
    fb = C.fb['flight']
    fb.draw(fb.H(x, y, 1250, angle=ang, by='width'), t, fb_at('flight', lt), breath=0.0,
            look=stand_look(rimCol=(1.0, 1.1, 1.3), rimDir=(-1.0, 0.0), rimW=4.0))
    packets(t, lt, True)
    tex = gl.text('gRPC · HTTP/2', 30, fam='segoeb', weight=None, color=(1, 1, 1), stroke=3, stroke_color=E.hexc('1b4a8e'))
    gl.sprite(tex, 1840, 120, anchor=(1, 0.5), alpha=prog(lt, 0.4, 0.9) * 0.9)
    return dict(bloomAmt=0.45, vign=0.15, ca=2.5, **O.flash(t, 13.0, 0.25, (0.8, 0.9, 1.0)))


def s_open2(t, lt, d):
    """光の扉が開き四季の里へ。扉の前に立つ後ろ姿"""
    p = F.f_open(t, lt, d)
    k = ease_out(prog(lt, 0.15, d), 2)
    fb = C.fb['wind']
    fb.draw(fb.H(960, lerp(1500, 1420, k), lerp(1300, 1180, k)), t, fb_at('wind', lt),
            look=stand_look(rimCol=(1.6, 1.4, 1.1), rimDir=(0.0, 1.0), rimW=4.0, tintMul=(0.92, 0.9, 0.98)))
    return p


def s_screens(t, lt, d):
    """いくつもの画面: 実画面の回廊を進む。画面の数は実数（View の数）"""
    gl = C.gl
    fx.rect(gl, 960, 540, 1920, 1080, E.hexc('0a0c24'))
    fx.grad(gl, 960, 540, 1920, 1080, (0.1, 0.12, 0.35, 0.0), (0.25, 0.3, 0.7, 0.6), blend='add')
    speed = 1.6                 # 歩く速さに合わせて回廊を進む
    ez = lt * speed
    cam = E.Cam3(eye=(0.25 * math.sin(lt * 0.7), 0.35, ez - 6.0), target=(0, 0.25, ez + 4.0), fov=58, roll=0.03 * math.sin(lt))
    quads = []
    sp = 2.3
    for i in range(int(ez / sp) - 1, int(ez / sp) + 12):
        for side in (-1, 1):
            for row, y in enumerate((-1.2, 0.35, 1.9)):
                j = (i * 7 + row * 3 + (0 if side < 0 else 50)) % len(C.screens_all)
                z = i * sp + (row % 2) * sp * 0.5
                c = np.array([side * 3.2, y, z])
                right = np.array([0, 0, 1.0 if side < 0 else -1.0])
                right = np.array([0.25 * -side, 0, right[2]]); right /= np.linalg.norm(right)
                quads.append((cam.to_cam(c)[2], c, right, j, i))
    for zc, c, right, j, i in sorted(quads, key=lambda q: -q[0]):
        if zc < 0.4 or zc > 28:
            continue
        tex = C.screens_all[j]
        w, h = tex.size
        H_, z = cam.quad_h(c, right, np.array([0, -1.0, 0]), 2.0, 2.0 * h / w)
        if H_ is None:
            continue
        fade = clamp((28 - zc) / 10)
        fx.window(gl, tex, H_, appear=1.0, alpha=0.9 * fade, seed=j, edge=(0.6, 0.75, 1.0), bright=1.0)
    # 奥の光
    p, zz = cam.project(np.array([[0, 0.3, ez + 30]]))
    fx.parts(gl, [p[0, 0]], [p[0, 1]], [900], (0.6, 0.7, 1.0), [0.35], kind=0)
    # 後ろ姿（画面の光を受けて進む）
    fb = C.fb['walk']
    fb.draw(fb.H(960 + 20 * math.sin(lt * 0.9), 1230, 960), t, fb_at('walk', lt), breath=0.0,
            look=stand_look(tintMul=(0.72, 0.76, 1.0), tintAdd=(0.0, 0.02, 0.06), rimCol=(0.6, 0.8, 1.5), rimDir=(1.0, 0.0), rimW=4.0))
    O.fog_bottom((0.05, 0.06, 0.2), 0.8, 940, 1080)
    k = ease_out(prog(lt, 0.3, d - 0.4), 2)
    F.glow_text(f'{int(C.n_views * k):,}', 1830, 130, 110, fam='segoeb', weight=None, stroke=5, stroke_color=E.hexc('1b2a6e'),
                anchor=(1, 0.5), alpha=prog(lt, 0.2, 0.5))
    F.glow_text('screens  (WPF Views)', 1830, 210, 30, fam='segoe', weight=None, stroke=2, stroke_color=E.hexc('1b2a6e'),
                anchor=(1, 0.5), alpha=prog(lt, 0.4, 0.8))
    return dict(bloomAmt=0.7, vign=0.4, **O.flash(t, 25.8, 0.25, (0.7, 0.8, 1.0)))


def s_fly2(t, lt, d):
    """明日へ 今 飛び出せ: 雲の中を飛ぶ（立ち絵）。op の s_fly の雲に、主人公だけ差し替える"""
    gl = C.gl
    roll = 0.10 * math.sin(lt * 1.1 + 0.4)
    fx.sky(gl, 'day', t=t, cam=(0, roll))
    rng = np.random.default_rng(17)
    pal = clouds.PALETTES['day']
    items = []
    for j in range(30):
        z = (rng.uniform(0, 1) - lt * 0.45) % 1.0
        items.append((z, rng.uniform(-1.1, 1.1), rng.uniform(-0.3, 0.9), j))

    def cloud_at(z, x, y, j, alpha_k=1.0):
        s = 0.25 / (z + 0.08)
        tex = C.cl.cu[j % len(C.cl.cu)]
        w, h = tex.size
        sx = 960 + x * 900 / (z + 0.15); sy = 600 + y * 500 / (z + 0.15)
        a = smooth(prog(1 - z, 0.0, 0.25)) * (1 - smooth(prog(1 - z, 0.88, 1.0))) * alpha_k
        Hm = E.m3_translate(960, 540) @ E.m3_rot(roll) @ E.m3_translate(-960, -540) @ gl.rect_h(sx, sy, w * s * 0.5, h * s * 0.5)
        C.cl.draw(tex, Hm, pal, haze=0.5 * z, alpha=a)
    for z, x, y, j in sorted(items, key=lambda q: -q[0]):
        if z > 0.22:
            cloud_at(z, x, y, j)
    fx.focus_lines(gl, inner=0.55, seed=int(t * 15), amount=0.35, col=(1, 1, 1))
    enter = ease_out(prog(lt, 0.0, 0.9), 3)
    x = lerp(200, 900, enter) + 20 * math.sin(lt * 1.7)
    y = lerp(1100, 560, enter) + 16 * math.sin(lt * 3.0)
    ang = lerp(-0.35, -0.12, enter) + roll * 0.6        # 右上へ飛び出す
    speed_trail(x - 220, y + 60, t=t, length=900)
    fb = C.fb['flight']
    fb.draw(fb.H(x, y, lerp(1050, 1250, enter), angle=ang, by='width'), t, fb_at('flight', 1.0 + lt), breath=0.0,
            look=stand_look(rimCol=(1.2, 1.2, 1.3), rimDir=(0.5, -1.0), rimW=3.0))
    O.fog_bottom(E.hexc('cfe4ff'), 0.95, 900, 1080)
    for z, x2, y2, j in sorted(items, key=lambda q: -q[0]):
        if z <= 0.22:
            cloud_at(z, x2, y2, j, 0.55)        # 手前を横切る雲（体を隠しすぎない）
    return dict(bloomAmt=0.4, vign=0.15, ca=3.0)


def s_wings2(t, lt, d):
    return O.s_wings(t, lt, d)


def s_dive(t, lt, d):
    """飛び込んでく: 放射状に迫る実画面の世界へ、後ろ姿が飛び込む"""
    gl = C.gl
    fx.sky(gl, 'day', t=t, cam=(0, 0.1))
    fx.burst(gl, 0.5, 0.45, rot=t * 0.2, n=26, c1=E.hexc('cfe6ff'), c2=E.hexc('e6f2ff'), c3=(1, 1, 1), alpha=0.6)
    rng = np.random.default_rng(46)
    n = 44
    items = []
    for j in range(n):
        a = rng.uniform(0, 2 * math.pi)
        z = (rng.uniform(0, 1) - lt * 0.75) % 1.0      # 1 遠 → 0 手前
        items.append((z, a, j))
    for z, a, j in sorted(items, key=lambda q: -q[0]):
        r = 60 + 1500 * (1 - z) ** 2.2
        x = 960 + math.cos(a) * r * 1.2; y = 500 + math.sin(a) * r * 0.75
        s = 40 + 520 * (1 - z) ** 2
        tex = C.screens_all[(j * 5) % len(C.screens_all)]
        w, h = tex.size
        al = smooth(prog(1 - z, 0.0, 0.2)) * (1 - smooth(prog(1 - z, 0.9, 1.0)))
        gl.sprite(tex, x, y, scale=s / w, alpha=0.9 * al, angle=(a - math.pi / 2) * 0.05)
    fx.parts(gl, [960], [500], [700 + 900 * lt / d], (1.0, 0.97, 0.9), [0.55], kind=0)
    k = ease_in(prog(lt, 0.0, d), 2.2)
    fb = C.fb['dive']
    fb.draw(fb.H(960, lerp(1650, 600, k), lerp(1500, 120, k), angle=lerp(0.0, -0.15, k)), t, fb_at('dive', lt), breath=0.0,
            look=stand_look(rimCol=(1.2, 1.1, 1.3), rimDir=(0.0, -1.0), rimW=4.0, opacity=1 - smooth(prog(k, 0.85, 1.0))))
    fx.focus_lines(gl, inner=0.35, seed=int(t * 15), amount=0.6, col=(1, 1, 1))
    return dict(bloomAmt=0.5, vign=0.15, ca=3.0, flash=smooth(prog(lt, d - 0.2, d)) * 0.9, flashCol=(1, 1, 1))


def s_gate2(t, lt, d, ft=None):
    """まだ見ぬ明日の扉: 3階層を昇り、扉の前の後ろ姿が手を伸ばすと扉が開く。ft は連続コマの経過秒（既定は lt - 2.4）"""
    p = O.s_gate(t, lt, d)
    k = smooth(prog(lt, 2.2, 3.0))
    if k > 0:
        open_k = ease_io(prog(lt, 3.3, 4.5))
        # 「開けよう」の前から扉へ手を伸ばし（はじめの1秒で伸ばし切る）、扉が開くあいだ伸ばしたまま
        fb = C.fb['gate']
        fb.draw(fb.H(960, lerp(1500, 1180, k), 900), t, fb_at('gate', lt - 2.4 if ft is None else ft),
                    look=stand_look(silhouette=0.85, silCol=E.hexc('170c2a'), rimCol=(1.6 + 1.6 * open_k, 1.4 + 1.4 * open_k, 1.8),
                                    rimDir=(0.0, -1.0), rimW=4.0 + 3 * open_k, opacity=k))
    return p


def s_ending2(t, lt, d):
    """エンディング: ドラムソロの実画面 → 一撃で仕事着の立ち絵とロゴ"""
    gl = C.gl
    hit_t = T_CUT + 1.6
    if t < hit_t:
        return O.s_ending(t, lt, d)
    lt2 = t - hit_t
    O.sky_layer(t, 'dawn', cam=(0, 120), cloud_y=1000, scroll=10, n=8, scale=0.7, haze=0.45, seed=14, drift=0.6)
    fx.god_rays(gl, 0.62, 0.86, amount=0.8)
    pop = ease_out_back(prog(lt2, 0.0, 0.45), 1.6)
    Hm = C.work.H(1480, 1130 + 60 * (1 - pop), 1060)
    C.work.draw(Hm, t, wind=6 * math.sin(t), hem=3.0, look=stand_look(outlineW=5.0, outlineCol=(1, 1, 1)))
    O.fog_bottom(E.hexc('a65a86'), 0.9, 900, 1080)
    fx.parts(gl, [640], [560], [1400], E.hexc('140828'), [0.75], kind=0, blend='over')
    sc = 0.5 * (0.9 + 0.1 * ease_out_back(prog(lt2, 0, 0.4)))
    gl.sprite(C.logo, 640, 400, scale=sc * 1.06, alpha=0.8, tint=(0, 0, 0), add=(1.6, 1.0, 2.2), blend='add')
    gl.sprite(C.logo, 640, 400, scale=sc, tint=(0.35, 0.3, 0.45), add=(0.75, 0.72, 0.9))
    tex = gl.text('Creative Vision 10', 100, fam='segoeb', weight=None, color=(1, 1, 1), stroke=7, stroke_color=E.hexc('3b1a6e'))
    gl.sprite(tex, 640, 660)
    tex = gl.text('2027年1月 リリース予定', 44, fam='noto', weight='Black', color=E.hexc('fff3c4'), stroke=5, stroke_color=E.hexc('4a1a6a'))
    gl.sprite(tex, 640, 765)
    credit_footer(590, 875, 1150, 900, 170, alpha=prog(lt2, 0.0, 0.25))
    tex = gl.text(O.VOICES, 22, fam='noto', weight='Medium', color=(1, 1, 1), stroke=2, stroke_color=(0.2, 0.1, 0.3))
    gl.sprite(tex, 960, 1045, alpha=0.9)
    return dict(bloomAmt=0.6, vign=0.25, flash=pulse(t, hit_t, 0.01, 0.4), fade=prog(t, DURATION - 0.9, DURATION - 0.05))


def shifted(fn):
    """opfull の2番のショットを、その時刻（v1 の歌詞の位置）で描く"""
    def g(t, lt, d):
        return fn(t + SHIFT_V1, lt, d)
    g.shift = SHIFT_V1
    return g


SHOTS = [
    (0.0, 3.4, O.s_predawn),
    (3.4, 6.6, O.s_circle),
    (6.6, 9.8, s_reveal2),
    (9.8, 11.4, O.s_beats),
    (11.4, 13.0, O.s_title),
    (13.0, 16.2, s_wind2),
    (16.2, 19.4, shifted(s_open2)),
    (19.4, 22.6, shifted(F.f_ai)),
    (22.6, 25.8, shifted(F.f_morning)),
    (25.8, 29.0, s_screens),
    (29.0, 32.2, shifted(F.f_tests)),
    (32.2, 35.4, shifted(F.f_days)),
    (35.4, 38.6, shifted(F.f_shine)),
    (38.6, 41.8, O.s_chorus_title),
    (41.8, 45.0, s_fly2),
    (45.0, 46.6, s_wings2),
    (46.6, 48.2, s_dive),
    (48.2, 49.8, O.s_ikou),
    (49.8, 54.6, s_gate2),
    (54.6, 61.0, O.s_sunrise),
    (61.0, T_CUT, O.s_logo),
    (T_CUT, 1e9, s_ending2),
]


def credits(t):
    gl = C.gl
    for (a, b, lines, pos) in CREDITS:
        if a <= t <= b:
            al = min(prog(t, a, a + 0.4), 1 - prog(t, b - 0.4, b))
            for i, s in enumerate(lines):
                tex = gl.text(s, 22 if i == 0 else 32, fam='noto', weight='Bold', color=(1, 1, 1), stroke=3, stroke_color=(0.1, 0.05, 0.2))
                gl.sprite(tex, 70, 70 + i * 40, anchor=(0, 0), alpha=al * (0.85 if i == 0 else 1.0))


def frame(gl, t):
    global CUR_SHIFT
    post = {}
    for k in range(O.MB):
        ts = t + ((k + 0.5) / O.MB - 0.5) * O.SHUTTER / FPS if O.MB > 1 else t
        gl.time = ts
        gl.use(gl.main, clear=(0, 0, 0, 1))
        p = {}
        for a, b, fn in SHOTS:
            if a <= ts < b:
                CUR_SHIFT = getattr(fn, 'shift', 0.0)
                p = fn(ts, ts - a, b - a) or {}
                CUR_SHIFT = 0.0
                break
        if k == O.MB // 2:
            post = p
        gl.use(gl.main)
        if 13.0 <= t < T_CUT and not (38.6 <= t < 41.8):
            O.karaoke(t)
        credits(t)
        if O.MB > 1:
            gl.accumulate(1.0 / O.MB, k == 0)
    pp = dict(grainAmt=0.018, vign=0.25)
    pp.update(post)
    gl.time = t
    if O.MB > 1:
        pp['src'] = gl.acc
    return gl.finish(pp)


def audio():
    return O.audio()
