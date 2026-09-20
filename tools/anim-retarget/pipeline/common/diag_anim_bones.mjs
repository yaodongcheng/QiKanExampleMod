/* 统计一段动画里"真正在动"的骨骼数量（旋转轨道采样方差） */
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
const base='http://127.0.0.1:8810/';
const L=new GLTFLoader(); const load=u=>new Promise((r,j)=>L.load(base+u,r,undefined,j));
const JOBS=[['assets/ue_mannequin_ground.glb','002_01'],['assets/bannerlord_ground.glb','002_01'],
            ['assets/ue_mannequin_ground.glb','013_17'],['assets/bannerlord_ground.glb','013_17'],
            ['assets/ue_mannequin_src.glb','002_06'],['assets/bannerlord_src.glb','002_06']];
for (const [f,clip] of JOBS){
  const g=await load(f); const c=g.animations.find(x=>x.name===clip);
  if(!c){ console.log(f,clip,'无此片段'); continue; }
  let moving=0, total=0; const names=[];
  for (const t of c.tracks){
    const m=t.name.match(/^(.*)\.quaternion$/); if(!m) continue; total++;
    const v=t.values; let mx=0;
    for(let i=4;i<v.length;i+=4){ const d=Math.abs(v[i]*v[0]+v[i+1]*v[1]+v[i+2]*v[2]+v[i+3]*v[3]);
      mx=Math.max(mx, 2*Math.acos(Math.min(1,d))*180/Math.PI); }
    if(mx>2){ moving++; if(names.length<10) names.push(m[1].split('/').pop()+'='+mx.toFixed(0)+'°'); }
  }
  console.log(f.split('/').pop().padEnd(28)+' ['+clip+'] 旋转轨道 '+total+'  真正在动(>2°) '+moving);
  console.log('     '+names.join('  '));
}
