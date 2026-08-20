using HarmonyLib;
using Helpers;
using Microsoft.SqlServer.Server;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SandBox.Missions.MissionLogics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using static TaleWorlds.CampaignSystem.CampaignOptions;

namespace LivingWorldNpcs
{

    public static class SkillCheckSystem
    {
        public struct CheckResult
        {
            public bool IsSuccess;
            public string Log;          // "玩家魅力 50 vs NPC意志 40 => 成功"
            public float WinChance;     // 胜率，用于显示给玩家看 (可选)
        }

        /// <summary>
        /// 唯一权威的「手段 → 主导技能」映射。SkillCheckOption 与单次检定 resolver 共用，
        /// 避免两处各写一套 switch（见 plans 重复点 1）。
        /// </summary>
        public static TaleWorlds.Core.SkillObject MapTacticToSkill(NegotiationTactic tactic)
        {
            switch (tactic)
            {
                case NegotiationTactic.Threaten:
                case NegotiationTactic.Coerce:
                    return DefaultSkills.Roguery;       // 恐吓/施压 → 流氓习气
                case NegotiationTactic.Reason:
                    return DefaultSkills.Tactics;       // 说理 → 战术/逻辑
                case NegotiationTactic.Swear:
                    return DefaultSkills.Leadership;    // 立誓 → 统御
                case NegotiationTactic.Flatter:
                case NegotiationTactic.Plead:
                case NegotiationTactic.Bribe:
                case NegotiationTactic.Gift:
                case NegotiationTactic.Submisson:
                case NegotiationTactic.OfferPower:
                default:
                    return DefaultSkills.Charm;         // 社交类 → 魅力
            }
        }

        public static CheckResult CalculateSkillCheck(Hero player, Hero npc, NegotiationTactic tactic)
        {
            float playerScore = 0f;
            float difficulty = 0f;
            string attrName = "";

            // 1. 简单的属性映射逻辑
            switch (tactic)
            {
                case NegotiationTactic.Threaten: // 武力/恶名
                    attrName = "武力(Roguary/Athletics)";
                    playerScore = player.GetSkillValue(DefaultSkills.Roguery) + player.GetSkillValue(DefaultSkills.Athletics) * 0.5f;
                    // 对抗 NPC 的胆量 (勇气越高越难吓唬)
                    difficulty = npc.GetTraitLevel(DefaultTraits.Valor) * 50f + 30f;
                    break;

                case NegotiationTactic.Reason: // 智力/战术
                    attrName = "智力(Tactics/Engineering)";
                    playerScore = player.GetSkillValue(DefaultSkills.Tactics) + player.GetSkillValue(DefaultSkills.Engineering) * 0.5f;
                    // 对抗 NPC 的智商 (计算能力)
                    difficulty = npc.GetTraitLevel(DefaultTraits.Calculating) * 50f + 30f;
                    break;

                case NegotiationTactic.Flatter: // 魅力/交易
                case NegotiationTactic.Plead:
                default:
                    attrName = "魅力(Charm)";
                    playerScore = player.GetSkillValue(DefaultSkills.Charm);
                    // 对抗 NPC 的铁石心肠 (仁慈越高越容易)
                    difficulty = 100f - (npc.GetTraitLevel(DefaultTraits.Mercy) * 30f);
                    break;
            }

            // 2. 引入随机波动 (d20 规则变体)
            float randomFactor = MBRandom.RandomFloat * 40f; // 0-40 的运气值
            float finalScore = playerScore + randomFactor;

            // 3. 判定
            bool success = finalScore >= difficulty;

            return new CheckResult
            {
                IsSuccess = success,
                Log = $"[{attrName}检定] 胜率{(100*playerScore / (difficulty + playerScore)):F0}%  你的点数 {playerScore:F0} + 运气 {randomFactor:F0} = {finalScore:F0} vs 难度 {difficulty:F0}",
                WinChance = playerScore / (difficulty+ playerScore)
            };
        }
    }

    // [新增] NPC的主动意图类型
    public enum InitiativeType
    {
        NewsConflict,       // 新闻/传闻冲突 (原 PendingConflict)
        GuardIntercept,     // 守卫拦截 (没交钱、通缉、携带违禁品)
        CrimeAccusation,    //犯罪指控
        Revenge,            // 寻仇
        Greeting,           // 熟人主动打招呼
        OfficialBusiness,   // 公务 (比如税务官、传令兵)
        Crush               // 爱慕者搭讪
    }
    [Obsolete("Use IntentBase subclasses + IntentRegistry instead. NpcInitiativeIntents.cs provides equivalent NPC intent classes.")]
    public class NpcInitiative
    {
        public InitiativeType Type { get; set; }
        public string ContextDescription { get; set; } // "玩家因偷窃被守卫拦截"
        public PendingConflict ConflictData { get; private set; }
        public bool IsSkillCheckPassed { get; set; }
        public string SkillCheckResult { get; set; }
        public LLMResponse_Opening CachedOpening { get; set; }

        public string JsonResponseOpening = "{}";
        public NpcInitiative(InitiativeType type, string desc, string skillCheckInfo = "", bool isPassed = false)
        {
            Type = type;
            ContextDescription = desc;
            SkillCheckResult = skillCheckInfo;
            IsSkillCheckPassed = isPassed;

        }
        // 判断是否已经准备好（已预计算）
        public bool IsReady => CachedOpening != null;

        public NpcInitiative(InitiativeType type,string Desc)
        {
            Type = type;
            ContextDescription = Desc;
        }
        // 构造函数 2：冲突模式 (传入详细的 Conflict 对象)
        public NpcInitiative(InitiativeType type, PendingConflict conflict)
        {
            Type = type;
            ConflictData = conflict;
            // 自动从 Conflict 生成描述，或者你可以拼接
            ContextDescription = $"{conflict.TopicName} - {conflict.GoalDescription}";
        }

    }








    public class PendingConflict
    {
        public string EventId { get; set; }
        public string TopicName { get; set; } // 标题: "关于阿市被骚扰的事"
        public string GoalDescription { get; set; } // 描述: "说服浅井长政原谅你的行为"
        public float Severity { get; set; } // 严重程度 (0-100)，用于计算谈判难度
        public NegotiationGoalType GoalType { get; set; } // 对应的谈判类型

        public PendingConflict(string eventId, string topicName, string goalDesc, float severity, NegotiationGoalType type)
        {
            EventId = eventId;
            TopicName = topicName;
            GoalDescription = goalDesc;
            Severity = severity;
            GoalType = type;
        }
    }
    public enum TraitPolarity
    {
        Neutral,    // 中性/描述性
        Weakness,   // 弱点 (对玩家有利，绿色)
        Resistance, // 阻力 (对玩家不利，红色)
        Immunity    // 免疫 (完全无效，灰色)
    }
    public class NegotiationTrait
    {
        public string ID { get; set; }           // 唯一标识
        public string Name { get; set; }         // UI显示名称
        public string Description { get; set; }  // 悬停描述
        public bool IsSecret { get; set; }       // 是否一开始隐藏
        public string SourceField { get; set; }  // 来源回溯，例如 "Trait.Honor"
        public TraitPolarity Polarity { get; set; } // 总体倾向（用于UI显示颜色，红/绿/灰）

        // ========================================================================
        // 核心修改器：三个维度的修正
        // ========================================================================

        // 1. 手段修正 (Tactic Modifiers)
        // 针对 "怎么说" (威胁、利诱、讲理、乞求...)
        // Value > 1.0: 效果拔群 | Value < 1.0: 效果减弱 | Value = 0: 无效/激怒
        public Dictionary<NegotiationTactic, float> TacticModifiers { get; set; } = new Dictionary<NegotiationTactic, float>();

        // 2. 筹码修正 (Cost/Resource Modifiers)
        // 针对 "给什么" (钱、物品、承诺、艺术品...)
        // Value > 1.0: 这种东西他很喜欢，能换更多进度 | Value < 1.0: 他不感兴趣
        public Dictionary<NegotiationCostType, float> CostModifiers { get; set; } = new Dictionary<NegotiationCostType, float>();

        // 3. 目标修正 (Goal Modifiers)
        // 针对 "想干嘛" (求婚、招募、停战...)
        // Value > 0: 增加阻力/难度 (Resistance) | Value < 0: 减少阻力 (Synergy)
        public Dictionary<NegotiationGoalType, float> GoalResistanceModifiers { get; set; } = new Dictionary<NegotiationGoalType, float>();

        // 构造函数
        public NegotiationTrait(string id, string name, TraitPolarity polarity)
        {
            ID = id;
            Name = name;
            Polarity = polarity;
        }

        // ========================================================================
        // 辅助方法：安全获取数值
        // ========================================================================

        public float GetTacticMultiplier(NegotiationTactic tactic)
        {
            return TacticModifiers.ContainsKey(tactic) ? TacticModifiers[tactic] : 1.0f;
        }

        public float GetCostMultiplier(NegotiationCostType costType)
        {
            return CostModifiers.ContainsKey(costType) ? CostModifiers[costType] : 1.0f;
        }

        public float GetGoalResistance(NegotiationGoalType goalType)
        {
            return GoalResistanceModifiers.ContainsKey(goalType) ? GoalResistanceModifiers[goalType] : 0f;
        }
    }

