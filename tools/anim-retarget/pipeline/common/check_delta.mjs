/* 正确口径：比较「相对静止姿态的旋转增量」ΔR = R_anim · R_rest⁻¹
   这样两套骨架的局部轴约定、骨长、父子命名全部约掉，剩下的就是“这段动画到底转了多少”。
   骑砍侧再按 Y 轴 180° 共轭到与源同向。
   自检：静止时 ΔR=I → 误差应为 0°。 */
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
const YAW=new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0,1,0),Math.PI);
const YAWINV=YAW.clone().invert();
const G={},C={},B={},REST={};
for(const k in F){
  G[k]=await load(F[k]);
  const g=new THREE.Group(); g.add(G[k].scene); g.updateWorldMatrix(true,true); G[k].root=g;   // 不转，保持各自坐标系
  C[k]={}; G[k].animations.forEach(c=>C[k][c.name.replace(/^RT_/,'')]=c);
  B[k]={}; REST[k]={};
  G[k].scene.updateWorldMatrix(true,true);
  G[k].scene.traverse(o=>{ if(o.isBone){ B[k][o.name]=o;
    const q=new THREE.Quaternion(); o.getWorldQuaternion(q); REST[k][o.name]=q; } });
}
const PAIRS=Object.entries(ALL).filter(([s,t])=>B.ue_g[s]&&B.bl_g[t]);
function delta(k,n){ const q=new THREE.Quaternion(); B[k][n].getWorldQuaternion(q);
  return q.multiply(REST[k][n].clone().invert()).normalize(); }
function errAll(ku,kb,mode){
  let sum=0,mx=0; const per={};
  for(const [s,t] of PAIRS){
    const a=delta(ku,s); let b=delta(kb,t);
    if(mode==='src'){} // 同一张坐标处理
    b = YAW.clone().multiply(b).multiply(YAWINV).normalize();    // 骑砍朝 -Z → 共轭到源朝向
    const d = 2*Math.acos(Math.min(1,Math.abs(a.dot(b))))*180/Math.PI;
    per[s]=d; sum+=d; mx=Math.max(mx,d);
  }
  return {mean:sum/PAIRS.length, max:mx, per};
}
// 自检：静止（未播动画）误差必须 ≈ 0
console.log('自检·静止姿态: mean '+errAll('ue_g','bl_g').mean.toFixed(3)+'°  (应≈0)');
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
    G[ku].scene.updateWorldMatrix(true,true); G[kb].scene.updateWorldMatrix(true,true);
    const r=errAll(ku,kb,mode); sum+=r.mean; cnt++; mx=Math.max(mx,r.max);
    for(const k in r.per){ (per[k]=per[k]||[]).push(r.per[k]); }
  }
  const worst=Object.entries(per).map(([k,v])=>[k,v.reduce((x,y)=>x+y,0)/v.length]).sort((x,y)=>y[1]-x[1]).slice(0,3);
  rows.push({n, mode:man.clips[n].mode, func:man.clips[n].func, weapon:man.clips[n].weapon, mean:sum/cnt, max:mx, worst});
}
const ok=rows.filter(r=>isFinite(r.mean));
const sorted=[...ok].sort((a,b)=>b.mean-a.mean);
const avg=ok.reduce((s,r)=>s+r.mean,0)/ok.length;
console.log('\n=== 280 段：旋转增量（相对静止）平均偏差 ===');
console.log('  全体 mean '+avg.toFixed(2)+'°   单根最差 '+Math.max(...ok.map(r=>r.max)).toFixed(2)+'°   中位数 '+sorted[Math.floor(sorted.length/2)].mean.toFixed(2)+'°');
console.log('  累计: '+[5,10,15,20,30].map(t=>t+'°内:'+ok.filter(r=>r.mean<t).length).join('  ')+'  (共 '+ok.length+')');
const gg=ok.filter(r=>r.mode==='ground'), ss=ok.filter(r=>r.mode==='src');
console.log('  ground('+gg.length+') mean '+(gg.reduce((s,r)=>s+r.mean,0)/gg.length).toFixed(2)+'°   src('+ss.length+') mean '+(ss.reduce((s,r)=>s+r.mean,0)/ss.length).toFixed(2)+'°');
console.log('\n=== 最差 12 段 ===');
sorted.slice(0,12).forEach(r=>console.log('  '+r.n.padEnd(24)+String(r.mode).padEnd(8)+r.mean.toFixed(2).padEnd(9)+r.max.toFixed(2).padEnd(9)+r.worst.map(w=>w[0]+'='+w[1].toFixed(1)).join(' ')));
console.log('\n=== 抽查 ===');
for(const n of ['002_01','002_07','002_06','013_17','Anim_CS_SP','Anim_CS_KD_F','Anim_BOW_KD_F','079_49','143_41','Anim_BOWST']){
  const r=ok.find(x=>x.n===n);
  console.log('  '+n.padEnd(16)+(r?('mode='+String(r.mode).padEnd(8)+'mean='+r.mean.toFixed(2)+'°  max='+r.max.toFixed(2)+'°'):'无数据'));
}
fs.writeFileSync('pose_delta_report.json', JSON.stringify({rows:ok}, null, 1));
console.log('\n写出 pose_delta_report.json');
