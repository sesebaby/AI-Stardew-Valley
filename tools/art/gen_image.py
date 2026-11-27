#!/usr/bin/env python3
"""开发专用：调生图网关画 mod 的素材。

python3 -I tools/art/gen_image.py models [--api box]                 列出这个密钥能用的模型
python3 -I tools/art/gen_image.py JOBS.json OUTDIR [--workers N] [--api box]

JOBS.json 是 [{"name", "prompt", "size": "1024x1536", "model"?, "images"?: [本机文件...]}, ...]；每张写到 OUTDIR/<name>.png，
请求和回复（不含密钥和图）写到 OUTDIR/<name>.json。已经有图的跳过，重跑只补失败的。
"images" 是参考图（图生图，让同一个角色几张图长得一样），只传我们自己画的图，不传原版或别人的素材。
--api box 走 168box 中转站：用它最新的 gpt-image 模型（AISV_IMAGE_MODEL 可以指定），尺寸贴到 1024x1024、1536x1024、1024x1536 里最近的一种。
密钥从环境变量（AISV_IMAGE_KEY / AISV_BOX_KEY）读，没有就从本机的调用文档里读（黑黑 AI 先读 ~/.config/aisv/gpt-image-guide.md，
再读 Windows-Host 里的旧文档）；不打印、不落盘，也不要提交进仓库。任务里可以写 "quality"（默认 high）。
"""
import base64
import concurrent.futures
import json
import math
import os
import re
import sys
import time
import urllib.error
import urllib.request

API = sys.argv[sys.argv.index("--api") + 1] if "--api" in sys.argv else ""
BOX = API == "box"
GUIDES = ["/home/zyb/Windows-Host/deadbook/云卡卡生图接口.md"] if BOX else [os.path.expanduser("~/.config/aisv/gpt-image-guide.md"), "/home/zyb/Windows-Host/MyStone/gpt-image-call-guide.md"]
BASE = "https://api.168box.xyz/v1" if BOX else "https://www.heilovehei.com/v1"
KEY_VAR = "AISV_BOX_KEY" if BOX else "AISV_IMAGE_KEY"
MODEL = os.environ.get("AISV_IMAGE_MODEL", "" if BOX else "gpt-image-2.6")


def key():
    if os.environ.get(KEY_VAR):
        return os.environ[KEY_VAR]
    for guide in GUIDES:
        try:
            with open(guide, encoding="utf-8") as f:
                m = re.search(r"sk-[A-Za-z0-9_-]{20,}", f.read())
        except OSError:
            m = None
        if m:
            return m.group(0)
    sys.exit(f"没有生图密钥：设置 {KEY_VAR}")


def call(path, body=None, timeout=600):
    req = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(),
                                 headers={"Authorization": "Bearer " + key(), "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read())


_model = None


def model():
    """中转站上最新的 gpt-image（flare 优先）；原来的网关用固定的。"""
    global _model
    if _model is None:
        _model = MODEL
        if not _model:
            ids = [m.get("id", "") for m in call("/models").get("data", [])]

            def rank(i):
                v = re.match(r"gpt-image-(\d+(?:\.\d+)?)(-(\w+))?$", i)
                return (-float(v.group(1)), {"flare": 0, None: 1}.get(v.group(3), 2)) if v else (0, 9)
            found = sorted((i for i in ids if re.match(r"gpt-image-\d", i)), key=rank)
            _model = found[0] if found else "gpt-image-2"
    return _model


def as_data_url(path):
    with open(path, "rb") as f:
        return "data:image/png;base64," + base64.b64encode(f.read()).decode("ascii")


def body_for(job):
    w, h = (int(v) for v in job.get("size", "1024x1024").split("x"))
    if BOX:
        size = "1536x1024" if w > h * 1.15 else "1024x1536" if h > w * 1.15 else "1024x1024"
        body = {"model": job.get("model") or model(), "prompt": job["prompt"], "size": size, "quality": job.get("quality", "high"),
                "output_format": "png", "response_format": "b64_json"}
    else:
        d = math.gcd(w, h)
        body = {"model": job.get("model", MODEL), "prompt": job["prompt"], "size": f"{w}x{h}", "aspect_ratio": f"{w // d}:{h // d}",
                "quality": job.get("quality", "high"), "response_format": "url", "n": 1}
    if job.get("images"):
        body["images"] = [{"image_url": as_data_url(p)} for p in job["images"]]
        if BOX:
            body["input_fidelity"] = "high"
    return body


def image_of(resp):
    d = resp["data"][0]
    raw = d.get("b64_json") or d.get("url")
    if raw.startswith("http"):
        with urllib.request.urlopen(raw, timeout=300) as r:
            return r.read()
    return base64.b64decode(raw.split(",", 1)[1] if raw.startswith("data:") else raw)


def one(job, out):
    name = job["name"]
    png = os.path.join(out, name + ".png")
    if os.path.exists(png):
        return f"{name}: 已有，跳过"
    body = body_for(job)
    last = ""
    for attempt in range(3):
        try:
            t = time.time()
            resp = call("/images/edits" if job.get("images") else "/images/generations", body)
            data = image_of(resp)
            with open(png, "wb") as f:
                f.write(data)
            with open(os.path.join(out, name + ".json"), "w", encoding="utf-8") as f:
                json.dump({"job": job, "request": {k: v for k, v in body.items() if k not in ("prompt", "images")},
                           "response": {k: v for k, v in resp.items() if k != "data"}}, f, ensure_ascii=False, indent=1)
            return f"{name}: 好了（{body['model']}，{time.time() - t:.0f} 秒，{len(data) // 1024} KB）"
        except urllib.error.HTTPError as e:
            last = f"HTTP {e.code} " + re.sub(r"sk-[A-Za-z0-9_-]+", "sk-***", e.read()[:300].decode("utf-8", "replace"))
            if e.code not in (429, 500, 502, 503, 504):
                break
            time.sleep(20 * (attempt + 1))
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
