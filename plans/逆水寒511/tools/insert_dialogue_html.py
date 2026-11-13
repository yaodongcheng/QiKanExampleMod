# -*- coding: utf-8 -*-
"""把 gen_dialogue_html.py 生成的台词块贴进玩法演示页（幂等替换）。

页面里那一节的正文是**生成物**：`<h2>他们到底说些什么</h2>` 之后、该 `</section>` 之前，
全部由本脚本重写。流程：改台词 → 插进语言 XML → 跑本脚本。
"""
import io
import os
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
GEN = os.path.join(ROOT, "plans", "逆水寒511", "tools", "gen_dialogue_html.py")
PAGE = os.path.join(ROOT, "plans", "逆水寒511-玩法演示.html")
H2 = "<h2>他们到底说些什么</h2>"

TOGGLE = """      <label class="gtoggle"><input type="checkbox" id="showGated"> 显示「条件未满足」的选项</label>
"""

SCRIPT = """<script>
(function(){
  function markSeen(root){
    root.querySelectorAll('details.fnode').forEach(function(d){
      d.addEventListener('toggle', function(){
        var s = d.querySelector('summary');
        if (!s) return;
        if (d.open) s.classList.add('seen');
      });
    });
  }
  markSeen(document);
  // goto 行（整行可点）：收起当前分支，回到目标节点 —— 语义上等于"退回上一级"
  document.addEventListener('click', function(e){
    var a = e.target.closest ? e.target.closest('a.jgotorow') : null;
    if (!a) return;
    e.preventDefault();
    var id = (a.getAttribute('href') || '').slice(1);
    var target = id ? document.getElementById(id) : null;
    if (!target) return;
    var d = a.closest('details.fnode');       // 收起当前这条链
    while (d) {
      d.open = false;
      var up = d.parentElement;
      d = up ? up.closest('details.fnode') : null;
    }
    var p = target;                            // 展开目标所在链
    while (p) {
      if (p.tagName === 'DETAILS') p.open = true;
      p = p.parentElement;
    }
    target.scrollIntoView({block: 'center'});
    target.classList.add('jflash');
    setTimeout(function(){ target.classList.remove('jflash'); }, 1200);
  });
  var cb = document.getElementById('showGated');
  if (cb) {
    cb.addEventListener('change', function(){
      var sec = cb.closest('section');
      if (sec) sec.classList.toggle('show-gated', cb.checked);
    });
  }
})();
</script>
"""

frag = subprocess.run([sys.executable, GEN], capture_output=True, text=True,
                      encoding="utf-8", errors="ignore").stdout
if "dlghead" not in frag:
    raise SystemExit("generator produced nothing; check gen_dialogue_html.py")

page = io.open(PAGE, encoding="utf-8").read()
i = page.index(H2)
sec_end = page.index("</section>", i)
p_start = page.index('<p class="sub">', i)
p_end = page.index("</p>", p_start) + len("</p>")

new = page[:p_end] + "\n" + TOGGLE + frag + SCRIPT + "\n  " + page[sec_end:]
io.open(PAGE, "w", encoding="utf-8", newline="").write(new)

back = io.open(PAGE, encoding="utf-8").read()
print("gated-list:", back.count("gated-list"), "| showGated:", back.count("showGated"),
      "| fnode:", back.count('class="fnode"'))
