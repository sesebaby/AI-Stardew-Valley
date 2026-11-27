#!/usr/bin/env python3
"""开发专用：本机假 AI（OpenAI 兼容的 /v1/chat/completions）。

用法：python3 -I fakeai.py [端口，默认 18080] [工作目录，默认 /dev/shm/aisv/fakeai]

- 每次请求的完整提示词写进 <工作目录>/prompts/，方便检查 mod 发了什么。
- <工作目录>/mode.txt 控制返回：ok（默认）、500、garbage、long、slow、real。
  real 转发给真 AI：环境变量 REAL_BASE（如 https://host/v1）、REAL_KEY、REAL_MODEL。
- <工作目录>/replies.json 可放 [{"match": "提示词里的一段字", "reply": "回复"}]，按顺序第一个匹配的生效；
  没匹配时：提示词要 JSON 的回 {}，否则回一句普通的话。
"""
import json, os, sys, time, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 18080
WORK = sys.argv[2] if len(sys.argv) > 2 else '/dev/shm/aisv/fakeai'
os.makedirs(os.path.join(WORK, 'prompts'), exist_ok=True)
counter = [0]


def read(name, default):
    try:
        with open(os.path.join(WORK, name), encoding='utf-8') as f:
            return f.read().strip() or default
    except OSError:
        return default


def pick_reply(text):
    try:
        rules = json.loads(read('replies.json', '[]'))
    except ValueError:
        rules = []
    for r in rules:
        if r.get('match') and r['match'] in text:
            return r['reply']
    if 'JSON' in text or 'json' in text:
        return '{}'
    return '嗯，今天天气不错，你也要好好照顾自己。'


def completion(content, model):
    return {
        'id': f'fake-{counter[0]}', 'object': 'chat.completion', 'created': int(time.time()), 'model': model,
        'choices': [{'index': 0, 'message': {'role': 'assistant', 'content': content}, 'finish_reason': 'stop'}],
        'usage': {'prompt_tokens': 1, 'completion_tokens': max(1, len(content)), 'total_tokens': 1 + len(content)},
    }


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def send(self, code, obj):
        data = json.dumps(obj, ensure_ascii=False).encode('utf-8') if not isinstance(obj, bytes) else obj
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        self.send(200, {'object': 'list', 'data': [{'id': 'fake', 'object': 'model'}]})

    def do_POST(self):
        body = self.rfile.read(int(self.headers.get('Content-Length', 0)))
        try:
            req = json.loads(body)
        except ValueError:
            return self.send(400, {'error': 'bad json'})
        counter[0] += 1
        text = '\n\n'.join(str(m.get('content', '')) for m in req.get('messages', []))
        with open(os.path.join(WORK, 'prompts', f'{counter[0]:05d}.txt'), 'w', encoding='utf-8') as f:
            f.write(text)
        mode = read('mode.txt', 'ok')
        model = req.get('model', 'fake')
        if mode == '500':
            return self.send(500, {'error': {'message': 'fake 500'}})
        if mode == 'garbage':
            return self.send(200, b'not json at all')
        if mode == 'slow':
            time.sleep(40)
        if mode == 'long':
            return self.send(200, completion('很长的话。' * 2000, model))
        if mode == 'real':
            up = urllib.request.Request(os.environ['REAL_BASE'].rstrip('/') + '/chat/completions', method='POST',
                                        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + os.environ.get('REAL_KEY', '')})
            req['model'] = os.environ.get('REAL_MODEL', model)
            try:
                with urllib.request.urlopen(up, json.dumps(req).encode('utf-8'), timeout=120) as r:
                    data = r.read()
                reply = json.loads(data)['choices'][0]['message'].get('content', '')
                with open(os.path.join(WORK, 'prompts', f'{counter[0]:05d}.reply.txt'), 'w', encoding='utf-8') as f:
                    f.write(reply)
                return self.send(200, data)
            except Exception as e:  # noqa: BLE001 - 开发工具，原样报给 mod
                return self.send(502, {'error': {'message': str(e)}})
        reply = pick_reply(text)
        with open(os.path.join(WORK, 'prompts', f'{counter[0]:05d}.reply.txt'), 'w', encoding='utf-8') as f:
            f.write(reply)
        self.send(200, completion(reply, model))


print(f'fakeai on 127.0.0.1:{PORT}, work={WORK}', flush=True)
ThreadingHTTPServer(('127.0.0.1', PORT), Handler).serve_forever()
