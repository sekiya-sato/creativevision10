# ACE-Step 1.5（ComfyUI）でプロモーションソングを作り直す。
#   方式 A1（--song なし）: 既存の伴奏を元に伴奏だけを作り直し、既存の VOICEVOX の歌を重ねる（2026-10-31 試作は全て不採用）
#   方式 B （--song）    : 歌詞 v2 で歌ごと生成する。参照は既存の完成版（ritsu）か伴奏（band）、または参照なし
# 元の音源の渡し方（MODES）
#   cover      turbo AIO + ReferenceTimbreAudio（カバー。参照の旋律・構成をなぞる）
#   turbo_a2a  turbo AIO で、元の音源の潜在表現から途中だけ描き直す（denoise）
#   sft_a2a    XL SFT で同上
#   turbo_t2m / sft_t2m  参照なし（タグと歌詞・BPM・キーだけから生成）
# createdemo の「ずんだもんの令和版電波ソング」（2026-10-31）の比較で、試聴で採用されたのは turbo のカバー、
# 元曲との一致の数値が最も高かったのは XL の部分描き直し d0.6。XL のカバーは ComfyUI 0.43.2 で音にならない（ノイズ）ため使わない。
#   python ace.py prep                        元の伴奏の WAV・曲調の指定・セクション区切りの歌詞を ace/ へ書き出す
#   python ace.py workflows [--song] [--mode ..] [--ref ..] [--denoise ..] [--seeds ..]
#                                             ComfyUI の API 形式ワークフローを ace/workflows/ へ書き出す
#   python ace.py compare <生成した音源...>   元の伴奏とセクションごとに比べる（タイミング・和音・音量）→ ace/compare.md
#   python ace.py mix <音源 出力名>... [--singer _ritsu]
#                                             ずれを直して伴奏のみと、既存の歌（v2）を重ねた仮ミックスを Doc/promo/song2 へ書き出す
#   python ace.py place <音源 出力名>...      歌入りの生成音源の音量をそろえ creativevision10_v2_<出力名>.m4a として song2 へ置く
# 生成は ComfyUI で行う（comfy-mcp の upload_file → run_workflow → fetch_outputs）。手順は README.md を参照。
import os, json, argparse
import numpy as np
from scipy import signal as sig
from scipy.ndimage import uniform_filter1d

import song as S
import band as B
C = S.C
SR = C.SR

ACE = os.path.join(S.OUT, 'ace')
LEAD = 0.2                 # 全音源の冒頭の無音（秒）

# 参照する音源（元の名前, ComfyUI の input に置く WAV）。band.py の和ロック版の伴奏（v2 の完成版と同じ伴奏）、
# jp.py の和楽器版の伴奏、波音リツ版の v2 完成版
REFS = {'band': ('creativevision10_inst', 'cv10_inst_band_ref.wav'),
        'jp': ('creativevision10_inst_jp', 'cv10_inst_jp_ref.wav'),
        'ritsu': ('creativevision10_v2_full_ritsu', 'cv10_v2_full_ritsu_ref.wav')}

# 使うモデル（ComfyUI の models 配下にあるファイル名）
AIO = 'ace_step_1.5_turbo_aio.safetensors'
UNET = 'acestep_v1.5_xl_sft_bf16.safetensors'
CLIP1, CLIP2 = 'qwen_0.6b_ace15.safetensors', 'qwen_4b_ace15.safetensors'
VAE = 'ace_1.5_vae.safetensors'
MODES = ['cover', 'turbo_a2a', 'sft_a2a', 'turbo_t2m', 'sft_t2m']
SEED = 20261031

# 方式 B の曲調。歌い手はタグで指定するだけで、VOICEVOX の声にはならない
TAGS_SONG = ('Japanese anime opening theme, J-pop rock, wa-rock, energetic, uplifting, hopeful, 150 BPM, C major, '
             'bright clear young female lead vocal, clear Japanese pronunciation, catchy powerful chorus, '
             'group shout backing vocals, driving rock drums, distorted electric guitar, lead guitar, electric bass, '
             'shamisen, koto, shakuhachi, taiko drums, polished modern studio mix')
