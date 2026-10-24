# 和楽器版（jp 版）の伴奏。ドラム・ギター・ベースを和楽器に置き換える。
# 和楽器の無料実録素材が乏しいため、手元の実録サンプル（CC0、sampler.py）を和楽器の音へ作り替える。
#   和太鼓一式  実録ドラム（バスドラム・タム・リム・ライドのベル）を移調・加工
#   三味線      実録ギターの単音に、皮の胴の減衰・張り・さわり・撥の打撃を加える
#   箏          実録ギターのクリーン単音に、長い木の胴の響きと爪の当たりを加える
#   十七絃      実録ベースに、箏の胴の響きと爪の当たりを加える
#   篠笛・尺八・笙  合成（息・装飾・揺り、笙は倍音を重ねた合竹）
#   python jp.py            伴奏 creativevision10_inst_jp.m4a
#   python jp.py --demo     楽器ごとの試聴用 jp_demo/*.m4a（音作りの確認用）
import os, math, argparse
import numpy as np
from scipy.ndimage import uniform_filter1d

import song as S
import sampler as P
import band as B
C = S.C
SR = C.SR
E8, BAR = S.E8, S.BAR
E16 = E8 / 2


def norm(x, peak=0.9):
    return (x / (np.abs(x).max() + 1e-9) * peak).astype(np.float32)


def eq_peaks(x, peaks):
    """[(低, 高, 量)] の帯域を足し引きする簡易 EQ"""
    y = x.copy()
    for lo, hi, g in peaks:
        y = y + g * C.bandpass(x, lo, hi)
    return y

# ---------------------------------------------------------------- 和太鼓一式（実録ドラムから）
class Wadaiko:
    """実録ドラムを和太鼓へ。移調量は元素材の胴鳴りの基音（実測）から目標の基音へ合わせて決めた
    （元素材: ミュートなしバスドラム 105Hz、フロアタム 46Hz、ハイタム 205Hz。2026-10-24 測定）"""

    def __init__(self, kit, rng):
        self.kit, self.rng = kit, rng

    def _h(self, piece, v, choke=None):
        return self.kit.hit(piece, min(1.0, max(0.03, v)), self.rng, choke)

    @staticmethod
    def _mix(*layers):
        n = max(len(x) for x, _ in layers)
        y = np.zeros((n, 2), np.float32)
        for x, g in layers:
            y[:len(x)] += x * g
        return y

    def odaiko(self, v):
        """大太鼓（基音 約55Hz、余韻 2〜3秒）"""
        y = self._mix((P.shift(self._h('taiko', v), -12), 1.0), (P.shift(self._h('toml', v), 3), 0.5))
        return np.stack([C.lowpass(y[:, c], 3500, 2) for c in range(2)], 1)

    def don(self, v):
        """長胴太鼓の面打ち（基音 約100Hz、余韻 1〜1.5秒）。撥の当たりをフロアタムで足す"""
        y = self._mix((P.shift(self._h('taiko', v), -1), 1.0), (P.shift(self._h('toml', v * 0.8), 12), 0.25))
        return np.stack([C.highpass(y[:, c], 55, 2) for c in range(2)], 1)

    def ka(self, v):
        """縁打ち: リムを下げて短く、木の撥の硬さを足す"""
        x = P.shift(self._h('rim', v, choke=0.07), -5).copy()
        k = int(0.004 * SR)
        x[:k] += (C.highpass(C.noise(0.004, int(v * 100)), 2500)[:k] * 0.4 * v)[:, None]
        return x

    def okedo(self, v):
        """桶胴太鼓（基音 約155Hz、余韻 約1秒）"""
        y = self._mix((P.shift(self._h('tomh', v, choke=0.55), -5), 1.0),
                      (P.shift(self._h('rim', v * 0.6, choke=0.05), -2), 0.2))
        return np.stack([C.highpass(y[:, c], 140, 4) for c in range(2)], 1)     # 他の太鼓のかぶりを除く（基音 約180Hz は残す）

    def shime(self, v):
        """締太鼓（基音 約410Hz、短く張りの強い「テン」）"""
        y = self._mix((P.shift(self._h('tomh', v, choke=0.25), 12), 1.0),
                      (P.shift(self._h('rim', v * 0.5, choke=0.04), 4), 0.3))
        return np.stack([C.highpass(y[:, c], 320, 4) for c in range(2)], 1)     # 他の太鼓のかぶりを除く（基音 約410Hz を残す）

    def kane(self, v, open_=False):
        """当たり鉦（金属の高域 2〜5kHz、止めると短い）"""
        x = P.shift(self._h('bell', v, choke=0.6 if open_ else 0.14), 2)
        return np.stack([C.lowpass(C.highpass(x[:, c], 800, 2), 5000, 2) for c in range(2)], 1)

