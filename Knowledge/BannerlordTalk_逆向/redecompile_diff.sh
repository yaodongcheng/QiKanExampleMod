#!/bin/bash
# BannerlordTalk 重新反编译 + diff 一键脚本
# 用法: ./redecompile_diff.sh "<新包>/BannerlordTalk/bin/Win64_Shipping_Client/BannerlordTalk.dll" "<版本号>"
# 例:   ./redecompile_diff.sh "../BannerlordTalk-v1.1.0-BL1.4.8/BannerlordTalk/bin/Win64_Shipping_Client/BannerlordTalk.dll" "v1.1.0"
set -euo pipefail

DLL="${1:?需要 DLL 路径}"
VER="${2:?需要版本号（如 v1.1.0）}"
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/$VER"
OLD="$(ls -d "$ROOT"/v*/ 2>/dev/null | sort -V | tail -1 || true)"

mkdir -p "$OUT"

TYPES=(
  "BannerlordTalk.Runtime.PromptBuilder"
  "BannerlordTalk.Runtime.UserPromptBudget"
  "BannerlordTalk.Runtime.ResponseParser"
  "BannerlordTalk.Runtime.CampaignEventMemoryService"
  "BannerlordTalk.Runtime.NativeHeroContextProvider"
  "BannerlordTalk.Runtime.ChatterManagerDataSource"
  "BannerlordTalk.Runtime.CampaignChatterBehavior"
  "BannerlordTalk.Knowledge.StandaloneKnowledgeRetriever"
  "BannerlordTalk.Generation.Tts.FishTtsOptions"
  "BannerlordTalk.Generation.Tts.TtsTextComposer"
  "BannerlordTalk.Generation.Tts.TtsPlaybackService"
)

echo "==> 反编译 $VER ..."
for t in "${TYPES[@]}"; do
  ilspycmd "$DLL" -t "$t" -o "$OUT" 2>/dev/null || echo "  !! $t 反编译失败"
done
ilspycmd "$DLL" -l c 2>/dev/null > "$OUT/type_list.txt" || true

echo "==> 类型清单差异（新增/消失类型，新类型按需补反编译）:"
if [ -n "$OLD" ] && [ -f "$OLD/type_list.txt" ]; then
  diff <(sed 's/^Class //' "$OLD/type_list.txt" | sort) \
       <(sed 's/^Class //' "$OUT/type_list.txt" | sort) \
       | grep -E "^[<>]" | head -40 || true
else
  echo "  无旧快照，跳过"
fi

echo "==> 与旧版本 ($OLD) 的代码 diff 摘要:"
if [ -n "$OLD" ]; then
  for f in "$OUT"/*.decompiled.cs; do
    base="$(basename "$f")"
    if [ -f "$OLD/$base" ]; then
      n="$(diff "$OLD/$base" "$f" | grep -cE '^[<>]' || true)"
      [ "$n" -gt 0 ] && echo "  $base: $n 行差异"
    else
      echo "  $base: 新文件"
    fi
  done
else
  echo "  无旧快照，跳过"
fi

echo "==> 完成。按 Knowledge/BannerlordTalk_逆向/README.md 更新分析文档。"