# 歌詞の表記を歌う読みへ寄せる（英字・数字・読みの揺れる漢字）
SUNG = [('Creative Vision 10', 'クリエイティブ・ビジョン・テン'), ('AI', 'エーアイ'), ('200社', 'にひゃくしゃ'),
        ('30', 'さんじゅう'), ('明日', 'あした'), ('（いこう！）', '(いこう！)')]

TAGS = ('Japanese wa-rock, anime opening theme, energetic, uplifting, bright, 150 BPM, C major, '
        'driving rock drums, distorted electric guitar power chords, clean arpeggio guitar, lead guitar, '
        'electric bass, shamisen, koto, shakuhachi, taiko drums, '
        'instrumental, no vocals, polished modern studio mix, wide stereo')

# セクション名（ACE-Step の構成タグ）
TAG_OF = {'イントロ': 'Intro', 'Aメロ1': 'Verse 1', 'Bメロ1': 'Pre-Chorus 1', 'サビ1': 'Chorus 1',
          '間奏': 'Interlude', 'Aメロ2': 'Verse 2', 'Bメロ2': 'Pre-Chorus 2', 'サビ2': 'Chorus 2',
          'ブリッジ': 'Bridge', 'ラストサビ': 'Final Chorus', 'アウトロ': 'Outro', 'エンディング': 'Ending'}


def sections():
    """[(名前, 開始秒, 終了秒)]。冒頭の無音 LEAD を含めた実時間"""
    out, t = [], LEAD
    for s in S.SECTIONS:
        d = len(s['chords'].split()) * S.BAR
        out.append((s['name'], t, t + d))
        t += d
    return out


def lyrics_inst():
    """構成タグだけの歌詞（歌なし）"""
    return '\n\n'.join('[%s]\n[Instrumental]' % TAG_OF[n] for n, _, _ in sections())


def lyrics_song():
    """歌詞 v2 に構成タグを付けたもの（song.py の各セクションの歌詞をそのまま使う）"""
    S.use('v2')
    out = []
    for s in S.SECTIONS:
        tag = TAG_OF[s['name']] + (' - key change up' if s['name'] == 'ラストサビ' else '')
        lines = []
        for e in s.get('vocal') or []:
            t = e[1]
            for a, b in SUNG:
                t = t.replace(a, b)
            lines.append(t)
        out.append('[%s]\n%s' % (tag, '\n'.join(lines)) if lines else '[%s]' % tag)
    return '\n\n'.join(out)


def ref_path(key):
    return os.path.join(ACE, 'input', REFS[key][1])


def prep():
    os.makedirs(os.path.join(ACE, 'input'), exist_ok=True)
    os.makedirs(os.path.join(ACE, 'prompts'), exist_ok=True)
    import subprocess
    for key, (name, _) in REFS.items():
        subprocess.run([C.ffmpeg_exe(), '-loglevel', 'error', '-y', '-i', os.path.join(S.OUT, name + '.m4a'),
                        '-ar', str(SR), '-ac', '2', '-c:a', 'pcm_s16le', ref_path(key)], check=True)
        print('参照', ref_path(key))
    with open(os.path.join(ACE, 'prompts', 'tags.txt'), 'w', encoding='utf-8', newline='\r\n') as f:
        f.write(TAGS + '\n')
    with open(os.path.join(ACE, 'prompts', 'lyrics_inst.txt'), 'w', encoding='utf-8', newline='\r\n') as f:
        f.write(lyrics_inst() + '\n')
    with open(os.path.join(ACE, 'prompts', 'tags_song.txt'), 'w', encoding='utf-8', newline='\r\n') as f:
        f.write(TAGS_SONG + '\n')
    with open(os.path.join(ACE, 'prompts', 'lyrics_song.txt'), 'w', encoding='utf-8', newline='\r\n') as f:
        f.write(lyrics_song() + '\n')
    with open(os.path.join(ACE, 'prompts', 'sections.md'), 'w', encoding='utf-8', newline='\r\n') as f:
        f.write('| セクション | タグ | 開始 | 終了 |\n|---|---|---|---|\n')
        for n, a, b in sections():
            f.write('| %s | %s | %.2f | %.2f |\n' % (n, TAG_OF[n], a, b))


