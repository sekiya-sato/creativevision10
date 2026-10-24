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
    # 母音区間を作り直す: 音程は歌唱指導モデル（他の声と同じ歌い方）の f0 カーブ、声色はずん子の話し声から取り、
    # 倍音を合成し直す（話し声モデルのかすれ・息の雑音を残さない）
    regions = []
    for k, ((s, e, n), m) in enumerate(zip(ev, flat)):
        if m['vowel'] == 'cl':
            continue
        nxt = ev[k + 1] if k + 1 < len(ev) else None
        end = nxt[0] - cons[k + 1] if nxt is not None and abs(nxt[0] - e) < 1e-6 else e
        regions.append((s, end, C.midi(n['key'])))
    score = {'notes': tk['notes']}
    tq = shape_f0(tk, api(f'/sing_frame_audio_query?speaker={TEACHER}', score))
    f0_ref = np.array(tq['f0']) * math.exp(detune)
    x = vocode(x, 24000, regions, f0_ref)
    # 歌詞のない区間（フレーズ間の休符）は無音にする（話し声モデルの息・雑音を残さない）
    keep = np.zeros(len(x), np.float32)
    for k, (s, e, n) in enumerate(ev):
        keep[max(0, int((s - cons[k] - 0.015) * 24000)):int((e + 0.03) * 24000)] = 1
    x = x * uniform_filter1d(keep, 240)
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


