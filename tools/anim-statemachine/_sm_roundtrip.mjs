// 一次性往返自检（2026-09-28）：拿**真的** flight.xml 喂给页面 → 调 fullXml() → 与原文逐块 diff。
//
// 🔴 为什么必须做：页面导出时**只重写** <families> / <states> / <edges> 三段 ——
//    凡是没有被解析进模型、或没被写回去的属性，都会在**用户点一次保存**之后静默消失。
//    这正是加 `enter`/`leave`（容器进出动作）时最怕的事：丢了 = 特效永远不出现。
//
// 用法：node _sm_roundtrip.mjs <xml路径>
import { spawn } from 'node:child_process';
import { setTimeout as sleep } from 'node:timers/promises';
import { existsSync, readFileSync, rmSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const PAGE = resolve(HERE, 'statemachine_editor.html');
const XML = resolve(process.argv[2] || resolve(HERE, '../../ModuleData/statemachines/flight.xml'));
const PORT = 9361;

const BROWSERS = [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
];
const CHROME = BROWSERS.find(existsSync);
if (!CHROME) { console.log('没找到 Chrome/Edge'); process.exit(0); }

const UDD = resolve(HERE, 'out', '_shots', '.rt-profile');
const chrome = spawn(CHROME, ['--headless=new', '--disable-gpu', '--no-first-run',
  '--no-default-browser-check', `--remote-debugging-port=${PORT}`, `--user-data-dir=${UDD}`,
  '--window-size=1680,1300', 'about:blank'], { stdio: 'ignore' });

let ws = null, msgId = 0;
const pending = new Map();
function send(method, params = {}, sessionId) {
  const id = ++msgId;
  return new Promise((res, rej) => {
    pending.set(id, { res, rej });
    ws.send(JSON.stringify(sessionId ? { id, method, params, sessionId } : { id, method, params }));
  });
}
const ev = async expr => {
  const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error('页面里抛异常: ' + JSON.stringify(r.exceptionDetails.exception));
  return r.result.value;
};

async function main() {
  let wsUrl = null;
  for (let i = 0; i < 60 && !wsUrl; i++) {
    try {
      const l = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
      const p = l.find(t => t.type === 'page');
      if (p) wsUrl = p.webSocketDebuggerUrl;
    } catch { }
    if (!wsUrl) await sleep(250);
  }
  if (!wsUrl) throw new Error('连不上调试端口');
  ws = new WebSocket(wsUrl);
  ws.addEventListener('message', e => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); p.res(m.result || m); }
  });
  await new Promise(r => ws.addEventListener('open', r, { once: true }));

  await send('Runtime.enable');
  await send('Page.enable');
  await send('Page.navigate', { url: pathToFileURL(PAGE).href });
  await sleep(1500);

  const raw = readFileSync(XML, 'utf8');

  // 🔴 **两条路都要验**（2026-09-28 教训）：
  //    ① 「打开 XML…」→ 走 JS 的 applyXmlText 解析；
  //    ② **全新打开页面直接保存** → 走生成器（Python）烘进页面的 DATA。
  //    两条路的解析器是**两份代码**，只验一条会漏（Python 那份当时连 enter/leave 都没读）。
  const paths = [];
  for (const [label, expr, needApply] of [
    ['① 打开 XML… 后导出', 'fullXml()', true],
    ['② 全新页面直接导出（走生成器烘的 DATA）', 'fullXml()', false],
  ]) {
    if (needApply) {
      const loaded = await ev('applyXmlText(' + JSON.stringify(raw) + ')');
      console.log('载入：', JSON.stringify(loaded));
      // ② 那条要在同一页面上验"没打开过文件"的状态 ⇒ 重新载入页面
    } else {
      await send('Page.navigate', { url: pathToFileURL(PAGE).href });
      await sleep(1200);
    }
    paths.push([label, await ev(expr)]);
  }

  const grab = (s, tag) => {
    const m = s.match(new RegExp('\\n[ \\t]*<' + tag + '>[\\s\\S]*?\\n[ \\t]*</' + tag + '>'));
    return m ? m[0] : '(没找到 <' + tag + '>)';
  };
  let bad = 0;
  for (const [label, out] of paths) {
    console.log('\n── ' + label + ' ──');
    for (const tag of ['families', 'states', 'edges']) {
      const a = grab(raw, tag), b = grab(out, tag);
      if (a === b) { console.log(`✅ <${tag}> 逐字节相同`); continue; }
      bad++;
      console.log(`❌ <${tag}> 有差异：`);
      const al = a.split('\n'), bl = b.split('\n');
      for (let i = 0; i < Math.max(al.length, bl.length); i++) {
        if (al[i] !== bl[i]) console.log(`   原: ${JSON.stringify(al[i])}\n   出: ${JSON.stringify(bl[i])}`);
      }
    }
  }
  console.log(bad === 0 ? '\n结论：两条路都往返无损' : `\n结论：${bad} 处有问题`);
  process.exitCode = bad === 0 ? 0 : 1;
}

main().catch(e => { console.error('ERR', e.message); process.exitCode = 1; })
  .finally(async () => {
    try { ws && ws.close(); } catch { }
    try { chrome.kill(); } catch { }
    await sleep(400);
    try { rmSync(UDD, { recursive: true, force: true }); } catch { }
  });
