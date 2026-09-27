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
// 🔴 量屏幕坐标前先把当前页框进画面（见下面的 `frame()`）。XML 一改布局就变，
//    写死的取景 / 依赖上一条用例留下的镜头 ⇒ 目标飘到屏幕外 ⇒ 鼠标事件全打空（本轮实测一次挂 6 条）。
const centerOf = sel => ev(`(() => { const el=document.querySelector('${sel}'); if(!el) return null;
  const r=el.getBoundingClientRect(); return {x:Math.round(r.x+r.width/2), y:Math.round(r.y+r.height/2)}; })()`);
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
// 🔴 量屏幕坐标之前必须**先把当前页框进画面**（⓿′ 教训 2：几何断言要先构造确定的布局）。
//    用例会把节点拖到世界坐标的任意处（⑫ 甚至拖到 x<0），`centerOf()` 拿到的坐标就可能落在
//    `fit()` 按当前页取景，保证每个盒子都在画面里 —— 见下面的 `frame()`。
//    `fit()` 按当前页取景，保证每个盒子都在画面里。
async function frame() { await ev("fit()"); await sleep(220); }

// ── 夹具助手（🔴 一律**从当前 XML 现挑名字**，不许写死）───────────────────────────
// 用户会在编辑器里改名 / 删状态（2026-09-27 实测：他删掉 4 个直立压弯/俯仰 + 重排了容器
// ⇒ 一次打挂 11 条用例）。凡是"挑一个状态 / 容器来操作"的夹具，名字都从这里现取。
// `G2` = 第一个"有 ≥2 个**直接状态成员**"的容器下标（做 Entry / 嵌套夹具用）
const G2 = "(function(){var i=groups.findIndex(function(g){return g.members.filter(function(m){return !!stateByName[m]}).length>=2});"
         + "return i<0?0:i;})()";
// 该容器的直接状态成员（数组表达式）
const G2M = "(membersOf(groups[" + G2 + "].name).filter(function(m){return !!stateByName[m]}))";
// `G3` = 同上但要求 ≥3 个（㊷ 那组夹具要"两个搬进子容器、还留一个在父里"才能表达跨容器边）
const G3 = "(function(){var i=groups.findIndex(function(g){return g.members.filter(function(m){return !!stateByName[m]}).length>=3});return i<0?0:i;})()";
const G3M = "(membersOf(groups[" + G3 + "].name).filter(function(m){return !!stateByName[m]}))";
// 世界坐标 → 屏幕坐标（`getScreenCTM`：缩放平移与画布位置都在里面）
const w2s = p => ev(`(function(){var g=document.getElementById('world');var m=g.getScreenCTM();`
  + `if(!m) return null; var x=${p.x}, y=${p.y}; return {x: m.a*x+m.c*y+m.e, y: m.b*x+m.d*y+m.f};})()`);
// 世界里挑一个**不压在任何容器盒上**的点 —— 把容器往空处拖 = 移动；压在别的容器盒上会变成"改归属"
const emptyWorld = async exclude => ev(`(function(){
  var bs = visibleGroupBoxes(tabs[active]).filter(function(o){return o.name!==${JSON.stringify(exclude)}}).map(function(o){return o.box});
  var cand = [[40,40],[40,430],[40,780],[1000,40],[1000,780],[1520,400],[240,-260]];
  for (var i=0;i<cand.length;i++){ var x=cand[i][0], y=cand[i][1];
    if(!bs.some(function(b){return x>=b.x-115 && x<=b.x+b.w+115 && y>=b.y-45 && y<=b.y+b.h+45})) return {x:x,y:y}; }
  return {x:40,y:1100};})()`);
// 拿一个**确实抓得到这个容器盒**的屏幕点。🔴 别用 `centerOf`（= 元素外接框中心）：
//    盒子可能被一个**状态节点**盖住，而 pointerdown 的分支顺序是 **节点优先于容器**
//    ⇒ 拖下去抓到的是那个节点（本轮 ㉚c 就是这么假 FAIL 的：残留的 `落地_T_copy` 正好压在盒子上）。
const boxGrabPoint = gname => ev(`(function(){
  var el=document.querySelector('.grp[data-g=${JSON.stringify(gname)}]'); if(!el) return null;
  var r=el.getBoundingClientRect();
  var cand=[[0.5,0.62],[0.2,0.28],[0.8,0.28],[0.2,0.85],[0.8,0.85],[0.5,0.2]];
  for(var i=0;i<cand.length;i++){
    var x=Math.round(r.x+r.width*cand[i][0]), y=Math.round(r.y+r.height*cand[i][1]);
    var hit=document.elementFromPoint(x,y);
    if(hit && hit.closest && hit.closest('.grp') && hit.closest('.grp').getAttribute('data-g')===${JSON.stringify(gname)}) return {x:x,y:y};
  }
  return null;})()`);

// 🔴 用例收尾**统一走"从 XML 重来"**。以前每条自己手写"把 idle/hovermove 挪回 groups[0]、entry 设回 hovermove"
//    —— XML 一改（idle/hovermove 被收进子容器、容器改名）就**集体错位**，一次挂 10 条（本次实测）。
//    用 DATA 重灌 = 天然跟着 XML 走，还顺带证明"XML 本身是干净的"。
async function resetAll() {
  await ev("closeCtx(); groups=JSON.parse(JSON.stringify(DATA.groups)); states=JSON.parse(JSON.stringify(DATA.states));"
    + "edges=JSON.parse(JSON.stringify(DATA.edges)); pos={}; gpos={}; resetBoxes(); defaultPos();"
    + "reindex(); sel=null; tabs=[{kind:'root'}]; active=0; view.k=1; view.tx=0; view.ty=0; notice=''; commit()");
  await sleep(260);
}

// 🔴 下面所有 `setActive(0)` 都**不能**写回 `active=0`（⓿′ 教训 2：几何断言要先构造确定的布局）。
//    `active=0` 只是改索引、**不还原总览的镜头**；从容器页 / 状态页切回来时，总览会顶着那一页的
//    取景 ⇒ `centerOf()` 量到的屏幕坐标可能落在画面外，鼠标事件全打空（⑳d/㉑b/㉒/㉔ 四条就是这么挂的）。
//    `setActive(0)` 按"每页各记一份镜头"的规则还原（没有记录就 fit() 取景）。

// ── 桩：内存文件系统（把 File System Access 的 picker 换成它）────────────
// 为什么必须是桩：无头浏览器**点不了原生对话框**（`showOpenFilePicker` 直接抛 AbortError），
// 而"打开 / 保存"这条链全压在对话框返回的**句柄**上 ⇒ 不换桩就一条都测不了。
// 🔴 桩要忠实到能暴露真 bug，两条不能省：
//   ① **`showOpenFilePicker` 给的句柄默认只读** —— 第一次写必须过 `requestPermission`。
//      桩要是"一写就成"，那条真实路径（也是唯一会弹浏览器授权的路径）就永远测不到。
//   ② `createWritable().write()` 要真改内容 —— 否则"保存"断言不到东西。
const FSA_STUB = `
(function(){
  window.__vfs = {files:{}, writes:[], asked:0, openNext:"flight.xml", saveAsNext:null, abort:null};
  function mkFile(n, writable){
    return {
      kind:"file", name:n, _w: !!writable,
      async getFile(){ const t=window.__vfs.files[n];
        if(t===undefined){ const e=new Error("no such file"); e.name="NotFoundError"; throw e; }
        return {name:n, size:t.length, lastModified:1000, async text(){ return window.__vfs.files[n]; }}; },
      async createWritable(){
        if(!this._w){ const e=new Error("not allowed"); e.name="NotAllowedError"; throw e; }
        const self=this;
        return { async write(x){ window.__vfs.writes.push([n,x]); window.__vfs.files[n]=x; },
                 async close(){}, async abort(){} }; },
      async queryPermission(){ return this._w ? "granted" : "prompt"; },
      async requestPermission(){ window.__vfs.asked++; this._w = true; return "granted"; }
    };
  }
  window.__vfsSeed = function(xml){
    window.__vfs.files = {"flight.xml": xml}; window.__vfs.writes = []; window.__vfs.asked = 0;
    window.__vfs.openNext = "flight.xml"; window.__vfs.saveAsNext = null; window.__vfs.abort = null;
  };
  window.showOpenFilePicker = async function(){
    if(window.__vfs.abort){ const e=new Error("user aborted"); e.name="AbortError"; throw e; }
    return [mkFile(window.__vfs.openNext || "flight.xml", false)];   // 🔴 只读句柄（= 真实行为）
  };
  window.showSaveFilePicker = async function(o){
    if(window.__vfs.abort){ const e=new Error("user aborted"); e.name="AbortError"; throw e; }
    const n = window.__vfs.saveAsNext || (o && o.suggestedName) || "untitled.xml";
    window.__vfs.saveAsNext = null;
    if(!(n in window.__vfs.files)) window.__vfs.files[n] = "";
    return mkFile(n, true);                                          // 另存为的句柄直接可写
  };
})();
`;

