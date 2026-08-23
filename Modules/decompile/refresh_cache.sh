#!/bin/bash
# ══════════════════════════════════════════════════════════════════
# 反编译缓存刷新 — Modules/decompile/<版本>/<类型>.cs
#
# 用途：新游戏版本（或新备份 DLL）到达后，一键补全该版本的关键类型
#       反编译缓存。签名对比流程：先 grep 缓存，未命中再单独 ilspycmd。
#
# 用法：
#   bash refresh_cache.sh <版本号> [DLL源目录]
#     <版本号>     如 1.3.15 / 1.5.1
#     [DLL源目录]  可选。缺省自动解析：1.2.12→Modules/1.2.12DLL，
#                  1.3.15→游戏 bin，1.4.6→Modules/1.4.6DLL，
#                  1.5.1→Modules/1.5.1DLL；
#                  其他版本→游戏 bin\Win64_Shipping_Client
#
# 例：
#   bash refresh_cache.sh 1.3.15                       # 当前游戏版本
#   bash refresh_cache.sh 1.5.1                        # Latest 备份（1.5.1DLL）
#   bash refresh_cache.sh 1.5.0 "D:\MB2\bin\Win64_Shipping_Client"
#
# 新增类型：直接往下方 TYPES 数组加一行（DLL别名|类型全名），
#           再把 DLL 别名映射加进 DL 数组。
# ══════════════════════════════════════════════════════════════════
set -u
VERSION="${1:?用法: bash refresh_cache.sh <版本号> [DLL源目录]}"
MB2="${MB2_PATH:-h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord}"
LWN="$MB2/Modules/LivingWorldNpcs"

# DLL 源目录解析（core 源）
case "$VERSION" in
  1.2.12) SRC="${2:-$LWN/Modules/1.2.12DLL}" ;;
  1.3.15) SRC="${2:-$MB2/bin/Win64_Shipping_Client}" ;;
  1.4.6)  SRC="${2:-$LWN/Modules/1.4.6DLL}" ;;
  1.5.1)  SRC="${2:-$LWN/Modules/1.5.1DLL}" ;;
  *)      SRC="${2:-$MB2/bin/Win64_Shipping_Client}" ;;
esac
# SandBox 源：1.3.15 在模块目录，其余默认与 core 同目录
SB_SRC="${3:-}"
if [ -z "$SB_SRC" ]; then
  case "$VERSION" in
    1.3.15) SB_SRC="$MB2/Modules/SandBox/bin/Win64_Shipping_Client" ;;
    *)      SB_SRC="$SRC" ;;
  esac
fi
DEST="$LWN/Modules/decompile/$VERSION"
mkdir -p "$DEST"
echo "═══ 刷新反编译缓存: v$VERSION ═══"
echo "  core 源: $SRC"
echo "  sandbox 源: $SB_SRC"
echo "  输出: $DEST"

# DLL 别名 → 文件名（含可选的独立源目录；空 = 用 core 源）
declare -A DL=(
  [CampaignSystem]="TaleWorlds.CampaignSystem.dll"
  [MountAndBlade]="TaleWorlds.MountAndBlade.dll"
  [Engine]="TaleWorlds.Engine.dll"
  [GauntletUI]="TaleWorlds.Engine.GauntletUI.dll"
  [Localization]="TaleWorlds.Localization.dll"
  [Core]="TaleWorlds.Core.dll"
  [ViewModelCollection]="TaleWorlds.CampaignSystem.ViewModelCollection.dll"
  [SaveSystem]="TaleWorlds.SaveSystem.dll"
  [SandBox]="SandBox.dll"
)