    public static class NegotiationTraitFactory
    {
        /// <summary>
        /// 根据 NPC 档案和当前谈判目标，生成 1-4 个关键特征
        /// </summary>
        public static List<NegotiationTrait> GenerateTraits(NPCProfile profile, NegotiationGoalType goalType)
        {
            List<NegotiationTrait> potentialTraits = new List<NegotiationTrait>();

            Hero hero = profile.BaseHero;


            if (hero.IsFactionLeader && Hero.MainHero.Clan.Kingdom == hero.Clan.Kingdom)
            {
                // 如果对方是君主，且玩家是臣下
                // 本地化：LWN_nego_trait_socialhierarchy_master_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Trait_SocialHierarchy_Master", LWNTextHelper.ResolvePrompt("LWN_nego_trait_socialhierarchy_master_name"), TraitPolarity.Immunity);
                // 本地化：LWN_nego_trait_socialhierarchy_master_desc（谈判特质描述，双桶）
                t.Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_socialhierarchy_master_desc");
                t.GoalResistanceModifiers[NegotiationGoalType.RecruitHero] = 0.0f;
                potentialTraits.Add(t);
            }



            // ==========================================
            // 1. 欲望维度 (DesireEnum)
            // ==========================================
            if (profile.Desire == NPCProfile.DesireEnum.Greedy)
            {
                // 本地化：LWN_nego_trait_greedy_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Greedy", LWNTextHelper.ResolvePrompt("LWN_nego_trait_greedy_name"), TraitPolarity.Weakness)
                {
                    // 本地化：LWN_nego_trait_greedy_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_greedy_desc"),
                    SourceField = "DesireEnum.Greedy"
                };
                // 手段修正：吃软不吃硬，尤其吃贿赂
                t.TacticModifiers[NegotiationTactic.Bribe] = 2.0f; // 效果翻倍
                t.TacticModifiers[NegotiationTactic.Plead] = 0.5f; // 没钱别求我

                // 筹码修正：极度喜欢金钱和物品
                t.CostModifiers[NegotiationCostType.PersonalGold] = 1.5f; // 给钱效果好
                t.CostModifiers[NegotiationCostType.Item] = 1.2f;

                // 目标修正：如果是谈生意，难度降低
                t.GoalResistanceModifiers[NegotiationGoalType.TradeAgreement] = -20f;
                potentialTraits.Add(t);
            }
            else if (profile.Desire == NPCProfile.DesireEnum.DesireLess)
            {
                // 本地化：LWN_nego_trait_ascetic_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Ascetic", LWNTextHelper.ResolvePrompt("LWN_nego_trait_ascetic_name"), TraitPolarity.Resistance)
                {
                    // 本地化：LWN_nego_trait_ascetic_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_ascetic_desc"),
                    SourceField = "DesireEnum.DesireLess"
                };
                t.TacticModifiers[NegotiationTactic.Bribe] = 0.1f; // 几乎无效
                t.CostModifiers[NegotiationCostType.PersonalGold] = 0.0f;
                potentialTraits.Add(t);
            }
            // ==========================================
            // 2. 勇气维度 (SpiritEnum)
            // ==========================================
            if (profile.Spirit == NPCProfile.SpiritEnum.Timid)
            {
                // 本地化：LWN_nego_trait_coward_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Coward", LWNTextHelper.ResolvePrompt("LWN_nego_trait_coward_name"), TraitPolarity.Weakness)
                {
                    // 本地化：LWN_nego_trait_coward_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_coward_desc"),
                    SourceField = "SpiritEnum.Timid"
                };
                t.TacticModifiers[NegotiationTactic.Threaten] = 2.0f; // 威胁暴击
                t.CostModifiers[NegotiationCostType.Notoriety] = 1.5f; // 恶名对他来说很有威慑力
                potentialTraits.Add(t);
            }
            else if (profile.Spirit == NPCProfile.SpiritEnum.Brave)
            {
                // 本地化：LWN_nego_trait_fearless_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Fearless", LWNTextHelper.ResolvePrompt("LWN_nego_trait_fearless_name"), TraitPolarity.Resistance)
                {
                    // 本地化：LWN_nego_trait_fearless_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_fearless_desc"),
                    SourceField = "SpiritEnum.Brave"
                };
                t.TacticModifiers[NegotiationTactic.Threaten] = 0.0f; // 威胁完全无效甚至反噬
                t.TacticModifiers[NegotiationTactic.Coerce] = 0.5f;

                // 目标修正：很难劝降
                t.GoalResistanceModifiers[NegotiationGoalType.DefectFaction] = 50f;
                potentialTraits.Add(t);
            }
            if (profile.Ism == NPCProfile.IsmEnum.Ideal)
            {
                // 本地化：LWN_nego_trait_idealist_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Idealist", LWNTextHelper.ResolvePrompt("LWN_nego_trait_idealist_name"), TraitPolarity.Neutral)
                {
                    // 本地化：LWN_nego_trait_idealist_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_idealist_desc"),
                    SourceField = "IsmEnum.Ideal"
                };
                // 喜欢誓言，讨厌贿赂
                t.TacticModifiers[NegotiationTactic.Swear] = 1.5f;
                t.TacticModifiers[NegotiationTactic.Bribe] = 0.5f;

                // 喜欢荣誉类筹码
                t.CostModifiers[NegotiationCostType.Reputation] = 1.5f;
                potentialTraits.Add(t);
            }
            else if (profile.Ism == NPCProfile.IsmEnum.Realistic)
            {
                // 本地化：LWN_nego_trait_realist_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Realist", LWNTextHelper.ResolvePrompt("LWN_nego_trait_realist_name"), TraitPolarity.Neutral)
                {
                    // 本地化：LWN_nego_trait_realist_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_realist_desc"),
                    SourceField = "IsmEnum.Realistic"
                };
                // 讨厌空话
                t.TacticModifiers[NegotiationTactic.Swear] = 0.5f;
                t.TacticModifiers[NegotiationTactic.Plead] = 0.5f;

                // 喜欢实际权力
                t.TacticModifiers[NegotiationTactic.OfferPower] = 1.3f;
                t.CostModifiers[NegotiationCostType.SettlementOwnership] = 1.5f;
                potentialTraits.Add(t);
            }
            // ==========================================
            // 4. 友情重视程度 (FriendshipImportanceEnum)
            // ==========================================
            if (profile.theImportanceOfFriendship == NPCProfile.FriendshipImportanceEnum.Important)
            {
                // 本地化：LWN_nego_trait_loyalfriend_name（谈判特质名，双桶）
                var t = new NegotiationTrait("LoyalFriend", LWNTextHelper.ResolvePrompt("LWN_nego_trait_loyalfriend_name"), TraitPolarity.Weakness)
                {
                    // 本地化：LWN_nego_trait_loyalfriend_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_loyalfriend_desc"),
                    SourceField = "FriendshipImportance.Important"
                };
                t.TacticModifiers[NegotiationTactic.Plead] = 1.5f; // 恳求有效
                t.CostModifiers[NegotiationCostType.SocialRelation] = 1.5f; // 消耗人情效果拔群

                // 结拜容易
                t.GoalResistanceModifiers[NegotiationGoalType.MakeFriend] = -30f;
                potentialTraits.Add(t);
            }
            else if (profile.theImportanceOfFriendship == NPCProfile.FriendshipImportanceEnum.NotImportant)
            {
                // 本地化：LWN_nego_trait_coldblooded_name（谈判特质名，双桶）
                var t = new NegotiationTrait("ColdBlooded", LWNTextHelper.ResolvePrompt("LWN_nego_trait_coldblooded_name"), TraitPolarity.Resistance)
                {
                    // 本地化：LWN_nego_trait_coldblooded_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_coldblooded_desc"),
                    SourceField = "FriendshipImportance.NotImportant"
                };
                t.TacticModifiers[NegotiationTactic.Plead] = 0.2f;
                t.CostModifiers[NegotiationCostType.SocialRelation] = 0.1f;
                potentialTraits.Add(t);
            }
            // ==========================================
            // 5. 行事风格 (ActStyleEnum)
            // ==========================================
            if (profile.ActStyle == NPCProfile.ActStyleEnum.Flippancy)
            {
                // 本地化：LWN_nego_trait_flippant_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Flippant", LWNTextHelper.ResolvePrompt("LWN_nego_trait_flippant_name"), TraitPolarity.Neutral)
                {
                    // 本地化：LWN_nego_trait_flippant_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_flippant_desc"),
                    SourceField = "ActStyle.Flippancy"
                };
                t.TacticModifiers[NegotiationTactic.Flatter] = 1.5f; // 吃恭维
                t.TacticModifiers[NegotiationTactic.Swear] = 0.6f;   // 讨厌严肃誓言
                potentialTraits.Add(t);
            }
            else if (profile.ActStyle == NPCProfile.ActStyleEnum.Considerate)
            {
                // 本地化：LWN_nego_trait_cautious_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Cautious", LWNTextHelper.ResolvePrompt("LWN_nego_trait_cautious_name"), TraitPolarity.Resistance)
                {
                    // 本地化：LWN_nego_trait_cautious_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_cautious_desc"),
                    SourceField = "ActStyle.Considerate"
                };
                t.TacticModifiers[NegotiationTactic.Flatter] = 0.5f; // 恭维被看穿
                t.TacticModifiers[NegotiationTactic.OfferPower] = 1.2f; // 只有具体的权力规划能打动
                potentialTraits.Add(t);
            }


            // ==========================================
            // 6. 嗜酒 (AlcoholDesireEnum)
            // ==========================================
            if (profile.AlcoholDesire == NPCProfile.AlcoholDesireEnum.Alcoholic)
            {
                // 本地化：LWN_nego_trait_drunkard_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Drunkard", LWNTextHelper.ResolvePrompt("LWN_nego_trait_drunkard_name"), TraitPolarity.Weakness)
                {
                    // 本地化：LWN_nego_trait_drunkard_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_drunkard_desc"),
                    SourceField = "AlcoholDesire.Alcoholic"
                };
                // 并没有单独的“送酒”Tactic，但归类为 Gift
                t.TacticModifiers[NegotiationTactic.Gift] = 1.5f;
                // 假设物品中包含酒，整体 Item 价值提升
                t.CostModifiers[NegotiationCostType.Item] = 1.3f;
                potentialTraits.Add(t);
            }
            // ==========================================
            // 7. 关系维度 (Relation & BaseHero)
            // ==========================================
            if (profile.BaseHero != null)
            {
                int relation = profile.BaseHero.GetRelation(Hero.MainHero);

                if (relation <= -20)
                {
                    // 本地化：LWN_nego_trait_hostile_name（谈判特质名，双桶）
                    var t = new NegotiationTrait("Hostile", LWNTextHelper.ResolvePrompt("LWN_nego_trait_hostile_name"), TraitPolarity.Resistance)
                    {
                        // 本地化：LWN_nego_trait_hostile_desc（谈判特质描述，双桶）
                        Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_hostile_desc"),
                        SourceField = "Relation.Low"
                    };
                    // 全面抗性
                    t.TacticModifiers[NegotiationTactic.Plead] = 0.0f;
                    t.TacticModifiers[NegotiationTactic.Flatter] = 0.0f;
                    t.GoalResistanceModifiers[goalType] = 50f; // 所有目标难度+50
                    potentialTraits.Add(t);
                }
                else if (relation >= 20)
                {
                    // 本地化：LWN_nego_trait_friend_name（谈判特质名，双桶）
                    var t = new NegotiationTrait("Friend", LWNTextHelper.ResolvePrompt("LWN_nego_trait_friend_name"), TraitPolarity.Weakness)
                    {
                        // 本地化：LWN_nego_trait_friend_desc（谈判特质描述，双桶）
                        Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_friend_desc"),
                        SourceField = "Relation.High"
                    };
                    t.TacticModifiers[NegotiationTactic.Plead] = 1.5f;
                    t.CostModifiers[NegotiationCostType.SocialRelation] = 1.5f; // 消耗好感很有效
                    t.GoalResistanceModifiers[goalType] = -20f; // 所有目标难度-20
                    potentialTraits.Add(t);
                }
            }
            // ==========================================
            // 8. 身份地位 (Status / Clan)
            // ==========================================
            // 判断是否是大家族族长
            bool isClanLeader = profile.BaseHero != null && profile.BaseHero.Clan != null && profile.BaseHero.Clan.Leader == profile.BaseHero;

            if (isClanLeader)
            {
                // 本地化：LWN_nego_trait_clanleader_name（谈判特质名，双桶）
                var t = new NegotiationTrait("ClanLeader", LWNTextHelper.ResolvePrompt("LWN_nego_trait_clanleader_name"), TraitPolarity.Resistance)
                {
                    // 本地化：LWN_nego_trait_clanleader_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_clanleader_desc"),
                    SourceField = "Role.ClanLeader"
                };
                // 很难被小利打动
                t.CostModifiers[NegotiationCostType.PersonalGold] = 0.7f;
                // 只有家族利益（权力、领地）有效
                t.TacticModifiers[NegotiationTactic.OfferPower] = 1.2f;
                // 劝降难度极大
                t.GoalResistanceModifiers[NegotiationGoalType.DefectFaction] = 80f;
                potentialTraits.Add(t);
            }

            // 身份高贵
            float estimatedValue = profile.CalculateEstimatedValue(); // 假设 NPCProfile 有这个方法
            if (estimatedValue > 500000)
            {
                // 本地化：LWN_nego_trait_noble_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Noble", LWNTextHelper.ResolvePrompt("LWN_nego_trait_noble_name"), TraitPolarity.Resistance)
                {
                    // 本地化：LWN_nego_trait_noble_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_noble_desc"),
                    SourceField = "Status.High"
                };
                t.CostModifiers[NegotiationCostType.PersonalGold] = 0.5f; // 钱太少看不上
                t.TacticModifiers[NegotiationTactic.Submisson] = 1.2f; // 喜欢别人低头
                potentialTraits.Add(t);
            }

            // ==========================================
            // 9. 特殊目标阻断 (Contextual Blockers)
            // ==========================================
            // 如果求婚，但对方已婚
            if (goalType == NegotiationGoalType.ProposeMarriage && profile.BaseHero != null && profile.BaseHero.Spouse != null)
            {
                // 本地化：LWN_nego_trait_married_name（谈判特质名，双桶）
                var t = new NegotiationTrait("Married", LWNTextHelper.ResolvePrompt("LWN_nego_trait_married_name"), TraitPolarity.Immunity)
                {
                    // 本地化：LWN_nego_trait_married_desc（谈判特质描述，双桶）
                    Description = LWNTextHelper.ResolvePrompt("LWN_nego_trait_married_desc"),
                    SourceField = "Spouse"
                };
                // 阻力无限大
                t.GoalResistanceModifiers[NegotiationGoalType.ProposeMarriage] = 9999f;
                potentialTraits.Add(t);
            }

            return potentialTraits
               .OrderByDescending(x => x.Polarity == TraitPolarity.Immunity)
               .ThenByDescending(x => x.Polarity == TraitPolarity.Weakness)
               .ThenByDescending(x => x.Polarity == TraitPolarity.Resistance)
               .Take(4) // UI可能只放得下4个
               .ToList();        

           
        }
    }


