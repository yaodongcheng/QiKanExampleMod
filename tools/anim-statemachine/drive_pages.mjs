// 页面「交互自检」：用 CDP 的真实鼠标输入跑一遍关键操作，打印 ✅/❌。
//
// 为什么必须有它（和 check_pages.py / shoot_pages.py 组成三件套）：
//   · check_pages.py  查**语法** —— 抓"JS 整段不跑、页面却看着能显示"。
//   · shoot_pages.py  出**静态画面** —— 抓"渲染不对"（错位、压字、Entry 不在）。
//   · drive_pages.mjs 打**真实输入** —— 抓"画面全对、一交互就废"。
//     🔴 实战教训（2026-09-25，用户报的）：双击进容器/进状态**全废**，根因是双击判定用了
//        `ev.detail >= 2`，而 **Chrome 对 pointerdown 不填点击次数（恒 0）** ⇒ 判断永不成立。
//        静态截图 100% 看不出这种 bug；真输入一按就现原形。所以固化成本脚本。
//
// 用法（在 tools/anim-statemachine 下；需要本机有 Chrome/Edge）：
//     node drive_pages.mjs                       # 跑 statemachine_editor.html
//     node drive_pages.mjs xxx.html              # 相对路径按工具链根解析
import { spawn } from 'node:child_process';
import { setTimeout as sleep } from 'node:timers/promises';
import { existsSync, rmSync } from 'node:fs';
import { resolve, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const PAGE = resolve(HERE, process.argv[2] || 'statemachine_editor.html');
const PORT = 9351;

const BROWSERS = [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
];
const CHROME = BROWSERS.find(existsSync);
if (!CHROME) { console.log('没找到 Chrome/Edge，跳过交互自检'); process.exit(0); }
if (!existsSync(PAGE)) { console.log('页面不存在：' + PAGE + '（先跑生成器）'); process.exit(1); }

const UDD = resolve(HERE, 'out', '_shots', '.drive-profile');
const chrome = spawn(CHROME, ['--headless=new', '--disable-gpu', '--no-first-run',
  '--no-default-browser-check', `--remote-debugging-port=${PORT}`, `--user-data-dir=${UDD}`,
  '--window-size=1680,1300', 'about:blank'], { stdio: 'ignore' });

// 跑完清掉 Chrome 的 --user-data-dir（几十 MB，里面大半是 Chrome 自带的 tflite 模型）——
// 它只在本次运行内有意义（草稿那条用例靠它跨 reload 留住 localStorage），跑完就是垃圾。
// Chrome 刚被 kill 时文件可能还占着句柄 ⇒ 退避重试；实在删不掉也不报错（下次跑还会先清）。
async function cleanProfile() {
  for (let i = 0; i < 6; i++) {
    try { rmSync(UDD, { recursive: true, force: true }); return; }
    catch { await sleep(400); }
  }
}

async function wsUrl() {
  for (let i = 0; i < 80; i++) {
    try {
      const l = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
      const t = l.find(x => x.type === 'page' && x.webSocketDebuggerUrl);
      if (t) return t.webSocketDebuggerUrl;
    } catch {}
    await sleep(250);
  }
  throw new Error('连不上调试端口（Chrome 起不来？）');
}

let id = 0, ws = null;
const w8 = new Map();
const errs = [];
const send = (m, p = {}) => new Promise((res, rej) => {
  const i = ++id; w8.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method: m, params: p }));
});
async function ev(e) {
  const r = await send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) return 'EXC: ' + (r.exceptionDetails.exception?.description || 'err').split('\n')[0];
  return r.result.value;
}
const mouse = (type, x, y, cc, bt) => send('Input.dispatchMouseEvent',
  { type, x, y, button: 'left', buttons: bt, clickCount: cc, pointerType: 'mouse' });

