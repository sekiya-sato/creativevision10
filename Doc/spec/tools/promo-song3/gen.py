# Creative Vision 10 のイメージから、ComfyUI の音楽生成モデルで曲（伴奏・歌詞・歌声）を作り比べる。
# 当初のプロモーションソング（../promo-song、VOICEVOX 版）とは別物。出力は Doc/promo/song3。
#
# モデル（MODELS）と、それぞれの調整方法
#   yue2     YuE2 3B（int8）。Style（言語・ジャンル・声・テンポ・楽器）と Lyrics を分け、ABC（楽譜）計画を挟んで生成する。
#            調整: ABC 計画 full / melody、YuE2GenerateMusic の cfg_scale（歌詞・スタイルへの追従）
#   minimax  MiniMax Music 3。Caption を Global Metadata → Vocal Details → Arrangement の3部で書く。
#            歌詞の構成タグ（[Intro] [Verse] [Pre-Chorus] [Chorus] [Bridge] [Outro]）だけが構成の指示として効く。
#            調整: テキストエンコードの cfg_scale・top_k、KSampler の steps
#   acexl    ACE-Step 1.5 XL Base。説明文タグ＋BPM・キー・言語、歌詞の構成タグに演奏の指示を添えられる。
#            調整: KSampler の cfg、言語モデル（音声コード）の cfg_scale・temperature
#   heartmula HeartMuLa-oss-3B（ComfyUI_FL-HeartMuLa）。タグはカンマ区切り、構成タグは [Prechorus] 表記。
#            調整: cfg_scale、temperature、top_k
#
#   python gen.py workflows                 各モデル×調整違い（VARIANTS）の API 形式ワークフローと、渡した歌詞・スタイル
#   python gen.py transcribe <音源...>       HeartTranscriptor で歌詞を書き起こすワークフロー
#   python gen.py analyze                   生成音源の数値（長さ・ラウドネス・帯域・無音）と歌詞の一致率 → work/compare.md
#   python gen.py place <音源 出力名>...     音量を -14 LUFS にそろえて song3/<出力名>.m4a へ置く
# 生成は comfy-mcp（upload_file → validate_workflow → run_workflow → fetch_outputs）で行う。
import os, sys, json, argparse, difflib, re, subprocess
import numpy as np
from scipy import signal as sig
from scipy.ndimage import maximum_filter1d, uniform_filter1d

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
OUT = os.environ.get('PROMO_SONG3_DIR', os.path.join(REPO, 'Doc', 'promo', 'song3'))
WORK = os.path.join(OUT, 'work')
SR = 48000
SEED = 20261031
DURATION = 120
BPM = 150

# ---------------------------------------------------------------- イメージと歌詞
# 伝えるのはイメージだけ（README の「プロジェクトの目的」から）。既存のプロモーションソングの歌詞・旋律は使わない。
BRIEF = ('Creative Vision 10 は、アパレル企業向けの販売管理をオープンソースで届ける基幹業務パッケージ。'
         '20年以上・延べ200社で使われた CV.net の技術を受け継ぎ、.NET 10 と AI を使った開発で生まれ変わった。'
         '中小企業でも安心して使える、ひらかれた未来の道具。明るく前向きで、受け継いだものを新しく描き直す希望の歌。')

# 共通の歌詞（セクション名, 行）。セクション名は内部の呼び名で、モデルごとの表記へ置き換える
LYRICS = [
    ('intro', []),
    ('verse', ['ほどいた糸を もう一度つなぐ',
               '色とりどりの 夢をならべて',
               '小さな店にも 大きな未来',
               'ひらいた窓から 風が吹きこむ']),
    ('prechorus', ['受け継いだ想い 新しいかたちで',
                   'いま 走り出す']),
    ('chorus', ['クリエイティブ・ビジョン・テン',
                '描こう 明日のデザイン',
                'だれもが使える ひらかれた力',
                'クリエイティブ・ビジョン・テン',
                '重ねた時間が 光になる']),
    ('bridge', ['エーアイと並んで 確かめながら',
                'ひとつずつ 未来へ届ける']),
    ('chorus', ['クリエイティブ・ビジョン・テン',
                '描こう 明日のデザイン',
                'だれもが使える ひらかれた力',
                'クリエイティブ・ビジョン・テン',
                'いっしょに 夢をかなえよう']),
    ('outro', ['クリエイティブ・ビジョン・テン']),
]

