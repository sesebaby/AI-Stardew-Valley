#!/usr/bin/env python3
"""开发专用：同步多语言文件。

python3 -I tools/i18n-sync.py check [--fill]     把代码里 Tr.Get(键, "中文") 新出现的键补进 zh.json（文字以代码为准），
                                                列出其他语言缺的键（写到 /dev/shm/aisv/i18n-missing.json）；
                                                --fill 先把中文原文放进其他语言（还没翻译时的做法，发布前统一翻译）
python3 -I tools/i18n-sync.py merge <译文.json>  合并译文：{"en": {"键": "…"}, "de": {...}, ...}；en 写进 default.json
python3 -I tools/i18n-sync.py prune             删掉代码里已经不用的键（所有语言）

新键按代码里出现的顺序追加在文件末尾；JSON 保持两个空格缩进、不转义中文。
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
I18N = os.path.join(ROOT, 'src', 'AISV.Mod', 'i18n')
CALL = re.compile(r'Tr\.Get\("([^"]+)",\s*"((?:[^"\\]|\\.)*)"')
LANGS = {'en': 'default', 'de': 'de', 'es': 'es', 'fr': 'fr', 'hu': 'hu', 'it': 'it', 'ja': 'ja', 'ko': 'ko', 'pt': 'pt', 'ru': 'ru', 'tr': 'tr'}


def load(name):
    with open(os.path.join(I18N, name + '.json'), encoding='utf-8-sig') as f:
        return json.load(f)


def save(name, data):
    with open(os.path.join(I18N, name + '.json'), 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write('\n')


def unescape(s):
    return json.loads('"' + s + '"')


def code_keys():
    out = {}
    for dirpath, _, files in os.walk(os.path.join(ROOT, 'src')):
        if '/obj' in dirpath or '/bin' in dirpath:
            continue
        for f in sorted(files):
            if f.endswith('.cs'):
                text = open(os.path.join(dirpath, f), encoding='utf-8').read()
                for k, v in CALL.findall(text):
                    out.setdefault(k, unescape(v))
    return out


def dynamic_keys():
    """代码里拼出来的键（没有中文默认值的 Tr.Get），prune 时保留。"""
    keep = set()
    for dirpath, _, files in os.walk(os.path.join(ROOT, 'src')):
        for f in files:
            if f.endswith('.cs'):
                keep.update(re.findall(r'Tr\.Get\("([^"]+)"\)', open(os.path.join(dirpath, f), encoding='utf-8').read()))
    return keep


def check(fill=False):
    zh = load('zh')
    keys = code_keys()
    added = 0
    for k, v in keys.items():
        if zh.get(k) != v:
            if k not in zh:
                added += 1
            zh[k] = v
    save('zh', zh)
    missing = {}
    for lang, name in LANGS.items():
        other = load(name)
        miss = {k: v for k, v in zh.items() if k not in other}
        if miss and fill:
            other.update(miss)
            save(name, other)
        elif miss:
            missing[lang] = miss
    os.makedirs('/dev/shm/aisv', exist_ok=True)
    with open('/dev/shm/aisv/i18n-missing.json', 'w', encoding='utf-8') as f:
        json.dump(missing.get('en', {}), f, ensure_ascii=False, indent=1)
    print(f'zh.json 新增 {added} 个键；其他语言缺：' + ', '.join(f'{l} {len(m)}' for l, m in missing.items()) if missing else f'zh.json 新增 {added} 个键；其他语言都齐了。')


def merge(path):
    with open(path, encoding='utf-8') as f:
        tr = json.load(f)
    zh = load('zh')
    token = re.compile(r'\{\{\w+\}\}')
    bad = 0
    for lang, entries in tr.items():
        name = LANGS[lang]
        other = load(name)
        for k, v in entries.items():
            if k not in zh:
                print(f'{lang}: zh.json 没有 {k}，跳过'); bad += 1; continue
            if sorted(token.findall(zh[k])) != sorted(token.findall(v)):
                print(f'{lang}: {k} 占位符不对，跳过'); bad += 1; continue
            other[k] = v
        save(name, other)
    print('合并完' + (f'，{bad} 条有问题' if bad else ''))


def prune():
    used = set(code_keys()) | dynamic_keys()
    zh = load('zh')
    gone = [k for k in zh if k not in used]
    print(f'代码里不再用的键 {len(gone)} 个（只列出，不自动删）：')
    for k in gone[:200]:
        print('  ', k)


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'check':
        check('--fill' in sys.argv)
    elif cmd == 'merge':
        merge(sys.argv[2])
    else:
        prune()