def run_name(mode, ref, seed, denoise, song=False):
    dn = '_d%02d' % round(denoise * 100) if mode.endswith('a2a') else ''
    rf = '' if mode.endswith('t2m') else '_' + ref
    return '%s_%s%s%s_s%d' % ('b' if song else 'a1', mode, rf, dn, seed)


def workflow(mode, ref, seed, denoise, song=False):
    """API 形式のワークフロー。createdemo で動作を確かめたグラフ（ace15-ref-full / ace15-*-a2a-d60 / ace15-*-noref）と同じ形。
    長さ・seed は受け側のノードへ直接入れる（PrimitiveNode 経由だと API 変換後にテンプレート既定値が使われた）"""
    dur = round(sections()[-1][2] + 0.25, 1)
    turbo = not mode.startswith('sft')
    if turbo:
        g = {'1': {'class_type': 'CheckpointLoaderSimple', 'inputs': {'ckpt_name': AIO}}}
        model, clip, vae = ['1', 0], ['1', 1], ['1', 2]
    else:
        g = {'1': {'class_type': 'UNETLoader', 'inputs': {'unet_name': UNET, 'weight_dtype': 'default'}},
             '3': {'class_type': 'DualCLIPLoader', 'inputs': {'clip_name1': CLIP1, 'clip_name2': CLIP2,
                                                              'type': 'ace', 'device': 'default'}},
             '4': {'class_type': 'VAELoader', 'inputs': {'vae_name': VAE}}}
        model, clip, vae = ['1', 0], ['3', 0], ['4', 0]
    g.update({
        '2': {'class_type': 'ModelSamplingAuraFlow', 'inputs': {'model': model, 'shift': 3.0}},
        '5': {'class_type': 'LoadAudio', 'inputs': {'audio': os.path.basename(ref_path(ref))}},
        '6': {'class_type': 'VAEEncodeAudio', 'inputs': {'audio': ['5', 0], 'vae': vae}},
        '7': {'class_type': 'TextEncodeAceStepAudio1.5', 'inputs': {
            'clip': clip, 'tags': TAGS_SONG if song else TAGS, 'lyrics': lyrics_song() if song else lyrics_inst(),
            'seed': seed, 'bpm': S.BPM,
            'duration': dur, 'timesignature': '4', 'language': 'ja', 'keyscale': 'C major',
            'generate_audio_codes': True, 'cfg_scale': 2.0, 'temperature': 0.85, 'top_p': 0.9,
            'top_k': 0, 'min_p': 0.0}},
        '8': {'class_type': 'ConditioningZeroOut', 'inputs': {'conditioning': ['7', 0]}},
        '10': {'class_type': 'VAEDecodeAudio', 'inputs': {'samples': ['9', 0], 'vae': vae}},
        '11': {'class_type': 'SaveAudioAdvanced', 'inputs': {
            'audio': ['10', 0], 'filename_prefix': 'audio/cv10_ace/' + run_name(mode, ref, seed, denoise, song),
            'format': 'flac'}},
    })
    ks = {'model': ['2', 0], 'seed': seed, 'sampler_name': 'euler', 'scheduler': 'simple',
          'positive': ['7', 0], 'negative': ['8', 0]}
    if mode.endswith('t2m'):
        del g['5'], g['6']
        g['13'] = {'class_type': 'EmptyAceStep1.5LatentAudio', 'inputs': {'seconds': dur, 'batch_size': 1}}
        ks.update(latent_image=['13', 0], denoise=1.0, **(dict(steps=8, cfg=1.0) if turbo else dict(steps=50, cfg=7.0)))
    elif mode == 'cover':
        # 参照の潜在表現を条件に付け、空の潜在表現から全部描く
        g['12'] = {'class_type': 'ReferenceTimbreAudio', 'inputs': {'conditioning': ['7', 0], 'latent': ['6', 0]}}
        g['13'] = {'class_type': 'EmptyAceStep1.5LatentAudio', 'inputs': {'seconds': dur, 'batch_size': 1}}
        ks.update(positive=['12', 0], latent_image=['13', 0], steps=8, cfg=1.0, denoise=1.0)
    else:
        ks.update(latent_image=['6', 0], denoise=denoise,
                  **(dict(steps=8, cfg=1.0) if turbo else dict(steps=50, cfg=7.0)))
    g['9'] = {'class_type': 'KSampler', 'inputs': ks}
    return g


