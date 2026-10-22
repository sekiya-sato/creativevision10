# 和ロック編成の伴奏。ドラム・ギター・ベースは実録サンプル（CC0、sampler.py）、和楽器は物理モデル等の合成。
#   python band.py                 伴奏だけを creativevision10_inst.m4a へ書き出す
#   python band.py --stems         パート別 WAV と帯域・ラウドネス表を stems/ へ書き出す（ミックス確認用）
import os, math, argparse
import numpy as np
from scipy import signal as sig
from scipy.ndimage import minimum_filter1d, uniform_filter1d

import song as S
import sampler as P
C = S.C
SR = C.SR
E8, BAR = S.E8, S.BAR
E16 = E8 / 2


def hz(n):
    return C.midi(n)


def onepole(fc):
    k = math.exp(-2 * math.pi * fc / SR)
    return [1 - k], [1, -k]

# ---------------------------------------------------------------- 和楽器（合成）
def ks(freq, dur, t60=1.0, a=0.5, bright=6000, pick=0.15, seed=0):
    """Karplus-Strong 撥弦。整数遅延で生成し、補間で正確な音程へ合わせる"""
    L = int(dur * SR)
    N = max(4, int(SR / freq))
    f_act = SR / (N + 1 - a)
    r = freq / f_act
    n = int(L * r) + N + 4
    rng = np.random.default_rng(seed)
    exc = sig.lfilter(*onepole(bright), rng.uniform(-1, 1, N))
    p = max(1, int(N * pick))
    exc = exc - np.roll(exc, p)
    exc /= np.abs(exc).max() + 1e-9
    g = 0.001 ** (1 / (f_act * t60))
    y = np.zeros(n)
    y[:N] = exc
    y[N] = g * a * y[0]
    i = N + 1
    while i < n:
        m = min(N, n - i)
        y[i:i + m] = g * (a * y[i - N:i - N + m] + (1 - a) * y[i - N - 1:i - N - 1 + m])
        i += m
    out = np.interp(np.arange(L) * r, np.arange(n), y)
    f = min(L, int(0.006 * SR))
    out[L - f:] *= np.linspace(1, 0, f)
    return out.astype(np.float32)


def koto(n, dur=2.2, vel=1.0, seed=0):
    s = ks(hz(n), dur, t60=1.9, a=0.82, bright=9000, pick=0.09, seed=seed)
    k = int(0.004 * SR)
    s[:k] += C.highpass(C.noise(0.004, seed + 3), 3000)[:k] * 0.25   # 爪の当たり
    return s * vel


def shamisen(n, dur=0.6, vel=1.0, seed=0):
    s = ks(hz(n), dur, t60=0.55, a=0.93, bright=12000, pick=0.05, seed=seed)
    s = s + 0.35 * np.tanh(6 * C.bandpass(s, 1800, 6000))               # さわり
    k = int(0.005 * SR)
    s[:k] += C.highpass(C.noise(0.005, seed + 5), 2000)[:k] * 0.6        # 撥の打撃
    return s * vel


def shakuhachi(n, dur, vel=1.0, seed=0, bend=True, attack=0.06):
    """尺八: 倍音の少ない管の音＋息。メリ（下から当てる）と遅れて掛かる揺り"""
    f = hz(n)
    t = C.t_axis(dur)
    semis = (-0.8 * np.exp(-t / 0.07) if bend else 0) + 0.22 * np.clip((t - 0.3) / 0.3, 0, 1) * np.sin(2 * np.pi * 5.0 * t)
    ph = 2 * np.pi * np.cumsum(f * 2 ** (semis / 12)) / SR
    tone = np.sin(ph) + 0.32 * np.sin(2 * ph) + 0.10 * np.sin(3 * ph) + 0.04 * np.sin(4 * ph)
    am = 1 + 0.12 * np.clip((t - 0.3) / 0.3, 0, 1) * np.sin(2 * np.pi * 5.0 * t + 1)
    br = C.bandpass(C.noise(dur, seed), min(f * 0.9, 8000), min(f * 3.5, 16000)) * 0.35
    mura = C.bandpass(C.noise(dur, seed + 1), 500, 5000) * np.exp(-t / 0.05) * 0.8  # ムラ息
    env = C.env_adsr(len(t), min(attack, dur / 2), 0.15, 0.85, min(0.15, dur / 3))
    return (tone * am + br) * env * vel + mura * vel * (0.5 if attack < 0.1 else 0.15)

# ---------------------------------------------------------------- ギターアンプ
# 4x12 キャビネットの典型的な周波数特性（Hz, dB）から作る FIR
_CAB_PTS = [(0, -60), (60, -24), (90, -6), (120, 0), (200, 1), (400, -3), (800, 0), (1500, 1), (2500, 4),
            (3500, 2), (4500, -2), (5500, -7), (7000, -16), (10000, -32), (SR / 2, -60)]
CAB = sig.firwin2(1023, [f / (SR / 2) for f, _ in _CAB_PTS], [10 ** (d / 20) for _, d in _CAB_PTS])


