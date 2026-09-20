/* 移植 glb_check.py 的口径到 node：
   ① 每根映射骨取「最近的映射祖骨 → 该骨」的单位方向向量（父子两端都必须是映射骨）
   ② 穷举 4 个绕竖直轴旋转 × 2 个镜像 = 8 种全局朝向变换，取平均误差最小的那个（对两边公平）
   ③ 参考侧用「源 GLB」（原项目已验证它与源 FBX 差 0.29°），逐段输出 mean/max
   与 glb_check.py 的差别只有参考侧（GLB 代替 FBX）与采样点数。 */
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
import fs from 'node:fs';
const base='http://127.0.0.1:8810/';
const MAP=JSON.parse(fs.readFileSync('../../ue5_to_bannerlord_map.json','utf8')).bone_map;
const ALL=Object.assign({},MAP,JSON.parse(fs.readFileSync('lib/twist_map.json','utf8')));
const L=new GLTFLoader(); const load=u=>new Promise((r,j)=>L.load(base+u,r,undefined,j));
const man=await (await fetch(base+'assets/manifest.json')).json();
const F={ue_g:'assets/ue_mannequin_ground.glb',ue_s:'assets/ue_mannequin_src.glb',
         bl_g:'assets/bannerlord_ground.glb', bl_s:'assets/bannerlord_src.glb'};
const G={},C={},B={},PAR={};
for(const k in F){ G[k]=await load(F[k]); C[k]={}; G[k].animations.forEach(c=>C[k][c.name.replace(/^RT_/,'')]=c);
  B[k]={}; PAR[k]={}; G[k].scene.updateWorldMatrix(true,true);
  G[k].scene.traverse(o=>{ if(o.isBone){ B[k][o.name]=o; let p=o.parent; while(p&&!p.isBone) p=p.parent;
    PAR[k][o.name]=p?p.name:null; } });
}
const SRC=new Set(Object.keys(ALL)), TGT=new Set(Object.values(ALL));
// 最近的映射祖骨
function mp(k,mapped,nm){ let p=PAR[k][nm];
  while(p){ if(mapped.has(p)) return p; p=PAR[k][p]; } return null; }
const SEGS=[];
for(const [s,t] of Object.entries(ALL)){
  if(!B.ue_g[s]||!B.bl_g[t]) continue;
  const ps=mp('ue_g',SRC,s), pt=mp('bl_g',TGT,t);
  if(!ps||!pt) continue;
  SEGS.push({s,t,ps,pt});
}
console.log('可用肢段 %d 条: %s', SEGS.length, SEGS.map(x=>x.ps+'→'+x.s).join(', '));
const V=new THREE.Vector3(), V2=new THREE.Vector3();
function dirs(k){ const o={};
  for(const g of SEGS){ const nm=(k[0]==='u')?g.s:g.t, pn=(k[0]==='u')?g.ps:g.pt;
    B[k][nm].getWorldPosition(V); B[k][pn].getWorldPosition(V2);
    const v=V.clone().sub(V2); if(v.length()>1e-9) o[g.t]=v.normalize(); }
  return o; }
