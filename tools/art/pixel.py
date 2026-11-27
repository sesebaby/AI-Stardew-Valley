#!/usr/bin/env python3
"""开发专用：把生图工具画的图整理成 mod 用的素材。

python3 -I tools/art/pixel.py diary IN.png OUT.png          日记夹页：裁成 2:3，缩到 512x768，256 色（和 assets/diary 一样）
python3 -I tools/art/pixel.py prop IN.png OUT.png W H       事件道具：抠掉洋红底、裁边，按比例缩进 W x H 游戏像素（底边对齐），
                                                            32 色、透明度只留全透和不透；另存一张放大 8 倍的预览 OUT-preview.png
"""
import sys
from collections import deque

from PIL import Image


def diary(src, out):
    im = Image.open(src).convert("RGB")
    w, h = im.size
    if w * 3 > h * 2:
        nw = h * 2 // 3
        im = im.crop(((w - nw) // 2, 0, (w - nw) // 2 + nw, h))
    else:
        nh = w * 3 // 2
        im = im.crop((0, (h - nh) // 2, w, (h - nh) // 2 + nh))
    im = im.resize((512, 768), Image.LANCZOS)
    im.quantize(256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).save(out, optimize=True)


def magenta(r, g, b):
    return r > 150 and b > 150 and g < 110 and abs(r - b) < 90


def cut(im):
    """从四边往里把洋红底抠掉（物体里面的洋红不动），再把贴着边的洋红晕边去掉。"""
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    seen = bytearray(w * h)
    q = deque([(x, y) for x in range(w) for y in (0, h - 1)] + [(x, y) for y in range(h) for x in (0, w - 1)])
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
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a and r > 120 and b > 120 and g < r * 0.6 and g < b * 0.6:
                px[x, y] = (0, 0, 0, 0)
    return im


def prop(src, out, W, H):
    im = cut(Image.open(src))
    im = im.crop(im.getbbox())
    s = min(W / im.width, H / im.height)
    nw, nh = max(1, round(im.width * s)), max(1, round(im.height * s))
    small = im.resize((nw, nh), Image.BOX)
    alpha = small.getchannel("A").point(lambda v: 255 if v > 140 else 0)
    rgb = small.convert("RGB").quantize(32, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    rgb.putalpha(alpha)
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sheet.paste(rgb, ((W - nw) // 2, H - nh), rgb)
    sheet.save(out)
    big = sheet.resize((W * 8, H * 8), Image.NEAREST)
    bg = Image.new("RGBA", big.size, (120, 170, 90, 255))
    bg.alpha_composite(big)
    bg.save(out.replace(".png", "-preview.png"))


if __name__ == "__main__":
    if sys.argv[1] == "diary":
        diary(sys.argv[2], sys.argv[3])
    else:
        prop(sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5]))