# 类型清单（DLL别名|类型全名）—— 新增类型加这里
declare -A TYPES=(
  [MobileParty]="CampaignSystem|TaleWorlds.CampaignSystem.Party.MobileParty"
  [MobilePartyAi]="CampaignSystem|TaleWorlds.CampaignSystem.Party.MobilePartyAi"
  [Settlement]="CampaignSystem|TaleWorlds.CampaignSystem.Settlements.Settlement"
  [Kingdom]="CampaignSystem|TaleWorlds.CampaignSystem.Kingdom"
  [Clan]="CampaignSystem|TaleWorlds.CampaignSystem.Clan"
  [Campaign]="CampaignSystem|TaleWorlds.CampaignSystem.Campaign"
  [CampaignTimeModel]="CampaignSystem|TaleWorlds.CampaignSystem.ComponentInterfaces.CampaignTimeModel"
  [ChangeKingdomAction]="CampaignSystem|TaleWorlds.CampaignSystem.Actions.ChangeKingdomAction"
  [DestroyPartyAction]="CampaignSystem|TaleWorlds.CampaignSystem.Actions.DestroyPartyAction"
  [MapState]="CampaignSystem|TaleWorlds.CampaignSystem.GameState.MapState"
  [IMapStateHandler]="CampaignSystem|TaleWorlds.CampaignSystem.GameState.IMapStateHandler"
  [IMapScene]="CampaignSystem|TaleWorlds.CampaignSystem.Map.IMapScene"
  [PartyComponent]="CampaignSystem|TaleWorlds.CampaignSystem.Party.PartyComponents.PartyComponent"
  [GameMenu]="CampaignSystem|TaleWorlds.CampaignSystem.GameMenus.GameMenu"
  [QuestBase]="CampaignSystem|TaleWorlds.CampaignSystem.QuestBase"
  [SetPartyAiAction]="CampaignSystem|TaleWorlds.CampaignSystem.Actions.SetPartyAiAction"
  [IssueBase]="CampaignSystem|TaleWorlds.CampaignSystem.Issues.IssueBase"
  [FactionManager]="CampaignSystem|TaleWorlds.CampaignSystem.FactionManager"
  [MobilePartyHelper]="CampaignSystem|TaleWorlds.CampaignSystem.Party.MobilePartyHelper"
  [VillageMarketData]="CampaignSystem|TaleWorlds.CampaignSystem.Settlements.VillageMarketData"
  [Agent]="MountAndBlade|TaleWorlds.MountAndBlade.Agent"
  [Mission]="MountAndBlade|TaleWorlds.MountAndBlade.Mission"
  [MissionBehavior]="MountAndBlade|TaleWorlds.MountAndBlade.MissionBehavior"
  [InventoryManager]="CampaignSystem|TaleWorlds.CampaignSystem.Inventory.InventoryManager"
  [MissionObject]="MountAndBlade|TaleWorlds.MountAndBlade.MissionObject"
  [Scene]="Engine|TaleWorlds.Engine.Scene"
  [ScriptComponentBehavior]="Engine|TaleWorlds.Engine.ScriptComponentBehavior"
  [GauntletLayer]="GauntletUI|TaleWorlds.Engine.GauntletUI.GauntletLayer"
  [TextObject]="Localization|TaleWorlds.Localization.TextObject"
  [AgentControllerType]="Core|TaleWorlds.Core.AgentControllerType"
  [QuestsVM]="ViewModelCollection|TaleWorlds.CampaignSystem.ViewModelCollection.Quests.QuestsVM"
  [VariableSaveData]="SaveSystem|TaleWorlds.SaveSystem.Save.VariableSaveData"
  [AgentNavigator]="SandBox|SandBox.AgentNavigator"
  [MissionConversationLogic]="SandBox|SandBox.Conversation.MissionLogics.MissionConversationLogic"
  [ConversationMissionLogic]="SandBox|SandBox.Conversation.MissionLogics.ConversationMissionLogic"
  [DisguiseMissionLogic]="SandBox|SandBox.Missions.MissionLogics.DisguiseMissionLogic"
  [StealthFailCounterMissionLogic]="SandBox|SandBox.Missions.StealthFailCounterMissionLogic"
  [ConversationManager]="CampaignSystem|TaleWorlds.CampaignSystem.Conversation.ConversationManager"
  [ConversationSentence]="CampaignSystem|TaleWorlds.CampaignSystem.Conversation.ConversationSentence"
  [Hero]="CampaignSystem|TaleWorlds.CampaignSystem.Hero"
)

# ⚠️ ilspycmd -t 一次只能一个类型（多传整体失败，输出 "Specify --help"）
ok=0; miss=0
for key in "${!TYPES[@]}"; do
  IFS='|' read -r dll type <<< "${TYPES[$key]}"
  dllfile="${DL[$dll]}"
  SRC_FILE="$SRC/$dllfile"
  # SandBox 类型走 sandbox 源
  [ "$dll" = "SandBox" ] && SRC_FILE="$SB_SRC/$dllfile"
  if [ ! -f "$SRC_FILE" ]; then
    echo "❌ SKIP  $key (DLL 缺失: $SRC_FILE)"
    miss=$((miss+1)); continue
  fi
  ilspycmd "$SRC_FILE" -t "$type" > "$DEST/$key.cs" 2>/dev/null
  if [ -s "$DEST/$key.cs" ]; then ok=$((ok+1)); else echo "❌ FAIL $key ($type)"; miss=$((miss+1)); fi
done
echo "═══ 完成: $ok 个类型已刷新, $miss 个失败/缺失 ═══"