const LUTMP=[];
for(const deg of [0,90,180,-90]) LUTMP.push(new THREE.Matrix4().makeRotationY(deg*Math.PI/180));
const MIR=new THREE.Matrix4().makeScale(-1,1,1);
function best(S,T){
  let bv=null;
  for(let i=0;i<4;i++) for(const mir of [false,true]){
    const vals=[];
    for(const k in S){ if(!T[k]) continue;
      for(const b in S[k]){ if(!T[k][b]) continue;
        const w=S[k][b].clone();
        if(mir) w.applyMatrix4(MIR);
        w.applyMatrix4(LUTMP[i]);
        vals.push(Math.acos(Math.max(-1,Math.min(1,w.dot(T[k][b]))))*180/Math.PI); } }
    if(!vals.length) continue;
    const m=vals.reduce((a,b)=>a+b,0)/vals.length;
    if(bv===null||m<bv.mean) bv={mean:m, max:Math.max(...vals), tag:'rotY'+[0,90,180,-90][i]+(mir?'+镜像':'')};
  }
  return bv;
}
const NS=9;
const rows=[];
let done=0;
for(const n of Object.keys(man.clips)){
  const mode=man.clips[n].mode==='src'?'s':'g', ku='ue_'+mode, kb='bl_'+mode;
  if(!C[ku][n]||!C[kb][n]){ rows.push({n, mean:NaN}); continue; }
  const A=C[ku][n], Bc=C[kb][n];
  const mA=new THREE.AnimationMixer(G[ku].scene), mB=new THREE.AnimationMixer(G[kb].scene);
  mA.stopAllAction(); mB.stopAllAction();
  const a=mA.clipAction(A), b=mB.clipAction(Bc); a.reset().play(); b.reset().play();
  const S=[], T=[];
  for(let i=0;i<NS;i++){
    const f=i/(NS-1);
    a.time=f*A.duration; b.time=f*Bc.duration; mA.update(0.0001); mB.update(0.0001);
    G[ku].scene.updateWorldMatrix(true,true); G[kb].scene.updateWorldMatrix(true,true);
    S.push(dirs(ku)); T.push(dirs(kb));
  }
  const r=best(S,T);
  rows.push({n, mode:man.clips[n].mode, func:man.clips[n].func, weapon:man.clips[n].weapon,
             mean:r?r.mean:NaN, max:r?r.max:NaN, tag:r?r.tag:'', dur:A.duration});
  if(++done%60===0) console.log('  ... '+done+'/'+Object.keys(man.clips).length);
}
const ok=rows.filter(r=>isFinite(r.mean));
const sorted=[...ok].sort((a,b)=>b.mean-a.mean);
const avg=ok.reduce((s,r)=>s+r.mean,0)/ok.length;
const avgMax=ok.reduce((s,r)=>Math.max(s,r.max),0);
console.log('\n=== 280 段：重定向质量（口径 = glb_check.py，参考侧换成了源 GLB） ===');
console.log('  全体 mean '+avg.toFixed(2)+'°   全体 max '+avgMax.toFixed(2)+'°   中位数 '+sorted[Math.floor(sorted.length/2)].mean.toFixed(2)+'°');
console.log('  累计: '+[10,20,30,40,60].map(t=>t+'°内:'+ok.filter(r=>r.mean<t).length).join('  ')+'  (共 '+ok.length+')');
const gg=ok.filter(r=>r.mode==='ground'), ss=ok.filter(r=>r.mode==='src');
console.log('  ground('+gg.length+') mean '+(gg.reduce((s,r)=>s+r.mean,0)/gg.length).toFixed(2)+'°   src('+ss.length+') mean '+(ss.reduce((s,r)=>s+r.mean,0)/ss.length).toFixed(2)+'°');
const tags={}; ok.forEach(r=>tags[r.tag]=(tags[r.tag]||0)+1);
console.log('  最优变换分布: '+JSON.stringify(tags));
console.log('\n=== 最好 5 段 / 最差 15 段 ===');
[...sorted.slice(-5)].forEach(r=>console.log('  最好 '+r.n.padEnd(24)+r.mean.toFixed(2)+'°  max '+r.max.toFixed(2)+'°'));
sorted.slice(0,15).forEach(r=>console.log('  最差 '+r.n.padEnd(24)+String(r.mode).padEnd(8)+r.mean.toFixed(2).padEnd(8)+r.max.toFixed(2).padEnd(9)+r.tag));
console.log('\n=== 抽查 ===');
for(const n of ['002_01','002_07','002_06','013_17','Anim_CS_SP','Anim_CS_KD_F','Anim_BOW_KD_F','079_49','143_41','Anim_BOWST']){
  const r=ok.find(x=>x.n===n);
  console.log('  '+n.padEnd(16)+(r?('mode='+String(r.mode).padEnd(8)+'mean='+r.mean.toFixed(2)+'°  max='+r.max.toFixed(2)+'°'):'无数据'));
}
fs.writeFileSync('quality_report.json', JSON.stringify(ok, null, 1));
console.log('\n写出 quality_report.json');