def workflows(modes, refs, denoises, seeds, song=False):
    d = os.path.join(ACE, 'workflows')
    os.makedirs(d, exist_ok=True)
    for mode in modes:
        for ref in (refs[:1] if mode.endswith('t2m') else refs):
            for dn in (denoises if mode.endswith('a2a') else [1.0]):
                for sd in seeds:
                    name = run_name(mode, ref, sd, dn, song) + '.json'
                    with open(os.path.join(d, name), 'w', encoding='utf-8', newline='\r\n') as f:
                        json.dump(workflow(mode, ref, sd, dn, song), f, ensure_ascii=False, indent=1)
                    print(name)


def place(path, name, target=-12.2):
    """歌入りの生成音源を、EQ 等は掛けずに音量だけそろえて song2 へ置く。
    ピークは band.master と同じリミッターで抑える（全体を下げると音量がそろわない）。AAC 後は encode_safe で -1dBTP 以下"""
    x = B.decode(path)
    for _ in range(10):
        x = B.limiter(x * 10 ** ((target - B.lufs(x)) / 20), ceil=0.75)
        if abs(target - B.lufs(x)) < 0.1:
            break
    src = S.OUT
    os.makedirs(MIX_OUT, exist_ok=True)
    S.OUT = MIX_OUT
    try:
        nm = 'creativevision10_v2_' + name
        tp = B.encode_safe(x, nm)
        print('%s.m4a  %.1f LUFS  TP %.1f dBTP  %.1f 秒' % (nm, B.lufs(B.decode(os.path.join(MIX_OUT, nm + '.m4a'))),
                                                          tp, len(x) / SR))
    finally:
        S.OUT = src


# ---------------------------------------------------------------- 比較
_PC = None


