/* 逐段·肢段方向角误差（与原项目 glb_check.py 同口径，与骨长无关）
   err = angle( 源: parent→bone 方向 , 骑砍: parent→bone 方向 )
   另外给出「静止姿态基线」：不做任何动画时同一批骨的角误差，用来扣除两套骨架本身的比例/朝向差异。
*/
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
import fs from 'node:fs';
const PORT = process.env.PORT || '8810';
const base = 'http://127.0.0.1:'+PORT+'/';
const MAP  = JSON.parse(fs.readFileSync('../../ue5_to_bannerlord_map.json','utf8'));
const ALL  = Object.assign({}, MAP.bone_map, JSON.parse(fs.readFileSync('lib/twist_map.json','utf8')));
const L = new GLTFLoader();
const load = u => new Promise((r,j)=>L.load(base+u, r, undefined, j));
const man = await (await fetch(base+'assets/manifest.json')).json();
const F = { ue_g:'assets/ue_mannequin_ground.glb', ue_s:'assets/ue_mannequin_src.glb',
            bl_g:'assets/bannerlord_ground.glb',   bl_s:'assets/bannerlord_src.glb' };
const G={}, C={}, WRAP={}, BONE={}, PAR={};
for (const k in F){
  G[k] = await load(F[k]);
  C[k] = {}; G[k].animations.forEach(c=>C[k][c.name.replace(/^RT_/,'')]=c);
  const bx=new THREE.Box3(); G[k].scene.updateWorldMatrix(true,true);
  G[k].scene.traverse(o=>{ if(o.isBone){ const v=new THREE.Vector3(); o.getWorldPosition(v); bx.expandByPoint(v);} });
  const s = 1.75/(bx.max.y-bx.min.y);
  G[k].scene.scale.setScalar(s); G[k].scene.position.y = -bx.min.y*s;
  const w=new THREE.Group(); w.rotation.y=(k[0]==='b')?Math.PI:0; w.add(G[k].scene); w.updateWorldMatrix(true,true); WRAP[k]=w;
  BONE[k]={}; PAR[k]={};
  G[k].scene.traverse(o=>{ if(o.isBone){ BONE[k][o.name]=o;
    let p=o.parent; while(p && !p.isBone) p=p.parent;
    PAR[k][o.name]= p ? p.name : null; } });
}
const V=new THREE.Vector3(), V2=new THREE.Vector3();
function dir(k, name){  // parent→bone 单位方向
  const pn = PAR[k][name]; if(!pn) return null;
  const a=BONE[k][name], b=BONE[k][pn];
  a.getWorldPosition(V); b.getWorldPosition(V2);
  const v=V.clone().sub(V2); return v.length()<1e-9 ? null : v.normalize();
}
const pairs = Object.entries(ALL).filter(([s,t])=>BONE.ue_g[s]&&BONE.bl_g[t]&&PAR.ue_g[s]&&PAR.bl_g[t]);
function angles(ku,kb){
  const out={}, res=[];
  for (const [s,t] of pairs){
    const a=dir(ku,s), b=dir(kb,t); if(!a||!b) continue;
    const d = Math.acos(Math.max(-1,Math.min(1,a.dot(b))))*180/Math.PI;
    out[s]=d; res.push(d);
  }
  return {out, mean: res.reduce((x,y)=>x+y,0)/res.length, max: Math.max(...res)};
}
// 静止姿态基线
G.ue_g.scene.updateWorldMatrix(true,true); WRAP.bl_g.updateWorldMatrix(true,true);
const rest = angles('ue_g','bl_g');
console.log('=== 静止姿态基线（无动画，扣除骨架比例差异后的“地板误差”） ===');
console.log('  mean '+rest.mean.toFixed(2)+'°  max '+rest.max.toFixed(2)+'°   可比对 '+pairs.length+' 根');
const rows=[];
for (const n of Object.keys(man.clips)){
  const mode = man.clips[n].mode==='src' ? 's':'g';
  const ku='ue_'+mode, kb='bl_'+mode;
  if(!C[ku][n]||!C[kb][n]){ rows.push({n, mean:NaN, max:NaN}); continue; }
  const A=C[ku][n], B=C[kb][n];
  const mA=new THREE.AnimationMixer(G[ku].scene), mB=new THREE.AnimationMixer(G[kb].scene);
  mA.stopAllAction(); mB.stopAllAction();
  const a=mA.clipAction(A), b=mB.clipAction(B); a.reset().play(); b.reset().play();
  let sum=0,cnt=0,mx=0; const per={};
  for (const f of [0.15,0.4,0.65,0.9]){
    a.time=f*A.duration; b.time=f*B.duration; mA.update(0.0001); mB.update(0.0001);
    G[ku].scene.updateWorldMatrix(true,true); WRAP[kb].updateWorldMatrix(true,true);
    const r=angles(ku,kb);
    sum+=r.mean; cnt++; mx=Math.max(mx,r.max);
    for(const k in r.out){ (per[k]=per[k]||[]).push(r.out[k]); }
  }
  const worst=Object.entries(per).map(([k,v])=>[k, v.reduce((x,y)=>x+y,0)/v.length]).sort((x,y)=>y[1]-x[1]).slice(0,3);
  rows.push({n, mode:man.clips[n].mode, func:man.clips[n].func, weapon:man.clips[n].weapon,
             mean:sum/cnt, max:mx, worst});
}
const ok=rows.filter(r=>isFinite(r.mean));
const avg=ok.reduce((s,r)=>s+r.mean,0)/ok.length;
const mxAll=ok.reduce((s,r)=>Math.max(s,r.max),0);
const sorted=[...ok].sort((a,b)=>b.mean-a.mean);
console.log('\n=== 全部 '+ok.length+' 段：肢段方向平均角误差 ===');
console.log('  全体 mean ' + avg.toFixed(2) + '°   全体最大单根骨 ' + mxAll.toFixed(2) + '°');
const bin={}; [5,10,15,20,30,45].forEach(t=>bin['<'+t+'°']=ok.filter(r=>r.mean<t).length);
console.log('  累计分布: '+JSON.stringify(bin));
console.log('  中位数 ' + sorted[Math.floor(sorted.length/2)].mean.toFixed(2) + '°');
console.log('\n=== 误差最大的 15 段 ===');
console.log('  '+'clip'.padEnd(24)+'mode'.padEnd(8)+'func'.padEnd(16)+'mean°.padEnd(8)max°'.padEnd(10)+'最差三根');
sorted.slice(0,15).forEach(r=>console.log('  '+r.n.padEnd(24)+String(r.mode).padEnd(8)+(r.func||'').slice(0,14).padEnd(16)
  +r.mean.toFixed(2).padEnd(9)+r.max.toFixed(2).padEnd(10)+r.worst.map(w=>w[0]+'='+w[1].toFixed(1)).join(' ')));
console.log('\n=== 视觉专家点名的 3 段（对照基线 '+rest.mean.toFixed(2)+'°） ===');
for (const n of ['002_07','002_06','Anim_CS_KD_F','079_49','143_41','002_01','013_17','Anim_CS_SP','Anim_BOW_KD_F']){
  const r=ok.find(x=>x.n===n);
  console.log(r? ('  '+n.padEnd(16)+' mode='+String(r.mode).padEnd(8)+' mean='+r.mean.toFixed(2)+'°  max='+r.max.toFixed(2)+'°  最差: '+r.worst.map(w=>w[0]+'='+w[1].toFixed(1)).join(' '))
             : '  '+n+' 无数据');
}
fs.writeFileSync('pose_angle_report.json', JSON.stringify({rest_baseline_deg:rest.mean, rows:ok}, null, 1));
console.log('\n写出 pose_angle_report.json');