# ---------------------------------------------------------------- 撥弦楽器（実録ギター・ベースから）
class Strings:
    def __init__(self, kit, rng):
        self.g, self.b = P.guitar(tune=True), P.bass(tune=True)      # 録音ごとの音程のずれを補正
        self.kit, self.rng = kit, rng

    def shamisen(self, n, v, dur, sukui=False):
        """三味線: 皮の胴で減衰が速く、中高域が張り、さわりが鳴る。撥の打撃音を頭に付ける"""
        x = self.g.note(n, v, dur + 0.6, self.rng, release=0.06).astype(np.float64)
        t = np.arange(len(x)) / SR
        x *= np.exp(-t / (0.4 if not sukui else 0.25))
        x = C.highpass(x, max(170, C.midi(n)), 2)                        # 皮の胴: 基音が弱く倍音が強い
        x = eq_peaks(x, [(2000, 5000, 1.0), (3000, 8000, 1.4), (700, 1100, 0.35), (250, 400, -0.4)])
        env = uniform_filter1d(np.abs(x), int(0.005 * SR))
        ref = np.abs(x).max() + 1e-9
        buzz = C.highpass(np.tanh(10 * C.bandpass(x, 800, 7000) / ref), 2500, 2) * np.clip(env / ref * 4, 0, 1)
        amt = 0.45 + 0.45 * float(np.clip((64 - n) / 12, 0, 1))          # さわりは一の糸（低い音）ほど強い
        x = x + amt * buzz * ref                                          # さわり（倍音を高域へ広げる）
        bachi = self.kit.hit('rim', min(1.0, v * (0.5 if sukui else 1.0)), self.rng, choke=0.03)[:, 0]
        bachi = C.highpass(P.shift(bachi, 12).astype(np.float64), 2000, 2)
        k = min(len(bachi), len(x))
        x[:k] += bachi[:k] * (0.35 if not sukui else 0.12) * v / (np.abs(bachi).max() + 1e-9) * np.abs(x).max()
        return x.astype(np.float32)

    def koto(self, n, v, dur):
        """箏: ギターのクリーン単音に、長い胴の響き（低中域）と爪の当たり（高域の短い雑音）を加える"""
        x = self.g.note(n, v, max(dur, 2.5) + 0.6, self.rng, release=0.4).astype(np.float64)   # 箏は弦が鳴り続ける
        t = np.arange(len(x)) / SR
        x *= np.exp(-t / 3.0)
        x = C.highpass(x, 140, 2)
        x = eq_peaks(x, [(220, 420, 0.5), (1500, 3200, 0.8), (2000, 6000, 1.0), (6000, 10000, 0.5)])
        ref = np.abs(x).max() + 1e-9
        x = x + 0.25 * ref * C.highpass(np.tanh(4 * x / ref), 3000, 2)           # 絹・テトロン弦の明るい倍音
        k = int(0.005 * SR)
        x[:k] += C.highpass(C.noise(0.005, n * 7 + int(v * 10)), 3500)[:k] * np.abs(x).max() * 0.8 * v   # 爪
        return x.astype(np.float32)

    def jushichi(self, n, v, dur):
        """十七絃（低音箏）: 実録ベースに箏の胴の響きと爪の当たり、余韻を長めに"""
        x = self.b.note(n, v, dur + 0.4, self.rng, release=0.15).astype(np.float64)
        t = np.arange(len(x)) / SR
        x *= np.exp(-t / 1.3)
        x = eq_peaks(x, [(120, 220, 0.4), (900, 1800, 0.5), (3000, 6000, 0.4)])
        x *= 1 + 1.5 * np.exp(-t / 0.015)                                 # 爪で弾く鋭い立ち上がり
        k = int(0.006 * SR)
        x[:k] += C.highpass(C.noise(0.006, n * 3 + 1), 2500)[:k] * np.abs(x).max() * 0.6 * v
        sub = B.sub_note(n, dur, v) * 0.35
        m = min(len(x), len(sub))
        x[:m] += sub[:m] * np.abs(x).max()
        return x.astype(np.float32)

