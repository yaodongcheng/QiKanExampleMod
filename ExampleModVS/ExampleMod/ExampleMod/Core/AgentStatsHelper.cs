using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;   // DefaultCharacterAttributes（TaleWorlds.Core.dll）
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Agent 属性统一读取（玩家/NPC 共享，2026-08-13 平权重构）：
    /// Hero 有属性记录 → 直接读 Vigor/Control；模板 NPC 无 Hero → 按 Level 均分估算（(3+Level/3)/2）。
    /// 重构前玩家路径（InteractionMissionView.GetAgentStats）与 NPC 击晕内联各写一份（"同口径"注释，
    /// 2026-08-13 改公式时两侧各改一遍）——现在只此一处，改公式只改这里。
    /// </summary>
    public static class AgentStatsHelper
    {
        /// <summary>Agent 的 Vigor+Control 合计（骑砍版"力量+敏捷"；模板 NPC 估算同口径）。</summary>
        public static int GetAgentStatTotal(Agent agent)
        {
            var (v, c) = GetAgentStats(agent);
            return v + c;
        }

        /// <summary>Agent 的 Vigor 和 Control 各自值。Hero → 直接读属性；模板 NPC → 按 Level 均分估算。</summary>
        public static (int vigor, int control) GetAgentStats(Agent agent)
        {
            if (agent == null) return (5, 5);

            var character = agent.Character as CharacterObject;
            var hero = character?.HeroObject;

            if (hero != null)
            {
                return (hero.GetAttributeValue(DefaultCharacterAttributes.Vigor),
                        hero.GetAttributeValue(DefaultCharacterAttributes.Control));
            }

            if (character != null)
            {
                // 模板 NPC 无 Hero → 按 Level 均分估算（农民 ≈4+4、女农民更低）
                int half = (3 + character.Level / 3) / 2;
                return (half, half);
            }

            return (5, 5);
        }
    }
}
