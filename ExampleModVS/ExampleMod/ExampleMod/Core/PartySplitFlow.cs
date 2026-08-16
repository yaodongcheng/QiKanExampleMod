using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-16（方案 J）：campaign 随从行为空间——分兵/归队执行流。
    /// SPLIT_PARTY 分兵跟随玩家：随从 Hero 移出 MainParty + 创建随从领导的独立 party
    ///（SafeLordPartyComponent——项目既有安全组件）+ 兵力划转（档位 small/medium/large ≈ 10/30/60 兵，
    /// 按队伍兵力上限钳制不抽空）+ 跟随 MainParty（V.GatherToPlayer = SetPartyAiAction escort 语义）。
    /// GATHER_TO_PLAYER 归队（方案 J 参数化）：随从独立 party → 合并回主队——兵力归还 MemberRoster、
    /// Hero 归队、销毁 party；非随从独立部队保持 escort 集结现状。
    /// 口嗨联动（方案 C）：动作注册后动作空间自然出现——LLM 声称"我去 X 城"时动作空间有对应码 →
    /// 有执行路径豁免 ✓；未注册的声称（"我去募兵"）→ 口嗨检测拦截 ✓（注册了才是真的，没注册就是吹牛）。
    /// </summary>
    public static class PartySplitFlow
    {
        /// <summary>分兵档位 → 兵力（small/medium/large ≈ 10/30/60；按队伍兵力上限钳制，不抽空）。</summary>
        private static int SplitAmount(string level)
        {
            string lv = level?.ToLowerInvariant();
            int want = lv == "small" ? 10 : (lv == "large" ? 60 : 30);
            var main = MobileParty.MainParty;
            if (main?.MemberRoster == null) return 0;
            int avail = main.MemberRoster.TotalRegulars;
            // 最多带走一半（不抽空主队）；少于 5 人不成军
            int take = Math.Min(want, avail / 2);
            return take >= 5 ? take : 0;
        }

        /// <summary>SPLIT_PARTY 核心执行（ExecuteCore——RequiresConfirm 卡片批准后直接跑）。</summary>
        public static void Execute(Hero hero, string level)
        {
            try
            {
                var main = MobileParty.MainParty;
                if (hero == null || main == null) return;
                if (hero.PartyBelongedTo != main)
                {
                    DebugLogger.Log($"[Party] SPLIT_PARTY {hero.Name} 不在主队（{hero.PartyBelongedTo?.StringId ?? "null"}）→ 降级 NONE");
                    return;
                }
                int take = SplitAmount(level);
                if (take <= 0)
                {
                    DebugLogger.Log($"[Party] SPLIT_PARTY {hero.Name} 兵力不足（主队 {main.MemberRoster.TotalRegulars} 人）→ 降级 NONE");
                    return;
                }
                // 1. 创建随从领导的独立 party（SafeLordPartyComponent——MyBehavior 同款安全组件）
                var component = new SafeLordPartyComponent(hero);
                MobileParty newParty = V.MakeParty($"split_{hero.StringId}_{MBRandom.RandomInt(10000)}", component);
                if (newParty == null)
                {
                    DebugLogger.Log($"[Party] SPLIT_PARTY {hero.Name} 创建 party 失败 → 降级 NONE");
                    return;
                }
                // 2. 位置 = 主队旁边
                V.SetPos(newParty, V.Pos(main));
                // 3. 初始化 + 清空模板塞的兵，再按档位划转（参考 MyBehavior.SpawnIndependentPartyInWilderness 模式）
                try
                {
                    var template = hero.Culture?.DefaultPartyTemplate;
                    if (template != null)
                        V.InitPartyPos(newParty, template, V.Pos(newParty));
                    newParty.MemberRoster.Clear();
                    newParty.PrisonRoster.Clear();
                }
                catch (Exception ex) { DebugLogger.Log($"[Party] SPLIT_PARTY 初始化失败（继续）: {ex.Message}"); }
                // 4. Hero 移入新 party
                main.MemberRoster.RemoveTroop(hero.CharacterObject, 1);
                newParty.MemberRoster.AddToCounts(hero.CharacterObject, 1);
                // 5. 兵力划转（普通兵从主队抽 take 个；按兵种从多的开始）
                int remaining = take;
                foreach (var el in main.MemberRoster.GetTroopRoster()
                    .Where(e => e.Character != null && !e.Character.IsHero && e.Number > 0)
                    .OrderByDescending(e => e.Number))
                {
                    if (remaining <= 0) break;
                    int n = Math.Min(el.Number, remaining);
                    main.MemberRoster.RemoveTroop(el.Character, n);
                    newParty.MemberRoster.AddToCounts(el.Character, n);
                    remaining -= n;
                }
                // 6. 跟随主队（escort——分兵随从率部跟随玩家，V.GatherToPlayer = SetPartyAiAction.EscortParty）
                try { V.GatherToPlayer(newParty); } catch (Exception ex) { DebugLogger.Log($"[Party] SPLIT_PARTY 跟随设置失败: {ex.Message}"); }
                try { newParty.SetPartyUsedByQuest(true); } catch { }
                try { newParty.Party.SetVisualAsDirty(); } catch { }
                // 🔴 2026-08-16（方案 L2）：分兵见闻旁白——第一人称亲历，写执行者本人记忆
                //（归队后【近期经历】段自然带出 → 玩家问「这趟怎么样」能答）
                AllNpcMemoryManager.GetMemory(hero.StringId)?.RecordNarration($"我领了一队人马离了主队，跟着主公走");
                DebugLogger.Log($"[Party] SPLIT_PARTY {hero.Name} 分兵成功（带走 {take} 兵，剩余主队 {main.MemberRoster.TotalRegulars}）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Party] SPLIT_PARTY 异常: {ex.Message}");
            }
        }

        /// <summary>GATHER_TO_PLAYER 归队合并（随从独立 party → 主队）：兵力归还 MemberRoster、
        /// Hero 归队、销毁 party。非随从独立部队（领主等）保持 escort 集结（调用方分流）。</summary>
        public static void MergeBack(Hero hero)
        {
            try
            {
                var main = MobileParty.MainParty;
                if (hero == null || main == null) return;
                var party = hero.PartyBelongedTo;
                if (party == null || party == main)
                {
                    DebugLogger.Log($"[Party] MERGE {hero.Name} 无独立 party → 降级 NONE");
                    return;
                }
                // 1. 兵力归还（普通兵）
                int moved = 0;
                foreach (var el in party.MemberRoster.GetTroopRoster())
                {
                    if (el.Character == null || el.Character.IsHero) continue;
                    int n = el.Number;
                    if (n <= 0) continue;
                    party.MemberRoster.RemoveTroop(el.Character, n);
                    main.MemberRoster.AddToCounts(el.Character, n);
                    moved += n;
                }
                // 2. Hero 归队（PartyBelongedTo 由引擎自动更新）
                party.MemberRoster.RemoveTroop(hero.CharacterObject, 1);
                main.MemberRoster.AddToCounts(hero.CharacterObject, 1);
                // 3. 销毁 party（V.DelParty 版本兼容）
                try { V.DelParty(party); } catch (Exception ex) { DebugLogger.Log($"[Party] MERGE 销毁 party 失败: {ex.Message}"); }
                // 🔴 2026-08-16（方案 L2）：归队见闻旁白（第一人称，写执行者本人）
                AllNpcMemoryManager.GetMemory(hero.StringId)?.RecordNarration("我带着队伍回来了，跟主公会合");
                DebugLogger.Log($"[Party] GATHER_TO_PLAYER {hero.Name} 归队合并（归还 {moved} 兵，主队现有 {main.MemberRoster.TotalRegulars}）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Party] MERGE 异常: {ex.Message}");
            }
        }

        /// <summary>独立 party 判定（分兵随从 = 不在主队、有独立 party 的玩家家族成员）。
        /// 🔴 禁止改 FriendlinessHelper.IsPlayerPartyMember 共享判定本身（全局行为变更，影响面不可控）——
        /// 认知/感知口径的分兵裁剪在注入组装层单独判断（本 helper 供裁剪用）。</summary>
        public static bool IsSplitPartyLeader(Hero hero)
        {
            try
            {
                if (hero == null) return false;
                return hero.PartyBelongedTo != null
                    && hero.PartyBelongedTo != MobileParty.MainParty
                    && hero.PartyBelongedTo.LeaderHero == hero
                    && hero.Clan == Clan.PlayerClan;
            }
            catch { return false; }
        }

        /// <summary>settlement 名 → Settlement（铁律 5 动态遍历 Settlement.All，无硬编码 ID；
        /// 匹配 Name/FirstName 忽略大小写；无命中 → null）。</summary>
        public static Settlement ResolveSettlementByName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var s in Settlement.All)
            {
                if (s == null) continue;
                string n = s.Name?.ToString();
                if (string.IsNullOrEmpty(n)) continue;
                if (n.Equals(text, StringComparison.OrdinalIgnoreCase)
                    || n.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    return s;
            }
            return null;
        }

        /// <summary>部队名/类型词 → MobileParty（铁律 5 动态遍历 MobileParty.All；可见性过滤 = 玩家视角
        /// IsVisible + 只追可见敌方/匪徒；匹配 party.Name/类型词；无命中 → null）。</summary>
        public static MobileParty ResolvePartyByName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var candidates = new List<MobileParty>();
            foreach (var p in MobileParty.All)
            {
                if (p == null || p.IsMainParty || p.IsGarrison) continue;
                try { if (!p.IsVisible) continue; } catch { continue; }
                // 敌我：只追可见敌方/匪徒（原版地图可见性 = 同行视角）
                try
                {
                    if (!p.IsBandit && !(p.MapFaction != null && p.MapFaction.IsAtWarWith(Clan.PlayerClan))) continue;
                }
                catch { continue; }
                string n = p.Name?.ToString() ?? "";
                if (string.IsNullOrEmpty(n)) continue;
                if (n.Equals(text, StringComparison.OrdinalIgnoreCase)
                    || n.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                    || ContainsTypeWord(text, p))
                    candidates.Add(p);
            }
            return candidates.OrderBy(p => V.Pos(p).DistanceSquared(V.Pos(MobileParty.MainParty))).FirstOrDefault();
        }

        private static bool ContainsTypeWord(string text, MobileParty p)
        {
            string low = text.Trim().ToLowerInvariant();
            if (low.Contains("匪徒") || low.Contains("强盗") || low.Contains("bandit")) return p.IsBandit;
            if (low.Contains("商队") || low.Contains("caravan")) return p.IsCaravan;
            if (low.Contains("农民") || low.Contains("农夫") || low.Contains("villager")) return p.IsVillager;
            if (low.Contains("民兵") || low.Contains("militia")) return p.IsMilitia;
            return false;
        }
    }
}