# 構成タグの表記（モデルの推奨に合わせる）
TAGS_OF = {
    'yue2': {'verse': 'Verse', 'prechorus': 'Verse', 'chorus': 'Chorus', 'bridge': 'Bridge', 'outro': 'Outro'},
    'minimax': {'intro': 'Intro', 'verse': 'Verse', 'prechorus': 'Pre-Chorus', 'chorus': 'Chorus',
                'bridge': 'Bridge', 'outro': 'Outro'},
    'acexl': {'intro': 'Intro - bright koto riff and synth, drums build up',
              'verse': 'Verse - light groove, clear female vocal',
              'prechorus': 'Pre-Chorus - rising energy',
              'chorus': 'Chorus - full band, powerful, catchy hook',
              'bridge': 'Bridge - half-time, soft piano and pad',
              'outro': 'Outro - final hook, big ending'},
    'heartmula': {'intro': 'Intro', 'verse': 'Verse', 'prechorus': 'Prechorus', 'chorus': 'Chorus',
                  'bridge': 'Bridge', 'outro': 'Outro'},
}


def lyrics_for(model):
    """モデルの構成タグで歌詞を組み立てる。YuE2 は歌う区間だけ（前奏は自動で付く）"""
    out = []
    for sec, lines in LYRICS:
        tag = TAGS_OF[model].get(sec)
        if tag is None or (model == 'yue2' and not lines):
            continue
        out.append('[%s]\n%s' % (tag, '\n'.join(lines)) if lines else '[%s]' % tag)
    return '\n\n'.join(out)


# スタイルの指定（モデルごとの書式）
STYLE = {
    # YuE2: 言語 + ジャンル + 声 + テンポ + 楽器 + 曲の特徴（テンプレートの Prompt Guide の順）
    'yue2': ('Japanese, J-pop rock, anime opening theme, bright clear young female vocal, 150 BPM, '
             'electric guitars, punchy drums, melodic bass, sparkling synth, koto accents, '
             'uplifting and hopeful, catchy memorable chorus, polished modern production, clear diction'),
    # MiniMax Music 3: Global Metadata → Vocal Details → Arrangement
    'minimax': ('Global Metadata: Japanese J-pop rock, anime opening theme. 150 BPM, C major, major scale. '
                'Bright, hopeful and forward-looking; starts light and sparkling, lifts in the pre-chorus, '
                'bursts open in a big singalong chorus, calms in the bridge and ends on a triumphant final chorus. '
                'Product launch video, morning energy. Polished, wide, modern pop-rock production.\n'
                'Vocal Details: Solo young female lead singing in Japanese, bright and clear timbre, '
                'crisp diction, confident and cheerful delivery, doubled lead and light harmonies in the chorus, '
                'group backing vocals on the hook.\n'
                'Arrangement: Electric guitars (power chords and clean arpeggios), melodic electric bass, '
                'punchy rock drums with driving eighth-note hi-hats, sparkling synth lead, '
                'koto accents in the intro and verses, piano and pad in the bridge, '
                'big crash and full band ending.'),
    # ACE-Step 1.5: 説明文（テンプレートは文章で書く）
    'acexl': ('An uplifting Japanese J-pop rock anime opening theme at 150 BPM in 4/4, C major. '
              'Bright clear young female lead vocal singing in Japanese with crisp diction, '
              'doubled vocals and harmonies in the chorus. Electric guitars with power chords and clean arpeggios, '
              'melodic electric bass, punchy rock drums, sparkling synth lead and koto accents. '
              'Hopeful, forward-looking and catchy, polished modern production.'),
    # HeartMuLa: カンマ区切りの短いタグ（ジャンル・声・気分・テンポ・楽器）
    'heartmula': ('j-pop,anime,pop rock,japanese,female vocal,bright,energetic,uplifting,hopeful,'
                  'fast tempo,electric guitar,bass,drums,synthesizer,koto'),
}

