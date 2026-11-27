#!/usr/bin/env python3
"""开发专用：调生图网关画 mod 的素材。

python3 -I tools/art/gen_image.py models                 列出这个密钥能用的模型
python3 -I tools/art/gen_image.py JOBS.json OUTDIR [--workers N]

JOBS.json 是 [{"name", "prompt", "size": "1024x1536", "model"?}, ...]；每张写到 OUTDIR/<name>.png，
请求和回复（不含密钥）写到 OUTDIR/<name>.json。已经有图的跳过，重跑只补失败的。
密钥从环境变量 AISV_IMAGE_KEY 读，没有就从本机的调用文档里读；不打印、不落盘，也不要提交进仓库。
"""
import concurrent.futures
import json
import math
import os
import re
import sys
import time
import urllib.error
import urllib.request

GUIDE = "/home/zyb/Windows-Host/MyStone/gpt-image-call-guide.md"
BASE = "https://www.heilovehei.com/v1"
MODEL = os.environ.get("AISV_IMAGE_MODEL", "gpt-image-2.6")


def key():
    if os.environ.get("AISV_IMAGE_KEY"):
        return os.environ["AISV_IMAGE_KEY"]
    try:
        with open(GUIDE, encoding="utf-8") as f:
            m = re.search(r"sk-[A-Za-z0-9_-]{20,}", f.read())
    except OSError:
        m = None
    if not m:
        sys.exit("没有生图密钥：设置 AISV_IMAGE_KEY")
    return m.group(0)


def call(path, body=None, timeout=600):
    req = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(),
                                 headers={"Authorization": "Bearer " + key(), "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read())


def one(job, out):
    name = job["name"]
    png = os.path.join(out, name + ".png")
    if os.path.exists(png):
        return f"{name}: 已有，跳过"
    w, h = (int(v) for v in job.get("size", "1024x1024").split("x"))
    d = math.gcd(w, h)
    body = {"model": job.get("model", MODEL), "prompt": job["prompt"], "size": f"{w}x{h}", "aspect_ratio": f"{w // d}:{h // d}",
            "quality": "high", "response_format": "url", "n": 1}
    last = ""
    for attempt in range(3):
        try:
            t = time.time()
            resp = call("/images/generations", body)
            url = resp["data"][0]["url"]
            with urllib.request.urlopen(url, timeout=300) as r:
                data = r.read()
            with open(png, "wb") as f:
                f.write(data)
            with open(os.path.join(out, name + ".json"), "w", encoding="utf-8") as f:
                json.dump({"request": body, "response": resp}, f, ensure_ascii=False, indent=1)
            return f"{name}: 好了（{time.time() - t:.0f} 秒，{len(data) // 1024} KB）"
        except urllib.error.HTTPError as e:
            last = f"HTTP {e.code} {e.read()[:300].decode('utf-8', 'replace')}"
            time.sleep(10 * (attempt + 1))
            continue
        except Exception as e:
            last = str(e)[:200]
            time.sleep(10 * (attempt + 1))
    return f"{name}: 失败 {last}"


def main():
    if sys.argv[1] == "models":
        for m in call("/models")["data"]:
            print(m.get("id"))
        return
    jobs = json.load(open(sys.argv[1], encoding="utf-8"))
    out = sys.argv[2]
    os.makedirs(out, exist_ok=True)
    workers = int(sys.argv[sys.argv.index("--workers") + 1]) if "--workers" in sys.argv else 4
    with concurrent.futures.ThreadPoolExecutor(workers) as pool:
        for line in pool.map(lambda j: one(j, out), jobs):
            print(line, flush=True)


if __name__ == "__main__":
    main()
