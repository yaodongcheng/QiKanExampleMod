/* 逐段·逐关节 姿态误差表
   做法：两人各自把身高归一到 1.75m、脚下贴地，取映射骨的世界坐标；
        以各自 pelvis 为原点得到"相对关节向量"，再把骑砍侧绕 Y 转 180°（与查看器一致）后与源逐关节比较。
        误差单位 = 归一化后的米（1.75m 身高下的真实距离偏差）。
   输出：全部 280 段的 mean/max 误差，按误差排序；另单独打印被怀疑的几段。
*/
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
import fs from 'node:fs';

const PORT = process.env.PORT || '8810';
const base = 'http://127.0.0.1:'+PORT+'/';
const MAP  = JSON.parse(fs.readFileSync('../../ue5_to_bannerlord_map.json','utf8'));
const LBASE = JSON.parse(fs.readFileSync('lib/twist_map.json','utf8'));   // {src: tgt} 额外扭骨
const ALL = Object.assign({}, MAP.bone_map, LBASE);
const L = new GLTFLoader();
const load = u => new Promise((r,j)=>L.load(base+u, r, undefined, j));
const man = await (await fetch(base+'assets/manifest.json')).json();

const F = { ue_g:'assets/ue_mannequin_ground.glb', ue_s:'assets/ue_mannequin_src.glb',
            bl_g:'assets/bannerlord_ground.glb',   bl_s:'assets/bannerlord_src.glb' };
const G = {}, C = {}, WRAP = {}, SCALE = {};
for (const k in F){ G[k] = await load(F[k]);
  C[k] = {}; G[k].animations.forEach(c=>C[k][c.name.replace(/^RT_/,'')]=c);
  const bx = new THREE.Box3(); G[k].scene.updateWorldMatrix(true,true);
  G[k].scene.traverse(o=>{ if(o.isBone){ const v=new THREE.Vector3(); o.getWorldPosition(v); bx.expandByPoint(v);} });
  const h = bx.max.y - bx.min.y; SCALE[k] = 1.75/h;
  G[k].scene.scale.setScalar(SCALE[k]); G[k].scene.position.y = -bx.min.y*SCALE[k];
  const w = new THREE.Group(); w.rotation.y = (k[0]==='b') ? Math.PI : 0; w.add(G[k].scene);
  w.updateWorldMatrix(true,true); WRAP[k] = w;
  G[k].scene.updateWorldMatrix(true,true);
}
const BONE = {};
for (const k in F){ BONE[k] = {}; G[k].scene.traverse(o=>{ if(o.isBone) BONE[k][o.name] = o; }); }
const pairs = Object.entries(ALL).filter(([s,t])=>BONE.ue_g[s] && BONE.bl_g[t]);
console.log('可比对关节 '+pairs.length+' 对: '+pairs.map(p=>p[0]).join(','));

