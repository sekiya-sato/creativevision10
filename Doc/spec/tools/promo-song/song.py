# プロモーションソング「Creative Vision 10！ ～明日へ飛べ～」の楽譜データ
# 楽譜（コード・メロディ・歌詞の譜割り）をこのファイルに持ち、次を Doc/promo/song へ書き出す。
#   譜割り.md                セクション別のコード進行と、歌詞1モーラごとの音名・長さ
#   creativevision10.mid     メロディ（歌詞メタイベント付き）・コード・ベース・ドラム
# 伴奏は band.py、歌とミックスは sing.py が作る。使い方は同じフォルダの README.md を参照。
import os, sys, subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'promo-video'))
import core as C  # noqa: E402

OUT = os.environ.get('PROMO_SONG_DIR', os.path.join(C.REPO, 'Doc', 'promo', 'song'))

TITLE = 'Creative Vision 10！ ～明日へ飛べ～'
BPM = 150
E8 = 60 / BPM / 2          # 8分音符の秒数
BAR = E8 * 8               # 4/4 拍子
KEY = -2                   # 楽譜は D メジャー表記で書き、全体を -2 して C メジャーで鳴らす

# ---------------------------------------------------------------- メロディ（D メジャー表記）
# 「音名オクターブ.8分音符数」、r は休符。1 音に歌詞 1 モーラを当てる。
A_L1 = 'r.1 A4.1 A4.1 B4.1 A4.1 F#4.1 A4.1 D5.1  C#5.2 B4.1 A4.1 A4.3 r.1'
A_L2 = 'r.1 B4.1 B4.1 C#5.1 B4.1 A4.1 F#4.1 A4.1  A4.2 F#4.1 E4.1 F#4.3 r.1'
A_L3 = 'r.1 G4.1 G4.1 A4.1 B4.1 D5.1 C#5.1 B4.1  A4.2 F#4.1 A4.1 D5.3 r.1'
A_L4 = 'r.1 E5.1 D5.1 C#5.1 B4.1 A4.1 B4.1 C#5.1  E5.2 D5.1 C#5.1 A4.4'

B_L1 = 'r.2 B4.1 B4.1 B4.1 A4.1 B4.2  C#5.2 D5.2 E5.4'
B_L2 = 'r.2 A4.1 A4.1 A4.1 G4.1 A4.2  B4.2 C#5.2 D5.4'
B_L3 = 'r.1 D5.1 D5.1 D5.1 C#5.1 D5.1 E5.2  F#5.2 E5.1 D5.1 E5.4'
B_L4 = 'r.1 D5.1 E5.1 F#5.1 E5.1 D5.1 C#5.1 D5.1  E5.6 r.2'

S_L1 = 'r.1 B4.1 D5.1 D5.1 E5.1 F#5.1 E5.2  F#5.1 F#5.1 A5.2 F#5.2 E5.2'
S_L2 = 'r.1 F#5.1 F#5.1 E5.1 D5.1 E5.1 F#5.2  E5.1 D5.1 C#5.2 B4.4'
S_L3 = 'r.1 D5.1 D5.1 E5.1 F#5.1 G5.1 F#5.1 E5.1  E5.2 F#5.1 E5.1 D5.1 C#5.1 D5.2'
S_L4 = 'F#5.3 E5.1 D5.4  r.6'
S_L5 = 'A4.1 B4.1  D5.2 D5.1 E5.1 F#5.2 E5.1 D5.1  E5.2 F#5.2 E5.4'
S_L6 = 'r.1 F#5.1 F#5.1 G5.1 A5.2 F#5.1 E5.1  F#5.2 D5.2 B4.4'
S_L7 = 'r.1 G5.1 G5.1 F#5.1 E5.1 D5.1 E5.1 F#5.1  G5.2 F#5.2 E5.2 D5.1 E5.1  D5.8  r.8'
S_TMPL = [S_L1, S_L2, S_L3, S_L4, S_L5, S_L6, S_L7]

OUTRO = 'r.16 ' + S_L1


def chorus(lines):
    """サビ7フレーズの歌詞（表示, 読み）をテンプレートに当てる"""
    return [(n, d, y) for n, (d, y) in zip(S_TMPL, lines)]