    public enum NegotiationCostType
    {
        // --- 基础资源 ---
        None,               // 纯嘴炮 (高风险)
        PersonalGold,       // 个人金钱 (痛点：自身财富减少)
        Item,               // 特定物品 (如：装备、贡品)

        // --- 社会资本 (Social Capital) ---
        Reputation, // 善名 (消耗：透支面子，或者用爱感化)
        Notoriety,       // 恶名 (消耗：加深恐惧，但可能导致后续反噬)
        SocialRelation,         // 个人好感度 (消耗：欠人情)
        FactionInfluence,       // 势力内功勋 (消耗：动用特权) 但是用一次下次就不能用了


        //--群体资源 (Strategic Resources) ---
        FactionGold,        // 势力资金 (痛点：需要权限，可能导致贪污指控)
        SettlementOwnership, // 城市所有权
        Troop,             // 军队 
        Prisoner,          // 俘虏

        // --- 契约期货 (Futures) ---
        Promise,      // 承诺


        // ---无法交易的，但是可以衡量价值的 ---
        CityProsperity, // 城市繁荣度
    }

    public class CostTypeInfo
    {
        public NegotiationCostType Type { get; set; }

        public string Name { get; set; } // UI 显示名称
        public float UnitValue { get; set; } 

        //之后还要细分，比如城池肯定不能用统一的价值，而是要基于城池的繁荣度、兵力等计算价值
    }

    //谈判筹码
    public class Chip
    {
        public NegotiationCostType Type { get; set; }
        public string Name { get; set; }        // 显示名称，如 "1000 第纳尔" 或 "肖农"
        public string StringId { get; set; }     // 游戏内ID，用于结算
        public int Amount { get; set; }         // 数量
        public float EstimatedValue { get; set; } // 估算价值（用于UI预览条计算）

        public Chip(NegotiationCostType costType,string name="",string stringId = "", int amount=1)
        {
            Type = costType;
            StringId = stringId;
            Amount = amount;
            Name = name;
            EstimatedValue = NegotiationRegistry.CalculateChipValue(costType,stringId,amount);

            if (Settings.Instance.ShowDebugMessages)
                InformationManager.DisplayMessage(new InformationMessage($"筹码创建{costType.ToString()}：{Name}，价值：{EstimatedValue}"));
            if(EstimatedValue == 0)
            {
                DebugLogger.Log($"0 warning:筹码创建{costType.ToString()}：{Name}，数量 {amount} 价值：{EstimatedValue}");
            }
        }
    }
    public class DraftProposal
    {
        public List<Chip> chips { get; set; } = new List<Chip>();

        public void AddOrUpdate(Chip chip)
        {
            // 简单的合并逻辑：如果是金钱则累加，其他则添加
            if (chip.Type == NegotiationCostType.PersonalGold)
            {
                var existing = chips.FirstOrDefault(x => x.Type == NegotiationCostType.PersonalGold);
                if (existing != null)
                {
                    existing.Amount += chip.Amount;
                    // 【核心修复】：更新显示名称和估值，否则 UI 上显示的还是旧的 "20金"
                    existing.Name = $"{existing.Amount}金";
                    existing.EstimatedValue = NegotiationRegistry.CalculateChipValue(existing);
                }
                else
                {
                    chips.Add(chip);
                }
            }
            else
            {
                chips.Add(chip);
            }
        }
        public int GetCurrentGoldAmount()
        {
            var goldChip = chips.FirstOrDefault(x => x.Type == NegotiationCostType.PersonalGold);
            return goldChip?.Amount ?? 0;
        }

        public void Remove(Chip chip)
        {
            chips.Remove(chip);
        }

        public void Clear() => chips.Clear();

        // 生成给 LLM 看的描述
        public string GetDescription()
        {
            // 本地化：LWN_nego_chips_none（无筹码描述，双桶）
            if (chips.Count == 0) return LWNTextHelper.ResolvePrompt("LWN_nego_chips_none");
            return string.Join(", ", chips.Select(x => $"{x.Name}"));

            //这里之后要补充筹码的总价值
        }

        // 计算总估值（用于进度条预览）
        public float GetTotalEstimatedValue()
        {
            return chips.Sum(x => x.EstimatedValue);
        }
    }

    public enum NegotiationGoalType
    {
        None,

        // --- 社交类 ---
        ProposeMarriage,      // 求婚
        Exaction,//索要
        MakeFriend,           // 结交/拜把子
        MakeMaster,           // 拜师学艺
        JoinInFaction,        // 加入势力
        EnquireAboutHero, //打听英雄信息

        // --- 招募/人事 ---
        RecruitHero,          // 招募家族伙伴
        DefectFaction,        // 劝降敌将

        // --- 临时/交互 ---
        EnterSettlement,           // 进入城市/城堡(大部分时候是通过贿赂守卫，但是也可以说服）
        PeaceTalk,            // 临时停战/放过一马