const V = new THREE.Vector3();
function pos(k, name){ const o=BONE[k][name]; V.set(0,0,0); o.getWorldPosition(V); return V.clone(); }
function normOffsets(k){
  const p = pos(k,'pelvis'); const out = {};
  for (const [s,t] of pairs){ const nm = (k[0]==='u') ? s : t; const v = pos(k,nm).sub(p); out[s]=v; }
  out.__pelvis = p.clone();
  return out;
}
function err(ku, kb, clip){
  const A = C[ku][clip], B = C[kb][clip];
  const mA = new THREE.AnimationMixer(G[ku].scene), mB = new THREE.AnimationMixer(G[kb].scene);
  mA.stopAllAction(); mB.stopAllAction();
  const a = mA.clipAction(A), b = mB.clipAction(B); a.reset().play(); b.reset().play();
  let mn = Infinity, mx = 0, sum = 0, n = 0, first = 0, last = 0;
  const per = {};
  for (const f of [0.15, 0.4, 0.65, 0.9]){
    a.time = f*A.duration; b.time = f*B.duration; mA.update(0.0001); mB.update(0.0001);
    G[ku].scene.updateWorldMatrix(true,true); WRAP[kb].updateWorldMatrix(true,true);
    const oa = normOffsets(ku), ob = normOffsets(kb);
    for (const [s,t] of pairs){
      const vt = ob[s].clone();   // 骑砍侧已被 WRAP 绕 Y 转 180°，此处直接比较
      const d = vt.sub(oa[s]).length();
      sum += d; n++; mx = Math.max(mx, d); mn = Math.min(mn, d);
      (per[s] = per[s] || []).push(d);
    }
    if (f===0.15) first = ob.__pelvis.y - oa.__pelvis.y;
    if (f===0.9)  last  = ob.__pelvis.y - oa.__pelvis.y;
  }
  const worst = Object.entries(per).map(([k,v])=>[k, v.reduce((x,y)=>x+y,0)/v.length]).sort((x,y)=>y[1]-x[1]);
  return { mean: sum/n, max: mx, min: mn, pelvisT0: first, pelvisT1: last, worst: worst.slice(0,3) };
}
const names = Object.keys(man.clips);
const rows = [];
let done = 0;
for (const n of names){
  const mode = man.clips[n].mode === 'src' ? 's' : 'g';
  const ku = 'ue_'+mode, kb = 'bl_'+mode;
  if (!C[ku][n] || !C[kb][n]) { rows.push({n, mode, mean: NaN, max: NaN, note:'缺 clip'}); continue; }
  const r = err(ku, kb, n); rows.push(Object.assign({n, mode:man.clips[n].mode, func:man.clips[n].func, weapon:man.clips[n].weapon}, r));
  if (++done % 100 === 0) console.log('  ... 已算 '+done+'/'+names.length);
}
const ok = rows.filter(r=>isFinite(r.mean));
ok.sort((a,b)=>b.mean-a.mean);
const p = (x)=>x.toFixed(3);
console.log('\n=== 全部 %d 段：关节平均误差（米，1.75m 身高下） ===', ok.length);
const avg = ok.reduce((s,r)=>s+r.mean,0)/ok.length;
const mx  = ok.reduce((s,r)=>Math.max(s,r.max),0);
console.log('  全体平均 '+avg.toFixed(3)+' m  全体最大 '+mx.toFixed(3)+' m');
const bins = { '≥0.15':0, '0.10~0.15':0, '0.06~0.10':0, '0.04~0.06':0, '<0.04':0 };
ok.forEach(r=>{ const m=r.mean;
  if(m>=0.15)bins['≥0.15']++; else if(m>=0.10)bins['0.10~0.15']++; else if(m>=0.06)bins['0.06~0.10']++;
  else if(m>=0.04)bins['0.04~0.06']++; else bins['<0.04']++; });
console.log('  分布:', JSON.stringify(bins));
console.log('\n=== 误差最大的 20 段 ===');
console.log('  '+'clip'.padEnd(24)+'mode'.padEnd(8)+'func'.padEnd(16)+'mean'.padEnd(10)+'max'.padEnd(10)+'最差关节');
ok.slice(0,20).forEach(r=>console.log('  '+r.n.padEnd(24)+String(r.mode).padEnd(8)+(r.func||'').slice(0,14).padEnd(16)
  +p(r.mean).padEnd(10)+p(r.max).padEnd(10)+r.worst.map(w=>w[0]+'='+p(w[1])).join(' ')));
console.log('\n=== 视觉专家点名的 3 段 ===');
for (const n of ['002_07','002_06','Anim_CS_KD_F','079_49','143_41','002_01','013_17','Anim_CS_SP']){
  const r = ok.find(x=>x.n===n);
  if(r) console.log('  '+n.padEnd(16)+' mode='+String(r.mode).padEnd(8)+' mean='+p(r.mean).padEnd(8)
    +' max='+p(r.max).padEnd(8)+' 骨盆高度差(首/末帧)='+r.pelvisT0.toFixed(3)+'/'+r.pelvisT1.toFixed(3)
    +'  最差: '+r.worst.map(w=>w[0]+'='+p(w[1])).join(' '));
  else console.log('  '+n+' 无数据');
}
fs.writeFileSync('pose_err_report.json', JSON.stringify(ok, null, 1));
console.log('\n明细写出 pose_err_report.json');
