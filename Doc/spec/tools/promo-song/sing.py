# VOICEVOX Engine（無料・ローカル実行）の歌唱合成で、song.py の譜割りどおりに歌わせて完成版を作る。
#   python sing.py --list                 歌えるスタイルの一覧
#   python sing.py --audition             サビ1を候補の声で歌わせ、客観指標で順位付け → audition/
#   python sing.py --singer 3002          完成版 creativevision10_full.m4a と歌のみ creativevision10_vocal.m4a
# エンジンは Doc/promo/song/voicevox_engine/run.exe（VOICEVOX_ENGINE で変更可）を CPU で自動起動する。
# 使い方・クレジット表記は同じフォルダの README.md を参照。
import os, sys, json, math, time, argparse, subprocess, urllib.request, urllib.parse, urllib.error, io, wave
import numpy as np
from scipy import signal as sig
from scipy.ndimage import uniform_filter1d

import song as S
import band as B
C = S.C

URL = 'http://127.0.0.1:50021'
ENGINE = os.environ.get('VOICEVOX_ENGINE', os.path.join(S.OUT, 'voicevox_engine', 'run.exe'))
TEACHER = 6000              # 音高・音量を作る歌唱指導モデル（波音リツ sing）
FPS = 24000 / 256           # VOICEVOX のフレームレート
PAD = 0.6                   # テイク前後の休符（子音が前へ食い込むぶん）

# 候補（女性・明るめ）。--audition で順位付けする
CANDIDATES = [3002, 3008, 3014, 3016, 3009, 3010, 3054, 3070, 3043, 3046, 3074, 3024]


