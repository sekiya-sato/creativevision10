# フルバージョン（opfull.py）の追加素材を作る
#   富士山の夜景: 透かしの入る右下を外して切り出し、陸（山・町・湖）／雪の笠／湖面のマスクを作る
#   四季の里: 透かしの入る下端を外して横長のパノラマに切り出す
#   ミニキャラ（Codex のペット）: スプライトシートをそのまま使う（走る・手を振る・跳ぶ）
# 出力は work/full/。元画像は Doc/promo/assets4anime（.gitignore 対象）
import os
import numpy as np
import cv2
import engine as E

OUT = os.path.join(E.WORK, 'full')
SRC_FUJI = os.path.join(E.ASSETS4, 'Gemini_Generated_Image_6khp936khp936khp.png')
SRC_SEASONS = os.path.join(E.ASSETS4, 'Gemini_Generated_Image_v1ddzdv1ddzdv1dd.png')
PET = os.path.join(E.ASSETS4, 'codex-pets-2026-06', 'magicgirl', 'spritesheet.webp')

# 生成画像の透かし（右下 x≈2460..2570, y≈1245..1345）を画面に出さない切り出し範囲
FUJI_CROP = (0, 0, 2400, 1536)
SEASONS_CROP = (0, 0, 2752, 1240)


def fuji_masks(img):
    """陸（R）・雪の笠（G）・湖面（B）。GrabCut を 1/4 で解き、列ごとに稜線から下を陸にする"""
    H, W = img.shape[:2]
    sm = cv2.resize(img, (W // 4, H // 4), interpolation=cv2.INTER_AREA)
    h, w = sm.shape[:2]
    m = np.full((h, w), cv2.GC_PR_BGD, np.uint8)
    m[:int(h * 0.45)] = cv2.GC_BGD
    m[int(h * 0.62):int(h * 0.80)] = cv2.GC_PR_FGD
    m[int(h * 0.80):] = cv2.GC_FGD
    bg = np.zeros((1, 65)); fg = np.zeros((1, 65))
    cv2.grabCut(sm, m, None, bg, fg, 8, cv2.GC_INIT_WITH_MASK)
    land = ((m == 1) | (m == 3)).astype(np.uint8)
    # 雪の笠: 山頂の三角形（斜面に沿う）の中の明るく彩度の低い画素（GrabCut は空に含めてしまう）
    hsv = cv2.cvtColor(sm, cv2.COLOR_BGR2HSV)
    yy, xx = np.mgrid[0:h, 0:w]
    tri = np.zeros((h, w), np.uint8)
    ax, ay = 0.579 * w, 0.482 * h          # 山頂
    cv2.fillPoly(tri, [np.array([[ax, ay - 2], [0.433 * w, 0.622 * h], [0.733 * w, 0.622 * h]], np.int32)], 1)
    snow = ((hsv[..., 2] > 140) & (hsv[..., 1] < 110) & (tri > 0)).astype(np.uint8)
    snow = cv2.morphologyEx(snow, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
    land = np.maximum(land, snow)
    land = cv2.morphologyEx(land, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    # 列ごとに最初の陸から下はすべて陸（星や町明かりの穴をなくす）
    first = np.where(land.any(0), land.argmax(0), h)
    first = cv2.medianBlur(first.astype(np.float32).reshape(1, -1), 5).ravel()
    land = (yy >= first[None, :]).astype(np.float32)
    # 雪の笠は稜線の下に限る
    snow = snow.astype(np.float32) * land
    # 湖面: 下の帯のうち暗すぎない（森ではない）なめらかな画素
    v = hsv[..., 2].astype(np.float32)
    var = cv2.blur(v * v, (5, 5)) - cv2.blur(v, (5, 5)) ** 2
    lake = ((yy > h * 0.80) & (yy < h * 0.965) & (xx > w * 0.14) & (xx < w * 0.86) & (v > 38) & (var < 900)).astype(np.uint8)
    lake = cv2.morphologyEx(lake, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
    lake = cv2.morphologyEx(lake, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8)).astype(np.float32)
    up = lambda a, k: cv2.GaussianBlur(cv2.resize(a, (W, H), interpolation=cv2.INTER_LINEAR), (0, 0), k)
    return np.clip(np.stack([up(land, 2.0), up(snow, 3.0), up(lake, 6.0)], -1), 0, 1)


def build(force=False):
    os.makedirs(OUT, exist_ok=True)
    f_img, f_msk = os.path.join(OUT, 'fuji.png'), os.path.join(OUT, 'fuji_mask.png')
    if force or not os.path.exists(f_msk):
        x0, y0, x1, y1 = FUJI_CROP
        img = cv2.imread(SRC_FUJI)[y0:y1, x0:x1]
        cv2.imwrite(f_img, img)
        msk = fuji_masks(img)
        cv2.imwrite(f_msk, (msk[..., ::-1] * 255).astype(np.uint8))
        vis = img.astype(np.float32)
        vis = vis * (1 - 0.5 * msk[..., :1]) + np.array([0, 0, 255.0]) * 0.5 * msk[..., :1]
        vis[..., 1] += 160 * msk[..., 1]; vis[..., 0] += 200 * msk[..., 2]
        cv2.imwrite(os.path.join(OUT, 'check_fuji_mask.jpg'), cv2.resize(np.clip(vis, 0, 255).astype(np.uint8), (1200, 768)))
    s_img = os.path.join(OUT, 'seasons.png')
    if force or not os.path.exists(s_img):
        x0, y0, x1, y1 = SEASONS_CROP
        cv2.imwrite(s_img, cv2.imread(SRC_SEASONS)[y0:y1, x0:x1])
    return dict(fuji=f_img, fuji_mask=f_msk, seasons=s_img, pet=PET)


if __name__ == '__main__':
    import sys
    print(build(force='--force' in sys.argv))