# ---------------------------------------------------------------- 管楽器（合成）
def shinobue(n, dur, v=1.0, seed=0, grace=True):
    """篠笛: 高く明るい息の多い音。頭に上からの打ち指（装飾）、伸ばす音に遅れた揺り"""
    f = C.midi(n)
    t = C.t_axis(dur)
    semis = np.zeros_like(t)
    if grace and dur > 0.15:
        semis += 2.0 * (t < 0.035)                                   # 打ち指（上の音を一瞬）
    semis += 0.25 * np.clip((t - 0.28) / 0.25, 0, 1) * np.sin(2 * np.pi * 5.4 * t)
    semis += 0.06 * C.lowpass(C.noise(dur, seed + 9), 8) * 20      # 息による微小な揺れ
    ph = 2 * np.pi * np.cumsum(f * 2 ** (semis / 12)) / SR
    tone = np.sin(ph) + 0.18 * np.sin(2 * ph) + 0.06 * np.sin(3 * ph)
    breath = C.bandpass(C.noise(dur, seed), min(f * 0.9, 9000), min(f * 4, 18000)) * 0.45
    chiff = C.bandpass(C.noise(dur, seed + 1), 2000, 9000) * np.exp(-t / 0.03) * 0.8
    env = C.env_adsr(len(t), 0.03, 0.1, 0.85, min(0.12, dur / 3))
    return ((tone + breath) * env + chiff) * v


def sho(notes, dur, v=1.0, seed=0):
    """笙（合竹）: リードの倍音列を重ね、管ごとに少しずらしてうなりを出す。ゆっくり立ち上がる"""
    rng = np.random.default_rng(seed)
    t = C.t_axis(dur)
    out = np.zeros_like(t)
    for n in notes:
        f = C.midi(n) * (1 + rng.uniform(-0.002, 0.002))
        for k in range(1, 9):
            out += np.sin(2 * np.pi * f * k * t + rng.uniform(0, 6.28)) / k ** 1.3
    out = C.lowpass(out / len(notes), 3500, 2) + C.bandpass(C.noise(dur, seed), 800, 5000) * 0.04
    return out * C.env_adsr(len(t), min(1.0, dur / 3), 0.0, 1.0, min(0.6, dur / 3)) * v

# ---------------------------------------------------------------- 編曲
# ドラムの各打点を和太鼓へ置き換える（band.kit_bar のパターンを使う）
DRUM_TO_WA = {'kick': 'don', 'snare': 'okedo+ka', 'rim': 'ka', 'hat': 'shime', 'hatl': 'shime', 'ohat': 'kane',
              'open': 'kane_open', 'tomh': 'shime', 'tomm': 'okedo', 'toml': 'don', 'crash': 'odaiko+kane',
              'crash2': 'kane_open', 'ride': 'kane', 'bell': 'kane'}
PART_LU = {'wadaiko': 0.0, 'bass': -5.0, 'sham': -3.5, 'koto': -6.0, 'lead': -3.0, 'sho': -14.0}


