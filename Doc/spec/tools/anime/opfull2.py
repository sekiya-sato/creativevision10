# 作品1 v2 フルバージョン「明日へ飛べ」— 主題歌 v2 のフルコーラス（2分44秒）
# 0〜54.6秒は op2.py（v2 のショート版）のショットをそのまま使う。秒割りは v1 と同じなので、その先は歌詞に合わせて組み直す:
#   v2 の2番の歌詞は v1 の1番（op.py のショット）、サビ2・ラストサビは opfull.py のショットを、入れ替わった1行だけ差し替える。
# 主人公は背景なしの素材（rig_p: onlyperson の6表情・シーン別の連続コマ・仕事着の立ち絵）で合成する。動画生成AIは使わない。
#
# 秒割り（音源の時刻そのまま。150BPM・1小節1.6秒）
#    0.0- 54.6  op2.py と同じ（イントロ〜サビ1「開けよう 今すぐに」）
#   54.6- 62.6  雲海の日の出で歌い終える（最後は目を閉じた笑顔）    「目指すはひとつ あの未来へ」
#   62.6- 64.2  空を見上げて夜へ
#   64.2- 70.6  富士山と天の川、流れ星、星座の「10」              間奏
#   70.6- 73.8  冬の夜、最初のコミット（op）                      Aメロ2「たった30のファイル」
#   73.8- 77.0  画面の光とコードが顔に映る（op）                  「冬のはじまりだったね」
#   77.0- 80.2  コミットが積み上がる塔（op）                      「ひとつひとつと 重ねて」
#   80.2- 83.4  光のかけらが集まる（op）                          「夢のかけらを 集めて」
#   83.4- 86.6  CV.net → CV10 のバトン（op）                      Bメロ2「受け継いだバトン」
#   86.6- 89.8  200の灯籠（op）                                   「200社の笑顔」
#   89.8- 93.0  灯籠の中で歌う（op）                              「今はわたしがつなぐ」
#   93.0- 96.2  拍ごとのカットで溜める（op）                      「あの日があるから」
#   96.2- 99.4  タイトルの文字が飛び込む                          サビ2「Creative Vision 10！」
#   99.4-102.6  毎日のコミットの表が光になって集まり、魔法陣が灯る「重ねた日々 力に」
#  102.6-105.8  涙が光になる                                      「涙もぜんぶ 光になる」
#  105.8-107.4  いこう！
#  107.4-112.2  富士山の夜明けを見渡す後ろ姿（風の連続コマ）      「ほら 夜明けの空 晴れた」
#  112.2-115.4  ミニキャラが四季の里を駆け抜ける                  「ためらわず走ろう」
#  115.4-121.8  線で描いた実画面が色づいてロゴになる              「描いた夢が 未来になる」
#  121.8-128.2  全コミットの銀河（AI と共同のコミットは桃色）     ブリッジ
#  128.2-131.4  富士の湖に花火、金のタイトル                      ラストサビ「Creative Vision 10！」
#  131.4-134.6  4つの声の光がひとつの輪になる                    「声を合わせ ひとつに」
#  134.6-137.8  ミニキャラの群れと笑顔                            「みんなの笑顔 あふれ出すよ」
#  137.8-139.4  いこう！
#  139.4-146.6  3階層の扉へ手を伸ばし、灯籠が扉へ集まる           「まだ見ぬ明日の扉 / みんなで開こうよ」
#  146.6-153.8  夜明けの富士を背に瞳から全景へ                    「目指すはひとつ あの未来へ」
#  153.8-160.2  スタッフロール（実数値・プロデューサー・リポジトリと QR コード）→タイトル  アウトロ「Creative Vision 10！」
#  160.2-163.8  エンディング（op2 と同じ: 仕事着の立ち絵とロゴ）
#   python film.py opfull2                       四国めたん版 → out/opfull2_metan.mp4
#   python film.py opfull2 --variant zundamon    歌い手の版（opfull.py の SINGERS）
import os, math
import numpy as np
import op2 as O2                 # 歌詞 v2 への切り替えと op / opfull の読み込みは op2 が行う
import songsync as SS
import engine as E, fx
import op as O
import opfull as F
from engine import prog, lerp, smooth, ease_out, ease_in, ease_io, ease_out_back, pulse, clamp

