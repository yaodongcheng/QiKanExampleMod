#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""骑砍2 动画重定向 · 通用对比查看器 —— 本地静态服务器

  浏览器加载 glb/gltf 需要 http 协议（file:// 会被跨域策略拦住）。
  端口被占用时自动往后找。
  数据集用 URL 参数切换：viewer.html?ds=<数据集名>
  可用数据集见 datasets/index.json
"""
import http.server, socketserver, os, sys, socket, webbrowser, threading
import json, shutil, urllib.parse, time

START_PORT, MAX_TRIES = 8810, 24


def pick_port(start, tries):
    for p in range(start, start + tries):
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        try:
            s.bind(("127.0.0.1", p)); s.close(); return p
        except OSError:
            s.close()
    return None


class Handler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, *a):
        pass

    # ---- 把查看器面板里调好的成对动画参数写回 dataset.json（含备份）----
    def _json(self, code, payload):
        b = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(b)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(b)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_POST(self):
        u = urllib.parse.urlparse(self.path)
        if u.path != "/api/save-pairs":
            self._json(404, {"ok": False, "err": "unknown endpoint " + u.path}); return
        try:
            ds = (urllib.parse.parse_qs(u.query).get("ds") or [""])[0].strip()
            if not ds or "/" in ds or "\\" in ds or ds in (".", ".."):
                raise RuntimeError("bad ds")
            n = int(self.headers.get("Content-Length") or 0)
            body = self.rfile.read(n).decode("utf-8")
            data = json.loads(body or "{}")
            pairs = data.get("pairs")
            if not isinstance(pairs, dict):
                raise RuntimeError("body must be {pairs:{...}}")
            dp = os.path.join("datasets", ds, "dataset.json")
            if not os.path.isfile(dp):
                raise RuntimeError("dataset.json not found for " + ds)
            with open(dp, encoding="utf-8") as f:
                cur = json.load(f)
            bdir = os.path.join(os.path.dirname(dp), "_backup")   # 备份统一丢 _backup/，只留最近 10 份
            os.makedirs(bdir, exist_ok=True)
            bak = os.path.join(bdir, os.path.basename(dp).replace(
                ".json", ".bak_%s.json" % time.strftime("%Y%m%d_%H%M%S")))
            shutil.copyfile(dp, bak)
            try:
                olds = sorted(f for f in os.listdir(bdir) if f.startswith("dataset.bak_"))
                for f in olds[:-10]:
                    os.remove(os.path.join(bdir, f))
            except Exception:
                pass
            cur.setdefault("pairs", {})
            cur["pairs"].update(pairs)
            tmp = dp + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(cur, f, ensure_ascii=False, indent=2); f.write("\n")
            os.replace(tmp, dp)
            self._json(200, {"ok": True, "file": dp, "backup": os.path.basename(bak),
                             "count": len(cur["pairs"]), "saved": len(pairs)})
        except Exception as e:
            self._json(500, {"ok": False, "err": "%s: %s" % (type(e).__name__, e)})


def main():
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    port = pick_port(START_PORT, MAX_TRIES)
    if port is None:
        print("找不到可用端口"); return
    socketserver.TCPServer.allow_reuse_address = False

    # 数据集：命令行参数优先（serve.py ue_exec_pair），否则用 index.json 的默认
    ds = (sys.argv[1].strip() if len(sys.argv) > 1 else "") or None
    if ds and not os.path.isfile(os.path.join("datasets", ds, "dataset.json")):
        print("  [warn] 数据集不存在: %s（改用默认）" % ds); ds = None
    if not ds:
        try:
            idx = json.load(open(os.path.join("datasets", "index.json"), encoding="utf-8"))
            ds = idx.get("default") or (idx.get("datasets") or [{}])[0].get("id")
        except Exception:
            ds = None

    httpd = http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler)
    url = "http://127.0.0.1:%d/viewer.html%s" % (port, ("?ds=" + ds) if ds else "")
    print("=" * 64)
    print("  骑砍2 动画重定向 · 通用对比查看器")
    print("  已启动: %s" % url)
    print("  数据集目录: datasets/  (用页面左上角下拉切换)")
    print("  关闭本窗口即停止服务")
    print("=" * 64)
    threading.Timer(0.8, lambda: webbrowser.open(url)).start()
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n已停止")


if __name__ == "__main__":
    main()
