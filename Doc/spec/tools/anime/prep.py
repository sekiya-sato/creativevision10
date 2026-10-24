# 主人公イラストからリグ用の素材（切り抜き・背景の穴埋め・パーツ重み・口の差分）を作る
# 出力: Doc/promo/anime/work/rig/*.png（rig.py が読み込む）
import os, sys
import numpy as np, cv2
from scipy.ndimage import label as ndimage_label
import engine as E

SRC = E.ASSETS4
OUT = os.path.join(E.WORK, 'rig')
os.makedirs(OUT, exist_ok=True)
S = 2048


def load(name):
    im = cv2.imread(os.path.join(SRC, name), cv2.IMREAD_COLOR)
    if im.shape[0] != S:
        im = cv2.resize(im, (S, S), interpolation=cv2.INTER_LANCZOS4)
        # 1024 の素材は拡大で線がぼけるため軽く鮮鋭化
        bl = cv2.GaussianBlur(im, (0, 0), 1.6)
        im = cv2.addWeighted(im, 1.6, bl, -0.6, 0)
    return im


def poly(pts, val=255):
    m = np.zeros((S, S), np.uint8)
    cv2.fillPoly(m, [np.array(pts, np.int32)], val)
    return m


def soft(m, r):
    m = m.astype(np.float32) / 255 if m.dtype == np.uint8 else m
    return cv2.GaussianBlur(m, (0, 0), r) if r > 0 else m