def sub_note(n, dur, vel):
    """ベースの基音を補う正弦波（DI 素材は基音が弱いため。オクターバーの代わり）"""
    t = C.t_axis(dur + 0.03)
    f = hz(n) if hz(n) < 90 else hz(n) / 2
    return (np.sin(2 * np.pi * f * t) * np.exp(-t / 0.9) * C.env_adsr(len(t), 0.004, 0.0, 1.0, 0.03) * vel).astype(np.float32)


def gate(x, th=0.004, ms=8):
    e = env_follow(x, ms)
    g = np.clip((e - th) / th, 0, 1)
    return x * uniform_filter1d(g, int(0.01 * SR))


def amp(x, gain=24.0, tight=110, mids=1.0, crunch=False):
    """DI → ノイズゲート → オーバードライブ（中域を押し出す）→ 歪み → パワー段 → キャビネット"""
    act = np.abs(x) > 1e-4
    x = x / (np.percentile(np.abs(x[act]), 99.5) + 1e-9) * 0.5 if act.any() else x
    x = gate(x)
    x = C.highpass(x, tight, 2)
    x = x + mids * C.bandpass(x, 600, 1500)
    y = np.tanh(gain * x + 0.1) - math.tanh(0.1)
    if not crunch:
        y = np.tanh(1.8 * C.highpass(y, 60, 2))
    y = sig.fftconvolve(y, CAB)[:len(y)]
    return (y / (np.abs(y).max() + 1e-9)).astype(np.float32)

# ---------------------------------------------------------------- 補助
def root_of(name, shift, lo):
    pc, _ = S.parse_chord(name)
    return lo + ((pc + shift - lo) % 12)


def penta(shift, lo, hi):
    tonic = (2 + shift) % 12
    return [n for n in range(lo, hi) if (n - tonic) % 12 in (0, 2, 4, 7, 9)]


class Buf:
    def __init__(self, n, ch=1):
        self.x = np.zeros((n, 2) if ch == 2 else n, np.float32)

    def add(self, t, s, g=1.0):
        i = int(round(t * SR))
        if i < 0:
            s, i = s[-i:], 0
        m = min(len(s), len(self.x) - i)
        if m > 0:
            if self.x.ndim == 2 and s.ndim == 1:
                s = np.stack([s, s], 1)
            self.x[i:i + m] += s[:m] * g


def pan(x, p):
    l, r = math.cos((p + 1) * math.pi / 4), math.sin((p + 1) * math.pi / 4)
    return np.stack([x * l * 1.414, x * r * 1.414], 1)


def env_follow(x, ms=10):
    b, a = onepole(1000 / (2 * math.pi * ms))
    return sig.lfilter(b, a, np.abs(x)).astype(np.float32)

# ---------------------------------------------------------------- 編曲
PLAN = {
    'イントロ': dict(kit='intro', gtr='intro', bass='intro', wa='intro'),
    'Aメロ1': dict(kit='eight', gtr='verse', bass='verse', wa='sham'),
    'Bメロ1': dict(kit='build', gtr='build', bass='build', wa='koto'),
    'サビ1': dict(kit='chorus', gtr='chorus', bass='chorus', wa='chorus'),
    '間奏': dict(kit='chorus', gtr='chorus', bass='chorus', wa='lead'),
    'Aメロ2': dict(kit='eight', gtr='verse', bass='verse', wa='sham+koto'),
    'Bメロ2': dict(kit='build', gtr='build', bass='build', wa='koto'),
    'サビ2': dict(kit='chorus', gtr='chorus', bass='chorus', wa='chorus'),
    'ブリッジ': dict(kit='bridge', gtr='bridge', bass='half', wa='bridge'),
    'ラストサビ': dict(kit='chorus', gtr='chorus', bass='chorus', wa='last'),
    'アウトロ': dict(kit='chorus', gtr='chorus', bass='chorus', wa='chorus'),
    'エンディング': dict(kit='solo', gtr='ending', bass='ending', wa='ending'),
}

# セクションごとの音量（Aメロを引き、サビで開く）
SECTION_LEVEL = {'Aメロ': 0.75, 'Bメロ': 0.88, 'ブリッジ': 0.85, 'ラストサビ': 1.05}

# サビでの各パートのラウドネス（ドラム基準の LU）。2026-10-22 の試聴でギター・ベースを下げる指摘を反映
PART_LU = {'drums': 0.0, 'bass': -4.5, 'gtr': -4.0, 'lead': -3.0, 'wa': -7.0, 'taiko': -9.0, 'pad': -15.0}

# ブリッジの尺八ソロ（D メジャー表記、song.py と同じ書式）
BRIDGE_SOLO = 'B5.4 A5.2 F#5.2  E5.6 F#5.1 A5.1  B5.4 C#6.2 B5.2  A5.6 r.2'