FPS = 30
SUPERSAMPLE = 2
SINGER = O2.SINGER
OUT_NAME = f'opfull2_{SINGER}'
SONG = O2.SONG
DURATION = F.DURATION
C = None


def remap(t):
    return t


# 音源の時刻そのままで使う（op2 のショート版の切り方を戻す）
O.remap = O2.remap = remap
O.T_CUT = O2.T_CUT = SS.bar(100)          # エンディングの一撃の基準（音源 160.2 秒）
O.DURATION = O2.DURATION = DURATION
O.SONG = F.SONG = SONG
F.CREDITS2 = F.CREDITS2[:1]                # 背景原画の表記（ミニキャラは op2 の冒頭のクレジットにある）


def setup(gl):
    global C
    O2.setup(gl)
    C = O.C


def sh(fn, shift):
    """別の時刻（その歌詞の位置）のショットを描く。口パクは v2 の歌のまま"""
    def g(t, lt, d):
        return fn(t + shift, lt, d)
    g.shift = shift
    return g


# ---------------------------------------------------------------- 追加のショット
def s_power(t, lt, d):
    """重ねた日々 力に: 毎日のコミットの表（実データ）が灯り、光の粒になって主人公へ集まり、足元の魔法陣が灯る"""
    gl = C.gl
    fx.sky(gl, 'night', t=t, cam=(0, 0.05))
    fx.grad(gl, 960, 900, 2200, 500, (0.5, 0.25, 0.6, 0.0), (0.8, 0.4, 0.7, 0.5), blend='add')
    weeks, rows = C.heat_dims
    rev = ease_io(prog(lt, 0.0, 1.5), 1.5)
    gather = smooth(prog(lt, 1.2, 2.4))
    cam = E.Cam3(eye=(0.0, 0.4, -7.5 + 0.4 * lt / d), target=(0.0, 0.5, 0.0), fov=46)
    cell = 0.22
    W, Hh = weeks * cell, rows * cell
    wall_y = 2.25
    for w0 in range(0, weeks, 8):
        w1 = min(w0 + 8, weeks)
        cxw = -W / 2 + (w0 + w1) / 2 * cell
        H_, z = cam.quad_h(np.array([cxw, wall_y, 1.5]), np.array([1.0, 0, 0]), np.array([0, -1.0, 0]), (w1 - w0) * cell, Hh)
        if H_ is not None:
            gl.draw(F.GRID_FS, H_, dict(dims=(weeks, rows), reveal=rev, alpha=1.0 - 0.55 * gather, u0=w0 / weeks, u1=w1 / weeks),
                    dict(dat=C.heat))
    # 足元の魔法陣（集まるほど明るく）
    R3 = E.rot_y(lt * 0.5)
    H_, z = cam.quad_h((0, -1.9, -0.4), R3 @ np.array([1, 0, 0.0]), R3 @ np.array([0, 0, -1.0]), 6.5, 6.5)
    C.mg.draw(H_, prog=smooth(prog(lt, 0.6, 2.0)), spin=lt * 0.4, glow=1.0 + 2.5 * gather)
    Hm = O.char_H(1050, 980, 0.6, sx=960, sy=690)

    def pose(tt):
        p = O.base_pose(tt, wind=12 + 25 * smooth(prog(tt, 101.0, 101.8)), seed=40, sing=True)
        e = smooth(prog(tt, 101.2, 101.7))
        p.expr_full = p.expr_face = (1 - e, e, 0, 0)
        return p
    look = dict(cropBottom=1600, rimCol=(1.2 + 0.8 * gather, 0.8 + 0.6 * gather, 1.6), rimDir=(0, 1), rimW=8,
                tintAdd=(0.05 * gather, 0.02 * gather, 0.06 * gather))
    O.draw_char('power', t, 99.4 - 0.5, pose, Hm, look)
    O.fog_bottom((0.25, 0.12, 0.4), 0.9, 900, 1080)
    # 表の灯ったマスから光の粒が胸元へ
    hx, hy = F.rig_pt(Hm, 1120, 1450)
    rng = np.random.default_rng(99)
    n = 360
    wk = rng.integers(0, weeks, n); dy = rng.integers(0, rows, n)
    pts = np.stack([-W / 2 + (wk + 0.5) * cell, wall_y + Hh / 2 - (dy + 0.5) * cell, np.full(n, 1.5)], -1)
    scr, _ = cam.project(pts)
    ph = np.clip((lt - 1.2 - rng.uniform(0, 0.9, n)) / 0.9, 0, 1)
    live = (ph > 0) & (ph < 1)
    q = ph ** 2
    xs = lerp(scr[:, 0], hx, q) + np.sin(ph * 6 + wk) * 40 * (1 - q)
    ys = lerp(scr[:, 1], hy, q)
    fx.parts(gl, xs[live], ys[live], rng.uniform(8, 20, n)[live], E.hexc('ffb8f0'), 0.9, kind=1)
    fx.parts(gl, [hx], [hy + 80], [260 + 420 * gather], (1.0, 0.75, 1.0), [0.15 + 0.2 * gather], kind=0)   # 顔に掛けない
    # 「力に」で光が弾ける
    hit = pulse(t, 101.6, 0.02, 0.6)
    if hit > 0:
        a = np.linspace(0, 2 * math.pi, 48, endpoint=False)
        r = 200 + 900 * (1 - hit)
        fx.parts(gl, hx + np.cos(a) * r, hy + np.sin(a) * r * 0.6, np.full(48, 30), (1.0, 0.85, 1.0), np.full(48, hit), kind=1)
    F.glow_text(f'{C.n_days} days', 1830, 120, 64, fam='segoeb', weight=None, stroke=4, stroke_color=E.hexc('2a1458'),
                anchor=(1, 0.5), alpha=prog(lt, 0.2, 0.6))
    F.glow_text(f'{len(C.clog):,} commits', 1830, 190, 30, fam='segoe', weight=None, stroke=2, stroke_color=E.hexc('2a1458'),
                anchor=(1, 0.5), alpha=prog(lt, 0.4, 0.9))
    return dict(bloomAmt=0.7 + 0.15 * gather, vign=0.35, flash=0.25 * hit, flashCol=(1.0, 0.85, 1.0))


