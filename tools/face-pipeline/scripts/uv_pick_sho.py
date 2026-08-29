# uv_pick_sho.py — 自适应通用版: 任意头部网格 -> uvview 渲染 -> 3D 解剖点采样 uv
# 用法: python uv_pick_sho.py <obj> [--uvview 输出.png]
# 解剖定位用 bbox 归一化高度 (0=顶,1=底) + z 前脸带
import re
import sys
import numpy as np
from PIL import Image

OBJ = sys.argv[1] if len(sys.argv) > 1 else r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\meshes\sho_head_male_japanese.obj"
OUT_VV = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\uvview_sho.png"

verts, uvs, faces, names, cur = [], [], [], [], -1
for line in open(OBJ, encoding="utf-8", errors="ignore"):
    if line.startswith("o "):
        cur += 1; names.append(line[2:].strip())
    elif line.startswith("v "):
        m = re.match(r"v\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        verts.append(tuple(map(float, m.groups())))
    elif line.startswith("vt"):
        m = re.match(r"vt\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        uvs.append(tuple(map(float, m.groups())))
    elif line.startswith("f"):
        parts = line[2:].split()
        vi = [int(p.split("/")[0]) - 1 for p in parts]
        ti = [int(p.split("/")[1]) - 1 for p in parts]
        faces.append((cur, vi, ti))

V = np.array(verts); U = np.array(uvs)
print("mesh:", OBJ, "verts", len(V), "uvs", len(U), "faces", len(faces), "submeshes", len(names))
print("bbox x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]" % (V[:,0].min(), V[:,0].max(), V[:,1].min(), V[:,1].max(), V[:,2].min(), V[:,2].max()))

xmin, xmax = V[:,0].min(), V[:,0].max()
ymin, ymax = V[:,1].min(), V[:,1].max()
zmax = V[:,2].max()
Wp = Hp = 1024

def to_screen(p):
    sx = (p[0] - xmin) / (xmax - xmin) * Wp
    sy = Hp * 0.95 - (p[1] - ymin) / (ymax - ymin) * Hp * 0.9
    return int(sx), int(sy)

# 渲染 uvview(颜色=uv)
img = np.zeros((Hp, Wp, 3), np.uint8); img[:] = (40,40,50)
for mesh_idx, vi, ti in faces:
    for i in range(0, len(vi)-2, 3):
        a,b,c = vi[i],vi[i+1],vi[i+2]; ta,tb,tc = ti[i],ti[i+1],ti[i+2]
        p0 = to_screen(V[a]); p1 = to_screen(V[b]); p2 = to_screen(V[c])
        x0,y0 = p0; x1,y1 = p1; x2,y2 = p2
        minx = int(max(0,min(x0,x1,x2))); maxx = int(min(Wp-1,max(x0,x1,x2)))
        miny = int(max(0,min(y0,y1,y2))); maxy = int(min(Hp-1,max(y0,y1,y2)))
        if maxx<minx or maxy<miny: continue
        det = (y1-y2)*(x0-x2)+(x2-x1)*(y0-y2)
        if abs(det)<1e-12: continue
        ys,xs = np.meshgrid(np.arange(miny,maxy+1), np.arange(minx,maxx+1), indexing="ij")
        xx = xs.astype(np.float64); yy = ys.astype(np.float64)
        l0 = ((y1-y2)*(xx-x2)+(x2-x1)*(yy-y2))/det
        l1 = ((y2-y0)*(xx-x2)+(x0-x2)*(yy-y2))/det
        l2 = 1.0-l0-l1
        mask = (l0>=-1e-4)&(l1>=-1e-4)&(l2>=-1e-4)
        uu = (l0*U[ta][0]+l1*U[tb][0]+l2*U[tc][0])[mask]
        vv = (l0*U[ta][1]+l1*U[tb][1]+l2*U[tc][1])[mask]
        col = np.clip(np.stack([uu,vv,np.zeros_like(uu)],axis=1)*255,0,255).astype(np.uint8)
        img[ys[mask].astype(int), xs[mask].astype(int)] = col
Image.fromarray(img).save(OUT_VV)
print("saved uvview:", OUT_VV)

def sample(u_norm, v_norm, label):
    """u_norm/v_norm: 归一化坐标 (0=顶). 取 z 前脸带内该高度的点"""
    y = ymin + v_norm * (ymax - ymin)
    # 集所有顶点于该 y 的 ±0.5% + z>zmax-0.08
    sel = np.abs(V[:,1]-y) < 0.004*(ymax-ymin)
    sel &= V[:,2] > zmax - 0.10
    sel &= np.abs(V[:,0] - (xmin + u_norm*(xmax-xmin))) < 0.02*(xmax-xmin)
    if sel.sum() < 1:
        print(f"{label}: no verts"); return
    uvmean = U[sel].mean(0)
    sx, sy = to_screen(V[sel].mean(0))
    drawn = img[sy, sx] if 0 <= sy < Hp and 0 <= sx < Wp else None
    print(f"{label}: uv_center=({uvmean[0]:.4f},{uvmean[1]:.4f})  screen_pick=({drawn[0]/255:.4f},{drawn[1]/255:.4f})" if drawn is not None else f"{label}: uv_center=({uvmean[0]:.4f},{uvmean[1]:.4f})")

# 解剖点 (归一化: v 0=顶), x u_norm 0.5=中线
sample(0.46, 0.62, "R_EYE")
sample(0.54, 0.62, "L_EYE")
sample(0.50, 0.72, "NOSE_TIP")
sample(0.50, 0.78, "MOUTH")
sample(0.50, 0.55, "BROW")
sample(0.50, 0.86, "CHIN")
sample(0.50, 0.45, "FOREHEAD")
