/* 干净口径：只用「映射骨 → 映射子骨」构成的肢段方向比较
   （父子两端都必须是映射骨，因此两套骨架的父子链语义一致，不再被“父骨不同名”污染）
   输出：每段 肢段平均角误差 / 最大角误差，以及全体分布。 */
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
import fs from 'node:fs';
const base='http://127.0.0.1:8810/';
const MAP=JSON.parse(fs.readFileSync('../../ue5_to_bannerlord_map.json','utf8'));
const ALL=Object.assign({},MAP.bone_map,JSON.parse(fs.readFileSync('lib/twist_map.json','utf8')));
const L=new GLTFLoader(); const load=u=>new Promise((r,j)=>L.load(base+u,r,undefined,j));
const man=await (await fetch(base+'assets/manifest.json')).json();
const F={ue_g:'assets/ue_mannequin_ground.glb',ue_s:'assets/ue_mannequin_src.glb',
         bl_g:'assets/bannerlord_ground.glb', bl_s:'assets/bannerlord_src.glb'};
const G={},C={},W={},B={},PAR={};
for(const k in F){ G[k]=await load(F[k]); C[k]={}; G[k].animations.forEach(c=>C[k][c.name.replace(/^RT_/,'')]=c);
  const bx=new THREE.Box3(); G[k].scene.updateWorldMatrix(true,true);
  G[k].scene.traverse(o=>{ if(o.isBone){const v=new THREE.Vector3();o.getWorldPosition(v);bx.expandByPoint(v);} });
  const s=1.75/(bx.max.y-bx.min.y); G[k].scene.scale.setScalar(s); G[k].scene.position.y=-bx.min.y*s;
  const w=new THREE.Group(); w.rotation.y=(k[0]==='b')?Math.PI:0; w.add(G[k].scene); w.updateWorldMatrix(true,true); W[k]=w;
  B[k]={}; PAR[k]={};
  G[k].scene.traverse(o=>{ if(o.isBone){ B[k][o.name]=o; let p=o.parent; while(p&&!p.isBone) p=p.parent;
    PAR[k][o.name]=p?p.name:null; } });
}
// 映射子骨：s 的子孙中"最近的映射骨"
function mappedChild(k, sname, mappedSet){
  const stack=[...B[k][sname].children]; let best=null;
  while(stack.length){ const o=stack.shift();
    if(o.isBone && mappedSet.has(o.name)) return o.name;            // 最近的映射骨
    stack.push(...o.children);
  }
  return null;
}
// 源侧映射骨集合 / 目标侧映射骨集合
const SRC=new Set(Object.keys(ALL)), TGT=new Set(Object.values(ALL));
const SEGS=[];
for (const [s,t] of Object.entries(ALL)){
  if(!B.ue_g[s]||!B.bl_g[t]) continue;
  const cs=mappedChild('ue_g',s,SRC), ct=mappedChild('bl_g',t,TGT);
  if(!cs||!ct) continue;
  SEGS.push({s,t,cs,ct});          // 两边都取“映射骨→映射子骨”，语义对齐
}
console.log('可比对肢段 %d 条: %s', SEGS.length, SEGS.map(x=>x.s+'→'+x.cs).join(', '));
const V=new THREE.Vector3(), V2=new THREE.Vector3();
function seg(k,a,b){ B[k][a].getWorldPosition(V); B[k][b].getWorldPosition(V2);
  const v=V.clone().sub(V2); return v.length()<1e-9?null:v.normalize(); }
function angles(ku,kb){ let sum=0,mx=0; const per={};
  for(const g of SEGS){ const a=seg(ku,g.s,g.cs), b=seg(kb,g.t,g.ct); if(!a||!b) continue;
    const d=Math.acos(Math.max(-1,Math.min(1,a.dot(b))))*180/Math.PI;
    sum+=d; mx=Math.max(mx,d); per[g.s+'→'+g.cs]=d; }
  return {mean:sum/SEGS.length, max:mx, per};
}
const rest=angles('ue_g','bl_g');
console.log('=== 静止姿态基线（未做重定向，纯套骨架差异）: mean %s° max %s° ===',
            rest.mean.toFixed(2), rest.max.toFixed(2));
