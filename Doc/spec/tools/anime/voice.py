# VOICEVOX で台詞を合成し、音声と1モーラごとの時刻・母音（口パク用）を返す
# エンジンは promo-song と同じ Doc/promo/song/voicevox_engine/run.exe を CPU で自動起動する。
import os, sys, json, hashlib, wave, urllib.request, urllib.parse
import numpy as np
import engine as E

sys.path.insert(0, os.path.join(E.REPO, 'Doc', 'spec', 'tools', 'promo-song'))
DIR = os.path.join(E.WORK, 'voice')
URL = 'http://127.0.0.1:50021'


def api(path, body=None, raw=False):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(URL + path, data, {'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=600) as f:
        b = f.read()
    return b if raw else json.loads(b)


_engine = None


def ensure():
    global _engine
    import sing
    _engine = sing.ensure_engine() or _engine


VOWELS = {'a': 'a', 'i': 'i', 'u': 'u', 'e': 'e', 'o': 'o', 'N': 'n', 'cl': 'q', 'pau': 'q', 'A': 'a', 'I': 'i', 'U': 'u', 'E': 'e', 'O': 'o'}


def say(text, speaker, speed=1.0, pitch=0.0, intonation=1.2, volume=1.0, pre=0.05, post=0.1, kana=None, pause=1.0):
    """台詞を合成。(音声 (N,2) 48kHz float, モーラの列 [(開始秒, 終了秒, 母音)]) を返す。結果はキャッシュする"""
    os.makedirs(DIR, exist_ok=True)
    key = hashlib.md5(json.dumps([text, speaker, speed, pitch, intonation, volume, pre, post, kana] + ([pause] if pause != 1.0 else [])).encode()).hexdigest()[:16]
    wp, jp = os.path.join(DIR, key + '.wav'), os.path.join(DIR, key + '.json')
    if not (os.path.exists(wp) and os.path.exists(jp)):
        ensure()
        if kana:
            aps = api(f'/accent_phrases?speaker={speaker}&is_kana=true&text=' + urllib.parse.quote(kana), {})
            q = api(f'/audio_query?speaker={speaker}&text=' + urllib.parse.quote('あ'), {})
            q['accent_phrases'] = aps
        else:
            q = api(f'/audio_query?speaker={speaker}&text=' + urllib.parse.quote(text), {})
        q.update(speedScale=speed, pitchScale=pitch, intonationScale=intonation, volumeScale=volume,
                 prePhonemeLength=pre, postPhonemeLength=post, outputSamplingRate=48000, outputStereo=False,
                 pauseLengthScale=pause)
        wav = api(f'/synthesis?speaker={speaker}', q, raw=True)
        with open(wp, 'wb') as f:
            f.write(wav)
        # モーラの時刻（話速で割る）
        t = pre / speed
        moras = []
        for ap in q['accent_phrases']:
            for m in ap['moras']:
                c = (m.get('consonant_length') or 0) / speed
                v = (m.get('vowel_length') or 0) / speed
                moras.append((t + c, t + c + v, VOWELS.get(m['vowel'], 'a'), m['text']))
                t += c + v
            pm = ap.get('pause_mora')
            if pm:
                t += ((pm.get('vowel_length') or 0)) * pause / speed
        with open(jp, 'w', encoding='utf8') as f:
            json.dump(moras, f, ensure_ascii=False)
    with wave.open(wp) as w:
        sr = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), '<i2').astype(np.float32) / 32768
        if w.getnchannels() == 2:
            x = x.reshape(-1, 2).mean(1)
    if sr != 48000:
        n = int(len(x) * 48000 / sr)
        x = np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x).astype(np.float32)
    with open(jp, encoding='utf8') as f:
        moras = json.load(f)
    return np.stack([x, x], 1), moras


def speakers():
    ensure()
    out = []
    for s in api('/speakers'):
        for st in s['styles']:
            out.append((st['id'], s['name'], st['name']))
    return out


if __name__ == '__main__':
    for sid, n, st in speakers():
        print(sid, n, st)
