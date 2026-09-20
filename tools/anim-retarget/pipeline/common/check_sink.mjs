/* 全量筛查：逐段逐相位比较「源 / 骑砍」最低脚离地高度，找穿地(<-0.05m)与骨盆位移倍率异常 */
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
  B[k]={}; G[k].scene.traverse(o=>{ if(o.isBone) B[k][o.name]=o; });
}
const FEET={ue:['foot_l','foot_r','ball_l','ball_r'], bl:['l_foot','r_foot','l_toe0','r_toe0']};
const V=new THREE.Vector3();
const P=(k,n)=>{ const o=B[k][n]; if(!o) return null; o.getWorldPosition(V); return V.clone(); };
const minFoot=(k,side)=>{ let m=Infinity; for(const n of FEET[side]){ const v=P(k,n); if(v) m=Math.min(m,v.y);} return m; };
const rows=[];
for(const n of Object.keys(man.clips)){
  const mode = man.clips[n].mode==='src'?'s':'g', ku='ue_'+mode, kb='bl_'+mode;
  if(!C[ku][n]||!C[kb][n]) continue;
  const A=C[ku][n], Bc=C[kb][n];
  const mA=new THREE.AnimationMixer(G[ku].scene), mB=new THREE.AnimationMixer(G[kb].scene);
  mA.stopAllAction(); mB.stopAllAction();
  const a=mA.clipAction(A), b=mB.clipAction(Bc); a.reset().play(); b.reset().play();
  let worst=0, worstF=0, srcWorst=0, ratio=0, maxDelta=0, deltaF=0, sAt=0, tAt=0;
  for(let i=0;i<=10;i++){
    const f=i/10; a.time=f*A.duration; b.time=f*Bc.duration; mA.update(0.0001); mB.update(0.0001);
    G[ku].scene.updateWorldMatrix(true,true); W[kb].updateWorldMatrix(true,true);
    const mt=minFoot(kb,'bl'), ms=minFoot(ku,'ue');
    if(mt<worst){ worst=mt; worstF=f; }
    srcWorst=Math.min(srcWorst,ms);
    const dl=Math.abs(mt-ms);
    if(dl>maxDelta){ maxDelta=dl; deltaF=f; sAt=ms; tAt=mt; }
    // 骨盆位移相对各自静止的比值（只看水平位移幅度）
    const pa=P(ku,'pelvis'), pb=P(kb,'pelvis');
    const ds=Math.hypot(pa.x,pa.z), db=Math.hypot(pb.x,pb.z);
    if(ds>0.3) ratio=Math.max(ratio, db/ds);
  }
  rows.push({n, mode:man.clips[n].mode, worst, worstF, srcWorst, ratio, maxDelta, deltaF, sAt, tAt});
}
const bad=rows.filter(r=>r.maxDelta>0.08).sort((a,b)=>b.maxDelta-a.maxDelta);
const ratioBad=rows.filter(r=>r.ratio>1.35||(r.ratio>0&&r.ratio<0.7));
console.log('=== 全 '+rows.length+' 段：骑砍 vs 源 的「最低脚离地高度」最大差（真误差）===');
const d=rows.map(r=>r.maxDelta).sort((a,b)=>a-b);
const pct=q=>d[Math.floor(d.length*q)].toFixed(3);
console.log('  中位数 '+pct(0.5)+'m   75分位 '+pct(0.75)+'m   90分位 '+pct(0.9)+'m   >0.08m 段数 '+bad.length+' / '+rows.length);
bad.slice(0,12).forEach(r=>console.log('   '+r.n.padEnd(22)+r.mode.padEnd(7)+'Δ'+r.maxDelta.toFixed(3)+'m @相位'+r.deltaF.toFixed(1)+'  (源 '+r.sAt.toFixed(3)+' / 骑砍 '+r.tAt.toFixed(3)+')'));
console.log('\n=== 骨盆水平位移倍率异常(>1.35 或 <0.7)的段 ===');
console.log('  段数: '+ratioBad.length);
ratioBad.slice(0,15).forEach(r=>console.log('   '+r.n.padEnd(22)+r.mode.padEnd(7)+'倍率 '+r.ratio.toFixed(3)));
