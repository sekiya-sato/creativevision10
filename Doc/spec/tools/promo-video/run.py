# 使い方:
#   python run.py v1                 本番レンダリング（mp4 + 音声）
#   python run.py v1 --stills 1,5,9  指定秒の静止画を out/stills へ
#   python run.py v1 --range 10 14   区間だけ低速確認用 mp4
#   python run.py v1 --audio         音声だけ書き出し
import os, sys, time, importlib, subprocess, argparse
from multiprocessing import Pool
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import core

_mod = None

def _init(name):
    global _mod
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    _mod = importlib.import_module(name)

def _render(i):
    t = i / core.FPS
    return core.to_u8(_mod.render(t)).tobytes()

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('name')
    ap.add_argument('--stills')
    ap.add_argument('--range', nargs=2, type=float)
    ap.add_argument('--audio', action='store_true')
    ap.add_argument('--procs', type=int, default=max(1, os.cpu_count() - 2))
    a = ap.parse_args()
    mod = importlib.import_module(a.name)
    os.makedirs(core.OUT, exist_ok=True)
    if a.stills:
        sd = os.path.join(core.OUT, 'stills', a.name + time.strftime('_%H%M%S')); os.makedirs(sd, exist_ok=True)
        ts = [float(x) for x in a.stills.split(',')]
        with Pool(min(len(ts), a.procs), _init, (a.name,)) as p:
            imgs = p.map(_render, [int(round(t * core.FPS)) for t in ts])
        for t, b in zip(ts, imgs):
            im = Image.frombytes('RGB', (core.W, core.H), b)
            fn = os.path.join(sd, f'{a.name}_{t:05.1f}.jpg')
            im.save(fn, quality=88); print(fn)
        return
    dur = mod.DURATION
    wav = os.path.join(core.OUT, f'{a.name}.wav')
    if not a.range:
        t0 = time.time()
        core.write_wav(wav, mod.audio())
        print('audio', round(time.time() - t0, 1), 's')
        if a.audio:
            return
    s, e = (a.range if a.range else (0, dur))
    frames = range(int(s * core.FPS), int(e * core.FPS))
    out = os.path.join(core.OUT, f'{a.name}{"_part" if a.range else ""}.mp4')
    vtmp = out + '.video.mp4'
    cmd = [core.ffmpeg_exe(), '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
           '-s', f'{core.W}x{core.H}', '-r', str(core.FPS), '-i', '-',
           '-c:v', 'libx264', '-preset', 'slow' if not a.range else 'veryfast', '-crf', '15' if not a.range else '22',
           '-pix_fmt', 'yuv420p', '-tune', 'film', vtmp]
    ff = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    t0 = time.time()
    with Pool(a.procs, _init, (a.name,)) as p:
        for k, b in enumerate(p.imap(_render, frames, chunksize=2)):
            ff.stdin.write(b)
            if k % 60 == 0:
                el = time.time() - t0
                print(f'frame {k}/{len(frames)}  {el:.0f}s  eta {el / (k + 1) * (len(frames) - k):.0f}s', flush=True)
    ff.stdin.close(); ff.wait()
    if a.range:
        os.replace(vtmp, out)
    else:
        subprocess.run([core.ffmpeg_exe(), '-y', '-loglevel', 'error', '-i', vtmp, '-i', wav,
                        '-c:v', 'copy', '-c:a', 'aac', '-b:a', '256k', '-shortest', '-movflags', '+faststart', out], check=True)
        os.remove(vtmp)
    print('done', out, round(time.time() - t0), 's')

if __name__ == '__main__':
    main()
