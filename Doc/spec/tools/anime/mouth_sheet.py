# 口パクの全パターン（表情 × 母音の口の形）を、今のリグ（rig_p.Character）で描いて顔の寄りの一覧にする
#   python mouth_sheet.py                 → work/mouth/rig_<表情>_<母音>.png と work/mouth/rig_sheet.jpg
# 口の形は songsync.SHAPES（歌の口パク）の (開き, 幅) をそのまま使う。開きは最大（音量・母音の頭の補正なし）
import os
import numpy as np
from PIL import Image, ImageDraw
import engine as E
import prep_person
import rig_p as RP
import songsync as SS

OUT = os.path.join(E.WORK, 'mouth')
# 表情（rig_p の6表情の並び: 通常, ハッピー, ストレス, ハイテンション, 目を閉じた笑顔, 困り顔）。ハイテンションは口が動かない
EXPRS = {'N': (1, 0, 0, 0, 0, 0), 'HP': (0, 1, 0, 0, 0, 0), 'ST': (0, 0, 1, 0, 0, 0), 'CE': (0, 0, 0, 0, 1, 0),
         'WR': (0, 0, 0, 0, 0, 1)}
VOWELS = ['q', 'n', 'a', 'i', 'u', 'e', 'o']
FACE = (1040, 980)     # 顔の中心（旧リグ座標）
ZOOM = 1.05
CROP = 680              # 書き出す正方形の一辺（画面 px）
SIM = dict(head=0.0, head_shift=(0.0, 0.0), hat=0.0, bow=0.0, hairL=0.0, hairR=0.0, wave=0.0, bang=0.0, ear=0.0)


def render(gl, ch, expr, vowel):
    p = RP.Pose()
    p.expr_full = p.expr_face = expr
    p.mouth, p.wide = SS.SHAPES[vowel]
    Hm = E.m3_translate(960, 540) @ E.m3_scale(ZOOM) @ E.m3_translate(-FACE[0], -FACE[1]) @ E.m3_scale(2048)
    gl.use(gl.main, clear=(0.16, 0.27, 0.51, 1.0))
    ch.draw(Hm, ch.uniforms(p, 0.0, SIM))
    img = gl.finish(dict(bloomAmt=0.0, vign=0.0, grainAmt=0.0))
    h = CROP // 2
    return img[540 - h:540 + h, 960 - h:960 + h]


def main():
    os.makedirs(OUT, exist_ok=True)
    prep_person.build_rig()
    gl = E.GL(ss=2)
    ch = RP.Character(gl)
    tw = 300
    sheet = Image.new('RGB', (tw * len(VOWELS) + 60, tw * len(EXPRS) + 30), (30, 30, 30))
    d = ImageDraw.Draw(sheet)
    for j, v in enumerate(VOWELS):
        d.text((60 + j * tw + 6, 8), v, fill=(255, 255, 0))
    for i, (k, e) in enumerate(EXPRS.items()):
        d.text((8, 30 + i * tw + tw // 2), k, fill=(255, 255, 0))
        for j, v in enumerate(VOWELS):
            im = Image.fromarray(render(gl, ch, e, v))
            im.save(os.path.join(OUT, f'rig_{k}_{v}.png'))
            sheet.paste(im.resize((tw, tw), Image.LANCZOS), (60 + j * tw, 30 + i * tw))
    sheet.save(os.path.join(OUT, 'rig_sheet.jpg'), quality=90)
    print(os.path.join(OUT, 'rig_sheet.jpg'))


def compare():
    """今のリグ（左）と ComfyUI の静止画（右, mouth_gen.py）を表情 × 母音で並べる → work/mouth/compare_sheet.jpg"""
    import glob
    gen_dir = r'C:\Users\user2010\AppData\Local\Comfy-Desktop\ComfyUI-Shared\output\cv10-mouth'
    vs = ['a', 'i', 'u', 'e', 'o']
    tw = 260
    sheet = Image.new('RGB', (60 + len(vs) * (2 * tw + 12), 30 + len(EXPRS) * tw), (30, 30, 30))
    d = ImageDraw.Draw(sheet)
    for j, v in enumerate(vs):
        d.text((60 + j * (2 * tw + 12) + 6, 8), f'{v}  rig | comfy', fill=(255, 255, 0))
    for i, k in enumerate(EXPRS):
        d.text((8, 30 + i * tw + tw // 2), k, fill=(255, 255, 0))
        for j, v in enumerate(vs):
            x = 60 + j * (2 * tw + 12)
            rig = Image.open(os.path.join(OUT, f'rig_{k}_{v}.png')).convert('RGB').resize((tw, tw), Image.LANCZOS)
            g = sorted(glob.glob(os.path.join(gen_dir, f'{k}_{v}_*.png')))
            sheet.paste(rig, (x, 30 + i * tw))
            if g:
                sheet.paste(Image.open(g[-1]).convert('RGB').resize((tw, tw), Image.LANCZOS), (x + tw, 30 + i * tw))
    path = os.path.join(OUT, 'compare_sheet.jpg')
    sheet.save(path, quality=90)
    print(path)


if __name__ == '__main__':
    import sys
    compare() if '--compare' in sys.argv else main()
