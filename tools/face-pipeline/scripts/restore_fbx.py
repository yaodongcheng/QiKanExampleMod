# -*- coding: utf-8 -*-
"""
restore_fbx.py —— 把备份目录里的 FBX 全量回填到模块 AssetSources。

为什么需要它（2026-09-13 实机确认）：
  🔴 **ModKit 删除 mesh 资产时，会连带删掉 AssetSources 里创建它的那个源 FBX。**
  （删除确认框里列出的 "Geometry file  head_tifa_a_v5.fbx" 就是磁盘上那个文件，不是内部条目）
  → 每轮"删旧网格 → 导新 FBX"都会顺手删掉一个源文件；迭代几轮后 AssetSources 就空了。
  → **备份目录才是权威副本**，AssetSources 只是"当前要导的那一份"的暂存区。

约定：
  · 权威副本 = D:\\BrainMaker\\blend_projects\\tifa_export\\backup_20260913\\*.fbx
  · 每次进编辑器前跑一次本脚本回填；生成脚本的【输入】也一律读备份目录，不读 AssetSources
    （双保险：即使 AssetSources 被清空，也还能重新生成）

用法：
  python restore_fbx.py                      # 回填到默认模块
  python restore_fbx.py --to <AssetSources>  # 回填到指定目录
  python restore_fbx.py --list               # 只列出备份内容
"""
import argparse
import os
import shutil
import sys

BACKUP = r'D:\BrainMaker\blend_projects\tifa_export\backup_20260913'
DEFAULT_TO = (r'H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12'
              r'\Mount & Blade II Bannerlord\Modules\TifaHead2\AssetSources')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--from', dest='src', default=BACKUP)
    ap.add_argument('--to', dest='dst', default=DEFAULT_TO)
    ap.add_argument('--list', action='store_true')
    a = ap.parse_args()

    if not os.path.isdir(a.src):
        sys.exit('备份目录不存在: %s' % a.src)
    fbx = sorted(f for f in os.listdir(a.src) if f.lower().endswith('.fbx'))

    if a.list:
        print('备份目录 %s 共 %d 个 FBX:' % (a.src, len(fbx)))
        for f in fbx:
            print('   %-26s %12s B' % (f, format(os.path.getsize(os.path.join(a.src, f)), ',')))
        return 0

    os.makedirs(a.dst, exist_ok=True)
    copied, skipped = [], []
    for f in fbx:
        s, d = os.path.join(a.src, f), os.path.join(a.dst, f)
        if os.path.exists(d) and os.path.getsize(d) == os.path.getsize(s):
            skipped.append(f)
            continue
        shutil.copy2(s, d)
        copied.append(f)

    print('回填到: %s' % a.dst)
    for f in copied:
        print('   [写入] %s' % f)
    for f in skipped:
        print('   [已有] %s' % f)
    print('共 %d 个（新写 %d，已存在 %d）' % (len(fbx), len(copied), len(skipped)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
