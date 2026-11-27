#!/usr/bin/env python3
"""开发专用：给测试工具发命令并等结果。

用法：python3 -I h.py <实例名> <命令> [参数...]      打印结果 JSON，失败时退出码 1
      python3 -I h.py <实例名> --flow <文件>          一行一条命令（# 开头是注释），遇到失败就停
超时：环境变量 H_TIMEOUT（秒，默认 600）。
"""
import json, os, shlex, sys, time, uuid

def send(inst, cmd, args):
    io = f'/dev/shm/aisv/{inst}/io'
    cid = time.strftime('%H%M%S') + '-' + uuid.uuid4().hex[:6]
    tmp = f'{io}/in/{cid}.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        json.dump({'cmd': cmd, 'args': args}, f, ensure_ascii=False)
    os.rename(tmp, f'{io}/in/{cid}.json')
    out = f'{io}/out/{cid}.json'
    deadline = time.time() + float(os.environ.get('H_TIMEOUT', '600'))
    while time.time() < deadline:
        if os.path.exists(out):
            with open(out, encoding='utf-8') as f:
                return json.load(f)
        time.sleep(0.2)
    return {'ok': False, 'error': 'client timeout'}

inst = sys.argv[1]
if sys.argv[2] == '--flow':
    for line in open(sys.argv[3], encoding='utf-8'):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        parts = shlex.split(line)
        r = send(inst, parts[0], parts[1:])
        print('>', line, '->', 'ok' if r['ok'] else 'FAIL', json.dumps(r.get('result'), ensure_ascii=False)[:300] if r['ok'] else r.get('error', '')[:800])
        if not r['ok']:
            sys.exit(1)
else:
    r = send(inst, sys.argv[2], sys.argv[3:])
    print(json.dumps(r, ensure_ascii=False, indent=1))
    sys.exit(0 if r['ok'] else 1)