# ---------------------------------------------------------------- 調整違い（モデルごとに3つ）
VARIANTS = {
    'yue2': [('v1', {}, 'テンプレート既定（ABC 計画 full、cfg 1.0）'),
             ('v2', {'cfg_scale': 1.5}, '歌詞・スタイルへの追従を上げる（cfg 1.5）'),
             ('v3', {'mode': 'melody'}, 'ABC 計画を旋律だけにし、伴奏は自由にする')],
    'minimax': [('v1', {}, 'テンプレート既定（エンコード cfg 1.7・top_k 50、30 ステップ）'),
                ('v2', {'te_cfg': 2.5}, 'キャプション・歌詞への追従を上げる（エンコード cfg 2.5）'),
                ('v3', {'steps': 50}, 'ステップ数を 50 に増やす')],
    'acexl': [('v1', {}, 'テンプレート既定（50 ステップ、cfg 6、音声コード cfg 2・temperature 0.85）'),
              ('v2', {'cfg': 4.0}, 'KSampler の cfg を 4 に下げる（過剰な強調を抑える）'),
              ('v3', {'lm_cfg': 2.5, 'lm_temp': 0.7}, '音声コードの cfg 2.5・temperature 0.7（構成・歌詞への追従）')],
    'heartmula': [('v1', {}, '既定（cfg 1.5、temperature 1.0、top_k 50）'),
                  ('v2', {'cfg': 2.5}, 'cfg 2.5（タグ・歌詞への追従を上げる）'),
                  ('v3', {'temp': 0.8, 'top_k': 30}, 'temperature 0.8・top_k 30（ぶれを減らす）')],
}
MODELS = list(VARIANTS)


def save(prefix, src):
    return {'class_type': 'SaveAudioAdvanced',
            'inputs': {'audio': src, 'filename_prefix': 'audio/cv10_song3/' + prefix, 'format': 'flac'}}


def wf_yue2(name, p):
    style, lyr, mode = STYLE['yue2'], lyrics_for('yue2'), p.get('mode', 'full')
    return {
        '15': {'class_type': 'CheckpointLoaderSimple', 'inputs': {'ckpt_name': 'yue2_3b_int8_convrot.safetensors'}},
        '24': {'class_type': 'YuE2GenerateABC', 'inputs': {
            'clip': ['15', 1], 'style': style, 'lyrics': lyr, 'seed': SEED, 'mode': mode, 'max_abc_tokens': 8192,
            'temperature': 0.7, 'top_p': 0.9, 'top_k': 30, 'repetition_penalty': 1.005, 'penalty_window': 100}},
        '25': {'class_type': 'YuE2GenerateMusic', 'inputs': {
            'clip': ['15', 1], 'style': style, 'lyrics': lyr, 'abc': ['24', 0], 'seed': SEED, 'mode': mode,
            'max_duration': float(DURATION + 30), 'temperature': 1.0, 'top_p': 0.95, 'top_k': 100,
            'repetition_penalty': 1.2, 'cfg_scale': p.get('cfg_scale', 1.0)}},
        '18': {'class_type': 'ConditioningZeroOut', 'inputs': {'conditioning': ['25', 0]}},
        '5': {'class_type': 'EmptyYuE2LatentAudio', 'inputs': {'seconds': ['25', 1], 'batch_size': 1}},
        '8': {'class_type': 'KSampler', 'inputs': {
            'model': ['15', 0], 'positive': ['25', 0], 'negative': ['18', 0], 'latent_image': ['5', 0],
            'seed': SEED, 'steps': 32, 'cfg': 1.0, 'sampler_name': 'dpm_2', 'scheduler': 'sgm_uniform', 'denoise': 1.0}},
        '9': {'class_type': 'VAEDecodeAudio', 'inputs': {'samples': ['8', 0], 'vae': ['15', 2]}},
        '10': save(name, ['9', 0]),
    }


