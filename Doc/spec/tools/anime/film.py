# 作品の書き出し（静止画・区間・本番）。各台本は setup(gl) / frame(gl, t) -> RGB uint8 / audio() / DURATION / FPS を持つ
#   python film.py op --stills 3,10.5,40         指定秒の静止画（out/stills/）
#   python film.py op --sheet 0,68,24             等間隔の静止画を1枚のシートに
#   python film.py op --range 38 46               区間だけ mp4（音声付き）
#   python film.py op                              本番 → out/<名前>.mp4
import os, time, importlib, subprocess, argparse
import numpy as np
from PIL import Image
import engine as E


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('name')
    ap.add_argument('--stills')
    ap.add_argument('--sheet')
    ap.add_argument('--range', nargs=2, type=float)
    ap.add_argument('--audio', action='store_true')
    ap.add_argument('--crf', type=int, default=16)
    a = ap.parse_args()
    mod = importlib.import_module(a.name)
    os.makedirs(E.OUT, exist_ok=True)
    fps = getattr(mod, 'FPS', 30)
    if a.audio:
        E.write_wav(os.path.join(E.OUT, f'{a.name}.wav'), mod.audio()); return
    gl = E.GL(ss=getattr(mod, 'SUPERSAMPLE', 2))
    mod.setup(gl)
    if a.stills or a.sheet:
        sd = os.path.join(E.OUT, 'stills', a.name + time.strftime('_%H%M%S')); os.makedirs(sd, exist_ok=True)
        if a.stills:
            ts = [float(x) for x in a.stills.split(',')]
        else:
            s, e, n = a.sheet.split(','); s, e, n = float(s), float(e), int(n)
            ts = [s + (e - s) * i / (n - 1) for i in range(n)]
        ims = []
        for t in ts:
            t = round(t * fps) / fps
            img = mod.frame(gl, t)
            im = Image.fromarray(img)
            fn = os.path.join(sd, f'{a.name}_{t:06.2f}.jpg')
            im.save(fn, quality=90); ims.append((t, im))
            print(fn, flush=True)
        if a.sheet or len(ims) > 4:
            cols = 4
            tw, th = 480, 270
            rows = (len(ims) + cols - 1) // cols
            sh = Image.new('RGB', (cols * tw, rows * th))
            from PIL import ImageDraw
            d = ImageDraw.Draw(sh)
            for i, (t, im) in enumerate(ims):
                x, y = (i % cols) * tw, (i // cols) * th
                sh.paste(im.resize((tw, th)), (x, y))
                d.text((x + 6, y + 4), f'{t:.2f}', fill=(255, 255, 0))
            sh.save(os.path.join(sd, 'sheet.jpg'), quality=88)
            print(os.path.join(sd, 'sheet.jpg'))
        return
    dur = mod.DURATION
    s, e = a.range if a.range else (0, dur)
    frames = range(int(round(s * fps)), int(round(e * fps)))
    tag = '_part' if a.range else ''
    out = os.path.join(E.OUT, f'{a.name}{tag}.mp4')
    wav = os.path.join(E.OUT, f'{a.name}.wav')
    t0 = time.time()
    if not os.path.exists(wav) or not a.range or os.path.getmtime(wav) < os.path.getmtime(mod.__file__):
        E.write_wav(wav, mod.audio())
        print('audio', round(time.time() - t0, 1), 's', flush=True)
    vtmp = out + '.video.mp4'
    cmd = [E.ffmpeg_exe(), '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
           '-s', f'{gl.W}x{gl.H}', '-r', str(fps), '-i', '-',
           '-c:v', 'libx264', '-preset', 'slow' if not a.range else 'fast', '-crf', str(a.crf if not a.range else 20),
           '-pix_fmt', 'yuv420p', '-tune', 'animation', vtmp]
    ff = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    t0 = time.time()
    for k, i in enumerate(frames):
        img = mod.frame(gl, i / fps)
        ff.stdin.write(np.ascontiguousarray(img).tobytes())
        if k % 60 == 0:
            el = time.time() - t0
            print(f'frame {k}/{len(frames)}  {el:.0f}s  eta {el / (k + 1) * (len(frames) - k):.0f}s', flush=True)
    ff.stdin.close(); ff.wait()
    cmd = [E.ffmpeg_exe(), '-y', '-loglevel', 'error', '-i', vtmp]
    if a.range:
        cmd += ['-ss', str(s), '-t', str(e - s)]
    cmd += ['-i', wav, '-map', '0:v', '-map', '1:a', '-c:v', 'copy', '-c:a', 'aac', '-b:a', '256k', '-shortest',
            '-movflags', '+faststart', out]
    subprocess.run(cmd, check=True)
    os.remove(vtmp)
    print('done', out, round(time.time() - t0), 's')


if __name__ == '__main__':
    main()
