# 背景なしの主人公（onlyperson の6表情）と立ち絵から、リグ用の素材を作る
# 出力: work/rig_p/（rig_p.Character が読む）、work/stand/（rig_p.Standee が読む）
#
# onlyperson は旧リグの一枚絵（github-logo-gemini-normal.png）の描き直しで、顔の位置がほぼ一致する。
# 旧リグの座標（2048px の一枚絵の画素）をそのまま使えるよう、onlyperson をその座標系へ合わせて置く。
# 帽子の上端が一枚絵の外（y<0）へはみ出すため、キャンバスは原点 ORG・一辺 SZ の正方形にする。
#   python prep_person.py            作成（作成済みなら飛ばす）
#   python prep_person.py --force    作り直す
import os, sys, json
import numpy as np, cv2
from scipy.ndimage import label as ndimage_label
import engine as E
import prep as P0

SRC = E.ASSETS4
OUT = os.path.join(E.WORK, 'rig_p')
OUT_ST = os.path.join(E.WORK, 'stand')
ORG = (-64.0, -320.0)          # キャンバス左上の旧リグ座標
SZ = 2400                      # キャンバスの一辺（旧リグ座標で 1px = 1 画素）
EXPR = [('N', 'onlyperson'), ('HP', 'onlyperson-smile-open'), ('ST', 'onlyperson-stressful'),
        ('HT', 'onlyperson-hightention'), ('CE', 'onlyperson-smile-closedeyes'), ('WR', 'onlyperson-worried')]
STANDS = ['fullbody', 'fullbody-back', 'fullbody-smile', 'fullbody3d', 'fullbody3d-back']


def rgba(name):
    return cv2.imread(os.path.join(SRC, f'github-logo-{name}.png'), cv2.IMREAD_UNCHANGED)


def gray(img):
    return cv2.cvtColor(img[..., :3], cv2.COLOR_BGR2GRAY)


def register(src, dst, src_mask=None, partial=True):
    """src→dst のアフィン（SIFT＋RANSAC）"""
    sift = cv2.SIFT_create(8000)
    ks, ds = sift.detectAndCompute(gray(src), src_mask)
    kd, dd = sift.detectAndCompute(gray(dst), None)
    m = cv2.BFMatcher().knnMatch(ds, dd, k=2)
    g = [a for a, b in m if a.distance < 0.75 * b.distance]
    ps = np.float32([ks[a.queryIdx].pt for a in g]); pd = np.float32([kd[a.trainIdx].pt for a in g])
    f = cv2.estimateAffinePartial2D if partial else cv2.estimateAffine2D
    M, inl = f(ps, pd, ransacReprojThreshold=4)
    return M, int(inl.sum())


def to_canvas_affine():
    """onlyperson の画素 → キャンバスの画素。旧一枚絵と特徴点で合わせ、顔は ECC で詰める"""
    N0 = cv2.imread(os.path.join(SRC, 'github-logo-gemini-normal.png'))
    op = rgba('onlyperson')
    M, n = register(op, N0, (op[..., 3] > 200).astype(np.uint8))
    # 顔（目・口・輪郭）を優先して合わせ直す: 旧リグの目・口の座標をそのまま使うため
    x0, y0, x1, y1 = 650, 700, 1500, 1250
    w = cv2.warpAffine(op, M, (2048, 2048), flags=cv2.INTER_LINEAR)
    ga = gray(N0)[y0:y1, x0:x1].astype(np.float32); gb = gray(w)[y0:y1, x0:x1].astype(np.float32)
    wm = np.eye(2, 3, dtype=np.float32)
    _, wm = cv2.findTransformECC(ga, gb, wm, cv2.MOTION_AFFINE, (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 200, 1e-6), None, 5)
    # wm: 旧の顔の画素 → 合わせた onlyperson の画素（どちらも切り出し原点）。逆にして M に重ねる
    A = np.vstack([wm, [0, 0, 1]]).astype(np.float64)
    T = np.array([[1, 0, x0], [0, 1, y0], [0, 0, 1.0]])
    corr = T @ np.linalg.inv(A) @ np.linalg.inv(T)
    Mf = corr @ np.vstack([M, [0, 0, 1]])
    Mf = np.array([[1, 0, -ORG[0]], [0, 1, -ORG[1]], [0, 0, 1.0]]) @ Mf
    print('register onlyperson', n, 'inliers', np.round(Mf[:2], 4).tolist())
    return Mf[:2]


