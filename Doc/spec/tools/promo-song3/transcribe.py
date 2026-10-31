# 生成した歌を HeartTranscriptor（HeartMuLa の歌詞向け Whisper）で書き起こし、work/transcripts.json へ書く。
# ComfyUI_FL-HeartMuLa の書き起こしノードは transformers 5 の音声認識パイプラインと入力形式が合わず空文字を返すため、
# 同じモデルを ComfyUI の Python 環境（torch・transformers 入り）から直接使う。
#   <ComfyUI>\.venv\Scripts\python.exe transcribe.py [名前...]   名前を省くと work/raw/*.flac 全部
import os, sys, json, glob
import numpy as np
import torch
from transformers import WhisperForConditionalGeneration, WhisperProcessor, pipeline

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
WORK = os.path.join(os.environ.get('PROMO_SONG3_DIR', os.path.join(REPO, 'Doc', 'promo', 'song3')), 'work')
MODEL = os.environ.get('HEART_TRANSCRIPTOR', os.path.join(
    os.environ['LOCALAPPDATA'], 'Comfy-Desktop', 'ComfyUI-Installs', 'comfy-local', 'ComfyUI', 'models', 'heartmula',
    'HeartTranscriptor-oss', 'HeartTranscriptor-oss'))
SR = 16000


def load(path):
    """FLAC をモノラル 16kHz へ（ComfyUI の環境にある soundfile と torchaudio を使う）"""
    import soundfile as sf
    import torchaudio.functional as AF
    x, sr = sf.read(path, dtype='float32', always_2d=True)
    return AF.resample(torch.from_numpy(x.mean(1)), sr, SR).numpy()


def main(names):
    sys.stdout.reconfigure(encoding='utf-8')
    dev = 'cuda' if torch.cuda.is_available() else 'cpu'
    dt = torch.float16 if dev == 'cuda' else torch.float32
    model = WhisperForConditionalGeneration.from_pretrained(MODEL, dtype=dt).to(dev)
    proc = WhisperProcessor.from_pretrained(MODEL)
    asr = pipeline('automatic-speech-recognition', model=model, tokenizer=proc.tokenizer,
                   feature_extractor=proc.feature_extractor, device=dev, dtype=dt, chunk_length_s=30, batch_size=8)
    out_p = os.path.join(WORK, 'transcripts.json')
    out = json.load(open(out_p, encoding='utf-8')) if os.path.exists(out_p) else {}
    files = [os.path.join(WORK, 'raw', n + '.flac') for n in names] if names else sorted(
        glob.glob(os.path.join(WORK, 'raw', '*.flac')))
    for f in files:
        n = os.path.splitext(os.path.basename(f))[0]
        r = asr({'raw': load(f), 'sampling_rate': SR}, generate_kwargs={'language': 'ja', 'task': 'transcribe'})
        out[n] = r['text'].strip()
        print(n, out[n][:120])
    with open(out_p, 'w', encoding='utf-8', newline='\r\n') as fp:
        json.dump(out, fp, ensure_ascii=False, indent=1)
        fp.write('\n')


if __name__ == '__main__':
    main(sys.argv[1:])