# ---------------------------------------------------------------- 歌詞（表示, 読み）
# 読みは歌う発音で書く（助詞の「へ」は「え」、「は」は「わ」）。「ー」「っ」「ん」も1モーラ、拗音（ゃゅょ・ぁ等）は前の文字と合わせて1モーラ。
A1 = [(A_L1, 'たった30のファイル', 'たったさんじゅう のふぁいる'),
      (A_L2, '冬のはじまりだったね', 'ふゆのはじまり だったね'),
      (A_L3, 'ひとつひとつと 重ねて', 'ひとつひとつと かさねて'),
      (A_L4, '夢のかけらを 集めて', 'ゆめのかけらを あつめて')]
B1 = [(B_L1, '受け継いだバトン', 'うけついだ ばとん'),
      (B_L2, '200社の笑顔', 'にひゃくしゃの えがお'),
      (B_L3, '今はわたしがつなぐ', 'いまわわたし がつなぐ'),
      (B_L4, 'ここから始まる', 'ここからはじま る')]
S1 = chorus([('Creative Vision 10！', 'くりえいてぃぶ びじょんてん'),
             ('明日へ 今 飛び出せ', 'あしたえいま とびだせ'),
             ('重ねた日々が 翼になる', 'かさねたひびが つばさになる'),
             ('（いこう！）', 'いこう'),
             ('まだ見ぬ明日の扉', 'まだ みぬあしたの とびら'),
             ('開けよう 今すぐに', 'あけよういま すぐに'),
             ('目指すはひとつ あの未来へ', 'めざすわひとつ あのみらい え')])
A2 = [(A_L1, '風より速く 届くよ', 'かぜよりはやく とどくよ'),
      (A_L2, 'オープンな夢 広がる', 'おーぷんなゆめ ひろがる'),
      (A_L3, 'AIと手を つないで', 'えーあいとてを つないで'),
      (A_L4, '新しい朝 つくろう', 'あたらしいあさ つくろう')]
B2 = [(B_L1, '迷った日だって', 'まよったひ だって'),
      (B_L2, '何度も確かめ', 'なんどもた しかめ'),
      (B_L3, '毎日 前へ進む', 'まいにちまえ えすすむ'),
      (B_L4, '今 輝くとき', 'いまかがやくと き')]
S2 = chorus([('Creative Vision 10！', 'くりえいてぃぶ びじょんてん'),
             ('声を合わせ ひとつに', 'こえをあわせ ひとつに'),
             ('涙もぜんぶ 光になる', 'なみだもぜんぶ ひかりになる'),
             ('（いこう！）', 'いこう'),
             ('ほら 夜明けの空 晴れた', 'ほら よあけのそら はれた'),
             ('ためらわず走ろう', 'ためらわずは しろう'),
             ('描いた夢が 未来になる', 'えがいたゆめが みらいにな る')])
S3 = chorus([('Creative Vision 10！', 'くりえいてぃぶ びじょんてん'),
             ('今こそ 夢を描け', 'いまこそゆめ をえがけ'),
             ('みんなの笑顔 あふれ出すよ', 'みんなのえがお あふれだすよ'),
             ('（いこう！）', 'いこう'),
             ('まだ見ぬ明日の扉', 'まだ みぬあしたの とびら'),
             ('みんなで開こうよ', 'みんなでひら こうよ'),
             ('目指すはひとつ あの未来へ', 'めざすわひとつ あのみらい え')])
OUT_V = [(OUTRO, 'Creative Vision 10！', 'くりえいてぃぶ びじょんてん')]

# 歌詞なし（リード楽器）のフレーズ
HOOK8 = [(S_L1, None, None), (S_L2, None, None), (S_L3, None, None), (S_L4 + ' r.2', None, None)]
HOOK4 = [(S_L1, None, None), (S_L2, None, None)]

CH16 = 'G A F#m Bm G A D D G A F#m Bm Em A D D'
SECTIONS = [
    dict(name='イントロ', chords='G A F#m Bm G A D D', drums='four', lead=HOOK8, crash=True),
    dict(name='Aメロ1', chords='D A Bm F#m G D Em A', drums='eight', vocal=A1, crash=True),
    dict(name='Bメロ1', chords='G A F#m Bm G A Bm A', drums='build', vocal=B1),
    dict(name='サビ1', chords=CH16, drums='four', vocal=S1, crash=True),
    dict(name='間奏', chords='G A D D', drums='four', lead=HOOK4),
    dict(name='Aメロ2', chords='D A Bm F#m G D Em A', drums='eight', vocal=A2, crash=True),
    dict(name='Bメロ2', chords='G A F#m Bm G A Bm A', drums='build', vocal=B2),
    dict(name='サビ2', chords=CH16, drums='four', vocal=S2, crash=True),
    dict(name='ブリッジ', chords='Bm G A A', drums='half'),
    dict(name='ラストサビ', chords=CH16, drums='four', vocal=S3, crash=True, shift=1),
    dict(name='アウトロ', chords='G A D D', drums='four', vocal=OUT_V, shift=1),
    dict(name='エンディング', chords='D D', drums='solo', shift=1),
]