FILL = [('kick', 8, .9), ('snare', 8, .75), ('snare', 9, .55), ('snare', 10, .8), ('snare', 11, .6),
        ('tomh', 12, .85), ('tomh', 13, .75), ('tomm', 14, .9), ('toml', 15, 1.0)]


def kit_bar(style, i, nb):
    """1小節の [(楽器, 16分位置, 強さ)]"""
    h = []
    last = i == nb - 1
    if style == 'intro' and i < 4:
        if i == 3:                                              # バンドが入る前のスネアロール
            h += [('snare', p, 0.25 + 0.7 * (p - 8) / 7) for p in range(8, 16)]
        return h
    if style in ('chorus', 'intro'):
        h += [('kick', 0, 1.0), ('kick', 6, 0.85), ('kick', 8, 0.95)] + ([('kick', 14, 0.75)] if i % 2 else [('kick', 10, 0.7)])
        h += [('snare', 4, 0.95), ('snare', 12, 1.0)]
        h += [('ohat', p, 0.75 if p % 4 == 0 else 0.55) for p in range(0, 16, 2)]
        if i % 4 == 0:
            h += [('crash', 0, 0.85)]
    elif style == 'eight':
        h += [('kick', 0, 1.0), ('kick', 6, 0.8), ('kick', 8, 0.95), ('kick', 11, 0.6)]
        h += [('snare', 4, 0.85), ('snare', 12, 0.9), ('snare', 7, 0.18), ('snare', 15, 0.15)]   # ゴーストノート
        h += [('hat', p, 0.6 if p % 4 == 0 else 0.4) for p in range(0, 16, 2)]
        if i % 2:
            h += [('hat', 13, 0.25), ('hat', 15, 0.3)]
    elif style == 'build':
        h += [('kick', p, 0.95) for p in (0, 4, 8, 12)]
        h += [('snare', 4, 0.85), ('snare', 12, 0.9)]
        h += [('hatl', p, 0.55 if p % 4 == 0 else 0.4) for p in range(0, 16, 2)]
        if i >= nb - 2:
            h = [x for x in h if x[0] != 'hatl'] + [('open', p, 0.5) for p in range(2, 16, 4)] + [('hat', p, 0.4) for p in range(0, 16, 4)]
    elif style == 'bridge':
        h += [('kick', 0, 1.0), ('kick', 10, 0.75), ('snare', 12, 0.85)]
        h += [('toml', p, 0.45 + 0.15 * (p % 4 == 0)) for p in range(0, 16, 2)]
        h += [('bell', p, 0.5) for p in (0, 4, 8, 12)]
    if last and style in ('chorus', 'eight', 'intro') and nb >= 4:
        h = [x for x in h if x[1] < 8] + FILL
    if last and style in ('build', 'bridge'):
        h = [x for x in h if x[1] < 8 or x[0] == 'kick']
        h += [('snare', p, 0.3 + 0.7 * (p - 8) / 7) for p in range(8, 16)]
        h += [('snare', p + 0.5, 0.25 + 0.6 * (p - 8) / 7) for p in range(12, 16)]
    return h


