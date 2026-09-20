globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
const base='http://127.0.0.1:8810/';
const L=new GLTFLoader(); const load=u=>new Promise((r,j)=>L.load(base+u,r,undefined,j));
const man=await (await fetch(base+'assets/manifest.json')).json();
const A=await load('assets/ue_mannequin_ground.glb'), B=await load('assets/ue_mannequin_src.glb');
const Cg={}, Cb={}, Cs={};
(await load('assets/bannerlord_ground.glb')).animations.forEach(c=>Cg[c.name.replace(/^RT_/,'')]=c);
(await load('assets/bannerlord_src.glb')).animations.forEach(c=>Cs[c.name.replace(/^RT_/,'')]=c);
A.animations.forEach(c=>{});
const U={ground:{},src:{}};
A.animations.forEach(c=>U.ground[c.name.replace(/^RT_/,'')]=c);
B.animations.forEach(c=>U.src[c.name.replace(/^RT_/,'')]=c);
let nU=0,nB=0,miss=[],dur=[];
for(const n of Object.keys(man.clips)){
  const mode = man.clips[n].mode==='src'?'src':'ground';
  const a=U[mode][n], b=(mode==='src'?Cs:Cg)[n];
  if(a)nU++; if(b)nB++;
  if(!a) miss.push(n);
  if(a&&b){ const d=Math.abs(a.duration-b.duration); if(d>1e-4) dur.push(n+' Δ'+d.toFixed(4)); }
}
console.log('全量源侧 ground '+A.animations.length+' 段 / src '+B.animations.length+' 段');
console.log('manifest 280 | 源命中 '+nU+' | 骑砍命中 '+nB+' | 源缺 '+miss.length+' | 时长不一致 '+dur.length);
console.log(((nU===280&&nB===280&&!miss.length&&!dur.length)?'✅ 全量版可用，可删分片':'❌ 全量版不完备，保留分片'));
