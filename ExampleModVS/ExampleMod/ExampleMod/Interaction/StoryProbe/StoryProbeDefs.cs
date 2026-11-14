using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
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
        public string[] RewardItems;    // 交差奖励物品 id
        public string QuestLogKey;      // 任务日志标题键
        public string AttrItemId;       // 随身属性道具（偷到 +1 技能）
        public string AttrSkill;        // 对应技能（Charm / Roguery / Vigor / OneHanded / Trade）
        public bool IsBlocker;          // 守门人：不走委托，走五条过门路
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
        public string[] Actions;
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
        public string HostNpcId;        // 玩家接入时跟谁谈（= 事件节点所属 NPC）
        public string JoinNode;         // "event_<n>_join"
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

        /// <summary>玩家三类属性的 0..100 口径（与 NPC 三维同一把尺）。</summary>
        public static float PlayerAttr(StoryAbility ability)
        {
            try
            {
                Hero hero = Hero.MainHero;
                if (hero == null) return 50f;
                switch (ability)
                {
                    case StoryAbility.Force:
                        // Vigor + Control（各 1..10）→ 0..100
                        return MathF.Clamp((hero.GetAttributeValue(DefaultCharacterAttributes.Vigor)
                                          + hero.GetAttributeValue(DefaultCharacterAttributes.Control)) * 5f, 5f, 100f);
                    case StoryAbility.Guile:
                        return MathF.Clamp(hero.GetSkillValue(DefaultSkills.Roguery), 5f, 100f);
                    case StoryAbility.Charm:
                        return MathF.Clamp(hero.GetSkillValue(DefaultSkills.Charm), 5f, 100f);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] PlayerAttr({ability}) failed: {ex.Message}");
            }
            return 50f;
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

        /// <summary>成功率 → 门槛 → 掷点（d20：掷点 ≥ 门槛即成功；铁律 17）。</summary>
        public static StoryRoll RollCheck(string npcId, StoryAbility ability)
        {
            var def = GetNpc(npcId);
            string id = def?.Id ?? npcId;

            float pa = PlayerAttr(ability);
            float na = NpcAttr(npcId, ability);
            float chance = na > 0f ? pa / (pa + na) : 0.5f;
            chance = MathF.Clamp(chance, 0.05f, 0.95f);

            var r = new StoryRoll
            {
                Ability = ability,
                NpcId = id,
                PlayerAttr = pa,
                NpcAttr = na,
                Chance = chance,
                Threshold = 1f - chance,
            };

            // 命令强制掷点（验成功 / 失败）：只对**指定的那一门**生效，用一次即清
            float forced = -1f;
            string want = StoryProbeState.Data.RollOverrideAbility;
            if (StoryProbeState.Data.RollOverride >= 0f
                && (string.IsNullOrEmpty(want) || want == AbilityId(ability)))
            {
                forced = StoryProbeState.Data.RollOverride;
                StoryProbeState.Data.RollOverride = -1f;
                StoryProbeState.Data.RollOverrideAbility = "";
            }

            r.Roll = forced >= 0f ? MathF.Clamp(forced / 100f, 0f, 1f) : MBRandom.RandomFloat;
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
        public string NpcId;
        public float PlayerAttr;
        public float NpcAttr;
        public float Chance;      // 成功率（事前概率，可显示给玩家）
        public float Threshold;   // 门槛 = 1 − 成功率（铁律 17：只播报掷点 vs 门槛）
        public float Roll;
        public bool Success;

        public string Describe()
            => $"{NpcId}/{Ability} player={PlayerAttr:F0} npc={NpcAttr:F0} chance={Chance:P0} " +
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
}