def vocode(x, sr, regions, f0_ref, top=9000, air=7000):
    """母音区間を倍音の和で作り直す。各フレームで元の声の倍音振幅から声色（包絡）を測り、
    手本の f0（93.75fps のフレーム列）の倍音へ当てはめる。区間の端は元の音へ 10ms でつなぐ"""
    n, hop = 1024, 64
    _, ts, Z = sig.stft(x, sr, nperseg=n, noverlap=n - hop)
    mag = np.abs(Z)
    binhz = sr / n
    y = x.copy()
    rng = np.random.default_rng(3)
    for s, e, ft in regions:
        a, b = int(s * sr), int(e * sr)
        if b - a < int(0.03 * sr):
            continue
        cols = np.where((ts >= s) & (ts <= e))[0]
        if len(cols) < 2:
            continue
        # 元の声の f0（包絡を測るため。目標の前後に限定）
        own = f0_track(np.concatenate([x[a:b], np.zeros(int(sr / ft * 4))]), sr, ft, hop)
        own_t = s + (np.arange(len(own)) * hop + hop) / sr
        valid = own > 0
        f_own = np.interp(ts[cols], own_t[valid], own[valid]) if valid.sum() >= 2 else np.full(len(cols), ft)
        # 手本の f0（無声フレームは音符の音程で埋める）
        fr_t = np.arange(len(f0_ref)) / FPS
        f_ref_c = np.interp(ts[cols], fr_t, np.where(f0_ref > 0, f0_ref, np.nan))
        f_ref_c = np.where(np.isfinite(f_ref_c), f_ref_c, ft)
        K = int(top / (f_ref_c.min() * 0.95))
        # フレームごとの包絡（倍音振幅の対数を周波数で補間）
        amps = np.zeros((K, len(cols)))
        for j, c in enumerate(cols):
            kk = np.arange(1, int(top / f_own[j]) + 1)
            hz_ = kk * f_own[j]
            A = 2 * np.interp(hz_ / binhz, np.arange(mag.shape[0]), mag[:, c])
            env = np.log(np.maximum(A, 1e-6))
            hk = np.arange(1, K + 1) * f_ref_c[j]
            amps[:, j] = np.where(hk < top, np.exp(np.interp(hk, hz_, env)), 0)
        amps = uniform_filter1d(amps, 3, axis=1)                       # 時間方向に軽く平滑（揺れ・ちらつき防止）
        tt = np.arange(a, b) / sr
        f_s = np.interp(tt, fr_t, np.where(f0_ref > 0, f0_ref, ft))
        harm = np.zeros(b - a)
        ph = 2 * np.pi * np.cumsum(f_s) / sr
        for h in range(K):
            ah = np.interp(tt, ts[cols], amps[h])
            harm += ah * np.sin((h + 1) * ph + rng.uniform(0, 2 * np.pi))
        harm += C.highpass(x[a:b].astype(np.float64), air, 2) * 0.35        # 息の高域だけ少し残す
        L = b - a
        f10 = min(int(0.01 * sr), L // 4)
        fade = np.ones(L)
        fade[:f10] = np.linspace(0, 1, f10)
        fade[L - f10:] = np.linspace(1, 0, f10)
        y[a:b] = harm * fade + x[a:b] * (1 - fade)
    return y.astype(np.float32)


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


def pitch_noise(tk, x):
    """0.3秒以上の音で、目標音程からのずれ（セントの絶対値の中央値）と、倍音と倍音間のエネルギー比（dB）"""
    f, t, Z = sig.stft(x, C.SR, nperseg=4096, noverlap=4096 - 480)
    P = np.abs(Z) ** 2
    keys = {n['id']: n['key'] for n in tk['notes'] if n.get('id')}
    errs, hs, ns = [], 0.0, 0.0
    for i, (a, b, e) in enumerate(tk['spans']):
        if (b - a) / FPS < 0.3 or e['vowel'] == 'q':
            continue
        f0 = C.midi(keys[str(i)])
        tr = f0_track(x[int((a / FPS + 0.08) * C.SR):int((b / FPS - 0.04) * C.SR):2], C.SR // 2, f0)
        if (tr > 0).sum() >= 3:
            errs.append(abs(1200 * np.log2(np.median(tr[tr > 0]) / f0)))
        cols = np.where((t > a / FPS + 0.08) & (t < b / FPS - 0.05))[0]
        r = (f % f0) / f0
        band = (f > f0 * 0.7) & (f < 5000)
        harm, mid = band & ((r < 0.12) | (r > 0.88)), band & (r > 0.35) & (r < 0.65)
        hs += P[np.ix_(harm, cols)].sum() / harm.sum()
        ns += P[np.ix_(mid, cols)].sum() / mid.sum()
    return float(np.median(errs)) if errs else float('nan'), float(10 * np.log10(hs / (ns + 1e-12)))


# 再オーディションの参加者（ID, 移調半音, 備考）。歌唱モデルのある女性ボイス全員と、版で使った話し声・男声
AUDITION = [(3002, 0, ''), (3003, 0, ''), (3008, 0, ''), (3010, 0, ''), (3009, 0, ''), (3014, 0, ''), (3016, 0, ''),
            (3020, 0, ''), (3023, 0, ''), (3027, 0, ''), (3029, 0, ''), (3043, 0, ''), (3046, 0, ''), (3047, 0, ''),
            (3054, 0, ''), (3055, 0, ''), (3058, 0, ''), (3061, 0, ''), (3068, 0, ''), (3069, 0, ''), (3074, 0, ''),
            (107, -7, '話し声モデル・5度下'), (3011, -12, '男声・1オクターブ下')]


def audition(timeline, entries):
    names = style_names()
    d = os.path.join(S.OUT, 'audition')
    os.makedirs(d, exist_ok=True)
    tk0 = takes(timeline, only=['サビ1'])[0]
    rows = []
    for sid, semis, note in entries:
        tk = transpose(tk0, semis)
        x = synth(tk, sid)
        m = metrics(x)
        m['pitch'], m['hnr2'] = pitch_noise(tk, x)
        nm = f'{names[sid][0]}（{names[sid][1]}）'
        rows.append((sid, nm, m, note))
        st = np.stack([x, x], 1) * 0.9 / (np.abs(x).max() + 1e-9)
        S.C.write_wav(os.path.join(d, f'{sid}.wav'), st)
        subprocess.run([C.ffmpeg_exe(), '-y', '-loglevel', 'error', '-i', os.path.join(d, f'{sid}.wav'),
                        '-c:a', 'aac', '-b:a', '160k', os.path.join(d, f'{sid}_{names[sid][0].replace("/", "_")}.m4a')], check=True)
        os.remove(os.path.join(d, f'{sid}.wav'))
        print(f'{sid} {nm} HNR {m["hnr"]:.1f} 倍音比 {m["hnr2"]:.1f} 音程 {m["pitch"]:.0f}c 明るさ {m["centroid"]:.0f} 急変 {m["glitch"]:.3f}', flush=True)
    col = lambda k: np.array([r[2][k] for r in rows])
    z = lambda a: (a - np.nanmean(a)) / (np.nanstd(a) + 1e-9)
    # 調波性と倍音比（声の澄み・雑音の少なさ）を重視し、明るさを加点、急変と音程のずれを減点
    score = 1.0 * z(col('hnr')) + 0.6 * z(col('hnr2')) + 0.4 * z(col('centroid')) - 0.8 * z(col('glitch')) - 0.6 * np.nan_to_num(z(col('pitch')))
    order = np.argsort(-score)
    used = {3002: 'メイン（標準版）', 3000: '', 3054: 'メイン', 3043: 'メイン', 3003: 'メイン', 107: 'メイン', 3011: 'メイン',
            3009: 'ハモリ・掛け声', 3046: '掛け声', 3016: '掛け声'}
    L = ['# 歌声オーディション（サビ1、2026-10-23）', '',
         '`sing.py --audition` が生成。聴き比べ用の音声 `<ID>_<名前>.m4a` は同じフォルダにある。', '',
         '- 対象: VOICEVOX Engine 0.25.2 で歌唱モデルのある女性ボイス全員（各ノーマルスタイル）と、版で使った東北ずん子（話し声モデル）・玄野武宏（男声）。',
         '- 調波性 HNR: 声の澄み（自己相関）。倍音比: 母音で倍音と倍音間のエネルギー比（大きいほど息・雑音が少ない）。',
         '- 音程: 0.3秒以上の音の目標からのずれ（中央値、ビブラートの揺れを含む）。明るさ: スペクトル重心。急変: フレーム間のスペクトル変化の98%点（破綻・ノイズの目安）。',
         '- 総合 = 1.0×HNR + 0.6×倍音比 + 0.4×明るさ − 0.8×急変 − 0.6×音程ずれ（各指標を参加者内で標準化）。機械的な目安で、最終判断は聴いて行う。', '',
         '| 順位 | ID | 声 | 調波性 HNR(dB) | 倍音比(dB) | 音程ずれ(セント) | 明るさ(Hz) | 急変 | 総合 | 今回の使用 | 備考 |',
         '|---|---|---|---|---|---|---|---|---|---|---|']
    for rank, i in enumerate(order, 1):
        sid, nm, m, note = rows[i]
        L.append(f'| {rank} | {sid} | {nm} | {m["hnr"]:.1f} | {m["hnr2"]:.1f} | {m["pitch"]:.0f} | {m["centroid"]:.0f} | '
                 f'{m["glitch"]:.3f} | {score[i]:+.2f} | {used.get(sid, "")} | {note} |')
    with open(os.path.join(d, 'ranking.md'), 'w', encoding='utf-8') as f:
        f.write(chr(10).join(L) + chr(10))
    print(chr(10).join(L))
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
            if e['t'] - prev_end > 0.4 and voice not in TALK_STYLES:   # 話し声モデルの版は息継ぎを入れない
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
    outs = [(full, f'{S.PREFIX}_full{tag}'), (vo, f'{S.PREFIX}_vocal{tag}')]
    if tag == '_metan' and S.LYRICS == 'v1':   # 標準の四国めたん版のときは伴奏も書き出す
        outs.append((inst_m, 'creativevision10_inst'))
    for x, nm in outs:
        tp = B.encode_safe(x, nm)
        print(f'  {nm}: AAC後 TP {tp:.1f} dBTP')
    print(f'完成版 {B.lufs(B.decode(os.path.join(S.OUT, f"{S.PREFIX}_full{tag}.m4a"))):.1f} LUFS（AAC後）、{dur:.1f}秒')
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
    ap.add_argument('--lyrics', default='v1', choices=['v1', 'v2'], help='歌詞の版（v2 は 1番・2番の主題を入れ替えた版）')
    ap.add_argument('--audition', nargs='*', type=int)
    ap.add_argument('--singer', type=int, default=3002, help='メイン（四国めたん ノーマル）')
    ap.add_argument('--verse', type=int, default=3000, help='Aメロのメイン（四国めたん あまあま）')
    ap.add_argument('--harmony', type=int, default=3009, help='ハモリ（波音リツ）')
    ap.add_argument('--gang', type=int, nargs='*', default=[3002, 3009, 3046, 3016], help='掛け声の声')
    ap.add_argument('--octave', type=int, default=0, help='メインを何オクターブ移すか（男声は -1）')
    ap.add_argument('--key', type=int, default=0, help='曲全体（伴奏も）の移調。話し声モデルの東北ずん子は -7')
    ap.add_argument('--tag', default='_metan', help='出力名の接尾辞（例 _nana）。既定の _metan のときは inst も書き出す')
    a = ap.parse_args()
    proc = ensure_engine()
    try:
        S.use(a.lyrics)
        S.KEY += a.key
        timeline, total = S.build()
        if a.list:
            for k, v in sorted(style_names().items()):
                print(k, *v)
        elif a.audition is not None:
            audition(timeline, [(i, 0, '') for i in a.audition] if a.audition else AUDITION)
        else:
            style_names()
            produce(timeline, total, a.singer, a.verse, a.harmony, a.gang, a.octave, a.tag)
    finally:
        if proc:
            proc.terminate()


if __name__ == '__main__':
    main()
