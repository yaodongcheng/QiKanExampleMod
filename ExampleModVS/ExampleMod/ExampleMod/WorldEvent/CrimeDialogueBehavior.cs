using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 控制台测试指令：模拟偷动物 → 观察整个系统运行
    /// custom.crime_test — 在玩家当前村庄创建一个测试偷窃事件
    /// custom.crime_inject — 手动触发犯罪对话注入（调试用）
    /// </summary>
    public static class CrimeConsoleCommands
    {
        public static void CrimeTest()
        {
            try
            {
                var settlement = Settlement.CurrentSettlement ?? Hero.MainHero.CurrentSettlement;
                if (settlement == null || !settlement.IsVillage)
                {
                    InformationManager.DisplayMessage(
                        new TaleWorlds.Library.InformationMessage("请先进入一个村庄再测试。"));
                    return;
                }

                // 两轮查找：先精确 ID，再兜底遍历
                ItemObject sheep = MBObjectManager.Instance.GetObject<ItemObject>("sheep");
                if (sheep == null)
                {
                    sheep = MBObjectManager.Instance.GetObject<ItemObject>(
                        item => item.ItemType == ItemObject.ItemTypeEnum.Animal);
                }

                string itemId = sheep?.StringId ?? "sheep";

                var evt = new WorldEvent
                {
                    EventId = $"test_{settlement.StringId}_{(float)CampaignTime.Now.ToDays}",
                    Category = EventCategory.Crime,
                    Type = EventType.Theft_Animal,
                    Severity = 30,
                    InitiatorId = Hero.MainHero.StringId,
                    TargetSettlementId = settlement.StringId,
                    StolenItems = new Dictionary<string, int> { { itemId, 3 } },
                    OccurredDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = 14f,
                    LocationName = settlement.Name?.ToString() ?? "村庄",
                    Stage = EventStage.Emerging,
                    PublicAwareness = 0.2f,
                    InvestigationProgress = 0.3f,
                };
                WorldEventStore.AddOrMerge(evt);

                InformationManager.DisplayMessage(
                    new TaleWorlds.Library.InformationMessage(
                        $"[犯罪测试] 在 {settlement.Name} 创建了偷窃事件: {evt.EventId}。\n" +
                        $"找任意村民对话即可看到效果。阶段={evt.Stage} 认知度={evt.PublicAwareness}"));
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(
                    new TaleWorlds.Library.InformationMessage($"[犯罪测试] 失败: {ex.Message}"));
            }
        }

        public static void CrimeInject()
        {
            try
            {
                var settlement = Settlement.CurrentSettlement ?? Hero.MainHero.CurrentSettlement;
                if (settlement == null) return;

                var evt = WorldEventStore.FindActive(settlement.StringId);
                if (evt == null)
                {
                    InformationManager.DisplayMessage(
                        new TaleWorlds.Library.InformationMessage("当前定居点没有活跃犯罪事件。先用 custom.crime_test 创建。"));
                    return;
                }

                // 找权威 NPC
                var authority = WorldEventStore.GetAuthorityNpc(evt);
                if (authority == null)
                {
                    InformationManager.DisplayMessage(
                        new TaleWorlds.Library.InformationMessage("找不到权威 NPC（村长/族长/领主）。"));
                    return;
                }

                // 清除旧注入
                DialogueInjector.RemoveRelatedLines($"crime_{evt.EventId}");

                // 构建并注入
                var script = CrimeDialogueBuilder.BuildScript(authority, Hero.MainHero);
                if (script != null && script.Turns != null && script.Turns.Count > 0)
                {
                    DialogueInjector.InjectScript(script, $"crime_{evt.EventId}");
                    InformationManager.DisplayMessage(
                        new TaleWorlds.Library.InformationMessage(
                            $"犯罪对话已注入：event={evt.EventId} stage={evt.Stage} speaker={authority.Name} turns={script.Turns.Count}。\n找 {authority.Name} 对话即可。"));
                }
                else
                {
                    InformationManager.DisplayMessage(
                        new TaleWorlds.Library.InformationMessage("CrimeDialogueBuilder.BuildScript 返回了 null/空脚本。"));
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(
                    new TaleWorlds.Library.InformationMessage($"[犯罪注入] 失败: {ex.Message}"));
            }
        }
    }
}
