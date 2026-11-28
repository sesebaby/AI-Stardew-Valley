#!/usr/bin/env python3
"""开发专用：把生图工具画的孩子动作网格（4 列 × 6 行，排法和原版幼儿一样）整理成游戏贴图。

python3 -I tools/art/kidsheet.py GRID.png OUT.png --height 52 --scale 2 [--colours 28]
python3 -I tools/art/kidsheet.py --portrait GRID.png OUT.png   头像：2×2 网格（平常、开心、难过、吃惊）拼成 128×128，每张 64×64
python3 -I tools/art/kidsheet.py --dark IN.png OUT.png        深肤色版：皮肤和头发换深色（行走图、头像都行）

原版 Characters/Toddler 是 64×192：每帧 16×32，第 1～4 行朝下、右、上、左走路，第 5 行朝前举手（16～19），第 6 行朝右坐下（20～23）。
长大的孩子头发更宽，这里每帧 22×32（88×192，和原版婴儿一样宽），游戏里让孩子按一格宽定位，碰撞框不变。这里：
1. 抠掉洋红底，在每两行、两列之间找最空的线切开，丢掉伸进格子的邻居碎块；
2. 生图画的是一格好几个屏幕像素的像素画：按第一帧站着的身高定一格多大（身高 / --height），每帧估出格线起点，\n   每格取中间最多的颜色（平滑缩放会把眼睛糊掉）；再去掉孤立的杂点。坐着太宽、举手太高的帧单独缩到放得下；
3. 整张共用一套调色板（--colours 色），透明度只留全透和不透；
4. 每帧脚踩在第 31 行，按头（身体最上面五分之一）的中心左右居中，所以甩手不会让身子晃。
--scale 2：按两倍精度切（每帧 44×64，脚踩第 63 行），游戏里按半尺寸画，个头和 22×32 一样，脸上的五官不会被缩没。
另存一张放大 6 倍的预览 OUT-preview.png，和原版幼儿并排看。
"""
import cmath
import math
import sys
from collections import Counter, deque

from PIL import Image

CELL_W, CELL_H, COLS, ROWS = 22, 32, 4, 6
FEET = 31


def arg(name, default):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def magenta(r, g, b):
    """底色是接近纯洋红的（衣服上的粉、紫绿色分量高得多，不会被当成底）。"""
    return r > 190 and b > 190 and g < 90 and abs(r - b) < 60


