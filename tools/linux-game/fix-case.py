#!/usr/bin/env python3
"""给 mod 里写错大小写的路径补软链接：Windows 不区分大小写，Linux 区分。

用法：python3 -I fix-case.py <Mods 目录>
扫 manifest/content.json 里出现的相对路径，找不到时按不区分大小写匹配，在原位置建软链接。
"""
import json, os, re, sys

root = sys.argv[1]
pat = re.compile(r'"([^"\n]+\.(?:png|json|tmx|tbin|xnb|dll))"', re.I)


def resolve(base, rel):
    cur = base
    for part in rel.replace('\\', '/').split('/'):
        if part in ('', '.'):
            continue
        if part == '..':
            cur = os.path.dirname(cur)
            continue
        nxt = os.path.join(cur, part)
        if not os.path.exists(nxt):
            try:
                hit = [n for n in os.listdir(cur) if n.lower() == part.lower()]
            except OSError:
                return None
            if not hit:
                return None
            os.symlink(hit[0], nxt)
        cur = nxt
    return cur


made = 0
for dirpath, _, files in os.walk(root):
    for f in files:
        if not f.lower().endswith('.json'):
            continue
        try:
            text = open(os.path.join(dirpath, f), encoding='utf-8-sig', errors='ignore').read()
        except OSError:
            continue
        for rel in pat.findall(text):
            if '{{' in rel or ':' in rel or rel.startswith(('Maps/', 'Characters/', 'LooseSprites/', 'TileSheets/', 'Data/')):
                continue
            target = os.path.join(dirpath, rel.replace('\\', '/'))
            if os.path.exists(target):
                continue
            before = os.path.lexists(target)
            if resolve(dirpath, rel) and not before:
                made += 1
print(f'fix-case: {made} links')