def wf_minimax(name, p):
    cfg = p.get('te_cfg', 1.7)
    return {
        '6': {'class_type': 'UNETLoader', 'inputs': {'unet_name': 'minimax_music3_dit_fp16.safetensors',
                                                     'weight_dtype': 'default'}},
        '3': {'class_type': 'CLIPLoader', 'inputs': {
            'clip_name': 'minimax_music3_text_encoder_pruned_int8_convrot.safetensors', 'type': 'minimax',
            'device': 'default'}},
        '7': {'class_type': 'VAELoader', 'inputs': {'vae_name': 'minimax_music3_dav.safetensors'}},
        '13': {'class_type': 'MiniMaxMusic3TextEncode', 'inputs': {
            'clip': ['3', 0], 'caption': STYLE['minimax'], 'lyrics': lyrics_for('minimax'), 'seed': SEED,
            'max_duration': float(DURATION), 'cfg_scale': cfg, 'top_k': p.get('top_k', 50)}},
        '10': {'class_type': 'ConditioningZeroOut', 'inputs': {'conditioning': ['13', 0]}},
        '15': {'class_type': 'EmptyMiniMaxMusic3LatentAudio', 'inputs': {'seconds': ['13', 1], 'batch_size': 1}},
        '9': {'class_type': 'KSampler', 'inputs': {
            'model': ['6', 0], 'positive': ['13', 0], 'negative': ['10', 0], 'latent_image': ['15', 0],
            'seed': SEED, 'steps': p.get('steps', 30), 'cfg': 1.7, 'sampler_name': 'euler', 'scheduler': 'simple',
            'denoise': 1.0}},
        '12': {'class_type': 'VAEDecodeAudio', 'inputs': {'samples': ['9', 0], 'vae': ['7', 0]}},
        '35': save(name, ['12', 0]),
    }


def wf_acexl(name, p):
    return {
        '104': {'class_type': 'UNETLoader', 'inputs': {'unet_name': 'acestep_v1.5_xl_base_bf16.safetensors',
                                                       'weight_dtype': 'default'}},
        '78': {'class_type': 'ModelSamplingAuraFlow', 'inputs': {'model': ['104', 0], 'shift': 3.0}},
        '105': {'class_type': 'DualCLIPLoader', 'inputs': {
            'clip_name1': 'qwen_0.6b_ace15.safetensors', 'clip_name2': 'qwen_4b_ace15.safetensors',
            'type': 'ace', 'device': 'default'}},
        '106': {'class_type': 'VAELoader', 'inputs': {'vae_name': 'ace_1.5_vae.safetensors'}},
        '94': {'class_type': 'TextEncodeAceStepAudio1.5', 'inputs': {
            'clip': ['105', 0], 'tags': STYLE['acexl'], 'lyrics': lyrics_for('acexl'), 'seed': SEED, 'bpm': BPM,
            'duration': float(DURATION), 'timesignature': '4', 'language': 'ja', 'keyscale': 'C major',
            'generate_audio_codes': True, 'cfg_scale': p.get('lm_cfg', 2.0), 'temperature': p.get('lm_temp', 0.85),
            'top_p': 0.9, 'top_k': 0, 'min_p': 0.0}},
        '47': {'class_type': 'ConditioningZeroOut', 'inputs': {'conditioning': ['94', 0]}},
        '98': {'class_type': 'EmptyAceStep1.5LatentAudio', 'inputs': {'seconds': float(DURATION), 'batch_size': 1}},
        '3': {'class_type': 'KSampler', 'inputs': {
            'model': ['78', 0], 'positive': ['94', 0], 'negative': ['47', 0], 'latent_image': ['98', 0],
            'seed': SEED, 'steps': 50, 'cfg': p.get('cfg', 6.0), 'sampler_name': 'euler', 'scheduler': 'simple',
            'denoise': 1.0}},
        '18': {'class_type': 'VAEDecodeAudio', 'inputs': {'samples': ['3', 0], 'vae': ['106', 0]}},
        '111': save(name, ['18', 0]),
    }