def render(timeline, total, stems_dir=None):
    dur = total + 4.0
    n = int(dur * SR)
    rng = np.random.default_rng(11)
    kit = P.Kit()
    W, ST = Wadaiko(kit, rng), Strings(kit, rng)
    T = {'wadaiko': B.Buf(n, 2)}
    T.update({k: B.Buf(n) for k in ('shamL', 'shamR', 'koto', 'bass', 'lead', 'shaku', 'sho')})
    seed = 0

    def wa(kind, t, v):
        v = v * rng.uniform(0.92, 1.05)
        t = t + rng.normal(0, 0.003)
        for k in kind.split('+'):
            x = {'don': W.don, 'okedo': W.okedo, 'ka': W.ka, 'shime': W.shime, 'odaiko': W.odaiko,
                 'kane': W.kane, 'kane_open': lambda vv: W.kane(vv, True)}[k](v * (0.8 if k in ('kane', 'kane_open') else 1.0))
            T['wadaiko'].add(t, x)

    def sham(side, t, notes, d, v, sukui=False):
        for k, m in enumerate(notes):
            T[side].add(t + k * 0.004, ST.shamisen(m, v, d, sukui))

    for si, s in enumerate(timeline):
        sec = s['sec']
        name = sec['name']
        plan = B.PLAN[name]
        sh = s['shift']
        nb = s['bars']
        nxt = timeline[si + 1] if si + 1 < len(timeline) else None
        if name in ('Aメロ1', 'Bメロ1', 'サビ1', '間奏', 'Aメロ2', 'Bメロ2', 'サビ2', 'ブリッジ', 'ラストサビ', 'アウトロ'):
            wa('odaiko+kane_open', s['t'], 0.9)
        for i, c in enumerate(s['chords']):
            t0 = s['t'] + i * BAR
            nxt_chord = s['chords'][i + 1] if i + 1 < nb else (nxt['chords'][0] if nxt else c)
            nxt_shift = sh if i + 1 < nb else (nxt['shift'] if nxt else sh)
            tones = S.chord_tones(c, sh)
            pc, minor = S.parse_chord(c)
            # ---- 和太鼓（ドラムの打点を置き換え＋大太鼓・締太鼓の地打ち）
            if plan['kit'] != 'solo':
                for k, p, v in B.kit_bar(plan['kit'], i, nb):
                    wa(DRUM_TO_WA[k], t0 + p * E16, v * (0.8 if k in ('hat', 'hatl', 'ohat') else 1.0))
                for k, p, v in B.taiko_bar(plan, i, nb):
                    if k == 'doon':
                        wa('odaiko', t0 + p * E16, v)
            if plan['wa'] == 'intro' and i < 4:
                pats = [((0, 1.0),), ((0, 0.85), (8, 0.7)), ((0, 1.0), (6, 0.65), (8, 0.9), (14, 0.6)),
                        ((0, 1.0), (4, .8), (8, .9), (10, .7), (12, 1.0), (14, .9), (15, 1.0))]
                for p, v in pats[i]:
                    wa('odaiko' if i == 0 or p == 0 else 'don', t0 + p * E16, v)
                if i >= 2:
                    for p in range(0, 16, 2 if i == 2 else 1):
                        wa('shime', t0 + p * E16, (0.3 if p % 4 else 0.45) if i == 2 else 0.25 + 0.65 * p / 15)
            if plan['wa'] == 'bridge':
                for p, v in ((0, 1.0), (3, .6), (6, .7), (10, .7), (12, .8), (14, .6)):
                    wa('odaiko' if p == 0 else 'don', t0 + p * E16, v)
                for p in range(16):
                    wa('shime', t0 + p * E16, 0.3 + 0.4 * (i == nb - 1) * p / 15)
            # ---- 三味線（ギターの代わり）と箏（右ギターの代わり）
            g = plan['gtr']
            r = B.root_of(c, sh, 48)                                 # 三味線の音域（C3〜）
            chord3 = [r, r + 7, r + 12]
            kr = B.root_of(c, sh, 60)
            koto_ch = [kr, kr + (3 if minor else 4), kr + 7, kr + 12]
            if g == 'verse':
                for p in range(8):
                    m = r if p % 2 == 0 else r + 7
                    sham('shamL', t0 + p * E8, [m], E8 * 0.8, 0.8 if p in (0, 3, 6) else 0.55, sukui=p % 2 == 1)
                arp = [kr, kr + 7, kr + 12, kr + (15 if minor else 16)]
                for p in range(8):
                    T['koto'].add(t0 + p * E8, ST.koto(arp[[0, 1, 2, 3, 2, 1, 2, 3][p]], 0.5, E8 * 2.5))
            elif g == 'build':
                pat = [(0, 3, 1.0), (3, 1, 0.7), (4, 2, 0.95), (6, 2, 0.8)] if i < nb - 2 else [(p, 1, 0.9 if p % 2 == 0 else 0.7) for p in range(8)]
                for p, ln, v in pat:
                    sham('shamL', t0 + p * E8, chord3, ln * E8, v, sukui=p % 2 == 1)
                    sham('shamR', t0 + p * E8 + 0.008, chord3, ln * E8, v * 0.9, sukui=p % 2 == 1)
            elif g == 'chorus' or (g == 'intro' and i >= 4):
                acc = [1.0, 0.6, 0.8, 0.85, 0.95, 0.6, 0.8, 0.85]
                for p in range(8):
                    sham('shamL', t0 + p * E8, chord3, E8 + 0.02, acc[p], sukui=p % 2 == 1)
                    sham('shamR', t0 + p * E8 + 0.008, chord3[::-1], E8 + 0.02, acc[p] * 0.9, sukui=p % 2 == 1)
                for p in (0, 4):                                          # 箏の和音（拍頭で掻き鳴らす）
                    for k, m in enumerate(koto_ch):
                        T['koto'].add(t0 + p * E8 + k * 0.02, ST.koto(m, 0.75, E8 * 4))
            elif g == 'bridge':
                if i < nb - 1:
                    for p, m in enumerate([kr, kr + 7, kr + 12, kr + 7, kr + (15 if minor else 16), kr + 7, kr + 12, kr + 7]):
                        T['koto'].add(t0 + p * E8, ST.koto(m, 0.5, E8 * 3))
                else:
                    for p in range(16):
                        sham('shamL', t0 + p * E16, chord3, E16 * 0.9, 0.4 + 0.6 * p / 15, sukui=p % 2 == 1)
            # ---- 十七絃（ベースの代わり。band と同じ音の動き）
            bp = plan['bass']
            br = B.root_of(c, sh, 28)
            nr = B.root_of(nxt_chord, nxt_shift, 28)
            steps = []
            if bp == 'intro':
                steps = [] if i < 4 else [(p, 1, .9, br + (12 if p in (2, 6) else 0)) for p in range(8)]
            elif bp == 'verse':
                steps = [(p, 2, 0.9, br + (7 if p == 4 else 0)) for p in (0, 4)] + [(6, 1, 0.7, br + 12)]
            elif bp == 'build':
                steps = [(p, 1, 0.9, br + (12 if p == 7 else 0)) for p in range(0, 8, 2)]
                if i == nb - 1:
                    run = [br, br + 2, br + 4, br + 5, br + 7, br + 9, br + 11, br + 12]
                    steps = [(p, 1, 0.95, run[p]) for p in range(8)]
            elif bp == 'chorus':
                oct_ = [0, 0, 12, 0, 0, 0, 12, 0]
                steps = [(p, 1, 0.95 if p % 2 == 0 else 0.8, br + oct_[p]) for p in range(8)]
            elif bp == 'half':
                steps = [(0, 4, 0.9, br), (4, 4, 0.8, br)] if i < nb - 1 else [(p, 1, 0.5 + 0.45 * p / 7, br) for p in range(8)]
            if steps and nr != br and bp == 'chorus':
                p, ln, v, _ = steps[-1]
                steps[-1] = (p, ln, v, nr - 1 if nr > br else nr + 2)
            for p, ln, v, m in steps:
                T['bass'].add(t0 + p * E8 + rng.normal(0, 0.003), ST.jushichi(m, v, ln * E8 * 0.9))
            # ---- 笙（パッドの代わり）
            if plan['kit'] in ('chorus', 'build', 'bridge') or plan['wa'] == 'intro':
                aitake = sorted(set(tones + [tones[0] + 12, tones[0] + 14]))
                first = plan['wa'] == 'intro' and i == 0
                T['sho'].add(t0, sho(aitake, BAR + 0.3, 1.0, seed := seed + 1) * (0.6 if first else 1.0))
            # ---- 箏のアルペジオ・グリッサンド（band と同じ場面）
            wa_ = plan['wa']
            if wa_ == 'intro' and 1 <= i < 4 or wa_ in ('koto', 'sham+koto') and (wa_ == 'koto' or i % 2 == 1) or wa_ == 'last' and i >= 8:
                arp = [tones[0] + 12, tones[1] + 12, tones[2] + 12, tones[0] + 24]
                arp = [x + 12 if x < 67 else x for x in arp]
                pat = [0, 1, 2, 3, 2, 1, 2, 3] if wa_ != 'last' else [0, 1, 2, 3, 1, 2, 3, 2, 0, 1, 2, 3, 1, 2, 3, 2]
                step = E8 if len(pat) == 8 else E16
                for k, idx in enumerate(pat):
                    kv = (0.8 if k % 4 else 1.0) * ((0.55 + 0.15 * i) if wa_ == 'intro' else 0.8)
                    T['koto'].add(t0 + k * step, ST.koto(arp[idx], kv, 1.2))
            if i == nb - 1 and nxt and B.PLAN[nxt['sec']['name']]['kit'] == 'chorus' and name != 'アウトロ' or (wa_ == 'intro' and i in (0, 3)):
                pn = B.penta(nxt_shift if i == nb - 1 else sh, 67, 100)[:14]
                for k, m in enumerate(pn):
                    T['koto'].add(t0 + BAR - (len(pn) - k) * 0.03, ST.koto(m, 0.5 + 0.4 * k / len(pn), 1.0))
        # ---- 旋律（イントロ前半＝尺八、後半＝篠笛、間奏＝三味線＋篠笛）
        for e in s['events']:
            if e['kind'] != 'lead':
                continue
            bar = int((e['t'] - s['t']) / BAR + 1e-6)
            if name == 'イントロ' and bar < 4:
                first = e is next(x for x in s['events'] if x['kind'] == 'lead')
                T['shaku'].add(e['t'], B.shakuhachi(e['note'], e['dur'] + 0.05, 0.8 if first else 1.0, seed := seed + 1,
                                                    attack=0.35 if first else 0.06))
                continue
            if name != 'イントロ':
                T['shamR'].add(e['t'], ST.shamisen(e['note'], 1.0, max(e['dur'], 0.25)))
            T['lead'].add(e['t'], shinobue(e['note'] + 12, e['dur'] + 0.03, 1.0, seed := seed + 1))
        if name == 'ブリッジ':
            pos = 0
            for p, d in S.parse_notes(B.BRIDGE_SOLO):
                if p is not None:
                    T['shaku'].add(s['t'] + pos * E8, B.shakuhachi(p + sh, d * E8 + 0.05, 1.0, seed := seed + 1))
                pos += d
        # ---- エンディング: 和太鼓のソロ → 全員で一撃
        if name == 'エンディング':
            t0 = s['t']
            i0, i1 = int((t0 + 0.02) * SR), int((t0 + BAR) * SR)
            f = int(0.06 * SR)
            for k in ('shamL', 'shamR', 'koto', 'bass', 'lead', 'shaku', 'sho'):
                T[k].x[i0:i0 + f] *= np.linspace(1, 0, f)
                T[k].x[i0 + f:i1] = 0
            solo = [('odaiko', 0, 1), ('shime', 2, .8), ('shime', 3, .6), ('okedo', 4, .9), ('okedo', 5, .8),
                    ('don', 6, .9), ('don', 7, .85), ('odaiko', 8, 1), ('don', 9, 1), ('ka', 10, .8), ('don', 11, .9),
                    ('shime', 12, .7), ('shime', 12.5, .6), ('shime', 13, .8), ('shime', 13.5, .7),
                    ('okedo', 14, .9), ('don', 14.5, .9), ('odaiko', 15, 1)]
            for k, p, v in solo:
                wa(k, t0 + p * E16, v)
            hit = t0 + BAR
            r = B.root_of(s['chords'][-1], sh, 48)
            for side in ('shamL', 'shamR'):
                sham(side, hit, [r, r + 7, r + 12], 0.4, 1.0)
            T['bass'].add(hit, ST.jushichi(B.root_of(s['chords'][-1], sh, 28), 1.0, 0.4))
            wa('odaiko+kane_open+don', hit, 1.0)
    lv = np.ones(n, np.float32)
    for s in timeline:
        g = next((v for key, v in B.SECTION_LEVEL.items() if s['sec']['name'].startswith(key)), 1.0)
        lv[int(s['t'] * SR):int((s['t'] + s['bars'] * BAR) * SR)] = g
    ti = timeline[0]['t']
    k0, k1 = int(ti * SR), int((ti + 4 * BAR) * SR)
    lv[k0:k1] *= 0.5 + 0.42 * np.linspace(0, 1, k1 - k0) ** 1.3
    lv = uniform_filter1d(lv, int(0.08 * SR))
    for k in T:
        T[k].x *= lv[:, None] if T[k].x.ndim == 2 else lv
    return mixdown(T, n, timeline, stems_dir)