const results = [];
function check(name, ok, detail) { results.push([ok ? 'OK  ' : 'FAIL', name, detail || '']); }
const centerOf = sel => ev(`(() => { const el=document.querySelector('${sel}'); if(!el) return null;
  const r=el.getBoundingClientRect(); return {x:Math.round(r.x+r.width/2), y:Math.round(r.y+r.height/2)}; })()`);
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

async function dragFrom(p, dx, dy) {
  await mouse('mousePressed', p.x, p.y, 1, 1);
  for (let i = 1; i <= 5; i++) await mouse('mouseMoved', p.x + dx * i / 5, p.y + dy * i / 5, 1, 1);
  await mouse('mouseReleased', p.x + dx, p.y + dy, 1, 0);
  await sleep(250);
}
// 真双击：相隔 <480ms 的两组按下/抬起，第二组 clickCount=2
async function doubleClick(p) {
  await mouse('mousePressed', p.x, p.y, 1, 1); await mouse('mouseReleased', p.x, p.y, 1, 0);
  await sleep(90);
  await mouse('mousePressed', p.x, p.y, 2, 1); await mouse('mouseReleased', p.x, p.y, 2, 0);
  await sleep(350);
}

try {
  ws = new WebSocket(await wsUrl());
  await new Promise(r => ws.addEventListener('open', r));
  ws.addEventListener('message', m => {
    const d = JSON.parse(m.data);
    if (d.id && w8.has(d.id)) { const w = w8.get(d.id); w8.delete(d.id);
      d.error ? w.rej(new Error(JSON.stringify(d.error))) : w.res(d.result); return; }
    if (d.method === 'Runtime.exceptionThrown') errs.push((d.params.exceptionDetails.exception?.description || '').split('\n')[0]);
  });
  await send('Runtime.enable'); await send('Page.enable');
  await send('Page.navigate', { url: 'file:///' + PAGE.split(String.fromCharCode(92)).join('/') });
  await sleep(1800);
  await ev('try{localStorage.clear()}catch(e){}');
  await send('Page.reload'); await sleep(1800);

  const boot = await ev(`({states: states.length, edges: edges.length, tabs: tabs.map(t=>t.kind+':'+(t.id||'')), view: view})`);
  check('页面加载', boot && boot.states > 0, JSON.stringify(boot && boot.tabs));
  check('校验通过（0 问题）', same(await ev('validate()'), []), JSON.stringify(await ev('validate()')));
  check('总览不画机器级 Entry（只属于容器）', (await ev("document.querySelectorAll('.entry').length")) === 0, '');
  check('总览只剩机外一个特殊盒（不画「机内任意状态」）', (await ev("document.querySelectorAll('[data-box]').length")) === 1, '');
  check('状态唯一归属：没有任何状态同时属于两个容器',
    await ev("(function(){var o={};for(var i=0;i<groups.length;i++){var m=groups[i].members;for(var j=0;j<m.length;j++){if(o[m[j]])return false;o[m[j]]=1;}}return true;})()"), '');
  check('校验能抓出「一个状态属于两个容器」',
    await ev("(function(){groups[1].members.push(groups[0].members[0]);var r=validate().some(function(s){return s.indexOf('只能归属一个容器')>=0});groups[1].members.pop();return r;})()"), '');

  check('衔接状态带角标（superland 出机 / hoverstart 进机）',
    await ev("document.getElementById('svg').textContent.indexOf('出机') >= 0"), '');

  // ⑫ 画布不能有"隐形边界"：节点要能拖到世界坐标 x<0（用户实测："红框那侧我这么拖都拖不过去"）
  const n12 = await centerOf('.node[data-n="superland"]');
  await mouse('mousePressed', n12.x, n12.y, 1, 1);
  await mouse('mouseMoved', Math.round(n12.x * 0.4), n12.y, 1, 1);
  await mouse('mouseMoved', 30, n12.y, 1, 1);
  await mouse('mouseReleased', 30, n12.y, 1, 0);
  await sleep(250);
  const npx = await ev("pos.superland.x");
  check('⑫ 节点能拖到 x<0（画布没有左边界）', npx < 0, 'pos.superland.x=' + Math.round(npx));

  // ⑬ 内容左侧的空白也要能拖动画布
  await sleep(700);
  const v13 = await ev('JSON.stringify(view)');
  await dragFrom({ x: 140, y: 1050 }, 90, -50);
  check('⑬ 空白处（含内容左侧）都能平移画布', !same(await ev('JSON.stringify(view)'), v13), '');

  // ① 拖状态节点：只有它动，画布不平移
  let v = await ev('JSON.stringify(view)');
  const p1 = await centerOf('.node[data-n="superland"]');
  const before1 = await ev('JSON.stringify(pos.superland)');
  await dragFrom(p1, 70, 45);
  check('① 拖状态节点：节点动 / 画布不平移',
    !same(await ev('JSON.stringify(pos.superland)'), before1) && same(await ev('JSON.stringify(view)'), v),
    'pos=' + await ev('JSON.stringify(pos.superland)'));

  // ② 拖容器盒：单独移动（原来拖它等于拖空白）
  await sleep(700);
  const g = await centerOf('.grp[data-g="Upright"]');
  v = await ev('JSON.stringify(view)');
  await dragFrom(g, 140, 70);
  check('② 拖容器盒：容器单独移动 / 画布不平移',
    !!(await ev('gpos.Upright')) && same(await ev('JSON.stringify(view)'), v),
    'gpos=' + await ev('JSON.stringify(gpos.Upright)'));

  // ③ 拖「机外」盒：单独移动
  await sleep(700);
  const e1 = await centerOf('[data-box="outside"]');
  const eb = await ev('JSON.stringify({x:OUTSIDE.x,y:OUTSIDE.y})');
  v = await ev('JSON.stringify(view)');
  await dragFrom(e1, 90, 60);
  check('③ 拖「机外」盒：单独移动 / 画布不平移',
    !same(await ev('JSON.stringify({x:OUTSIDE.x,y:OUTSIDE.y})'), eb) && same(await ev('JSON.stringify(view)'), v),
    'OUTSIDE=' + await ev('JSON.stringify({x:OUTSIDE.x,y:OUTSIDE.y})'));

  // ④ 拖空白：整体平移画布
  await sleep(700);
  v = await ev('JSON.stringify(view)');
  await dragFrom({ x: 900, y: 1000 }, 60, -40);
  check('④ 拖空白：整体平移画布', !same(await ev('JSON.stringify(view)'), v), '');

  // ⑤ 单击容器（1px 抖动）不该挪动它
  await sleep(700);
  const g2 = await centerOf('.grp[data-g="ProneFamily"]');
  const gp0 = await ev('JSON.stringify(gpos.ProneFamily)');
  await mouse('mousePressed', g2.x, g2.y, 1, 1);
  await mouse('mouseMoved', g2.x + 1, g2.y + 1, 1, 1);
  await mouse('mouseReleased', g2.x + 1, g2.y + 1, 1, 0);
  await sleep(250);
  check('⑤ 单击容器（1px 抖动）不挪位', same(await ev('JSON.stringify(gpos.ProneFamily)'), gp0), '');

  // ⑥ 双击容器 → 进容器页
  await sleep(700);
  let tabs = await ev('JSON.stringify(tabs.map(t=>t.kind+":"+(t.id||"")))');
  await doubleClick(await centerOf('.grp[data-g="Upright"]'));
  let tabs2 = await ev('JSON.stringify(tabs.map(t=>t.kind+":"+(t.id||"")))');
  check('⑥ 双击容器 → 进容器页', tabs2.includes('group:Upright'), tabs2);

  // ⑥b 容器页必须是"钻进容器"：不出现容器自身盒 / 机外 / 任意状态 / 机器级 Entry
  await sleep(300);
  const gv = await ev(`({grp: document.querySelectorAll('.grp').length,
    box: document.querySelectorAll('[data-box]').length,
    entry: document.querySelectorAll('.entry').length,
    nodes: document.querySelectorAll('.node').length,
    tab: (tabs[active]||{}).id})`);
  check('⑥b 容器页不画容器自身盒 / 机外 / 任意状态', gv.grp === 0 && gv.box === 0, JSON.stringify(gv));
  check('⑥c 容器页有本容器的 Entry + 成员节点', gv.entry === 1 && gv.nodes > 0, JSON.stringify(gv));

  // ⑦ 双击状态节点 → 进状态页
  await ev('active=0; render()'); await sleep(300);
  await sleep(700);
  await doubleClick(await centerOf('.node[data-n="superland"]'));
  tabs2 = await ev('JSON.stringify(tabs.map(t=>t.kind+":"+(t.id||"")))');
  check('⑦ 双击状态节点 → 进状态页', tabs2.includes('state:superland'), tabs2);

  // ⑨ 拖画布不应触发文本选择（否则标签会被浏览器蓝色高亮盖住 —— 用户实测）
  await sleep(700);
  await dragFrom({ x: 900, y: 1000 }, 50, -30);
  check('⑨ 拖画布不选中文字', (await ev('String((window.getSelection() || {}).toString() || "")')) === '', '');

  // ⑧ 右键状态节点 → 出菜单
  await ev('active=0; render()'); await sleep(300);
  const n8 = await centerOf('.node[data-n="superland"]');
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: n8.x, y: n8.y, button: 'right', buttons: 2, clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: n8.x, y: n8.y, button: 'right', buttons: 0, clickCount: 1 });
  await sleep(250);
  check('⑧ 右键状态节点 → 弹菜单', await ev("document.getElementById('ctx').classList.contains('on')"), '');

  // ── 建边的三条路（都用"基线 + 截断恢复"，避免断言失败后清理把真边弹掉）──
  // ⑳ 右键菜单建边（可发现的路子）
  await ev("closeCtx(); active=0; sel=null; render()");
  const n20 = await centerOf('.node[data-n="superland"]');
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: n20.x, y: n20.y, button: 'right', buttons: 2, clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: n20.x, y: n20.y, button: 'right', buttons: 0, clickCount: 1 });
  await sleep(250);
  check('⑳ 右键状态节点有「从这里连一条边…」',
    await ev("(function(){return [].slice.call(document.querySelectorAll('#ctx .it')).some(function(e){return e.textContent.indexOf('从这里连一条边')>=0})})()"), '');
  await ev("(function(){var it=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(e){return e.textContent.indexOf('从这里连一条边')>=0})[0]; if(it) it.click();})()");
  await sleep(250);
  check('⑳b 二级菜单列出目标状态 + 容器（标注入口）',
    (await ev("(function(){var c=document.getElementById('ctx');return c.textContent.indexOf('idle')>=0 && c.textContent.indexOf('入口 hovermove')>=0})()")) === true,
    String(await ev("(document.getElementById('ctx').textContent||'').slice(0,60)")));
  const base20 = await ev("edges.length");
  await ev("(function(){var it=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(e){return e.textContent==='idle'})[0]; if(it) it.click();})()");
  await sleep(300);
  check('⑳c 选中目标后真的建了边',
    (await ev("edges.length")) === base20 + 1 && (await ev("edges[" + base20 + "].from")) === 'superland' && (await ev("edges[" + base20 + "].to")) === 'idle',
    'from=' + await ev("edges[" + base20 + "].from"));
  await ev("edges.length = " + base20 + "; sel=null; commit()");

  // ⑳d 最基本那条：状态端口 → 另一个状态节点（在容器页里做 —— 根视图只有 1 个散状态节点，测不了）
  await sleep(700);
  await ev("closeCtx(); openTab({kind:'group', id:'Upright'})");
  await sleep(350);
  const base20d = await ev("edges.length");
  const sp = await centerOf('.node[data-n="idle"] .port');
  const tgt2 = await centerOf('.node[data-n="hovermove"]');
  await mouse('mousePressed', sp.x, sp.y, 1, 1);
  await mouse('mouseMoved', sp.x + 40, sp.y + 20, 1, 1);
  await mouse('mouseMoved', tgt2.x, tgt2.y, 1, 1);
  await mouse('mouseReleased', tgt2.x, tgt2.y, 1, 0);
  await sleep(300);
  check('⑳d 拖状态端口 → 另一个状态节点 = 建边（最基本那条路）',
    (await ev("edges.length")) === base20d + 1 && (await ev("edges[" + base20d + "].from")) === 'idle'
      && (await ev("edges[" + base20d + "].to")) === 'hovermove',
    'from=' + await ev("edges[" + base20d + "].from") + ' to=' + await ev("edges[" + base20d + "].to"));
  await ev("edges.length = " + base20d + "; sel=null; active=0; render()");

  // ⑰ 从容器盒的端口拉出边（来源 = 整个容器）
  await sleep(700);
  await ev("closeCtx(); active=0; sel=null; render()");
  const base17 = await ev("edges.length");
  const p17 = await centerOf('.grp[data-g="Upright"] .port');
  await mouse('mousePressed', p17.x, p17.y, 1, 1);
  await mouse('mouseMoved', p17.x + 40, p17.y + 20, 1, 1);
  check('⑰ 按容器端口时拿到 pending=容器名', (await ev("pending")) === 'Upright', 'pending=' + await ev("pending"));
  const t17 = await centerOf('.node[data-n="superland"]');
  await mouse('mouseMoved', t17.x, t17.y, 1, 1);
  await mouse('mouseReleased', t17.x, t17.y, 1, 0);
  await sleep(300);
  check('⑰b 拖出「容器 → 状态」的边',
    (await ev("edges.length")) === base17 + 1 && (await ev("edges[" + base17 + "].from")) === 'Upright' && (await ev("edges[" + base17 + "].to")) === 'superland',
    'from=' + await ev("edges[" + base17 + "].from") + ' to=' + await ev("edges[" + base17 + "].to"));
  await ev("edges.length = " + base17 + "; sel=null; commit()");

  // ⑱ 拖线落到容器盒 = 直接连到容器
  await sleep(700);
  await ev("closeCtx(); active=0; sel=null; render()");
  const base18 = await ev("edges.length");
  const p18 = await centerOf('.node[data-n="superland"] .port');
  await mouse('mousePressed', p18.x, p18.y, 1, 1);
  await mouse('mouseMoved', p18.x + 40, p18.y + 20, 1, 1);
  const g18 = await centerOf('.grp[data-g="Upright"]');
  await mouse('mouseMoved', g18.x, g18.y, 1, 1);
  await mouse('mouseReleased', g18.x, g18.y, 1, 0);
  await sleep(350);
  check('⑱ 拖线落到容器盒 = 直接建「→ 容器」的边（不再弹选成员）',
    (await ev("edges.length")) === base18 + 1 && (await ev("edges[" + base18 + "].to")) === 'Upright',
    'to=' + await ev("edges[" + base18 + "].to") + ' 条数=' + await ev("edges.length"));
  check('⑱b 界面上把它显示成「容器 → 入口 X」',
    String(await ev("toText('Upright')")).indexOf('入口 hovermove') >= 0, String(await ev("toText('Upright')")));
  await ev("edges.length = " + base18 + "; sel=null; commit()");

  // ⑱c 容器面板要有「入口状态」下拉
  await ev("sel={type:'group', id:'Upright'}; render()");
  await sleep(250);
  check('⑱c 容器面板有「入口状态 entry」下拉（值来自 XML）',
    (await ev("!!document.getElementById('g-entry')")) === true && (await ev("document.getElementById('g-entry').value")) === 'hovermove',
    'value=' + await ev("document.getElementById('g-entry').value"));
  check('⑱d 容器盒上也写出入口', (await ev("document.getElementById('svg').textContent.indexOf('入口 → hovermove') >= 0")) === true, '');
  const v18 = await ev("(function(){var g=groupByName['Upright'],old=g.entry;g.entry='';"
    + "edges.push({from:'superland',to:'Upright',kind:'key',key:'W',mode:'held',blend:'',after:false,phase:false});"
    + "var r=validate().some(function(s){return s.indexOf('没设入口')>=0});"
    + "edges.pop();g.entry=old;return r;})()");
  check('⑱e 校验能抓出「目标是容器但没设入口」', v18 === true, '');
  await ev("sel=null; render()");

  // ⑲ 拖状态进容器盒 = 移动
  await sleep(700);
  await ev("closeCtx(); active=0; sel=null; render()");
  const n19 = await centerOf('.node[data-n="superland"]');
  const g19 = await centerOf('.grp[data-g="ProneFamily"]');
  await mouse('mousePressed', n19.x, n19.y, 1, 1);
  for (let k = 1; k <= 5; k++) await mouse('mouseMoved', n19.x + (g19.x - n19.x) * k / 5, n19.y + (g19.y - n19.y) * k / 5, 1, 1);
  await mouse('mouseReleased', g19.x, g19.y, 1, 0);
  await sleep(350);
  check('⑲ 拖状态到容器盒 = 移进该容器（唯一归属）',
    (await ev("membersOf('ProneFamily').indexOf('superland')>=0")) === true
      && (await ev("groups.every(function(g){return g.name==='ProneFamily'||g.members.indexOf('superland')<0})")) === true,
    await ev("groups.map(function(g){return g.name+':'+g.members.length}).join(' ')"));
  await ev("groups.forEach(function(g){g.members=g.members.filter(function(m){return m!=='superland'})}); sel=null; commit()");

  // ㉑ 机外盒的端点（用户指出："你没给非飞行（机外）加端点"）
  await sleep(700);
  await ev("closeCtx(); active=0; sel=null; render()");
  const base21 = await ev("edges.length");
  const oport = await centerOf('[data-box="outside"] .port');
  check('㉑ 机外盒有出边端口', oport !== null, JSON.stringify(oport));
  await mouse('mousePressed', oport.x, oport.y, 1, 1);
  await mouse('mouseMoved', oport.x + 40, oport.y + 30, 1, 1);
  const t21 = await centerOf('.node[data-n="superland"]');
  await mouse('mouseMoved', t21.x, t21.y, 1, 1);
  await mouse('mouseReleased', t21.x, t21.y, 1, 0);
  await sleep(300);
  check('㉑b 从机外拉线 → 来源=outside 且自动相位驱动',
    (await ev("edges.length")) === base21 + 1 && (await ev("edges[" + base21 + "].from")) === 'outside'
      && (await ev("edges[" + base21 + "].phase")) === true,
    'from=' + await ev("edges[" + base21 + "].from") + ' phase=' + await ev("edges[" + base21 + "].phase"));
  check('㉑c 这条机外边不会让整台定义失效（校验 0 问题）', (await ev("validate().length")) === 0, JSON.stringify(await ev("validate()")));
  await ev("edges.length = " + base21 + "; sel=null; commit()");

  // ㉒ 拖线落到机外盒 = to=outside（自动相位）
  await sleep(700);
  await ev("closeCtx(); active=0; sel=null; render()");
  const base22 = await ev("edges.length");
  const sp22 = await centerOf('.node[data-n="superland"] .port');
  const obox = await centerOf('[data-box="outside"]');
  await mouse('mousePressed', sp22.x, sp22.y, 1, 1);
  await mouse('mouseMoved', sp22.x + 40, sp22.y + 20, 1, 1);
  await mouse('mouseMoved', obox.x, obox.y, 1, 1);
  await mouse('mouseReleased', obox.x, obox.y, 1, 0);
  await sleep(300);
  check('㉒ 拖线落到机外盒 = 建 to=outside（自动相位）',
    (await ev("edges.length")) === base22 + 1 && (await ev("edges[" + base22 + "].to")) === 'outside'
      && (await ev("edges[" + base22 + "].phase")) === true,
    'to=' + await ev("edges[" + base22 + "].to") + ' phase=' + await ev("edges[" + base22 + "].phase"));
  await ev("edges.length = " + base22 + "; sel=null; commit()");

  // ㉓ 目标下拉含 outside，且改一下就自动相位
  await ev("active=0; sel={type:'edge', id:1}; render()");
  await sleep(300);
  check('㉓ 边的「目标」下拉里有 outside',
    (await ev("(function(){var s=document.getElementById('f-to');return [].slice.call(s.options).some(function(o){return o.value==='outside'})})()")) === true, '');
  await ev("(function(){var s=document.getElementById('f-to'); s.value='outside'; s.dispatchEvent(new Event('change'));})()");
  await sleep(300);
  check('㉓b 目标改成机外 ⇒ 自动变相位驱动',
    (await ev("edges[1].to")) === 'outside' && (await ev("edges[1].phase")) === true,
    'to=' + await ev("edges[1].to") + ' phase=' + await ev("edges[1].phase"));
  check('㉓c 校验仍 0 问题', (await ev("validate().length")) === 0, JSON.stringify(await ev("validate()")));
  await ev("edges[1].to='superland'; sel=null; commit()");

  // ㉔ 拉到空白处要有提示
  await sleep(700);
  await ev("closeCtx(); active=0; sel=null; notice=''; render()");
  const sp24 = await centerOf('.node[data-n="superland"] .port');
  await mouse('mousePressed', sp24.x, sp24.y, 1, 1);
  await mouse('mouseMoved', sp24.x + 30, sp24.y + 20, 1, 1);
  await mouse('mouseMoved', 150, 1100, 1, 1);
  await mouse('mouseReleased', 150, 1100, 1, 0);
  await sleep(300);
  check('㉔ 拉到空白处会给提示（不再毫无反应）',
    String(await ev("notice")).indexOf('没落到有效目标') >= 0, String(await ev("notice")).slice(0, 40));
  check('㉔b 空白落点不会建出边', (await ev("edges.length")) === 26, 'edges=' + await ev("edges.length"));
  await ev("notice=''; sel=null; commit()");

  // ⑭ 改状态名（级联）
  await ev("active=0; render()");
  const n14 = await centerOf('.node[data-n="superland"]');
  await mouse('mousePressed', n14.x, n14.y, 1, 1); await mouse('mouseReleased', n14.x, n14.y, 1, 0);
  await sleep(250);
  check('⑭ 选中状态后面板有「状态名」输入框', await ev("!!document.getElementById('s-name')"), '');
  await ev("(function(){var el=document.getElementById('s-name');el.value='landing';el.dispatchEvent(new Event('change'));})()");
  await sleep(350);
  check('⑭b 改名级联到边引用 + tab',
    (await ev("states.some(function(s){return s.name==='landing'})"))
      && !(await ev("edges.some(function(e){return e.to==='superland'||e.from==='superland'})"))
      && (await ev("tabs.some(function(t){return t.id==='landing'})")),
    'tabs=' + await ev("JSON.stringify(tabs)"));
  check('⑭c 改名后校验仍 0 问题', same(await ev('validate()'), []), JSON.stringify(await ev('validate()')));

  // ⑮ 复制 → 粘贴（真剪贴板）
  await ev("clip=null; active=0; render()");
  const n15 = await centerOf('.node[data-n="landing"]');
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: n15.x, y: n15.y, button: 'right', buttons: 2, clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: n15.x, y: n15.y, button: 'right', buttons: 0, clickCount: 1 });
  await sleep(250);
  await ev("(function(){var it=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(e){return e.textContent.indexOf('复制状态')>=0})[0]; if(it) it.click();})()");
  await sleep(250);
  check('⑮ 右键「复制状态」进了剪贴板', await ev("!!(clip && clip.name)"), 'clip=' + await ev("clip && clip.name"));
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: 600, y: 900, button: 'right', buttons: 2, clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: 600, y: 900, button: 'right', buttons: 0, clickCount: 1 });
  await sleep(250);
  check('⑮b 画布右键出现「粘贴状态」',
    await ev("(function(){return [].slice.call(document.querySelectorAll('#ctx .it')).some(function(e){return e.textContent.indexOf('粘贴状态')>=0})})()"), '');
  await ev("(function(){var it=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(e){return e.textContent.indexOf('粘贴状态')>=0})[0]; if(it) it.click();})()");
  await sleep(350);
  check('⑮c 粘贴出新的状态节点', await ev("states.some(function(s){return s.name==='landing_copy'})"), '');

  // ⑯ 边列表按选中聚焦
  await ev("active=0; sel=null; render()");     // 先取消选中，量"全部"的基线
  const allRows = await ev("document.querySelectorAll('#elist .erow').length");
  const n16 = await centerOf('.node[data-n="landing"]');
  await mouse('mousePressed', n16.x, n16.y, 1, 1); await mouse('mouseReleased', n16.x, n16.y, 1, 0);
  await sleep(300);
  const relRows = await ev("document.querySelectorAll('#elist .erow').length");
  const rowsOk = await ev("(function(){return [].slice.call(document.querySelectorAll('#elist .erow')).every(function(r){return (r.querySelector('.txt')||{}).textContent.indexOf('landing')>=0;})})()");
  check('⑯ 选中状态后边列表只列相关边', relRows > 0 && relRows < allRows && rowsOk === true,
    '全部=' + allRows + ' 相关=' + relRows);

  // ⑩⑪ 本地草稿必须**显眼**（用户实测踩过的坑：草稿静默盖住 XML，看着像"改了没生效"）
  await ev(`try{const d={states:DATA.states.concat([{name:'magicIdle',act:'act_magic_idle',dur:'',next:'',clip:'magic_idle',durText:'循环'}]),edges:DATA.edges.concat([{from:'*',to:'magicIdle',kind:'pred',pred:'casting-fallback',blend:'0.15',after:false,phase:false}]),groups:DATA.groups,pos:{},view:{k:1.15,tx:30,ty:70},gpos:{}};localStorage.setItem(LSKEY,JSON.stringify(d));}catch(e){}`);
  await send('Page.reload'); await sleep(1600);
  check('⑩ 旧草稿被载入时弹出显眼提示条',
    await ev("document.getElementById('banner').classList.contains('on')"),
    String(await ev("(document.getElementById('banner').textContent||'').slice(0,90)")));
  check('⑩b 提示条说清了"草稿 vs XML"的差异', (await ev("states.filter(s=>s.name==='magicIdle').length")) === 1, '');
  await ev("(function(){var b=document.querySelector('#banner button.primary'); if(b) b.click();})()");
  await sleep(500);
  check('⑪ 一键「用 XML 覆盖草稿」后 magicIdle 消失、提示条收起',
    (await ev("states.filter(s=>s.name==='magicIdle').length")) === 0
      && !(await ev("document.getElementById('banner').classList.contains('on')")), '');
} catch (e) {
  check('驱动异常', false, e.message);
} finally {
  for (const [s, n, d] of results) console.log('%s %s%s', s, n, d ? '   [' + d + ']' : '');
  const bad = results.filter(r => r[0] === 'FAIL').length;
  console.log('结果：%d 项，%d 项失败', results.length, bad);
  if (errs.length) console.log('页面异常：\n' + errs.join('\n'));
  try { ws && ws.close(); } catch {}
  chrome.kill();
  await cleanProfile();
  process.exit(bad ? 1 : 0);
}