def pushpull(img, known):
    """known=0 の画素を周囲の色でなめらかに埋める（ピラミッド押し上げ・引き下げ）"""
    img = img.astype(np.float32); w = known.astype(np.float32)
    levels = []
    ci, cw = img * w[..., None], w
    while min(cw.shape) > 4:
        levels.append((ci, cw))
        ci = cv2.resize(ci, (ci.shape[1] // 2, ci.shape[0] // 2), interpolation=cv2.INTER_AREA)
        cw = cv2.resize(cw, (cw.shape[1] // 2, cw.shape[0] // 2), interpolation=cv2.INTER_AREA)
    cur = ci / np.maximum(cw, 1e-6)[..., None]
    for ci, cw in reversed(levels):
        up = cv2.resize(cur, (ci.shape[1], ci.shape[0]), interpolation=cv2.INTER_LINEAR)
        a = np.clip(cw * 4, 0, 1)[..., None]
        cur = np.where(cw[..., None] > 1e-4, ci / np.maximum(cw, 1e-6)[..., None], up) * a + up * (1 - a)
    return cur


def cutout(N):
    m = np.full((S, S), cv2.GC_BGD, np.uint8)
    probable = [(0, 800), (280, 450), (330, 430), (430, 120), (700, 40), (900, 10), (1300, 40), (1550, 120),
                (1830, 60), (1840, 160), (1700, 300), (2010, 520), (2010, 620), (1650, 780), (1780, 1000),
                (1840, 1300), (1820, 1780), (1500, 1840), (1300, 1840), (900, 1790), (450, 1780), (400, 1400),
                (440, 1000), (150, 970)]
    cv2.fillPoly(m, [np.array(probable, np.int32)], cv2.GC_PR_FGD)
    sure = [[(800, 500), (1300, 450), (1500, 700), (1400, 1150), (1100, 1250), (800, 1150), (700, 800)],
            [(1100, 1300), (1650, 1350), (1650, 1650), (1100, 1600)],
            [(300, 700), (700, 500), (800, 700), (300, 800)],
            [(1100, 250), (1400, 250), (1400, 400), (1100, 400)],
            [(560, 200), (660, 160), (680, 400), (560, 420)],
            [(1440, 940), (1480, 940), (1495, 1010), (1460, 1070), (1430, 1010)],
            [(780, 1550), (830, 1550), (830, 1590), (780, 1590)],
            [(1310, 1660), (1360, 1660), (1360, 1720), (1310, 1720)],
            [(1580, 120), (1740, 90), (1700, 180), (1600, 200)]]
    for p in sure:
        cv2.fillPoly(m, [np.array(p, np.int32)], cv2.GC_FGD)
    bgd = np.zeros((1, 65)); fgd = np.zeros((1, 65))
    ms = cv2.resize(m, None, fx=.5, fy=.5, interpolation=cv2.INTER_NEAREST)
    cv2.grabCut(cv2.resize(N, None, fx=.5, fy=.5, interpolation=cv2.INTER_AREA), ms, None, bgd, fgd, 6, cv2.GC_INIT_WITH_MASK)
    fg = np.where((ms == cv2.GC_FGD) | (ms == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
    # 小さな島を除き、穴を埋める
    n, lab, st, _ = cv2.connectedComponentsWithStats(fg)
    keep = 1 + np.argmax(st[1:, cv2.CC_STAT_AREA])
    fg = np.where(lab == keep, 255, 0).astype(np.uint8)
    inv = 255 - fg
    n, lab, st, _ = cv2.connectedComponentsWithStats(inv)
    for i in range(1, n):
        if st[i, cv2.CC_STAT_AREA] < 4000:
            fg[lab == i] = 255
    fg = cv2.resize(fg, (S, S), interpolation=cv2.INTER_LINEAR)
    # 帽子の頂上と先端の巻きは背景と誤判定されやすいので、暗い紫と線で上部を補う
    hh, ss, vv = hsv(N)
    yy = np.mgrid[0:S, 0:S][0]
    pd = ((((hh > 235) & (hh < 305) & (vv < 0.62)) | (vv < 0.22)) & (yy < 330)).astype(np.uint8) * 255
    pd = cv2.morphologyEx(pd, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
    n, lab, st, _ = cv2.connectedComponentsWithStats(pd)
    core = cv2.erode(fg, np.ones((9, 9), np.uint8)) > 128
    for i in range(1, n):
        if st[i, cv2.CC_STAT_AREA] > 300 and core[lab == i].any():
            fg[lab == i] = 255
    cnts, _ = cv2.findContours((fg > 128).astype(np.uint8) * 255, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    big = max(cnts, key=cv2.contourArea)
    fg = np.zeros_like(fg); cv2.drawContours(fg, [big], -1, 255, -1)
    # 高解像度で輪郭を元画像の線に合わせ直す（ガイド付きの軽い平滑化）
    fg = cv2.GaussianBlur(fg, (0, 0), 1.2)
    return fix_hair(fg, N)


def fix_hair(fg, N):
    """左右の髪の切り抜きを直す: 右は外側の黒い縁取りまで髪を足し、左右とも房の間から見える背景を抜く"""
    h, s, v = hsv(N)
    yy, xx = np.mgrid[0:S, 0:S]
    line = v < 0.45
    pinkish = ((h > 295) | (h < 20)) & (s > 0.07) & (v > 0.5)
    fg = fg.copy()
    zr = ((xx > 1550) & (xx < 2000) & (yy > 700) & (yy < 1330)).astype(np.uint8)
    pk = (pinkish & (zr > 0)).astype(np.uint8) * 255
    n, lab, st, _ = cv2.connectedComponentsWithStats(pk)
    core = fg > 128
    add = np.zeros_like(fg)
    for i in range(1, n):
        if st[i, cv2.CC_STAT_AREA] > 500 and core[lab == i].any():
            add[lab == i] = 255
    ring = cv2.dilate(add, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (31, 31))) & ((line & (zr > 0)).astype(np.uint8) * 255)
    add = np.maximum(add, ring)
    add = cv2.morphologyEx(add, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))) * zr
    m = np.maximum(fg, add)
    cnts, _ = cv2.findContours((m > 128).astype(np.uint8) * 255, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    filled = np.zeros_like(m)
    cv2.drawContours(filled, [max(cnts, key=cv2.contourArea)], -1, 255, -1)
    add = np.maximum(add, filled * zr)
    fg = np.maximum(fg, cv2.GaussianBlur(add, (0, 0), 1.2))
    # 房の間の隙間: 切り抜きの内側で、髪のピンクでも線でもない無彩色に近い塊（奥の画面・棚）
    zl = ((xx > 430) & (xx < 640) & (yy > 900) & (yy < 1260)) | (zr > 0)
    bg = (~pinkish) & (~line) & zl & (fg > 128)
    bg = cv2.morphologyEx(bg.astype(np.uint8) * 255, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    n, lab, st, _ = cv2.connectedComponentsWithStats(bg)
    hole = np.zeros_like(fg)
    for i in range(1, n):
        r = lab == i
        if st[i, cv2.CC_STAT_AREA] > 1500 and np.median(s[r]) < 0.12:
            hole[r] = 255
    hole = cv2.dilate(hole, np.ones((3, 3), np.uint8))
    return np.minimum(fg, 255 - cv2.GaussianBlur(hole, (0, 0), 1.0))


def hsv(img):
    h = cv2.cvtColor(img, cv2.COLOR_BGR2HSV_FULL).astype(np.float32)
    return h[..., 0] * 360 / 255, h[..., 1] / 255, h[..., 2] / 255


def main():
    N = load('github-logo-gemini-normal.png')
    HP = load('github-logo-gemini-happy.png')
    ST = load('github-logo-gemini-stressful.png')
    HT = load('github-logo-gemini-hightention.jpg')
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)

    cut_path = os.path.join(OUT, 'cut.png')
    if os.path.exists(cut_path) and '--recut' not in sys.argv:
        cut = cv2.imread(cut_path, 0)
    else:
        cut = cutout(N)
        cv2.imwrite(cut_path, cut)
    cutf = cut.astype(np.float32) / 255

    # ---- 色分類（髪のピンク・帽子の紫・リボンの橙）
    h, s, v = hsv(N)
    pink = ((h > 300) | (h < 12)) & (s > 0.18) & (v > 0.55)
    purple = (h > 245) & (h < 300) & (s > 0.25)
    orange = (h > 18) & (h < 50) & (s > 0.45) & (v > 0.6)
    dark = v < 0.35
    ker = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))

    def close(m, k=9):
        return cv2.morphologyEx(m.astype(np.uint8) * 255, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k)))

    # 帽子: 上部の紫・橙・線（髪の生え際より上）
    hat_zone = poly([(0, 760), (0, 830), (470, 985), (520, 700), (700, 560), (900, 520), (1150, 520), (1400, 560),
                     (1560, 640), (1700, 760), (2048, 560), (2048, 0), (0, 0)])
    hat = close((purple | orange | dark) & (hat_zone > 0) & (cut > 128), 15)
    hat = cv2.morphologyEx(hat, cv2.MORPH_OPEN, ker)
    bow = close(orange & (yy < 640) & (cut > 128), 21)
    bow = cv2.dilate(bow, ker)

    hair = close(pink & (cut > 128), 11)
    hair = cv2.morphologyEx(hair, cv2.MORPH_OPEN, ker)
    # 頬の赤み・指先のピンクは髪から外す
    face_low = poly([(740, 880), (1000, 820), (1300, 790), (1390, 900), (1300, 1150), (1110, 1245), (900, 1205), (760, 1070)])

    # 体（上着・襟・ブローチ）と手
    body_zone = poly([(820, 1240), (1000, 1200), (1150, 1150), (1290, 1130), (1380, 1200), (1450, 1290), (1700, 1250),
                      (1850, 1500), (1850, 1900), (760, 1900), (760, 1500), (840, 1400)])
    handL = poly([(460, 1470), (560, 1440), (700, 1470), (800, 1520), (850, 1560), (860, 1640), (820, 1700),
                  (700, 1760), (560, 1760), (470, 1640)])
    handR = poly([(860, 1590), (980, 1560), (1150, 1580), (1300, 1630), (1400, 1650), (1400, 1760),
                  (1250, 1800), (1100, 1800), (960, 1820), (860, 1720)])
    hands = np.maximum(handL, handR)
    hair[(face_low > 0) | (cv2.dilate(hands, ker) > 0)] = 0
    body = ((body_zone > 0) & (cut > 128) & (hair == 0) & (hands == 0)).astype(np.uint8) * 255
    earring = poly([(1425, 925), (1490, 925), (1505, 1010), (1462, 1085), (1420, 1010)])

    head = ((cut > 128) & (body == 0) & (hands == 0)).astype(np.uint8) * 255
    # 頭の重み: 首元で 0 へなだらかに落とし、髪の毛先は 1 のまま
    headw = soft(head, 6)
    neck_fall = np.clip((1260 - yy) / 120, 0, 1)
    headw = np.maximum(headw * np.where(hair > 0, 1, neck_fall), soft(hair, 4) * (cutf > 0.5))
    headw = soft((headw * 255).astype(np.uint8), 10)

    # 髪の揺れ（下へ行くほど大きい）。左右の房に分ける
    hairs = soft(hair, 5)
    sway = np.clip((yy - 860) / 520, 0, 1) ** 1.4
    hairL = hairs * sway * (xx < 900) * (yy > 880)
    hairR = hairs * sway * (xx > 1300) * (yy > 820)
    hairL = soft(hairL, 12); hairR = soft(hairR, 12)
    # 前髪（顔の上の房）は小さく揺らす
    bang = hairs * soft(poly([(700, 560), (1500, 560), (1450, 900), (1300, 820), (1050, 760), (850, 860), (720, 900)]), 45)
    bang = soft(bang, 6)

    face = soft(poly([(730, 800), (900, 700), (1150, 660), (1320, 700), (1350, 900), (1260, 1140), (1110, 1230),
                      (920, 1190), (770, 1060)]), 18)

    # ---- 口: 通常の閉じた口の線、ハッピーの開いた口
    def mouth_mask(img, box, thr_v=0.62):
        x0, y0, x1, y1 = box
        hh, ss, vv = hsv(img)
        skin = (hh > 5) & (hh < 40) & (ss < 0.32) & (vv > 0.82)
        m = (~skin) & (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)
        m = m.astype(np.uint8) * 255
        m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)))
        n, lab, st, _ = cv2.connectedComponentsWithStats(m)
        keep = 1 + np.argmax(st[1:, cv2.CC_STAT_AREA])
        m = np.where(lab == keep, 255, 0).astype(np.uint8)
        # 内側の穴（歯の白）を埋める
        cnts, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        cv2.drawContours(m, cnts, -1, 255, -1)
        return m

    mouthN = mouth_mask(N, (1030, 1090, 1130, 1120))
    mouthHP = mouth_mask(HP, (995, 1030, 1180, 1170))
    mouthST = mouth_mask(ST, (1020, 1080, 1140, 1135))
    mouthHT = mouth_mask(HT, (980, 1010, 1200, 1190))

    def remove_mouth(img, m):
        k = cv2.dilate(m, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (13, 13)))
        x0, y0 = 940, 980
        sub = img[y0:y0 + 260, x0:x0 + 300].copy()
        sm = k[y0:y0 + 260, x0:x0 + 300]
        fill = cv2.inpaint(sub, sm, 9, cv2.INPAINT_TELEA)
        fill = cv2.GaussianBlur(fill, (0, 0), 2.0)
        a = (cv2.GaussianBlur(sm, (0, 0), 2.5).astype(np.float32) / 255)[..., None]
        out = img.copy()
        out[y0:y0 + 260, x0:x0 + 300] = (sub * (1 - a) + fill * a).astype(np.uint8)
        return out

    N_nm = remove_mouth(N, mouthN)
    HP_nm = remove_mouth(HP, mouthHP)
    ST_nm = remove_mouth(ST, mouthST)

    # ---- 目: 目の領域（まつげ〜下まぶた）を測り、目を消した肌プレートと列ごとの上端・下端・まつげ幅の表を作る
    eye_polys = [[(748, 880), (790, 852), (845, 846), (905, 852), (948, 885), (945, 960), (920, 1000), (860, 1008),
                  (800, 990), (775, 940), (748, 905)],
                 [(1078, 815), (1120, 772), (1180, 752), (1250, 752), (1296, 745), (1300, 800), (1294, 880),
                  (1240, 925), (1170, 930), (1110, 905), (1080, 850)]]
    eyem = np.zeros((S, S), np.uint8)
    for img in (N, HP):
        hh, ss, vv = hsv(img)
        skin = (hh > 4) & (hh < 45) & (ss < 0.36) & (vv > 0.78)
        for pp in eye_polys:
            m = (~skin) & (poly(pp) > 0)
            m = cv2.morphologyEx(m.astype(np.uint8) * 255, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (11, 11)))
            n, lab, st, _ = cv2.connectedComponentsWithStats(m)
            keep = 1 + np.argmax(st[1:, cv2.CC_STAT_AREA])
            eyem |= np.where(lab == keep, 255, 0).astype(np.uint8)
    cnts, _ = cv2.findContours(eyem, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    cv2.drawContours(eyem, cnts, -1, 255, -1)
    lut = np.zeros((1, S, 4), np.float32)
    vN = hsv(N)[2]
    for x in range(S):
        ys = np.where(eyem[:, x] > 0)[0]
        if len(ys) < 6:
            continue
        top, bot = ys[0], ys[-1]
        col = vN[top:min(bot + 1, top + 70), x] < 0.40
        # 上から 70px 内で最も長い暗部の連なりをまつげの帯とみなす（上に掛かる髪の細線は除く）
        best, bs, i = 0, 0, 0
        while i < len(col):
            if col[i]:
                j = i
                while j < len(col) and col[j]:
                    j += 1
                if j - i > best:
                    best, bs = j - i, i
                i = j
            else:
                i += 1
        if best >= 6:
            top = top + max(bs - 2, 0)
            T = best + 5
        else:
            T = 12
        lut[0, x] = (top, bot, max(8, min(T, 44)), 1)
    # 目ごとに上端・下端を4次式で近似し、まつげ幅は中央値にする（測定のばらつきで線がガタつかないように）
    valid = lut[0, :, 3] > 0
    xs_all = np.arange(S)
    lab_cols, nlab = ndimage_label(valid)
    for k in range(1, nlab + 1):
        xs = xs_all[lab_cols == k]
        if len(xs) < 20:
            valid[xs] = False; lut[0, xs] = 0
            continue
        core = xs[(xs > xs[0] + 6) & (xs < xs[-1] - 6)]
        pt = np.polyfit(core, lut[0, core, 0], 4); pb = np.polyfit(core, lut[0, core, 1], 4)
        Tm = float(np.median(lut[0, core, 2]))
        lut[0, xs, 0] = np.polyval(pt, xs) - 3
        lut[0, xs, 1] = np.polyval(pb, xs) + 8
        lut[0, xs, 2] = Tm + 1
    # w: 目の両端で 0 へ落とす重み（端の列での段差を防ぐ）
    dist = cv2.distanceTransform(np.pad(valid.astype(np.uint8), 1)[None].repeat(3, 0), cv2.DIST_L2, 3)[1, 1:-1]
    lut[0, :, 3] = (0.55 + 0.45 * np.clip(dist / 22.0, 0, 1)) * valid
    np.save(os.path.join(OUT, 'eye_lut.npy'), lut)
    # 目を消した肌プレート: 目の真上の肌色を列ごとに取り、縦に伸ばして塗る（下の頬紅を拾わない）
    ek = cv2.dilate(eyem, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15)))
    for name, img in (('N', N_nm), ('HP', HP_nm), ('ST', ST_nm)):
        hh, ss, vv = hsv(img)
        skin = (hh > 0) & (hh < 50) & (ss < 0.45) & (vv > 0.5)
        if name == 'ST':
            skin = vv > 0.45   # ストレス顔は顔色が灰色なので線だけ除く
        # 周囲の肌（目の下の頬紅側は除く）からピラミッドでなめらかに埋める
        below = np.zeros((S, S), bool)
        for x in range(700, 1350):
            ys = np.where(ek[:, x] > 0)[0]
            if len(ys):
                below[ys[-1] - 10:ys[-1] + 70, x] = True
        x0, y0, x1, y1 = 680, 680, 1380, 1100
        known = (skin & (ek == 0) & ~below)[y0:y1, x0:x1]
        known = cv2.erode(known.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(np.float32)
        sub = img[y0:y1, x0:x1].astype(np.float32)
        fill = pushpull(sub, known)
        fill = cv2.GaussianBlur(fill, (0, 0), 4)
        out = img.astype(np.float32).copy()
        a = (cv2.GaussianBlur(ek, (0, 0), 3).astype(np.float32) / 255)[y0:y1, x0:x1, None]
        out[y0:y1, x0:x1] = sub * (1 - a) + fill * a
        cv2.imwrite(os.path.join(OUT, f'img_{name}e.png'), np.clip(out, 0, 255).astype(np.uint8))
    vis = N.copy(); vis[eyem > 0] = vis[eyem > 0] // 2 + np.array([0, 120, 0], np.uint8)
    cv2.imwrite(os.path.join(OUT, 'check_eyes.jpg'), vis[720:1060, 700:1340])

    # ---- 背景の穴埋め（キャラの後ろ）
    k = cv2.dilate(cut, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (25, 25)))
    for name, img in (('N', N), ('HP', HP), ('ST', ST)):
        small = cv2.resize(img, (1024, 1024), interpolation=cv2.INTER_AREA)
        ks = cv2.resize(k, (1024, 1024), interpolation=cv2.INTER_NEAREST)
        fill = pushpull(small, (ks < 128).astype(np.float32))
        fill = cv2.resize(fill, (S, S), interpolation=cv2.INTER_CUBIC)
        a = (cv2.GaussianBlur(k, (0, 0), 3).astype(np.float32) / 255)[..., None]
        plate = img * (1 - a) + fill * a
        cv2.imwrite(os.path.join(OUT, f'plate_{name}.png'), np.clip(plate, 0, 255).astype(np.uint8))

    for name, img in (('N', N_nm), ('HP', HP_nm), ('ST', ST_nm), ('HT', HT), ('Nm', N), ('HPm', HP), ('STm', ST)):
        cv2.imwrite(os.path.join(OUT, f'img_{name}.png'), img)

    def pack(path, *chs):
        arr = np.stack([np.clip(c if c.dtype != np.uint8 else c.astype(np.float32) / 255, 0, 1) for c in chs], -1)
        # OpenCV は BGRA の順で書く
        arr = arr[..., [2, 1, 0, 3]]
        cv2.imwrite(path, (arr * 65535).astype(np.uint16))

    pack(os.path.join(OUT, 'm1.png'), soft(cut, 0.6), headw, soft(hat, 8), soft(bow, 8))
    pack(os.path.join(OUT, 'm2.png'), hairL, hairR, soft(earring, 3), soft(body, 10))
    pack(os.path.join(OUT, 'm3.png'), soft(handL, 8), soft(handR, 8), face, bang)
    pack(os.path.join(OUT, 'm4.png'), soft(mouthN, 0.8), soft(mouthHP, 0.8), soft(mouthST, 0.8), soft(mouthHT, 0.8))

    # ---- 確認用
    vis = N.astype(np.float32) * 0.45
    cols = [(hat, (255, 80, 200)), (bow, (0, 160, 255)), (hair, (180, 120, 255)), (body, (255, 200, 0)),
            (hands, (0, 255, 120)), (earring, (0, 255, 255))]
    for m, c in cols:
        a = (m.astype(np.float32) / 255)[..., None] * 0.45
        vis = vis * (1 - a) + np.array(c, np.float32) * a
    cv2.imwrite(os.path.join(OUT, 'check_parts.jpg'), cv2.resize(np.clip(vis, 0, 255).astype(np.uint8), (1024, 1024)))
    w = np.stack([headw, hairL + hairR + bang, face], -1)[..., ::-1]
    cv2.imwrite(os.path.join(OUT, 'check_weights.jpg'), cv2.resize((np.clip(w, 0, 1) * 255).astype(np.uint8), (1024, 1024)))
    mm = np.stack([mouthN, mouthHP, mouthHT], -1)
    cv2.imwrite(os.path.join(OUT, 'check_mouth.jpg'), mm[960:1220, 940:1240])
    print('ok', OUT)


if __name__ == '__main__':
    main()