def mixdown(T, n, timeline, stems_dir=None):
    x = {k: v.x for k, v in T.items()}
    wad = x['wadaiko']
    wad = B.compress(wad, np.percentile(np.abs(wad), 99.5) * 0.6, 2.5, att=0.005, rel=0.15)
    wad = wad + C.reverb(wad, 2.2, wet=1.0, damp=5000, seed=3) * 0.18          # 板張りのホール
    low_env = B.env_follow(C.lowpass(wad.mean(1), 150, 2), 30)
    sc = 1 - 0.2 * np.clip(low_env / (np.percentile(low_env, 99) + 1e-9), 0, 1)
    sham = B.pan(x['shamL'], -0.45) + B.pan(x['shamR'], 0.45)
    koto = B.pan(x['koto'], 0.6)
    bass = B.pan(B.compress(x['bass'], np.percentile(np.abs(x['bass']), 99) * 0.7 + 1e-6, 3), 0) * sc[:, None]
    lead = B.pan(x['lead'], 0.15) + B.pan(x['shaku'], 0.15)
    sho_ = B.pan(C.highpass(x['sho'], 200), 0)
    sho_ = np.stack([sho_[:, 0], np.roll(sho_[:, 1], int(0.012 * SR))], 1)          # 左右で少しずらして広げる
    parts = dict(wadaiko=wad, sham=sham, koto=koto, bass=bass, lead=lead, sho=sho_)
    ch = np.zeros(n, bool)
    for s in timeline:
        if B.PLAN[s['sec']['name']]['kit'] == 'chorus':
            ch[int(s['t'] * SR):int((s['t'] + s['bars'] * BAR) * SR)] = True
    ref = B.lufs(parts['wadaiko'][ch])
    gains = {}
    for k, v in parts.items():
        m = ch & (np.abs(v).max(1) > 1e-4) if k == 'lead' else ch
        if m.sum() < SR:
            m = np.abs(v).max(1) > 1e-4
        gains[k] = 10 ** ((ref + PART_LU[k] - B.lufs(v[m])) / 20)
    mix = sum(v * gains[k] for k, v in parts.items())
    room = C.reverb(mix - parts['wadaiko'] * gains['wadaiko'], 2.0, wet=1.0, damp=7000, seed=8) * 0.12
    mix = mix + room
    B.REF = mix
    if stems_dir:
        os.makedirs(stems_dir, exist_ok=True)
        k0 = 0.9 / (np.abs(mix).max() + 1e-9)
        cs = [c for c, _ in B.TARGET]
        rows = ['| パート | サビ LU | ' + ' | '.join(str(c) for c in cs) + ' |', '|---' * (len(cs) + 2) + '|']
        for k, v in parts.items():
            v = v * gains[k]
            C.write_wav(os.path.join(stems_dir, f'{k}.wav'), v * k0)
            rows.append(f'| {k} | {B.lufs(v[ch]) - B.lufs(mix[ch]):+.1f} | ' + ' | '.join(f'{l:.1f}' for l in B.band_levels(v, cs)) + ' |')
        with open(os.path.join(stems_dir, 'levels.md'), 'w', encoding='utf-8') as f:
            f.write(chr(10).join(rows) + chr(10))
        print(chr(10).join(rows))
    return mix.astype(np.float32)