def spec(x):
    """モノラル化した振幅スペクトル（2048点、hop 10ms）"""
    m = x.mean(1) if x.ndim == 2 else x
    _, _, z = sig.stft(m, SR, nperseg=2048, noverlap=2048 - SR // 100)
    return np.abs(z)


def chroma(z):
    """12 音のクロマ（55Hz〜4kHz）"""
    global _PC
    if _PC is None:
        f = np.fft.rfftfreq(2048, 1 / SR)
        ok = (f > 55) & (f < 4000)
        pc = np.full(len(f), -1)
        pc[ok] = np.round(12 * np.log2(f[ok] / 440) + 9).astype(int) % 12
        _PC = pc
    c = np.zeros((12, z.shape[1]))
    for k in range(12):
        c[k] = z[_PC == k].sum(0)
    # 12 音の平均を引いてから正規化する（内積が相関になり、音階を共有するだけの別の和音では下がる）
    c = c - c.mean(0)
    return c / (np.linalg.norm(c, axis=0) + 1e-9)


def onset_env(z):
    """スペクトルフラックス（立ち上がりの強さ）"""
    lz = np.log1p(z * 100)
    return np.maximum(np.diff(lz, axis=1), 0).sum(0)


def lag_ms(a, b, max_ms=120):
    """b が a に対して何 ms 遅れているか（立ち上がりの相互相関）"""
    m = max_ms // 10
    a, b = a - a.mean(), b - b.mean()
    r = [np.dot(a[m:-m], np.roll(b, -k)[m:-m]) for k in range(-m, m + 1)]
    return (int(np.argmax(r)) - m) * 10


def compare(paths, ref):
    x0 = B.decode(ref_path(ref))
    z0 = spec(x0)
    c0, o0 = chroma(z0), onset_env(z0)
    rows = ['元の伴奏: `%s`' % os.path.basename(ref_path(ref)), '',
            '和音一致はクロマの相関（同じ音源で 1。同じ和音で楽器が違えば 0.7〜0.8、全音ずらすと 0.35〜0.65）、ずれは立ち上がりの相互相関（+ は生成側が遅い）。', '']
    for p in paths:
        x = B.decode(p)
        n = min(len(x), len(x0))
        z = spec(x[:n])
        c, o = chroma(z), onset_env(z)
        rows += ['## %s' % os.path.basename(p), '',
                 '長さ %.2f 秒（元 %.2f 秒）、全体 %.1f LUFS' % (len(x) / SR, len(x0) / SR, B.lufs(x)), '',
                 '| セクション | 和音一致 | ずれ ms | 音量差 LU |', '|---|---|---|---|']
        for name, a, b in sections():
            i, j = int(a * 100), min(int(b * 100), c.shape[1], c0.shape[1])
            if j - i < 50:
                continue
            sim = float(np.mean(np.sum(c[:, i:j] * c0[:, i:j], 0)))
            lag = lag_ms(o0[i:j - 1], o[i:j - 1])
            sa, sb = int(a * SR), min(int(b * SR), n)
            dl = B.lufs(x[sa:sb]) - B.lufs(x0[sa:sb])
            rows.append('| %s | %.2f | %+d | %+.1f |' % (name, sim, lag, dl))
        rows.append('')
    out = os.path.join(ACE, 'compare.md')
    with open(out, 'w', encoding='utf-8', newline='\r\n') as f:
        f.write('\n'.join(rows) + '\n')
    print('\n'.join(rows))
    print('→', out)


# ---------------------------------------------------------------- 仮ミックス
VOCAL_REL_LU = {'Aメロ': 3.0, 'Bメロ': 2.0, 'サビ': 1.5, 'ラスト': 1.5, 'アウトロ': 2.0}   # sing.py と同じ
MIX_OUT = os.environ.get('PROMO_SONG2_DIR', os.path.join(C.REPO, 'Doc', 'promo', 'song2'))   # 試聴用の出力先


def measure_lag(x, ref):
    """元の伴奏に対する生成側の遅れ（ms）。音のあるセクションの中央値"""
    x0 = B.decode(ref_path(ref))
    n = min(len(x), len(x0))
    o, o0 = onset_env(spec(x[:n])), onset_env(spec(x0[:n]))
    lags = []
    for _, a, b in sections():
        i, j = int(a * 100), min(int(b * 100), len(o)) - 1
        if j - i >= 50 and o[i:j].sum() > 0.2 * o0[i:j].sum():
            lags.append(lag_ms(o0[i:j], o[i:j]))
    return int(np.median(lags))


def mix(path, name, ref, singer):
    """生成した伴奏のずれを直し、既存の歌（v2）を sing.py と同じ考え方で重ねる。
    伴奏のみ creativevision10_<name>_inst.m4a と、歌入り creativevision10_v2_<name>_full<singer>.m4a を書き出す"""
    pre = int(B.PRE_ROLL * SR)
    inst = B.decode(path)
    lag = measure_lag(inst, ref)
    sh = pre + int(lag * SR / 1000)            # 冒頭の無音と遅れを除く（master が無音を付け直す）
    inst = inst[sh:] if sh >= 0 else np.concatenate([np.zeros((-sh, 2), np.float32), inst])
    voc = B.decode(os.path.join(S.OUT, 'creativevision10_v2_vocal%s.m4a' % singer))[pre:]
    n = len(voc)
    inst = np.concatenate([inst, np.zeros((max(0, n - len(inst)), 2), np.float32)])[:n]
    # 歌がある間は伴奏の 1.2〜4.5kHz を約4dB、全体を約1dB下げる（sing.py と同じ）
    env = C.lowpass(np.abs(voc.mean(1)), 8)
    sung = env > env.max() * 0.02
    k = np.clip(env / (np.percentile(env[sung], 60) + 1e-9), 0, 1)[:, None]
    mid = np.stack([C.bandpass(inst[:, c], 1200, 4500) for c in range(2)], 1)
    inst_d = (inst - mid * 0.37 * k) * (1 - 0.1 * k)
    # セクションごとの歌と伴奏の比を VOCAL_REL_LU に合わせ、歌のない区間は直前の値を引き継ぐ
    gc, last = np.ones(n, np.float32), 1.0
    for nm, a, b in sections():
        i0, i1 = int((a - LEAD) * SR), min(int((b - LEAD) * SR), n)
        tgt = next((v for key, v in VOCAL_REL_LU.items() if nm.startswith(key)), None)
        if tgt is not None and sung[i0:i1].mean() > 0.2:
            last = 10 ** ((tgt - (B.lufs(voc[i0:i1]) - B.lufs(inst_d[i0:i1]))) / 20)
        gc[i0:i1] = last
    gc[i1:] = last
    voc = voc * uniform_filter1d(gc, int(0.3 * SR))[:, None]
    out, src = [], S.OUT
    os.makedirs(MIX_OUT, exist_ok=True)
    S.OUT = MIX_OUT                            # encode_safe は S.OUT へ書き出す
    try:
        for x, nm in [(B.master(inst), 'creativevision10_%s_inst' % name),
                      (B.master(inst_d + voc), 'creativevision10_v2_%s_full%s' % (name, singer))]:
            tp = B.encode_safe(x, nm)
            out.append((nm, B.lufs(B.decode(os.path.join(MIX_OUT, nm + '.m4a'))), tp))
    finally:
        S.OUT = src
    for nm, l, tp in out:
        print('%s.m4a  %.1f LUFS  TP %.1f dBTP  ずれ補正 %+d ms' % (nm, l, tp, -lag))
    return lag


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('cmd', choices=['prep', 'workflows', 'compare', 'mix', 'place'])
    ap.add_argument('--song', action='store_true', help='方式 B（歌詞 v2 で歌ごと生成）のワークフローを作る')
    ap.add_argument('files', nargs='*')
    ap.add_argument('--ref', nargs='+', default=['band'], choices=list(REFS))
    ap.add_argument('--mode', nargs='+', default=MODES, choices=MODES)
    ap.add_argument('--denoise', nargs='+', type=float, default=[0.45, 0.6])
    ap.add_argument('--seeds', nargs='+', type=int, default=[SEED])
    ap.add_argument('--singer', default='_ritsu', help='重ねる歌 creativevision10_v2_vocal<singer>.m4a')
    a = ap.parse_args()
    if a.cmd == 'prep':
        prep()
    elif a.cmd == 'workflows':
        workflows(a.mode, a.ref, a.denoise, a.seeds, a.song)
    elif a.cmd == 'place':
        for p, nm in zip(a.files[::2], a.files[1::2]):
            place(p, nm)
    elif a.cmd == 'mix':
        # files は「生成した音源 出力名」の組（例: out/a1_cover_band_s1.flac ace02_cover_b）
        for p, nm in zip(a.files[::2], a.files[1::2]):
            mix(p, nm, a.ref[0], a.singer)
    else:
        compare(a.files, a.ref[0])