def s_fuji_dawn2(t, lt, d):
    """ほら 夜明けの空 晴れた: 富士山の夜が明けるのを、手前の後ろ姿が見渡す（風の連続コマ）"""
    gl = C.gl
    k = smooth(prog(lt, 0.4, 4.2))
    cz = ease_io(prog(lt, 0.0, d), 2)
    Hm = F.img_H(C.fuji_wh, lerp(1200, 1330, cz), lerp(900, 880, cz), lerp(0.82, 0.98, cz))
    sun = (0.80, lerp(0.67, 0.53, smooth(prog(lt, 1.2, d))))
    F.fuji(Hm, dawn=k, twinkle=1.0 - k, sun=sun)
    sx, sy = F.fuji_pt(Hm, *sun)
    up = smooth(prog(lt, 2.0, d))
    if up > 0:
        fx.god_rays(gl, sx / 1920, sy / 1080, amount=0.7 * up, decay=0.972, thr=0.95, focus=2.5)
        for j, (q, s, c) in enumerate([(0.4, 90, (1.0, 0.7, 0.5)), (0.75, 50, (0.6, 0.8, 1.0)), (1.25, 140, (1.0, 0.5, 0.7))]):
            fx.parts(gl, [sx + (960 - sx) * q * 2], [sy + (540 - sy) * q * 2], [s], c, [0.22 * up], kind=5)
    a = ease_out(prog(lt, 0.3, 1.6), 3)
    fb = C.fb['wind']
    fb.draw(fb.H(lerp(560, 600, cz), lerp(1500, 1260, a), 1020), t, O2.fb_at('wind', lt),
            look=dict(silhouette=0.75 * (1 - k), silCol=E.hexc('1c1636'), rimCol=(2.0 * k + 0.4, 1.3 * k + 0.4, 0.8 * k + 0.8),
                      rimDir=(1.0, 0.0), rimW=4.0, tintMul=(lerp(0.55, 1.0, k), lerp(0.58, 0.95, k), lerp(0.85, 0.95, k))))
    O.fog_bottom(tuple(np.array((0.25, 0.25, 0.45)) * (1 - k) + np.array((0.95, 0.7, 0.6)) * k), 0.85, 940, 1080)
    return dict(bloomAmt=0.55, vign=0.3)