def render(timeline, total, stems_dir=None):
    dur = total + 4.0
    n = int(dur * SR)
    rng = np.random.default_rng(7)
    kit = P.Kit()
    gtr_s = P.guitar()
    bass_s, bass_st = P.bass(), P.bass(staccato=True)
    T = {k: Buf(n, 2) for k in ('drums', 'taiko')}
    T.update({k: Buf(n) for k in ('gtrL', 'gtrR', 'lead', 'bass', 'bsub', 'koto', 'sham', 'shaku', 'pad')})
    seed = 0

    def drum(piece, t, v, choke=None):
        T['drums'].add(t, kit.hit(piece, min(1.0, max(0.02, v * rng.uniform(0.92, 1.05))), rng, choke))

    def taiko_hit(t, v, deep=0):
        x = P.shift(kit.hit('taiko', v, rng), -4 - deep)
        T['taiko'].add(t, x)

    def shime_hit(t, v):
        T['taiko'].add(t, P.shift(kit.hit('rim', v, rng, choke=0.12), 5) * 0.8)

    def strum(side, t, notes, dur, vel, down=True, mute=False, clean=False):
        order = notes if down else notes[::-1]
        for k, m in enumerate(order):
            x = gtr_s.note(m, vel, dur, rng, release=0.03 if mute else 0.06, decay=0.07 if mute else None)
            if mute:
                x = C.lowpass(x, 1800, 2)
            T[side].add(t + k * (0.006 if not clean else 0.012), x, 0.9 if mute else 1.0)

    for si, s in enumerate(timeline):
        sec = s['sec']
        name = sec['name']
        plan = PLAN[name]
        sh = s['shift']
        nb = s['bars']
        nxt = timeline[si + 1] if si + 1 < len(timeline) else None
        if name in ('Aメロ1', 'Bメロ1', 'サビ1', '間奏', 'Aメロ2', 'Bメロ2', 'サビ2', 'ブリッジ', 'ラストサビ', 'アウトロ'):
            drum('crash', s['t'], 0.9)
            drum('kick', s['t'], 1.0)
        for i, c in enumerate(s['chords']):
            t0 = s['t'] + i * BAR
            nxt_chord = s['chords'][i + 1] if i + 1 < nb else (nxt['chords'][0] if nxt else c)
            nxt_shift = sh if i + 1 < nb else (nxt['shift'] if nxt else sh)
            tones = S.chord_tones(c, sh)
            pc, minor = S.parse_chord(c)
            # ---- ドラム（実録）
            if plan['kit'] != 'solo':
                for k, p, v in kit_bar(plan['kit'], i, nb):
                    jit = rng.normal(0, 0.0015 if k == 'kick' else 0.003)
                    drum(k, t0 + p * E16 + jit, v, choke=0.25 if k == 'ohat' else None)
            # ---- 和太鼓・締太鼓
            if plan['wa'] == 'intro' and i < 4:
                # 静かに始めて4小節で盛り上げる: 1小節目は一打の余韻、2小節目から打数が増え、4小節目は連打でバンドへ
                pats = [((0, 1.0),), ((0, 0.85), (8, 0.7)), ((0, 1.0), (6, 0.65), (8, 0.9), (14, 0.6)),
                        ((0, 1.0), (4, .8), (8, .9), (10, .7), (12, 1.0), (14, .9), (15, 1.0))]
                for p, v in pats[i]:
                    taiko_hit(t0 + p * E16, v, deep=2 if i == 0 else 0)
                if i >= 2:
                    for p in range(0, 16, 2 if i == 2 else 1):
                        shime_hit(t0 + p * E16, (0.25 if p % 4 else 0.4) if i == 2 else 0.25 + 0.6 * p / 15)
                if i == 3:   # 逆再生のシンバルでバンドの入りへ吸い込む
                    rc = kit.hit('crash', 0.9, rng)[:int(1.6 * SR)][::-1].copy()
                    rc *= (np.linspace(0, 1, len(rc)) ** 2)[:, None]
                    T['drums'].add(t0 + BAR - len(rc) / SR, rc, 0.7)
            if plan['kit'] == 'chorus' and i % 2 == 0:
                taiko_hit(t0, 0.9, deep=2)
            if plan['wa'] == 'bridge':
                for p, v in ((0, 1.0), (3, .6), (6, .7), (10, .7), (12, .8), (14, .6)):
                    taiko_hit(t0 + p * E16, v, deep=0 if p else 2)
                for p in range(16):
                    shime_hit(t0 + p * E16, 0.3 + 0.4 * (i == nb - 1) * p / 15)
            # ---- ギター（左: パワーコード / 右: アルペジオ・クランチのフルコード）
            g = plan['gtr']
            r = root_of(c, sh, 40)
            power = [r, r + 7, r + 12]
            full = [r, r + 7, r + 12, r + (15 if minor else 16), r + 19]
            if g == 'verse':
                for p in range(8):
                    strum('gtrL', t0 + p * E8 + rng.normal(0, 0.003), power[:2] + [r + 12], E8 * 0.85, 0.8 if p in (0, 3, 6) else 0.55, mute=True)
                arp = [r + 12, r + 19, r + 24, r + (27 if minor else 28)]
                for p in range(8):
                    strum('gtrR', t0 + p * E8 + rng.normal(0, 0.004), [arp[[0, 1, 2, 3, 2, 1, 2, 3][p]]], E8 * 2.5, 0.5, clean=True)
            elif g == 'build':
                pat = [(0, 3, 1.0), (3, 1, 0.7), (4, 2, 0.95), (6, 2, 0.8)] if i < nb - 2 else [(p, 1, 0.9 if p % 2 == 0 else 0.7) for p in range(8)]
                for side in ('gtrL', 'gtrR'):
                    for j, (p, ln, v) in enumerate(pat):
                        strum(side, t0 + p * E8 + rng.normal(0, 0.004), power, ln * E8, v, down=(p % 2 == 0))
            elif g == 'chorus' or (g == 'intro' and i >= 4):
                acc = [1.0, 0.6, 0.8, 0.85, 0.95, 0.6, 0.8, 0.85]
                last = i == nb - 1
                for p in range(8):
                    ln = E8 * (8 - p if last and p == 7 else 1) + 0.02
                    strum('gtrL', t0 + p * E8 + rng.normal(0, 0.003), power, ln, acc[p], down=(p % 2 == 0))
                for p in range(8):
                    strum('gtrR', t0 + p * E8 + rng.normal(0, 0.004), full, E8 + 0.02, acc[p] * 0.9, down=(p % 2 == 0))
            elif g == 'bridge':
                if i < nb - 1:
                    for p, m in enumerate([r + 12, r + 19, r + 24, r + 19, r + (27 if minor else 28), r + 19, r + 24, r + 19]):
                        strum('gtrR', t0 + p * E8, [m], E8 * 3, 0.45, clean=True)
                    if i == 0:
                        strum('gtrL', t0, power, BAR * (nb - 1), 0.9)
                else:
                    for p in range(16):
                        strum('gtrL', t0 + p * E16, power, E16 * 0.9, 0.4 + 0.6 * p / 15, mute=True)
                        strum('gtrR', t0 + p * E16 + 0.004, power, E16 * 0.9, 0.4 + 0.6 * p / 15, mute=True)
            # ---- ベース（実録）
            bp = plan['bass']
            br = root_of(c, sh, 28)
            nr = root_of(nxt_chord, nxt_shift, 28)
            steps, smp = [], bass_s
            if bp == 'intro':
                steps = [(0, 8, 0.8, br)] if i < 4 else [(p, 1, .9, br + (12 if p in (2, 6) else 0)) for p in range(8)]
            elif bp == 'verse':
                steps, smp = [(p, 1, 0.8 if p % 2 else 0.95, br) for p in range(8)], bass_st
            elif bp == 'build':
                steps = [(p, 1, 0.9, br + (12 if p == 7 else 0)) for p in range(8)]
                if i == nb - 1:
                    run = [br, br + 2, br + 4, br + 5, br + 7, br + 9, br + 11, br + 12]
                    steps = [(p, 1, 0.95, run[p]) for p in range(8)]
            elif bp == 'chorus':
                oct_ = [0, 0, 12, 0, 0, 0, 12, 0]
                steps = [(p, 1, 0.95 if p % 2 == 0 else 0.8, br + oct_[p]) for p in range(8)]
            elif bp == 'half':
                steps = [(0, 4, 0.9, br), (4, 4, 0.8, br)] if i < nb - 1 else [(p, 1, 0.5 + 0.45 * p / 7, br) for p in range(8)]
            if steps and nr != br and bp in ('verse', 'chorus'):
                p, ln, v, _ = steps[-1]
                steps[-1] = (p, ln, v, nr - 1 if nr > br else nr + 2)    # 次のコードへのアプローチ
            for p, ln, v, m in steps:
                tt = t0 + p * E8 + rng.normal(0, 0.003)
                T['bass'].add(tt, smp.note(m, v, ln * E8 * 0.9, rng, release=0.04))
                T['bsub'].add(tt, sub_note(m, ln * E8 * 0.9, v))
            # ---- パッド（ごく薄く）
            if plan['kit'] in ('chorus', 'build', 'bridge') or plan['wa'] == 'intro':
                first = plan['wa'] == 'intro' and i == 0
                T['pad'].add(t0, C.pad(tones + [tones[0] + 12], BAR + 0.2, cutoff=2000, attack=1.5 if first else 0.2, release=0.3, seed=i), 0.3)
            # ---- 箏
            wa = plan['wa']
            if wa == 'intro' and 1 <= i < 4 or wa in ('koto', 'sham+koto') and (wa == 'koto' or i % 2 == 1) or wa == 'last' and i >= 8:
                arp = [tones[0] + 12, tones[1] + 12, tones[2] + 12, tones[0] + 24]
                arp = [x + 12 if x < 67 else x for x in arp]
                pat = [0, 1, 2, 3, 2, 1, 2, 3] if wa != 'last' else [0, 1, 2, 3, 1, 2, 3, 2, 0, 1, 2, 3, 1, 2, 3, 2]
                step = E8 if len(pat) == 8 else E16
                for k, idx in enumerate(pat):
                    kv = (0.8 if k % 4 else 1.0) * ((0.55 + 0.15 * i) if wa == 'intro' else 1.0)
                    T['koto'].add(t0 + k * step, koto(arp[idx], 1.6, kv, seed := seed + 1))
            if i == nb - 1 and nxt and PLAN[nxt['sec']['name']]['kit'] == 'chorus' and name != 'アウトロ' or (wa == 'intro' and i in (0, 3)):
                pn = penta(nxt_shift if i == nb - 1 else sh, 67, 100)[:14]
                for k, m in enumerate(pn):
                    T['koto'].add(t0 + BAR - (len(pn) - k) * 0.03, koto(m, 1.2, 0.5 + 0.5 * k / len(pn), seed := seed + 1))
            # ---- 三味線（裏拍の刻み）
            if wa in ('sham', 'sham+koto', 'last'):
                sr_ = root_of(c, sh, 60)
                for k, p in enumerate((1, 3, 5, 7)):
                    T['sham'].add(t0 + p * E8 + rng.normal(0, 0.003), shamisen(sr_ + (7 if k % 2 else 0), 0.4, 0.8, seed := seed + 1))
        # ---- 旋律楽器（イントロ前半＝尺八、後半＝リードギター、間奏＝三味線＋リードギター）
        for e in s['events']:
            if e['kind'] != 'lead':
                continue
            bar = int((e['t'] - s['t']) / BAR + 1e-6)
            if name == 'イントロ' and bar < 4:
                first = e is next(x for x in s['events'] if x['kind'] == 'lead')
                T['shaku'].add(e['t'], shakuhachi(e['note'], e['dur'] + 0.05, 0.8 if first else 1.0, seed := seed + 1,
                                                  attack=0.35 if first else 0.06))
                continue
            if name != 'イントロ':
                T['sham'].add(e['t'], shamisen(e['note'], max(e['dur'], 0.3), 1.2, seed := seed + 1))
            T['lead'].add(e['t'], gtr_s.note(e['note'] - 12, 1.0, e['dur'] + 0.04, rng, release=0.05))
        if name == 'ブリッジ':
            pos = 0
            for p, d in S.parse_notes(BRIDGE_SOLO):
                if p is not None:
                    T['shaku'].add(s['t'] + pos * E8, shakuhachi(p + sh, d * E8 + 0.05, 1.0, seed := seed + 1))
                pos += d
        # ---- エンディング: ドラムソロ → 全員で一撃（短く止める）→ 3拍休んで太鼓・キック・クラッシュで締める
        if name == 'エンディング':
            t0 = s['t']
            i0, i1 = int((t0 + 0.02) * SR), int((t0 + BAR) * SR)
            f = int(0.06 * SR)
            for k in ('gtrL', 'gtrR', 'lead', 'bass', 'bsub', 'koto', 'sham', 'shaku', 'pad'):
                T[k].x[i0:i0 + f] *= np.linspace(1, 0, f)
                T[k].x[i0 + f:i1] = 0
            solo = [('kick', 0, 1), ('snare', 2, .8), ('snare', 3, .6), ('tomh', 4, .9), ('tomh', 5, .8),
                    ('tomm', 6, .9), ('tomm', 7, .85), ('kick', 8, 1), ('toml', 9, 1), ('toml', 11, .9),
                    ('snare', 12, .7), ('snare', 12.5, .6), ('snare', 13, .8), ('snare', 13.5, .7),
                    ('tomh', 14, .9), ('tomm', 14.5, .9), ('toml', 15, 1)]
            for k, p, v in solo:
                drum(k, t0 + p * E16, v)
            for p, v in ((0, 1), (8, .9), (10, .8), (15, 1)):
                taiko_hit(t0 + p * E16, v)
            for p in range(16):
                shime_hit(t0 + p * E16, 0.3 + 0.5 * p / 15)
            hit = t0 + BAR
            c = s['chords'][-1]
            r = root_of(c, sh, 40)
            for side in ('gtrL', 'gtrR'):
                strum(side, hit, [r, r + 7, r + 12], 0.42, 1.0)
            T['bass'].add(hit, bass_s.note(root_of(c, sh, 28), 1.0, 0.42, rng, release=0.05))
            for k in ('kick', 'crash', 'snare'):
                drum(k, hit, 1.0, choke=0.45 if k == 'crash' else None)
            taiko_hit(hit, 1.0)
            fin = hit + 3 * E8 * 2
            drum('toml', fin - E8, 0.8)
            drum('toml', fin - E16, 0.9)
            for k in ('kick', 'crash', 'crash2'):
                drum(k, fin, 1.0)
            taiko_hit(fin, 1.0, deep=3)
            taiko_hit(fin + 0.012, 0.9)
    lv = np.ones(n, np.float32)
    for s in timeline:
        g = next((v for key, v in SECTION_LEVEL.items() if s['sec']['name'].startswith(key)), 1.0)
        lv[int(s['t'] * SR):int((s['t'] + s['bars'] * BAR) * SR)] = g
    # イントロ前半4小節は約半分の音量から上げていく（バンドが入る5小節目で 1.0）
    ti = timeline[0]['t']
    k0, k1 = int(ti * SR), int((ti + 4 * BAR) * SR)
    u = np.linspace(0, 1, k1 - k0)
    lv[k0:k1] *= 0.5 + 0.42 * u ** 1.3
    lv = uniform_filter1d(lv, int(0.08 * SR))
    for k in T:
        T[k].x *= lv[:, None] if T[k].x.ndim == 2 else lv
    return mixdown(T, n, timeline, stems_dir)