def demo():
    """楽器ごとの試聴音（音作りの確認用）を jp_demo/ へ"""
    d = os.path.join(S.OUT, 'jp_demo')
    os.makedirs(d, exist_ok=True)
    rng = np.random.default_rng(5)
    kit = P.Kit()
    W, ST = Wadaiko(kit, rng), Strings(kit, rng)

    def put(name, items, length):
        buf = np.zeros((int(length * SR), 2), np.float32)
        for t, x in items:
            x = x if x.ndim == 2 else np.stack([x, x], 1)
            i = int(t * SR)
            m = min(len(x), len(buf) - i)
            buf[i:i + m] += x[:m]
        buf = buf + C.reverb(buf, 1.8, wet=1.0, seed=2) * 0.12
        C.write_wav(os.path.join(d, name + '.wav'), norm(buf, 0.8))
        S.encode(norm(buf, 0.8), os.path.join('jp_demo', name), False)
        os.remove(os.path.join(d, name + '.wav')) if os.path.exists(os.path.join(d, name + '.wav')) else None
    beat = 60 / S.BPM
    # 和太鼓: 大太鼓・長胴（弱→強）・縁・桶胴・締太鼓の地打ち・当たり鉦
    seq = [(0, W.odaiko(1.0)), (2 * beat, W.don(0.4)), (3 * beat, W.don(0.7)), (4 * beat, W.don(1.0)), (5 * beat, W.ka(0.8)),
           (6 * beat, W.okedo(0.9)), (7 * beat, W.okedo(0.6))]
    seq += [(8 * beat + k * beat / 2, W.shime(0.4 + 0.3 * (k % 2 == 0))) for k in range(8)]
    seq += [(12 * beat + k * beat / 2, W.kane(0.7 if k % 2 == 0 else 0.45)) for k in range(8)]
    seq += [(16 * beat, W.odaiko(1.0)), (16 * beat, W.don(1.0))]
    put('wadaiko', seq, 20 * beat + 3)
    put('shamisen', [(k * beat / 2, ST.shamisen(48 + [0, 7, 12, 7, 5, 7, 12, 15][k], 0.9, beat / 2, sukui=k % 2 == 1)) for k in range(8)]
        + [(4 * beat + k * 0.004, ST.shamisen(m, 1.0, beat)) for k, m in enumerate([50, 57, 62])], 6 * beat + 1)
    put('koto', [(k * beat / 2, ST.koto(m, 0.8, beat)) for k, m in enumerate([62, 64, 67, 69, 72, 74, 76, 79, 81, 84])]
        + [(6 * beat + k * 0.03, ST.koto(m, 0.7, 2.0)) for k, m in enumerate(range(60, 90, 3))], 9 * beat + 2)
    put('jushichi', [(k * beat, ST.jushichi(m, 0.9, beat)) for k, m in enumerate([28, 33, 36, 31, 29, 40])], 7 * beat + 1)
    put('fue', [(0, shinobue(84, 1.2)), (1.3, shinobue(86, 0.6)), (2.0, shinobue(88, 1.6)),
                (4.0, B.shakuhachi(72, 1.5)), (5.6, B.shakuhachi(74, 1.5))], 7.5)
    put('sho', [(0, sho([60, 64, 67, 72, 74], 4.0))], 4.5)
    print('試聴用 →', d)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--demo', action='store_true')
    ap.add_argument('--stems', action='store_true')
    a = ap.parse_args()
    if a.demo:
        demo()
        return
    timeline, total = S.build()
    inst = render(timeline, total, os.path.join(S.OUT, 'stems_jp') if a.stems else None)
    m = B.master(inst)
    tp = B.encode_safe(m, 'creativevision10_inst_jp')
    print(f'jp 伴奏 {len(m) / SR:.1f}秒  {B.lufs(m):.1f} LUFS  AAC後 TP {tp:.1f} dBTP')


if __name__ == '__main__':
    main()