def wf_heartmula(name, p):
    # fp16 のままだと本体（約7.5GB）と HeartCodec（fp32、約6.6GB）で 16GB の VRAM が溢れ、
    # 120 秒の曲が1時間たっても終わらなかった（2026-10-31）。本体を 4bit にして VRAM に収める
    return {
        '1': {'class_type': 'FL_HeartMuLa_ModelLoader', 'inputs': {
            'model_version': '3B', 'memory_mode': 'auto', 'precision': 'auto', 'use_4bit': True,
            'force_reload': False}},
        '2': {'class_type': 'FL_HeartMuLa_Conditioning', 'inputs': {
            'model': ['1', 0], 'lyrics': lyrics_for('heartmula'), 'tags': STYLE['heartmula'],
            'cfg_scale': p.get('cfg', 1.5)}},
        '3': {'class_type': 'FL_HeartMuLa_Sampler', 'inputs': {
            'model': ['1', 0], 'conditioning': ['2', 0], 'max_duration_sec': DURATION,
            'temperature': p.get('temp', 1.0), 'top_k': p.get('top_k', 50), 'seed': SEED}},
        '4': {'class_type': 'FL_HeartMuLa_Decode', 'inputs': {'model': ['1', 0], 'audio_tokens': ['3', 0]}},
        '5': save(name, ['4', 0]),
    }


BUILD = {'yue2': wf_yue2, 'minimax': wf_minimax, 'acexl': wf_acexl, 'heartmula': wf_heartmula}


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\r\n') as f:
        f.write(text)


def workflows(models):
    for m in models:
        write(os.path.join(WORK, 'prompts', m + '.txt'),
              '# style\n%s\n\n# lyrics\n%s\n' % (STYLE[m], lyrics_for(m)))
        for v, p, _ in VARIANTS[m]:
            name = '%s_%s' % (m, v)
            write(os.path.join(WORK, 'workflows', name + '.json'),
                  json.dumps(BUILD[m](name, p), ensure_ascii=False, indent=1) + '\n')
            print(name)


def transcribe(files):
    """LoadAudio（ComfyUI の input に置いた名前）→ HeartTranscriptor → 文字列表示"""
    for f in files:
        b = os.path.splitext(os.path.basename(f))[0]
        g = {'1': {'class_type': 'LoadAudio', 'inputs': {'audio': os.path.basename(f)}},
             '2': {'class_type': 'FL_HeartMuLa_Transcribe', 'inputs': {'audio': ['1', 0]}},
             '3': {'class_type': 'PreviewAny', 'inputs': {'source': ['2', 0]}}}
        write(os.path.join(WORK, 'workflows', 'tr_%s.json' % b), json.dumps(g, ensure_ascii=False, indent=1) + '\n')
        print('tr_%s.json' % b)


# ---------------------------------------------------------------- 数値
def ffmpeg():
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()


def decode(path):
    r = subprocess.run([ffmpeg(), '-loglevel', 'error', '-i', path, '-f', 's16le', '-ac', '2', '-ar', str(SR), '-'],
                       capture_output=True, check=True)
    return np.frombuffer(r.stdout, '<i2').reshape(-1, 2).astype(np.float32) / 32768


