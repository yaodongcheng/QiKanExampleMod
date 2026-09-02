using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 无文化角色的「生成期补全」（通用，2026-09-02 用户裁定：Code 方案替代织丰专用 XSLT 数据补丁）。
    ///
    /// 背景：实机发现织丰 spnpccharacters.xml 有 24/85 个「镇民/乞丐/匠人」模板漏写 culture 属性
    ///   （如 beggar_saikai — 而 caravan_master_saikai 等都写了 culture="Culture.saikai"）
    ///   → XML 加载期按 culture="id" 解析不到 → CharacterObject.Culture == null
    ///   → 伤害模型裸解引用 .Culture.IsBandit 时 NRE（见 [[damage-model-culture-null-fix]]）。
    ///
    /// 为什么补在这里：缺 culture 的模板全部是 Townsfolk/Villager 职业——只在**村庄/城镇场景**里
    ///   作为 idle 路人生成（兵种/商队/英雄模板都有 culture 无需补）——生成时刻的
    ///   Settlement.CurrentSettlement 必非空——文化的依据 = 当前定居点文化，零编造、零猜。
    ///   模板是全局共享对象（非 per-settlement 实例），但这类模板只在单一文化区使用，无冲突。
    ///
    /// 实现与性能/刷屏：事件驱动（MissionLogic.OnAgentCreated）——只检查「新创建」的单个 agent，
    ///   无每帧循环（村庄几百人时零成本）；日志按模板 StringId 去重（_handledTemplates 跨场次静态），
    ///   24 个漏写模板全生命周期最多 24 条日志。英雄跳过（HeroObject.Culture 独立于模板）。
    ///
    /// 防御：写失败/反射异常只记日志绝不抛出；与 AgentDamageModelCultureNullFix（伤害模型兜底）
    ///   是双层战线——本补全根治、兜底防漏网。
    /// </summary>
    public class CharacterCultureBackfill : MissionLogic
    {
        /// <summary>已补过/已记录过的模板 StringId（跨场次去重）</summary>
        private static readonly HashSet<string> _handledTemplates = new HashSet<string>();

        /// <summary>跨版本反射：Culture 属性各版本挂基类/派生类不同，取基类写《_culture》字段</summary>
        private static readonly PropertyInfo BaseCultureProperty =
            typeof(BasicCharacterObject).GetProperty("Culture", BindingFlags.Public | BindingFlags.Instance);

        public override void OnAgentCreated(Agent agent)
        {
            base.OnAgentCreated(agent);
            try
            {
                // idle 路人在 settlement 场景生成；无 settlement 场景（野战/攻城）无需本补全
                Settlement settlement = Settlement.CurrentSettlement;
                CultureObject settlementCulture = settlement?.Culture;
                if (settlementCulture == null)
                {
                    return;
                }

                if (!(agent?.Character is CharacterObject character))
                {
                    return;
                }

                // 英雄文化的来源是 HeroObject，与模板无关，跳过
                if (character.HeroObject != null)
                {
                    return;
                }

                if (character.Culture != null)
                {
                    return;
                }

                Backfill(character, settlementCulture, settlement);
            }
            catch (Exception ex)
            {
                try { DebugLogger.Log($"[CharacterCultureBackfill] 巡检异常（忽略该 agent）: {ex}"); } catch { }
            }
        }

        private static void Backfill(CharacterObject character, CultureObject culture, Settlement settlement)
        {
            string key = character.StringId ?? "?";
            try
            {
                if (!_handledTemplates.Add(key))
                {
                    return; // 已补过 -> 之后的 agent 在 Culture!=null 检查处就已跳过，不会重入这里
                }

                BaseCultureProperty?.SetValue(character, culture, null);
                DebugLogger.Log($"[CharacterCultureBackfill] 模板 {key}（{character.Name}）缺少 Culture，" +
                                $"已按生成地点「{settlement.Name}」的文化「{culture.Name}」补全。");
            }
            catch (Exception ex)
            {
                _handledTemplates.Remove(key); // 写失败 -> 下次允许重试
                try { DebugLogger.Log($"[CharacterCultureBackfill] 模板 {key} 补全失败: {ex.Message}"); } catch { }
            }
        }
    }
}
