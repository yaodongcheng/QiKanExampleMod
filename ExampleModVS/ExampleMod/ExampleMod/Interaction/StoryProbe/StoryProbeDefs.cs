using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>四家势力（叙事里叫「公会 / 会 / 剧团 / 帮」，数据上只是一个 id + 一个关系账）。</summary>
    public sealed class StoryFactionDef
    {
        public string Id;              // fixers / nightwalkers / players / dockhands
        public string NameKey;
        public string NameFallback;
        public string FactionIdOnHero; // 给代表英雄打的关系标记（仅日志用）
    }

    /// <summary>
    /// 一个人对钱的态度（2026-11-13 用户裁定：钱不能是万能钥匙）。
    ///   · <see cref="Open"/>   —— 厚脸皮收：当面递过去就接，明码标价。
    ///   · <see cref="Covert"/> —— 不肯光明正大地收：当面递钱会被挡回来，要走"正经买卖"（当、买）才收。
    ///   · <see cref="Disdain"/> —— 不屑：给钱是侮辱，只认你办的事。
    /// 🔴 判断只读这张表，**不在对话里各写各的**（换人只改一行）。
    /// </summary>
    public enum MoneyAttitude { Open, Covert, Disdain }

    /// <summary>一个城门口 NPC 的静态定义（数值 / 模板 / 站位 / 委托 / 奖励 / 随身道具）。</summary>
    public sealed class StoryNpcDef
    {
        public string Id;
        public string NameKey;
        public string NameFallback;
        public string IdentityKey, IdentityFallback;
        public string BackgroundKey, BackgroundFallback;
        public string TemperamentKey, TemperamentFallback;
        public string SpecialtyKey, SpecialtyFallback;
        public string GoalKey, GoalFallback;
        public string TroubleKey, TroubleFallback;
        public string PlayerHelpKey, PlayerHelpFallback;
        // 交差时给出的**原版属性**永久加值（2026-11-19 用户裁定）：键 = Vigor/Cunning/Social…，值 = 点数。
        // 例：屠户教发力 → Vigor +1（检定口径的力量随之 +10）。靠交差收据防重复。
        public Dictionary<string, int> AttributeGrants;
        public string ReactiveRole;
        public ReactivePersonality Personality;
        public string Faction;          // StoryFactionDef.Id
        public int Force, Guile, Charm; // §8.2 三维（0..100，直接填）
        public string TemplateId;       // 原版 NPCCharacter 模板（两轮策略查找）
        public string[] TemplateCandidates;
        public bool Female;             // ⚠️ 死字段：性别实际由模板 is_female 决定，代码不读它
        /// <summary>
        /// 年龄。🔴 **必须填**，-1 不是"模板默认"：HeroCreator.CreateNewHero 对 -1 的实现是
        /// `HeroComesOfAge(18) + RandomInt(30)`= **随机 18~47 岁**（1.2.12 反编译实证）。
        /// 填了才生效：hero.BodyProperties 用 DynamicBodyProperties(Age,...) ⇒ 脸按这个年龄长，
        /// ≥40 岁还会由 ModifyAppearanceByTraits 偏向灰发/胡子。
        /// </summary>
        public int Age = -1;

        /// <summary>站位（相对玩家开场点的本地偏移：+F 朝城门方向，+R 右手侧）。</summary>
        public float OffsetForward;
        public float OffsetRight;

        public string QuestTargetNpcId; // 委托仇家（null = 无委托）
        /// <summary>委托详情文案键（任务 HUD / 任务日志里那句「要做什么」；空 = 用标题 + 目标名的兜底模板）。</summary>
        public string QuestObjectiveKey;
        /// <summary>委托必需物品：玩家背包拿到它 = 目标达成（买/偷/打倒搜刮都算）；空 = 只认「打倒目标」。</summary>
        public string QuestItemId;
        public string[] RewardItems;    // 交差奖励物品 id
        /// <summary>出场时带在身上的物品（只填空武器槽，不改已有装备）；剧情道具/赃物的来源。</summary>
        public string[] CarryItems;
        public string QuestLogKey;      // 任务日志标题键
        public string AttrItemId;       // 随身属性道具（偷到 +1 技能）
        public string AttrSkill;        // 对应技能（Charm / Roguery / Vigor / OneHanded / Trade）
        public bool IsBlocker;          // 守门人：不走委托，走五条过门路
        /// <summary>P2：首遇阶段的登记守卫（两名）。位置 = 守门人刷新点 + 本地偏移。</summary>
        public bool FirstMeetGuard;
        public float FirstMeetOffsetForward;
        public float FirstMeetOffsetRight;
        /// <summary>生成时用平民装（true，默认）还是战斗装（false）。</summary>
        public bool Civilian = true;

        // ── 对钱的态度（见 MoneyAttitude 注释）──
        public MoneyAttitude Money = MoneyAttitude.Disdain;
        public int MoneyPrice;              // Open 的价；Covert 用 MoneyCovertPrice；Disdain 不收钱
        public int MoneyCovertPrice;        // Covert：走"正经买卖"时的价（通常更贵——他多担一层风险）
        public string MoneyHintKey;         // 收了钱之后多给的那句实话（本地化键）
        public string MoneyHintFallback;
    }

    /// <summary>常驻动作（§7.2）：每人 2~3 条原版动作名 + 吆喝台词键，8~20 秒轮换。</summary>
    public sealed class StreetLifeDef
    {
        public string NpcId;
        public string Listener;
        public string[] Actions;
        public string[] LineKeys;
        public string[] LineFallbacks;
        public List<StreetSpeechDef> Speech = new List<StreetSpeechDef>();
        public float SpeechRadiusMeters = 10f;
        public float SpeechFirstDelayMinSec = 2f;
        public float SpeechFirstDelayMaxSec = 8f;
        public float SpeechGapMinSec = 12f;
        public float SpeechGapMaxSec = 20f;
    }

    /// <summary>日常发话的状态分支；没有Listener表示自言自语，不建立注视目标。</summary>
    public sealed class StreetSpeechDef
    {
        public List<string> Requires = new List<string>();
        public string Listener;
        public string[] LineKeys;
        public string[] LineFallbacks;
    }

    /// <summary>开场事件（§6.1）：双人冲突 / 单人发愁，开演后自循环不散场。</summary>
    public sealed class StoryEventDef
    {
        public int Index;               // 1..6
        public string Key => "event_" + Index;
        public bool TwoPerson;
        public string ActorA;           // 主要说话人（单人事件 = 唯一的人）
        public string ActorB;           // 对手（双人事件）
        public string[] LineKeys;       // 交替台词（A,B,A,B…）
        public string[] LineFallbacks;
        public string[] ActionA;        // 循环动作
        public string[] ActionB;
        public List<string> Members = new List<string>();   // 群组全部成员（支持第三人及以上）
        public List<string> Speakers = new List<string>();  // 与 LineKeys 平行；空 = 按 Members 轮转
        public List<string> Listeners = new List<string>(); // 与 LineKeys 平行，真实听话对象
        public string HostNpcId;        // 玩家接入时跟谁谈（= 事件节点所属 NPC）
        public string JoinNode;         // "event_<n>_join"
        public string Placement = "gather"; // standing = 沿用布局初始点位/方向；gather = 原有聚拢
    }

    /// <summary>
    /// 序章的全部静态数据（数值 / 站位 / 台词键 / 事件表）。**加一条 = 加一行**，业务代码不写死。
    /// 说明：台词文本在 Languages XML（LWN_story_*），这里只放键；fallback 供 XML 缺失时兜底。
    /// </summary>
    public static class StoryProbeDefs
    {
        // ═══════════════════════════════════════════════════════════════
        // 数据（正本 = ModuleData/DesignData/StoryProbeCast.json）
        //
        // 🔴 这几张表**一个数都不许再写回 C#**：
        //    加人 / 改价码 / 换模板 / 加一场戏 / 换动作 = 改 JSON，不重编译。
        //    读不到文件会打 Error 日志并返回空表（宁可显式坏，不要静默用错数据）。
        // ═══════════════════════════════════════════════════════════════

        private static StoryProbeCastData _cast;

        private static readonly List<StoryFactionDef> EmptyFactions = new List<StoryFactionDef>();
        private static readonly List<StoryNpcDef> EmptyNpcs = new List<StoryNpcDef>();
        private static readonly List<StreetLifeDef> EmptyStreetLife = new List<StreetLifeDef>();
        private static readonly List<StoryEventDef> EmptyEvents = new List<StoryEventDef>();

        /// <summary>强制重读 `StoryProbeCast.json`（改表后用 `custom.story_probe reload` 生效）。</summary>
        public static void ReloadCast()
        {
            _cast = null;
            StoryItemBonuses.Reload();
            var _ = Cast;
        }

        private static StoryProbeCastData Cast
        {
            get
            {
                if (_cast == null) _cast = StoryProbeCastData.Load();
                return _cast;
            }
        }

        public static IReadOnlyList<StoryFactionDef> Factions => Cast?.Factions ?? EmptyFactions;
        public static IReadOnlyList<StoryNpcDef> Npcs => Cast?.Npcs ?? EmptyNpcs;
        public static IReadOnlyList<StreetLifeDef> StreetLife => Cast?.StreetLife ?? EmptyStreetLife;
        public static IReadOnlyList<StoryEventDef> Events => Cast?.Events ?? EmptyEvents;

        public static StoryFactionDef GetFaction(string id)
            => string.IsNullOrEmpty(id) ? null : Factions.FirstOrDefault(f => f.Id == id);

        public static StoryNpcDef GetNpc(string id)
            => string.IsNullOrEmpty(id) ? null : Npcs.FirstOrDefault(n => n.Id == id);

        /// <summary>四家代表（有委托的 4 个）；守门人与仇家不算。</summary>
        public static IEnumerable<StoryNpcDef> Representatives
            => Npcs.Where(n => n.QuestTargetNpcId != null);

        /// <summary>守门人（`IsBlocker = true` 的那一个）。</summary>
        public static StoryNpcDef Blocker => Npcs.FirstOrDefault(n => n.IsBlocker);

        /// <summary>
        /// 仇家：**被别人当成委托目标的**那些人（自己不派委托、也不是守门人）。
        /// 🔴 从表里推出来，不在代码里再列一遍名单 —— 加一个仇家 = JSON 里加一个人 + 有人指他。
        /// </summary>
        public static IEnumerable<StoryNpcDef> QuestTargets
            => Npcs.Where(n => string.IsNullOrEmpty(n.QuestTargetNpcId) && !n.IsBlocker
                               && Npcs.Any(o => o.QuestTargetNpcId == n.Id));

        /// <summary>四家的代表（每个势力一个，用于把关系记到具体 Hero 身上）。</summary>
        public static StoryNpcDef GetNpcByFaction(string factionId)
        {
            if (string.IsNullOrEmpty(factionId)) return null;
            return Npcs.FirstOrDefault(n => n.Faction == factionId && n.QuestTargetNpcId != null)
                ?? Npcs.FirstOrDefault(n => n.Faction == factionId);
        }

        public static StreetLifeDef GetStreetLife(string npcId)
            => string.IsNullOrEmpty(npcId) ? null : StreetLife.FirstOrDefault(s => s.NpcId == npcId);

        public static StoryEventDef GetEvent(int index)
            => Events.FirstOrDefault(e => e.Index == index);

        // ═══════════════════════════════════════════════════════════════
        // 玩家属性（§8.1：玩家某类属性 vs NPC 同一类属性；d20 掷点 ≥ 门槛）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>玩家三维 = 原版三个属性 ×10（1..10 → 10..100），与 NPC 手写的 0..100 同一把尺。
        /// 2026-11-19 用户裁定：基础值只认原版属性；道具的临时加成另算（见 <see cref="PlayerItemBonus"/>）。</summary>
        public static float PlayerAttrBase(StoryAbility ability)
        {
            try
            {
                Hero hero = Hero.MainHero;
                if (hero == null) return 50f;
                switch (ability)
                {
                    case StoryAbility.Force:
                        return MathF.Clamp(hero.GetAttributeValue(DefaultCharacterAttributes.Vigor) * 10f, 0f, 100f);
                    case StoryAbility.Guile:
                        return MathF.Clamp(hero.GetAttributeValue(DefaultCharacterAttributes.Cunning) * 10f, 0f, 100f);
                    case StoryAbility.Charm:
                        return MathF.Clamp(hero.GetAttributeValue(DefaultCharacterAttributes.Social) * 10f, 0f, 100f);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] PlayerAttr({ability}) failed: {ex.Message}");
            }
            return 50f;
        }

        /// <summary>持有道具给该属性的临时加成（2026-11-19 用户裁定）：背包里有、或正穿在身上即生效；
        /// **同一属性只取持有物里加成最高的一件**，不叠加。数值表 = StoryItemBonuses.json。</summary>
        public static int PlayerItemBonus(StoryAbility ability)
        {
            var items = StoryItemBonuses.Items;
            if (items == null || items.Count == 0) return 0;
            Hero hero = Hero.MainHero;
            if (hero == null) return 0;
            var roster = hero.PartyBelongedTo?.ItemRoster;
            int best = 0;
            foreach (var entry in items)
            {
                var def = entry.Value;
                if (def == null || def.Bonus <= 0) continue;
                if (!TryParseAbility(def.Ability, out var entryAbility) || entryAbility != ability) continue;
                if (!HoldsItem(hero, roster, entry.Key)) continue;
                if (def.Bonus > best) best = def.Bonus;
            }
            return best;
        }

        private static bool HoldsItem(Hero hero, ItemRoster roster, string itemId)
        {
            var item = FindItem(itemId);
            if (item != null && roster != null && roster.GetItemNumber(item) > 0) return true;
            // 穿在身上也算持有（贵族戏服这类必须实际穿戴才算数）；装备槽固定 12 个。
            var worn = hero.BattleEquipment;
            if (worn != null)
                for (int i = 0; i < 12; i++)
                    if (worn[(EquipmentIndex)i].Item?.StringId == itemId) return true;
            worn = hero.CivilianEquipment;
            if (worn != null)
                for (int i = 0; i < 12; i++)
                    if (worn[(EquipmentIndex)i].Item?.StringId == itemId) return true;
            return false;
        }

        /// <summary>检定用的合计值 = 原版属性 + 道具临时加成（上限 100）。</summary>
        public static float PlayerAttr(StoryAbility ability)
            => MathF.Clamp(PlayerAttrBase(ability) + PlayerItemBonus(ability), 0f, 100f);

        /// <summary>悬停要同时给出「永久 + 临时」两段数值。</summary>
        public static void PlayerAttrParts(StoryAbility ability, out float baseValue, out int bonus)
        {
            baseValue = PlayerAttrBase(ability);
            bonus = PlayerItemBonus(ability);
        }

        /// <summary>数据里的原版属性名 → CharacterAttribute（交差加属性用）。</summary>
        public static bool TryParseCharacterAttribute(string name, out CharacterAttribute attribute)
        {
            attribute = null;
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "vigor": attribute = DefaultCharacterAttributes.Vigor; return true;
                case "control": attribute = DefaultCharacterAttributes.Control; return true;
                case "endurance": attribute = DefaultCharacterAttributes.Endurance; return true;
                case "cunning": attribute = DefaultCharacterAttributes.Cunning; return true;
                case "social": attribute = DefaultCharacterAttributes.Social; return true;
                case "intelligence": attribute = DefaultCharacterAttributes.Intelligence; return true;
                default: return false;
            }
        }

        public static string AttributeDisplayKey(string name)
            => "LWN_story_attr_" + (name ?? "").Trim().ToLowerInvariant();

        /// <summary>查单件道具的属性加成（不管玩家有没有；用于拿到的提示）。</summary>
        public static bool TryGetItemBonus(string itemId, out StoryAbility ability, out int bonus)
        {
            ability = default;
            bonus = 0;
            var items = StoryItemBonuses.Items;
            if (items == null || string.IsNullOrEmpty(itemId) || !items.TryGetValue(itemId, out var def)
                || def == null || def.Bonus <= 0) return false;
            if (!TryParseAbility(def.Ability, out ability)) return false;
            bonus = def.Bonus;
            return true;
        }

        public static float NpcAttr(string npcId, StoryAbility ability)
        {
            var def = GetNpc(npcId);
            if (def == null) return 50f;
            switch (ability)
            {
                case StoryAbility.Force: return def.Force;
                case StoryAbility.Guile: return def.Guile;
                default: return def.Charm;
            }
        }

        // 2026-11-19 用户裁定：随机检定不设门槛 —— 属性差就体现为成功率低（可为 0），
        // 不再要求"先攒多少训练才能尝试"。MinimumGrowth 字段保留仅为兼容旧数据，不再拦人。

        private static readonly Dictionary<string, StoryAbility> DialogueCapabilities =
            new Dictionary<string, StoryAbility>(StringComparer.Ordinal)
            {
                { "Intimidate", StoryAbility.Force },
                { "Persuade", StoryAbility.Charm }, { "Mediate", StoryAbility.Charm },
                { "Bargain", StoryAbility.Charm }, { "Impersonate", StoryAbility.Charm },
                { "Deceive", StoryAbility.Charm }, { "Reason", StoryAbility.Charm },
            };

        public static bool TryGetCapability(string id, out StoryAbility ability)
        {
            ability = default;
            return id != null && DialogueCapabilities.TryGetValue(id, out ability);
        }

        public static string CapabilityName(string capability)
            => LWNTextHelper.ResolveText("LWN_story_capability_" + capability, capability);

        public static bool TryParseDialogueTactic(string text, out NegotiationTactic tactic)
        {
            tactic = default;
            return !string.IsNullOrWhiteSpace(text)
                && Enum.GetNames(typeof(NegotiationTactic)).Any(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase))
                && Enum.TryParse(text, true, out tactic);
        }

        /// <summary>纯查询：正式能力取三属性，旧定义保持技能口径；共用SimpleChance，不掷骰。</summary>
        public static StoryRoll PreviewDialogueCheck(string npcId, NegotiationTactic tactic, string capability = null, int minimumGrowth = 0)
        {
            var host = StoryProbeMissionBehavior.Current;
            var agent = host?.GetAgent(npcId);
            var hero = host?.GetHero(npcId);
            if (Hero.MainHero == null || (agent == null && hero == null)) return null;
            if (!string.IsNullOrEmpty(capability))
            {
                if (!TryGetCapability(capability, out var ability)) return null;
                // 永久（原版属性）+ 临时（持有道具）两段分开算：悬停要分别显示，检定用合计。
                PlayerAttrParts(ability, out float paBase, out int paBonus);
                float pa = MathF.Clamp(paBase + paBonus, 0f, 100f);
                float na = NpcAttr(npcId, ability);
                // 随机检定不设门槛：属性差多少就体现在成功率上，0% 也照实显示。
                float chance = SingleRollResolver.SimpleChance(pa, na);
                return new StoryRoll
                {
                    Ability = ability, Capability = capability, NpcId = npcId, Tactic = tactic.ToString(),
                    PlayerAttr = pa, PlayerBonus = paBonus, NpcAttr = na, Chance = chance, Threshold = 1f - chance,
                    Eligible = true, MinimumGrowth = 0, Growth = 0
                };
            }
            var context = new IntentContext(agent, speaker: hero);
            var skill = SkillCheckSystem.MapTacticToSkill(tactic);
            var computed = SingleRollResolver.SimpleCompute(context, tactic);
            return new StoryRoll
            {
                NpcId = npcId,
                Tactic = tactic.ToString(),
                Skill = skill,
                PlayerAttr = Hero.MainHero.GetSkillValue(skill),
                NpcAttr = context.Speaker?.GetSkillValue(skill) ?? 50f,
                Chance = computed.Chance,
                Threshold = 1f - computed.Chance,
            };
        }

        public static StoryRoll RollDialogueCheck(string npcId, NegotiationTactic tactic, string capability = null, int minimumGrowth = 0)
        {
            var result = PreviewDialogueCheck(npcId, tactic, capability, minimumGrowth);
            if (result == null) return null;
            if (!result.Eligible) return result; // 资格不足不掷骰，也不消费调试覆盖值。
            // 沿用原有三类覆盖命令，但实际概率和技能完全取共享对话轮子。
            string legacy = result.Capability != null ? AbilityId(result.Ability) : result.Skill == DefaultSkills.Charm ? "charm"
                : result.Skill == DefaultSkills.Roguery ? "guile" : null;
            result.Roll = TakeCheckRoll(result.Capability ?? tactic.ToString(), legacy);
            result.Success = SingleRollResolver.Roll(result.Chance, result.Roll);
            DebugLogger.Log("[StoryDialogueCheck] resolver=SimpleCompute " + result.Describe());
            return result;
        }

        private static float TakeCheckRoll(string kind, string legacy = null)
        {
            string want = StoryProbeState.Data.RollOverrideAbility;
            if (StoryProbeState.Data.RollOverride >= 0f && (string.IsNullOrEmpty(want)
                || string.Equals(want, kind, StringComparison.OrdinalIgnoreCase)
                || (legacy != null && string.Equals(want, legacy, StringComparison.OrdinalIgnoreCase))))
            {
                float value = MathF.Clamp(StoryProbeState.Data.RollOverride / 100f, 0f, 1f);
                StoryProbeState.Data.RollOverride = -1f;
                StoryProbeState.Data.RollOverrideAbility = "";
                return value;
            }
            return MBRandom.RandomFloat;
        }

        /// <summary>真实潜行/翻墙的旧属性检定；正式对话改用RollDialogueCheck。</summary>
        public static StoryRoll RollCheck(string npcId, StoryAbility ability)
        {
            var def = GetNpc(npcId);
            string id = def?.Id ?? npcId;

            float pa = PlayerAttr(ability);
            float na = NpcAttr(npcId, ability);
            float chance = AgentStatsHelper.ComputeOpposedSuccessRate(pa, na, maxRate: 0.95f);

            var r = new StoryRoll
            {
                Ability = ability,
                NpcId = id,
                PlayerAttr = pa,
                NpcAttr = na,
                Chance = chance,
                Threshold = 1f - chance,
            };

            r.Roll = TakeCheckRoll(AbilityId(ability));
            // 🔴 唯一掷骰入口（铁律 17：d20 风格统一走 SingleRollResolver.Roll 的成功判据）
            r.Success = SingleRollResolver.Roll(chance, r.Roll);

            DebugLogger.Log($"[StoryProbe] check {id}/{ability}: player={pa:F0} npc={na:F0} chance={chance:P0} " +
                            $"roll={r.Roll:P0} threshold={r.Threshold:P0} → {(r.Success ? "success" : "fail")}");
            return r;
        }

        /// <summary>能力 → 中文展示名（只给控制台/日志用；玩家可见文本走 LWN_story_ability_*）。</summary>
        public static string AbilityId(StoryAbility a)
        {
            switch (a)
            {
                case StoryAbility.Charm: return "charm";
                case StoryAbility.Guile: return "guile";
                default: return "force";
            }
        }

        public static bool TryParseAbility(string text, out StoryAbility ability)
        {
            ability = StoryAbility.Charm;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().ToLowerInvariant();
            if (t == "charm" || t == "魅力" || t == "chs" || t == "1") { ability = StoryAbility.Charm; return true; }
            if (t == "guile" || t == "技巧" || t == "rogue" || t == "2") { ability = StoryAbility.Guile; return true; }
            if (t == "force" || t == "武力" || t == "fight" || t == "3") { ability = StoryAbility.Force; return true; }
            return false;
        }

        public static string AbilityDisplayKey(StoryAbility a)
            => a == StoryAbility.Charm ? "LWN_story_ability_charm"
             : a == StoryAbility.Guile ? "LWN_story_ability_guile"
             : "LWN_story_ability_force";

        // ═══════════════════════════════════════════════════════════════
        // 物品查找（铁律 5：两轮策略，预设 id → 内存遍历）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>物品别名表 —— 正本在 `StoryProbeCast.json` 的 `ItemAliases` 段，代码里不再写死。</summary>
        private static string[] GetItemAliases(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var map = Cast?.ItemAliases;
            if (map == null) return null;
            return map.TryGetValue(id, out var aliases) ? aliases : null;
        }

        /// <summary>属性道具对应的技能对象（名字见 StoryNpcDef.AttrSkill）。查不到返回 null（不崩）。</summary>
        public static SkillObject ResolveSkill(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            switch (name.Trim().ToLowerInvariant())
            {
                case "charm": return DefaultSkills.Charm;
                case "roguery": return DefaultSkills.Roguery;
                case "trade": return DefaultSkills.Trade;
                case "onehanded": return DefaultSkills.OneHanded;
                case "vigor": return null;   // 属性不是技能：交差时只给物品
                default: return null;
            }
        }

        /// <summary>按 id 取物品：第一轮精确 id / 别名，第二轮按 id 子串在内存里遍历。</summary>
        public static ItemObject FindItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                var direct = MBObjectManager.Instance?.GetObject<ItemObject>(id);
                if (direct != null) return direct;

                var aliases = GetItemAliases(id);
                if (aliases != null)
                {
                    foreach (var alt in aliases)
                    {
                        var it = MBObjectManager.Instance?.GetObject<ItemObject>(alt);
                        if (it != null) return it;
                    }
                }

                // 第二轮：内存遍历（mod 屏蔽 id 时的兜底）
                var lower = id.ToLowerInvariant();
                return MBObjectManager.Instance?.GetObject<ItemObject>(x =>
                    x != null && x.StringId != null && x.StringId.ToLowerInvariant().Contains(lower));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] FindItem({id}) failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>预检：物品是否存在、数量是否合法（不落地）。</summary>
        public static bool CanGiveItem(string itemId, int count)
            => !string.IsNullOrEmpty(itemId) && count > 0 && FindItem(itemId) != null;

        /// <summary>回滚：把刚发出的物品从英雄身上收回（与 GiveItem 成对）。</summary>
        public static bool RemoveItem(Hero hero, string itemId, int count)
        {
            if (hero == null || string.IsNullOrEmpty(itemId) || count <= 0) return false;
            var item = FindItem(itemId);
            if (item == null) return false;
            try { return AgentControlHelper.TransferItems(hero, null, item, count) > 0; }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] RemoveItem({itemId}) failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>把物品装进玩家空武器槽（仅场景内有效；已装备的跳过，槽满只留背包）。
        /// 剧情给钩索这类"要玩家当场用"的东西必须走这里——只进背包滚轮切不出来（2026-11-20 实机）。</summary>
        public static void EquipOnPlayerHands(IEnumerable<ItemObject> items)
        {
            if (items == null) return;
            var mission = TaleWorlds.MountAndBlade.Mission.Current;
            var main = TaleWorlds.MountAndBlade.Agent.Main;
            if (mission == null || main == null || !AgentControlHelper.SafeIsActive(main)) return;
            bool civilian = false;
            try { civilian = mission.DoesMissionRequireCivilianEquipment; } catch { }
            var hero = Hero.MainHero;
            var heroEq = hero != null ? (civilian ? hero.CivilianEquipment : hero.BattleEquipment) : null;
            bool changed = false;
            foreach (var item in items)
            {
                if (item == null) continue;
                // 场景内：已经装在身上就不重复
                if (GrappleFirePatch.FindItemSlot(main, item.StringId) != EquipmentIndex.None) continue;
                EquipmentIndex slot = EquipmentIndex.None;
                for (var s = EquipmentIndex.WeaponItemBeginSlot; s < EquipmentIndex.NumPrimaryWeaponSlots; s++)
                    if (main.Equipment[s].IsEmpty) { slot = s; break; }
                if (slot == EquipmentIndex.None)
                { DebugLogger.Log("[StoryProbe] equip: no free weapon slot for " + item.StringId); continue; }
                try
                {
                    // 🔴 必须走引擎装备 API（同 `custom.grapple equip`）：直接改装备表只会"看着挂上了"，
                    //    武器实体没建 ⇒ 绳/钩发射不出去；钩的弹药数也要钉成 1（2026-11-20 实机）。
                    var weapon = new TaleWorlds.MountAndBlade.MissionWeapon(item, null, main.Origin?.Banner);
                    main.EquipWeaponWithNewEntity(slot, ref weapon);
                    if (item.StringId == GrappleFirePatch.HookItemId) main.SetWeaponAmountInSlot(slot, 1, false);
                    changed = true;
                }
                catch (Exception ex) { DebugLogger.Log("[StoryProbe] equip failed: " + ex.Message); continue; }
                // 同步进英雄的战役装备层：否则开一次物品栏，引擎按老装备重建，手上这件就没了。
                if (heroEq != null)
                {
                    for (var s = EquipmentIndex.WeaponItemBeginSlot; s < EquipmentIndex.NumPrimaryWeaponSlots; s++)
                        if (heroEq[s].IsEmpty) { heroEq[s] = new EquipmentElement(item); break; }
                }
            }
            if (!changed) return;
            try { main.UpdateAgentStats(); } catch { }
            DebugLogger.Log("[StoryProbe] equip: kit on player hands");
        }

        /// <summary>给英雄加物品（走 InventoryRoster，不碰场景）。返回是否成功。</summary>
        public static bool GiveItem(Hero hero, string itemId, int count = 1)
        {
            if (hero == null) return false;
            var item = FindItem(itemId);
            if (item == null)
            {
                DebugLogger.Log($"[StoryProbe] GiveItem: item '{itemId}' not found (skipped)");
                return false;
            }
            try
            {
                // 铁律 4：凭空奖励 = Grant（来源 null 显式标注虚空），统一走 AgentControlHelper
                int moved = AgentControlHelper.TransferItems(null, hero, item, count);
                if (moved > 0) return true;

                // 玩家没有 PartyBelongedTo（极少见：俘虏/特殊状态）→ 走主队背包
                if (hero.IsHumanPlayerCharacter && PartyBase.MainParty?.ItemRoster != null)
                {
                    PartyBase.MainParty.ItemRoster.AddToCounts(item, count);
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] GiveItem({itemId}) failed: {ex.Message}");
            }
            return false;
        }
    }

    /// <summary>一次属性对拼的结果（§8.3：只有成功/失败，不做大成功）。</summary>
    public sealed class StoryRoll
    {
        public StoryAbility Ability;
        public string Tactic;
        public string Capability;
        public SkillObject Skill;
        public string NpcId;
        public float PlayerAttr;
        public int PlayerBonus;   // 其中来自持有道具的临时加成（悬停显示成 "(+N)"）
        public float NpcAttr;
        public float Chance;      // 成功率（事前概率，可显示给玩家）
        public float Threshold;   // 门槛 = 1 − 成功率（铁律 17：只播报掷点 vs 门槛）
        public float Roll;
        public bool Success;
        public bool Eligible = true;
        public int MinimumGrowth, Growth;

        public string Describe()
            => $"{NpcId}/{Tactic ?? Ability.ToString()} capability={Capability ?? "legacy"} attribute={Ability} skill={Skill?.StringId ?? "attribute"} player={PlayerAttr:F0} npc={NpcAttr:F0} chance={Chance:P0} " +
               $"roll={Roll:P0} threshold={Threshold:P0} → {(Success ? "success" : "fail")}";

        /// <summary>玩家可见播报（铁律 17：只写「掷点 X% vs 门槛 Y%」，不写难度措辞）。</summary>
        public string PlayerMessage()
            => LWNTextHelper.ResolveCompound("LWN_story_check_result",
                "Roll {ROLL}% vs threshold {THRESHOLD}% — {RESULT}",
                ("ROLL", MathF.Round(Roll * 100f).ToString("F0")),
                ("THRESHOLD", MathF.Round(Threshold * 100f).ToString("F0")),
                ("RESULT", LWNTextHelper.ResolveText(
                    Success ? "LWN_story_check_success" : "LWN_story_check_fail",
                    Success ? "success" : "failed")));
    }

    // ── 剧情演员出场判据（2026-11-19 立，单一判据）──
    // 为什么要收口：旧存档的剧情演员会挂着上一座城的 CurrentSettlement，以前每个调用点各写一遍，
    // 于是"换城后整场布景判 missing"这种故障只能靠读日志才发现。
    public enum StoryCastAvailability
    {
        Ok = 0,
        /// <summary>本人不在任何队伍、只是挂着旧据点：按剧情演员放到当前场景。</summary>
        OkStaleSettlement = 1,
        Dead = 2,
        Detained = 3,
        InOtherParty = 4,
        Missing = 5
    }

    public static class StoryCastAvailabilityHelper
    {
        /// <summary>纯判据（可离线测试）：不得引入其他硬拒绝条件。</summary>
        public static StoryCastAvailability Evaluate(bool alive, bool prisoner, bool inOtherParty, bool staleSettlement)
        {
            if (!alive) return StoryCastAvailability.Dead;
            if (prisoner) return StoryCastAvailability.Detained;
            if (inOtherParty) return StoryCastAvailability.InOtherParty;
            return staleSettlement ? StoryCastAvailability.OkStaleSettlement : StoryCastAvailability.Ok;
        }

        public static bool IsRefused(StoryCastAvailability availability)
        {
            return availability == StoryCastAvailability.Dead
                || availability == StoryCastAvailability.Detained
                || availability == StoryCastAvailability.InOtherParty
                || availability == StoryCastAvailability.Missing;
        }

        public static string Describe(StoryCastAvailability availability)
        {
            switch (availability)
            {
                case StoryCastAvailability.Ok: return "ok";
                case StoryCastAvailability.OkStaleSettlement: return "ok(stale_settlement)";
                case StoryCastAvailability.Dead: return "dead";
                case StoryCastAvailability.Detained: return "detained";
                case StoryCastAvailability.InOtherParty: return "in_other_party";
                default: return "missing";
            }
        }

        /// <summary>引擎侧包装：只读英雄状态，不修改世界。</summary>
        public static StoryCastAvailability Evaluate(Hero hero, Settlement currentSettlement)
        {
            if (hero == null) return StoryCastAvailability.Missing;
            bool inOtherParty = hero.PartyBelongedTo != null && hero.PartyBelongedTo != MobileParty.MainParty;
            bool stale = hero.CurrentSettlement != null && hero.CurrentSettlement != currentSettlement;
            return Evaluate(hero.IsAlive, hero.IsPrisoner, inOtherParty, stale);
        }
    }
}