def compress(x, th, ratio, att=0.003, rel=0.1, makeup=1.0):
    e = env_follow(x if x.ndim == 1 else np.abs(x).max(1), 5)
    g = np.where(e > th, (np.maximum(e, 1e-9) / th) ** (1 / ratio - 1), 1.0).astype(np.float32)
    a_, r_ = math.exp(-1 / (SR * att)), math.exp(-1 / (SR * rel))
    out = np.empty_like(g)
    cur = 1.0
    B = 32
    for i in range(0, len(g), B):
        tg = g[i:i + B].min()
        cur = tg + (cur - tg) * (a_ ** B if tg < cur else r_ ** B)
        out[i:i + B] = cur
    return (x * (out if x.ndim == 1 else out[:, None])) * makeup


REF = None


def mixdown(T, n, timeline, stems_dir=None):
    x = {k: v.x for k, v in T.items()}
    # ギター: 左はハイゲイン、右はクランチ（ゲインを下げ、アルペジオがつぶれないように）
    gl = amp(x['gtrL'], 22)
    gr = amp(x['gtrR'], 7, mids=0.6, crunch=True)
    lead = amp(x['lead'], 45, tight=180)
    gtr = pan(gl, -0.9) + pan(gr, 0.9)
    gtr = gtr + C.reverb(gtr, 0.8, wet=1.0, damp=5000, seed=12) * 0.05
    # ベース: DI に軽い歪みを混ぜ、コンプで均す
    b = x['bass']
    b = b / (np.abs(b).max() + 1e-9)
    bass = b + 0.25 * C.lowpass(np.tanh(4 * C.highpass(b, 200, 2)), 2500, 2)
    bass = compress(bass + 0.45 * x['bsub'] / (np.abs(x['bsub']).max() + 1e-9), 0.3, 4, att=0.005, rel=0.08)
    drums = x['drums']
    kick_env = env_follow(C.lowpass(drums.mean(1), 120, 2), 30)
    sc = 1 - 0.25 * np.clip(kick_env / (np.percentile(kick_env, 99) + 1e-9), 0, 1)
    drums = compress(drums, np.percentile(np.abs(drums), 99.5) * 0.6, 3, att=0.004, rel=0.12)
    drums = drums + C.reverb(drums, 1.1, wet=1.0, damp=7000, seed=9) * 0.08
    taiko_ = np.stack([C.highpass(x['taiko'][:, c], 35, 2) for c in range(2)], 1)
    taiko_ = taiko_ + C.reverb(taiko_, 2.4, wet=1.0, damp=5000, seed=3) * 0.25
    wa = pan(x['koto'], 0.5) * 0.55 + pan(x['sham'], -0.45) * 0.5 + pan(x['shaku'], 0.2) * 0.42
    wa = wa + C.reverb(wa, 2.6, wet=1.0, damp=8000, seed=4) * 0.18
    ld = pan(lead, 0.1)
    dl = int(E8 * 3 * SR)
    ld[dl:, 0] += ld[:-dl, 1] * 0.3
    ld[dl:, 1] += ld[:-dl, 0] * 0.3
    ld = ld + C.reverb(ld, 2.0, wet=1.0, seed=6) * 0.15
    pad = pan(C.highpass(x['pad'], 250), 0) * sc[:, None]
    parts = dict(drums=drums, bass=pan(bass, 0) * sc[:, None], gtr=gtr, lead=ld, wa=wa, taiko=taiko_, pad=pad)
    # サビ（伴奏が全員揃う区間）で各パートのラウドネスを測り、PART_LU の比に合わせる
    ch = np.zeros(n, bool)
    for s in timeline:
        if PLAN[s['sec']['name']]['kit'] == 'chorus':
            ch[int(s['t'] * SR):int((s['t'] + s['bars'] * BAR) * SR)] = True
    ref = lufs(parts['drums'][ch])
    lead_mask = np.abs(parts['lead']).max(1) > 1e-3
    gains = {}
    for k, v in parts.items():
        m = lead_mask & ch if k == 'lead' else ch
        if k == 'lead' and m.sum() < SR:
            m = lead_mask
        lv_ = lufs(v[m]) if np.abs(v[m]).max() > 1e-4 else ref
        gains[k] = 10 ** ((ref + PART_LU[k] - lv_) / 20)
    global REF
    mix = sum(v * gains[k] for k, v in parts.items())
    REF = mix
    if stems_dir:
        os.makedirs(stems_dir, exist_ok=True)
        k0 = 0.9 / (np.abs(mix).max() + 1e-9)
        cs = [c for c, _ in TARGET]
        rows = ['| パート | サビ LU | ' + ' | '.join(str(c) for c in cs) + ' |', '|---' * (len(cs) + 2) + '|']
        for k, v in parts.items():
            v = v * gains[k]
            C.write_wav(os.path.join(stems_dir, f'{k}.wav'), v * k0)
            rows.append(f'| {k} | {lufs(v[ch]) - lufs(mix[ch]):+.1f} | ' + ' | '.join(f'{l:.1f}' for l in band_levels(v, cs)) + ' |')
        with open(os.path.join(stems_dir, 'levels.md'), 'w', encoding='utf-8') as f:
            f.write(chr(10).join(rows) + chr(10))
        print(chr(10).join(rows))
    return mix.astype(np.float32)

