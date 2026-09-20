/* 针对"到地下/跑飞"类问题：打印两具骨架的骨盆/头/最低脚 的 XYZ（单位=归一化米） */
globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
const base='http://127.0.0.1:8810/';
const L=new GLTFLoader(); const load=u=>new Promise((r,j)=>L.load(base+u,r,undefined,j));
const man=await (await fetch(base+'assets/manifest.json')).json();
const F={ue_g:'assets/ue_mannequin_ground.glb',ue_s:'assets/ue_mannequin_src.glb',
         bl_g:'assets/bannerlord_ground.glb', bl_s:'assets/bannerlord_src.glb'};
const G={},C={},W={},B={},SC={},POS={};
for(const k in F){ G[k]=await load(F[k]); C[k]={}; G[k].animations.forEach(c=>C[k][c.name.replace(/^RT_/,'')]=c);
  G[k].scene.updateWorldMatrix(true,true);
  const bx=new THREE.Box3(); G[k].scene.traverse(o=>{ if(o.isBone){const v=new THREE.Vector3();o.getWorldPosition(v);bx.expandByPoint(v);} });
  const s=1.75/(bx.max.y-bx.min.y); SC[k]=s;
  G[k].scene.scale.setScalar(s); G[k].scene.position.y=-bx.min.y*s;
  const w=new THREE.Group(); w.rotation.y=(k[0]==='b')?Math.PI:0; w.add(G[k].scene); w.updateWorldMatrix(true,true); W[k]=w;
  B[k]={}; G[k].scene.traverse(o=>{ if(o.isBone) B[k][o.name]=o; });
  POS[k]=JSON.parse('{}');
}
const FEET={ ue:['foot_l','foot_r','ball_l','ball_r'], bl:['l_foot','r_foot','l_toe0','r_toe0'] };
const V=new THREE.Vector3();
const P=(k,n)=>{ const o=B[k][n]; if(!o) return null; o.getWorldPosition(V); return V.clone(); };
for (const clip of process.argv.slice(2)){
  const mode = man.clips[clip] ? (man.clips[clip].mode==='src'?'s':'g') : 's';
  const ku='ue_'+mode, kb='bl_'+mode;
  console.log('\n['+clip+'] mode='+man.clips[clip].mode+'  func='+man.clips[clip].func+'  源缩放='+SC[ku].toFixed(4)+' 骑砍缩放='+SC[kb].toFixed(4));
  console.log('  相位   源骨盆(x,y,z)                 骑砍骨盆(x,y,z)                  源最低脚y / 骑砍最低脚y');
  const A=C[ku][clip], Bc=C[kb][clip];
  const mA=new THREE.AnimationMixer(G[ku].scene), mB=new THREE.AnimationMixer(G[kb].scene);
  mA.stopAllAction(); mB.stopAllAction();
  const a=mA.clipAction(A), b=mB.clipAction(Bc); a.reset().play(); b.reset().play();
  for(const f of [0,0.15,0.3,0.45,0.6,0.75,0.9]){
    a.time=f*A.duration; b.time=f*Bc.duration; mA.update(0.0001); mB.update(0.0001);
    G[ku].scene.updateWorldMatrix(true,true); W[kb].updateWorldMatrix(true,true);
    const pa=P(ku,'pelvis'), pb=P(kb,'pelvis');
    const mf=(k,side)=>Math.min(...FEET[side].map(n=>{const v=P(k,n); return v?v.y:Infinity;}));
    const fm=(v)=>v?('('+v.x.toFixed(2)+','+v.y.toFixed(2)+','+v.z.toFixed(2)+')'):'(无)';
    console.log('  '+(f.toFixed(2)+' ').padEnd(6)+fm(pa).padEnd(30)+fm(pb).padEnd(30)
      +mf(ku,'ue').toFixed(3)+' / '+mf(kb,'bl').toFixed(3));
  }
}