        // --- 贸易/契约 ---
        TradeAgreement,       // 贸易协定
        // [新增] 冲突解决类
        ResolveConflict_Apology,   // 道歉/赔偿 (对应原 RequiresApology)
        ResolveConflict_Explain,   // 解释/洗白 (对应原 MoralQuestioning)
        ResolveConflict_Intimidate // 威慑/压制 (对应原 Provocation)
    }
    public class NegotiationGoalTemplate
    {
        public NegotiationGoalType Type { get; set; }
        /// <summary>本地化 key（双桶 XML；2026-08-20 双语化：Name/Description 延迟解析——静态字典
        /// 初始化无法调引擎本地化，getter 运行时求值；UI 名走 ResolveText，prompt 材料走 ResolvePrompt）</summary>
        public string NameKey { get; set; }        // UI显示：求婚
        public string DescKey { get; set; }        // LLM提示词：玩家试图向NPC求婚

        public string Name => string.IsNullOrEmpty(NameKey) ? "" : LWNTextHelper.ResolveText(NameKey);
        public string Description => string.IsNullOrEmpty(DescKey) ? "" : LWNTextHelper.ResolvePrompt(DescKey);

        //希望从对方这里获得什么
        public NegotiationCostType NeedType { get; set; }= NegotiationCostType.None;

        //索要物品的时候，需要带一个物品ID,如果是城池，那么是城池ID
        public string TargetStringId { get; set; } = string.Empty;

        public float BaseDifficulty { get; set; } // 基础难度（比如求婚需要100分，过关只需要30分）

        // 允许的手段白名单（可选）：比如求婚时可能不允许“威胁”，或者威胁的效果极差
        // 也可以留空，交给 LLM 动态判断
    }

    public enum NegotiationTactic
    {
        // --- 利益驱动 ---
        Bribe,              // 贿赂 (金钱)
        Gift,               // 赠礼 (物品)
        OfferPower,         // 许诺权力/领地 (战略资源)

        // --- 情感/社交 ---
        Plead,              // 恳求 (消耗好感/人情)
        Flatter,            // 恭维 (无消耗/精力)

        // --- 压迫/强制 ---
        Threaten,           // 威胁 (消耗恶名/增加仇恨风险)
        Coerce,             // 强迫/施压 (消耗势力影响力)

        // --- 承诺/契约 ---
        Swear,           // 立誓 (生成任务)
        Submisson,          // 乞求 (消耗荣誉)

        Reason,             // 说理 

        
    }
    public class NegotiationMoveTemplate
    {
        public NegotiationTactic Tactic { get; set; }       // 意图 ID
        public string Name { get; set; }                    // UI 显示名称 (e.g. "重金收买")

        // --- 资源消耗配置 ---
        public NegotiationCostType CostType { get; set; }   // 对应的资源枚举
        public float BaseCostValue { get; set; }            // 基础消耗数值 (给 LLM 参考或代码扣除)

        // --- 判定配置 (Resistance) ---
        // 这种手段主要攻击 NPC 的哪个弱点？或者受到哪个属性的防御？
        // 例如：贿赂(Bribe) 对抗 NPC的 荣誉(Honor) 或 贪婪(Greed)
        //对抗属性
        public string TargetAttributeCheck { get; set; }
        //是否属性越高，越容易成功，比如贪婪越高，贿赂越容易成功；反之，勇敢越高，威胁越容易失败
        public bool HigherIsBetter { get; set; } = true;
        //被克制
        // --- 给 LLM 的指导 ---
        // 告诉 LLM 这个动作的语境，防止它理解偏
        public string DescriptionPrompt { get; set; }
    }

    
    
    
    
    
    

    public static class NegotiationRegistry
    {
        public static float CalculateMultiplier(NegotiationCard card, NegotiationState state)
        {
            float finalMultiplier = 1.0f;

            // 1. 遍历 NPC 当前所有的特征
            foreach (var trait in state.ActiveTraits)
            {
                // --- A. 手段修正 (Tactic) ---
                // 比如：贪婪的人(Trait) 对 贿赂(Bribe) 有 2.0 倍率
                float tacticMod = trait.GetTacticMultiplier(card.Tactic);
                if (tacticMod != 1.0f)
                {
                    // 这里可以做累乘，也可以做加权，建议累乘但限制边界
                    finalMultiplier *= tacticMod;
                    DebugLogger.Log($"[Trait触发] {trait.Name} 对手段 {card.Tactic} 修正: x{tacticMod}");
                }

                // --- B. 筹码修正 (Cost Type) ---
                // 比如：清高的人(Trait) 对 个人金钱(PersonalGold) 有 0.0 倍率
                // 如果卡牌里包含这种筹码，则整体效果受影响
                foreach (var chip in card.Chips)
                {
                    float costMod = trait.GetCostMultiplier(chip.Type);
                    if (costMod != 1.0f)
                    {
                        // 简单的处理方式：如果筹码被讨厌，整体效果打折
                        // 也可以根据筹码占比精细计算，这里简化处理
                        finalMultiplier *= costMod;
                        DebugLogger.Log($"[Trait触发] {trait.Name} 对筹码 {chip.Type} 修正: x{costMod}");
                    }
                }
            }

            // 防止倍率过高或过低
            return MathF.Clamp(finalMultiplier, 0.1f, 5.0f);
        }
        public static float CalculateCardValue(NegotiationCard card)
        {
            //计算卡牌价值
            if (card == null) { return 0; }
            float sum1 = card.Chips.Sum(chip => chip.EstimatedValue);
            float sum2 = card.Chips.Sum(chip => CalculateChipValue(chip));


            return sum1;
        }

        public static float CalculateChipValue(NegotiationCostType costType, string StringId, int amount)
        {
            float estimatedValue = 0;
            if (!Cost2Info.TryGetValue(costType, out var costInfo))
            {
                // 如果字典里没有这个类型，默认为0
                estimatedValue = 0;
            }
            else
            {
                // 2. 根据类型进行特殊计算
                switch (costType)
                {
                    // --- 物品计算逻辑 ---
                    case NegotiationCostType.Item:
                        estimatedValue = CalculateItemValue(StringId, amount, costInfo.UnitValue);
                        break;

                    // --- 城池计算逻辑 ---
                    case NegotiationCostType.SettlementOwnership:
                        estimatedValue = CalculateSettlementValue(StringId, costInfo.UnitValue);
                        break;

                    // --- 默认计算逻辑 (基础单价 * 数量) ---
                    default:
                        estimatedValue = costInfo.UnitValue * amount;
                        break;
                }
            }
            return estimatedValue;
        }
        public static float CalculateChipValue(Chip chip)
        {
            if (chip == null) return 0;
            // 1. 获取基础配置信息 (从上一轮对话定义的字典中获取)

            NegotiationCostType costType = chip.Type;
            string StringId = chip.StringId;
            int amount = chip.Amount;
            chip.EstimatedValue = CalculateChipValue(costType, StringId, amount);
            return chip.EstimatedValue;

           
        }

        public static float CalculateItemValue(string itemId, int amount, float fallbackUnitValue)
        {
            if (string.IsNullOrEmpty(itemId)) return fallbackUnitValue * amount;

            // 模拟调用游戏API获取物品对象
             var itemObject = MBObjectManager.Instance.GetObject<ItemObject>(itemId);

            // 伪代码演示：如果找到了物品对象
            if (itemObject != null)
             {
                 return (float)itemObject.Value * amount;
             }
            // 如果找不到ID，回退到默认估值（虽然对于具体物品来说，1.0的默认值可能很不准）
            return fallbackUnitValue * amount;
        }
        public static float CalculateSettlementValue(string settlementId, float baseUnitValue)
        {
            if (string.IsNullOrEmpty(settlementId)) return baseUnitValue;
            
            var settlement = MBObjectManager.Instance.GetObject<Settlement>(settlementId);
            float ProsperityToGoldMultiplier = GetCostTypeInfo(NegotiationCostType.CityProsperity).UnitValue;
            // 伪代码演示逻辑
             if (settlement != null)
             {
                  float prosperityValue = 0f;
                 
                 if (settlement.IsTown)
                 {
                     prosperityValue = settlement.Town.Prosperity * ProsperityToGoldMultiplier;
                 }
                 else if (settlement.IsCastle)
                 {
                     // 城堡繁荣度通常较低，但战略价值高，可以给个额外加成
                      prosperityValue = (settlement.Town.Prosperity * ProsperityToGoldMultiplier);
                 }
            
                 // 总价值 = 基础底价(Cost2Info里定义的10万) + 繁荣度折算
                 // 例如：基础10万 + (4000繁荣度 * 100) = 50万
                 return baseUnitValue + prosperityValue;
            }

            return baseUnitValue;
        }