# ---------------------------------------------------------------- マスタリング
_K1 = ([1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585])
_K2 = ([1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621])


def lufs(x):
    """ITU-R BS.1770 統合ラウドネス（48kHz、ゲーティングあり）"""
    y = sig.lfilter(*_K2, sig.lfilter(*_K1, x, axis=0), axis=0)
    blk, hop = int(0.4 * SR), int(0.1 * SR)
    ms = np.array([np.mean(y[i:i + blk] ** 2, 0).sum() for i in range(0, len(y) - blk, hop)])
    l = -0.691 + 10 * np.log10(ms + 1e-12)
    g = ms[l > -70]
    rel = -0.691 + 10 * np.log10(g.mean()) - 10
    g2 = ms[(l > -70) & (l > rel)]
    return -0.691 + 10 * np.log10(g2.mean())


def true_peak(x):
    return float(np.abs(sig.resample_poly(x, 4, 1, axis=0)).max())


def limiter(x, ceil=0.75, la_ms=3.0, rel_ms=80):
    la = int(la_ms * SR / 1000)
    # サンプル間ピーク（4倍オーバーサンプル）で判定し、AAC 化後も -1 dBTP 付近に収める
    up = np.abs(sig.resample_poly(x, 4, 1, axis=0)).max(1)
    peak = up[:len(x) * 4].reshape(-1, 4).max(1)
    need = np.minimum(1.0, ceil / (peak + 1e-9))
    g = minimum_filter1d(need, size=2 * la + 1)
    g = uniform_filter1d(g, size=la)
    r_ = math.exp(-1 / (SR * rel_ms / 1000))
    out = np.empty_like(g)
    cur = 1.0
    B = 16
    for i in range(0, len(g), B):
        tg = g[i:i + B].min()
        cur = tg if tg < cur else tg + (cur - tg) * r_ ** B
        out[i:i + B] = cur
    return np.clip(x * out[:, None], -ceil, ceil)