# ---------------------------------------------------------------- 解析
NOTE_PC = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
NAMES = ['C', 'D♭', 'D', 'E♭', 'E', 'F', 'G♭', 'G', 'A♭', 'A', 'B♭', 'B']
SMALL = set('ゃゅょぁぃぅぇぉゎ')
VOWEL = {}
for v, row in zip('aiueo', ['あかさたなはまやらわがざだばぱぁゃゎ', 'いきしちにひみりぎじぢびぴぃ',
                            'うくすつぬふむゆるぐずづぶぷぅゅ', 'えけせてねへめれげぜでべぺぇ',
                            'おこそとのほもよろをごぞどぼぽぉょ']):
    for ch in row:
        VOWEL[ch] = v


def parse_pitch(s):
    pc = NOTE_PC[s[0]]
    i = 1
    while s[i] in '#b':
        pc += 1 if s[i] == '#' else -1
        i += 1
    return 12 * (int(s[i:]) + 1) + pc


def parse_notes(s):
    """[(midi or None, 8分音符数)]"""
    out = []
    for tok in s.split():
        p, d = tok.rsplit('.', 1)
        out.append((None if p == 'r' else parse_pitch(p), int(d)))
    return out


def to_hira(s):
    return ''.join(chr(ord(c) - 0x60) if 'ァ' <= c <= 'ヶ' else c for c in s)


def moras(reading):
    out = []
    for ch in to_hira(reading.replace(' ', '')):
        if ch in SMALL and out:
            out[-1] += ch
        else:
            out.append(ch)
    return out


def vowel_of(m, prev):
    last = m[-1]
    if last == 'ー':
        return prev
    if last == 'ん':
        return 'n'
    if last == 'っ':
        return 'q'
    return VOWEL[last]


def parse_chord(name):
    m = name.endswith('m')
    root = name[:-1] if m else name
    pc = NOTE_PC[root[0]] + (1 if '#' in root else 0) - (1 if root.endswith('b') and len(root) > 1 else 0)
    return pc % 12, m


def chord_name(name, shift):
    pc, m = parse_chord(name)
    return NAMES[(pc + shift) % 12] + ('m' if m else '')


def chord_tones(name, shift, lo=55):
    pc, m = parse_chord(name)
    out = []
    for iv in (0, 3 if m else 4, 7):
        n = lo + ((pc + iv + shift - lo) % 12)
        out.append(n)
    return sorted(out)


def build():
    """セクションを時刻へ展開し、検算する"""
    t = 0.0
    timeline = []
    for sec in SECTIONS:
        shift = KEY + sec.get('shift', 0)
        chords = sec['chords'].split()
        nb = len(chords)
        events = []
        for kind in ('vocal', 'lead'):
            phrases = sec.get(kind)
            if not phrases:
                continue
            pos = 0
            for notes_s, disp, read in phrases:
                notes = parse_notes(notes_s)
                sung = [n for n in notes if n[0] is not None]
                ms = moras(read) if read else [None] * len(sung)
                if len(ms) != len(sung):
                    raise SystemExit(f'{sec["name"]} 「{disp}」: モーラ {len(ms)} ≠ 音符 {len(sung)} {ms}')
                k = 0
                prev = 'a'
                for p, d in notes:
                    if p is not None:
                        m = ms[k]; k += 1
                        v = vowel_of(m, prev) if m else 'a'
                        prev = v if v != 'q' else prev
                        events.append(dict(kind=kind, t=t + pos * E8, dur=d * E8, pos=pos, len=d,
                                           note=p + shift, mora=m, vowel=v, disp=disp))
                    pos += d
            if pos != nb * 8:
                raise SystemExit(f'{sec["name"]} {kind}: 8分音符 {pos} ≠ 小節 {nb}×8')
        timeline.append(dict(sec=sec, t=t, bars=nb, chords=chords, shift=shift, events=events))
        t += nb * BAR
    return timeline, t

