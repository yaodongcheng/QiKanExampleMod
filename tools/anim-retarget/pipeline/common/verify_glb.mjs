/* 数值自检：clip 数量 / 名称对应 / 时长一致 / 骨盆·头·脚高度对比 / 朝向 */
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from './lib/three.module.js';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';

const PORT = process.env.PORT || '8810';
const base = 'http://127.0.0.1:' + PORT + '/';
const L = new GLTFLoader();
const load = u => new Promise((r,j)=>L.load(base+u, r, undefined, j));

const files = {
  ue_g:'assets/ue_mannequin_ground.glb', ue_s:'assets/ue_mannequin_src.glb',
  bl_g:'assets/bannerlord_ground.glb',   bl_s:'assets/bannerlord_src.glb',
};
const man = await (await fetch(base+'assets/manifest.json')).json();
const G = {};
for (const k in files){ try { G[k] = await load(files[k]); } catch(e){ G[k] = null; console.log('!! 载入失败', k, files[k], ''+e); } }

function clipMap(g){ const m={}; if(g) g.animations.forEach(c=>m[c.name.replace(/^RT_/,'')]=c); return m; }
const C = {}; for (const k in files) C[k] = clipMap(G[k]);

console.log('=== clip 数量 ===');
for (const k in files) console.log(' ', k.padEnd(5), G[k] ? (G[k].animations.length + ' 段') : '未载入');

const expect = { ue_g:0, ue_s:0, bl_g:0, bl_s:0 };
const bad = [];
for (const n in man.clips){
  const mode = man.clips[n].mode;
  const want = (mode==='src') ? ['ue_s','bl_s'] : ['ue_g','bl_g'];
  for (const k of want) expect[k]++;
  const okA = !!C[want[0]][n], okB = !!C[want[1]][n];
  if(!okA || !okB) bad.push(n+' 期望在 '+want.join('/')+' 中: ue='+okA+' bl='+okB);
}
console.log('=== manifest 280 段落在正确的 GLB 上 ===');
console.log('  期望分布', JSON.stringify(expect));
let cnt = {}; for (const k in C) cnt[k] = Object.keys(C[k]).length;
console.log('  实际分布', JSON.stringify(cnt));
console.log('  错位/缺失 ' + bad.length + ' 条');
bad.slice(0,8).forEach(b=>console.log('   -', b));

// 时长一致性（源 vs 骑砍）
console.log('=== 时长一致性（源 vs 骑砍，同 mode） ===');
let maxd = 0, worst = '';
for (const n in man.clips){
  const mode = man.clips[n].mode === 'src' ? 's' : 'g';
  const a = C['ue_'+mode][n], b = C['bl_'+mode][n];
  if(!a || !b) continue;
  const d = Math.abs(a.duration - b.duration);
  if(d > maxd){ maxd = d; worst = n; }
}
console.log('  最大时长差 %.4f s  (' + worst + ')', maxd);

// 姿态/高度对比：抽样几段，比较骨盆·头·脚的世界高度
function boneBox(root){ const bx=new THREE.Box3(); root.updateWorldMatrix(true,true);
  root.traverse(o=>{ if(o.isBone){ const v=new THREE.Vector3(); o.getWorldPosition(v); bx.expandByPoint(v);} }); return bx; }
function fit(root){ const b=boneBox(root); const h=b.max.y-b.min.y||1; const s=1.75/h;
  root.scale.setScalar(s); root.position.y=-b.min.y*s; root.updateWorldMatrix(true,true); return s; }
const wrap = {}; for (const k in files){ if(!G[k]) continue;
  const g = new THREE.Group(); g.rotation.y = (k[0]==='b') ? Math.PI : 0; g.add(G[k].scene);
  wrap[k] = g; fit(G[k].scene); }
function bw(k, name){ let o=null; G[k].scene.traverse(x=>{ if(x.name===name && !o) o=x; });
  const v=new THREE.Vector3(); o && o.getWorldPosition(v); return v; }

const samples = ['002_01','009_01','002_07','013_17','Anim_BOWST','Anim_CS_SP'].filter(n=>man.clips[n]);
console.log('=== 抽样姿态对比（骨盆 y / 头 y，单位=归一化后米） ===');
for (const n of samples){
  const mode = man.clips[n].mode === 'src' ? 's' : 'g';
  const ku='ue_'+mode, kb='bl_'+mode;
  const a = C[ku][n], b = C[kb][n]; if(!a||!b) { console.log(' ', n, '缺 clip'); continue; }
  const mA = new THREE.AnimationMixer(G[ku].scene), mB = new THREE.AnimationMixer(G[kb].scene);
  mA.stopAllAction(); mB.stopAllAction();
  const a1 = mA.clipAction(a), b1 = mB.clipAction(b);
  a1.reset().play(); b1.reset().play();
  const rows = [];
  for (const f of [0.1, 0.5, 0.9]){
    a1.time = f*a.duration; b1.time = f*b.duration;
    mA.update(0.0001); mB.update(0.0001);
    G[ku].scene.updateWorldMatrix(true,true); G[kb].scene.updateWorldMatrix(true,true);
    const pa=bw(ku,'pelvis'), pb=bw(kb,'pelvis');
    const ha=bw(ku,'head'),   hb=bw(kb,'head');
    rows.push('t='+f.toFixed(1)+' 骨盆 '+pa.y.toFixed(3)+'/'+pb.y.toFixed(3)+'(Δ'+(pb.y-pa.y).toFixed(3)+')'
              +' 头 '+ha.y.toFixed(3)+'/'+hb.y.toFixed(3)+'(Δ'+(hb.y-ha.y).toFixed(3)+')');
  }
  console.log('  ['+n+' | mode='+man.clips[n].mode+']');
  rows.forEach(r=>console.log('     '+r));
}
console.log('=== DONE ===');
