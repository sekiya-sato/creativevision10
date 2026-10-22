# 実録サンプル（CC0）の読み込みと発音。ファイル名の規則から音域・強さ・ラウンドロビンを引く。
#   ギター  karoryfer.emilyguitar   notes/<音名>_<p|mp|mf|f>_rr<n>.wav（DI 録音、3半音おき）
#   ベース  karoryfer.growlybass    sustain|staccato/<音名>_<pp|p|f|ff>_rr<n>.wav（DI 録音。表記より1オクターブ低い実音）
#   ドラム  karoryfer.big-rusty-drums  Samples/<太鼓>/<奏法>/<マイク>/<名前>_vl<強さ>_rr<n>.flac（近接・トップ等とオーバーヘッド）
# 置き場所は Doc/promo/song/samples（SAMPLES_DIR で変更可）。FLAC は ffmpeg で一度だけ展開して _cache に保存する。
import os, re, glob, subprocess
from fractions import Fraction
from functools import lru_cache
import numpy as np
from scipy import signal as sig
from scipy.io import wavfile
import warnings

import song as S
C = S.C
SR = C.SR
ROOT = os.environ.get('SAMPLES_DIR', os.path.join(S.OUT, 'samples'))
CACHE = os.path.join(ROOT, '_cache')
NOTE = {'c': 0, 'db': 1, 'd': 2, 'eb': 3, 'e': 4, 'f': 5, 'gb': 6, 'g': 7, 'ab': 8, 'a': 9, 'bb': 10, 'b': 11}


def note_midi(name):
    m = re.fullmatch(r'([a-g]b?)(\d)', name)
    return 12 * (int(m.group(2)) + 1) + NOTE[m.group(1)]


@lru_cache(maxsize=None)
def load(path):
    """48kHz・float32 で返す（モノラルは (n,)、ステレオは (n,2)）"""
    if path.endswith('.flac'):
        os.makedirs(CACHE, exist_ok=True)
        key = os.path.join(CACHE, re.sub(r'[\\/:]', '_', os.path.relpath(path, ROOT)) + '.npy')
        if os.path.exists(key):
            return np.load(key)
        r = subprocess.run([C.ffmpeg_exe(), '-loglevel', 'error', '-i', path, '-f', 'f32le', '-ar', str(SR), '-'],
                           capture_output=True, check=True)
        ch = 2 if b'stereo' in subprocess.run([C.ffmpeg_exe(), '-i', path], capture_output=True).stderr else 1
        x = np.frombuffer(r.stdout, '<f4').reshape(-1, ch) if ch == 2 else np.frombuffer(r.stdout, '<f4')
        np.save(key, x)
        return x
    with warnings.catch_warnings():
        warnings.simplefilter('ignore')
        sr, x = wavfile.read(path)
    x = x.astype(np.float32) / (2 ** 31 if x.dtype == np.int32 else 2 ** 15 if x.dtype == np.int16 else 1)
    if x.ndim == 2:
        x = x.mean(1)
    return sig.resample_poly(x, 160, 147).astype(np.float32) if sr == 44100 else x


def shift(x, semis):
    """半音単位のピッチ移動（再サンプル。±3半音以内で使う）"""
    if abs(semis) < 1e-6:
        return x
    fr = Fraction(2 ** (-semis / 12)).limit_denominator(200)
    return sig.resample_poly(x, fr.numerator, fr.denominator, axis=0).astype(np.float32)


class Pitched:
    """音名・強さ・ラウンドロビンで並んだ単音サンプル"""

    def __init__(self, folder, dyns, octave=0):
        self.dyns = dyns
        self.map = {}
        for p in glob.glob(os.path.join(folder, '*.wav')):
            m = re.fullmatch(r'([a-g]b?\d)_([a-z]+)_rr(\d+)\.wav', os.path.basename(p))
            if m and m.group(2) in dyns:
                self.map.setdefault((note_midi(m.group(1)) + 12 * octave, m.group(2)), []).append(p)
        self.keys = sorted({k for k, _ in self.map})
        self.rr = {}

    def note(self, n, vel, dur, rng, release=0.08, decay=None):
        k = min(self.keys, key=lambda k: (abs(k - n), k < n))
        dyn = self.dyns[min(len(self.dyns) - 1, int(vel * len(self.dyns)))]
        files = sorted(self.map[(k, dyn)])
        i = self.rr.get((k, dyn), int(rng.integers(len(files)))) % len(files)
        self.rr[(k, dyn)] = i + 1
        x = shift(load(files[i]), n - k)
        L = min(len(x), int((dur + release) * SR))
        x = x[:L].copy()
        if decay:                                    # ブリッジミュート等の速い減衰
            x *= np.exp(-np.arange(L) / SR / decay)
        r = min(L, int(release * SR))
        x[L - r:] *= np.linspace(1, 0, r) ** 2
        return x * (0.55 + 0.45 * vel)


