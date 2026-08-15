using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;   // DefaultCharacterAttributes（TaleWorlds.Core.dll）
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>武装档位（2026-08-14，npc-risk-aware-planning.md M7）：属性 ≠ 战力——
    /// 守卫全副武装（重甲+长矛）与空手农民属性相近但实际战力天差地别。读 SpawnEquipment：
    /// 武器槽伤害 + 头/身/腿/手/披风护甲值 → 档位。战力段分列属性与武装，
    /// LLM 才能看出"悬殊来自装备"并推理出"先卸其甲兵"。</summary>
    public enum ArmorProfileTier { Unarmed, Light, Armed, Full }
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
        /// <summary>武装档位判定（读 SpawnEquipment：武器槽伤害 + 护甲值 → 四档）。</summary>
        public static ArmorProfileTier GetArmorProfile(Agent agent)
        {
            if (agent == null || agent.SpawnEquipment == null) return ArmorProfileTier.Unarmed;
            // 武器伤害合计（削攻最直观；SwingDamage 优先，投掷/箭走 ThrustDamage）
            float weaponDamage = 0f;
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
            {
                try
                {
                    var el = agent.SpawnEquipment[i];
                    if (el.IsEmpty || el.Item == null) continue;
                    var pw = el.Item.PrimaryWeapon;
                    if (pw != null)
                        weaponDamage += pw.SwingDamage > 0 ? pw.SwingDamage : pw.ThrustDamage;
                }
                catch { }
            }
            // 护甲合计（头/身/腿/手/披风——反编译确认 ArmorComponent 无 CapeArmor，披风并入 BodyArmor）
            float armor = 0f;
            var armorSlots = new[] { EquipmentIndex.Head, EquipmentIndex.Body, EquipmentIndex.Leg, EquipmentIndex.Gloves, EquipmentIndex.Cape };
            foreach (var s in armorSlots)
            {
                try
                {
                    var el = agent.SpawnEquipment[s];
                    if (el.IsEmpty || el.Item == null) continue;
                    var ac = el.Item.ArmorComponent;
                    if (ac == null) continue;
                    armor += s == EquipmentIndex.Head ? ac.HeadArmor
                        : s == EquipmentIndex.Body || s == EquipmentIndex.Cape ? ac.BodyArmor
                        : s == EquipmentIndex.Leg ? ac.LegArmor
                        : ac.ArmArmor;
                }
                catch { }
            }
            bool hasWeapon = weaponDamage > 0f;
            if (!hasWeapon) return ArmorProfileTier.Unarmed;
            if (armor >= 50f) return ArmorProfileTier.Full;      // 重甲全套（守卫/重装兵）
            if (armor > 0f) return ArmorProfileTier.Armed;       // 有甲（佣兵/轻装兵）
            return ArmorProfileTier.Light;                       // 只有武器没护甲
        }
        /// <summary>武装档位本地化词（调用方拼 prompt/播报用；铁律 13 走 LWN_armor_tier_*）。</summary>
        public static string ArmorProfileWord(ArmorProfileTier tier)
        {
            switch (tier)
            {
                // 本地化：全副武装（重甲）
                case ArmorProfileTier.Full: return LWNTextHelper.ResolveText("LWN_armor_tier_full", "fully armed (heavy armor)");
                // 本地化：武装（轻甲）
                case ArmorProfileTier.Armed: return LWNTextHelper.ResolveText("LWN_armor_tier_armed", "armed (light armor)");
                // 本地化：轻装（有武器无甲）
                case ArmorProfileTier.Light: return LWNTextHelper.ResolveText("LWN_armor_tier_light", "unarmored with a weapon");
                // 本地化：徒手
                default: return LWNTextHelper.ResolveText("LWN_armor_tier_unarmed", "bare-handed");
            }
        }
    }
}