def s_gate_full(t, lt, d):
    """まだ見ぬ明日の扉 / みんなで開こうよ: op2 の扉（手を伸ばす連続コマ）を引き延ばし、みんな（灯籠・ミニキャラ）が扉へ集まる"""
    gl = C.gl
    # 扉の動きはショットに合わせて引き延ばし、手を伸ばす連続コマは自然な速さで送る
    post = O2.s_gate2(t, lt * 4.8 / d, 4.8, ft=lt - 2.4 * d / 4.8)
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
        F.chibi_crowd(t, 10, 980, 1100, seed=141, states=('run',), alpha=k, xr=(100, 1820))
    return post


def staff_lines():
    return [
        ('主題歌', '「Creative Vision 10！ ～明日へ飛べ～」（v2）'),
        ('歌', F.VX(F.MAIN)),
        ('コーラス', '　'.join(F.VX(n) for n in F.CHORUS)),
        ('作画・撮影・編集', 'Python + GLSL（すべてコードで描画・動画生成AI不使用）'),
        ('キャラクター・背景原画', '表情差分・立ち絵・連続コマ・富士山と天の川・四季の里（静止画の生成画像）'),
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


F.staff_lines = staff_lines


def s_outro2(t, lt, d):
    """アウトロ: opfull のスタッフロールの下に、プロデューサー・リポジトリと QR コード（op2 のエンディングと同じ）"""
    p = F.f_outro(t, lt, d)
    if t < 157.2:
        O2.credit_footer(760, 860, 1350, 885, 170, alpha=min(prog(lt, 0.05, 0.35), 1 - prog(t, 157.0, 157.2)))
    return p
V1_1 = SS.bar(36)               # 57.6: op.py の1番（v1 の歌詞）→ v2 の2番


SHOTS = [(a, b, fn) for (a, b, fn) in O2.SHOTS if b <= 54.6] + [
    (54.6, 62.6, O.s_sunrise),
    (62.6, 64.2, F.f_to_night),
    (64.2, 70.6, F.f_fuji_night),
    (70.6, 73.8, sh(O.s_firstcommit, -V1_1)),
    (73.8, 77.0, sh(O.s_codeface, -V1_1)),
    (77.0, 80.2, sh(O.s_tower, -V1_1)),
    (80.2, 83.4, sh(O.s_gather, -V1_1)),
    (83.4, 86.6, sh(O.s_baton, -V1_1)),
    (86.6, 89.8, sh(O.s_lanterns, -V1_1)),
    (89.8, 93.0, sh(O.s_connect, -V1_1)),
    (93.0, 96.2, sh(O.s_build, -V1_1)),
    (96.2, 99.4, F.f_title2),
    (99.4, 102.6, s_power),
    (102.6, 105.8, F.f_tears),
    (105.8, 107.4, F.f_ikou2),
    (107.4, 112.2, s_fuji_dawn2),
    (112.2, 115.4, F.f_run),
    (115.4, 121.8, F.f_draw),
    (121.8, 128.2, F.f_galaxy),
    (128.2, 131.4, F.f_title3),
    (131.4, 134.6, sh(F.f_voices, 99.4 - 131.4)),     # v1 のサビ2の行が v2 ではラストサビへ
    (134.6, 137.8, F.f_smiles),
    (137.8, 139.4, F.f_ikou3),
    (139.4, 146.6, s_gate_full),
    (146.6, 153.8, F.f_finale),
    (153.8, 160.2, s_outro2),
    (160.2, 1e9, O2.s_ending2),
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
                O2.CUR_SHIFT = getattr(fn, 'shift', 0.0)
                p = fn(ts, ts - a, b - a) or {}
                O2.CUR_SHIFT = 0.0
                break
        if k == MB // 2:
            post = p
        gl.use(gl.main)
        if 13.0 <= t and not any(a <= t < b for a, b in NO_KARAOKE):
            O.karaoke(t)
        O2.credits(t)
        F.credits2(t)
        if MB > 1:
            gl.accumulate(1.0 / MB, k == 0)
    pp = dict(grainAmt=0.018, vign=0.25)
    pp.update(post)
    gl.time = t
    if MB > 1:
        pp['src'] = gl.acc
    return gl.finish(pp)


def audio():
    return F.audio()