        public static readonly Dictionary<NegotiationCostType, CostTypeInfo> Cost2Info = new Dictionary<NegotiationCostType, CostTypeInfo>()
         { {
                    NegotiationCostType.None,
                    new CostTypeInfo { Type = NegotiationCostType.None, Name = "言语", UnitValue = 0.01f }
                },
                {
                    NegotiationCostType.PersonalGold,
                    new CostTypeInfo { Type = NegotiationCostType.PersonalGold, Name = "个人金", UnitValue = 1.0f }
                },
                {
                    NegotiationCostType.FactionGold,
                    new CostTypeInfo { Type = NegotiationCostType.FactionGold, Name = "家族金", UnitValue = 1.0f }
                },
                {
                    // 注意：通常物品交易逻辑会传入物品的市场总价，因此这里的单位系数设为 1
                    NegotiationCostType.Item,
                    new CostTypeInfo { Type = NegotiationCostType.Item, Name = "物品", UnitValue = 1.0f }
                },

                // --- 社会资本 ---
                {
                    NegotiationCostType.Reputation,
                    new CostTypeInfo { Type = NegotiationCostType.Reputation, Name = "声望", UnitValue = 500.0f }
                },
                {
                    NegotiationCostType.Notoriety,
                    new CostTypeInfo { Type = NegotiationCostType.Notoriety, Name = "恶名", UnitValue = 500.0f }
                },
                {
                    NegotiationCostType.SocialRelation,
                    new CostTypeInfo { Type = NegotiationCostType.SocialRelation, Name = "好感度", UnitValue = 500.0f }
                },
                {
                    NegotiationCostType.FactionInfluence,
                    new CostTypeInfo { Type = NegotiationCostType.FactionInfluence, Name = "影响力", UnitValue = 100.0f }
                },

                // --- 群体资源 ---
                {
                    NegotiationCostType.SettlementOwnership,
                    // 警告：城池价值极大且浮动，此处仅为从属代码的默认托底值，实际计算需Override
                    new CostTypeInfo { Type = NegotiationCostType.SettlementOwnership, Name = "城池", UnitValue = 100000.0f }
                },
                {
                    NegotiationCostType.Troop,
                    new CostTypeInfo { Type = NegotiationCostType.Troop, Name = "军队", UnitValue = 150.0f }
                },
                {
                    NegotiationCostType.Prisoner,
                    new CostTypeInfo { Type = NegotiationCostType.Prisoner, Name = "俘虏", UnitValue = 50.0f }
                },

                // --- 契约/其他 ---
                {
                    NegotiationCostType.Promise,
                    new CostTypeInfo { Type = NegotiationCostType.Promise, Name = "承诺", UnitValue = 5000.0f }
                },
                {
                    NegotiationCostType.CityProsperity,
                    new CostTypeInfo { Type = NegotiationCostType.CityProsperity, Name = "繁荣度", UnitValue = 20.0f }
                }
        };
        public static CostTypeInfo GetCostTypeInfo(NegotiationCostType type)
        {
            if(Cost2Info.ContainsKey(type))
            {
                return Cost2Info[type];
            }
            else
                return null;
        }
        public static string GetCostName(NegotiationCostType type)
        {
            if(Cost2Info.ContainsKey(type))
            {
                return Cost2Info[type].Name;
            }
            else
                return "嘴炮";
        }

        public static readonly Dictionary<NegotiationTactic, NegotiationMoveTemplate> Tactic2Info = new Dictionary<NegotiationTactic, NegotiationMoveTemplate>()
    {
        // =========================================================
        // 1. 金钱与物质 (Greed vs Honor)
        // =========================================================
        {
            NegotiationTactic.Bribe,
            new NegotiationMoveTemplate
            {
                Tactic = NegotiationTactic.Bribe,
                Name = "贿赂",
                CostType = NegotiationCostType.PersonalGold,
                BaseCostValue = 1000f, // 基础单位
                TargetAttributeCheck = "Greed",
                HigherIsBetter = true,
                // 本地化：LWN_nego_tactic_bribe_desc（谈判手段描述，双桶）
                DescriptionPrompt = LWNTextHelper.ResolvePrompt("LWN_nego_tactic_bribe_desc"),
            }
        },
        {
            NegotiationTactic.Gift,
            new NegotiationMoveTemplate
            {
                Tactic = NegotiationTactic.Gift,
                Name = "赠品",
                CostType = NegotiationCostType.Item,
                BaseCostValue = 1f, // 1个物品
                TargetAttributeCheck = "Greed", // 后面要细分一下，对各类物品有不同的效果，比如武器、书画等
                HigherIsBetter = true,
                // 本地化：LWN_nego_tactic_gift_desc（谈判手段描述，双桶）
                DescriptionPrompt = LWNTextHelper.ResolvePrompt("LWN_nego_tactic_gift_desc"),
            }
        },

        // =========================================================
        // 2. 威慑与压迫 (Fear vs Bravery)
        // =========================================================
        {
            NegotiationTactic.Threaten,
            new NegotiationMoveTemplate
            {
                Tactic = NegotiationTactic.Threaten,
                Name = "威胁",
                // 这里对应你的 Notoriety。虽然通常是"获得"恶名，但在谈判资源里，
                // 可以理解为"消费你的凶名"来震慑对方，或者"冒着增加恶名的风险"。
                CostType = NegotiationCostType.Notoriety,
                BaseCostValue = 10f,
                TargetAttributeCheck = "Bravery", // 对方越懦弱，越有效
                HigherIsBetter = false,
                // 本地化：LWN_nego_tactic_threaten_desc（谈判手段描述，双桶）
                DescriptionPrompt = LWNTextHelper.ResolvePrompt("LWN_nego_tactic_threaten_desc"),
            }
        },
        {
            NegotiationTactic.Coerce,
            new NegotiationMoveTemplate
            {
                Tactic = NegotiationTactic.Coerce,
                Name = "施压",
                CostType = NegotiationCostType.FactionInfluence,
                BaseCostValue = 50f,
                TargetAttributeCheck = "Scheming", // 也取决于对方的势力大小。对方如果是老谋深算的，可能不吃这一套或者需要更高影响力
                HigherIsBetter = false,
                // 本地化：LWN_nego_tactic_coerce_desc（谈判手段描述，双桶）
                DescriptionPrompt = LWNTextHelper.ResolvePrompt("LWN_nego_tactic_coerce_desc"),
            }
        },

        // =========================================================
        // 3. 情感与人际 (Sympathy vs Calculating)
        // =========================================================
        {
            NegotiationTactic.Plead,
            new NegotiationMoveTemplate
            {
                Tactic = NegotiationTactic.Plead,
                Name = "恳求",
                CostType = NegotiationCostType.SocialRelation,
                BaseCostValue = 20f, // 消耗好感度
                TargetAttributeCheck = "Compassion", // 对方同情心越高越容易成功，或者越重视人情越容易成功
                HigherIsBetter = true,
                // 本地化：LWN_nego_tactic_plead_desc（谈判手段描述，双桶）
                DescriptionPrompt = LWNTextHelper.ResolvePrompt("LWN_nego_tactic_plead_desc"),
            }
        },        

        // =========================================================
        // 4. 尊严与自我 (Dignity vs Pride)
        // =========================================================
        {
            NegotiationTactic.Submisson,
            new NegotiationMoveTemplate
            {
                Tactic = NegotiationTactic.Submisson,
                Name = "乞怜",
                CostType = NegotiationCostType.Reputation,
                BaseCostValue = 30f, // 大幅扣除名声
                TargetAttributeCheck = "Pride", // 对方如果傲慢，会很享受这个
                HigherIsBetter = true,
                // 本地化：LWN_nego_tactic_submisson_desc（谈判手段描述，双桶；保留枚举原拼写 Submisson）
                DescriptionPrompt = LWNTextHelper.ResolvePrompt("LWN_nego_tactic_submisson_desc"),
            }
        },
        {
            NegotiationTactic.Flatter,
            new NegotiationMoveTemplate
            {
                Tactic = NegotiationTactic.Flatter,
                Name = "恭维",
                CostType = NegotiationCostType.None, // 嘴炮
                BaseCostValue = 0f,
                TargetAttributeCheck = "Scheming", // 聪明人能识破
                HigherIsBetter = false,
                // 本地化：LWN_nego_tactic_flatter_desc（谈判手段描述，双桶）
                DescriptionPrompt = LWNTextHelper.ResolvePrompt("LWN_nego_tactic_flatter_desc"),
            }
        },
        
        // =========================================================
        // 5. 战略与大权 (Ambition vs Loyalty)
        // =========================================================
        {
            NegotiationTactic.OfferPower,
            new NegotiationMoveTemplate
            {
                Tactic = NegotiationTactic.OfferPower,
                Name = "授权",
                CostType = NegotiationCostType.SettlementOwnership, 
                BaseCostValue = 1f,
                TargetAttributeCheck = "Scheming", // 野心家喜欢这个
                HigherIsBetter=true,
                // 本地化：LWN_nego_tactic_offerpower_desc（谈判手段描述，双桶）
                DescriptionPrompt = LWNTextHelper.ResolvePrompt("LWN_nego_tactic_offerpower_desc"),
            }
        },
        {
            NegotiationTactic.Swear,
            new NegotiationMoveTemplate
            {
                Tactic = NegotiationTactic.Swear,
                Name = "许诺",
                CostType = NegotiationCostType.Promise,
                BaseCostValue = 1f,
                TargetAttributeCheck = "Trust", // 取决于信任度
                HigherIsBetter = true,
                // 本地化：LWN_nego_tactic_swear_desc（谈判手段描述，双桶）
                DescriptionPrompt = LWNTextHelper.ResolvePrompt("LWN_nego_tactic_swear_desc"),
            }
        }
    };

        public static NegotiationMoveTemplate GetTacticInfo(NegotiationTactic tactic)
        {
            if (Tactic2Info.TryGetValue(tactic, out var template))
                return template;
            return Tactic2Info[NegotiationTactic.Flatter]; // 默认兜底
        }

