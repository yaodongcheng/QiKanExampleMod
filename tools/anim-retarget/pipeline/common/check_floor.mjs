/* 全骨骼最低点 vs 地面：找出"任何人任何部位穿到地面以下"的段与相位（脚/头/躯干全都算） */
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
const base='http://127.0.0.1:8810/';
const L=new GLTFLoader(); const load=u=>new Promise((r,j)=>L.load(base+u,r,undefined,j));
const man=await (await fetch(base+'assets/manifest.json')).json();
const F={ue_g:'assets/ue_mannequin_ground.glb',ue_s:'assets/ue_mannequin_src.glb',
         bl_g:'assets/bannerlord_ground.glb', bl_s:'assets/bannerlord_src.glb'};
const G={},C={},W={},B={};
for(const k in F){ G[k]=await load(F[k]); C[k]={}; G[k].animations.forEach(c=>C[k][c.name.replace(/^RT_/,'')]=c);
  G[k].scene.updateWorldMatrix(true,true);
  const bx=new THREE.Box3(); G[k].scene.traverse(o=>{ if(o.isBone){const v=new THREE.Vector3();o.getWorldPosition(v);bx.expandByPoint(v);} });
  const s=1.75/(bx.max.y-bx.min.y); G[k].scene.scale.setScalar(s); G[k].scene.position.y=-bx.min.y*s;
  const w=new THREE.Group(); w.rotation.y=(k[0]==='b')?Math.PI:0; w.add(G[k].scene); w.updateWorldMatrix(true,true); W[k]=w;
  B[k]=[]; G[k].scene.traverse(o=>{ if(o.isBone) B[k].push(o); });
}
const V=new THREE.Vector3();
const minAll=k=>{ let m=Infinity; for(const o of B[k]){ o.getWorldPosition(V); if(V.y<m) m=V.y; } return m; };
const rows=[];
for(const n of Object.keys(man.clips)){
  const mode=man.clips[n].mode==='src'?'s':'g', ku='ue_'+mode, kb='bl_'+mode;
  if(!C[ku][n]||!C[kb][n]) continue;
  const A=C[ku][n], Bc=C[kb][n];
  const mA=new THREE.AnimationMixer(G[ku].scene), mB=new THREE.AnimationMixer(G[kb].scene);
  mA.stopAllAction(); mB.stopAllAction();
  const a=mA.clipAction(A), b=mB.clipAction(Bc); a.reset().play(); b.reset().play();
  let tMin=Infinity, sMin=Infinity, tF=0, dMax=0, dF=0;
  for(let i=0;i<=10;i++){
    const f=i/10; a.time=f*A.duration; b.time=f*Bc.duration; mA.update(0.0001); mB.update(0.0001);
    G[ku].scene.updateWorldMatrix(true,true); W[kb].updateWorldMatrix(true,true);
    const mt=minAll(kb), ms=minAll(ku);
    if(mt<tMin){ tMin=mt; tF=f; }
    sMin=Math.min(sMin,ms);
    if(Math.abs(mt-ms)>dMax){ dMax=Math.abs(mt-ms); dF=f; }
  }
  rows.push({n, mode:man.clips[n].mode, tMin, tF, sMin, dMax, dF});
}
const sink=rows.filter(r=>r.tMin<-0.03).sort((a,b)=>a.tMin-b.tMin);
console.log('=== 全 '+rows.length+' 段：骑砍侧「任何骨骼最低点」是否穿地 ===');
console.log('  穿地(<-0.03m) 段数: '+sink.length);
sink.slice(0,14).forEach(r=>console.log('   '+r.n.padEnd(22)+r.mode.padEnd(7)+'最低点 '+r.tMin.toFixed(3)+'m @'+r.tF.toFixed(1)+'  (源该段最低 '+r.sMin.toFixed(3)+')'));
const d=rows.map(r=>r.dMax).sort((a,b)=>a-b);
console.log('\n=== 源/骑砍 最低点高度差 ===');
console.log('  中位数 '+d[Math.floor(d.length*0.5)].toFixed(3)+'  75分位 '+d[Math.floor(d.length*0.75)].toFixed(3)+'  90分位 '+d[Math.floor(d.length*0.9)].toFixed(3)+'  >0.15m 段数 '+rows.filter(r=>r.dMax>0.15).length);