def warp_rgba(img, M, size):
    """前乗算して変形し、ストレートαへ戻す（αの外の色を引き込まない）"""
    a = img[..., 3:4].astype(np.float32) / 255
    pm = np.concatenate([img[..., :3].astype(np.float32) * a, a * 255], -1)
    w = cv2.warpAffine(pm, M, size, flags=cv2.INTER_LANCZOS4, borderValue=0)
    al = np.clip(w[..., 3:4] / 255, 0, 1)
    rgb = np.where(al > 1e-3, w[..., :3] / np.maximum(al, 1e-3), 0)
    return np.clip(rgb, 0, 255).astype(np.uint8), (al[..., 0] * 255).astype(np.uint8)


def sharpen(im, r=1.4, k=0.5):
    bl = cv2.GaussianBlur(im, (0, 0), r)
    return cv2.addWeighted(im, 1 + k, bl, -k, 0)


def done(d, names):
    return all(os.path.exists(os.path.join(d, n)) for n in names) and '--force' not in sys.argv


# ---------------------------------------------------------------- onlyperson のリグ
def build_rig():
    os.makedirs(OUT, exist_ok=True)
    if done(OUT, ['rig.json', 'm1.png', 'm4.png', 'eye_lut.npy']):
        return OUT
    M = to_canvas_affine()
    base = rgba('onlyperson')
    imgs, alphas = {}, {}
    for key, name in EXPR:
        src = rgba(name)
        Mk = M
        if key == 'HT':
            # ハイテンションは描き直しで位置がずれているため、顔以外（帽子・服・手）で onlyperson へ合わせる
            fm = (src[..., 3] > 200).astype(np.uint8)
            fm[330:820, 380:900] = 0
            Mh, n = register(src, base, fm, partial=False)
            print('register HT', n, 'inliers')
            Mk = (np.vstack([M, [0, 0, 1]]) @ np.vstack([Mh, [0, 0, 1]]))[:2]
        rgb, al = warp_rgba(src, Mk, (SZ, SZ))
        imgs[key] = sharpen(rgb)
        alphas[key] = al
    cut = alphas['N']
    cv2.imwrite(os.path.join(OUT, 'cut.png'), cut)
    S = SZ
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    yy += ORG[1]; xx += ORG[0]          # 以降の座標は旧リグ座標

    def poly(pts, val=255):
        m = np.zeros((S, S), np.uint8)
        cv2.fillPoly(m, [np.array([(x - ORG[0], y - ORG[1]) for x, y in pts], np.int32)], val)
        return m

    soft = P0.soft
    hsv = P0.hsv
    N = imgs['N']
    cutf = cut.astype(np.float32) / 255
    ker = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))

    def close(m, k=9):
        return cv2.morphologyEx(m.astype(np.uint8) * 255, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k)))

    # ---- パーツ（色分類と領域。旧リグの prep.py と同じ考え方）
    h, s, v = hsv(N)
    inside = cut > 128
    pink = ((h > 300) | (h < 12)) & (s > 0.18) & (v > 0.55)
    purple = (h > 245) & (h < 300) & (s > 0.25)
    orange = (h > 18) & (h < 50) & (s > 0.45) & (v > 0.6)
    dark = v < 0.35
    hat_zone = poly([(-100, 760), (-100, 830), (470, 985), (520, 700), (700, 560), (900, 520), (1150, 520), (1400, 560),
                     (1560, 640), (1700, 760), (2300, 560), (2300, -400), (-100, -400)])
    hat = close((purple | orange | dark) & (hat_zone > 0) & inside, 15)
    hat = cv2.morphologyEx(hat, cv2.MORPH_OPEN, ker)
    bow = close(orange & (yy < 640) & inside, 21)
    bow = cv2.dilate(bow, ker)
    hair = close(pink & inside, 11)
    hair = cv2.morphologyEx(hair, cv2.MORPH_OPEN, ker)
    face_low = poly([(740, 880), (1000, 820), (1300, 790), (1390, 900), (1300, 1150), (1110, 1245), (900, 1205), (760, 1070)])
    body_zone = poly([(820, 1240), (1000, 1200), (1150, 1150), (1290, 1130), (1380, 1200), (1450, 1290), (1700, 1250),
                      (2100, 1500), (2100, 2100), (700, 2100), (700, 1500), (840, 1400)])
    # 手（onlyperson はキーボードの上に浮かせた手。左手は袖口から先、右手は手前）
    handL = poly([(330, 1610), (480, 1580), (640, 1590), (790, 1610), (830, 1660), (800, 1760), (640, 1820), (430, 1800),
                  (330, 1700)])
    handR = poly([(760, 1640), (900, 1600), (1080, 1620), (1250, 1690), (1380, 1720), (1400, 1880), (1180, 1930),
                  (960, 1940), (780, 1860)])
    hands = np.maximum(handL, handR)
    hair[(face_low > 0) | (cv2.dilate(hands, ker) > 0)] = 0
    body = ((body_zone > 0) & inside & (hair == 0) & (hands == 0)).astype(np.uint8) * 255
    earring = poly([(1425, 925), (1500, 925), (1515, 1030), (1462, 1105), (1415, 1030)])
    head = (inside & (body == 0) & (hands == 0)).astype(np.uint8) * 255
    headw = soft(head, 6)
    neck_fall = np.clip((1260 - yy) / 120, 0, 1)
    headw = np.maximum(headw * np.where(hair > 0, 1, neck_fall), soft(hair, 4) * (cutf > 0.5))
    headw = soft((headw * 255).astype(np.uint8), 10)
    hairs = soft(hair, 5)
    sway = np.clip((yy - 860) / 520, 0, 1) ** 1.4
    hairL = soft(hairs * sway * (xx < 900) * (yy > 880), 12)
    hairR = soft(hairs * sway * (xx > 1300) * (yy > 820), 12)
    bang = soft(hairs * soft(poly([(700, 560), (1500, 560), (1450, 900), (1300, 820), (1050, 760), (850, 860), (720, 900)]), 45), 6)
    face = soft(poly([(730, 800), (900, 700), (1150, 660), (1320, 700), (1350, 900), (1260, 1140), (1110, 1230),
                      (920, 1190), (770, 1060)]), 18)

    # ---- 口: 閉じ口は表情ごとに測って和集合を作り、開いた口はハッピー（smile-open）から取る
    def mouth_mask(img, box, holes=True):
        x0, y0, x1, y1 = box
        hh, ss, vv = hsv(img)
        skin = (hh > 5) & (hh < 40) & (ss < 0.32) & (vv > 0.82)
        m = ((~skin) & (xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)).astype(np.uint8) * 255
        m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)))
        n, lab, st, _ = cv2.connectedComponentsWithStats(m)
        if n < 2:
            return np.zeros_like(m)
        keep = 1 + np.argmax(st[1:, cv2.CC_STAT_AREA])
        m = np.where(lab == keep, 255, 0).astype(np.uint8)
        if holes:
            cnts, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
            cv2.drawContours(m, cnts, -1, 255, -1)
        return m

    mouths = {k: mouth_mask(imgs[k], (1010, 1060, 1160, 1135)) for k in ('N', 'ST', 'WR', 'CE')}
    mouthHP = mouth_mask(imgs['HP'], (980, 1010, 1200, 1190))
    closed = np.zeros((S, S), np.uint8)
    for m in mouths.values():
        closed = np.maximum(closed, m)
    closed = cv2.dilate(closed, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)))

    def remove_mouth(img, m):
        k = cv2.dilate(m, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (13, 13)))
        x0, y0 = int(940 - ORG[0]), int(980 - ORG[1])
        sub = img[y0:y0 + 260, x0:x0 + 300].copy()
        sm = k[y0:y0 + 260, x0:x0 + 300]
        fill = cv2.inpaint(sub, sm, 9, cv2.INPAINT_TELEA)
        fill = cv2.GaussianBlur(fill, (0, 0), 2.0)
        a = (cv2.GaussianBlur(sm, (0, 0), 2.5).astype(np.float32) / 255)[..., None]
        out = img.copy()
        out[y0:y0 + 260, x0:x0 + 300] = (sub * (1 - a) + fill * a).astype(np.uint8)
        return out

    nm = {k: remove_mouth(imgs[k], closed) for k in ('N', 'ST', 'WR', 'CE')}
    nm['HP'] = remove_mouth(imgs['HP'], np.maximum(closed, mouthHP))
    # 開いた口の口角（左右の端）と、口角を結ぶ線の傾き
    ys_, xs_ = np.where(mouthHP > 0)
    il, ir = np.argmin(xs_), np.argmax(xs_)
    pl = (xs_[il] + ORG[0], ys_[il] + ORG[1]); pr = (xs_[ir] + ORG[0], ys_[ir] + ORG[1])
    mc0 = ((pl[0] + pr[0]) / 2, (pl[1] + pr[1]) / 2)
    mk0 = (pr[1] - pl[1]) / max(pr[0] - pl[0], 1)
    ys_, xs_ = np.where(mouths['N'] > 0)
    mcN = (float(xs_.mean() + ORG[0]), float(ys_.mean() + ORG[1]))

    # ---- 目（開いた目の表情: N・HP・ST・WR）
    eye_polys = [[(748, 880), (790, 852), (845, 846), (905, 852), (948, 885), (945, 960), (920, 1000), (860, 1008),
                  (800, 990), (775, 940), (748, 905)],
                 [(1078, 815), (1120, 772), (1180, 752), (1250, 752), (1296, 745), (1300, 800), (1294, 880),
                  (1240, 925), (1170, 930), (1110, 905), (1080, 850)]]
    eyem = np.zeros((S, S), np.uint8)
    for k in ('N', 'HP', 'WR'):
        hh, ss, vv = hsv(imgs[k])
        skin = (hh > 4) & (hh < 45) & (ss < 0.36) & (vv > 0.78)
        for pp in eye_polys:
            m = cv2.morphologyEx(((~skin) & (poly(pp) > 0)).astype(np.uint8) * 255, cv2.MORPH_CLOSE,
                                 cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (11, 11)))
            n, lab, st, _ = cv2.connectedComponentsWithStats(m)
            if n < 2:
                continue
            keep = 1 + np.argmax(st[1:, cv2.CC_STAT_AREA])
            eyem |= np.where(lab == keep, 255, 0).astype(np.uint8)
    cnts, _ = cv2.findContours(eyem, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    cv2.drawContours(eyem, cnts, -1, 255, -1)
    lut = np.zeros((1, S, 4), np.float32)    # 列はキャンバスの x、値は旧リグ座標の y
    vN = hsv(N)[2]
    for x in range(S):
        ys = np.where(eyem[:, x] > 0)[0]
        if len(ys) < 6:
            continue
        top, bot = ys[0], ys[-1]
        col = vN[top:min(bot + 1, top + 70), x] < 0.40
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
        lut[0, x] = (top + ORG[1], bot + ORG[1], max(8, min(T, 44)), 1)
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
    dist = cv2.distanceTransform(np.pad(valid.astype(np.uint8), 1)[None].repeat(3, 0), cv2.DIST_L2, 3)[1, 1:-1]
    lut[0, :, 3] = (0.55 + 0.45 * np.clip(dist / 22.0, 0, 1)) * valid
    np.save(os.path.join(OUT, 'eye_lut.npy'), lut)
    # 左右の目の境（列の x、旧リグ座標）
    xs_eye = np.where(valid)[0]
    gaps = np.where(np.diff(xs_eye) > 1)[0]
    eye_split = float((xs_eye[gaps[0]] + xs_eye[gaps[0] + 1]) / 2 + ORG[0]) if len(gaps) else 1010.0
    ek = cv2.dilate(eyem, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15)))
    for k in ('N', 'HP', 'ST', 'WR'):
        img = nm[k]
        hh, ss, vv = hsv(img)
        skin = (hh > 0) & (hh < 50) & (ss < 0.45) & (vv > 0.5)
        if k == 'ST':
            skin = vv > 0.45
        below = np.zeros((S, S), bool)
        for x in range(int(700 - ORG[0]), int(1350 - ORG[0])):
            ys = np.where(ek[:, x] > 0)[0]
            if len(ys):
                below[ys[-1] - 10:ys[-1] + 70, x] = True
        x0, y0, x1, y1 = int(680 - ORG[0]), int(680 - ORG[1]), int(1380 - ORG[0]), int(1100 - ORG[1])
        known = (skin & (ek == 0) & ~below)[y0:y1, x0:x1]
        known = cv2.erode(known.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(np.float32)
        sub = img[y0:y1, x0:x1].astype(np.float32)
        fill = cv2.GaussianBlur(P0.pushpull(sub, known), (0, 0), 4)
        out = img.astype(np.float32).copy()
        a = (cv2.GaussianBlur(ek, (0, 0), 3).astype(np.float32) / 255)[y0:y1, x0:x1, None]
        out[y0:y1, x0:x1] = sub * (1 - a) + fill * a
        cv2.imwrite(os.path.join(OUT, f'img_{k}e.png'), np.clip(out, 0, 255).astype(np.uint8))

    # 口なし（基本）と元の口つき
    for k in ('N', 'HP', 'ST', 'WR', 'CE'):
        cv2.imwrite(os.path.join(OUT, f'img_{k}.png'), nm[k])
        cv2.imwrite(os.path.join(OUT, f'img_{k}m.png'), imgs[k])
    cv2.imwrite(os.path.join(OUT, 'img_HT.png'), imgs['HT'])

    def pack(path, *chs):
        arr = np.stack([np.clip(c if c.dtype != np.uint8 else c.astype(np.float32) / 255, 0, 1) for c in chs], -1)
        cv2.imwrite(path, (arr[..., [2, 1, 0, 3]] * 65535).astype(np.uint16))

    pack(os.path.join(OUT, 'm1.png'), soft(cut, 0.6), headw, soft(hat, 8), soft(bow, 8))
    pack(os.path.join(OUT, 'm2.png'), hairL, hairR, soft(earring, 3), soft(body, 10))
    pack(os.path.join(OUT, 'm3.png'), soft(handL, 8), soft(handR, 8), face, bang)
    # m4.b: 目の領域（ハッピー顔のまばたきは、目を閉じた笑顔の目へ差し替える）
    eyes_soft = soft(cv2.dilate(eyem, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (31, 31))), 8)
    pack(os.path.join(OUT, 'm4.png'), soft(closed, 1.0), soft(mouthHP, 0.8), eyes_soft, np.zeros((S, S), np.float32))
    cfg = dict(org=ORG, size=SZ, mc0=mc0, mk0=mk0, mcN=mcN, eye_split=eye_split,
               head_pivot=(1060.0, 1260.0), hat_pivot=(1050.0, 560.0), bow_pivot=(720.0, 300.0), ear_pivot=(1462.0, 960.0),
               breath_y=1950.0)
    json.dump(cfg, open(os.path.join(OUT, 'rig.json'), 'w'), indent=1)
    print('rig_p', {k: (round(v, 3) if isinstance(v, float) else v) for k, v in cfg.items()})

    # ---- 確認用
    vis = N.astype(np.float32) * 0.45
    for m, c in [(hat, (255, 80, 200)), (bow, (0, 160, 255)), (hair, (180, 120, 255)), (body, (255, 200, 0)),
                 (hands, (0, 255, 120)), (earring, (0, 255, 255))]:
        a = (m.astype(np.float32) / 255)[..., None] * 0.45
        vis = vis * (1 - a) + np.array(c, np.float32) * a
    cv2.imwrite(os.path.join(OUT, 'check_parts.jpg'), cv2.resize(np.clip(vis, 0, 255).astype(np.uint8), (1200, 1200)))
    fy0, fx0 = int(700 - ORG[1]), int(700 - ORG[0])
    vis = N.copy(); vis[eyem > 0] = vis[eyem > 0] // 2 + np.array([0, 120, 0], np.uint8)
    vis[closed > 0] = vis[closed > 0] // 2 + np.array([0, 0, 120], np.uint8)
    vis[mouthHP > 0] = vis[mouthHP > 0] // 2 + np.array([120, 0, 0], np.uint8)
    cv2.imwrite(os.path.join(OUT, 'check_face.jpg'), vis[fy0:fy0 + 560, fx0:fx0 + 700])
    return OUT