# ---------------------------------------------------------------- 譜割り.md
def write_sheet(timeline, total):
    sung = [e['note'] for s in timeline for e in s['events'] if e['kind'] == 'vocal']
    nm = lambda n: f'{NAMES[n % 12]}{n // 12 - 1}'
    L = [f'# {TITLE} 譜割り', '',
         '> `Doc/spec/tools/promo-song/song.py` が生成する。直接編集せず、song.py を直して再生成する。', '',
         f'- テンポ {BPM} BPM、4/4 拍子、全 {sum(s["bars"] for s in timeline)} 小節（約 {int(total // 60)}分{int(total % 60):02d}秒）',
         f'- キー C メジャー、ラストサビとアウトロは半音上げて D♭ メジャー',
         f'- 歌唱音域 {nm(min(sung))}〜{nm(max(sung))}',
         '- 譜割りの表記は「モーラ 音名×長さ」。長さは8分音符の数（1=8分、2=4分、4=2分、8=全音符）。`っ` は音価ぶん止める。', '']
    for s in timeline:
        sec = s['sec']
        m, sec_s = divmod(s['t'], 60)
        key = 'D♭' if sec.get('shift') else 'C'
        L.append(f'## {sec["name"]}（{int(m)}:{sec_s:04.1f}〜、{s["bars"]}小節、キー {key}）')
        L.append('')
        cn = [chord_name(c, s['shift']) for c in s['chords']]
        for i in range(0, len(cn), 4):
            L.append('| ' + ' | '.join(cn[i:i + 4]) + ' |')
        L.append('')
        voc = [e for e in s['events'] if e['kind'] == 'vocal']
        if not voc:
            L.append('（インスト。' + ('リード楽器がサビのメロディを演奏）' if sec.get('lead') else '歌なし）'))
            L.append('')
            continue
        L += ['| 歌詞 | 譜割り |', '|---|---|']
        cur, buf = None, []
        for e in voc + [None]:
            if e is None or e['disp'] != cur:
                if buf:
                    L.append(f'| {cur} | ' + ' '.join(buf) + ' |')
                if e is None:
                    break
                cur, buf = e['disp'], []
            buf.append(f'{e["mora"]} {nm(e["note"])}×{e["len"]}')
        L.append('')
    with open(os.path.join(OUT, '譜割り.md'), 'w', encoding='utf-8', newline='\r\n') as f:
        f.write('\n'.join(L))

# ---------------------------------------------------------------- MIDI
def write_midi(timeline, total):
    TPQ = 480
    tick = lambda sec: int(round(sec / E8 * TPQ / 2))

    def vlq(n):
        b = [n & 0x7F]
        n >>= 7
        while n:
            b.append(0x80 | (n & 0x7F)); n >>= 7
        return bytes(reversed(b))

    def track(evs):
        evs.sort(key=lambda e: (e[0], e[1]))
        data, last = b'', 0
        for tk, _, raw in evs:
            data += vlq(tk - last) + raw; last = tk
        data += b'\x00\xff\x2f\x00'
        return b'MTrk' + len(data).to_bytes(4, 'big') + data

    def meta(kind, text):
        b = text.encode('cp932')
        return b'\xff' + bytes([kind]) + vlq(len(b)) + b

    def note(evs, ch, n, t0, dur, vel=96):
        evs.append((tick(t0), 1, bytes([0x90 | ch, n, vel])))
        evs.append((tick(t0 + dur), 0, bytes([0x80 | ch, n, 0])))

    us = int(60_000_000 / BPM)
    cond = [(0, 0, meta(0x03, TITLE)), (0, 0, b'\xff\x51\x03' + us.to_bytes(3, 'big')),
            (0, 0, b'\xff\x58\x04\x04\x02\x18\x08')]
    mel = [(0, 0, meta(0x03, 'Vocal')), (0, 0, bytes([0xC0, 54]))]
    pad = [(0, 0, meta(0x03, 'Chords')), (0, 0, bytes([0xC1, 89]))]
    bas = [(0, 0, meta(0x03, 'Bass')), (0, 0, bytes([0xC2, 38]))]
    drm = [(0, 0, meta(0x03, 'Drums'))]
    for s in timeline:
        cond.append((tick(s['t']), 0, meta(0x06, s['sec']['name'])))
        for i, c in enumerate(s['chords']):
            t0 = s['t'] + i * BAR
            for n in chord_tones(c, s['shift']):
                note(pad, 1, n, t0, BAR, 70)
            r = chord_tones(c, s['shift'], lo=36)[0]
            r = 36 + (r - 36) % 12
            for k in range(8):
                note(bas, 2, r, t0 + k * E8, E8 * 0.9, 90)
            for kind, pos, vel in drum_hits(s['sec']['drums'], i, s['bars']):
                gm = {'kick': 36, 'snare': 38, 'clap': 39, 'hat': 42, 'ohat': 46, 'crash': 49}[kind]
                note(drm, 9, gm, t0 + pos * E8 / 2, E8 / 2, int(127 * min(vel, 1.0)))
        if s['sec'].get('crash'):
            note(drm, 9, 49, s['t'], E8, 110)
        for e in s['events']:
            if e['kind'] != 'vocal':
                continue
            mel.append((tick(e['t']), 1, meta(0x05, e['mora'])))
            note(mel, 0, e['note'], e['t'], e['dur'] * 0.95, 100)
    chunks = [track(x) for x in (cond, mel, pad, bas, drm)]
    head = b'MThd' + (6).to_bytes(4, 'big') + (1).to_bytes(2, 'big') + len(chunks).to_bytes(2, 'big') + TPQ.to_bytes(2, 'big')
    with open(os.path.join(OUT, 'creativevision10.mid'), 'wb') as f:
        f.write(head + b''.join(chunks))

