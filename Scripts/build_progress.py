#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""给长任务写一个「会自己刷新的 HTML 进度页」（用户用浏览器开着看）。

用法：
    from build_progress import Progress
    p = Progress(path, "圣丹尼斯地图图标")
    p.update("减面", done=12, total=60)
    p.log("cell 12 / 60 tris 431203 -> 15234")

产物是一个静态 HTML（每 2 秒自动刷新），不需要起服务。
"""
import html
import io
import os
import time


class Progress:
    def __init__(self, path, title="任务"):
        self.path = path
        self.title = title
        self.t0 = time.time()
        self.stage = "启动"
        self.done = 0
        self.total = 0
        self.lines = []
        self.extra = ""
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        self.update(self.stage)

    def log(self, text):
        self.lines.append("[%s] %s" % (time.strftime("%H:%M:%S"), text))
        self.lines = self.lines[-10:]
        self.update(self.stage)

    def update(self, stage=None, done=None, total=None, extra=None):
        if stage is not None:
            self.stage = stage
        if done is not None:
            self.done = done
        if total is not None:
            self.total = total
        if extra is not None:
            self.extra = extra
        elapsed = time.time() - self.t0
        pct = (self.done / self.total * 100.0) if self.total else 0.0
        eta = ""
        if self.done and self.total and self.done < self.total:
            remain = elapsed / self.done * (self.total - self.done)
            eta = "预计剩余 %d 分 %02d 秒" % (int(remain // 60), int(remain % 60))
        bar_w = 100.0 if not self.total else min(100.0, pct)
        rows = "".join(
            "<div class='logline'>%s</div>" % html.escape(x) for x in self.lines)
        page = """<!doctype html>
<html lang="zh"><head><meta charset="utf-8">
<meta http-equiv="refresh" content="2">
<title>$title</title>
<style>
 body{background:#14161a;color:#e6e6e6;font:14px/1.5 "Segoe UI",system-ui,sans-serif;margin:0;padding:28px}
 h1{font-size:20px;margin:0 0 6px}
 .sub{color:#8b93a1;font-size:13px;margin-bottom:18px}
 .bar{background:#232833;border-radius:8px;height:26px;overflow:hidden;margin:10px 0}
 .fill{background:linear-gradient(90deg,#2d7ff9,#43c3ff);height:100%;width:$barw;transition:width .3s}
 .pct{font-size:26px;font-weight:600;margin:6px 0}
 .meta{color:#a8b0be;font-size:13px}
 .stage{display:inline-block;background:#1f2733;border:1px solid #2e3a4d;border-radius:999px;
        padding:3px 12px;margin-bottom:10px;color:#8fd0ff}
 .logbox{margin-top:22px;background:#0f1216;border:1px solid #232833;border-radius:8px;padding:12px;
         font:12px/1.6 Consolas,monospace;color:#9fb0c4}
</style></head><body>
<h1>$title</h1>
<div class="sub">本页每 2 秒自动刷新；关掉浏览器不影响任务。</div>
<div class="stage">当前阶段：$stage</div>
<div class="pct">$pct $counts</div>
<div class="bar"><div class="fill"></div></div>
<div class="meta">已用 $elapsed　$eta　$extra</div>
<div class="logbox">$rows</div>
</body></html>"""
        from string import Template
        page = Template(page).substitute(
            title=html.escape(self.title),
            stage=html.escape(self.stage),
            pct="%.1f%%" % pct,
            counts=("(%d / %d)" % (self.done, self.total)) if self.total else "",
            barw="%.1f%%" % bar_w,
            elapsed="%d 分 %02d 秒" % (int(elapsed // 60), int(elapsed % 60)),
            eta=eta,
            extra=html.escape(self.extra),
            rows=rows or "<div class='logline'>…</div>",
        )
        tmp = self.path + ".tmp"
        io.open(tmp, "w", encoding="utf-8").write(page)
        os.replace(tmp, self.path)