# ---------------------------------------------------------------- 立ち絵
def build_stands():
    """立ち絵: 余白を詰めた画像と、髪・帽子・裾の揺れの重み（r 髪、g 帽子、b 裾、a 頭）"""
    os.makedirs(OUT_ST, exist_ok=True)
    if done(OUT_ST, [f'{n}.json' for n in STANDS]):
        return OUT_ST
    for name in STANDS:
        img = rgba(name)
        rgb, al = warp_rgba(img, np.float32([[1, 0, 0], [0, 1, 0]]), (img.shape[1], img.shape[0]))
        ys, xs = np.where(al > 8)
        pad = 24
        x0, y0 = max(xs.min() - pad, 0), max(ys.min() - pad, 0)
        x1, y1 = min(xs.max() + pad, img.shape[1]), min(ys.max() + pad, img.shape[0])
        rgb, al = rgb[y0:y1, x0:x1], al[y0:y1, x0:x1]
        Hh, Ww = al.shape
        yy, xx = np.mgrid[0:Hh, 0:Ww].astype(np.float32)
        h, s, v = P0.hsv(rgb)
        inside = al > 128
        pink = ((h > 300) | (h < 15)) & (s > 0.18) & (v > 0.5) & inside & (yy < 0.6 * Hh)
        pk = cv2.morphologyEx(pink.astype(np.uint8) * 255, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
        n, lab, st, _ = cv2.connectedComponentsWithStats(pk)
        pink = (lab == 1 + np.argmax(st[1:, cv2.CC_STAT_AREA])) & pink if n > 1 else pink    # 髪のかたまり（脚・手の肌色を除く）
        purple = (h > 240) & (h < 305) & (s > 0.2) & inside
        # 帽子: 髪より上の紫（各列で最初の髪より上）
        first_hair = np.full(Ww, Hh, np.float32)
        cols = np.where(pink.any(0))[0]
        for x in cols:
            first_hair[x] = np.argmax(pink[:, x])
        hy = float(np.percentile(first_hair[cols], 20)) if len(cols) else Hh * 0.2
        hat = (inside & (yy < first_hair[None, :] + 6) & (yy < hy + 0.12 * Hh)).astype(np.float32)
        hat = cv2.morphologyEx(hat, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
        hat_top = float(np.where(hat.any(1))[0].min()) if hat.any() else 0.0
        hat_bot = float(np.percentile(np.where(hat.any(1))[0], 95)) if hat.any() else hy
        hatw = P0.soft(hat * np.clip((hat_bot - yy) / max(hat_bot - hat_top, 1), 0, 1) ** 1.3, 3)
        # 髪: 下ほど大きく揺れる
        hy0 = float(np.where(pink.any(1))[0].min()) if pink.any() else Hh * 0.2
        hy1 = float(np.where(pink.any(1))[0].max()) if pink.any() else Hh * 0.5
        hairw = P0.soft(P0.soft(pink.astype(np.float32), 3) * np.clip((yy - hy0 - 0.25 * (hy1 - hy0)) / max(hy1 - hy0, 1), 0, 1) ** 1.3, 8)
        # 裾: 上着・スカートの下の方（膝より上）
        hem0, hem1 = 0.42 * Hh, 0.66 * Hh
        hemw = P0.soft((purple | ((v > 0.8) & (s < 0.15) & inside)).astype(np.float32) *
                       np.clip((yy - hem0) / (hem1 - hem0), 0, 1) * (yy < hem1 + 0.04 * Hh), 10)
        head_y = (hy0 + hy1) * 0.5
        headw = P0.soft((yy < head_y + 0.02 * Hh).astype(np.float32) * inside, 20)
        cv2.imwrite(os.path.join(OUT_ST, f'{name}.png'), np.dstack([rgb, al]))
        arr = np.stack([hairw, hatw, hemw, headw], -1)
        cv2.imwrite(os.path.join(OUT_ST, f'{name}_w.png'), (np.clip(arr, 0, 1)[..., [2, 1, 0, 3]] * 65535).astype(np.uint16))
        neck = (Ww * 0.5, hy0 + 0.55 * (hy1 - hy0) * 0.5)
        cfg = dict(size=(Ww, Hh), crop=(int(x0), int(y0)), hat_pivot=(float(xs.mean() - x0), hat_bot), neck=neck,
                   foot_y=float(Hh - pad), head_y=head_y)
        json.dump(cfg, open(os.path.join(OUT_ST, f'{name}.json'), 'w'), indent=1)
        vis = rgb.astype(np.float32) * 0.5
        for m, c in [(hairw, (180, 120, 255)), (hatw, (255, 80, 200)), (hemw, (0, 220, 255))]:
            vis = vis * (1 - m[..., None] * 0.6) + np.array(c, np.float32) * m[..., None] * 0.6
        cv2.imwrite(os.path.join(OUT_ST, f'check_{name}.jpg'), cv2.resize(np.clip(vis, 0, 255).astype(np.uint8), (Ww // 2, Hh // 2)))
        print('stand', name, cfg['size'])
    return OUT_ST


# ---------------------------------------------------------------- シーン別の連続コマ（op2-scene-v1 / v2）
# v1: 1秒刻みのキー姿勢（motion/<名前>/<名前>-NN.png）。v2: v1 のキーを 0.1 秒刻みに補間したもの（<名前>/<名前>_NNNN.png、10fps）
SCENE_SETS = {
    'v1': dict(dir=os.path.join(SRC, 'op2-scene-v1', 'motion'), pat='{n}/{n}-??.png', fps=1.0),
    'v2': dict(dir=os.path.join(SRC, 'op2-scene-v2'), pat='{n}/{n}_????.png', fps=10.0),
}
OUT_SC = os.path.join(E.WORK, 'scene')
# 名前 → (位置合わせに使う動かない部分（コマ0の外接矩形に対する比 x0,y0,x1,y1）, 基準点)
SCENES = {
    'wind': ((0.0, 0.0, 1.0, 0.25), 'foot'),       # 帽子
    'flight': ((0.45, 0.0, 1.0, 0.55), 'center'),  # 頭・帽子・前へ伸ばした腕の付け根
    'walk': ((0.0, 0.0, 1.0, 0.3), 'foot'),
    'dive': ((0.0, 0.0, 1.0, 0.3), 'foot'),
    'gate': ((0.0, 0.0, 0.75, 0.3), 'foot'),       # 帽子（右上へ伸ばす腕は除く）
}


def build_scenes(version='v2'):
    """連続コマをコマ0に合わせて重ね（生成画像のためキーごとに位置が数十 px ずれる）、共通の余白で切り出す。
    補間したコマ（v2）は1枚ずつの推定のばらつきで揺れないよう、補正量を時間方向にならす"""
    import glob
    st_ = SCENE_SETS[version]
    out_dir = os.path.join(OUT_SC, version)
    os.makedirs(out_dir, exist_ok=True)
    if done(out_dir, [f'{n}.json' for n in SCENES]):
        return out_dir
    sift = cv2.SIFT_create(6000)
    for name, (box, anchor) in SCENES.items():
        files = sorted(glob.glob(os.path.join(st_['dir'], st_['pat'].format(n=name))))
        frames = [cv2.imread(f, cv2.IMREAD_UNCHANGED) for f in files]
        b = frames[0]
        H, W = b.shape[:2]
        ys, xs = np.where(b[..., 3] > 128)
        bx0, by0, bx1, by1 = xs.min(), ys.min(), xs.max(), ys.max()
        rx0, ry0 = int(bx0 + box[0] * (bx1 - bx0)), int(by0 + box[1] * (by1 - by0))
        rx1, ry1 = int(bx0 + box[2] * (bx1 - bx0)), int(by0 + box[3] * (by1 - by0))

        def feats(img):
            m = (img[..., 3] > 200).astype(np.uint8)
            r = np.zeros_like(m); r[ry0:ry1, rx0:rx1] = 1
            return sift.detectAndCompute(gray(img), m * r)
        kb, db = feats(b)
        params = []          # (tx, ty, 角度, 拡縮) か None
        for i, f in enumerate(frames):
            prm = (0.0, 0.0, 0.0, 1.0) if i == 0 else None
            if i:
                kx, dx = feats(f)
                mm = cv2.BFMatcher().knnMatch(dx, db, k=2) if dx is not None else []
                g = [a for a, c in mm if a.distance < 0.75 * c.distance]
                if len(g) >= 8:
                    Mr, inl = cv2.estimateAffinePartial2D(np.float32([kx[a.queryIdx].pt for a in g]),
                                                          np.float32([kb[a.trainIdx].pt for a in g]), ransacReprojThreshold=3)
                    # 大きく外れた推定（50px 超・2% 超の拡縮）は使わない
                    if Mr is not None and np.abs(Mr[:, 2]).max() < 50 and abs(np.hypot(Mr[0, 0], Mr[1, 0]) - 1) < 0.02:
                        prm = (float(Mr[0, 2]), float(Mr[1, 2]), float(np.arctan2(Mr[1, 0], Mr[0, 0])), float(np.hypot(Mr[0, 0], Mr[1, 0])))
            params.append(prm)
        # 推定できなかったコマは前後から補い、時間方向にならす（コマ0は固定）
        P = np.array([p if p is not None else (np.nan,) * 4 for p in params], np.float64)
        idx = np.arange(len(P))
        for c in range(4):
            ok = ~np.isnan(P[:, c])
            P[:, c] = np.interp(idx, idx[ok], P[ok, c])
        if len(P) > 4:
            k = np.exp(-0.5 * (np.arange(-3, 4) / 1.5) ** 2); k /= k.sum()
            Ps = np.stack([np.convolve(np.pad(P[:, c], 3, mode='edge'), k, mode='valid') for c in range(4)], -1)
            P = Ps - Ps[0]               # コマ0の補正を 0（拡縮は 1）にそろえる
            P[:, 3] += 1.0
        out = []
        for i, f in enumerate(frames):
            tx, ty, an, sc = P[i]
            M = np.float32([[sc * np.cos(an), -sc * np.sin(an), tx], [sc * np.sin(an), sc * np.cos(an), ty]])
            out.append(warp_rgba(f, M, (W, H)))
        print('scene', version, name, len(frames), 'frames, max shift', np.round(np.abs(P[:, :2]).max(0), 1).tolist())
        al_all = np.max([a for _, a in out], 0)
        ys, xs = np.where(al_all > 8)
        pad = 16
        x0, y0 = max(xs.min() - pad, 0), max(ys.min() - pad, 0)
        x1, y1 = min(xs.max() + pad, W), min(ys.max() + pad, H)
        for i, (rgb, al) in enumerate(out):
            cv2.imwrite(os.path.join(out_dir, f'{name}-{i:04d}.png'), np.dstack([rgb, al])[y0:y1, x0:x1])
        a0 = out[0][1][y0:y1, x0:x1]
        ys0, xs0 = np.where(a0 > 128)
        Ww, Hh = x1 - x0, y1 - y0
        if anchor == 'foot':
            ap = (float(xs0.mean()), float(ys0.max()))
        else:
            ap = (float((xs0.min() + xs0.max()) / 2), float((ys0.min() + ys0.max()) / 2))
        cfg = dict(size=(int(Ww), int(Hh)), frames=len(frames), fps=st_['fps'], anchor=anchor, anchor_pt=ap,
                   height=float(ys0.max() - ys0.min()), width=float(xs0.max() - xs0.min()))
        json.dump(cfg, open(os.path.join(out_dir, f'{name}.json'), 'w'), indent=1)
    return out_dir


def build():
    return build_rig(), build_stands(), build_scenes('v2')


if __name__ == '__main__':
    build()