# ---------------------------------------------------------------- エンジン
def api(path, body=None, raw=False):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(URL + path, data, {'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=600) as f:
        b = f.read()
    return b if raw else json.loads(b)


def ensure_engine():
    try:
        api('/version')
        return None
    except (urllib.error.URLError, ConnectionError):
        pass
    if not os.path.exists(ENGINE):
        raise SystemExit(f'VOICEVOX Engine がありません: {ENGINE}（README.md の準備を参照）')
    # NVIDIA 版でも RTX 50 系は同梱 CUDA が非対応のため CPU で動かす
    p = subprocess.Popen([ENGINE, '--host', '127.0.0.1', '--port', '50021', '--cpu_num_threads', str(os.cpu_count() or 8)],
                         cwd=os.path.dirname(ENGINE), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(120):
        time.sleep(1)
        try:
            api('/version')
            return p
        except (urllib.error.URLError, ConnectionError):
            pass
    p.kill()
    raise SystemExit('VOICEVOX Engine が起動しません')


def style_names():
    """歌唱スタイルと、歌唱モデルのない話し声スタイル（TALK_STYLES に登録）"""
    out = {}
    for s in api('/speakers'):
        for st in s['styles']:
            out[st['id']] = (s['name'], st['name'], 'talk')
    for s in api('/singers'):
        for st in s['styles']:
            out[st['id']] = (s['name'], st['name'], st.get('type'))
    sing_names = {v[0] for v in out.values() if v[2] in ('frame_decode', 'sing')}
    for k, v in out.items():
        if v[2] == 'talk' and v[0] not in sing_names:
            TALK_STYLES.add(k)
    return out

# ---------------------------------------------------------------- 譜面 → テイク
KANA_VOWEL = {'a': 'ア', 'i': 'イ', 'u': 'ウ', 'e': 'エ', 'o': 'オ', 'n': 'ン'}


def lyric_of(mora, prev_vowel):
    if mora == 'ー':
        return KANA_VOWEL[prev_vowel]
    return ''.join(chr(ord(c) + 0x60) if 'ぁ' <= c <= 'ゖ' else c for c in mora)


def takes(timeline, only=None):
    """歌のあるセクションごとに1テイク。音符はテイク先頭からの絶対フレームで丸め、累積ずれを出さない"""
    out = []
    for s in timeline:
        ev = [e for e in s['events'] if e['kind'] == 'vocal']
        if not ev or (only and s['sec']['name'] not in only):
            continue
        t0 = ev[0]['t'] - PAD
        fr = lambda t: int(round((t - t0) * FPS))
        notes, spans, cur, prev = [], [], 0, 'a'
        for e in ev:
            a, b = fr(e['t']), fr(e['t'] + e['dur'])
            if a < cur:
                a = cur     # 丸めで前の音と1フレーム重なったら、前の音の終わりから始める（ずれを累積させない）
            if 0 < a - cur < 4 and notes and notes[-1]['key'] is not None:
                # 丸めで生じた数フレームの隙間は前の音へ吸収する（短すぎる休符はエンジンが落ちる）
                notes[-1]['frame_length'] += a - cur
            elif a > cur:
                notes.append({'key': None, 'frame_length': a - cur, 'lyric': ''})
            ly = lyric_of(e['mora'], prev)
            if e['vowel'] not in ('q',):
                prev = e['vowel']
            notes.append({'key': e['note'], 'frame_length': b - a, 'lyric': ly, 'id': str(len(spans))})
            spans.append((a, b, e))
            cur = b
        notes.append({'key': None, 'frame_length': fr(ev[-1]['t'] + ev[-1]['dur'] + PAD) - cur, 'lyric': ''})
        out.append(dict(sec=s['sec'], shift=s['shift'], t0=t0, notes=notes, spans=spans))
    return out


def harmony_take(tk):
    """サビのダイアトニック3度下ハモリ（同じリズム・歌詞）"""
    tonic = (2 + tk['shift']) % 12
    scale = [(tonic + d) % 12 for d in (0, 2, 4, 5, 7, 9, 11)]
    notes = []
    for n in tk['notes']:
        n = dict(n)
        if n['key'] is not None:
            k = n['key']
            if k % 12 in scale:
                i = scale.index(k % 12)
                j = (i - 2) % 7
                k2 = k - ((scale[i] - scale[j]) % 12)
            else:
                k2 = k - 3
            n['key'] = k2
        notes.append(n)
    return dict(tk, notes=notes)

# ---------------------------------------------------------------- 表情付け
def shape_f0(tk, q, tune=0.55, vib=0.32, detune_seed=None):
    """歌唱指導モデルの f0 を、音程を詰め（tune）、伸ばす音にビブラートを付けて仕上げる"""
    f0 = np.array(q['f0'], float)
    keys = {n['id']: n['key'] for n in tk['notes'] if n.get('id') is not None}
    for i, (a, b, e) in enumerate(tk['spans']):
        if e['vowel'] == 'q':
            continue
        target = C.midi(keys[str(i)])   # ハモリでは移した音程
        n = b - a
        lo, hi = a + int(0.07 * FPS), b - int(0.05 * FPS)
        if hi > lo:
            seg = f0[lo:hi]
            v = seg > 0
            # セント偏差を tune の割合だけ縮める（自然な揺れは残す）。歌唱指導モデルが高音で
            # 平均 25 セント以上外す音は、ほぼ正確な音程まで詰める
            med = abs(1200 * np.log2(np.median(seg[v]) / target)) if v.any() else 0
            seg[v] = target * (seg[v] / target) ** (1 - (0.9 if med > 25 else tune))
            f0[lo:hi] = seg
        dur = n / FPS
        if dur >= 0.34:
            k = np.arange(n) / FPS
            ramp = np.clip((k - 0.16) / 0.22, 0, 1) * np.clip((dur - k) / 0.06, 0, 1)
            depth = vib * (1.0 if dur > 0.6 else 0.7)
            semis = depth * ramp * np.sin(2 * np.pi * 5.7 * k + 0.3)
            seg = f0[a:b]
            seg[seg > 0] *= 2 ** (semis[seg > 0] / 12)
    if detune_seed is not None:
        # ダブル用: ゆっくり揺れる ±7 セント
        rng = np.random.default_rng(detune_seed)
        drift = sig.lfilter([0.02], [1, -0.98], rng.standard_normal(len(f0)))
        drift = drift / (np.abs(drift).max() + 1e-9) * 7
        f0[f0 > 0] *= 2 ** (drift[f0 > 0] / 1200)
    q['f0'] = f0.tolist()
    return q


def transpose(tk, semis):
    if not semis:
        return tk
    return dict(tk, notes=[dict(n, key=n['key'] + semis) if n['key'] is not None else n for n in tk['notes']])


TALK_STYLES = set()   # 歌唱モデルがなく話し声モデルで歌わせるスタイル（style_names で判定）


def talk_sing(tk, speaker, detune=0.0):
    """話し声モデルで歌わせる: 各モーラの音高を音符の音程に、子音は拍の前・母音は拍頭に置いて長さを合わせる"""
    notes = tk['notes']
    t, ev = 0.0, []                              # (開始秒, 終了秒, 音符)
    for n in notes:
        d = n['frame_length'] / FPS
        if n['key'] is not None:
            ev.append((t, t + d, n))
        t += d
    total = t
    # 伸ばす「ン」は前の母音を7割伸ばしてから「ン」にする（「ビジョーン」。高い音の長いンは話し声モデルが鳴らせない）
    split = []
    for s, e, n in ev:
        if n['lyric'] == 'ン' and e - s >= 0.25 and split and abs(split[-1][1] - s) < 1e-6:
            pv = S.VOWEL.get(S.to_hira(split[-1][2]['lyric'])[-1], 'a')
            cut = s + (e - s) * 0.7
            split += [(s, cut, dict(n, lyric=KANA_VOWEL[pv])), (cut, e, n)]
        else:
            split.append((s, e, n))
    ev = split
    phrases, cur = [], []
    for e in ev:
        if cur and abs(cur[-1][1] - e[0]) > 1e-6:
            phrases.append(cur); cur = []
        cur.append(e)
    phrases.append(cur)
    # 歌では「よう」「こう」等の「う」を「お」と伸ばして歌う（話し声モデルは「う」を弱く発音するため）
    prev_o = False
    for e in ev:
        n = e[2]
        if n['lyric'] == 'ウ' and prev_o:
            n['lyric'] = 'オ'
        prev_o = n['lyric'][-1] in 'オコソトノホモヨロヲゴゾドボポョォ'
    kana = '、'.join(p[0][2]['lyric'] + "'" + ''.join(e[2]['lyric'] for e in p[1:]) for p in phrases)
    aps = api(f'/accent_phrases?speaker={speaker}&is_kana=true&text=' + urllib.parse.quote(kana), {})
    moras = [m for ap in aps for m in ap['moras']]
    if len(moras) != len(ev):
        raise SystemExit(f'{tk["sec"]["name"]}: モーラ数 {len(moras)} ≠ 音符 {len(ev)}')
    cons = [min(m['consonant_length'] or 0.0, 0.07) if m.get('consonant') else 0.0 for m in moras]
    k = 0
    for ap in aps:
        for m in ap['moras']:
            s, e, n = ev[k]
            nxt = ev[k + 1] if k + 1 < len(ev) else None
            contiguous = nxt is not None and abs(nxt[0] - e) < 1e-6
            end = nxt[0] - cons[k + 1] if contiguous else e
            if m.get('consonant'):
                m['consonant_length'] = cons[k]
            m['vowel'] = m['vowel'].lower() if m['vowel'] not in ('cl', 'N') else m['vowel']   # 無声化させない
            m['vowel_length'] = max(0.02, end - s)
            m['pitch'] = 0.0 if m['vowel'] == 'cl' else math.log(C.midi(n['key'])) + detune
            k += 1
        if ap.get('pause_mora'):
            s_prev_end = ev[k - 1][1]
            ap['pause_mora']['vowel_length'] = max(0.02, ev[k][0] - cons[k] - s_prev_end)
    q = api(f'/audio_query?speaker={speaker}&text=' + urllib.parse.quote('あ'), {})
    q.update(accent_phrases=aps, speedScale=1.0, pitchScale=0.0, intonationScale=1.0, volumeScale=1.0,
             prePhonemeLength=ev[0][0] - cons[0], postPhonemeLength=total - ev[-1][1], outputSamplingRate=24000)
    flat = [m for ap in aps for m in ap['moras']]
    # 話し声モデルは指定音高からずれるため、合成音の音程を測って音高へ戻す（閉ループで3回）
    for it in range(4):
        wav = api(f'/synthesis?speaker={speaker}', q, raw=True)
        with wave.open(io.BytesIO(wav)) as w:
            x = np.frombuffer(w.readframes(w.getnframes()), '<i2').astype(np.float32) / 32768
        if it == 3:
            break
        errs = []
        for (s, e, n), m in zip(ev, flat):
            tg = C.midi(n['key'])
            f = None
            if m['vowel'] != 'cl':
                tr = f0_track(x[int((s + 0.03) * 24000):int((e - 0.02) * 24000)], 24000, tg)
                f = float(np.median(tr[tr > 0])) if (tr > 0).sum() >= 3 else None
            errs.append(math.log(f / tg) if f else None)
        known = [v for v in errs if v is not None]
        if not known:
            break
        fill = float(np.median(known))
        for m, v in zip(flat, errs):
            if m['vowel'] != 'cl':
                m['pitch'] -= 0.8 * float(np.clip(v if v is not None else fill, -0.3, 0.3))
    # 母音区間を目標音程へ合わせ（PSOLA）、倍音以外の息・雑音を下げる
    regions = []
    for k, ((s, e, n), m) in enumerate(zip(ev, flat)):
        if m['vowel'] == 'cl':
            continue
        nxt = ev[k + 1] if k + 1 < len(ev) else None
        end = nxt[0] - cons[k + 1] if nxt is not None and abs(nxt[0] - e) < 1e-6 else e
        regions.append((s, end, C.midi(n['key']) * math.exp(detune)))
    x = retune(x, 24000, regions)
    x = harmonic_clean(x, 24000, regions)
    # 母音ごとの音量差をならす（フレーズの中央値より 6dB 以上小さい母音を持ち上げる）
    rms = [np.sqrt(np.mean(x[int(s * 24000):int(e * 24000)] ** 2) + 1e-9) for s, e, _ in regions]
    med = float(np.median(rms))
    g = np.ones(len(x), np.float32)
    for (s, e, _), r in zip(regions, rms):
        g[int(s * 24000):int(e * 24000)] = min(2.5, max(1.0, med * 0.5 / r))
    x = x * uniform_filter1d(g, 240)
    return sig.resample_poly(x, 2, 1).astype(np.float32)


def f0_track(x, sr, target, hop=None, spread=1.26):
    """目標音程の前後（既定 ±4半音）に探索を絞った YIN。1オクターブ違いを拾わない。無声は 0"""
    hop = hop or int(sr * 0.0025)
    lo, hi = int(sr / (target * spread)), int(sr / (target / spread)) + 1
    W = 2 * hi
    out = []
    for i in range(0, len(x) - W - hi, hop):
        fr = x[i:i + W + hi]
        if np.sqrt(np.mean(fr ** 2)) < 0.004:
            out.append(0.0)
            continue
        taus = np.arange(1, hi + 1)
        d = np.array([np.sum((fr[:W] - fr[t:t + W]) ** 2) for t in taus])
        cm = d * taus / (np.cumsum(d) + 1e-12)
        seg = cm[lo - 1:hi]
        k = int(np.argmin(seg))
        if seg[k] > 0.35:
            out.append(0.0)
            continue
        j = k + lo - 1
        if 0 < j < len(cm) - 1:
            a_, b_, c_ = cm[j - 1], cm[j], cm[j + 1]
            j = j + 0.5 * (a_ - c_) / (a_ - 2 * b_ + c_ + 1e-12)
        out.append(sr / (j + 1))
    return np.array(out)


def retune(x, sr, regions, glide=0.03):
    """TD-PSOLA: 各母音区間の周期を測り、目標周期で並べ直す。区間の端は元の音へ 8ms でつなぐ"""
    y = x.copy()
    prev_f = None
    for s, e, ft in regions:
        a, b = int(s * sr), int(e * sr)
        if b - a < int(0.04 * sr):
            prev_f = ft
            continue
        seg = x[a:b]
        hop = int(sr * 0.0025)
        tr = f0_track(np.concatenate([seg, np.zeros(int(sr / ft * 4))]), sr, ft, hop)
        if (tr > 0).sum() < 3:
            prev_f = ft
            continue
        tr[tr == 0] = np.median(tr[tr > 0])
        tt = np.arange(len(tr)) * hop + hop
        pa = lambda i: sr / np.interp(i, tt, tr)                  # 解析側の周期
        L = b - a
        dur = L / sr
        # 目標 f0（前の音から glide 秒で移り、長い音は遅れてビブラート）
        def f_target(i):
            t = i / sr
            f = ft
            if prev_f and t < glide:
                f = prev_f * (ft / prev_f) ** (0.5 - 0.5 * math.cos(math.pi * t / glide))
            if dur > 0.34:
                ramp = min(1.0, max(0.0, (t - 0.16) / 0.22)) * min(1.0, max(0.0, (dur - t) / 0.06))
                f *= 2 ** (0.3 * ramp * math.sin(2 * math.pi * 5.6 * t) / 12)
            return f
        # 解析マーク（周期ごとの振幅ピーク）
        P0 = pa(0)
        m = int(np.argmax(np.abs(seg[:int(P0) + 1])))
        marks = []
        while m < L:
            marks.append(m)
            P = pa(m)
            c = int(m + P)
            w = max(1, int(P * 0.2))
            lo_, hi_ = max(0, c - w), min(L, c + w + 1)
            if lo_ >= hi_:
                break
            m = lo_ + int(np.argmax(seg[lo_:hi_] * np.sign(seg[marks[-1]] or 1)))
            if m <= marks[-1]:
                m = marks[-1] + int(P)
        marks = np.array(marks)
        out = np.zeros(L + int(sr / ft * 3))
        wsum = np.zeros_like(out)
        t_s = float(marks[0])
        while t_s < L:
            j = int(np.argmin(np.abs(marks - t_s)))
            mk = marks[j]
            P = int(pa(mk))
            lo_, hi_ = mk - P, mk + P
            if lo_ < 0 or hi_ > L:
                t_s += sr / f_target(t_s)
                continue
            w = np.hanning(2 * P)
            c = int(t_s)
            if c - P >= 0:
                out[c - P:c + P] += seg[lo_:hi_] * w
                wsum[c - P:c + P] += w
            t_s += sr / f_target(t_s)
        res = out[:L] / np.maximum(wsum[:L], 0.3)
        f8 = min(int(0.008 * sr), L // 4)
        fade = np.ones(L)
        fade[:f8] = np.linspace(0, 1, f8)
        fade[L - f8:] = np.linspace(1, 0, f8)
        y[a:b] = res * fade + seg * (1 - fade)
        prev_f = ft
    return y


def harmonic_clean(x, sr, regions, floor=0.22, top=5000):
    """母音区間で、目標 f0 の倍音の近く以外を下げる（息・かすれ・雑音の低減）"""
    n, hop = 1024, 128
    f, t, Z = sig.stft(x, sr, nperseg=n, noverlap=n - hop)
    fr_t = t
    mask = np.ones(Z.shape)
    for s, e, ft in regions:
        cols = np.where((fr_t >= s + 0.01) & (fr_t <= e - 0.01))[0]
        if not len(cols):
            continue
        h = np.round(f / ft)
        dist = np.abs(f - np.maximum(h, 1) * ft)
        m = floor + (1 - floor) * np.exp(-(dist / (0.12 * ft)) ** 2)
        m[f >= top] = 0.6
        m[f < ft * 0.7] = floor
        mask[:, cols] = m[:, None]
    _, y = sig.istft(Z * mask, sr, nperseg=n, noverlap=n - hop)
    return y[:len(x)].astype(np.float32)


def est_f0(x, sr):
    """区間の基本周波数（YIN 風、中央値）。推定できなければ None"""
    n = 1024 if sr == 24000 else 2048
    if len(x) < n + 64:
        return None
    lo, hi = int(sr / 1100), int(sr / 70)
    W = n - hi
    if W < 128:
        return None
    ests = []
    for i in range(0, len(x) - n, n // 2):
        fr = x[i:i + n]
        d = np.array([np.sum((fr[:W] - fr[tau:tau + W]) ** 2) for tau in range(1, hi)])
        cm = d * np.arange(1, hi) / (np.cumsum(d) + 1e-12)
        under = np.where(cm[lo:] < 0.15)[0]
        if len(under):
            k = under[0] + lo
            while k + 1 < len(cm) and cm[k + 1] < cm[k]:
                k += 1
            ests.append(sr / (k + 1))
    return float(np.median(ests)) if ests else None


def synth(tk, singer, **kw):
    if singer in TALK_STYLES:
        return talk_sing(tk, singer, detune=0.004 if kw.get('detune_seed') else 0.0)
    score = {'notes': tk['notes']}
    q = api(f'/sing_frame_audio_query?speaker={TEACHER}', score)
    q = shape_f0(tk, q, **kw)
    q['volume'] = api(f'/sing_frame_volume?speaker={TEACHER}', {'score': score, 'frame_audio_query': q})
    q['outputSamplingRate'] = 24000
    wav = api(f'/frame_synthesis?speaker={singer}', q, raw=True)
    with wave.open(io.BytesIO(wav)) as w:
        x = np.frombuffer(w.readframes(w.getnframes()), '<i2').astype(np.float32) / 32768
    return sig.resample_poly(x, 2, 1).astype(np.float32)   # 24k → 48k

# ---------------------------------------------------------------- ボーカル処理
def compress(x, th=0.12, ratio=3.5, att=0.004, rel=0.12):
    env = np.sqrt(sig.lfilter([1 - np.exp(-1 / (C.SR * 0.01))], [1, -np.exp(-1 / (C.SR * 0.01))], x ** 2) + 1e-12)
    g = np.where(env > th, (env / th) ** (1 / ratio - 1), 1.0)
    # アタック・リリースで平滑化
    a_, r_ = np.exp(-1 / (C.SR * att)), np.exp(-1 / (C.SR * rel))
    gs = np.empty_like(g)
    cur = 1.0
    for i in range(0, len(g), 64):
        tgt = g[i:i + 64].min()
        cur = tgt + (cur - tgt) * (a_ ** 64 if tgt < cur else r_ ** 64)
        gs[i:i + 64] = cur
    return x * gs


def vocal_chain(x):
    x = C.highpass(x, 110, 2)
    x = x - 0.3 * C.bandpass(x, 220, 480)                # こもり除去
    x = x + 0.30 * C.bandpass(x, 2500, 6000)             # 明瞭度
    x = compress(x)
    ds = C.bandpass(x, 5500, 9000)                       # 簡易ディエッサー
    env = C.lowpass(np.abs(ds), 40)
    x = x - ds * np.clip((env - 0.04) / 0.08, 0, 0.6)
    air = np.tanh(C.highpass(x, 4500) * 4) * 0.05        # 24kHz 由来で欠ける高域を倍音で補う
    return x + C.highpass(air, 9000)


def breath(dur=0.28, seed=0):
    t = C.t_axis(dur)
    nz = C.bandpass(C.noise(dur, seed), 900, 7000)
    return nz * np.sin(np.pi * t / dur) ** 2 * 0.03

# ---------------------------------------------------------------- 評価（オーディション）
def metrics(x):
    """有声区間の調波性（HNR 近似）、高域の明るさ、フレーム間のスペクトル急変（ノイズ・破綻）"""
    y = sig.resample_poly(x, 1, 2)
    n, hop = 1024, 256
    hnr, cen, flux = [], [], []
    prev = None
    win = np.hanning(n)
    for i in range(0, len(y) - n, hop):
        fr = y[i:i + n] * win
        e = np.sqrt(np.mean(fr ** 2))
        if e < 0.02:
            prev = None
            continue
        sp = np.abs(np.fft.rfft(fr))
        ac = np.fft.irfft(sp ** 2)
        lo, hi = int(24000 / 1100), int(24000 / 180)
        r = ac[lo:hi].max() / (ac[0] + 1e-9)
        hnr.append(10 * np.log10(r / max(1 - r, 1e-4)))
        f = np.fft.rfftfreq(n, 1 / 24000)
        cen.append((sp * f).sum() / (sp.sum() + 1e-9))
        if prev is not None:
            a, b = sp / (sp.sum() + 1e-9), prev / (prev.sum() + 1e-9)
            flux.append(np.abs(a - b).sum())
        prev = sp
    return dict(hnr=float(np.median(hnr)), centroid=float(np.median(cen)), glitch=float(np.percentile(flux, 98)))


def audition(timeline, ids):
    names = style_names()
    d = os.path.join(S.OUT, 'audition')
    os.makedirs(d, exist_ok=True)
    tk = takes(timeline, only=['サビ1'])[0]
    rows = []
    for sid in ids:
        x = synth(tk, sid)
        m = metrics(x)
        nm = f'{names[sid][0]}（{names[sid][1]}）'
        rows.append((sid, nm, m))
        st = np.stack([x, x], 1) * 0.9 / (np.abs(x).max() + 1e-9)
        S.C.write_wav(os.path.join(d, f'{sid}.wav'), st)
        subprocess.run([C.ffmpeg_exe(), '-y', '-loglevel', 'error', '-i', os.path.join(d, f'{sid}.wav'),
                        '-c:a', 'aac', '-b:a', '160k', os.path.join(d, f'{sid}_{names[sid][0].replace("/", "_")}.m4a')], check=True)
        os.remove(os.path.join(d, f'{sid}.wav'))
        print(f'{sid} {nm} HNR {m["hnr"]:.1f}dB 明るさ {m["centroid"]:.0f}Hz 急変 {m["glitch"]:.3f}')
    # 調波性を重視し、明るさを加点、急変を減点
    hs = np.array([r[2]['hnr'] for r in rows]); cs = np.array([r[2]['centroid'] for r in rows]); gs = np.array([r[2]['glitch'] for r in rows])
    z = lambda a: (a - a.mean()) / (a.std() + 1e-9)
    score = 1.0 * z(hs) + 0.5 * z(cs) - 0.8 * z(gs)
    order = np.argsort(-score)
    L = ['# 歌声オーディション（サビ1）', '', '`sing.py --audition` が生成。聴き比べ用の音声は同じフォルダにある。', '',
         '| 順位 | ID | 声 | 調波性 HNR(dB) | 明るさ(Hz) | 急変(98%点) | 総合 |', '|---|---|---|---|---|---|---|']
    for rank, i in enumerate(order, 1):
        sid, nm, m = rows[i]
        L.append(f'| {rank} | {sid} | {nm} | {m["hnr"]:.1f} | {m["centroid"]:.0f} | {m["glitch"]:.3f} | {score[i]:+.2f} |')
    with open(os.path.join(d, 'ranking.md'), 'w', encoding='utf-8', newline='\r\n') as f:
        f.write('\n'.join(L) + '\n')
    print('\n'.join(L))
    return rows[order[0]][0]

# ---------------------------------------------------------------- 完成版
# セクションごとの歌の音量（Aメロは抑え、サビで前へ出す）
SECTION_GAIN = {'Aメロ': 0.82, 'Bメロ': 0.92, 'サビ': 1.04, 'ラスト': 1.07, 'アウトロ': 1.07}
SHOUT = '（いこう！）'
# 歌を伴奏より何 LU 大きくするか（2026-10-22 調整。サビは +1.5、Aメロは言葉を立てて +3）
VOCAL_REL_LU = {'Aメロ': 3.0, 'Bメロ': 2.0, 'サビ': 1.5, 'ラスト': 1.5, 'アウトロ': 2.0}


def produce(timeline, total, singer, verse_singer, harmony_singer, gang, octave=0, tag=''):
    names = style_names()
    inst = B.render(timeline, total)
    n = len(inst)
    dur = n / C.SR
    lead = np.zeros(n, np.float32)
    dbl = np.zeros((n, 2), np.float32)
    har = np.zeros((n, 2), np.float32)
    gng = np.zeros((n, 2), np.float32)
    brs = np.zeros(n, np.float32)

    def place(buf, t0, x, g=1.0):
        i = int(t0 * C.SR)
        m = min(len(x), n - i)
        buf[i:i + m] += x[:m] * g

    for k, tk in enumerate(takes(timeline)):
        name = tk['sec']['name']
        print(f'  {name} …', flush=True)
        g = next((v for key, v in SECTION_GAIN.items() if name.startswith(key)), 1.0)
        voice = verse_singer if name.startswith('Aメロ') else singer
        place(lead, tk['t0'], synth(transpose(tk, 12 * octave), voice), g)
        # フレーズ頭の息継ぎ（直前に0.4秒以上の休符がある音）
        prev_end = -1.0
        for a, b, e in tk['spans']:
            if e['t'] - prev_end > 0.4:
                place(brs, e['t'] - 0.3, breath(seed=int(e['t'] * 10)), g)
            prev_end = e['t'] + e['dur']
        if name.startswith(('サビ', 'ラスト', 'アウトロ')):
            for side, seed in ((0, 11 + k), (1, 31 + k)):
                x = synth(transpose(tk, 12 * octave), singer, tune=0.45, vib=0.15, detune_seed=seed)
                d = int((0.012 + 0.008 * side) * C.SR)
                place(dbl[:, side], tk['t0'], np.concatenate([np.zeros(d, np.float32), x]), g)
            h = synth(harmony_take(tk), harmony_singer, tune=0.6, vib=0.2)
            place(har[:, 0], tk['t0'], h, 0.8 * g)
            place(har[:, 1], tk['t0'], np.concatenate([np.zeros(int(0.009 * C.SR), np.float32), h]), 0.65 * g)
            # 掛け声「いこう！」は複数の声で
            sh = takes([dict(sec=tk['sec'], shift=tk['shift'], events=[sp[2] for sp in tk['spans'] if sp[2]['disp'] == SHOUT])])
            for j, v in enumerate(gang):
                for t2 in sh:
                    x = synth(t2, v, tune=0.5, vib=0.0)
                    place(gng[:, j % 2], t2['t0'] + 0.006 * j, x, 0.8)
    lead = vocal_chain(lead)
    dbl = np.stack([vocal_chain(dbl[:, c]) for c in range(2)], 1)
    har = np.stack([vocal_chain(har[:, c]) for c in range(2)], 1)
    gng = np.stack([vocal_chain(gng[:, c]) for c in range(2)], 1)
    # 歌の音量: 歌唱区間で伴奏と同じ RMS（伴奏は下で 1.2〜4.5kHz を空ける）
    sung = np.abs(lead) > 1e-4
    rms = lambda a: np.sqrt(np.mean(a ** 2) + 1e-12)
    g = rms(B.REF[sung]) / rms(lead[sung]) * 0.78   # ギター・ベースを下げても歌とドラム等の比は保つ
    voc = np.stack([lead, lead], 1) * g + dbl * g * 0.3 + har * g * 0.34 + gng * g * 0.35 + np.stack([brs, brs], 1) * g
    # センド: プレートリバーブと付点8分ディレイ（ディレイは高域を落として奥へ）
    wet = C.reverb(voc, 1.9, wet=1.0, damp=7000, seed=5) * 0.12
    dl = int(S.E8 * 3 * C.SR)
    dly = np.zeros_like(voc)
    src = C.highpass(C.lowpass(lead, 3500), 300) * g
    for i in range(1, 4):
        o = dl * i
        if o < n:
            dly[o:, i % 2] += src[:n - o] * (0.18 * 0.5 ** (i - 1))
    voc_bus = voc + wet + dly
    # 歌がある間は伴奏の 1.2〜4.5kHz（歌の明瞭度の帯域）を約4dB、全体を約1dB下げる
    env = C.lowpass(np.abs(lead) * g, 8)
    k = np.clip(env / (np.percentile(env[sung], 60) + 1e-9), 0, 1)[:, None]
    mid = np.stack([C.bandpass(inst[:, c], 1200, 4500) for c in range(2)], 1)
    inst_d = (inst - mid * 0.37 * k) * (1 - 0.1 * k)
    # 歌と伴奏のバランス（歌唱区間の K 特性ラウドネス差。ポップスの目安は歌が伴奏と同等〜+2LU）
    secs = [(s['sec']['name'], s['t'] + 0.0, s['t'] + s['bars'] * S.BAR) for s in timeline if any(e['kind'] == 'vocal' for e in s['events'])]
    # セクションごとに歌と伴奏の比を測り、VOCAL_REL_LU に合わせる（境目は 0.3 秒でつなぐ）
    gcurve = np.ones(n, np.float32)
    for nm, a, b in secs:
        i0, i1 = int(a * C.SR), int(b * C.SR)
        d = B.lufs(voc_bus[i0:i1]) - B.lufs(inst_d[i0:i1])
        tgt = next((v for key, v in VOCAL_REL_LU.items() if nm.startswith(key)), 1.5)
        gcurve[i0:i1] = 10 ** ((tgt - d) / 20)
    # 歌のない区間（間奏等）は直前の値を引き継ぐ（残響が不自然に変わらないように）
    last = 1.0
    for s_ in timeline:
        i0, i1 = int(s_['t'] * C.SR), int((s_['t'] + s_['bars'] * S.BAR) * C.SR)
        if any(nm == s_['sec']['name'] for nm, _, _ in secs):
            last = float(gcurve[i0])
        else:
            gcurve[i0:i1] = last
    gcurve[int((timeline[-1]['t'] + timeline[-1]['bars'] * S.BAR) * C.SR):] = last
    voc_bus = voc_bus * uniform_filter1d(gcurve, int(0.3 * C.SR))[:, None]
    for nm, a, b in secs:
        i0, i1 = int(a * C.SR), int(b * C.SR)
        print(f'  歌−伴奏 {nm}: {B.lufs(voc_bus[i0:i1]) - B.lufs(inst_d[i0:i1]):+.1f} LU')
    full = B.master(inst_d + voc_bus)
    inst_m = B.master(inst)
    vo = B.master(voc_bus, target=-14.0, eq=False)
    outs = [(full, 'creativevision10_full' + tag), (vo, 'creativevision10_vocal' + tag)]
    if not tag:
        outs.append((inst_m, 'creativevision10_inst'))
    for x, nm in outs:
        tp = B.encode_safe(x, nm)
        print(f'  {nm}: AAC後 TP {tp:.1f} dBTP')
    print(f'完成版 {B.lufs(B.decode(os.path.join(S.OUT, f"creativevision10_full{tag}.m4a"))):.1f} LUFS（AAC後）、{dur:.1f}秒')
    used = []
    for v in [singer, verse_singer, harmony_singer] + list(gang):
        if names[v][0] not in used:
            used.append(names[v][0])
    credit = '、'.join(f'VOICEVOX:{u}' for u in used)
    print('クレジット表記:', credit)
    return credit


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--list', action='store_true')
    ap.add_argument('--audition', nargs='*', type=int)
    ap.add_argument('--singer', type=int, default=3002, help='メイン（四国めたん ノーマル）')
    ap.add_argument('--verse', type=int, default=3000, help='Aメロのメイン（四国めたん あまあま）')
    ap.add_argument('--harmony', type=int, default=3009, help='ハモリ（波音リツ）')
    ap.add_argument('--gang', type=int, nargs='*', default=[3002, 3009, 3046, 3016], help='掛け声の声')
    ap.add_argument('--octave', type=int, default=0, help='メインを何オクターブ移すか（男声は -1）')
    ap.add_argument('--key', type=int, default=0, help='曲全体（伴奏も）の移調。話し声モデルの東北ずん子は -7')
    ap.add_argument('--tag', default='', help='出力名の接尾辞（例 _nana）。空なら inst も書き出す')
    a = ap.parse_args()
    proc = ensure_engine()
    try:
        S.KEY += a.key
        timeline, total = S.build()
        if a.list:
            for k, v in sorted(style_names().items()):
                print(k, *v)
        elif a.audition is not None:
            audition(timeline, a.audition or CANDIDATES)
        else:
            style_names()
            produce(timeline, total, a.singer, a.verse, a.harmony, a.gang, a.octave, a.tag)
    finally:
        if proc:
            proc.terminate()


if __name__ == '__main__':
    main()
