/* 决定性诊断：逐相位比较 骨盆高度 / 头高度 / 最低脚高度（归一化后米，脚贴地=0） */
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
import fs from 'node:fs';
const base = 'http://127.0.0.1:8810/';
const L=new GLTFLoader(); const load=u=>new Promise((r,j)=>L.load(base+u,r,undefined,j));
const man = await (await fetch(base+'assets/manifest.json')).json();
const F={ue_g:'assets/ue_mannequin_ground.glb',ue_s:'assets/ue_mannequin_src.glb',
         bl_g:'assets/bannerlord_ground.glb', bl_s:'assets/bannerlord_src.glb'};
const G={},C={},W={},B={};
for(const k in F){ G[k]=await load(F[k]); C[k]={}; G[k].animations.forEach(c=>C[k][c.name.replace(/^RT_/,'')]=c);
  const bx=new THREE.Box3(); G[k].scene.updateWorldMatrix(true,true);
  G[k].scene.traverse(o=>{ if(o.isBone){const v=new THREE.Vector3();o.getWorldPosition(v);bx.expandByPoint(v);} });
  const s=1.75/(bx.max.y-bx.min.y); G[k].scene.scale.setScalar(s); G[k].scene.position.y=-bx.min.y*s;
  const w=new THREE.Group(); w.rotation.y=(k[0]==='b')?Math.PI:0; w.add(G[k].scene); w.updateWorldMatrix(true,true); W[k]=w;
  B[k]={}; G[k].scene.traverse(o=>{ if(o.isBone) B[k][o.name]=o; });
}
const FEET={ ue:['foot_l','foot_r','ball_l','ball_r'], bl:['l_foot','r_foot','l_toe0','r_toe0'] };
function y(k,n){ const o=B[k][n]; if(!o) return NaN; const v=new THREE.Vector3(); o.getWorldPosition(v); return v.y; }
function row(ku,kb,clip,f){
  const A=C[ku][clip], Bc=C[kb][clip];
  const mA=new THREE.AnimationMixer(G[ku].scene), mB=new THREE.AnimationMixer(G[kb].scene);
  mA.stopAllAction(); mB.stopAllAction();
  const a=mA.clipAction(A), b=mB.clipAction(Bc); a.reset().play(); b.reset().play();
  a.time=f*A.duration; b.time=f*Bc.duration; mA.update(0.0001); mB.update(0.0001);
  G[ku].scene.updateWorldMatrix(true,true); W[kb].updateWorldMatrix(true,true);
  const minf=(kk,side)=>Math.min(...FEET[side].map(n=>y(kk,n)).filter(v=>isFinite(v)));
  return { f, ueP:y(ku,'pelvis'), blP:y(kb,'pelvis'), ueH:y(ku,'head'), blH:y(kb,'head'),
           ueF:minf(ku,'ue'), blF:minf(kb,'bl') };
}
const CLIPS = process.argv.slice(2);
const list = CLIPS.length ? CLIPS : ['002_06','Anim_CS_KD_F','Anim_BOW_KD_F','002_07','079_49','143_41'];
for (const n of list){
  const mode = man.clips[n] ? (man.clips[n].mode==='src'?'s':'g') : 'g';
  const ku='ue_'+mode, kb='bl_'+mode;
  console.log('\n['+n+']  mode='+man.clips[n].mode+'  func='+(man.clips[n].func||'')+'  weapon='+(man.clips[n].weapon||''));
  console.log('  相位   骨盆(源/骑砍 Δ)        头(源/骑砍 Δ)          最低脚(源/骑砍 Δ)');
  for (const f of [0.0,0.15,0.3,0.45,0.6,0.75,0.9]){
    const r=row(ku,kb,n,f);
    const F1=(a,b)=>(a.toFixed(3)+'/'+b.toFixed(3)+'(Δ'+(b-a>=0?'+':'')+(b-a).toFixed(3)+')').padEnd(24);
    console.log('  '+(f.toFixed(2)+' ').padEnd(6)+F1(r.ueP,r.blP)+F1(r.ueH,r.blH)+F1(r.ueF,r.blF));
  }
}
