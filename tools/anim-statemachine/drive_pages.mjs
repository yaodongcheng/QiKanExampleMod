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
  // 🔴 只断言"没有关于机外的报错" —— 这条测试自己在上面建了一条**重复的**机外边（#27 与 #1 同来源同条件），
  //    断言"全局 0 问题"是**测试自己不干净**（新加的"同来源同条件"检查会正确地把它报出来）
  check('㉑c 这条机外边不会让整台定义失效（校验里没有关于"机外"的报错）',
    (await ev("validate().filter(function(x){return x.indexOf('机外')>=0||x.indexOf('outside')>=0}).length")) === 0,
    JSON.stringify(await ev("validate()")));
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
  check('㉓c 校验里没有关于"机外"的报错（同上：这条测试自己也留了条重复边）',
    (await ev("validate().filter(function(x){return x.indexOf('机外')>=0||x.indexOf('outside')>=0}).length")) === 0,
    JSON.stringify(await ev("validate()")));
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

  // ㉕ 撤销 / 重做（本轮新增：要"自己重连各个节点"就必须有后悔药）
  await ev("closeCtx(); sel=null; histReset(); render()");     // 以当前这一帧为新的历史起点
  const he0 = await ev("edges.length");
  await ev("addEdge('idle','hovermove')");
  const he1 = await ev("edges.length");
  await ev("undo()");
  const he2 = await ev("edges.length");
  await ev("redo()");
  const he3 = await ev("edges.length");
  check('㉕ 撤销：建边后一步回退', he1 === he0 + 1 && he2 === he0, `基线=${he0} 建后=${he1} 撤销后=${he2}`);
  check('㉕b 重做：一步恢复', he3 === he0 + 1, `重做后=${he3}`);
  check('㉕c 撤销到头不再往下掉（且给提示）',
    (await ev("(function(){undo();var n=edges.length;undo();return edges.length===n && notice.indexOf('已经到头')>=0;})()")) === true,
    String(await ev("notice")).slice(0, 36));

  // ㉖ 选中状态后按 Delete 能删（以前只处理"边"⇒ 按了毫无反应）；删错能 Ctrl+Z 回来
  await ev("closeCtx(); elistAll=true; active=0; sel={type:'state',id:'landing_copy'}; notice=''; render()");
  const b26 = await ev("states.filter(s=>s.name==='landing_copy').length");
  await send('Input.dispatchKeyEvent', { type: 'rawKeyDown', key: 'Delete', code: 'Delete', windowsVirtualKeyCode: 46, nativeVirtualKeyCode: 46 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Delete', code: 'Delete', windowsVirtualKeyCode: 46, nativeVirtualKeyCode: 46 });
  await sleep(350);
  const a26 = await ev("states.filter(s=>s.name==='landing_copy').length");
  await ev("undo()");
  const u26 = await ev("states.filter(s=>s.name==='landing_copy').length");
  check('㉖ 选中状态按 Delete 能删掉', b26 === 1 && a26 === 0, `前=${b26} 删后=${a26}`);
  check('㉖b 删错能撤销回来', u26 === 1, `撤销后=${u26}`);

  // ㉗㉘ 播放方式 = **单选开关**，且**不写时长**（用户："循环 or 不循环应该是个单选开关吧""动画本身时长是多少就是多少"）
  await ev("closeCtx(); active=0; sel={type:'state',id:'idle'}; notice=''; render()");
  check('㉗ 状态面板是「循环 / 一次性」单选（循环态：next 锁上、且**没有时长输入框**）',
    (await ev("!!(document.getElementById('s-mode-loop') && document.getElementById('s-mode-once'))"))
      && (await ev("document.getElementById('s-mode-loop').checked"))
      && (await ev("document.getElementById('s-next').disabled === true"))
      && (await ev("document.getElementById('s-dur') === null")), '');
  await ev("document.getElementById('s-mode-once').click()");     // 真点击，不是直接调函数
  await sleep(320);
  check('㉘ 切「一次性」= once=true、next 解锁，且**仍然没有时长输入框**',
    (await ev("stateByName['idle'].once === true && document.getElementById('s-next').disabled === false"))
      && (await ev("document.getElementById('s-dur') === null")), '');
  await ev("document.getElementById('s-mode-loop').click()");
  await sleep(320);
  check('㉘b 切回「循环」= once=false、next 锁上',
    (await ev("stateByName['idle'].once === false && document.getElementById('s-mode-loop').checked")) === true, '');

  // ㉙ 边的「剩余」条件 = **百分比**（不是秒），导出成 anim-rem-pct
  //    用一条**临时边**测，测完截断弹掉（别动真数据 —— 建边类测试的老教训）
  await ev("closeCtx(); active=0; elistAll=true; edges.push({from:'idle',to:'fastmovePitchD',kind:'key',key:'W',mode:'held',blend:'',after:false,phase:false}); sel={type:'edge',id:edges.length-1}; render()");
  const ti = await ev("edges.length-1");
  await ev("(function(){var k=document.getElementById('f-kind'); k.value='anim'; k.onchange({target:{value:'anim'}});})()");
  await sleep(300);
  await ev("(function(){var a=document.getElementById('f-anim'); a.value='remaining'; a.onchange({target:{value:'remaining'}});})()");
  await sleep(300);
  await ev("(function(){var el=document.getElementById('f-lt'); el.value='20'; el.onchange({target:{value:'20'}});})()");
  await sleep(300);
  const exml = await ev("edgesXml().split('\\n')[" + ti + "]");
  check('㉙ 边的「剩余」阈值是**百分比**，导出成 anim-rem-pct',
    (await ev("edges[" + ti + "].kind==='anim' && edges[" + ti + "].anim==='remaining' && edges[" + ti + "].lt==='20'")) === true
      && String(exml).indexOf('anim-rem-pct="20"') >= 0, 'xml=' + exml);
  check('㉙b 百分比范围校验（150 报错、20 通过）',
    (await ev("(function(){var e=edges[" + ti + "];e.lt='150';var bad=validate().some(function(x){return x.indexOf('百分比')>=0});e.lt='20';return bad && !validate().some(function(x){return x.indexOf('百分比')>=0});})()")) === true, '');
  await ev("edges.length = " + ti + "; sel=null; notice=''; commit()");   // 弹掉临时边

  // ㉚ 容器改名必须**级联**（用户实测："我给两个容器改了名字之后 无法拖动了"）
  //    真因：改名只改 groups[i].name、没重建 groupByName ⇒ `groupBox(i, undefined)` 在 `gpos[g.name]` 抛错
  //          ⇒ pointerdown 半路中断 ⇒ 整个容器拖不动。gpos 也没迁 ⇒ 盒子还会跳回默认位。
  await ev("closeCtx(); active=0; sel=null; groups.push({name:'临时容器甲', members:[], entry:''}); reindex(); commit(); fit()");
  await sleep(350);
  const gb30a = await centerOf('.grp[data-g="临时容器甲"]');
  if (gb30a) await dragFrom(gb30a, 40, 30);                        // 先挪一下，制造一个 gpos
  const gp30a = await ev("JSON.stringify(gpos['临时容器甲'])");
  await ev("sel={type:'group', id:'临时容器甲'}; render()");
  await sleep(250);
  await ev("(function(){var el=document.getElementById('g-name'); el.value='临时容器乙'; el.onchange({target:{value:'临时容器乙'}});})()");
  await sleep(350);
  check('㉚ 改容器名：索引重建（新名在 / 旧名没了）+ 拖过的位置(gpos)跟着迁',
    (await ev("!!groupByName['临时容器乙'] && !groupByName['临时容器甲']")) === true
      && (await ev("!!(gpos['临时容器乙'] && !gpos['临时容器甲'])")) === true,
    'gpos ' + gp30a + ' → ' + await ev("JSON.stringify(gpos['临时容器乙'])"));
  check('㉚b 改名当帧 groupBox 不再抛错（拖动的起手就是它）',
    (await ev("(function(){try{var b=groupBox(groupIndexOf('临时容器乙'), groupByName['临时容器乙']);return !!(b && b.w>0)}catch(e){return 'EXC:'+e.message}})()")) === true, '');
  const gp30b = await ev("JSON.stringify(gpos['临时容器乙'])");
  const gb30b = await centerOf('.grp[data-g="临时容器乙"]');
  if (gb30b) await dragFrom(gb30b, -50, 25);                       // 改名之后**真鼠标再拖一次**
  const gp30c = await ev("JSON.stringify(gpos['临时容器乙'])");
  check('㉚c 改容器名之后**真鼠标仍拖得动**（位置真的变了）',
    gb30b !== null && gp30b !== gp30c, 'before=' + gp30b + ' after=' + gp30c);
  await ev("groups = groups.filter(function(x){return x.name!=='临时容器乙'}); delete gpos['临时容器乙']; reindex(); sel=null; notice=''; commit()");

  // ㉛ 从**容器端口**拉线时，预览线起点必须是**容器盒**（不是世界原点）
  //    真因：预览线一律走 `nodeRect(pending)`（给状态用的）⇒ 容器名取不到 pos ⇒ 兜底 {0,0,240,50}
  //          ⇒ 起点落到世界 (240,25) ⇒ 线飞向画面外（用户实测："容器右侧的小圆点想连接时候 线条显示异常"）
  await ev("closeCtx(); active=0; tabs=[{kind:'root'}]; sel=null; notice=''; render(); fit()");
  await sleep(350);
  const ne31 = await ev("edges.length");
  const cp31 = await centerOf('.grp[data-g="Upright"] .port');
  check('㉛ 容器盒右侧有出边端口（拉线的起点）', cp31 !== null, JSON.stringify(cp31));
  if (cp31) {
    await mouse('mousePressed', cp31.x, cp31.y, 1, 1);
    await mouse('mouseMoved', cp31.x + 130, cp31.y + 70, 1, 1);
    await sleep(250);
  }
  const pv31 = await ev("(function(){var a=[].slice.call(document.querySelectorAll('path'));var p=a.filter(function(x){return x.getAttribute('pointer-events')==='none' && x.getAttribute('stroke-dasharray')})[0];return p?p.getAttribute('d'):'';})()");
  const exp31 = await ev("(function(){var b=groupBox(groupIndexOf('Upright'), groupByName['Upright']);return 'M '+(b.x+b.w)+' '+(b.y+b.h/2)+' L';})()");
  check('㉛b 预览线从**容器盒右侧中点**出发（不是世界原点）',
    String(pv31).indexOf(String(exp31)) === 0, 'd=' + pv31 + '  期望前缀=' + exp31);
  if (cp31) await mouse('mouseReleased', cp31.x + 130, cp31.y + 70, 1, 0);
  await sleep(250);
  await ev("edges.length = " + ne31 + "; pending=null; drag=null; notice=''; sel=null; commit()");   // 截断回基线，别污染后面的断言
  check('㉛c 这次拉线没留下脏边', (await ev("edges.length")) === ne31, 'edges=' + await ev("edges.length"));

  // ㉜ 相位边只该写**相位触发器**（用户实测："这里条件有点怪" —— 那个 move-input 是编辑器默认塞的）
  //    依据：FlightAnimConditions L62/L63 `takeoff-trigger`/`land-trigger` 真身就是 `c => false`；
  //          AgentAnimStateMachine `if (e.PhaseForced) continue;` ⇒ 相位边的 when= 运行时不求值。
  await ev("closeCtx(); active=0; tabs=[{kind:'root'}]; sel=null; notice=''; render()");
  const ne32 = await ev("edges.length");
  await ev("addEdge('outside','hoverstart')");
  await sleep(250);
  check('㉜ 机外 → 状态：时机自动是 takeoff-trigger（不再是 move-input）',
    (await ev("(function(){var e=edges[edges.length-1];return e.phase===true && e.pred==='takeoff-trigger'})()")) === true,
    await ev("JSON.stringify({pred:edges[edges.length-1].pred, phase:edges[edges.length-1].phase})"));
  check('㉜b 相位边写普通谓词 ⇒ 校验报错（以前静默通过）',
    (await ev("(function(){var e=edges[edges.length-1];e.pred='move-input';var bad=validate().some(function(x){return x.indexOf('相位时刻')>=0});e.pred='takeoff-trigger';return bad && !validate().some(function(x){return x.indexOf('相位时刻')>=0});})()")) === true, '');
  check('㉜c 相位边**仍是同一套控件**：有 #f-kind（按键/动画 标为用不到）、谓词下拉里**全部谓词都能选**、相位时刻人话置顶',
    (await ev("(function(){sel={type:'edge',id:edges.length-1};render();var k=document.getElementById('f-kind');if(!k)return false;var el=document.getElementById('f-pred');if(!el)return false;var o=[].slice.call(el.options).map(function(x){return x.value});var g=[].slice.call(el.querySelectorAll('optgroup')).map(function(x){return x.label});return o.indexOf('takeoff-trigger')>=0 && o.indexOf('land-trigger')>=0 && o.indexOf('move-input')>=0 && k.options[0].disabled===true && g.length===2;})()")) === true, '');
  await ev("edges.length = " + ne32 + "; pending=null; drag=null; sel=null; notice=''; commit()");
  check('㉜d 清理干净（边数回到基线）', (await ev("edges.length")) === ne32, 'edges=' + await ev("edges.length"));

  // ㉜e 面板要**直接写出正确值**，别让用户猜（用户实测："所以这里我咋填 看不懂"）
  await ev("closeCtx(); active=0; sel={type:'edge', id:edges.findIndex(function(x){return x.from==='outside'})}; render()");
  await sleep(220);
  const txt32 = await ev("document.getElementById('editor').textContent || ''");
  check('㉜e 相位边面板写人话：点明进机「写『起飞』即可」+ 选项是「起飞 —— 空中按跳跃（进机）」（不再甩 c=>false）',
    String(txt32).indexOf('写「起飞」即可') >= 0 && String(txt32).indexOf('空中按跳跃') >= 0
      && String(txt32).indexOf('=> false') < 0,
    String(txt32).replace(/\s+/g, ' ').slice(0, 120));

  // ㉝ 相位边也能写「动画 · 剩余 %」，而且**导出成真的条件**（相位会读它 ⇒ 出机判据）
  //    用**当时确实存在**的状态（`superland` 在前面 ⑭ 已被改名成 `landing` —— 上次这里就是踩了这个）
  await ev("closeCtx(); active=0; sel=null; render()");
  const ne33 = await ev("edges.length");
  await ev("addEdge('landing','outside')");
  await sleep(250);
  check('㉝ 相位边选「按键」被禁（相位读不到按键），但「动画」档是开的',
    (await ev("(function(){var k=document.getElementById('f-kind');return !!k && k.options[0].disabled===true && k.options[1].disabled===false;})()")) === true, '');
  await ev("(function(){var k=document.getElementById('f-kind');k.value='anim';k.onchange({target:{value:'anim'}});})()");
  await sleep(300);
  await ev("(function(){var a=document.getElementById('f-anim');a.value='remaining';a.onchange({target:{value:'remaining'}});})()");
  await sleep(300);
  await ev("(function(){var el=document.getElementById('f-lt');el.value='10';el.onchange({target:{value:'10'}});})()");
  await sleep(300);
  const x33 = await ev("edgesXml().split('\\n')[" + ne33 + "]");
  check('㉝b 导出成 anim="remaining" anim-rem-pct="10" phase="true"',
    String(x33).indexOf('anim="remaining"') >= 0 && String(x33).indexOf('anim-rem-pct="10"') >= 0
      && String(x33).indexOf('phase="true"') >= 0, 'xml=' + x33);
  check('㉝c 相位边写动画条件 ⇒ 校验 0 问题（它是真的，不是误导）',
    (await ev("(function(){return validate().filter(function(x){return x.indexOf('相位')>=0 || x.indexOf('anim-rem-pct')>=0}).length===0;})()")) === true,
    await ev("JSON.stringify(validate())"));
  await ev("edges.length = " + ne33 + "; pending=null; drag=null; sel=null; notice=''; commit()");

  // ㉞ 按键改成**勾选框**：多选 = 同时按着（组合），另有统一的「取反」
  //    用户原话："这四个模式没看懂" + "我还是希望你能够让我来写按键组合以及not否定"
  await ev("closeCtx(); active=0; edges.push({from:'idle',to:'hovermove',kind:'key',keys:'W',blend:'',after:false,phase:false,not:false}); sel={type:'edge',id:edges.length-1}; render()");
  await sleep(250);
  check('㉞ 按键是**勾选框**（8 个键 + 取反），没有"模式"下拉了',
    (await ev("(function(){return document.querySelectorAll('#f-args input[data-k]').length===8 && !!document.getElementById('f-not') && document.getElementById('f-mode')===null && document.getElementById('f-key')===null;})()")) === true,
    '键勾选框=' + await ev("document.querySelectorAll('#f-args input[data-k]').length"));
  await ev("(function(){var b=document.querySelector('#f-args input[data-k=Shift]'); b.checked=true; b.onchange();})()");
  await sleep(250);
  check('㉞b 勾第二个键 = **同时按着**（keys="W+Shift"）',
    (await ev("edges[edges.length-1].keys")) === 'W+Shift', 'keys=' + await ev("edges[edges.length-1].keys"));
  await ev("(function(){var c=document.getElementById('f-not'); c.checked=true; c.onchange({target:{checked:true}});})()");
  await sleep(250);
  const x34 = await ev("edgesXml().split('\\n')[edges.length-1]");
  check('㉞c 勾「取反」⇒ 导出 not="true"（就是 not（W+Shift））',
    String(x34).indexOf('keys="W+Shift"') >= 0 && String(x34).indexOf('not="true"') >= 0, 'xml=' + x34);
  await ev("(function(){var b=document.querySelector('#f-args input[data-k=A]'); b.checked=true; b.onchange();})()");
  await sleep(250);
  check('㉞d 组合能加到 3 个键', (await ev("edges[edges.length-1].keys")) === 'W+Shift+A', 'keys=' + await ev("edges[edges.length-1].keys"));
  await ev("edges.length = edges.length - 1; sel=null; notice=''; commit()");

  // ㉟ 校验能抓两种"结构性错误"（通用，不针对任何节点）：
  //    用户当前草稿就有 冲刺飞行⇄悬浮飞行 两条都写 sprinting ⇒ 互踢；还有一对完全重复的边
  await ev("closeCtx(); active=0; elistAll=true; sel=null; render()");
  const ne35 = await ev("edges.length");
  check('㉟ 校验能抓「互为反向、条件相同 ⇒ 互踢」',
    (await ev("(function(){edges.push({from:'idle',to:'landing',kind:'pred',pred:'moving',blend:'',after:false,phase:false});"
      + "edges.push({from:'landing',to:'idle',kind:'pred',pred:'moving',blend:'',after:false,phase:false});"
      + "var bad=validate().some(function(x){return x.indexOf('互踢')>=0});"
      + "edges.length=" + ne35 + ";commit();return bad;})()")) === true, '');
  check('㉟b 校验能抓「完全重复的边」',
    (await ev("(function(){edges.push({from:'idle',to:'hovermove',kind:'pred',pred:'moving',blend:'',after:false,phase:false});"
      + "edges.push({from:'idle',to:'hovermove',kind:'pred',pred:'moving',blend:'',after:false,phase:false});"
      + "var bad=validate().some(function(x){return x.indexOf('完全重复')>=0});"
      + "edges.length=" + ne35 + ";commit();return bad;})()")) === true, '');
  check('㉟c 校验能抓「同来源 + 同条件（目标不同）⇒ 后面那条永远轮不到」',
    (await ev("(function(){edges.push({from:'idle',to:'landing',kind:'pred',pred:'moving',blend:'',after:false,phase:false});"
      + "edges.push({from:'idle',to:'hovermove',kind:'pred',pred:'moving',blend:'',after:false,phase:false});"
      + "var bad=validate().some(function(x){return x.indexOf('永远轮不到')>=0});"
      + "edges.length=" + ne35 + ";commit();return bad;})()")) === true, '');
  // ㊱ 相位触发器（真身 `c => false`）用在**非相位边**上 = 死边
  //    用户实测：`冲刺飞行 → 超人落地` 写了 takeoff-trigger 却没勾相位驱动
  check('㊱ 校验能抓「相位触发器用在没勾相位驱动的边上 ⇒ 永远不会生效」',
    (await ev("(function(){edges.push({from:'idle',to:'landing',kind:'pred',pred:'takeoff-trigger',blend:'',after:false,phase:false});"
      + "var bad=validate().some(function(x){return x.indexOf('永远不会生效')>=0});"
      + "edges.length=" + ne35 + ";commit();return bad;})()")) === true, '');
  await ev("sel=null; notice=''; commit()");

  // ㊲ 容器页的「入口线」是**推导出来的装饰**（不是边）—— 不能抢走真边的点击/右键
  //    用户实测："这个线为什么删不掉"（右键它拿到的是画布菜单，所以永远没有"删除这条边"）
  await ev("closeCtx(); tabs=[{kind:'root'},{kind:'group',id:groups[0].name}]; active=1; sel=null; render()");
  await sleep(320);
  check('㊲ 容器页画了「入口线」，但它 pointer-events:none（鼠标能穿到下面的真边）',
    (await ev("(function(){var g=document.querySelector('.entry-edge');return !!g && getComputedStyle(g).pointerEvents==='none';})()")) === true,
    'entry-edge 数量=' + await ev("document.querySelectorAll('.entry-edge').length"));
  await ev("closeCtx(); active=0; tabs=[{kind:'root'}]; sel=null; render()");
  await sleep(220);

  // ㊳ Entry 也能**自己连**（用户："难道不是应该我自己连 entry-idle吗"）——
  //    容器页里从 Entry 右侧绿点拉线，落到成员状态 ⇒ 设置容器的 entry
  await ev("closeCtx(); tabs=[{kind:'root'},{kind:'group',id:groups[0].name}]; active=1; sel=null; render()");
  await sleep(320);
  const old38 = await ev("groups[0].entry");
  const mem38 = await ev("groups[0].members[0]");
  const ep38 = await centerOf('[data-entry]');
  check('㊳ 容器页的 Entry 盒有出边绿点（可以自己连入口）', ep38 !== null, JSON.stringify(ep38));
  const nd38 = await centerOf('.node[data-n="' + mem38 + '"]');
  if (ep38 && nd38) await dragFrom(ep38, nd38.x - ep38.x, nd38.y - ep38.y);
  await sleep(220);
  check('㊳b 从 Entry 拉线落到成员 ⇒ 容器入口改成它',
    (await ev("groups[0].entry")) === mem38,
    'entry=' + await ev("groups[0].entry") + '（期望 ' + mem38 + '，原来 ' + old38 + '）');
  await ev("groups[0].entry = " + JSON.stringify(old38) + "; closeCtx(); active=0; tabs=[{kind:'root'}]; sel=null; notice=''; commit()");
  await sleep(220);

  // ㊳c~㊳e **真嵌套**（用户："我想把这俩个组成一个新的子容器 看起来做不到"）
  await ev("closeCtx(); active=1; tabs=[{kind:'root'},{kind:'group',id:groups[0].name}]; sel={type:'group',id:groups[0].name}; render()");
  await sleep(300);
  const ng38 = await ev("(function(){var b=document.getElementById('g-sub'); if(!b) return ''; b.onclick(); return groups[groups.length-1].name;})()");
  await sleep(300);
  check('㊳c 容器页有「＋ 新建子容器」⇒ 父容器的成员里多了一个**容器名**',
    !!ng38 && (await ev("(groups[0].members||[]).indexOf(" + JSON.stringify(ng38) + ")>=0")) === true,
    '子容器=' + ng38);
  await ev("(function(){var sub=" + JSON.stringify(ng38) + ";var s=groupByName[sub];"
    + "['idle','hovermove'].forEach(function(m){groups.forEach(function(o){o.members=(o.members||[]).filter(function(x){return x!==m;});});s.members.push(m);});"
    + "s.entry='hovermove'; groups[0].entry=sub; commit();})()");
  await sleep(300);
  check('㊳d 嵌套后：两状态成为**子容器的直接成员**，父的**叶子展开**里仍然有它们',
    (await ev("(groups[0].members||[]).indexOf('hovermove')<0")) === true
      && (await ev("leafStatesOf(groups[0].name).indexOf('hovermove')>=0")) === true
      && (await ev("parentGroupOf('hovermove')")) === ng38,
    '子成员=' + await ev("JSON.stringify(groupByName[" + JSON.stringify(ng38) + "].members)"));
  check('㊳e 嵌套 + entry 指向子容器 ⇒ 校验 0 问题（入口链最终落到状态）',
    (await ev("validate().length")) === 0, JSON.stringify(await ev("validate()")));
  // ㊳f **画布右键**里也要有「新建子容器」（用户实测："看不见新建子容器按钮" —— 他是在画布右键里找的）
  //     🔴 用**合成事件直接派给 svg**（`ev.target` 必是 svg ⇒ 一定走画布分支），不靠"猜一个空白点"
  await ev("closeCtx(); active=1; tabs=[{kind:'root'},{kind:'group',id:groups[0].name}]; sel=null; render()");
  await sleep(300);
  await ev("(function(){var svg=document.getElementById('svg');var r=svg.getBoundingClientRect();"
    + "svg.dispatchEvent(new MouseEvent('contextmenu',{clientX:Math.round(r.left+40),clientY:Math.round(r.top+40),bubbles:true,cancelable:true}));})()");
  await sleep(300);
  const items38 = await ev("[].slice.call(document.querySelectorAll('#ctx .it')).map(function(x){return x.textContent}).join(' | ')");
  check('㊳f 容器页的**画布右键**里有「＋ 新建子容器」',
    String(items38).indexOf('新建子容器') >= 0 && String(items38).indexOf('新建容器') >= 0,
    '菜单=' + String(items38).slice(0, 120));
  await ev("closeCtx(); active=0; tabs=[{kind:'root'}]; sel=null; render()");
  await ev("groups = groups.filter(function(x){return x.name!==" + JSON.stringify(ng38) + "});"
    + "groups[0].members = groups[0].members.filter(function(m){return m!==" + JSON.stringify(ng38) + "});"
    + "groups[0].members.push('idle','hovermove'); groups[0].entry='hovermove';"
    + "reindex(); sel=null; tabs=[{kind:'root'}]; active=0; notice=''; commit()");
  await sleep(250);
  await ev("sel=null; notice=''; commit()");

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
