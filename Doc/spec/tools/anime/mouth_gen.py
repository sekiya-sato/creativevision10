# 口パクの顔の静止画を ComfyUI（FLUX.2 Klein 4B の参照画像編集）で作るグラフを書き出す
#   python mouth_gen.py   → work/mouth/klein.json（ComfyUI MCP の run_workflow で実行）
# 入力は mouth_sheet.py が描いた閉じ口の顔（rig_<表情>_q.png。ComfyUI の input へ cv10_mouth_<表情>.png として置く）。
# 口だけを母音の形に描き直し、目・眉・頬・汗・輪郭はそのまま残すよう指示する
import json
import os
import engine as E

OUT = os.path.join(E.WORK, 'mouth')
EXPRS = ['N', 'HP', 'ST', 'CE', 'WR']
FEEL = {'N': 'calm neutral', 'HP': 'happy smiling', 'ST': 'stressed and tired', 'CE': 'happy with closed eyes',
        'WR': 'worried'}
# 日本語の母音の口の形（アニメの口パクの作画の約束ごと）
SHAPES = {
    'a': 'open wide vertically in a large rounded shape, upper teeth and tongue slightly visible, singing "a"',
    'i': 'opened only slightly and stretched wide horizontally, a thin opening showing the teeth, singing "i"',
    'u': 'small and rounded, lips pushed slightly forward with a small round opening, singing "u" '
         '(a simple small round mouth, not a cat mouth, not a smirk)',
    'e': 'opened halfway and stretched horizontally, singing "e"',
    'o': 'opened in a rounded vertical oval of medium size, singing "o"',
}
SIZE = 1024
SEED = 20261026


def prompt(expr, vowel):
    return (f'Edit this anime illustration of a girl\'s face. Change ONLY her mouth: the mouth is {SHAPES[vowel]}. '
            f'The mouth must match her {FEEL[expr]} facial expression and sit naturally between the nose and the chin, '
            'with the same position and size proportion as a hand-drawn anime mouth, drawn with thin soft dark red lines '
            'like the existing nose line. '
            'Keep everything else exactly the same: eyes, eyelids, eyebrows, blush, nose, face outline, chin, '
            'hair, colors, shading and framing. Do not add sweat drops, tears or any new marks. '
            'Same art style, no new objects, no text.')


def graph():
    g = {}

    def node(key, cls, **inputs):
        g[str(key)] = {'class_type': cls, 'inputs': inputs}
        return [str(key), 0]
    model = node(1, 'UNETLoader', unet_name='flux-2-klein-4b-fp8.safetensors', weight_dtype='default')
    clip = node(2, 'CLIPLoader', clip_name='qwen_3_4b.safetensors', type='flux2', device='cpu')
    vae = node(3, 'VAELoader', vae_name='flux2-vae.safetensors')
    noise = node(7, 'RandomNoise', noise_seed=SEED)
    sampler = node(8, 'KSamplerSelect', sampler_name='euler')
    b = 100
    for expr in EXPRS:
        img = node(b, 'LoadImage', image=f'cv10_mouth_{expr}.png')
        big = node(b + 1, 'ImageScale', image=img, upscale_method='lanczos', width=SIZE, height=SIZE, crop='disabled')
        ref = node(b + 2, 'VAEEncode', pixels=big, vae=vae)
        b += 3
        for v in SHAPES:
            text = node(b, 'CLIPTextEncode', clip=clip, text=prompt(expr, v))
            zero = node(b + 1, 'ConditioningZeroOut', conditioning=text)
            pos = node(b + 2, 'ReferenceLatent', conditioning=text, latent=ref)
            neg = node(b + 3, 'ReferenceLatent', conditioning=zero, latent=ref)
            guide = node(b + 4, 'CFGGuider', model=model, positive=pos, negative=neg, cfg=1)
            sig = node(b + 5, 'Flux2Scheduler', steps=4, width=SIZE, height=SIZE)
            lat = node(b + 6, 'EmptyFlux2LatentImage', width=SIZE, height=SIZE, batch_size=1)
            out = node(b + 7, 'SamplerCustomAdvanced', noise=noise, guider=guide, sampler=sampler, sigmas=sig, latent_image=lat)
            rgb = node(b + 8, 'VAEDecode', samples=out, vae=vae)
            node(b + 9, 'SaveImage', images=rgb, filename_prefix=f'cv10-mouth/{expr}_{v}')
            b += 10
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, 'klein.json')
    json.dump(g, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(path)


if __name__ == '__main__':
    graph()