# 市販ポップスの平均的な帯域バランス（オクターブ帯の相対 dB）。マスター EQ の目標
TARGET = [(45, -3.0), (90, 0.0), (180, -1.5), (350, -4.0), (700, -6.5), (1400, -9.0), (2800, -11.0),
          (5600, -13.5), (11000, -17.0)]


def band_levels(x, centers):
    m = x.mean(1) if x.ndim == 2 else x
    X = np.abs(np.fft.rfft(m)) ** 2
    f = np.fft.rfftfreq(len(m), 1 / SR)
    return np.array([10 * np.log10(X[(f >= c / 1.414) & (f < c * 1.414)].sum() + 1e-12) for c in centers])


def match_eq(x, limit=5.0):
    """目標の帯域バランスとの差を ±limit dB まで補正する直線位相 EQ（対数周波数で滑らかに補間）"""
    cs = np.array([c for c, _ in TARGET])
    tg = np.array([v for _, v in TARGET])
    lv = band_levels(x, cs)
    diff = (tg - tg.max()) - (lv - lv[np.argmax(tg)])
    diff -= np.median(diff)
    gain = np.clip(diff, -limit, limit)
    n = len(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    gdb = np.interp(np.log(np.maximum(f, 1)), np.log(cs), gain)
    G = 10 ** (gdb / 20)
    out = np.stack([np.fft.irfft(np.fft.rfft(x[:, c]) * G, n) for c in range(2)], 1)
    return out.astype(np.float32), dict(zip(cs.tolist(), np.round(gain, 1).tolist()))


PRE_ROLL = 0.2   # 冒頭の無音（1拍目の一撃を AAC の先頭フレームに載せない。動画と合わせるときはこのぶんずらす）


def master(x, target=-12.0, eq=True):
    x = np.concatenate([np.zeros((int(PRE_ROLL * SR), 2), np.float32), x - x.mean(0)])
    x = np.stack([C.highpass(x[:, c], 28, 2) for c in range(2)], 1)
    if eq:
        x, g = match_eq(x)
        print('  マスターEQ(dB):', g)
    x = compress(x, np.sqrt(np.mean(x ** 2)) * 2.0, 1.5, att=0.01, rel=0.2)               # グルー（軽く）
    for _ in range(3):
        x = x * 10 ** ((target - lufs(x)) / 20)
        x = limiter(x)
    fo = int(0.01 * SR)
    x[:fo] *= np.linspace(0, 1, fo)[:, None]
    return x.astype(np.float32)


def decode(path):
    import subprocess
    r = subprocess.run([C.ffmpeg_exe(), '-loglevel', 'error', '-i', path, '-f', 's16le', '-ac', '2', '-ar', str(SR), '-'],
                       capture_output=True, check=True)
    return np.frombuffer(r.stdout, '<i2').reshape(-1, 2).astype(np.float32) / 32768


def encode_safe(x, name, tp_max=-1.0):
    """AAC 化後のサンプル間ピークを測り、tp_max を超えたら下げて作り直す（閉ループ）"""
    for _ in range(4):
        S.encode(x, name, False)
        tp = 20 * np.log10(true_peak(decode(os.path.join(S.OUT, name + '.m4a'))))
        if tp <= tp_max:
            break
        x = x * 10 ** ((tp_max - tp - 0.05) / 20)
    return tp


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--stems', action='store_true')
    ap.add_argument('--name', default='creativevision10_inst')
    a = ap.parse_args()
    timeline, total = S.build()
    inst = render(timeline, total, os.path.join(S.OUT, 'stems') if a.stems else None)
    m = master(inst)
    tp = encode_safe(m, a.name)
    print(f'伴奏 {len(m) / SR:.1f}秒  {lufs(m):.1f} LUFS  AAC後 TP {tp:.1f} dBTP')


if __name__ == '__main__':
    main()