_K1 = ([1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585])
_K2 = ([1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621])


def lufs(x):
    """ITU-R BS.1770 統合ラウドネス（48kHz、ゲーティングあり）"""
    y = sig.lfilter(*_K2, sig.lfilter(*_K1, x, axis=0), axis=0)
    blk, hop = int(0.4 * SR), int(0.1 * SR)
    ms = np.array([np.mean(y[i:i + blk] ** 2, 0).sum() for i in range(0, len(y) - blk, hop)])
    l = -0.691 + 10 * np.log10(ms + 1e-12)
    g = ms[l > -70]
    if len(g) == 0:
        return -99.0
    g2 = ms[(l > -70) & (l > -0.691 + 10 * np.log10(g.mean()) - 10)]
    return float(-0.691 + 10 * np.log10(g2.mean()))


def metrics(x):
    m = x.mean(1)
    f, _, z = sig.stft(m, SR, nperseg=4096)
    p = (np.abs(z) ** 2).sum(1)
    c = np.cumsum(p) / p.sum()
    hop = int(0.5 * SR)
    rms = np.array([np.sqrt(np.mean(m[i:i + hop] ** 2)) for i in range(0, len(m) - hop, hop)])
    return {'sec': len(x) / SR, 'lufs': lufs(x), 'peak_db': float(20 * np.log10(np.abs(x).max() + 1e-12)),
            'rolloff95': float(f[np.searchsorted(c, 0.95)]),
            'hi8k': float(p[f >= 8000].sum() / p.sum() * 100),
            'silence': float((20 * np.log10(rms + 1e-9) < -50).mean() * 100)}


# 書き起こし（Whisper は漢字・英字で書く）と歌詞の表記ゆれ。読みが同じ語を同じ表記へ寄せる
SAME_WORD = [(r'creative\s*vision\s*10|クリエイティブ[\s・]*[ビヴ]ィ?ジョン[\s・-]*テン', 'CV10'),
             ('解いた', 'ほどいた'), ('繋ぐ', 'つなぐ'), ('並べて', 'ならべて'), ('開かれた', 'ひらかれた'),
             ('開いた', 'ひらいた'), ('吹き込む', '吹きこむ'), ('カタチ', 'かたち'), ('形', 'かたち'),
             ('チカラ', '力'), ('思い', '想い'), ('今', 'いま'), ('あした', '明日'), ('誰', 'だれ'),
             ('一緒', 'いっしょ'), ('叶え', 'かなえ'), ('AI', 'エーアイ')]


def norm_text(t):
    """書き起こしと歌詞を比べるための正規化（表記ゆれ・記号・空白をそろえ、カタカナをひらがなへ）"""
    for a, b in SAME_WORD:
        t = re.sub(a, b, t, flags=re.I)
    t = re.sub(r'[\s・、。！!？?「」（）()\[\]\-ー〜~,.　]', '', t)
    return ''.join(chr(ord(ch) - 0x60) if 'ァ' <= ch <= 'ヶ' else ch for ch in t)


def lyric_score(text):
    """歌詞の文字がどれだけ書き起こしに順番どおり現れるか（recall）と、製品名が聞き取れた回数"""
    ref = norm_text(''.join(l for _, ls in LYRICS for l in ls))
    hyp = norm_text(text)
    sm = difflib.SequenceMatcher(None, ref, hyp, autojunk=False)
    hit = sum(b.size for b in sm.get_matching_blocks())
    return {'recall': hit / max(1, len(ref)), 'name_hits': hyp.count('CV10')}