const rows=[];
for(const n of Object.keys(man.clips)){
  const mode=man.clips[n].mode==='src'?'s':'g', ku='ue_'+mode, kb='bl_'+mode;
  if(!C[ku][n]||!C[kb][n]){ rows.push({n,mean:NaN}); continue; }
  const A=C[ku][n], Bc=C[kb][n];
  const mA=new THREE.AnimationMixer(G[ku].scene), mB=new THREE.AnimationMixer(G[kb].scene);
  mA.stopAllAction(); mB.stopAllAction();
  const a=mA.clipAction(A), b=mB.clipAction(Bc); a.reset().play(); b.reset().play();
  let sum=0,cnt=0,mx=0; const per={};
  for(const f of [0.05,0.3,0.55,0.8]){
    a.time=f*A.duration; b.time=f*Bc.duration; mA.update(0.0001); mB.update(0.0001);
    G[ku].scene.updateWorldMatrix(true,true); W[kb].updateWorldMatrix(true,true);
    const r=angles(ku,kb); sum+=r.mean; cnt++; mx=Math.max(mx,r.max);
    for(const k in r.per){ (per[k]=per[k]||[]).push(r.per[k]); }
  }
  const worst=Object.entries(per).map(([k,v])=>[k,v.reduce((x,y)=>x+y,0)/v.length]).sort((x,y)=>y[1]-x[1]).slice(0,3);
  rows.push({n, mode:man.clips[n].mode, func:man.clips[n].func, mean:sum/cnt, max:mx, worst});
}
const ok=rows.filter(r=>isFinite(r.mean));
const sorted=[...ok].sort((a,b)=>b.mean-a.mean);
const avg=ok.reduce((s,r)=>s+r.mean,0)/ok.length;
console.log('\n=== 280 段 肢段方向平均角误差（口径与 glb_check.py 一致） ===');
console.log('  全体 mean %s°   单根最差 %s°   中位数 %s°',
  avg.toFixed(2), Math.max(...ok.map(r=>r.max)).toFixed(2), sorted[Math.floor(sorted.length/2)].mean.toFixed(2));
const cum=[5,10,15,20,30].map(t=>t+'°内:'+ok.filter(r=>r.mean<t).length).join('  ');
console.log('  累计: '+cum+'  (共 '+ok.length+')');
console.log('  分组: ground mean '+ (ok.filter(r=>r.mode==='ground').reduce((s,r)=>s+r.mean,0)/ok.filter(r=>r.mode==='ground').length).toFixed(2)
  +'°   src mean '+(ok.filter(r=>r.mode==='src').reduce((s,r)=>s+r.mean,0)/ok.filter(r=>r.mode==='src').length).toFixed(2)+'°');
console.log('\n=== 最差 12 段 ===');
console.log('  '+'clip'.padEnd(24)+'mode'.padEnd(8)+'mean°.padEnd(9)max°'.padEnd(9)+'最差肢段');
sorted.slice(0,12).forEach(r=>console.log('  '+r.n.padEnd(24)+String(r.mode).padEnd(8)
  +r.mean.toFixed(2).padEnd(9)+r.max.toFixed(2).padEnd(9)+r.worst.map(w=>w[0]+'='+w[1].toFixed(1)).join(' ')));
console.log('\n=== 抽查 ===');
for(const n of ['002_01','002_07','002_06','013_17','Anim_CS_SP','Anim_CS_KD_F','079_49','143_41','Anim_BOWST']){
  const r=ok.find(x=>x.n===n);
  console.log('  '+n.padEnd(16)+(r?('mode='+String(r.mode).padEnd(8)+'mean='+r.mean.toFixed(2)+'°  max='+r.max.toFixed(2)+'°'):'无数据'));
}
fs.writeFileSync('pose_final_report.json', JSON.stringify({rest:rest.mean, rows:ok}, null, 1));
console.log('\n写出 pose_final_report.json');
