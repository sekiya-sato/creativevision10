# 主題歌の譜割り（promo-song/song.py）から、歌詞の行・1モーラごとの時刻・母音を取り出し、口の形を求める
import os, sys, functools
import numpy as np
import engine as E

SONG_TOOL = os.path.join(E.REPO, 'Doc', 'spec', 'tools', 'promo-song')
SONG_DIR = os.path.join(E.PROMO, 'song')
LEAD = 0.2          # 音源冒頭の無音
BPM = 150
BEAT = 60 / BPM
BAR = BEAT * 4


def beat(n):
    return LEAD + BEAT * n


def bar(n):
    return LEAD + BAR * n


@functools.lru_cache(maxsize=1)
def timeline():
    sys.path.insert(0, SONG_TOOL)
    import song
    tl, total = song.build()
    secs = []
    for s in tl:
        secs.append(dict(name=s['sec']['name'], t=s['t'] + LEAD, bars=s['bars'],
                         events=[dict(e, t=e['t'] + LEAD) for e in s['events'] if e['kind'] == 'vocal']))
    return secs


def lines(section_names):
    """指定セクションの歌詞の行（表示文字列, 開始, 終了, モーラ列）"""
    out = []
    for s in timeline():
        if s['name'] not in section_names:
            continue
        cur = None
        for e in s['events']:
            if cur is None or e['disp'] != cur['text'] or e['t'] - cur['end'] > 0.6:
                if cur:
                    out.append(cur)
                cur = dict(text=e['disp'], start=e['t'], end=e['t'] + e['dur'], moras=[])
            cur['moras'].append(e)
            cur['end'] = e['t'] + e['dur']
        if cur:
            out.append(cur)
    return out


def all_moras(section_names=None):
    ev = []
    for s in timeline():
        if section_names and s['name'] not in section_names:
            continue
        ev += s['events']
    return sorted(ev, key=lambda e: e['t'])


SHAPES = {'a': (0.95, 0.85), 'i': (0.38, 1.0), 'u': (0.34, 0.12), 'e': (0.62, 0.9), 'o': (0.72, 0.28),
          'n': (0.06, 0.55), 'q': (0.0, 0.6)}


class Vocal:
    """歌の音量包絡（歌だけの音源から）と譜割りを合わせて口の開き・幅を返す"""
    def __init__(self, name='metan', remap=None):
        self.ev = all_moras()
        path = os.path.join(SONG_DIR, f'creativevision10_vocal_{name}.m4a')
        a = E.read_audio(path)
        x = np.abs(a).mean(1)
        hop = 480   # 10ms
        n = len(x) // hop
        rms = np.sqrt((x[:n * hop].reshape(n, hop) ** 2).mean(1))
        rms = rms / (np.percentile(rms[rms > 1e-4], 95) + 1e-9)
        self.env = np.clip(rms, 0, 1.2)
        self.remap = remap  # 映像時刻→音源時刻

    def level(self, ts):
        i = int(ts * 100)
        if i < 0 or i >= len(self.env):
            return 0.0
        return float(self.env[i])

    def mouth(self, t):
        """(開き, 幅)。映像時刻 t"""
        ts = self.remap(t) if self.remap else t
        lv = self.level(ts + 0.03)
        cur = None
        for e in self.ev:
            if e['t'] - 0.06 <= ts < e['t'] + e['dur'] + 0.02:
                cur = e
            if e['t'] > ts + 0.2:
                break
        if cur is None or lv < 0.06:
            return 0.0, 0.6
        op, wd = SHAPES.get(cur['vowel'], (0.6, 0.7))
        # 子音の頭は少し閉じ、母音で開く
        k = E.smooth((ts - cur['t'] + 0.06) / 0.08)
        if cur['mora'] in ('ん',):
            op, wd = SHAPES['n']
        amp = min(1.0, 0.35 + lv * 0.9)
        return op * amp * (0.45 + 0.55 * k), wd