def cut_background(im):
    """洋红底变透明（从四边灌水，人身上的粉色不受影响），再去掉贴着人的洋红毛边。"""
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    seen = bytearray(w * h)
    q = deque((x, y) for x in range(w) for y in (0, h - 1))
    q.extend((x, y) for y in range(h) for x in (0, w - 1))
    while q:
        x, y = q.popleft()
        if x < 0 or y < 0 or x >= w or y >= h or seen[y * w + x]:
            continue
        seen[y * w + x] = 1
        r, g, b, _ = px[x, y]
        if not magenta(r, g, b):
            continue
        px[x, y] = (0, 0, 0, 0)
        q.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    # 贴着底的洋红毛边（颜色偏洋红、旁边就是透明）
    fringe = []
    for y in range(1, h - 1):
        for x in range(1, w - 1):
            r, g, b, a = px[x, y]
            if a and r > 150 and b > 150 and g < 110 and abs(r - b) < 70 and any(px[x + dx, y + dy][3] == 0 for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                fringe.append((x, y))
    for x, y in fringe:
        px[x, y] = (0, 0, 0, 0)
    return im


def splits(profile, parts):
    """在每个等分点附近找最空的那条线。"""
    n = len(profile)
    cuts = [0]
    for i in range(1, parts):
        mid = n * i // parts
        win = n // parts // 3
        cuts.append(min(range(mid - win, mid + win), key=lambda j: (profile[j], abs(j - mid))))
    cuts.append(n)
    return cuts


def biggest_only(crop):
    """只留这格里的主体：和最大那块连着（隔几个像素也算，头发、手常常断开）的都留下。"""
    w, h = crop.size
    a = crop.split()[3].load()
    seen = [[False] * w for _ in range(h)]
    blobs = []
    for y0 in range(h):
        for x0 in range(w):
            if seen[y0][x0] or a[x0, y0] == 0:
                continue
            pts, stack = [], [(x0, y0)]
            seen[y0][x0] = True
            while stack:
                x, y = stack.pop()
                pts.append((x, y))
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and a[nx, ny]:
                        seen[ny][nx] = True
                        stack.append((nx, ny))
            xs, ys = [p[0] for p in pts], [p[1] for p in pts]
            blobs.append((len(pts), (min(xs), min(ys), max(xs) + 1, max(ys) + 1), pts))
    if not blobs:
        return crop
    blobs.sort(key=lambda b: -b[0])
    keep = [blobs[0]]
    box = list(blobs[0][1])
    pad = max(w, h) // 40
    for b in blobs[1:]:
        bx = b[1]
        near = bx[0] <= box[2] + pad and bx[2] >= box[0] - pad and bx[1] <= box[3] + pad and bx[3] >= box[1] - pad
        if near and b[0] > 6:
            keep.append(b)
            box = [min(box[0], bx[0]), min(box[1], bx[1]), max(box[2], bx[2]), max(box[3], bx[3])]
    out = Image.new("RGBA", crop.size, (0, 0, 0, 0))
    src, dst = crop.load(), out.load()
    for b in keep:
        for x, y in b[2]:
            dst[x, y] = src[x, y]
    return out.crop(out.getbbox())


def frames_of(grid):
    a = grid.split()[3]
    w, h = grid.size
    col = [sum(1 for y in range(0, h, 2) if a.getpixel((x, y))) for x in range(w)]
    row = [sum(1 for x in range(0, w, 2) if a.getpixel((x, y))) for y in range(h)]
    xs, ys = splits(col, COLS), splits(row, ROWS)
    return [[biggest_only(grid.crop((xs[c], ys[r], xs[c + 1], ys[r + 1]))) for c in range(COLS)] for r in range(ROWS)]


def shrink(fig, scale):
    w, h = fig.size
    nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
    # 先预乘透明度再缩，免得边上混进黑色
    small = fig.resize((nw, nh), Image.LANCZOS, reducing_gap=3.0)
    px = small.load()
    for y in range(nh):
        for x in range(nw):
            r, g, b, al = px[x, y]
            px[x, y] = (r, g, b, 255 if al >= 128 else 0)
    return small


def edges(fig, axis):
    """颜色突变的位置（axis 0 看横向，1 看纵向）。"""
    px = fig.load()
    w, h = fig.size
    out = []
    if axis == 0:
        for y in range(0, h, 2):
            for x in range(1, w):
                a, b = px[x - 1, y], px[x, y]
                if (a[3] > 0) != (b[3] > 0) or sum(abs(a[i] - b[i]) for i in range(3)) > 70:
                    out.append(x)
    else:
        for x in range(0, w, 2):
            for y in range(1, h):
                a, b = px[x, y - 1], px[x, y]
                if (a[3] > 0) != (b[3] > 0) or sum(abs(a[i] - b[i]) for i in range(3)) > 70:
                    out.append(y)
    return out


def lattice(positions, lo=5.0, hi=14.0):
    """像素格的大小和起点：颜色突变的位置最整齐地落在格线上的那个周期。"""
    best = (-1.0, lo, 0.0)
    s = lo
    while s <= hi + 1e-9:
        z = sum(cmath.exp(2j * math.pi * (p % s) / s) for p in positions) / max(1, len(positions))
        if abs(z) > best[0]:
            best = (abs(z), s, (cmath.phase(z) / (2 * math.pi)) % 1 * s)
        s += 0.02
    return best[1], best[2]


def by_blocks(fig, size=None):
    """按格取色：每格取中间一片里最多的颜色，透明的格子留空。"""
    s, ox = lattice(edges(fig, 0), size, size) if size else lattice(edges(fig, 0))
    _, oy = lattice(edges(fig, 1), s, s)
    px = fig.load()
    w, h = fig.size
    cols, rows = int((w - ox) / s) + 1, int((h - oy) / s) + 1
    out = Image.new("RGBA", (cols + 1, rows + 1), (0, 0, 0, 0))
    o = out.load()
    for j in range(-1, rows):
        for i in range(-1, cols):
            cx, cy = ox + (i + 0.5) * s, oy + (j + 0.5) * s
            votes = Counter()
            r = max(1, int(s * 0.25))
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    x, y = int(cx + dx), int(cy + dy)
                    if 0 <= x < w and 0 <= y < h:
                        c = px[x, y]
                        votes[(c[0] // 8 * 8, c[1] // 8 * 8, c[2] // 8 * 8, 255) if c[3] >= 128 else (0, 0, 0, 0)] += 1
            if votes:
                o[i + 1, j + 1] = votes.most_common(1)[0][0]
    # 边上残留的洋红点（只去贴着透明的，衣服上的亮粉不动）
    ow, oh = out.size
    stray = [(x, y) for y in range(oh) for x in range(ow) if o[x, y][3] and magenta(*o[x, y][:3])
             and any(not (0 <= x + dx < ow and 0 <= y + dy < oh) or o[x + dx, y + dy][3] == 0 for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    for x, y in stray:
        o[x, y] = (0, 0, 0, 0)
    box = out.getbbox()
    return out.crop(box) if box else out, s


def despeckle(fig):
    """去杂点：一个像素和上下左右都不一样、而周围多数是同一种颜色时，换成那种颜色；人身上单个透明的洞补上。"""
    w, h = fig.size
    src = fig.load()
    out = fig.copy()
    dst = out.load()
    for y in range(h):
        for x in range(w):
            me = src[x, y]
            near = [src[x + dx, y + dy] for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if 0 <= x + dx < w and 0 <= y + dy < h]
            if len(near) < 4:
                continue
            if me[3] == 0:
                if all(n[3] for n in near):
                    dst[x, y] = Counter(near).most_common(1)[0][0]
                continue
            if any(n[:3] == me[:3] for n in near if n[3]):
                continue
            ring = [src[x + dx, y + dy] for dx in (-1, 0, 1) for dy in (-1, 0, 1) if (dx or dy)]
            top, count = Counter(ring).most_common(1)[0]
            if count >= 5 and top[3]:
                dst[x, y] = top
    return out


def head_center(fig):
    a = fig.split()[3]
    w, h = fig.size
    top = max(1, h // 5)
    xs = [x for y in range(top) for x in range(w) if a.getpixel((x, y))]
    return (min(xs) + max(xs) + 1) / 2 if xs else w / 2


def build(grid_path, out, height, colours):
    grid = cut_background(Image.open(grid_path))
    frames = frames_of(grid)
    block = frames[0][0].size[1] / height
    first, _ = by_blocks(frames[0][0], block)
    print(f"像素格 {block:.2f}，站着身高 {first.size[1]} 像素")
    scale = 1.0
    sheet = Image.new("RGBA", (CELL_W * COLS, CELL_H * ROWS), (0, 0, 0, 0))
    for r in range(ROWS):
        for c in range(COLS):
            small, _ = by_blocks(frames[r][c], block)
            small = despeckle(small)
            if small.size[0] > CELL_W + 2 and r == ROWS - 1:
                # 坐着腿伸出去太宽：单独缩到放得下
                small = shrink(small, CELL_W / small.size[0])
            if small.size[1] > CELL_H - 1:
                # 举手太高：单独缩到放得下
                small = shrink(small, (CELL_H - 1) / small.size[1])
            hc = head_center(small) if r < ROWS - 1 or c < 2 else small.size[0] / 2
            x = round(CELL_W / 2 - hc)
            y = FEET + 1 - small.size[1]
            cell = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
            cell.alpha_composite(small, (max(-small.size[0], x), max(0, y))) if y >= 0 else cell.alpha_composite(small.crop((0, -y, small.size[0], small.size[1])), (x, 0))
            sheet.alpha_composite(cell, (c * CELL_W, r * CELL_H))
    # 共用调色板
    alpha = sheet.split()[3]
    rgb = sheet.convert("RGB").quantize(colours, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    final = rgb.convert("RGBA")
    final.putalpha(alpha.point(lambda v: 255 if v >= 128 else 0))
    final.save(out)
    return final


def preview(sheet, out, vanilla=None):
    zoom = 6 if sheet.width <= 128 else 3
    big = sheet.resize((sheet.width * zoom, sheet.height * zoom), Image.NEAREST)
    if vanilla:
        v = Image.open(vanilla).convert("RGBA")
        f = big.height // v.height
        v = v.resize((v.width * f, v.height * f), Image.NEAREST)
        canvas = Image.new("RGBA", (big.width * 2 + 24, big.height), (200, 220, 200, 255))
        canvas.alpha_composite(v, (0, 0))
        canvas.alpha_composite(big, (big.width + 24, 0))
        big = canvas
    else:
        bg = Image.new("RGBA", big.size, (200, 220, 200, 255))
        bg.alpha_composite(big)
        big = bg
    big.save(out)


# 原版深肤色幼儿的肤色（亮、中、暗）
SKIN_DARK = [(185, 99, 66), (158, 80, 50), (119, 46, 46)]


def is_skin(r, g, b):
    return r >= 0xe0 and 0x98 <= g <= 0xdc and 0x68 <= b <= 0xbd and r - b >= 0x38


def is_hair(r, g, b):
    """红棕、橙色的头发（和皮肤比绿得少，和衣服比红得多）。"""
    return r >= 0x50 and 0.12 <= g / r <= 0.58 and b / r < 0.36


def darken_skin(src, out):
    """深肤色：皮肤按明暗换成原版深肤色那三档，头发压暗成深棕（原版深肤色幼儿也是这样）。"""
    im = Image.open(src).convert("RGBA")
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if not a:
                continue
            if is_skin(r, g, b):
                lum = (r * 3 + g * 6 + b) / 10
                px[x, y] = SKIN_DARK[0 if lum > 200 else 1 if lum > 175 else 2] + (255,)
            elif is_hair(r, g, b):
                px[x, y] = (round(r * 0.47), round(g * 0.45), round(b * 0.45), 255)
    im.save(out)
    return im


def portraits(grid_path, out, colours=40):
    """头像：四张用同一个像素格（第一张高 60 像素），肩膀贴着底边、左右居中，顺序和原版表情编号一样。"""
    grid = cut_background(Image.open(grid_path))
    a = grid.split()[3]
    w, h = grid.size
    col = [sum(1 for y in range(0, h, 2) if a.getpixel((x, y))) for x in range(w)]
    row = [sum(1 for x in range(0, w, 2) if a.getpixel((x, y))) for y in range(h)]
    xs, ys = splits(col, 2), splits(row, 2)
    faces = [biggest_only(grid.crop((xs[c], ys[r], xs[c + 1], ys[r + 1]))) for r in range(2) for c in range(2)]
    block = faces[0].size[1] / 60
    sheet = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    for i, face in enumerate(faces):
        small, _ = by_blocks(face, block)
        small = despeckle(small)
        if small.size[0] > 64 or small.size[1] > 64:
            small = shrink(small, min(64 / small.size[0], 64 / small.size[1]))
        x = (i % 2) * 64 + (64 - small.size[0]) // 2
        y = (i // 2) * 64 + 64 - small.size[1]
        sheet.alpha_composite(small, (x, y))
    alpha = sheet.split()[3]
    final = sheet.convert("RGB").quantize(colours, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGBA")
    final.putalpha(alpha.point(lambda v: 255 if v >= 128 else 0))
    final.save(out)
    return final


def main():
    if sys.argv[1] == "--portrait":
        sheet = portraits(sys.argv[2], sys.argv[3])
        preview(sheet, sys.argv[3].replace(".png", "-preview.png"))
        return
    if sys.argv[1] == "--dark":
        sheet = darken_skin(sys.argv[2], sys.argv[3])
        preview(sheet, sys.argv[3].replace(".png", "-preview.png"))
        return
    global CELL_W, CELL_H, FEET
    k = int(arg("--scale", "1"))
    CELL_W, CELL_H, FEET = CELL_W * k, CELL_H * k, CELL_H * k - 1
    sheet = build(sys.argv[1], sys.argv[2], int(arg("--height", str(26 * k))), int(arg("--colours", "28")))
    preview(sheet, sys.argv[2].replace(".png", "-preview.png"), arg("--vanilla", None))


if __name__ == "__main__":
    main()
