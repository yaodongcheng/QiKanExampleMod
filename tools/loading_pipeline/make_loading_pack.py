# -*- coding: utf-8 -*-
"""
太阁5 事件图(已转 PNG) → 骑砍2 loading tpac 打包管线
=====================================================
前置：dds_to_png_wic.ps1 已把 image_event/*.dds 转成 PNG（WIC 系统解码，
      输出 1920x1080 横屏 - 该 dds 布局非标准，自研 DXT 解码会出条纹）。

输入：--png-dir（WIC 输出目录，文件 EVSTILL_*.png）
处理：PIL 读 PNG → （可选 --size 缩放）→ PNG 24bit
输出：
  gen/PNG/taikou_loading_001.png ...
  gen/manifest.json
  gen/TaikouSpriteData.xml   （引擎全局合并 SpriteData：79 张池 + loading_01~12 槽位）
  gen/taikou_loading.tpac    （tpaccli makepack 产物）

生成物禁止手改（铁律 22）：改内容 = 改本脚本重跑。
用法：
  powershell -ExecutionPolicy Bypass -File dds_to_png_wic.ps1 -SrcDir E:/taikou5/TaikouImage/image_event -OutDir gen/WIC
  python make_loading_pack.py --png-dir gen/WIC
  # 要压缩尺寸加 --size 1280x720
"""
import argparse
import json
import os
import re
import subprocess
import sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
TPACCLI = os.path.join(HERE, "..", "face-pipeline", "tpactool", "TpacToolCLI", "bin", "Release", "net9.0", "tpaccli.exe")

CAT = "taikou_loading"           # SpriteCategory 名（纹理键名 = {CAT}_{N} 短名）
HILITE_W, HILITE_H = 1920, 1080  # loading 窗口显示尺寸（渲染按 widget 拉伸）
SLOT_COUNT = 12                  # 原版 loading_01~12 槽位覆盖数（零代码兜底路径）
DEFAULT_SIZE = (1920, 1080)      # 默认不缩放（素材原生即全高清）


def natural_key(name):
    m = re.search(r"EVSTILL_(\d+)_", name)
    return int(m.group(1)) if m else 0


def build_sprite_data_xml(n_textures, sheet_w, sheet_h):
    """生成 SpriteData XML。零注释（解析器手坑）；全子元素形式。"""
    lines = ['<?xml version="1.0" encoding="utf-8"?>', '<SpriteData>', '  <SpriteCategories>']
    lines.append('    <SpriteCategory>')
    lines.append(f'      <Name>{CAT}</Name>')
    lines.append(f'      <SpriteSheetCount>{n_textures}</SpriteSheetCount>')
    for i in range(1, n_textures + 1):
        lines.append(f'      <SpriteSheetSize ID="{i}" Width="{sheet_w}" Height="{sheet_h}" />')
    lines.append('    </SpriteCategory>')
    lines.append('  </SpriteCategories>')
    lines.append('  <SpriteParts>')

    def spritepart(name, sheet_id):
        return [
            '    <SpritePart>',
            f'      <SheetID>{sheet_id}</SheetID>',
            f'      <Name>{name}</Name>',
            f'      <Width>{HILITE_W}</Width>',
            f'      <Height>{HILITE_H}</Height>',
            '      <SheetX>0</SheetX>',
            '      <SheetY>0</SheetY>',
            f'      <CategoryName>{CAT}</CategoryName>',
            '    </SpritePart>',
        ]

    for i in range(1, n_textures + 1):
        lines += spritepart(f"{CAT}_{i:03d}", i)
    for i in range(1, SLOT_COUNT + 1):
        lines += spritepart(f"loading_{i:02d}", i)
    lines.append('  </SpriteParts>')
    lines.append('  <Sprites>')
    for i in range(1, n_textures + 1):
        lines.append('    <GenericSprite>')
        lines.append(f'      <Name>{CAT}_{i:03d}</Name>')
        lines.append(f'      <SpritePartName>{CAT}_{i:03d}</SpritePartName>')
        lines.append('    </GenericSprite>')
    for i in range(1, SLOT_COUNT + 1):
        lines.append('    <GenericSprite>')
        lines.append(f'      <Name>loading_{i:02d}</Name>')
        lines.append(f'      <SpritePartName>loading_{i:02d}</SpritePartName>')
        lines.append('    </GenericSprite>')
    lines.append('  </Sprites>')
    lines.append('</SpriteData>')
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--png-dir", required=True, help="WIC 转换出的 PNG 目录（EVSTILL_*.png）")
    ap.add_argument("--out", default=os.path.join(HERE, "gen"))
    ap.add_argument("--tpaccli", default=TPACCLI)
    ap.add_argument("--size", default=None, help="可选输出尺寸(如 1280x720，默认素材原尺寸)")
    ap.add_argument("--no-tpac", action="store_true", help="只出 PNG/SpriteData，不调 tpaccli")
    args = ap.parse_args()

    if args.size:
        target = tuple(int(x) for x in args.size.split("x"))
    else:
        target = None

    names = sorted([n for n in os.listdir(args.png_dir) if re.match(r"EVSTILL_\d+_\d+\.png", n)],
                   key=natural_key)
    if not names:
        sys.exit("no EVSTILL_*.png in " + args.png_dir)
    n = len(names)
    print(f"found {n} png")

    png_dir = os.path.join(args.out, "PNG")
    os.makedirs(png_dir, exist_ok=True)

    manifest_packs = []
    for idx, name in enumerate(names, start=1):
        img = Image.open(os.path.join(args.png_dir, name)).convert("RGB")
        sheet_w, sheet_h = img.size
        if target:
            img = img.resize(target, Image.LANCZOS)
            sheet_w, sheet_h = target
        png_name = f"{CAT}_{idx:03d}.png"
        img.save(os.path.join(png_dir, png_name), "PNG")
        manifest_packs.append({
            "name": f"{CAT}_{idx:03d}",
            "png": os.path.join(png_dir, png_name),
            "width": sheet_w,
            "height": sheet_h,
        })
        if idx % 20 == 0 or idx == n:
            print(f"  {idx}/{n}")

    manifest = {"outDir": args.out, "packs": [{"packName": CAT, "textures": manifest_packs}]}
    manifest_path = os.path.join(args.out, "manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)

    xml_path = os.path.join(args.out, "TaikouSpriteData.xml")
    with open(xml_path, "w", encoding="utf-8") as f:
        f.write(build_sprite_data_xml(n, sheet_w, sheet_h))

    print("PNG:" + png_dir)
    print("manifest:" + manifest_path)
    print("spritedata:" + xml_path)

    if args.no_tpac:
        return 0
    if not os.path.exists(args.tpaccli):
        sys.exit("tpaccli not found: " + args.tpaccli + " (dotnet build -c Release 先)")
    r = subprocess.run([args.tpaccli, "makepack", "--manifest", manifest_path, "--out", args.out],
                       capture_output=True, text=True)
    print(r.stdout)
    if r.returncode != 0:
        print(r.stderr)
        return r.returncode
    return 0


if __name__ == "__main__":
    sys.exit(main())