# ---------------------------------------------------------------- ドラムパターン（16分音符位置）
def drum_hits(style, i, nb):
    """1小節ぶん [(種類, 16分位置, 強さ)]"""
    h = []
    last = i == nb - 1
    if style == 'four':
        h += [('kick', p, 1.0) for p in (0, 4, 8, 12)]
        h += [('clap', p, 0.9) for p in (4, 12)]
        h += [('ohat', p, 0.5) for p in (2, 6, 10, 14)]
        h += [('hat', p, 0.35) for p in range(0, 16, 2)]
    elif style == 'eight':
        h += [('kick', p, 1.0) for p in (0, 6, 8)]
        h += [('snare', p, 0.85) for p in (4, 12)]
        h += [('hat', p, 0.4 if p % 4 else 0.55) for p in range(0, 16, 2)]
    elif style == 'build':
        h += [('kick', p, 1.0) for p in (0, 4, 8, 12)]
        h += [('snare', p, 0.85) for p in (4, 12)]
        h += [('hat', p, 0.45) for p in range(0, 16, 2)]
    elif style == 'half':
        h += [('kick', 0, 1.0), ('snare', 8, 0.8)]
        h += [('hat', p, 0.35) for p in range(0, 16, 4)]
    elif style == 'solo':
        # ドラムソロ → 全員の一撃 → 4拍目の最後の一打
        if i == 0:
            h += [('kick', 0, 1.0), ('kick', 8, 1.0)] + [('snare', p, 0.5 + 0.03 * p) for p in range(2, 16)]
        else:
            h += [('kick', 0, 1.0), ('crash', 0, 1.0), ('kick', 12, 1.0), ('crash', 12, 1.0)]
    # Bメロ・ブリッジの最終小節はスネアロールでサビへつなぐ
    if last and style in ('build', 'half'):
        h = [x for x in h if x[0] != 'snare' or x[1] < 8]
        h += [('snare', p, 0.4 + 0.6 * (p - 8) / 7) for p in range(8, 16)]
    if last and style == 'four' and nb in (4, 8):
        h = [x for x in h if x[1] < 12] + [('snare', p, 0.6 + 0.1 * (p - 12)) for p in range(12, 16)]
    return h

def encode(stereo, name, keep_wav):
    wav = os.path.join(OUT, name + '.wav')
    C.write_wav(wav, stereo)
    subprocess.run([C.ffmpeg_exe(), '-y', '-loglevel', 'error', '-i', wav, '-c:a', 'aac', '-b:a', '192k',
                    '-metadata', f'title={TITLE}', os.path.join(OUT, name + '.m4a')], check=True)
    if not keep_wav:
        os.remove(wav)


def main():
    os.makedirs(OUT, exist_ok=True)
    timeline, total = build()
    write_sheet(timeline, total)
    write_midi(timeline, total)
    print(f'{len(timeline)} セクション、{total:.1f} 秒 → {OUT}')


if __name__ == '__main__':
    main()
