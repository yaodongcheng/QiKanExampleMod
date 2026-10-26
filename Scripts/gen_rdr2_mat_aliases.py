"""从 rdr2_tex_manifest.json 生成材质别名表（给 tpaccli editormeta --aliases 用）。

内容 = 无贴图材质（原版靠顶点色/纯色）→ 纯色占位贴图。
· 名字按 Windows 大小写规则归一（同名只差大小写的只留一个拼写，和 FBX 导出用同一套规则）。
· 输出格式：`材质名=贴图资产名`，一行一条。
"""
import hashlib
import io
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# 🔴 必须与 Scripts/export_rdr2_fbx_tile.py 的 short_name() 完全一致：
#    Blender 导 FBX 时材质名 >63 字符会被截断加哈希，所以两边都先截到 55。
SHORT_LIMIT = 55


def short_name(name):
    if len(name) <= SHORT_LIMIT:
        return name
    h = hashlib.md5(name.encode("utf-8")).hexdigest()[:8]
    return name[:SHORT_LIMIT - 9] + "_" + h

MANIFEST = (r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
            r"\Debug\RD2构造相关\rdr2_tex_manifest.json")
OUT = (r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
       r"\Debug\RD2构造相关\rdr2_mat_aliases.txt")


def main():
    d = json.load(io.open(MANIFEST, encoding="utf-8"))
    mats = d["materials"]
    # 🔴 贴图资产名（= canonical albedo stem）。别名不能和它撞名：
    #    Windows 大小写不敏感，`bra_rf_metalroof01_ab_mtl.tpac` 会把
    #    `bra_rf_metalRoof01_ab_mtl.tpac` 顶掉（实测 4 例，材质变成纯色占位）。
    tex_names = {os.path.splitext(e["alb"])[0].lower()
                 for e in mats.values() if e.get("alb")}
    groups = {}
    dropped = 0
    for name, e in mats.items():
        if e.get("flat"):
            groups.setdefault(name.lower(), []).append(name)
    lines, dup = [], 0
    for variants in groups.values():
        variants.sort(key=lambda n: (-mats[n].get("uses", 0), n))
        canon = variants[0]
        if canon.lower() in tex_names:
            dropped += 1
            continue
        lines.append("%s=%s" % (short_name(canon), mats[canon]["alb"]))
        dup += len(variants) - 1
    # 有 albedo 的材质：名字 = 贴图名（去掉扩展名）。超长时 FBX 里是截断名，
    # 补一条别名让「截断名」也指向同一张贴图。
    long_alias = 0
    for name, e in mats.items():
        if e.get("flat") or not e.get("alb"):
            continue
        stem = os.path.splitext(e["alb"])[0]
        if short_name(stem) != stem:
            lines.append("%s=%s" % (short_name(stem), stem))
            long_alias += 1
    lines.sort()
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("# 无贴图材质 → 纯色占位贴图（材质名=贴图资产名，脚本生成勿手改）\n")
        for ln in lines:
            f.write(ln + "\n")
    print("别名 %d 条（无贴图材质 %d 个 / 合并大小写 %d 个 / 超长截断 %d 个 / 与贴图撞名丢弃 %d 个）→ %s"
          % (len(lines), len(groups) - dropped, dup, long_alias, dropped, OUT))


if __name__ == "__main__":
    main()