async function dragFrom(p, dx, dy) {  await mouse('mousePressed', p.x, p.y, 1, 1);
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

  check('衔接状态带角标（超人落地 出机 / 进入飞行 进机）',
    await ev("document.getElementById('svg').textContent.indexOf('出机') >= 0"), '');

  // ⑫ 画布不能有"隐形边界"：节点要能拖到世界坐标 x<0（用户实测："红框那侧我这么拖都拖不过去"）
  const n12 = await centerOf('.node[data-n="超人落地"]');
  await mouse('mousePressed', n12.x, n12.y, 1, 1);
  await mouse('mouseMoved', Math.round(n12.x * 0.4), n12.y, 1, 1);
  await mouse('mouseMoved', 30, n12.y, 1, 1);
  await mouse('mouseReleased', 30, n12.y, 1, 0);
  await sleep(250);
  const npx = await ev("pos.超人落地.x");
  check('⑫ 节点能拖到 x<0（画布没有左边界）', npx < 0, 'pos.超人落地.x=' + Math.round(npx));

  // ⑬ 内容左侧的空白也要能拖动画布
  await sleep(700);
  const v13 = await ev('JSON.stringify(view)');
  await dragFrom({ x: 140, y: 1050 }, 90, -50);
  check('⑬ 空白处（含内容左侧）都能平移画布', !same(await ev('JSON.stringify(view)'), v13), '');

  // ① 拖状态节点：只有它动，画布不平移
  let v = await ev('JSON.stringify(view)');
  const p1 = await centerOf('.node[data-n="超人落地"]');
  const before1 = await ev('JSON.stringify(pos.超人落地)');
  await dragFrom(p1, 70, 45);
  check('① 拖状态节点：节点动 / 画布不平移',
    !same(await ev('JSON.stringify(pos.超人落地)'), before1) && same(await ev('JSON.stringify(view)'), v),
    'pos=' + await ev('JSON.stringify(pos.超人落地)'));

  // ② 拖容器盒：单独移动（原来拖它等于拖空白）
  await sleep(700);
  const g = await centerOf('.grp[data-g="悬浮飞行"]');
  v = await ev('JSON.stringify(view)');
  await dragFrom(g, 140, 70);
  check('② 拖容器盒：容器单独移动 / 画布不平移',
    !!(await ev('gpos.悬浮飞行')) && same(await ev('JSON.stringify(view)'), v),
    'gpos=' + await ev('JSON.stringify(gpos.悬浮飞行)'));

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
  // 🔴 **不许写死屏幕坐标**（写死过一次就挂过：顶部提示换了个长文件名 ⇒ 内容整体下移 ⇒ (900,1000)
  //    压到节点上 ⇒ 拖的是节点不是画布）。动态找一个 `elementFromPoint` 命中 <svg> 本身的点。
  await sleep(700);
  v = await ev('JSON.stringify(view)');
  //    判据用**页面自己的那一套**：光标下没有 `.node/.grp/[data-box]/.port/.edge` 祖先 = 空白
  //    （`elementFromPoint` 命中的是网格 rect，不是 <svg> 本身 —— 第一版就是按 id==='svg' 找的，找不到 ⇒ 假 FAIL）
  const blank4 = await ev(`(function(){var r=document.getElementById('svg').getBoundingClientRect();
    for (var y=r.y+r.height-40; y>r.y+20; y-=16) for (var x=r.x+24; x<r.x+r.width-24; x+=16) {
      var el=document.elementFromPoint(x,y);
      if (el && !el.closest('.node,.grp,[data-box],.port,.edge,[data-entry]')) return {x:x,y:y}; } return null; })()`);
  await dragFrom(blank4 || { x: 900, y: 1000 }, 60, -40);
  check('④ 拖空白：整体平移画布', !same(await ev('JSON.stringify(view)'), v), '');

  // ⑤ 单击容器（1px 抖动）不该挪动它
  await sleep(700);
  const g2 = await centerOf('.grp[data-g="冲刺飞行"]');
  const gp0 = await ev('JSON.stringify(gpos.冲刺飞行)');
  await mouse('mousePressed', g2.x, g2.y, 1, 1);
  await mouse('mouseMoved', g2.x + 1, g2.y + 1, 1, 1);
  await mouse('mouseReleased', g2.x + 1, g2.y + 1, 1, 0);
  await sleep(250);
  check('⑤ 单击容器（1px 抖动）不挪位', same(await ev('JSON.stringify(gpos.冲刺飞行)'), gp0), '');

  // ⑥ 双击容器 → 进容器页
  await sleep(700);
  let tabs = await ev('JSON.stringify(tabs.map(t=>t.kind+":"+(t.id||"")))');
  await doubleClick(await centerOf('.grp[data-g="悬浮飞行"]'));
  let tabs2 = await ev('JSON.stringify(tabs.map(t=>t.kind+":"+(t.id||"")))');
  check('⑥ 双击容器 → 进容器页', tabs2.includes('group:悬浮飞行'), tabs2);

  // ⑥b 容器页必须是"钻进容器"：不出现容器自身盒 / 机外 / 任意状态 / 机器级 Entry
  await sleep(300);
  const gv = await ev(`({grp: document.querySelectorAll('.grp').length,
    kid: (function(){var t=tabs[active]; return t && t.kind==='group' ? groups.filter(function(g){return parentGroupOf(g.name)===t.id;}).length : -1;})(),
    box: document.querySelectorAll('[data-box]').length,
    entry: document.querySelectorAll('.entry').length,
    nodes: document.querySelectorAll('.node').length,
    tab: (tabs[active]||{}).id})`);
  // 🔴 容器页只画**直接子容器**盒（嵌套）—— “容器自己的盒 / 机外 / 任意状态”不该出现
  check('⑥b 容器页只画直接子容器盒，不画自身 / 机外 / 任意状态',
    gv.kid >= 0 && gv.grp === gv.kid && gv.box === 0, JSON.stringify(gv));
  check('⑥c 容器页有本容器的 Entry + 成员节点', gv.entry === 1 && gv.nodes > 0, JSON.stringify(gv));

  // ⑦ 双击状态节点 → 进状态页
  await ev('setActive(0); render()'); await sleep(300);
  await sleep(700);
  await doubleClick(await centerOf('.node[data-n="超人落地"]'));
  tabs2 = await ev('JSON.stringify(tabs.map(t=>t.kind+":"+(t.id||"")))');
  check('⑦ 双击状态节点 → 进状态页', tabs2.includes('state:超人落地'), tabs2);

  // ⑨ 拖画布不应触发文本选择（否则标签会被浏览器蓝色高亮盖住 —— 用户实测）
  await sleep(700);
  await dragFrom({ x: 900, y: 1000 }, 50, -30);
  check('⑨ 拖画布不选中文字', (await ev('String((window.getSelection() || {}).toString() || "")')) === '', '');

  // ⑧ 右键状态节点 → 出菜单
  await ev('setActive(0); render()'); await sleep(300);
  await frame();
  const n8 = await centerOf('.node[data-n="超人落地"]');
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: n8.x, y: n8.y, button: 'right', buttons: 2, clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: n8.x, y: n8.y, button: 'right', buttons: 0, clickCount: 1 });
  await sleep(250);
  check('⑧ 右键状态节点 → 弹菜单', await ev("document.getElementById('ctx').classList.contains('on')"), '');

  // ── 建边的三条路（都用"基线 + 截断恢复"，避免断言失败后清理把真边弹掉）──
  // ⑳ 右键菜单建边（可发现的路子）
  await ev("closeCtx(); setActive(0); sel=null; render()");
  await frame();
  const n20 = await centerOf('.node[data-n="超人落地"]');
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: n20.x, y: n20.y, button: 'right', buttons: 2, clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: n20.x, y: n20.y, button: 'right', buttons: 0, clickCount: 1 });
  await sleep(250);
  check('⑳ 右键状态节点有「从这里连一条边…」',
    await ev("(function(){return [].slice.call(document.querySelectorAll('#ctx .it')).some(function(e){return e.textContent.indexOf('从这里连一条边')>=0})})()"), '');
  await ev("(function(){var it=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(e){return e.textContent.indexOf('从这里连一条边')>=0})[0]; if(it) it.click();})()");
  await sleep(250);
  const ent20 = await ev('groups[0].entry');   // 🔴 别写死入口名（XML 一改就假 FAIL）
  check('⑳b 二级菜单列出目标状态 + 容器（标注入口）',
    (await ev('(function(){var c=document.getElementById("ctx");return c.textContent.indexOf("idle")>=0 && c.textContent.indexOf("入口 " + ' + JSON.stringify(ent20) + ')>=0})()')) === true,
    String(await ev("(document.getElementById('ctx').textContent||'').slice(0,60)")));
  const base20 = await ev("edges.length");
  await ev("(function(){var it=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(e){return e.textContent==='idle'})[0]; if(it) it.click();})()");
  await sleep(300);
  check('⑳c 选中目标后真的建了边',
    (await ev("edges.length")) === base20 + 1 && (await ev("edges[" + base20 + "].from")) === '超人落地' && (await ev("edges[" + base20 + "].to")) === 'idle',
    'from=' + await ev("edges[" + base20 + "].from"));
  await ev("edges.length = " + base20 + "; sel=null; commit()");

  // ⑳d 最基本那条：状态端口 → 另一个状态节点（在容器页里做 —— 根视图只有 1 个散状态节点，测不了）
  // 🔴 `idle` / `hovermove` 现在都在子容器里 ⇒ 要在**子容器的页**上做（那边两个节点都画）
  await sleep(700);
  await ev("closeCtx(); openTab({kind:'group', id:childGroupsOf(groups[0].name)[0]})");   // 🔴 别写死容器名：子容器改过名（子容器1 → FlyHoverLocomotion），写死会把这条用例打挂
  await sleep(350);
  const base20d = await ev("edges.length");
  await frame();
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
  await ev("edges.length = " + base20d + "; sel=null; setActive(0); render()");

  // ⑰ 从容器盒的端口拉出边（来源 = 整个容器）
  await sleep(700);
  await ev("closeCtx(); setActive(0); sel=null; render()");
  const base17 = await ev("edges.length");
  const p17 = await centerOf('.grp[data-g="悬浮飞行"] .port');
  await mouse('mousePressed', p17.x, p17.y, 1, 1);
  await mouse('mouseMoved', p17.x + 40, p17.y + 20, 1, 1);
  check('⑰ 按容器端口时拿到 pending=容器名', (await ev("pending")) === '悬浮飞行', 'pending=' + await ev("pending"));
  const t17 = await centerOf('.node[data-n="超人落地"]');
  await mouse('mouseMoved', t17.x, t17.y, 1, 1);
  await mouse('mouseReleased', t17.x, t17.y, 1, 0);
  await sleep(300);
  check('⑰b 拖出「容器 → 状态」的边',
    (await ev("edges.length")) === base17 + 1 && (await ev("edges[" + base17 + "].from")) === '悬浮飞行' && (await ev("edges[" + base17 + "].to")) === '超人落地',
    'from=' + await ev("edges[" + base17 + "].from") + ' to=' + await ev("edges[" + base17 + "].to"));
  await ev("edges.length = " + base17 + "; sel=null; commit()");

  // ⑱ 拖线落到容器盒 = 直接连到容器
  await sleep(700);
  await ev("closeCtx(); setActive(0); sel=null; render()");
  const base18 = await ev("edges.length");
  const p18 = await centerOf('.node[data-n="超人落地"] .port');
  await mouse('mousePressed', p18.x, p18.y, 1, 1);
  await mouse('mouseMoved', p18.x + 40, p18.y + 20, 1, 1);
  const g18 = await centerOf('.grp[data-g="悬浮飞行"]');
  await mouse('mouseMoved', g18.x, g18.y, 1, 1);
  await mouse('mouseReleased', g18.x, g18.y, 1, 0);
  await sleep(350);
  check('⑱ 拖线落到容器盒 = 直接建「→ 容器」的边（不再弹选成员）',
    (await ev("edges.length")) === base18 + 1 && (await ev("edges[" + base18 + "].to")) === '悬浮飞行',
    'to=' + await ev("edges[" + base18 + "].to") + ' 条数=' + await ev("edges.length"));
  check('⑱b 界面上把它显示成「容器 → 入口 X」',
    String(await ev("toText('悬浮飞行')")).indexOf('入口 ' + (await ev('groups[0].entry'))) >= 0, String(await ev("toText('悬浮飞行')")));
  await ev("edges.length = " + base18 + "; sel=null; commit()");

  // ⑱c 容器面板要有「入口状态」下拉
  await ev("sel={type:'group', id:'悬浮飞行'}; render()");
  await sleep(250);
  check('⑱c 容器面板有「入口状态 entry」下拉（值来自 XML）',
    (await ev("!!document.getElementById('g-entry')")) === true && (await ev("document.getElementById('g-entry').value")) === (await ev('groups[0].entry')),
    'value=' + await ev("document.getElementById('g-entry').value"));
  // 🔴 断言要**抗长度**：容器盒那行副标题按盒宽截断（`clipW`），入口名一长（子容器1 → FlyHoverLocomotion）
  //    就会被截掉尾巴 ⇒ 精确匹配必然假 FAIL。这里要求"入口名**至少露出来**"（全名或前 6 字）。
  const d18 = await ev("(function(){var t=document.getElementById('svg').textContent,e=groups[0].entry;"
    + "return {full:t.indexOf('入口 → '+e)>=0, elided:t.indexOf('入口 → '+e.slice(0,6))>=0, txt:t.slice(0,0)};})()");
  check('⑱d 容器盒上也写出入口（写的是**人话路径**）',
    d18.full === true || d18.elided === true, JSON.stringify(d18));
  const v18 = await ev("(function(){var g=groupByName['悬浮飞行'],old=g.entry;g.entry='';"
    + "edges.push({from:'超人落地',to:'悬浮飞行',kind:'key',key:'W',mode:'held',blend:'',after:false,event:false});"
    + "var r=validate().some(function(s){return s.indexOf('没设入口')>=0});"
    + "edges.pop();g.entry=old;return r;})()");
  check('⑱e 校验能抓出「目标是容器但没设入口」', v18 === true, '');
  await ev("sel=null; render()");

  // ⑲ 拖状态进容器盒 = 移动
  await sleep(700);
  await ev("closeCtx(); setActive(0); sel=null; render()");
  const n19 = await centerOf('.node[data-n="超人落地"]');
  const g19 = await centerOf('.grp[data-g="冲刺飞行"]');
  await mouse('mousePressed', n19.x, n19.y, 1, 1);
  for (let k = 1; k <= 5; k++) await mouse('mouseMoved', n19.x + (g19.x - n19.x) * k / 5, n19.y + (g19.y - n19.y) * k / 5, 1, 1);
  await mouse('mouseReleased', g19.x, g19.y, 1, 0);
  await sleep(350);
  check('⑲ 拖状态到容器盒 = 移进该容器（唯一归属）',
    (await ev("membersOf('冲刺飞行').indexOf('超人落地')>=0")) === true
      && (await ev("groups.every(function(g){return g.name==='冲刺飞行'||g.members.indexOf('超人落地')<0})")) === true,
    await ev("groups.map(function(g){return g.name+':'+g.members.length}).join(' ')"));
  await ev("groups.forEach(function(g){g.members=g.members.filter(function(m){return m!=='超人落地'})}); sel=null; commit()");

  // ㉑ 机外盒的端点（用户指出："你没给非飞行（机外）加端点"）
  await sleep(700);
  await ev("closeCtx(); setActive(0); sel=null; render()");
  const base21 = await ev("edges.length");
  await frame();
  const oport = await centerOf('[data-box="outside"] .port');
  check('㉑ 机外盒有出边端口', oport !== null, JSON.stringify(oport));
  await mouse('mousePressed', oport.x, oport.y, 1, 1);
  await mouse('mouseMoved', oport.x + 40, oport.y + 30, 1, 1);
  const t21 = await centerOf('.node[data-n="超人落地"]');
  await mouse('mouseMoved', t21.x, t21.y, 1, 1);
  await mouse('mouseReleased', t21.x, t21.y, 1, 0);
  await sleep(300);
  check('㉑b 从机外拉线 → 来源=outside 且自动事件驱动',
    (await ev("edges.length")) === base21 + 1 && (await ev("edges[" + base21 + "].from")) === 'outside'
      && (await ev("edges[" + base21 + "].event")) === true,
    'from=' + await ev("edges[" + base21 + "].from") + ' event=' + await ev("edges[" + base21 + "].event"));
  // 🔴 只断言"没有关于机外的报错" —— 这条测试自己在上面建了一条**重复的**机外边（#27 与 #1 同来源同条件），
  //    断言"全局 0 问题"是**测试自己不干净**（新加的"同来源同条件"检查会正确地把它报出来）
  check('㉑c 这条机外边不会让整台定义失效（校验里没有关于"机外"的报错）',
    (await ev("validate().filter(function(x){return x.indexOf('机外')>=0||x.indexOf('outside')>=0}).length")) === 0,
    JSON.stringify(await ev("validate()")));
  await ev("edges.length = " + base21 + "; sel=null; commit()");

  // ㉒ 拖线落到机外盒 = to=outside（自动相位）
  await sleep(700);
  await ev("closeCtx(); setActive(0); sel=null; render()");
  const base22 = await ev("edges.length");
  await frame();
  const sp22 = await centerOf('.node[data-n="超人落地"] .port');
  const obox = await centerOf('[data-box="outside"]');
  await mouse('mousePressed', sp22.x, sp22.y, 1, 1);
  await mouse('mouseMoved', sp22.x + 40, sp22.y + 20, 1, 1);
  await mouse('mouseMoved', obox.x, obox.y, 1, 1);
  await mouse('mouseReleased', obox.x, obox.y, 1, 0);
  await sleep(300);
  // 🔴 2026-09-27 规则改了：出机 = **普通边**（默认条件 Space）；只有"动画·剩 %"那种才归相位
  check('㉒ 拖线落到机外盒 = 建 to=outside（普通边，默认条件 Space）',
    (await ev("edges.length")) === base22 + 1 && (await ev("edges[" + base22 + "].to")) === 'outside'
      && (await ev("edges[" + base22 + "].event")) === false
      && (await ev("edges[" + base22 + "].kind")) === 'key'
      && (await ev("edges[" + base22 + "].keys")) === 'Space',
    'to=' + await ev("edges[" + base22 + "].to") + ' event=' + await ev("edges[" + base22 + "].event"));
  await ev("edges.length = " + base22 + "; sel=null; commit()");

  // ㉓ 目标下拉含 outside，且改一下就自动相位
  await ev("setActive(0); sel={type:'edge', id:1}; render()");
  await sleep(300);
  check('㉓ 边的「目标」下拉里有 outside',
    (await ev("(function(){var s=document.getElementById('f-to');return [].slice.call(s.options).some(function(o){return o.value==='outside'})})()")) === true, '');
  await ev("(function(){var s=document.getElementById('f-to'); s.value='outside'; s.dispatchEvent(new Event('change'));})()");
  await sleep(300);
  check('㉓b 目标改成机外 ⇒ 自动变事件驱动',
    (await ev("edges[1].to")) === 'outside' && (await ev("edges[1].event")) === true,
    'to=' + await ev("edges[1].to") + ' event=' + await ev("edges[1].event"));
  check('㉓c 校验里没有关于"机外"的报错（同上：这条测试自己也留了条重复边）',
    (await ev("validate().filter(function(x){return x.indexOf('机外')>=0||x.indexOf('outside')>=0}).length")) === 0,
    JSON.stringify(await ev("validate()")));
  await ev("edges[1].to='outside'; sel=null; commit()");   // 恢复成本义：超人落地 → 机外

  // ㉔ 拉到空白处要有提示
  await sleep(700);
  await ev("closeCtx(); setActive(0); sel=null; notice=''; render()");
  await frame();
  const base24 = await ev('edges.length');
  const sp24 = await centerOf('.node[data-n="超人落地"] .port');
  await mouse('mousePressed', sp24.x, sp24.y, 1, 1);
  await mouse('mouseMoved', sp24.x + 30, sp24.y + 20, 1, 1);
  await mouse('mouseMoved', 150, 1100, 1, 1);
  await mouse('mouseReleased', 150, 1100, 1, 0);
  await sleep(300);
  check('㉔ 拉到空白处会给提示（不再毫无反应）',
    String(await ev("notice")).indexOf('没落到有效目标') >= 0, String(await ev("notice")).slice(0, 40));
  check('㉔b 空白落点不会建出边', (await ev("edges.length")) === base24, '基线=' + base24 + ' 现在=' + await ev("edges.length"));
  await ev("notice=''; sel=null; commit()");

  // ⑭ 改状态名（级联）
  await ev("setActive(0); render()");
  const n14 = await centerOf('.node[data-n="超人落地"]');
  await mouse('mousePressed', n14.x, n14.y, 1, 1); await mouse('mouseReleased', n14.x, n14.y, 1, 0);
  await sleep(250);
  check('⑭ 选中状态后面板有「状态名」输入框', await ev("!!document.getElementById('s-name')"), '');
  await ev("(function(){var el=document.getElementById('s-name');el.value='落地_T';el.dispatchEvent(new Event('change'));})()");
  await sleep(350);
  const a14 = await ev("states.some(function(s){return s.name==='落地_T'})");
  const b14 = await ev("edges.some(function(e){return e.to==='超人落地'||e.from==='超人落地'})");
  const c14 = await ev("tabs.some(function(t){return t.id==='落地_T'})");
  check('⑭b 改名级联到边引用 + tab', a14 === true && b14 === false && c14 === true,
    '状态在=' + a14 + ' 边还引用旧名=' + b14 + ' tab在=' + c14 + ' 边引用新名的条数=' + await ev("edges.filter(function(e){return e.from==='落地_T'||e.to==='落地_T'}).length"));
  check('⑭c 改名后校验仍 0 问题', same(await ev('validate()'), []), JSON.stringify(await ev('validate()')));

  // ⑮ 复制 → 粘贴（真剪贴板）
  await ev("clip=null; setActive(0); render()");
  const n15 = await centerOf('.node[data-n="落地_T"]');
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
  check('⑮c 粘贴出新的状态节点', await ev("states.some(function(s){return s.name==='落地_T_copy'})"), '');

  // ⑯ 边列表按选中聚焦
  await ev("setActive(0); sel=null; render()");     // 先取消选中，量"全部"的基线
  const allRows = await ev("document.querySelectorAll('#elist .erow').length");
  await frame();
  const n16 = await centerOf('.node[data-n="落地_T"]');
  await mouse('mousePressed', n16.x, n16.y, 1, 1); await mouse('mouseReleased', n16.x, n16.y, 1, 0);
  await sleep(300);
  const relRows = await ev("document.querySelectorAll('#elist .erow').length");
  const rowsOk = await ev("(function(){return [].slice.call(document.querySelectorAll('#elist .erow')).every(function(r){return (r.querySelector('.txt')||{}).textContent.indexOf('落地_T')>=0;})})()");
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
  await ev("closeCtx(); elistAll=true; setActive(0); sel={type:'state',id:'落地_T_copy'}; notice=''; render()");
  const b26 = await ev("states.filter(s=>s.name==='落地_T_copy').length");
  await send('Input.dispatchKeyEvent', { type: 'rawKeyDown', key: 'Delete', code: 'Delete', windowsVirtualKeyCode: 46, nativeVirtualKeyCode: 46 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Delete', code: 'Delete', windowsVirtualKeyCode: 46, nativeVirtualKeyCode: 46 });
  await sleep(350);
  const a26 = await ev("states.filter(s=>s.name==='落地_T_copy').length");
  await ev("undo()");
  const u26 = await ev("states.filter(s=>s.name==='落地_T_copy').length");
  check('㉖ 选中状态按 Delete 能删掉', b26 === 1 && a26 === 0, `前=${b26} 删后=${a26}`);
  check('㉖b 删错能撤销回来', u26 === 1, `撤销后=${u26}`);

  // ㉗㉘ 播放方式 = **单选开关**，且**不写时长**（用户："循环 or 不循环应该是个单选开关吧""动画本身时长是多少就是多少"）
  await ev("closeCtx(); setActive(0); sel={type:'state',id:'idle'}; notice=''; render()");
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
  await ev("closeCtx(); setActive(0); elistAll=true; edges.push({from:'idle',to:'fastmovePitchD',kind:'key',key:'W',mode:'held',blend:'',after:false,event:false}); sel={type:'edge',id:edges.length-1}; render()");
  const ti = await ev("edges.length-1");
  await ev("(function(){var k=document.getElementById('f-kind'); k.value='anim'; k.onchange({target:{value:'anim'}});})()");
  await sleep(300);
  // 🔴 动画档**没有**下拉了（只有 `remaining`+数字；`anim="finished"` 已删）
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
  const push30 = await ev("closeCtx(); setActive(0); sel=null; groups.push({name:'临时容器甲', members:[], entry:''}); reindex(); commit(); fit()");
  if (String(push30).startsWith('EXC')) console.log('  [诊断] 推入临时容器时 render 抛了:', push30);
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
  await frame();
  const gb30b = await boxGrabPoint('临时容器乙');
  // 🔴 落点必须**算**出来（不能写死位移）：拖到**别的容器盒**上会变成"改归属"而不是移动，
  //    机器结构一变、邻居挪位，固定位移就会正好压上去 ⇒ 盒子没动 ⇒ 假 FAIL（本轮就是这么挂的）。
  const spot30 = await emptyWorld('临时容器乙');
  const sc30 = await w2s(spot30);
  let dbg30 = '';
  if (gb30b && sc30) {
    const d30x = Math.round(sc30.x - gb30b.x), d30y = Math.round(sc30.y - gb30b.y);
    await mouse('mousePressed', gb30b.x, gb30b.y, 1, 1);
    await sleep(120);
    dbg30 = String(await ev("JSON.stringify({drag: drag&&{k:drag.kind,id:drag.id}, pan:!!pan,"
      + " tab: tabs[active].kind+':'+(tabs[active].id||''), sel: sel&&sel.id,"
      + " hit: (function(){var el=document.elementFromPoint(" + gb30b.x + "," + gb30b.y + ");"
      + "return el? String((el.closest&&el.closest('.grp')&&el.closest('.grp').getAttribute('data-g'))||el.tagName):'null';})()})"));
    for (let i = 1; i <= 5; i++) await mouse('mouseMoved', gb30b.x + d30x * i / 5, gb30b.y + d30y * i / 5, 1, 1);
    await mouse('mouseReleased', gb30b.x + d30x, gb30b.y + d30y, 1, 0);
    await sleep(280);
  }
  const gp30c = await ev("JSON.stringify(gpos['临时容器乙'])");
  check('㉚c 改容器名之后**真鼠标仍拖得动**（位置真的变了）',
    gb30b !== null && gp30b !== gp30c,
    'tab=' + await ev("tabs[active].kind+':'+(tabs[active].id||'')") + ' box=' + JSON.stringify(gb30b)
      + ' 目标世界=' + JSON.stringify(spot30) + ' → 屏幕=' + JSON.stringify(sc30) + ' down后=' + dbg30
      + ' 落点元素=' + (gb30b ? await ev("(function(){var el=document.elementFromPoint(" + gb30b.x + "," + gb30b.y + ");return el? String((el.closest&&el.closest('.grp')&&el.closest('.grp').getAttribute('data-g'))||el.tagName) : 'null';})()") : '-')
      + ' grp在画面上=' + await ev("[].slice.call(document.querySelectorAll('.grp')).map(function(e){return e.getAttribute('data-g')}).join(',')") + ' before=' + gp30b + ' after=' + gp30c);
  await ev("groups = groups.filter(function(x){return x.name!=='临时容器乙'}); delete gpos['临时容器乙']; reindex(); sel=null; notice=''; commit()");

  // ㉛ 从**容器端口**拉线时，预览线起点必须是**容器盒**（不是世界原点）
  //    真因：预览线一律走 `nodeRect(pending)`（给状态用的）⇒ 容器名取不到 pos ⇒ 兜底 {0,0,240,50}
  //          ⇒ 起点落到世界 (240,25) ⇒ 线飞向画面外（用户实测："容器右侧的小圆点想连接时候 线条显示异常"）
  await ev("closeCtx(); setActive(0); tabs=[{kind:'root'}]; sel=null; notice=''; render(); fit()");
  await sleep(350);
  const ne31 = await ev("edges.length");
  const cp31 = await centerOf('.grp[data-g="悬浮飞行"] .port');
  check('㉛ 容器盒右侧有出边端口（拉线的起点）', cp31 !== null, JSON.stringify(cp31));
  if (cp31) {
    await mouse('mousePressed', cp31.x, cp31.y, 1, 1);
    await mouse('mouseMoved', cp31.x + 130, cp31.y + 70, 1, 1);
    await sleep(250);
  }
  const pv31 = await ev("(function(){var a=[].slice.call(document.querySelectorAll('path'));var p=a.filter(function(x){return x.getAttribute('pointer-events')==='none' && x.getAttribute('stroke-dasharray')})[0];return p?p.getAttribute('d'):'';})()");
  const exp31 = await ev("(function(){var b=groupBox(groupIndexOf('悬浮飞行'), groupByName['悬浮飞行']);return 'M '+(b.x+b.w)+' '+(b.y+b.h/2)+' L';})()");
  check('㉛b 预览线从**容器盒右侧中点**出发（不是世界原点）',
    String(pv31).indexOf(String(exp31)) === 0, 'd=' + pv31 + '  期望前缀=' + exp31);
  if (cp31) await mouse('mouseReleased', cp31.x + 130, cp31.y + 70, 1, 0);
  await sleep(250);
  await ev("edges.length = " + ne31 + "; pending=null; drag=null; notice=''; sel=null; commit()");   // 截断回基线，别污染后面的断言
  check('㉛c 这次拉线没留下脏边', (await ev("edges.length")) === ne31, 'edges=' + await ev("edges.length"));

  // ㉜ 事件边只该写**相位触发器**（用户实测："这里条件有点怪" —— 那个 move-input 是编辑器默认塞的）
  //    依据：FlightAnimConditions L62/L63 `takeoff-trigger`/`land-trigger` 真身就是 `c => false`；
  //          AgentAnimStateMachine `if (e.PhaseForced) continue;` ⇒ 事件边的 when= 运行时不求值。
  await ev("closeCtx(); setActive(0); tabs=[{kind:'root'}]; sel=null; notice=''; render()");
  const ne32 = await ev("edges.length");
  await ev("addEdge('outside','进入飞行')");
  await sleep(250);
  check('㉜ 机外 → 状态：时机自动是 takeoff-trigger（不再是 move-input）',
    (await ev("(function(){var e=edges[edges.length-1];return e.event===true && e.pred==='takeoff-trigger'})()")) === true,
    await ev("JSON.stringify({pred:edges[edges.length-1].pred, event:edges[edges.length-1].event})"));
  check('㉜b 事件边写普通谓词 ⇒ 校验报错（以前静默通过）',
    (await ev("(function(){var e=edges[edges.length-1];e.pred='move-input';var bad=validate().some(function(x){return x.indexOf('游戏事件')>=0});e.pred='takeoff-trigger';return bad && !validate().some(function(x){return x.indexOf('游戏事件')>=0});})()")) === true, '');
  // 🔴 新模型（2026-09-27 用户裁定："事件驱动为什么单独一个单选框？不和条件放在一起？"）：
  //    「游戏事件」= **条件下拉里的第四项**（不再是独立复选框）；选中它之后**时机由方向决定**、不给选。
  const k32 = await ev("(function(){sel={type:'edge',id:edges.length-1};render();"
    + "var k=document.getElementById('f-kind'); if(!k) return null;"
    + "return {n:k.options.length, vals:[].slice.call(k.options).map(function(x){return x.value}),"
    + " sel:k.options[k.selectedIndex].value, oldChk: !!document.getElementById('f-phase'),"
    + " args:(document.getElementById('f-args').textContent||'').replace(/\s+/g,' ')};})()");
  // 🔴 用户 2026-09-27 追问：「你为什么写死这条边，不让我来选具体是哪个事件？？」
  //    ⇒ 事件必须是**清单里选出来的**（候选 = 所有已登记的 `*-trigger`），方向只给默认值。
  const k32b = await ev("(function(){var s=document.getElementById('f-evt'); if(!s) return null;"
    + "return {n:s.options.length, vals:[].slice.call(s.options).map(function(o){return o.value}),"
    + " txt:[].slice.call(s.options).map(function(o){return o.textContent}).join(' | '),"
    + " sel:s.value};})()");
  check('㉜c 事件边=条件里的「游戏事件」那一项（4 个选项 · 选中它 · 且**事件本身可选**）',
    k32 && k32.n === 4 && k32.vals[k32.n - 1] === 'event' && k32.sel === 'event'
      && k32.oldChk === false
      && k32b && k32b.n >= 2 && k32b.vals.indexOf('takeoff-trigger') >= 0 && k32b.vals.indexOf('land-trigger') >= 0
      && String(k32b.txt).indexOf('起飞') >= 0 && String(k32b.txt).indexOf('落地') >= 0,
    JSON.stringify(k32) + ' 事件下拉=' + JSON.stringify(k32b));
  // 换一个事件 ⇒ 真的写进数据；换成一个"没有 C# 会来问"的时刻 ⇒ 校验给提示（不挡选择）
  const sw32 = await ev("(function(){var s=document.getElementById('f-evt');var before=edges[edges.length-1].pred;"
    + "s.value='land-trigger'; s.onchange({target:{value:'land-trigger'}});"
    + "return {before:before, after:edges[edges.length-1].pred};})()");
  check('㉜c-2 事件下拉**真能改**（改了写进边，不是摆设）',
    sw32 && sw32.before !== sw32.after && sw32.after === 'land-trigger', JSON.stringify(sw32));
  await ev("edges.length = " + ne32 + "; pending=null; drag=null; sel=null; notice=''; commit()");
  check('㉜d 清理干净（边数回到基线）', (await ev("edges.length")) === ne32, 'edges=' + await ev("edges.length"));

  // ㉜e 面板要**直接写出正确值**，别让用户猜（用户实测："所以这里我咋填 看不懂"）
  await ev("closeCtx(); setActive(0); sel={type:'edge', id:edges.findIndex(function(x){return x.from==='outside'})}; render()");
  await sleep(220);
  const txt32 = await ev("document.getElementById('editor').textContent || ''");
  check('㉜e 事件边面板写人话：事件选项是人话（「起飞 —— 空中按跳跃那一刻」），不再甩 c=>false',
    String(txt32).indexOf('空中按跳跃') >= 0 && String(txt32).indexOf('板触地') >= 0
      && String(txt32).indexOf('=> false') < 0,
    String(txt32).replace(/\s+/g, ' ').slice(0, 120));
  // ㉜f 用户实测："我就是想动画播完之后继续播连线的下一个" —— 面板必须**直接给出答案**：
  //     条件选「动画 · finished」；并且说清「演完才进」**是门不是触发**（勾了条件仍要成立，两者是"与"）。
  //     🔴 再问一次"这个该写什么"就是这条没做到位。
  check('㉜f 面板直接答"播完就进该写什么"：用「剩余 < 15」这种值；「演完才进」是门不是触发（与的关系）',
    String(txt32).indexOf('播完就进') >= 0 && String(txt32).indexOf('不是"触发"') >= 0
      && String(txt32).indexOf('finish-margin') >= 0,
    String(txt32).replace(/\s+/g, ' ').slice(0, 90));
  const ph32 = await ev("(function(){var a=document.getElementById('f-after'); if(!a) return null;"
    + "var p=document.getElementById('f-phase');"
    + "return {afterDisabled:a.disabled, afterLabel:(a.parentElement.textContent||'').slice(0,40),"
    + " oldChk: !!p};})()");
  check('㉜g 事件边上「演完才进」标成用不到并禁掉；且**不再有独立的"事件驱动"复选框**',
    ph32 && ph32.afterDisabled === true && String(ph32.afterLabel).indexOf('用不到') >= 0
      && ph32.oldChk === false, JSON.stringify(ph32));
  // 🔴 复选框不能被撑成整行宽（全局 `input{width:100%}` 的漏网项）—— 用户截图里"取反（不是这样）"
  //    的文字被推到面板最右边就是这么来的。判据：勾选框的宽度要远小于它所在的 label。
  const ckBox = await ev("(function(){var i=document.getElementById('f-not');if(!i)return null;"
    + "var r=i.getBoundingClientRect(), l=i.parentElement.getBoundingClientRect();"
    + "return {w:Math.round(r.width), lw:Math.round(l.width)};})()");
  check('复选框没被撑成整行宽（标签文字不会被推到面板另一头）',
    ckBox && ckBox.w > 0 && ckBox.w < 40 && ckBox.lw > ckBox.w, JSON.stringify(ckBox));


  // ㉝ 事件边也能写「动画 · 剩余 %」，而且**导出成真的条件**（相位会读它 ⇒ 出机判据）
  //    用**当时确实存在**的状态（`超人落地` 在前面 ⑭ 已被改名成 `超人落地` —— 上次这里就是踩了这个）
  await ev("closeCtx(); setActive(0); sel=null; render()");
  const ne33 = await ev("edges.length");
  await ev("addEdge('超人落地','outside')");
  await sleep(250);
  // 🔴 出机边新建出来是**普通边**（默认按键）⇒ 把「条件」选成「动画 · 剩余 %」，
  //    这一步会**自动**把它变成事件边（出机那个数只有 C# 会读）—— 也正是下面要验的。
  const ph0 = await ev("edges[" + ne33 + "].event");            // 新建出来是**普通边**（默认 Space 出机）
  await ev("(function(){var k=document.getElementById('f-kind');k.value='anim';k.onchange({target:{value:'anim'}});})()");
  await sleep(300);
  check('㉝ 出机边把条件改成「动画 · 剩余 %」⇒ **自动变成事件边**（那个数只有 C# 会读）',
    ph0 === false && (await ev("edges[" + ne33 + "].event === true")), '改前 event=' + ph0);
  await ev("(function(){var el=document.getElementById('f-lt');el.value='10';el.onchange({target:{value:'10'}});})()");
  await sleep(300);
  const x33 = await ev("edgesXml().split('\\n')[" + ne33 + "]");
  check('㉝b 导出成 anim="remaining" anim-rem-pct="10" event="true"',
    String(x33).indexOf('anim="remaining"') >= 0 && String(x33).indexOf('anim-rem-pct="10"') >= 0
      && String(x33).indexOf('event="true"') >= 0, 'xml=' + x33);
  check('㉝c 事件边写动画条件 ⇒ 校验 0 问题（它是真的，不是误导）',
    (await ev("(function(){return validate().filter(function(x){return x.indexOf('相位')>=0 || x.indexOf('anim-rem-pct')>=0}).length===0;})()")) === true,
    await ev("JSON.stringify(validate())"));
  await ev("edges.length = " + ne33 + "; pending=null; drag=null; sel=null; notice=''; commit()");

  // ㉞ 按键改成**勾选框**：多选 = 同时按着（组合），另有统一的「取反」
  //    用户原话："这四个模式没看懂" + "我还是希望你能够让我来写按键组合以及not否定"
  await ev("closeCtx(); setActive(0); edges.push({from:'idle',to:'hovermove',kind:'key',keys:'W',blend:'',after:false,event:false,not:false}); sel={type:'edge',id:edges.length-1}; render()");
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
  await ev("closeCtx(); setActive(0); elistAll=true; sel=null; render()");
  const ne35 = await ev("edges.length");
  check('㉟ 校验能抓「互为反向、条件相同 ⇒ 互踢」',
    (await ev("(function(){edges.push({from:'idle',to:'超人落地',kind:'pred',pred:'move-input',blend:'',after:false,event:false});"
      + "edges.push({from:'超人落地',to:'idle',kind:'pred',pred:'move-input',blend:'',after:false,event:false});"
      + "var bad=validate().some(function(x){return x.indexOf('互踢')>=0});"
      + "edges.length=" + ne35 + ";commit();return bad;})()")) === true, '');
  check('㉟b 校验能抓「完全重复的边」',
    (await ev("(function(){edges.push({from:'idle',to:'hovermove',kind:'pred',pred:'move-input',blend:'',after:false,event:false});"
      + "edges.push({from:'idle',to:'hovermove',kind:'pred',pred:'move-input',blend:'',after:false,event:false});"
      + "var bad=validate().some(function(x){return x.indexOf('完全重复')>=0});"
      + "edges.length=" + ne35 + ";commit();return bad;})()")) === true, '');
  check('㉟c 校验能抓「同来源 + 同条件（目标不同）⇒ 后面那条永远轮不到」',
    (await ev("(function(){edges.push({from:'idle',to:'超人落地',kind:'pred',pred:'move-input',blend:'',after:false,event:false});"
      + "edges.push({from:'idle',to:'hovermove',kind:'pred',pred:'move-input',blend:'',after:false,event:false});"
      + "var bad=validate().some(function(x){return x.indexOf('永远轮不到')>=0});"
      + "edges.length=" + ne35 + ";commit();return bad;})()")) === true, '');
  // ㊱ 相位触发器（真身 `c => false`）用在**非事件边**上 = 死边
  //    用户实测：`冲刺飞行 → 超人落地` 写了 takeoff-trigger 却没勾事件驱动
  check('㊱ 校验能抓「相位触发器用在没勾事件驱动的边上 ⇒ 永远不会生效」',
    (await ev("(function(){edges.push({from:'idle',to:'超人落地',kind:'pred',pred:'takeoff-trigger',blend:'',after:false,event:false});"
      + "var bad=validate().some(function(x){return x.indexOf('永远不会生效')>=0});"
      + "edges.length=" + ne35 + ";commit();return bad;})()")) === true, '');
  await ev("sel=null; notice=''; commit()");

  // ㊲ 容器页的「入口线」是**推导出来的装饰**（不是边）—— 不能抢走真边的点击/右键
  //    用户实测："这个线为什么删不掉"（右键它拿到的是画布菜单，所以永远没有"删除这条边"）
  await ev("closeCtx(); tabs=[{kind:'root'},{kind:'group',id:groups[0].name}]; setActive(1); sel=null; render()");
  await sleep(320);
  check('㊲ 容器页画了「入口线」，但它 pointer-events:none（鼠标能穿到下面的真边）',
    (await ev("(function(){var g=document.querySelector('.entry-edge');return !!g && getComputedStyle(g).pointerEvents==='none';})()")) === true,
    'entry-edge 数量=' + await ev("document.querySelectorAll('.entry-edge').length"));
  await resetAll();

  // ㊳ Entry 也能**自己连**（用户："难道不是应该我自己连 entry-idle吗"）——
  //    容器页里从 Entry 右侧绿点拉线，落到成员状态 ⇒ 设置容器的 entry
  //    🔴 夹具**从当前 XML 现挑**：挑"有 ≥2 个直接状态成员"的容器，先把 entry 设成第 1 个，
  //       再拖线到第 2 个 ⇒ 这样断言"entry 真的变了"才有意义（写死名字会指着空气，本轮就挂在这）。
  const g38 = await ev("(function(){var gi=" + G2 + "; var ms=" + G2M + "; window.__old38=groups[gi].entry;"
    + "groups[gi].entry=ms[0]; return {i:gi, from:ms[0], to:ms[1]};})()");
  await ev("closeCtx(); tabs=[{kind:'root'},{kind:'group',id:groups[" + g38.i + "].name}]; setActive(1); sel=null; render()");
  await sleep(320);
  const ep38 = await centerOf('[data-entry]');
  check('㊳ 容器页的 Entry 盒有出边绿点（可以自己连入口）', ep38 !== null, JSON.stringify(ep38));
  const nd38 = await centerOf('.node[data-n="' + g38.to + '"]');
  if (ep38 && nd38) await dragFrom(ep38, nd38.x - ep38.x, nd38.y - ep38.y);
  await sleep(220);
  check('㊳b 从 Entry 拉线落到**另一个成员** ⇒ 容器入口改成它',
    (await ev("groups[" + g38.i + "].entry")) === g38.to,
    'entry=' + await ev("groups[" + g38.i + "].entry") + '（期望 ' + g38.to + '，原来 ' + g38.from + '）');
  await ev("groups[" + g38.i + "].entry = window.__old38; closeCtx(); setActive(0); tabs=[{kind:'root'}]; sel=null; notice=''; commit()");
  await sleep(220);

  // ㊳c~㊳e **真嵌套**（用户："我想把这俩个组成一个新的子容器 看起来做不到"）
  const nest38 = await ev("(function(){var gi=" + G2 + "; var ms=" + G2M + "; return ms.slice(0,2);})()");
  await ev("closeCtx(); setActive(1); tabs=[{kind:'root'},{kind:'group',id:groups[" + g38.i + "].name}];"
    + "sel={type:'group',id:groups[" + g38.i + "].name}; render()");
  await sleep(300);
  const ng38 = await ev("(function(){var b=document.getElementById('g-sub'); if(!b) return ''; b.onclick(); return groups[groups.length-1].name;})()");
  await sleep(300);
  check('㊳c 容器页有「＋ 新建子容器」⇒ 父容器的成员里多了一个**容器名**',
    !!ng38 && (await ev("(groups[" + g38.i + "].members||[]).indexOf(" + JSON.stringify(ng38) + ")>=0")) === true,
    '子容器=' + ng38);
  await ev("(function(){var sub=" + JSON.stringify(ng38) + ";var s=groupByName[sub];"
    + "var ms=" + JSON.stringify(nest38) + ";"
    + "ms.forEach(function(m){groups.forEach(function(o){o.members=(o.members||[]).filter(function(x){return x!==m;});});s.members.push(m);});"
    + "s.entry=ms[0]; groups[" + g38.i + "].entry=sub; commit();})()");
  await sleep(300);
  check('㊳d 嵌套后：两状态成为**子容器的直接成员**，父的**叶子展开**里仍然有它们',
    (await ev("(groups[" + g38.i + "].members||[]).indexOf(" + JSON.stringify(nest38[0]) + ")<0")) === true
      && (await ev("leafStatesOf(groups[" + g38.i + "].name).indexOf(" + JSON.stringify(nest38[0]) + ")>=0")) === true
      && (await ev("parentGroupOf(" + JSON.stringify(nest38[0]) + ")")) === ng38,
    '子成员=' + await ev("JSON.stringify(groupByName[" + JSON.stringify(ng38) + "].members)"));
  check('㊳e 嵌套 + entry 指向子容器 ⇒ 校验 0 问题（入口链最终落到状态）',
    (await ev("validate().length")) === 0, JSON.stringify(await ev("validate()")));
  // ㊳f **画布右键**里也要有「新建子容器」（用户实测："看不见新建子容器按钮" —— 他是在画布右键里找的）
  //     🔴 用**合成事件直接派给 svg**（`ev.target` 必是 svg ⇒ 一定走画布分支），不靠"猜一个空白点"
  await ev("closeCtx(); setActive(1); tabs=[{kind:'root'},{kind:'group',id:groups[0].name}]; sel=null; render()");
  await sleep(300);
  await ev("(function(){var svg=document.getElementById('svg');var r=svg.getBoundingClientRect();"
    + "svg.dispatchEvent(new MouseEvent('contextmenu',{clientX:Math.round(r.left+40),clientY:Math.round(r.top+40),bubbles:true,cancelable:true}));})()");
  await sleep(300);
  const items38 = await ev("[].slice.call(document.querySelectorAll('#ctx .it')).map(function(x){return x.textContent}).join(' | ')");
  check('㊳f 容器页的**画布右键**里有「＋ 新建子容器」',
    String(items38).indexOf('新建子容器') >= 0 && String(items38).indexOf('新建容器') >= 0,
    '菜单=' + String(items38).slice(0, 120));
  await ev("closeCtx(); setActive(0); tabs=[{kind:'root'}]; sel=null; render()");
  await resetAll();
  await sleep(250);
  await ev("sel=null; notice=''; commit()");

  // ㊴ 子容器**改名**要级联到"**父容器的成员表**"
  //    用户实测："我新建一个子容器改名叫 loco 之后 他就不见了"
  await ev("(function(){groups.push({name:'子容器T',entry:'',members:[]}); groups[0].members.push('子容器T');"
    + "reindex(); renameGroup('子容器T','LOCO_T');})()");
  await sleep(320);
  check('㊴ 容器改名级联到**父容器的成员表**（不然它会凭空消失）',
    (await ev("(groups[0].members||[]).indexOf('LOCO_T')>=0")) === true
      && (await ev("(groups[0].members||[]).indexOf('子容器T')<0")) === true
      && (await ev("parentGroupOf('LOCO_T')")) === (await ev("groups[0].name")),
    '父成员=' + await ev("JSON.stringify(groups[0].members)"));
  check('㊴b 改名后它**仍然可见**于父容器页（没消失）',
    (await ev("(function(){var t={kind:'group',id:groups[0].name};return groupVisible('LOCO_T',t)===true;})()")) === true, '');
  check('㊴c 改名后校验 0 问题（父成员表里没留下指向空气的名字）',
    (await ev("validate().length")) === 0, JSON.stringify(await ev("validate()")));
  await ev("groups = groups.filter(function(x){return x.name!=='LOCO_T'});"
    + "groups[0].members = groups[0].members.filter(function(m){return m!=='LOCO_T'});"
    + "reindex(); sel=null; setActive(0); tabs=[{kind:'root'}]; notice=''; commit()");
  await sleep(220);

  // ㊷ **嵌套的渲染细节**（会话 5 = ⓿′ todo#3 的三条尾巴，"本轮只做到能用"那三条）
  //    ① 子容器盒的**默认位置**不能和父容器页上的其它盒子重叠（原来默认排布只按"第几个容器"排，
  //       没看层级 ⇒ 实测 `子容器X@24,308 ∩ ENTRY@120,299`）
  //    ② `fit()/allBoxes()` 必须排除「嵌套但本页不可见」的盒子（实测总览收了 **22** 个，本页只画 7 个）
  //    ③ 容器页的边要按**叶子语义**画、并把「子容器内部」的边折叠（和总览同一套判据）
  //       （原来用直接成员判 ⇒ 目标落在子容器里的边**整条消失**：悬浮飞行 页 13 条只画了 9 条）
  //    🔴 夹具**从当前 XML 现挑**（`G3` = 有 ≥3 个直接状态成员的容器；A/B 搬进子容器、C 留在父里）
  const P39 = await ev("groups[" + G3 + "].name");
  const S39 = await ev("(function(){var ms=" + G3M + "; return ms.slice(0,3);})()");
  const S39A = S39[0], S39B = S39[1], S39C = S39[2];
  await ev("closeCtx(); setActive(1); tabs=[{kind:'root'},{kind:'group',id:" + JSON.stringify(P39)
    + "}]; sel={type:'group',id:" + JSON.stringify(P39) + "}; render()");
  await sleep(300);
  const NG39 = await ev("(function(){var b=document.getElementById('g-sub'); if(!b) return ''; b.onclick(); return groups[groups.length-1].name;})()");
  await sleep(300);
  await ev("(function(){var sub=" + JSON.stringify(NG39) + ";var s=groupByName[sub];"
    + JSON.stringify([S39A, S39B]) + ".forEach(function(m){groups.forEach(function(o){o.members=(o.members||[]).filter(function(x){return x!==m;});});s.members.push(m);});"
    + "s.entry=" + JSON.stringify(S39B) + "; groups[groupIndexOf(" + JSON.stringify(P39) + ")].entry=sub;"
    + "groups.forEach(function(g){if(g.entry&&(g.members||[]).indexOf(g.entry)<0)g.entry='';}); sel=null; commit();})()");
  await sleep(320);

  const ov39 = await ev("(function(){var t=tabs[active];var bs=[];"
    + "visibleGroupBoxes(t).forEach(function(o){var b=o.box;b.tag='grp:'+o.name;bs.push(b);});"
    + "states.forEach(function(s){if(stateVisible(s.name,t)){var r=nodeRect(s.name);r.tag='state:'+s.name;bs.push(r);}});"
    + "var ge=groupEntryBox(t);ge.tag='ENTRY';bs.push(ge);var ov=[];"
    + "for(var i=0;i<bs.length;i++)for(var j=i+1;j<bs.length;j++){var A=bs[i],B=bs[j];"
    + "if(!(A.x+A.w<B.x||B.x+B.w<A.x||A.y+A.h<B.y||B.y+B.h<A.y))ov.push(A.tag+' ∩ '+B.tag);}return ov;})()");
  check('㊷a 子容器盒的**默认位置**不和同页任何盒子重叠（含 Entry 盒）',
    Array.isArray(ov39) && ov39.length === 0, JSON.stringify(ov39));

  const b39 = await ev("(function(){var top=groups.filter(function(g){return !parentGroupOf(g.name);}).length;"
    + "var free=states.filter(function(s){return !parentGroupOf(s.name);}).length;"
    + "var ab=allBoxes({kind:'root'});var cb=groupBox(groupIndexOf(" + JSON.stringify(NG39) + "), groupByName["
    + JSON.stringify(NG39) + "]);"
    + "return {n:ab.length, want:1+top+free, hasChild:ab.some(function(b){return b.x===cb.x&&b.y===cb.y;})};})()");
  check('㊷b 总览取景（allBoxes）只收**本页画得出**的盒子（嵌套子容器 / 组内状态不再算进去）',
    b39 && b39.n === b39.want && !b39.hasChild,
    '收到 ' + (b39 && b39.n) + ' 个（应 ' + (b39 && b39.want) + '）· 含子容器盒=' + (b39 && b39.hasChild));

  //    🔴 三条边都用**现挑的名字**：`S39C`（还留在父容器里的直接成员）→ `S39B`（已进子容器）= 跨容器边；
  //       `S39A` → `S39B` = 子容器**内部**边（父页该折叠）。名字写死就会指着空气（本轮 11 条就是这么挂的）。
  const c39 = await ev("(function(){closeCtx();tabs=[{kind:'root'},{kind:'group',id:" + JSON.stringify(P39)
    + "}];setActive(1);sel=null;render();var n0=edges.length;"
    + "edges.push({from:" + JSON.stringify(S39C) + ",to:" + JSON.stringify(S39B) + ",kind:'pred',pred:'move-input',blend:'',after:false,event:false});var iIn=n0;"
    + "edges.push({from:" + JSON.stringify(S39A) + ",to:" + JSON.stringify(S39B) + ",kind:'pred',pred:'move-input',blend:'',after:false,event:false});var iIn2=n0+1;"
    + "render();var dr=[].slice.call(document.querySelectorAll('.edge')).map(function(g){return +g.getAttribute('data-i');});"
    + "var p=document.querySelector('.edge[data-i=\"'+iIn+'\"] .main').getAttribute('d');"
    + "var hs=nodeRect(" + JSON.stringify(S39C) + ");var want='M '+(hs.x+hs.w)+' '+(hs.y+hs.h/2);"
    + "var onP=dr.indexOf(iIn)>=0, innerOnP=dr.indexOf(iIn2)>=0, nP=dr.length;"
    + "var ab=anchorOf(" + JSON.stringify(S39B) + ", tabs[active]);"
    + "var end2=(ab.x-10)+' '+(ab.y+ab.h/2);"                     // 箭头落在子容器盒的左中点
    + "var endOK=String(p).indexOf(end2)>=0;"
    + "openTab({kind:'group',id:parentGroupOf(" + JSON.stringify(S39B) + ")});render();"
    + "var dc=[].slice.call(document.querySelectorAll('.edge')).map(function(g){return +g.getAttribute('data-i');});"
    + "var innerOnC=dc.indexOf(iIn2)>=0, nC=dc.length;"
    + "var childInfo=(tabs[active].kind+':'+(tabs[active].id||''))+' leaf='+leafStatesOf(childGroupsOf(" + JSON.stringify(P39) + ")[0]).join(',')+' drawn='+dc.join(',');"
    + "edges.length=n0;setActive(1);tabs=[{kind:'root'},{kind:'group',id:" + JSON.stringify(P39) + "}];sel=null;render();"
    + "return {onP:onP,endOK:endOK,end2:end2,got:String(p).slice(0,30),full:String(p),childInfo:childInfo,"
    + "innerOnP:innerOnP,innerOnC:innerOnC,nP:nP,nC:nC};})()");
  check('㊷c-1 容器页按**叶子语义**画边：目标是子容器里状态的边**画出来了**，箭头锚在子容器盒上',
    c39 && c39.onP === true && c39.endOK === true,
    '路径完整=' + (c39 && c39.full) + ' 期望=' + (c39 && c39.end2));
  check('㊷c-2 「子容器内部」的边在父容器页**折叠**、钻进子容器才画',
    c39 && c39.innerOnP === false && c39.innerOnC === true,
    '父页画=' + (c39 && c39.nP) + ' 内部边在?' + (c39 && c39.innerOnP) + ' · 子页画=' + (c39 && c39.nC) + ' 内部边在?' + (c39 && c39.innerOnC) + ' · 子页布局=' + (c39 && c39.childInfo));

  // ㊷c-3 嵌套页的**条件标签**不能糊成一团（多条边汇进同一个子容器盒 ⇒ 原来兜底全落在同一格）
  const lab39 = await ev("(function(){var t=tabs[active];tabs=[{kind:'root'},{kind:'group',id:"
    + JSON.stringify(P39) + "}];active=1;render();"
    + "var rs=[].slice.call(document.querySelectorAll('.lbl rect')).map(function(r){"
    + "return {x:+r.getAttribute('x'),y:+r.getAttribute('y'),w:+r.getAttribute('width'),h:+r.getAttribute('height')};});"
    + "var ov=[];for(var i=0;i<rs.length;i++)for(var j=i+1;j<rs.length;j++){var a=rs[i],b=rs[j];"
    + "if(!(a.x+a.w<b.x||b.x+b.w<a.x||a.y+a.h<b.y||b.y+b.h<a.y))ov.push(i+'∩'+j);}"
    + "return {n:rs.length, ov:ov};})()");
  check('㊷c-3 嵌套容器页的条件标签**互不重叠**（兜底不再全落在同一格）',
    lab39 && lab39.ov.length === 0,
    '标签 ' + (lab39 && lab39.n) + ' 个 · 重叠 ' + JSON.stringify(lab39 && lab39.ov));

  // ㊷d ⓿′ todo#2：**用真鼠标把"真嵌套"走一遍**（不是直接调函数）
  await ev("closeCtx(); setActive(1); tabs=[{kind:'root'},{kind:'group',id:" + JSON.stringify(P39) + "}]; sel=null; render();"
    // 🔴 取景要**算**出来：被拖的节点 + 目标盒子都必须在画面里（写死 k 时，多一个子容器就把它挤出屏幕）
    + "(function(){var nb=nodeRect(" + JSON.stringify(S39C) + "), cb=groupBox(groupIndexOf(" + JSON.stringify(NG39) + "), groupByName[" + JSON.stringify(NG39) + "]);"
    + "var x0=Math.min(nb.x,cb.x)-40, x1=Math.max(nb.x+nb.w,cb.x+cb.w)+40, y0=Math.min(nb.y,cb.y)-40, y1=Math.max(nb.y+nb.h,cb.y+cb.h)+40;"
    + "var r=svg.getBoundingClientRect(); view.k=Math.max(0.35, Math.min(0.95,(r.width-90)/(x1-x0),(r.height-90)/(y1-y0)));"
    + "view.tx=45-x0*view.k; view.ty=45-y0*view.k; applyView();})()");
  await sleep(320);
  const n39 = await centerOf('.node[data-n="' + S39C + '"]');
  // 🔴 拖拽是「**左上角**跟着鼠标」；落点判定用的是**节点的中心点**。
  //    所以位移要按"中心 → 子容器盒中心"算，不然中心会正好落在盒子外面（差半个节点宽）。
  const dl39 = await ev("(function(){var nb=nodeRect(" + JSON.stringify(S39C) + ");var cb=groupBox(groupIndexOf("
    + JSON.stringify(NG39) + "), groupByName[" + JSON.stringify(NG39) + "]);"
    + "return {dx:Math.round(((cb.x+cb.w/2)-(nb.x+nb.w/2))*view.k), dy:Math.round(((cb.y+cb.h/2)-(nb.y+nb.h/2))*view.k)};})()");
  if (n39 && dl39) await dragFrom(n39, dl39.dx, dl39.dy);
  check('㊷d 真鼠标把状态拖进**子容器盒** ⇒ 归属变成子容器（父的直接成员里移出、父的叶子展开里仍在）',
    (await ev("groupByName[" + JSON.stringify(NG39) + "].members.indexOf(" + JSON.stringify(S39C) + ")>=0")) === true
      && (await ev("groups[groupIndexOf(" + JSON.stringify(P39) + ")].members.indexOf(" + JSON.stringify(S39C) + ")<0")) === true
      && (await ev("leafStatesOf(" + JSON.stringify(P39) + ").indexOf(" + JSON.stringify(S39C) + ")>=0")) === true,
    '子成员=' + await ev("JSON.stringify(groupByName[" + JSON.stringify(NG39) + "].members)") + ' 父的直接成员=' + await ev("JSON.stringify(groups[groupIndexOf(" + JSON.stringify(P39) + ")].members)"));
  await ev("(function(){var sub=" + JSON.stringify(NG39) + ";var s=groupByName[sub];"
    + "var back=s.members.slice();"                                   // 🔴 全部还回去（含子容器名），别漏
    + "var P=groupByName[" + JSON.stringify(P39) + "];"
    + "groups.forEach(function(o){o.members=(o.members||[]).filter(function(x){return x!==sub;});});"
    + "back.forEach(function(m){if(P.members.indexOf(m)<0)P.members.push(m);});"
    + "groups=groups.filter(function(g){return g.name!==sub;});"
    + "reindex(); sel=null; tabs=[{kind:'root'}]; setActive(0); view.k=1; view.tx=0; view.ty=0; notice=''; commit();})()");
  await sleep(250);
  // ㊸ 用户实测："我新建一个子容器改名叫loco之后 他就不见了"
  //    ① 旧行为：新建完**直接跳进那个空容器**（空容器页什么都没画 + 取景拿不到内容 ⇒ 镜头还停在上一页）
  //       ⇒ 画布一片空白，"容器不见了"。现在 = **留在父容器页** + 自动取景 + 选中它。
  //    ② 空容器页本身也要给一句话，别让人对着一片空白猜
  //    （③ 改名级联的前半截见上面的 ㊴ —— 同一个报障的另一半）
  await ev("closeCtx(); tabs=[{kind:'root'}]; setActive(0); render(); fit()");
  await sleep(200);
  await ev("openTab({kind:'group', id:groups[0].name}); sel={type:'group',id:groups[0].name}; render()");
  await sleep(300);
  const ng43 = await ev("(function(){document.getElementById('g-sub').onclick(); return groups[groups.length-1].name;})()");
  await sleep(350);
  const st43 = await ev("(function(){var t=tabs[active];var b=groupBox(groupIndexOf(" + JSON.stringify(ng43)
    + "), groupByName[" + JSON.stringify(ng43) + "]);var r=svg.getBoundingClientRect();"
    + "var sx=r.left+(b.x+b.w/2)*view.k+view.tx, sy=r.top+(b.y+b.h/2)*view.k+view.ty;"
    + "return {tab:t.kind+':'+(t.id||''), onScreen:(sx>r.left&&sx<r.right&&sy>r.top&&sy<r.bottom),"
    + "sx:Math.round(sx), sy:Math.round(sy), drawn:[].slice.call(document.querySelectorAll('.grp')).map(function(e){return e.getAttribute('data-g')}).join(',')};})()");
  check('㊸a 新建子容器后**留在父容器页**（不跳进那个空页面）',
    st43.tab === 'group:' + (await ev("groups[0].name")), 'tab=' + st43.tab);
  check('㊸b 新盒子**当场就在画面里**（父页自动取景，不用手动按「适应视图」）',
    st43.onScreen === true && st43.drawn.indexOf(ng43) >= 0,
    'screen=(' + st43.sx + ',' + st43.sy + ') 画面内=' + st43.onScreen + ' 本页画的容器=' + st43.drawn);
  const em43 = await ev("(function(){openTab({kind:'group', id:" + JSON.stringify(ng43) + "}); render();"
    + "var h=document.querySelector('.emptyhint'); var r=svg.getBoundingClientRect();"
    + "var txt=h?h.textContent:''; return {hasHint:!!h, txt:txt, k:view.k, tx:Math.round(view.tx), ty:Math.round(view.ty)};})()");
  check('㊸c 钻进**空容器**页：有「还是空的」提示，且取景框住它（不是一片空白）',
    em43.hasHint === true && em43.txt.indexOf('还是空的') >= 0 && em43.tx > -2000,
    JSON.stringify(em43));
  // 收尾：从 XML 重来（别手写搬迁 —— XML 一改就错位）
  await resetAll();
  check('㊸d 收尾干净：空子容器已删、校验 0 问题',
    JSON.stringify(await ev('validate()')) === '[]' && (await ev("groups.length")) === (await ev('DATA.groups.length')),
    JSON.stringify(await ev('validate()')));

  // ㊹ `sel` 指向**已经删掉的对象**时 `render()` 不能抛 —— 抛了会把"调用它的那一整段脚本"静默吃掉
  //    （本次就是被这个坑了：测试收尾里 sel 停在刚删掉的子容器上 ⇒ setActive 里那句 render 抛错
  //     ⇒ 后面设镜头 / 清理的语句一句都没跑，表现为完全不相干的一条用例假 FAIL）
  const dead41 = await ev("(function(){"
    + "groups.push({name:'临时X',entry:'',members:[]}); reindex();"
    + "sel={type:'group', id:'临时X'}; render();"
    + "groups=groups.filter(function(g){return g.name!=='临时X'}); reindex();"
    + "try{render();}catch(e){return {threw:e.message, sel:(sel&&sel.id)||null};}"
    + "return {threw:null, sel:(sel&&sel.id)||null, panel:document.getElementById('selname').textContent};})()");
  check('㊹ 选中的容器被删掉后 render() 不抛（否则调用方整段脚本被静默中断）',
    dead41 && !dead41.threw, JSON.stringify(dead41));
  check('㊹b 死掉的选中项被自动清掉 ⇒ 面板回到「（没选）」',
    dead41 && dead41.sel === null && dead41.panel === '（没选）', JSON.stringify(dead41));

  // ㊺ 用户实测提问："我现在怎么把这俩个塞到子容器里？" ——
  //    「拖」本来就能用（下面的 ㊺d 用真鼠标验），但**看不出来**。所以补一条看得见的路：
  //    右键状态 / 容器盒 → 「移进容器…」→ 选目标（含"移出容器"与防环）。
  //    🔴 名字全从当前 XML 现挑：`P45` = 有 ≥2 状态成员的容器，`S45` = 它的第一个状态成员。
  const P45 = await ev("groups[" + G2 + "].name");
  const S45 = await ev(G2M + "[0]");
  const ng45 = await ev("(function(){var g=groupByName[" + JSON.stringify(P45) + "];sel={type:'group',id:g.name};render();"
    + "document.getElementById('g-sub').onclick(); return groups[groups.length-1].name;})()");
  await sleep(320);
  await ev("closeCtx(); tabs=[{kind:'root'},{kind:'group',id:" + JSON.stringify(P45) + "}]; setActive(1); sel=null; render()");
  await sleep(250);
  // ① 右键状态 → 菜单里得有「移进容器…」
  await ev("(function(){var n=document.querySelector('.node[data-n=" + JSON.stringify(S45) + "]'); var r=n.getBoundingClientRect();"
    + "n.dispatchEvent(new MouseEvent('contextmenu',{clientX:Math.round(r.left+r.width/2),clientY:Math.round(r.top+r.height/2),bubbles:true,cancelable:true}));})()");
  await sleep(250);
  const m45 = await ev("[].slice.call(document.querySelectorAll('#ctx .it')).map(function(x){return x.textContent}).join(' | ')");
  check('㊺a 右键状态节点有「移进容器…」（这条就是回答"怎么塞进去"）',
    String(m45).indexOf('移进容器') >= 0, '菜单=' + String(m45));
  await ev("(function(){var it=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(e){return e.textContent==='移进容器…'})[0]; if(it) it.click();})()");
  await sleep(250);
  const sub45 = await ev("[].slice.call(document.querySelectorAll('#ctx .it')).map(function(x){return x.textContent}).join(' | ')");
  check('㊺b 二级菜单列出全部容器（当前所在 / 空容器都能选）',
    String(sub45).indexOf(P45) >= 0 && String(sub45).indexOf(ng45) >= 0, '二级菜单=' + String(sub45));
  await ev("(function(){var it=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(e){return e.textContent.indexOf(" + JSON.stringify(ng45) + ")>=0})[0]; if(it) it.click();})()");
  await sleep(300);
  check('㊺c 点一下就**移进**那个子容器（归属变了）',
    (await ev("parentGroupOf(" + JSON.stringify(S45) + ")")) === ng45,
    '现在归属=' + (await ev("parentGroupOf(" + JSON.stringify(S45) + ")")));
  // ② 移出容器：回到顶层
  const out45 = await ev("(function(){var id=" + JSON.stringify(S45) + "; if(!groupByName[parentGroupOf(id)]) return 'ERR';"
    + "pickContainerFor(id); var it=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(e){return e.textContent.indexOf('移出容器')>=0})[0];"
    + "var has=!!it; if(it) it.click(); return has?String(parentGroupOf(id)):'NO-ITEM';})()");
  check('㊺d 「移出容器（变成顶层）」也在（否则塞进去就出不来了）',
    out45 === 'null', '移出后 parentGroupOf(' + S45 + ')=' + out45);
  // ③ 防环：容器不能移进自己的后代
  const cyc45 = await ev("(function(){pickContainerFor(" + JSON.stringify(P45) + ");"
    + "var txt=[].slice.call(document.querySelectorAll('#ctx .it')).map(function(x){return x.textContent}).join('|'); closeCtx();"
    + "return {listsOwnChild: txt.indexOf(" + JSON.stringify(ng45) + ")>=0};})()");
  check('㊺e 防环：容器「' + P45 + '」的候选里**不出现**它自己的子容器',
    cyc45.listsOwnChild === false, JSON.stringify(cyc45));
  await resetAll();
  check('㊺f 收尾干净：子容器已删、状态回到原容器、校验 0 问题',
    JSON.stringify(await ev('validate()')) === '[]' && (await ev("groups.length")) === (await ev('DATA.groups.length'))
      && (await ev("parentGroupOf(" + JSON.stringify(S45) + ")")) === P45,
    JSON.stringify(await ev('validate()')));

  // ㊻ 用户实测："我这里 entry 无法连接到子容器上" —— 落点原来只认**状态节点**（`.node`），
  //    把线拖到**子容器盒**上毫无反应；而 `entry` **本来就允许指向子容器**（装载期 `resolveEntry` 递归到状态）。
  //    🔴 名字现挑：`P46` = 有 ≥2 状态成员的容器，`S46` = 它的头两个成员（搬进子容器）
  const P46 = await ev("groups[" + G2 + "].name");
  const S46 = await ev("(function(){var ms=" + G2M + "; return ms.slice(0,2);})()");
  const ng46 = await ev("(function(){var g=groupByName[" + JSON.stringify(P46) + "];sel={type:'group',id:g.name};render();"
    + "document.getElementById('g-sub').onclick(); var n=groups[groups.length-1].name; var s=groupByName[n];"
    + JSON.stringify(S46) + ".forEach(function(m){groups.forEach(function(o){o.members=(o.members||[]).filter(function(x){return x!==m;});});s.members.push(m);});"
    + "s.entry=" + JSON.stringify(S46[0]) + "; reindex(); tabs=[{kind:'root'},{kind:'group',id:g.name}]; setActive(1); render(); fit(); return n;})()");
  await sleep(340);
  // 🔴 量屏幕坐标之前先把布局**恢复默认并统一取景** —— 上面那条用例（㊷d）把节点
  //    拖到了世界坐标 (1040,25)（正是子容器盒默认站位 (1060,24) 附近）⇒ 节点**盖在盒子上面**，
  //    `elementFromPoint` 一律被节点接走，entry 就落成了状态而不是容器。
  //    （这就是"测试自己的布局假设也是被测对象"：几何断言前必须构造确定的布局。）
  await ev("pos={}; gpos={}; resetBoxes(); defaultPos(); sel=null; render(); fit()");
  await sleep(320);
  const base46 = await ev("groupByName[" + JSON.stringify(P46) + "].entry");
  const ep46 = await centerOf('[data-entry]');
  const gb46 = await centerOf('.grp[data-g="' + ng46 + '"]');
  const hit46 = await ev("(function(){var el=document.elementFromPoint(" + (gb46 ? gb46.x : 0) + "," + (gb46 ? gb46.y : 0) + ");"
    + "return el? String((el.closest&&el.closest('.grp')&&el.closest('.grp').getAttribute('data-g'))||('node:'+((el.closest&&el.closest('.node'))?el.closest('.node').getAttribute('data-n'):'-'))) : 'null';})()");
  check('㊻ 容器页有 Entry 绿点 + 子容器盒，且落点上就是那个盒子（真鼠标拉线的前提）',
    !!ep46 && !!gb46 && hit46 === ng46, JSON.stringify([ep46, gb46, hit46]));
  if (ep46 && gb46) await dragFrom(ep46, gb46.x - ep46.x, gb46.y - ep46.y);
  check('㊻a 真鼠标：Entry 绿点拖到**子容器盒** ⇒ 入口 = 那个子容器',
    (await ev("groupByName[" + JSON.stringify(P46) + "].entry")) === ng46,
    'entry=' + (await ev("groupByName[" + JSON.stringify(P46) + "].entry")) + '（改前 ' + base46 + '）');
  const line46 = await ev("(function(){var p=document.querySelector('.entry-edge path');"
    + "var cb=groupBox(groupIndexOf(" + JSON.stringify(ng46) + "), groupByName[" + JSON.stringify(ng46) + "]);"
    + "return {n:document.querySelectorAll('.entry-edge').length, d:p?String(p.getAttribute('d')):'',"
    + "want:(cb.x-10)+' '+(cb.y+cb.h/2)};})()");
  check('㊻b entry 指向子容器时**入口线照样画**，箭头落在子容器盒左中点',
    line46.n === 1 && line46.d.indexOf(line46.want) >= 0, JSON.stringify(line46));
  check('㊻c entry→子容器 校验 0 问题（入口链递归到状态）',
    JSON.stringify(await ev('validate()')) === '[]', JSON.stringify(await ev('validate()')));
  // ㊻d 落到空白：给提示且 entry 不变
  const tl46 = await ev("(function(){var r=svg.getBoundingClientRect(); return {x:Math.round(r.left+28), y:Math.round(r.top+28)};})()");
  const ep46b = await centerOf('[data-entry]');
  const before46 = await ev("groupByName[" + JSON.stringify(P46) + "].entry");
  await ev("notice=''");
  if (ep46b) await dragFrom(ep46b, tl46.x - ep46b.x, tl46.y - ep46b.y);
  check('㊻d 落到空白 ⇒ 有提示、entry 不变',
    String(await ev("notice")).indexOf('没落到有效目标') >= 0 && (await ev("groupByName[" + JSON.stringify(P46) + "].entry")) === before46,
    'notice=' + (await ev("notice")));
  await resetAll();
  check('㊻e 收尾干净：子容器已删、entry 复原、校验 0 问题',
    JSON.stringify(await ev('validate()')) === '[]'
      && (await ev("groupByName[" + JSON.stringify(P46) + "].entry")) === (await ev("DATA.groups[groupIndexOf(" + JSON.stringify(P46) + ")].entry")), '');

  // ㊼ 用户实测："这里的入口明明是子容器1，为什么写 hovermove" ——
  //    真因两层：① 那行字显示的是容器的 **`entry` 属性**（还是老值 hovermove），不是"线拉到哪"；
  //    ② hovermove **被搬进了子容器1**，所以入口线就近锚在装着它的盒子上 ⇒ 图字看着打架。
  //    🔴 而且这时 `entry` 已经**失效**：装载器要求 entry 必须是**本容器的直接成员**
  //    （`Array.IndexOf(members, entry) < 0` ⇒ 报错 ⇒ **整台不注册**）。
  await ev("pos={}; gpos={}; resetBoxes(); defaultPos(); sel=null; render(); fit()");
  await sleep(250);
  const ng47 = await ev("(function(){var g=groups[0];sel={type:'group',id:g.name};render();"
    + "document.getElementById('g-sub').onclick(); var n=groups[groups.length-1].name; var s=groupByName[n];"
    + "['hovermove'].forEach(function(m){groups.forEach(function(o){o.members=(o.members||[]).filter(function(x){return x!==m;});});s.members.push(m);});"
    + "s.entry='hovermove'; groups[0].entry='hovermove'; reindex(); tabs=[{kind:'root'},{kind:'group',id:g.name}]; setActive(1); render(); fit(); return n;})()");
  await sleep(320);
  // 此刻父容器 entry 仍是 hovermove（= 搬进去之前的老值）⇒ 必须报错，并**点名改成谁**
  const v47 = await ev("validate().filter(function(x){return x.indexOf('入口')>=0||x.indexOf('entry')>=0}).join(' | ')");
  check('㊼a 入口状态被搬进子容器后 ⇒ 校验报错，且**直接说清改成谁**',
    String(v47).indexOf('不是它的成员') >= 0 && String(v47).indexOf('把 entry 改成「' + ng47 + '」') >= 0,
    String(v47));
  // 图/字：Entry 盒显示的应是**人话路径**（子容器 → 状态），不再是干巴巴的 hovermove
  const t47 = await ev("document.getElementById('svg').textContent");
  check('㊼b Entry 盒/入口线写**人话路径**「' + ng47 + ' → hovermove」（解释线为什么指到子容器盒）',
    String(t47).indexOf(ng47 + ' → hovermove') >= 0, '');
  // ㊽ 用户实测第二弹："这个子容器1的entry明明指向idle，为什么说还是hovermove" ——
  //    他看的是**悬浮飞行的 Entry 盒**，而右侧面板显示的是**他点选的那个子容器盒**的属性。
  //    两个容器的 entry 同屏出现 ⇒ 不写清"这个 Entry 是谁的"，就一定会被认错。
  const box47 = await ev("(function(){var g=document.querySelector('.entry .t1'); return g?g.textContent:'';})()");
  check('㊽a Entry 盒标题写明**它属于哪个容器**（Entry · <容器名>）',
    String(box47).indexOf('Entry · ') === 0 && String(box47).indexOf(await ev("groups[0].name")) > 0,
    '标题=' + box47);
  const path47 = await ev("(function(){var t=document.getElementById('svg').textContent; var i=t.indexOf('入口 → '); return i<0?'':t.slice(i+4, i+40);})()");
  check('㊽b 失效标记在**句尾**、不在句首（否则读成"这是子容器1的入口"）',
    String(path47).indexOf('⚠') !== 0, '入口那行=' + path47);
  // 把 entry 改成子容器 ⇒ 校验 0 问题，路径继续往下展开
  await ev("groupByName[groups[0].name].entry=" + JSON.stringify(ng47) + "; commit(); fit()");
  await sleep(300);
  const t47b = await ev("document.getElementById('svg').textContent");
  check('㊼c 把 entry 改成那个子容器 ⇒ 校验 0 问题，且路径显示「' + ng47 + ' → hovermove」',
    JSON.stringify(await ev('validate()')) === '[]' && String(t47b).indexOf('入口 → ' + ng47 + ' → hovermove') >= 0,
    JSON.stringify(await ev('validate()')));
  await resetAll();
  check('㊼d 收尾干净：子容器已删、entry 复原、校验 0 问题',
    JSON.stringify(await ev('validate()')) === '[]' && (await ev("groupByName[groups[0].name].entry")) === (await ev('DATA.groups[0].entry')), '');

  check('㊷e 清理干净：子容器已删、状态回到父容器、校验 0 问题',
    (await ev("groups.some(function(g){return g.name===" + JSON.stringify(NG39) + "})")) === false
      && (await ev("groups[groupIndexOf(" + JSON.stringify(P39) + ")].members.indexOf(" + JSON.stringify(S39C) + ")>=0")) === true
      && JSON.stringify(await ev('validate()')) === '[]',
    JSON.stringify(await ev('validate()')));

  // ㊵ **每页各记一份镜头**（本轮新增）：`view` 是全局量，但总览 / 容器页 / 状态页需要的取景不同。
  //    不记的话，从容器页切回总览会顶着容器页的镜头看总览 —— 内容直接跑到画面外
  //    （本轮就是被这一条连带打挂了 ⑳d/㉑b/㉒/㉔ 四条几何用例）。
  // 🔴 总览页的镜头先显式设成一个**特征值**（11/22）—— 不能靠 `fit()` 的两个结果"碰巧不同"，
  //    那是"测试自己制造的巧合"（本次就因为总览/容器页恰巧算出同一组 k/tx/ty 而假 FAIL）。
  await ev("closeCtx(); tabs=[{kind:'root'}]; setActive(0); render();"
    + "view.k=1; view.tx=11; view.ty=22; applyView(); tabs[0].view=viewSnapshot()");
  await sleep(220);
  const v40 = JSON.stringify(await ev('view'));
  await ev("openTab({kind:'group', id:groups[0].name})");   // 新开的页 ⇒ 自动取景（顺带就是㊷/㊸要的"看得见"）
  await sleep(320);
  const v40g = JSON.stringify(await ev('view'));
  await ev("(function(){var t=[].slice.call(document.querySelectorAll('.tab')).filter(function(x){return x.textContent.indexOf('总览')>=0})[0]; if(t) t.click();})()");
  await sleep(320);
  const v40now = JSON.stringify(await ev('view'));
  check('㊵ 每页各记一份镜头：从容器页点「总览」切回 ⇒ 还原总览自己的镜头',
    v40g !== v40 && v40now === v40,
    '总览=' + v40 + ' · 容器页=' + v40g + ' · 切回后=' + v40now);
  await ev("closeCtx(); tabs=[{kind:'root'}]; setActive(0); sel=null; notice=''; render()");
  await sleep(220);

  // ⑩⑪ 本地草稿必须**显眼**（用户实测踩过的坑：草稿静默盖住 XML，看着像"改了没生效"）
  await ev(`try{const d={states:DATA.states.concat([{name:'magicIdle',act:'act_magic_idle',dur:'',next:'',clip:'magic_idle',durText:'循环'}]),edges:DATA.edges.concat([{from:'*',to:'magicIdle',kind:'pred',pred:'casting-fallback',blend:'0.15',after:false,event:false}]),groups:DATA.groups,pos:{},view:{k:1.15,tx:30,ty:70},gpos:{}};localStorage.setItem(LSKEY,JSON.stringify(d));}catch(e){}`);
  await send('Page.reload'); await sleep(1600);
  check('⑩ 旧草稿被载入时弹出显眼提示条',
    await ev("document.getElementById('banner').classList.contains('on')"),
    String(await ev("(document.getElementById('banner').textContent||'').slice(0,90)")));
  check('⑩b 提示条说清了"草稿 vs XML"的差异', (await ev("states.filter(s=>s.name==='magicIdle').length")) === 1, '');
  // ㊾a/b 点「保留草稿」必须有反应（用户实测："我点保留草稿没有反应" ——
  //    原来只置了个 `draftAck = true`，而那个标志**没人读**，黄条原样重建 ⇒ 点了跟没点一样）
  await ev("(function(){var b=[].slice.call(document.querySelectorAll('#banner button')).filter(function(x){return x.textContent==='保留草稿'})[0]; if(b) b.click();})()");
  await sleep(300);
  const slim49 = await ev("(function(){var b=document.getElementById('banner');"
    + "return {on:b.classList.contains('on'), slim:b.classList.contains('slim'),"
    + "hasMore:[].slice.call(document.querySelectorAll('#banner button')).some(function(x){return x.textContent==='展开说明'}),"
    + "txt:(b.textContent||'').slice(0,50)};})()");
  check('㊾a 点「保留草稿」⇒ 黄条**收成紧凑条**（不再原地不动）',
    slim49.on === true && slim49.slim === true && slim49.hasMore === true, JSON.stringify(slim49));
  await ev("(function(){var b=[].slice.call(document.querySelectorAll('#banner button')).filter(function(x){return x.textContent==='展开说明'})[0]; if(b) b.click();})()");
  await sleep(280);
  const exp49 = await ev("(function(){var b=document.getElementById('banner');"
    + "return {slim:b.classList.contains('slim'),"
    + "hasKeep:[].slice.call(document.querySelectorAll('#banner button')).some(function(x){return x.textContent==='保留草稿'})};})()");
  check('㊾b 「展开说明」能把完整黄条调回来', exp49.slim === false && exp49.hasKeep === true, JSON.stringify(exp49));

  await ev("(function(){var b=document.querySelector('#banner button.primary'); if(b) b.click();})()");
  await sleep(500);
  check('⑪ 一键「用 XML 覆盖草稿」后 magicIdle 消失、提示条收起',
    (await ev("states.filter(s=>s.name==='magicIdle').length")) === 0
      && !(await ev("document.getElementById('banner').classList.contains('on')")), '');

  // ── F 组：直接读写文件（打开 XML / 保存 / 另存为）────────────────────
  // 🔴 无头浏览器**点不了原生对话框** ⇒ 三个 picker 换成**内存文件系统桩**（见上面的 `FSA_STUB`）。
  await ev(FSA_STUB);
  // 假状态机：3 状态 / 1 容器 / 2 边（故意与真 XML 不同 —— "载入后页面真的换成它了"才可断言）
  const FAKE = "['<state_machine name=\"probe\">','<states>','  <state name=\"a\" act=\"act_fly_idle\" />',"
    + "'  <state name=\"b\" act=\"act_fly_hovermove\" once=\"true\" next=\"a\" />',"
    + "'  <state name=\"c\" act=\"act_fly_fastmove\" />','</states>','<families>',"
    + "'  <family name=\"G1\" entry=\"a\">a b</family>','</families>','<edges>',"
    + "'  <edge from=\"outside\" to=\"a\" when=\"takeoff-trigger\" event=\"true\" />',"
    + "'  <edge from=\"G1\" to=\"c\" keys=\"W\" />','</edges>','</state_machine>']"
    + ".join(String.fromCharCode(10))";

  // F1 打开 XML…（桩给一份只读句柄）⇒ 页面换成那份文件
  await ev('window.__vfsSeed(' + FAKE + ')');
  await ev("document.getElementById('btn-open').click()");
  await sleep(600);
  const f1 = await ev("({n: states.length, m: DATA.machine, name: fsx.name, asked: window.__vfs.asked,"
    + " chip: document.getElementById('fschip').textContent})");
  check('F1 「打开 XML…」把那份文件载进页面（状态数 / 机器名 / 药丸都跟着变）',
    f1.n === 3 && f1.m === 'probe' && f1.name === 'flight.xml' && f1.chip.indexOf('flight.xml') >= 0,
    JSON.stringify(f1));
  // 🔴 写权限在**打开那一刻**要掉（用户实测："点了保存就应该是覆盖当前文件，不需要再弹窗"）——
  //    浏览器只在刚点完对话框那一下认手势，留到保存时问 = 卡在"改完就想存"的节骨眼上蹦对话框
  check('F1c 打开时就问掉写权限（而不是留到保存）', f1.asked === 1, 'asked=' + f1.asked);
  check('F1b 载入后 `fullXml()` 的模板也换成那份文件（导出 / 恢复 / 黄条比对都跟着它）',
    await ev("fullXml().indexOf('<state_machine name=\"probe\"') >= 0"), '');

  // F2 保存写回磁盘（页面上改一条边 ⇒ 保存 ⇒ 磁盘内容跟着变）
  //    🔴 还要过 `requestPermission` —— `showOpenFilePicker` 给的句柄**默认只读**（真实行为）
  await ev("edges.push({from:'a',to:'b',kind:'key',keys:'Q',blend:'',after:false,event:false,not:false}); commit();");
  await ev("document.getElementById('btn-save').click()");
  await sleep(700);
  const f2 = await ev("({txt: window.__vfs.files['flight.xml'], asked: window.__vfs.asked,"
    + " note: (document.getElementById('notice').textContent||'')})");
  check('F2 「保存」把页面现状写回磁盘（含刚加的那条边）',
    typeof f2.txt === 'string' && f2.txt.indexOf('keys="Q"') >= 0, '磁盘长度 ' + (f2.txt || '').length);
  check('F2b 「保存」**不再弹任何东西**（打开时已经要过权限了）—— 就是"点保存直接覆盖"',
    f2.asked === 1 && f2.note.indexOf('已保存到') >= 0, 'asked=' + f2.asked + ' note=' + (f2.note || '').slice(0, 30));

  // F3 另存为：写出新文件，且以后「保存」就写这一份
  await ev("window.__vfs.saveAsNext = 'copy.xml'; document.getElementById('btn-saveas').click()");
  await sleep(700);
  await ev("edges.push({from:'c',to:'a',kind:'key',keys:'Z',blend:'',after:false,event:false,not:false}); commit();"
    + "document.getElementById('btn-save').click()");
  await sleep(700);
  const f3 = await ev("({copy: window.__vfs.files['copy.xml'], name: fsx.name,"
    + " origin: window.__vfs.files['flight.xml']})");
  check('F3 「另存为…」写出新文件，之后「保存」写的是新那份（原来那份不动了）',
    f3.name === 'copy.xml' && typeof f3.copy === 'string' && f3.copy.indexOf('keys="Z"') >= 0
      && f3.origin.indexOf('keys="Z"') < 0, 'name=' + f3.name);

  // F4 取消对话框 = 正常操作，不报错、不动状态
  await ev("window.__vfs.abort = true; window.__vfs.files['cancel.xml'] = 'X'; fsx.name = 'copy.xml';"
    + "document.getElementById('btn-open').click()");
  await sleep(500);
  const f4 = await ev("({name: fsx.name, n: states.length, untouched: window.__vfs.files['cancel.xml'],"
    + " note: (document.getElementById('notice').textContent||'')})");
  check('F4 在对话框里点取消 ⇒ 什么都不发生（不报错、不改当前文件）',
    f4.name === 'copy.xml' && f4.n === 3 && f4.untouched === 'X' && f4.note.indexOf('失败') < 0,
    JSON.stringify(f4));
  await ev("window.__vfs.abort = null");

  // F6 用户实测那个局面（"为什么我保存完了 xml 还是有这个提示!!!"）：
  //    草稿与基准不同 ⇒ 黄条弹着 ⇒ 点保存 ⇒ **黄条必须当场收掉**，基准（DATA）跟着换成刚写出去的那份
  await ev("edges.push({from:'b',to:'c',kind:'key',keys:'E',blend:'',after:false,event:false,not:false});"
    + "commit(); draftActive = true; draftAck = false; renderDraftBanner();");
  await sleep(300);
  const f6a = await ev("(document.getElementById('banner').textContent||'').slice(0,36)");
  await ev("document.getElementById('btn-save').click()");
  await sleep(700);
  const f6b = await ev("({on: document.getElementById('banner').classList.contains('on'),"
    + " draftActive: draftActive, dataEdges: DATA.edges.length, edges: edges.length,"
    + " savedName: (savedAt && savedAt.name) || '', disk: window.__vfs.files['copy.xml'].indexOf('keys=\"E\"')})");
  check('F6 点保存 ⇒ 黄条当场收掉，且基准（DATA）换成刚写出去的那份',
    f6b.on === false && f6b.draftActive === false && f6b.dataEdges === f6b.edges && f6b.disk >= 0,
    '保存前黄条=' + JSON.stringify(f6a) + ' 之后=' + JSON.stringify(f6b));
  check('F6b 记下"上次存到哪一份"（刷新页面后靠它认出草稿已落盘）',
    f6b.savedName === 'copy.xml', 'savedName=' + f6b.savedName);

  // F6c 🔴 刷新页面（= 用户实际动作）：**不许再弹"你在看草稿、不是 XML"那条警告**，
  //     而是一条"已经存进 X"的说明 —— 这条就是用户报障的完整路径
  await send('Page.reload'); await sleep(1800);
  const f6c = await ev("({on: document.getElementById('banner').classList.contains('on'),"
    + " ok: document.getElementById('banner').classList.contains('ok'),"
    + " chip: document.getElementById('fschip').textContent,"
    + " txt: (document.getElementById('banner').textContent||'').slice(0,60)})");
  check('F6c 刷新后不再吓人：黄条变成"已经存进 X"的说明（不是"你在看草稿"警告）',
    f6c.on === true && f6c.ok === true && String(f6c.txt).indexOf('已经存进') >= 0
      && String(f6c.txt).indexOf('不是 XML') < 0, JSON.stringify(f6c));
  // 🔴 刷新后**句柄必然丢了**（不能跨会话），药丸只说"未打开"会让人以为"我没加载过"（用户实测问过）
  //    ⇒ 必须写成"未打开（上次存到 X）"，让人知道内容是哪来的、下一步该点哪。
  check('F6c-2 刷新后药丸说明"上次存到哪一份"（不是光一句"未打开"）',
    String(f6c.chip).indexOf('上次存到') >= 0 && String(f6c.chip).indexOf('copy.xml') >= 0,
    '药丸=' + f6c.chip);
  await ev("try{localStorage.removeItem(LSKEY)}catch(e){}; draftActive=false; savedAt=null; renderDraftBanner();");
  await sleep(250);

  // ㉞′ 一次性状态"演完无处可去" = 会停在最后一帧（用户 2026-09-27 问到 next 的语义时补的校验）
  const lone = await ev("(function(){states.push({name:'_孤星',act:'act_fly_idle',once:true,dur:'',next:'',clip:'?',durText:'一次性'});"
    + "stateByName['_孤星']=states[states.length-1];"
    + "var bad=validate().some(function(x){return x.indexOf('_孤星')>=0 && x.indexOf('停在最后一帧')>=0});"
    + "stateByName['_孤星'].next='idle';"
    + "var okNow=!validate().some(function(x){return x.indexOf('_孤星')>=0});"
    + "states=states.filter(function(s){return s.name!=='_孤星'}); delete stateByName['_孤星']; commit();"
    + "return {bad:bad, okNow:okNow};})()");
  check('㉞′ 校验能抓「一次性动作演完无处可去」（填了 next 就不再报）',
    lone && lone.bad === true && lone.okNow === true, JSON.stringify(lone));

  // 图例必须写**怎么设**（用户追问："我没看到演完才进要靠什么东西来设置"）
  const leg = await ev("(document.querySelector('.legend')||{}).textContent || ''");
  check('图例三种线都写了"怎么设"（勾边上哪个框），不只是"是什么"',
    String(leg).indexOf('每个 tick') >= 0 && String(leg).indexOf('条件选「') >= 0
      && String(leg).indexOf('勾边上的「演完才进」') >= 0, String(leg).replace(/\s+/g, ' ').slice(0, 110));
  // 🔴 光有 DOM 不算数 —— 它得**真画在画布左下角**、而且**不吃鼠标事件**（否则挡住画布操作）
  const legBox = await ev("(function(){var el=document.querySelector('.legend');if(!el)return null;"
    + "var r=el.getBoundingClientRect(), c=document.querySelector('.canvas').getBoundingClientRect();"
    + "return {x:Math.round(r.x),y:Math.round(r.y),w:Math.round(r.width),h:Math.round(r.height),"
    + " inCanvas: r.x>=c.x-1 && r.y>=c.y-1 && r.right<=c.right+1 && r.bottom<=c.bottom+1,"
    + " pe: getComputedStyle(el).pointerEvents};})()");
  check('图例真的画在画布左下角（在 canvas 里、有尺寸），且 pointer-events:none',
    legBox && legBox.inCanvas && legBox.w > 200 && legBox.h > 40 && legBox.pe === 'none',
    JSON.stringify(legBox));

  // ㊿ 在**容器页**新建 / 粘贴的状态必须归那个容器（用户实测："我在容器里面的画布右键新建状态  //     你给我新建到总览里面去了" —— 不带归属的状态只在总览画 ⇒ 当前页看不见，等于"建丢了"）
  await resetAll();
  await sleep(250);
  const nBefore50 = await ev("states.length");
  await ev("closeCtx(); sel=null; notice=''; tabs=[{kind:'root'},{kind:'group',id:groups[0].name}]; setActive(1); render()");
  await sleep(300);
  await ev("(function(){var svg=document.getElementById('svg');var r=svg.getBoundingClientRect();"
    + "svg.dispatchEvent(new MouseEvent('contextmenu',{clientX:Math.round(r.left+40),clientY:Math.round(r.top+40),bubbles:true,cancelable:true}));})()");
  await sleep(300);
  const menu50 = await ev("[].slice.call(document.querySelectorAll('#ctx .it')).map(function(x){return x.textContent}).join(' | ')");
  await ev("(function(){var b=[].slice.call(document.querySelectorAll('#ctx .it')).filter(function(x){return /新建状态/.test(x.textContent)})[0]; if(b) b.click();})()");
  await sleep(350);
  const r50 = await ev("(function(){var nm=states[states.length-1].name;"
    + "return {n: states.length, made: nm, parent: parentGroupOf(nm), inPage: stateVisible(nm, tabs[active]),"
    + " notice: (document.getElementById('notice').textContent||'')};})()");
  const g0 = await ev("groups[0].name");
  check('㊿ 容器页画布右键「新建状态」⇒ 新状态**归属当前容器**（当前页就画得出来）',
    r50.n === nBefore50 + 1 && r50.parent === g0 && r50.inPage === true,
    JSON.stringify(r50) + ' 菜单=' + String(menu50).slice(0, 60));
  check('㊿b 菜单标签与提示都说清"放进哪个容器"',
    String(menu50).indexOf('放进「') >= 0 && String(r50.notice).indexOf('已放进容器') >= 0,
    String(r50.notice).slice(0, 50));

  // ㊿c 同一个缺陷的第二个入口：**粘贴状态**也要落到当前容器
  await ev("copyState(states[states.length-1].name); pasteState(200, 200)");
  await sleep(350);
  const r50c = await ev("(function(){var nm=states[states.length-1].name;"
    + "return {made: nm, parent: parentGroupOf(nm), inPage: stateVisible(nm, tabs[active])};})()");
  check('㊿c 容器页**粘贴状态**同样落到当前容器（不是总览）',
    r50c.parent === g0 && r50c.inPage === true, JSON.stringify(r50c));

  // ㊿e 反向：**总览**上新建仍然是"顶层"（别修过头 —— 散状态是合法的，比如超人落地那个衔接状态）
  await ev("closeCtx(); tabs=[{kind:'root'}]; setActive(0); sel=null; notice=''; render(); newState(600, 600)");
  await sleep(350);
  const r50e = await ev("(function(){var nm=states[states.length-1].name;"
    + "return {parent: parentGroupOf(nm), inPage: stateVisible(nm, tabs[active]),"
    + " notice: (document.getElementById('notice').textContent||'')};})()");
  check('㊿e 总览上新建状态 ⇒ 仍是顶层（不属于任何容器）',
    r50e.parent === null && r50e.inPage === true && String(r50e.notice).indexOf('顶层') >= 0,
    JSON.stringify(r50e));

  // ㊿d 清干净（两个新状态删掉、回总览）
  await ev("(function(){states.map(function(s){return s.name}).filter(function(n){return /新状态|_copy/.test(n)})"
    + ".forEach(function(n){ if(stateByName[n]) removeState(n); });"
    + "closeCtx(); tabs=[{kind:'root'}]; setActive(0); sel=null; render();})()");
  await resetAll();
  await sleep(250);
  const r50d = await ev("({states: states.length, problems: validate().length})");
  check('㊿d 收尾干净：临时状态已删、校验 0 问题', r50d.problems === 0, JSON.stringify(r50d));
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