        public static readonly Dictionary<NegotiationGoalType, NegotiationGoalTemplate> Goal2Info = new Dictionary<NegotiationGoalType, NegotiationGoalTemplate>()
        {
            {
                NegotiationGoalType.ResolveConflict_Apology,
                new NegotiationGoalTemplate
                {
                    Type = NegotiationGoalType.ResolveConflict_Apology,
                    // 本地化：LWN_nego_goal_apology_name（双桶）
                    NameKey = "LWN_nego_goal_apology_name",
                    // 本地化：LWN_nego_goal_apology_desc（双桶）
                    DescKey = "LWN_nego_goal_apology_desc",
                    BaseDifficulty = 1.0f  //一般和身价相乘
                }
            },
            {
                NegotiationGoalType.ProposeMarriage,
                new NegotiationGoalTemplate
                {
                    Type = NegotiationGoalType.ProposeMarriage,
                    // 本地化：LWN_nego_goal_marriage_name（双桶）
                    NameKey = "LWN_nego_goal_marriage_name",
                    // 本地化：LWN_nego_goal_marriage_desc（双桶）
                    DescKey = "LWN_nego_goal_marriage_desc",
                    BaseDifficulty = 0.1f  //一般和身价相乘
                }
            },
            {
                NegotiationGoalType.EnterSettlement,
                new NegotiationGoalTemplate
                {
                    Type = NegotiationGoalType.EnterSettlement,
                    // 本地化：LWN_nego_goal_settlement_name（双桶）
                    NameKey = "LWN_nego_goal_settlement_name",
                    // 本地化：LWN_nego_goal_settlement_desc（双桶）
                    DescKey = "LWN_nego_goal_settlement_desc",
                    BaseDifficulty = 1.0f // 相对简单，不和身价相乘
                }
            },
             {
                NegotiationGoalType.RecruitHero,
                new NegotiationGoalTemplate
                {
                    Type = NegotiationGoalType.RecruitHero,
                    // 本地化：LWN_nego_goal_recruit_name（双桶）
                    NameKey = "LWN_nego_goal_recruit_name",
                    // 本地化：LWN_nego_goal_recruit_desc（双桶）
                    DescKey = "LWN_nego_goal_recruit_desc",
                    // 🔴 身价即招募价（与 CalculateEstimatedValue 流浪者公式统一）：0.05 系数会导致
                    // prompt 自报身价 10500、实际招募只花 ~525 的两套数字，NPC 自抬身价出戏。
                    BaseDifficulty = 1.0f // 一般和身价相乘（1.0 = 招募花费 = 身价）
                }
            },
             {
                NegotiationGoalType.None,
                new NegotiationGoalTemplate
                {
                    Type = NegotiationGoalType.None,
                    // 本地化：LWN_nego_goal_none_name（双桶）
                    NameKey = "LWN_nego_goal_none_name",
                    // 本地化：LWN_nego_goal_none_desc（双桶）
                    DescKey = "LWN_nego_goal_none_desc",
                    BaseDifficulty = 0.0f // 
                }
            },
        };
        public static NegotiationGoalTemplate GetGoalInfo(string  typeStr)
        {
            if (Enum.TryParse<NegotiationGoalType>(typeStr, true, out var type))
            {
                return GetGoalInfo(type);
            }
            return Goal2Info[NegotiationGoalType.None];
        }
        public static NegotiationGoalTemplate GetGoalInfo(NegotiationGoalType type)
        {
            if (Goal2Info.TryGetValue(type, out var val)) return val;
            return Goal2Info[NegotiationGoalType.None];
        }



    }

    //技能检定阶段卡牌（谈判之前）
    public class SkillCheckOption
    {
        [JsonIgnore]
        public NegotiationTactic Tactic { get; set; }

        [JsonProperty("tactic")]
        public string TacticRaw  // 意图: "重金贿赂"
        {
            get { return Tactic.ToString(); } // 序列化时（如果有）转回字符串
            set
            {
                // 兜底核心逻辑：尝试解析
                // 参数 true 表示忽略大小写 (e.g., "none" == "None")
                if (Enum.TryParse<NegotiationTactic>(value, true, out var result))
                {
                    Tactic = result;
                }
                else
                {
                    Tactic = NegotiationTactic.Flatter;
                }
                CalculateChance();
            }
        }
        [JsonProperty("text")]
        public string Text { get; set; } // 玩家具体说的话

        [JsonProperty("player_emotion")]
        public string Emotion { get; set; } // 玩家情绪

        [JsonProperty("outcome_prediction")]
        public string Prediction { get; set; } // "进度大幅增加，但会损失名誉"

        // 核心：C# 计算出的成功率 (0.0 - 1.0)
        public float SuccessChance { get; set; }
        // 核心：用于检定的技能ID (例如 "Charm", "Roguery")
        public TaleWorlds.Core.SkillObject RelatedSkill { get; set; }

        public SkillCheckOption()
        {           
        }

        private void CalculateChance()
        {
            // 手段→技能 走唯一权威映射（见 SkillCheckSystem.MapTacticToSkill）
            RelatedSkill = SkillCheckSystem.MapTacticToSkill(Tactic);

            // 获取玩家角色
            var hero = Hero.MainHero;
            if (hero != null)
            {
                int skillValue = hero.GetSkillValue(RelatedSkill);
                // 简单的成功率公式：基础30% + 技能等级 * 0.5%
                SuccessChance = 0.3f + (skillValue * 0.005f);
                if (SuccessChance > 0.95f) SuccessChance = 0.95f;
            }
        }
    }

    //谈判阶段卡牌
    public class NegotiationCard
    {
        [JsonIgnore]
        public NegotiationTactic Tactic { get; set; } = NegotiationTactic.Flatter;

        [JsonIgnore]
        public List<Chip> Chips { get; set; } = new List<Chip>();

        [JsonIgnore]
        public string SuccessProbabilityDesc { get; set; } // "成功率描述，比如高风险",但是这个成功率应该是c#计算的，而不是LLM 

        public NegotiationCard(string TacticString,string TextStr)
        {
            if (Enum.TryParse<NegotiationTactic>(TacticString, true, out var result))
            {
                Tactic = result;
            }
            else
            {
                Tactic = NegotiationTactic.Flatter;
            }
            Text = TextStr;

            Init();
            
        }
        public NegotiationCard()
        {
            // 这里不要调用 Init()，
        }
        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            // CostValue 如果 JSON 里有，也已经赋值了
            Init();
        }
        public float GetEstimatedValue()
        {
            return Chips.Sum(x => x.EstimatedValue);
        }
        public void Init()
        {

            var tem = NegotiationRegistry.GetTacticInfo(Tactic); // 获取成本类型
            if (CostAmount == 0)
                return;
            CostType = tem.CostType;
            Chip oneChip = new Chip(tem.CostType, "", "", CostAmount); // 创建筹码
            EffectBaseValue = (int)oneChip.EstimatedValue;
            if(EffectBaseValue == 0)
            {
                DebugLogger.Log($"0 warning: 策略{Tactic} 筹码创建{CostType.ToString()}：{oneChip.Name}，价值：{oneChip.EstimatedValue}");
            }
            Chips.Add(oneChip); // 添加筹码
        }

        [JsonProperty("tactic")]
        public string TacticRaw  // 意图: "重金贿赂"
        {
            get { return Tactic.ToString(); } // 序列化时（如果有）转回字符串
            set
            {
                // 兜底核心逻辑：尝试解析
                // 参数 true 表示忽略大小写 (e.g., "none" == "None")
                if (Enum.TryParse<NegotiationTactic>(value, true, out var result))
                {
                    Tactic = result;
                }
                else
                {
                    Tactic = NegotiationTactic.Flatter;}
                }
        }

        [JsonProperty("text")]
        public string Text { get; set; } // 玩家具体说的话

        [JsonProperty("player_emotion")]
        public string Emotion { get; set; } // 玩家情绪

        [JsonIgnore]
        public NegotiationCostType CostType { get; private set; } = NegotiationCostType.None;

        [JsonProperty("cost_type")]
        public string CostTypeRaw
        {
            get { return CostType.ToString(); } // 序列化时（如果有）转回字符串
            set
            {
                // 兜底核心逻辑：尝试解析
                // 参数 true 表示忽略大小写 (e.g., "none" == "None")
                if (Enum.TryParse<NegotiationCostType>(value, true, out var result))
                {
                    CostType = result;
                }
                else
                {
                    CostType = NegotiationCostType.None;
                }
            }
        }

        [JsonProperty("cost_amount")]
        public int CostAmount { get; set; } // 代价数量

        [JsonProperty("effect_base_value")]
        public int EffectBaseValue { get; set; } // 效果基础数值，这个实际效果还会根据Npc的抗性修正


