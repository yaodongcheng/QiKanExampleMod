globalThis.ProgressEvent = class { constructor(t,i={}){ this.type=t; Object.assign(this,i);} };
import * as THREE from 'three';
import { GLTFLoader } from './lib/addons/loaders/GLTFLoader.js';
const base='http://127.0.0.1:8810/';
const L=new GLTFLoader(); const load=u=>new Promise((r,j)=>L.load(base+u,r,undefined,j));
for (const f of ['assets/ue_mannequin_ground.glb','assets/bannerlord_ground.glb',
                 'assets/ue_mannequin_src.glb','assets/bannerlord_src.glb']){
  const g=await load(f);
  let bones=0; g.scene.traverse(o=>{ if(o.isBone) bones++; });
  const c=g.animations[0];
  // 统计"位置轨道"与"旋转轨道"涉及的骨骼数量
  const rot=new Set(), pos=new Set();
  c.tracks.forEach(t=>{ const m=t.name.match(/^(.*)\.(quaternion|position|scale)$/);
    if(m){ if(m[2]==='quaternion') rot.add(m[1]); else if(m[2]==='position') pos.add(m[1]); } });
  console.log(f.split('/').pop().padEnd(30)+' 骨总数 '+String(bones).padEnd(5)+' 片段 '+g.animations.length
    +' | 首段['+c.name+'] 旋转轨道涉及骨 '+rot.size+'  位置轨道 '+pos.size);
  console.log('     示例旋转骨: '+[...rot].slice(0,8).map(s=>s.split('/').pop()).join(', '));
}
