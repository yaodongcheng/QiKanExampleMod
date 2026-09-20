/* 分片覆盖自检：源侧(6 片) ∪ 骑砍侧(2 片) 必须都等于 manifest 的 280 段，且 mode 一致、时长一致 */
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
const base='http://127.0.0.1:8810/';
const L=new GLTFLoader(); const load=u=>new Promise((r,j)=>L.load(base+u,r,undefined,j));
const man=await (await fetch(base+'assets/manifest.json')).json();
const UE={ ground:['assets/ue_mannequin_ground.glb'],
           src:   ['assets/ue_mannequin_src.glb'] };
const BL={ ground:['assets/bannerlord_ground.glb'], src:['assets/bannerlord_src.glb'] };
const clipsOf = g => { const m={}; g.animations.forEach(c=>m[c.name.replace(/^RT_/,'')]=c); return m; };
const UEC={}, BLC={};
for (const mode of ['ground','src']){
  UEC[mode]={}; BLC[mode]={};
  for(const u of UE[mode]){ let g; try{ g=await load(u); }catch(e){ console.log('  !! 源片缺失 '+u); continue; }
    const m=clipsOf(g); const n=Object.keys(m).length;
    Object.assign(UEC[mode], m);
    console.log('  源片 '+u.split('/').pop().padEnd(20)+n+' 段'); }
  for(const u of BL[mode]){ const g=await load(u); const m=clipsOf(g);
    Object.assign(BLC[mode], m);
    console.log('  骑砍片 '+u.split('/').pop().padEnd(20)+Object.keys(m).length+' 段'); }
}
const want = Object.keys(man.clips);
const missUE=[], missBL=[], wrongMode=[], durBad=[];
let nU=0,nB=0;
for(const n of want){
  const mode = man.clips[n].mode==='src'?'src':'ground';
  const a = UEC[mode][n], b = BLC[mode][n];
  if(a) nU++; if(b) nB++;
  if(!a){
    const other = mode==='src'?'ground':'src';
    missUE.push(n + (UEC[other][n] ? '  (在 ' + other + ' 片里，mode 不符)' : ''));
  }
  if(!b) missBL.push(n);
  if(a && b){ const d=Math.abs(a.duration-b.duration); if(d>1e-4) durBad.push(n+' Δ'+d.toFixed(4)+'s'); }
}
// 反向：片里有但 manifest 没有的多余段
const extra = [];
for(const mode of ['ground','src'])
  for(const n of Object.keys(UEC[mode])) if(!man.clips[n]) extra.push('源:'+n);
console.log('\n=== 覆盖自检 ===');
console.log('  manifest 280 段 | 源侧命中 '+nU+' | 骑砍侧命中 '+nB);
console.log('  源侧缺 '+missUE.length+(missUE.length?': '+missUE.slice(0,6).join(', '):''));
console.log('  骑砍侧缺 '+missBL.length+(missBL.length?': '+missBL.slice(0,6).join(', '):''));
console.log('  时长不一致 '+durBad.length+(durBad.length?': '+durBad.slice(0,5).join(', '):''));
console.log('  多余段 '+extra.length+(extra.length?': '+extra.slice(0,6).join(', '):''));
console.log('  结论: ' + ((nU===280 && nB===280 && !missUE.length && !missBL.length && !durBad.length)
  ? '✅ 280 段源/骑砍两侧齐备、时长一致' : '❌ 有问题，见上'));