def analyze():
    raw = os.path.join(WORK, 'raw')
    tr = {}
    p = os.path.join(WORK, 'transcripts.json')
    if os.path.exists(p):
        tr = json.load(open(p, encoding='utf-8'))
    rows = ['| 名前 | 調整 | 長さ 秒 | LUFS | ピーク dB | 95%ロールオフ Hz | 8kHz以上 % | 無音 % | 歌詞一致 | 製品名 |',
            '|---|---|---|---|---|---|---|---|---|---|']
    res = {}
    for m in MODELS:
        for v, _, note in VARIANTS[m]:
            n = '%s_%s' % (m, v)
            f = os.path.join(raw, n + '.flac')
            if not os.path.exists(f):
                continue
            k = metrics(decode(f))
            s = lyric_score(tr[n]) if n in tr else {'recall': float('nan'), 'name_hits': -1}
            res[n] = dict(k, **s)
            rows.append('| %s | %s | %.1f | %.1f | %.1f | %.0f | %.2f | %.1f | %.2f | %d |' % (
                n, note, k['sec'], k['lufs'], k['peak_db'], k['rolloff95'], k['hi8k'], k['silence'],
                s['recall'], s['name_hits']))
    write(os.path.join(WORK, 'compare.md'), '\n'.join(rows) + '\n')
    write(os.path.join(WORK, 'compare.json'), json.dumps(res, ensure_ascii=False, indent=1) + '\n')
    print('\n'.join(rows))


# ---------------------------------------------------------------- 配置
def limiter(x, ceil=0.89, la_ms=3.0, rel_ms=80):
    """先読みピークリミッター（サンプルピークを ceil 以下に）"""
    la = max(1, int(la_ms * SR / 1000))
    pk = maximum_filter1d(np.abs(x).max(1), 2 * la + 1)
    g = np.minimum(1.0, ceil / (pk + 1e-9))
    a = np.exp(-1 / (rel_ms * SR / 1000))
    out = np.empty_like(g)
    cur = 1.0
    for i, v in enumerate(g):
        cur = v if v < cur else a * cur + (1 - a) * v
        out[i] = cur
    return x * uniform_filter1d(out, la)[:, None]


def place(path, name, target=-14.0, fade=3.0):
    x = decode(path)
    # 長さの上限で歌の途中のまま切れている（最後の1秒が全体から 15dB 以内）なら、最後の fade 秒を下げて終える
    rms = lambda a: 20 * np.log10(np.sqrt(np.mean(a ** 2)) + 1e-9)
    if rms(x[-SR:]) > rms(x) - 15:
        n = int(fade * SR)
        x[-n:] *= np.linspace(1, 0, n)[:, None] ** 2
        print('%s: 途中で切れているため最後の %.0f 秒をフェードアウト' % (name, fade))
    for _ in range(6):
        x = limiter(x * 10 ** ((target - lufs(x)) / 20))
        if abs(target - lufs(x)) < 0.1:
            break
    wav = os.path.join(OUT, name + '.wav')
    import wave
    with wave.open(wav, 'wb') as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype('<i2').tobytes())
    m4a = os.path.join(OUT, name + '.m4a')
    subprocess.run([ffmpeg(), '-y', '-loglevel', 'error', '-i', wav, '-c:a', 'aac', '-b:a', '192k',
                    '-metadata', 'title=Creative Vision 10 (%s)' % name, m4a], check=True)
    os.remove(wav)
    y = decode(m4a)
    print('%s.m4a  %.1f LUFS  ピーク %.1f dB  %.1f 秒' % (name, lufs(y), 20 * np.log10(np.abs(y).max()), len(y) / SR))


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('cmd', choices=['workflows', 'transcribe', 'analyze', 'place'])
    ap.add_argument('files', nargs='*')
    ap.add_argument('--model', nargs='+', default=MODELS, choices=MODELS)
    a = ap.parse_args()
    if a.cmd == 'workflows':
        workflows(a.model)
    elif a.cmd == 'transcribe':
        transcribe(a.files)
    elif a.cmd == 'analyze':
        analyze()
    else:
        for p, n in zip(a.files[::2], a.files[1::2]):
            place(p, n)