GTR = os.path.join(ROOT, 'karoryfer.emilyguitar-master')
BASS = os.path.join(ROOT, 'karoryfer.growlybass-master')
DRUM = os.path.join(ROOT, 'karoryfer.big-rusty-drums-main', 'Samples')


def guitar():
    return Pitched(os.path.join(GTR, 'notes'), ['p', 'mp', 'mf', 'f'])


def bass(staccato=False):
    if staccato:   # スタッカートは強さ1段階（5ラウンドロビン）
        return Pitched(os.path.join(BASS, 'staccato'), ['staccato'], octave=-1)
    return Pitched(os.path.join(BASS, 'sustain'), ['pp', 'p', 'f', 'ff'], octave=-1)


class Kit:
    """ドラム: 太鼓ごとに近接マイク（モノ）とオーバーヘッド（ステレオ）を混ぜて返す"""
    # 名前: (フォルダ, 近接マイク, 近接の量, オーバーヘッドの量, 近接の定位)
    PIECES = {
        'kick': ('kick_24/kick', 'kick', 1.0, 0.35, 0.0),
        'snare': ('snare_14/center', 'top', 1.0, 0.45, 0.05),
        'rim': ('snare_14/rc', 'top', 0.9, 0.45, 0.05),
        'hat': ('hihat_14/tc', 'cl', 0.7, 0.5, 0.35),
        'hatl': ('hihat_14/lc', 'cl', 0.7, 0.5, 0.35),
        'ohat': ('hihat_14/ho', 'cl', 0.6, 0.55, 0.35),
        'open': ('hihat_14/open', 'cl', 0.55, 0.6, 0.35),
        'tomh': ('tom_14/center', 'cl', 1.0, 0.5, 0.25),
        'tomm': ('tom_18/center', 'cl', 1.0, 0.5, -0.2),
        'toml': ('tom_22/center', 'cl', 1.0, 0.5, -0.45),
        'crash': ('crash_17/cr', 'cl', 0.6, 0.8, -0.5),
        'crash2': ('china_18/cn', 'cl', 0.5, 0.7, 0.55),
        'ride': ('ride_22/rd', 'cl', 0.6, 0.6, 0.45),
        'bell': ('ride_22/bl', 'cl', 0.6, 0.6, 0.45),
        'taiko': ('kick_24_nodamp/kick', 'kick', 0.8, 0.7, 0.1),   # 和太鼓の素材（ミュートなしのバスドラム）
    }

    def __init__(self):
        self.files = {}
        for k, (d, close, *_ ) in self.PIECES.items():
            c = sorted(glob.glob(os.path.join(DRUM, d, close, '*.flac')))
            o = sorted(glob.glob(os.path.join(DRUM, d, 'oh', '*.flac')))
            self.files[k] = (self._index(c), self._index(o))
        self.rr = {}

    @staticmethod
    def _index(files):
        out = {}
        for p in files:
            m = re.search(r'_vl(\d+)_rr(\d+)\.flac$', p)
            out.setdefault(int(m.group(1)), []).append(p)
        return out

    def hit(self, piece, vel, rng, choke=None):
        close, oh = self.files[piece]
        _, _, gc, go, pn = self.PIECES[piece]
        layers = sorted(close)
        vl = layers[min(len(layers) - 1, int(vel * len(layers)))]
        i = self.rr.get((piece, vl), int(rng.integers(len(close[vl])))) % len(close[vl])
        self.rr[(piece, vl)] = i + 1
        c = load(close[vl][i])
        o = load(oh[vl][i]) if vl in oh and i < len(oh[vl]) else np.zeros((len(c), 2), np.float32)
        n = max(len(c), len(o))
        out = np.zeros((n, 2), np.float32)
        l, r = np.cos((pn + 1) * np.pi / 4) * 1.414, np.sin((pn + 1) * np.pi / 4) * 1.414
        out[:len(c), 0] += c * gc * l
        out[:len(c), 1] += c * gc * r
        out[:len(o)] += (o if o.ndim == 2 else np.stack([o, o], 1)) * go
        if choke:
            k = int(choke * SR)
            if k < n:
                f = min(n - k, int(0.03 * SR))
                out[k:k + f] *= np.linspace(1, 0, f)[:, None]
                out[k + f:] = 0
        return out