        [JsonProperty("outcome_prediction")]
        public string PredictedImpact { get; set; } // "进度大幅增加，但会损失名誉"
    }
    // ----------------------------------------------------------------
    // 4. 谈判状态包 (Negotiation State)
    // ----------------------------------------------------------------
    // 用于在前后端、LLM 之间传递当前局势

    public class NegotiationTurnLog
    {
        public int TurnIndex { get; set; }
        public string PlayerTactic { get; set; } // 玩家用的卡牌名 (如 "利诱", "威胁")

        public string PlayerInput { get; set; }
        public float ProgressDelta { get; set; }    // 修正后价值

        public float ChipValue { get; set; } //筹码原生价值
        public string NpcReply { get; set; }     // NPC 当时的回复

        public string NpcThinking { get; set; }

        // 核心指标：NPC 的反馈系数 (0.5 = 厌恶/暴击抵抗, 1.0 = 平淡, 1.5+ = 暴击/心动)
        public float FeedbackMultiplier { get; set; }

        // 这回合后的总进度百分比
        public float ResultingProgressRatio { get; set; }
    }

    public class NegotiationState
    {
        public string Name { get; set; }
        public string TargetName { get; set; }

        public Agent TargetAgent;

        public Hero TargetHero;
        // 新增：当前谈判的宏观目标
        public NegotiationGoalType GoalType { get; set; }
        public string PlayerGoalDescription { get; set; } // 玩家目标: "求婚"

        // 1. 定义一个私有的后备字段
        private float _currentProgress = 0f;

        public StringBuilder CalculationLog { get; set; } = new StringBuilder();
        public float CurrentProgress
        {
            get => _currentProgress;
            set
            {
                _currentProgress = MathF.Clamp(value, 0f, TargetThreshold);
            }
        }
        public float TargetThreshold { get; set; } = 100f; // 目标值

        // [新增] 已确定的、放上桌的筹码（累计池）
        public List<Chip> CommittedChips { get; set; } = new List<Chip>();

        // [新增] 本轮新增的筹码（用于计算 Delta 和让 LLM 做出即时反应）
        public List<Chip> LastTurnAddedChips { get; set; } = new List<Chip>();

        public PlayerResources playerResources { get; set; }

        public string InitialImpressionContext { get; private set; } = "";


        public int TurnCount { get; set; } = 0;
        public int MaxTurns { get; set; } = 5; // 限制回合数，增加紧迫感

        //回合谈判记录
        public List<NegotiationTurnLog> TurnHistory { get; set; } = new List<NegotiationTurnLog>();

        public List<NegotiationTrait> ActiveTraits { get; set; } = new List<NegotiationTrait>();


        public NegotiationState(Agent targetAgent, string goalType, string playerGoalDesc)
        {
            CalculationLog.Clear(); // 初始化日志
            CalculationLog.AppendLine("=== 谈判难度分析 ===");

            var GoalInfo = NegotiationRegistry.GetGoalInfo(goalType);
            GoalType = GoalInfo.Type;
            // 本地化：LWN_nego_state_name_format（谈判状态名格式，双桶）
            Name = LWNTextHelper.ResolveCompound("LWN_nego_state_name_format", ("NAME", NegotiationRegistry.GetGoalInfo(goalType).Name));
            TargetAgent = targetAgent;

            // Null-safe: Agent may be unavailable in campaign-map conversations
            if (targetAgent != null)
            {
                TargetName = targetAgent.Name.ToString();
                if (targetAgent.Character is CharacterObject character && character.HeroObject != null)
                {
                    TargetHero = character.HeroObject;
                }
            }

            PlayerGoalDescription = playerGoalDesc;
            TurnCount = -1; // 初始化回合数

            CalculationLog.AppendLine("\n--- 对方预期 (进度条目标值) ---");
            InitializeGoal();

            CalculationLog.AppendLine("\n--- 开局优势 (进度条初始值) ---");
            CurrentProgress = CalculateInitialProgress();

            // Memory lookup: prefer Agent path, fall back to Hero
            SingNpcMemorySystem memory = null;
            if (targetAgent != null)
            {
                memory = AllNpcMemoryManager.GetMemoryForAgent(targetAgent);
            }
            if (memory == null && TargetHero != null)
            {
                memory = AllNpcMemoryManager.GetMemory(TargetHero.StringId);
            }

            ActiveTraits = memory?._profile != null
                ? NegotiationTraitFactory.GenerateTraits(memory._profile, GoalType)
                : new List<NegotiationTrait>();

            playerResources = new PlayerResources(TargetHero);
        }
        /// <summary>
        /// Hero-based constructor for campaign-map conversations where no Mission Agent exists.
        /// </summary>
        public NegotiationState(Hero targetHero, string goalType, string playerGoalDesc)
        {
            CalculationLog.Clear();
            CalculationLog.AppendLine("=== 谈判难度分析 ===");

            var GoalInfo = NegotiationRegistry.GetGoalInfo(goalType);
            GoalType = GoalInfo.Type;
            // 本地化：LWN_nego_state_name_format（谈判状态名格式，双桶）
            Name = LWNTextHelper.ResolveCompound("LWN_nego_state_name_format", ("NAME", NegotiationRegistry.GetGoalInfo(goalType).Name));
            TargetAgent = null;
            TargetHero = targetHero;
            TargetName = targetHero?.Name?.ToString() ?? "???";

            PlayerGoalDescription = playerGoalDesc;
            TurnCount = -1;

            CalculationLog.AppendLine("\n--- 对方预期 (进度条目标值) ---");
            InitializeGoal();

            CalculationLog.AppendLine("\n--- 开局优势 (进度条初始值) ---");
            CurrentProgress = CalculateInitialProgress();

            // Memory lookup: from Hero directly (no Agent in campaign-map context)
            SingNpcMemorySystem memory = null;
            if (TargetHero != null)
            {
                memory = AllNpcMemoryManager.GetMemory(TargetHero.StringId);
            }

            ActiveTraits = memory?._profile != null
                ? NegotiationTraitFactory.GenerateTraits(memory._profile, GoalType)
                : new List<NegotiationTrait>();

            playerResources = new PlayerResources(TargetHero);
        }
        public NegotiationState(Agent targetAgent, PendingConflict conflict)
        : this(targetAgent, conflict.GoalType.ToString(), conflict.GoalDescription)
        {
            // 可以在这里覆盖默认的计算逻辑
            // 比如：严重程度越高，TargetThreshold 越高，初始好感分越低
            
            // 重新计算难度，基于 Severity
            float severityMult = 1.0f + (conflict.Severity / 50.0f); // 50分时1.5倍，100分时3倍
            this.TargetThreshold *= severityMult;

            this.CalculationLog.AppendLine($"[冲突修正] 事件严重度 {conflict.Severity}，难度修正 x{severityMult:F2}");

            // 如果是非常严重的冲突，初始回合数减少
            if (conflict.Severity > 80)
            {
                this.MaxTurns = 3;
                this.CalculationLog.AppendLine($"[冲突修正] 对方极其愤怒，耐心(回合数)降为 {MaxTurns}");
            }
        }

        public float CalculateInitialProgress()
        {
            // 初始化日志头 (调试用)
            CalculationLog.AppendLine("\n--- 初始优势 (进度条) 计算过程 ---");

            // 初始化 LLM 印象描述构建器 (给AI看的内容)
            StringBuilder impressionSb = new StringBuilder();
            // 本地化：LWN_prompt_section_nego_impression（【开场印象判定】，双桶）
            impressionSb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_nego_impression"));
            // 本地化：LWN_prompt_negoimpress_intro（在谈判开始前…，双桶）
            impressionSb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_negoimpress_intro"));

            // 实际上要根据玩家当前的条件来一个初始进度
            float progress = 0f;
            Hero player = Hero.MainHero;
            string npcName = this.TargetHero != null ? this.TargetHero.Name.ToString() : "你";

            // 1. 关系加成 (Relation)
            if (TargetHero != null)
            {
                int relation = TargetHero.GetRelation(player);
                float additiveBonus_Relation = 0f;

                if (relation >= 10)
                {
                    additiveBonus_Relation = (relation / 100f) * 30f;
                    progress += additiveBonus_Relation;
                    CalculationLog.AppendLine($"[关系] 好感度：{relation}，加成：+{additiveBonus_Relation:F1}");

                    // --- 注入 Prompt ---
                    // 本地化：LWN_prompt_negoimpress_relation_high（1. **私交甚笃**…，双桶）
                    impressionSb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_negoimpress_relation_high", ("RELATION", relation.ToString())));
                }
                else if (relation < -5)
                {
                    additiveBonus_Relation = (relation / 100f) * 10f; // 负数
                    progress += additiveBonus_Relation;
                    CalculationLog.AppendLine($"[关系] 好感度：{relation}，扣除：{additiveBonus_Relation:F1}");

                    // --- 注入 Prompt ---
                    // 本地化：LWN_prompt_negoimpress_relation_low（1. **心存芥蒂**…，双桶）
                    impressionSb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_negoimpress_relation_low", ("RELATION", relation.ToString())));
                }
                else
                {
                    CalculationLog.AppendLine($"[关系] 好感度：0，无加成");
                    // --- 注入 Prompt ---
                    // 本地化：LWN_prompt_negoimpress_relation_neutral（1. **萍水相逢**…，双桶）
                    impressionSb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_negoimpress_relation_neutral"));
                }
            }

            // 2. 魅力技能加成 (Charm)
            int charmLevel = player.GetSkillValue(DefaultSkills.Charm);
            float charmBonus = (charmLevel / 300f) * 10f;
            progress += charmBonus;
            CalculationLog.AppendLine($"[技能] 魅力等级：{charmLevel}，加成：+{charmBonus:F1}贡献进度：中 (他说话确实好听)");

            // --- 注入 Prompt (只在魅力够高时提及，避免废话) ---
            if (charmLevel > 150)
            {
                // 本地化：LWN_prompt_negoimpress_charm（2. **谈吐不凡**…，双桶）
                impressionSb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_negoimpress_charm", ("LEVEL", charmLevel.ToString())));
            }

            // 3. 声望压制 (Renown)
            if (player.Clan != null && player.Clan.Renown > 1000)
            {
                progress += 5f;
                CalculationLog.AppendLine($"[声望] 玩家声望：{player.Clan.Renown:F0}，加成：+5.0");

                if (TargetHero != null && TargetHero.Clan != null && player.Clan.Renown > TargetHero.Clan.Renown)
                {
                    float renownDiff = player.Clan.Renown - TargetHero.Clan.Renown;
                    progress += 5f;
                    CalculationLog.AppendLine($"[压制] 声望高于对方，加成：+5.0");

                    // --- 注入 Prompt ---
                    // 本地化：LWN_prompt_negoimpress_renown_higher（3. **声名显赫**…，双桶）
                    impressionSb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_negoimpress_renown_higher", ("DIFF", $"{renownDiff:F0}")));
                }
                else
                {
                    // --- 注入 Prompt ---
                    // 本地化：LWN_prompt_negoimpress_renown_famous（3. **颇有名望**…，双桶）
                    impressionSb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_negoimpress_renown_famous"));
                }
            }

            if(progress > 50)
            {
                // 本地化：LWN_prompt_negoimpress_process_cap（流程仍要走，封顶 50%…，双桶）
                impressionSb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_negoimpress_process_cap"));
            }
            
            float finalProgress = MathF.Clamp(progress, 0f, 50f);

            float absoluteProgress = (finalProgress / 100) * TargetThreshold;

            CalculationLog.AppendLine($"--- 最终初始进度：{finalProgress:F1}% ---");

            // 本地化：LWN_prompt_negoimpress_decision（>> 基于上述心理活动…，双桶）
            impressionSb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_negoimpress_decision", ("PROGRESS", $"{finalProgress:F1}")));
            // 本地化：LWN_prompt_negoimpress_acting（>> 演绎指令…，双桶）
            impressionSb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_negoimpress_acting"));
            // 保存生成的 Prompt 文本到属性中
            this.InitialImpressionContext = impressionSb.ToString();
            return absoluteProgress;
        }

        public void InitializeGoal()
        {
            CalculationLog.AppendLine("\n--- 对方心理防线 (目标要价) 计算过程 ---");

            var template = NegotiationRegistry.GetGoalInfo(GoalType);

            // 设定目标分值：求婚可能很难(100)，贿赂守卫很简单(30)
            var memory = AllNpcMemoryManager.GetMemoryForAgent(TargetAgent);
            float npcValue = memory?._profile?.CalculateEstimatedValue() ?? 10000f;

            float baseDifficulty = template.BaseDifficulty;
            float finalThreshold = baseDifficulty;

            CalculationLog.AppendLine($"[基础] 谈判类型：{GoalType}，基础难度值：{baseDifficulty}");
            CalculationLog.AppendLine($"[参考] 目标估值(NPC Value)：{npcValue:F0}");

            // 场景 A: 求婚 (ProposeMarriage)
            if (GoalType == NegotiationGoalType.ProposeMarriage)
            {
                // 1. 对方身价越高，难度越高
                finalThreshold *= npcValue;
                CalculationLog.AppendLine($"[求婚] 更改人身依附，进行身价系数修正：x{npcValue:F2} ，修正后{finalThreshold}");

                // 2. 门当户对 (Clan Tier Check)
                if (TargetHero != null && Hero.MainHero.Clan != null)
                {
                    int targetTier = TargetHero.Clan?.Tier ?? 0;
                    int playerTier = Hero.MainHero.Clan.Tier;

                    // 如果对方阶级比你高，难度激增
                    if (targetTier > playerTier)
                    {
                        float tierFactor = (1 + (targetTier - playerTier) * 0.5f);
                        finalThreshold *= tierFactor;
                        CalculationLog.AppendLine($"[阶级] 对方家族阶级({targetTier})高于玩家({playerTier})，系数修正：x{tierFactor:F2}，修正后{finalThreshold}");
                    }

                    // 3. 双方关系 (如果是仇人，极难求婚)
                    int relation = TargetHero.GetRelation(Hero.MainHero);
                    if (relation < 0)
                    {
                        finalThreshold *= 2.0f;
                        CalculationLog.AppendLine($"[关系] 关系恶劣({relation})，系数修正：x2.0,，修正后{finalThreshold}");
                    }

                    // 4. 检查是否已婚 (逻辑死锁)
                    if (TargetHero.Spouse != null)
                    {
                        finalThreshold *= 10f; // 基本上不可能，除非你能在谈判中先让她离婚
                        // 本地化：LWN_nego_goal_married_hint（求婚已婚提示，双桶；CN 保留前导空格）
                        PlayerGoalDescription += LWNTextHelper.ResolvePrompt("LWN_nego_goal_married_hint");
                        CalculationLog.AppendLine($"[状态] 对方已婚，系数修正：x10.0 ，修正后{finalThreshold}");
                    }
                }
            }
            // 场景 B: 招募 (Recruit)
            else if (GoalType == NegotiationGoalType.RecruitHero)
            {
                // 招募难度直接等于 "买下这个人" 的概念
                // 这里用数值除以一个系数，转化为进度条血量
                finalThreshold *= npcValue;
                CalculationLog.AppendLine($"[招募] 更改人身依附，进行身价系数修正：x{npcValue:F2} ，修正后{finalThreshold}");
                // 忠诚度修正
                if (TargetHero != null && TargetHero.Clan != null)
                {
                    // 如果是国王，几乎不可能招募
                    if (TargetHero.IsFactionLeader)
                    {
                        finalThreshold *= 5f;
                        CalculationLog.AppendLine($"[身份] 对方是阵营领袖，系数修正：x5.0，修正后{finalThreshold}");
                    }
                }
            }
            // 场景 C: 通融/放行 (Enter Settlement)
            else if (GoalType == NegotiationGoalType.EnterSettlement)
            {
                // 看门卫的等级，或者城市的繁荣度
                if (TargetAgent.IsHero)
                {
                    finalThreshold = 200f; // 领主守门，很难
                    CalculationLog.AppendLine($"[身份] 守门员是英雄/领主，难度重置为：200");
                }
                else
                {
                    finalThreshold = 50f; // 普通小兵，简单
                    CalculationLog.AppendLine($"[身份] 守门员是普通士兵，难度重置为：50");
                }
            }
            else
            {
                // 默认逻辑
                CalculationLog.AppendLine($"[默认] 使用基础难度，无额外修正");
            }
            //如果是人身依附类需求，不能超过玩家的身价
            //之后如果索要城池，应该更多，之后在处理
            if(finalThreshold > npcValue)
            {
                CalculationLog.AppendLine($"价码无法超过玩家自身身价，需要修正为身价{npcValue}    ");
            }

            TargetThreshold = MathF.Clamp(finalThreshold, 10f, MathF.Max(100000.0f,npcValue));
            
            //PlayerGoalDescription = template.Description;
            MaxTurns = 5;

            CalculationLog.AppendLine($"--- 最终目标要价(Threshold)：{TargetThreshold:F1} ");
        }

    }
    // ----------------------------------------------------------------
    // 5. LLM 输出协议 (LLM Response Schema)
    // ----------------------------------------------------------------
    // 大模型必须严格返回这个 JSON 格式

    public class LLMResponse_Opening
    {
        [JsonProperty("npc_reply")]
        public string NpcReply { get; set; }

        [JsonProperty("npc_emotion")]
        public string NpcEmotion { get; set; }

        [JsonProperty("npc_thinking")]
        public string NpcThinking { get; set; } // 如果结束，返回结论

        [JsonProperty("npc_action")]
        public string NpcAction { get; set; }

        // 对应 Prompt 中的 player_next_options
        [JsonProperty("player_next_options")]
        public List<SkillCheckOption> PlayerNextOptions { get; set; }
    }

    public class LLMResponse_Casual
    {
        [JsonProperty("npc_reply")]
        public string NpcReply { get; set; }

        [JsonProperty("npc_emotion")]
        public string NpcEmotion { get; set; }

        [JsonProperty("npc_thinking")]
        public string NpcThinking { get; set; } // 如果结束，返回结论

        [JsonProperty("npc_action")]
        public string NpcAction { get; set; }

        // 🔴 2026-08-10（im-command-action-upgrade.md §5.1/§六）：IM 闲聊动作升级字段——
        // action_target = defender **名字文本**（不是 StringId，铁律 2：LLM 输出 ID 不可信，C# 解析）；
        // action_level = 档位（small/medium/large，仅关系/金额类用，LLM 只选档位不选数值）。
        [JsonProperty("action_target")]
        public string ActionTarget { get; set; }

        [JsonProperty("action_level")]
        public string ActionLevel { get; set; }

        // 🔴 2026-08-12（合并闲聊/计划模式）：NeedPlan/AdjustPlan 判定字段——bool 默认 false，
        // 解析失败/缺字段天然安全（铁律 2）。
        // need_plan：玩家这句是「需要实际行动的多步请求」→ 回复消息底部挂「制定计划/先不用」按钮；
        // adjust_plan：仅执行期 prompt 注入【当前计划执行中】段时有效——玩家明确要求改变正在执行的做法
        // → 复用 RequestModify 出「修改版」卡片；问进度/闲聊/催促一律 false；true 时 need_plan 必须 false。
        [JsonProperty("need_plan")]
        public bool NeedPlan { get; set; }

        [JsonProperty("adjust_plan")]
        public bool AdjustPlan { get; set; }

        // 🔴 2026-08-14（npc-risk-aware-planning.md M4 风险审视）：每次玩家提出要求（命令）时，
        // 回复轮 LLM 扩展两个字段——risk_analysis = 对局势的判断（1-2 句，含依据：人多人少/谁在看着/
        // 打不打得过/被抓会怎样）；risk_verdict = feasible | plan_needed | risky | refuse。
        // 深度分级：L0 低风险命令无【目之所及】段 → 无此二字段 → 裁决默认 feasible（现状直发）。
        [JsonProperty("risk_analysis")]
        public string RiskAnalysis { get; set; }
        [JsonProperty("risk_verdict")]
        public string RiskVerdict { get; set; }
        // 🔴 2026-08-16（方案 I3 观察出口）：need_fact——LLM 想引用某数值但【此刻现状】没有 →
        // 声明缺失事实。C# 只记 [StaleFact] 日志（对齐 [Bragging] 观察驱动），v1 不做任何动作
        //（不补查、不重试）；数据积累后再决定扩注入窗口/关键词表。
        [JsonProperty("need_fact")]
        public string NeedFact { get; set; }
        [JsonProperty("suggest_negotiation_start")]
        public bool SuggestNegotiationStart { get; set; }

        [JsonProperty("detected_nogotiation_goal")]
        public string DetectedNegotiationGoal { get; set; } // e.g., "Propose", "Bribe"

        [JsonProperty("detected_player_goal_desc")]
        public string DetectedPlayerGoalDesc { get; set; } // e.g., "Propose", "Bribe"

        // 对应 Prompt 中的 player_next_options
        [JsonProperty("player_next_options")]
        public List<NegotiationCard> PlayerNextOptions { get; set; }
    }


    public class LLMResponse_Negotiation
    {
        // 对上一轮玩家行为的反馈
        [JsonProperty("npc_reply")]
        public string NpcReply { get; set; }

        [JsonProperty("npc_emotion")]
        public string NpcEmotion { get; set; }

        [JsonProperty("npc_action")]
        public string NpcAction { get; set; } // e.g., "皱眉", "收下钱袋"

        // 数值计算结果
        [JsonProperty("delta_multiplier")]
        public float DeltaMultiplier { get; set; } // 命中弱点的乘数
        

        [JsonProperty("npc_thinking")]
        public string NpcThinking { get; set; } // 如果结束，返回结论

        // 动态调整阻力 (LLM 发现 NPC 被激怒了，可以新增一个阻力 Tag)

        // 下一轮生成的 3-4 张卡牌
        [JsonProperty("next_round_cards")]
        public List<NegotiationCard> NextRoundCards { get; set; }
    }
}
