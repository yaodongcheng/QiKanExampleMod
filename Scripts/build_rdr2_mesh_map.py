"""建立「glTF 模型名 → 编辑器里实际可用的 Metamesh 名」对照表。

为什么需要：
  编辑器把 `<名>_lod` 当成 `<名>` 的 LOD 级别来导入（原生行为）：
    · `<名>` 不存在时 → 它把网格注册成 `<名>`（去掉 _lod）
    · `<名>` 已存在时 → 撞名，这一件被跳过（网格仍在 `<名>` 上）
  所以摆场景时**不能拿文件/模型名直接当网格名**，必须查这张表。

产出：Debug/RD2构造相关/rdr2_mesh_map.json
    { "模型名": "Metamesh 名" }  —— 查不到的模型不会写进来（= 真的缺网格）

依赖：先跑一次 tpaccli list（本脚本会自己跑）。
"""
import io
import json
import os
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
          r"\Mount & Blade II Bannerlord\Modules\RDR2")
TPACCLI = os.path.join(ROOT, r"tools\tpactool\TpacToolCLI\bin\Release\net9.0\tpaccli.exe")
MASTER = os.path.join(MODULE, r"AssetSources\ImortReady\rdr2_bld_fbx")
BUILDINGS = os.path.join(MODULE, r"Assets\buildings")
OUT = os.path.join(ROOT, r"Debug\RD2构造相关\rdr2_mesh_map.json")

# 明确登记的人工兜底（数量极少，逐条写清楚原因）
KNOWN_FALLBACKS = {
    # 36 字节空包，唯一一件没导进来的；全图只用 1 次。用同一栋楼的非封板版本顶替。
    "arm_05_generalbuilding05x_brd": "arm_05_generalbuilding05x",
}


def metamesh_names():
    r = subprocess.run([TPACCLI, "list", "--packdir", BUILDINGS],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    meta = set()
    for line in (r.stdout or "").splitlines():
        n = line.split("\t")[0].strip()
        if not n or n.startswith("Loaded"):
            continue
        if not n.endswith(".fbx"):
            meta.add(n)
    return meta


def main():
    meta = metamesh_names()
    seeds = sorted({f[:-4] for f in os.listdir(MASTER) if f.endswith(".fbx")})
    mp, missing = {}, []
    for n in seeds:
        if n in meta:
            mp[n] = n
            continue
        base = None
        for suf in ("_lod", "_lod001"):
            if n.endswith(suf):
                base = n[:-len(suf)]
                break
        if base and base in meta:
            mp[n] = base
            continue
        fb = KNOWN_FALLBACKS.get(n)
        if fb and fb in meta:
            mp[n] = fb
            continue
        missing.append(n)
    with io.open(OUT, "w", encoding="utf-8") as f:
        json.dump(mp, f, ensure_ascii=False, indent=1)
    print("Metamesh 总数 %d · 模型 %d · 可映射 %d · 缺网格 %d"
          % (len(meta), len(seeds), len(mp), len(missing)))
    print("缺网格的:", missing[:10])
    print("→", OUT)


if __name__ == "__main__":
    main()
