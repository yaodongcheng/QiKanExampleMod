# -*- coding: utf-8 -*-
"""build_armor_chain.py — 一条命令跑完「源件 → 可进编辑器的甲」全流程。

为什么要有它：两步是**有序且不可反**的 ——
  · build_textures.py 依赖 build_armor.py 产出的**干净 UV**；顺序反了/拿自己的输出再跑
    会把 UV 映射两次（实测：图集利用率从 65% 涨到 96%，出的图全错）；
  · build_textures.py 的 `--diffuse` 必须是**原始图集**，指错会把源图覆盖掉（实测踩过）。
把顺序固定在代码里，这两个坑就不可能再犯。

跑完自动做「关卡 1」体检（fbx_probe：导出规格），并把结果打成一页摘要。

用法（系统 python，不需要 Blender）:
  python build_armor_chain.py \\
      --src  "D:/BrainMaker/.../export/fbx/L00_yukimura.fbx" \\
      --diffuse "D:/BrainMaker/.../web/textures/L00_yukimura.png" \\
      --name taikou_yukimura_do_a

  # 换角色只改 --src / --diffuse / --name；零件与缩放已按首件调好
"""
import argparse
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.join(HERE, "scripts")
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
SKEL_DEFAULT = os.path.join(
    r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12",
    "Mount & Blade II Bannerlord", "modding_resources", "skeletons", "human_skeleton.fbx")
PROBE = os.path.join(REPO, "tools", "face-pipeline", "scripts", "fbx_probe.py")

# 首件调好的参数（详见 README 的参数表与坑表）
DEFAULTS = dict(
    parts="body_kimono_arms",
    r="0.0120",
    r_arms="0.0160",
    ao="0.35",
)


def run(cmd, tag):
    print("\n" + "=" * 74)
    print("  " + tag)
    print("=" * 74)
    p = subprocess.run(cmd, cwd=REPO)
    return p.returncode


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("--src", required=True, help="源件 FBX（须自带绑定）")
    ap.add_argument("--diffuse", required=True, help="源件原始漫反射图集 PNG")
    ap.add_argument("--name", required=True, help="资产名（= FBX 内网格名 = 材质名 = Item XML 的 mesh=）")
    ap.add_argument("--out", default=os.path.join(HERE, "out"))
    ap.add_argument("--skel", default=SKEL_DEFAULT)
    ap.add_argument("--parts", default=DEFAULTS["parts"])
    ap.add_argument("--r", default=DEFAULTS["r"], help="躯干径向缩放")
    ap.add_argument("--r-arms", default=DEFAULTS["r_arms"], help="手臂径向缩放（沿骨轴另有解剖段长缩放，不用管）")
    ap.add_argument("--ao", default=DEFAULTS["ao"], help="_s 的 AO 强度")
    ap.add_argument("--nrm", default="0.30", help="_n 细节强度")
    ap.add_argument("--lod", default="0.834,0.563,0.249,0.140,0.072")
    ap.add_argument("--no-lod", action="store_true")
    a = ap.parse_args()

    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    for f, what in ((a.src, "源件 FBX"), (a.diffuse, "原始图集"), (a.skel, "官方骨架")):
        if not os.path.isfile(f):
            print("!! %s 不存在：%s" % (what, f))
            return 2
    if os.path.abspath(a.diffuse) == os.path.abspath(os.path.join(out, a.name + "_d.png")):
        print("!! --diffuse 不能指向输出文件 <out>/%s_d.png" % a.name)
        return 2

    # 原始图集另存一份到 out/ 下当"只读源"，避免误覆盖（build_textures 内部也有防呆）
    src_diffuse = os.path.join(out, "_src_" + os.path.basename(a.diffuse))
    if not os.path.exists(src_diffuse):
        shutil.copy2(a.diffuse, src_diffuse)
        print("已备份原始图集 ->", src_diffuse)

    # ── 第 1 步：网格 ──
    c1 = [BLENDER, "-b", "--python", os.path.join(SCRIPTS, "build_armor.py"), "--",
          "--src", os.path.abspath(a.src), "--skel", os.path.abspath(a.skel),
          "--out", out, "--name", a.name, "--parts", a.parts,
          "--kimono-torso-only", "--no-hands",
          "--r", a.r, "--r-arms", a.r_arms, "--lod", a.lod]
    if a.no_lod:
        c1.append("--no-lod")
    if run(c1, "第 1/3 步：网格（选件 → 重定向骨架 → LOD → 导出 FBX）") != 0:
        print("!! 第 1 步失败")
        return 1
    fbx = os.path.join(out, a.name + ".fbx")
    if not os.path.isfile(fbx):
        print("!! 第 1 步没产出", fbx)
        return 1

    # ── 第 2 步：贴图（必须跑在第 1 步之后）──
    c2 = [BLENDER, "-b", "--python", os.path.join(SCRIPTS, "build_textures.py"), "--",
          "--armor", fbx, "--src", os.path.abspath(a.src), "--diffuse", src_diffuse,
          "--out", out, "--name", a.name, "--parts", a.parts,
          "--ao", a.ao, "--nrm", a.nrm]
    if run(c2, "第 2/3 步：贴图（裁图集 + 生成 _n/_s + 重映射 UV）") != 0:
        print("!! 第 2 步失败")
        return 1

    # ── 第 3 步：关卡 1 体检 ──
    print("\n" + "=" * 74)
    print("  第 3/3 步：关卡 1 体检（导出规格）")
    print("=" * 74)
    subprocess.run([sys.executable, PROBE, fbx], cwd=REPO)

    print("\n" + "=" * 74)
    print("  产出（%s）" % out)
    print("=" * 74)
    for f in sorted(os.listdir(out)):
        if f.startswith(a.name) or f.startswith("_src_"):
            print("   %-44s %8.0f KB" % (f, os.path.getsize(os.path.join(out, f)) / 1024))
    print("""
  接下来（人做）：
    1. ModKit 导入 %s.fbx        unit=m ／ 不勾 Z-up ／ 只勾 Import meshes
    2. 材质勾 Bumpmap + Skinning ／ 挂三张贴图（_d 底色、_n 法线、_s 金属粗糙AO）
    3. Model Viewer 验（Add Entity -> Human -> 挂 Body parts -> 放 lord_walk，甲要跟着动）
    4. Publish（**目标选模块外**，保护 AssetPackages）-> pack0.tpac 拷进 Taikou\\AssetPackages\\
    5. Item XML 的 mesh= 必须 = %s
""" % (a.name, a.name))
    return 0


if __name__ == "__main__":
    sys.exit(main())
