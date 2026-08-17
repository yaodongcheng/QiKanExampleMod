using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 自动世界观提供者（2026-08-17 计划 world-background-auto-summary.md）：
    /// 读取（GetWorldSection 纯字符串查表，线程安全）+ 快照/指纹/生成 prompt 构建。
    /// 🔴 读取路径禁止引擎对象查找——PlanReplan 在 Task.Run 内构建 prompt，
    /// 引擎对象只读主线程；GetWorldSection 只读 <see cref="WorldBackgroundStore"/> 静态字段。
    /// 文化详情不预生成（每存档只跑一份生成）——对话时从 NPC 自身文化百科
    /// （CultureObject.EncyclopediaText，引擎原产）直接拼入 persona（NPCProfile 侧）。
    /// </summary>
    public static class WorldBackgroundProvider
    {
        /// <summary>世界格局段（全民同段，无身份裁剪）。blob 空 → 返回 ""（标题由调用方随内容条件化，
        /// 防止「【世界观】」标题残留）。heroId 暂不参与裁剪（保留参数供未来身份分级）。</summary>
        public static string GetWorldSection(string heroId)
        {
            return WorldBackgroundStore.Blob ?? "";
        }

        /// <summary>Agent 版便捷入口（respond 链调用点按 :822 同款模式取 hero；仅主线程调用）。</summary>
        public static string GetWorldSection(Agent agent)
        {
            if (agent == null) return "";
            return GetWorldSection((agent.Character as CharacterObject)?.HeroObject?.StringId);
        }

        /// <summary>
        /// 当前指纹：`culture:{StringId 序列}|kingdom:{StringId 序列}|hero:{关键英雄 StringId 序列}|lang:{语言 id}`。
        /// 序列排序保证顺序无关；hero 段与快照同口径（每王国 ≤3 关键英雄）——领袖更替/死亡 → 指纹变 →
        /// 重新生成（防 blob 点名已故在位者）。lang 口径 = GetReplyLanguageInstruction() 返回值
        /// （"English"/"简体中文"），禁止裸传 ActiveTextLanguage（与 prompt 语言指令口径错位会误重生成）。
        /// </summary>
        public static string GetFingerprint()
        {
            var sb = new StringBuilder();
            try
            {
                sb.Append("culture:");
                var cultures = MBObjectManager.Instance.GetObjectTypeList<CultureObject>()
                    .Where(c => c != null && !string.IsNullOrEmpty(c.StringId))
                    .Select(c => c.StringId)
                    .OrderBy(s => s, StringComparer.Ordinal);
                sb.Append(string.Join(",", cultures));

                sb.Append("|kingdom:");
                var kingdoms = Kingdom.All
                    .Where(k => k != null && !string.IsNullOrEmpty(k.StringId))
                    .Select(k => k.StringId)
                    .OrderBy(s => s, StringComparer.Ordinal);
                sb.Append(string.Join(",", kingdoms));

                sb.Append("|hero:");
                var heroes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var k in Kingdom.All)
                {
                    foreach (var h in SelectKeyHeroes(k))
                    {
                        if (h != null && !string.IsNullOrEmpty(h.StringId)) heroes.Add(h.StringId);
                    }
                }
                sb.Append(string.Join(",", heroes.OrderBy(s => s, StringComparer.Ordinal)));

                sb.Append("|lang:");
                sb.Append(LWNTextHelper.GetReplyLanguageInstruction());
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldBg] GetFingerprint 异常: {ex.Message} → 空指纹（下次重新生成）");
                return "";
            }
            return sb.ToString();
        }

        /// <summary>
        /// 主线程采集生成材料快照（cap 8000 字符）：文化全量（名称 + EncyclopediaText）+ 王国全量
        /// （名称 + 文化名 + 领袖名）+ 每王国 ≤3 关键英雄（RulingClan.Leader 必选 + Clans 按 Influence
        /// 降序前 2）的 Clan.EncyclopediaText / Hero 百科。铁律 5：枚举走 GetObjectTypeList / Kingdom.All
        /// 动态遍历（先例 WorldFactProvider:1446 / WorldEventSimulator:1202）。
        /// </summary>
        public static string BuildMaterialSnapshot()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== 文化 ==="); // lwn-ignore: A
                int ci = 1;
                foreach (var culture in MBObjectManager.Instance.GetObjectTypeList<CultureObject>())
                {
                    if (culture == null || string.IsNullOrEmpty(culture.Name?.ToString())) continue;
                    string enc = culture.EncyclopediaText?.ToString();
                    sb.AppendLine($"{ci}. {culture.Name}：{Truncate(enc, 600)}");
                    ci++;
                }

                sb.AppendLine();
                sb.AppendLine("=== 王国 ==="); // lwn-ignore: A
                int ki = 1;
                foreach (var kingdom in Kingdom.All)
                {
                    if (kingdom == null || string.IsNullOrEmpty(kingdom.Name?.ToString())) continue;
                    string leaderName = kingdom.RulingClan?.Leader?.Name?.ToString() ?? "（无在位领袖）"; // lwn-ignore: A
                    string cult = kingdom.Culture?.Name?.ToString() ?? "（无文化）"; // lwn-ignore: A
                    sb.AppendLine($"{ki}. {kingdom.Name}（文化：{cult}，领袖：{leaderName}）：{Truncate(kingdom.EncyclopediaText?.ToString(), 600)}"); // lwn-ignore: A
                    int hi = 1;
                    foreach (var hero in SelectKeyHeroes(kingdom))
                    {
                        if (hero == null) continue;
                        string clanEnc = Truncate(hero.Clan?.EncyclopediaText?.ToString(), 300);
                        string heroEnc = Truncate(hero.EncyclopediaText?.ToString(), 300);
                        string detail = !string.IsNullOrWhiteSpace(heroEnc) ? heroEnc : clanEnc;
                        if (string.IsNullOrWhiteSpace(detail)) continue;
                        sb.AppendLine($"  关键人物{hi}. {hero.Name}（{hero.Clan?.Name?.ToString() ?? "无家族"}）：{detail}"); // lwn-ignore: A
                        hi++;
                    }
                    ki++;
                }

                string result = sb.ToString();
                if (result.Length > 8000)
                    result = result.Substring(0, 8000) + "…（材料超长截断）"; // lwn-ignore: A
                return result;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldBg] BuildMaterialSnapshot 异常: {ex.Message} → 空快照");
                return "（材料采集失败）"; // lwn-ignore: A
            }
        }

        /// <summary>每王国关键英雄选取：RulingClan.Leader 必选 + Clans 按 Influence 降序前 2（去重）。</summary>
        private static IEnumerable<Hero> SelectKeyHeroes(Kingdom kingdom)
        {
            if (kingdom == null) return Enumerable.Empty<Hero>();
            try
            {
                var result = new List<Hero>();
                var leader = kingdom.RulingClan?.Leader;
                if (leader != null) result.Add(leader);
                result.AddRange(kingdom.Clans
                    .Where(c => c != null && c.Leader != null && c.Leader != leader)
                    .OrderByDescending(c => c.Influence)
                    .Take(2)
                    .Select(c => c.Leader));
                return result;
            }
            catch { return Enumerable.Empty<Hero>(); }
        }

        /// <summary>生成 prompt（静态纪律 = XML LWN_worldbg_generate 单一事实源，EN/CN 双文件同步；
        /// 缺 key 用代码兜底——DialogueComponent.ResolvePrompt 同款本地包装）。</summary>
        public static string BuildGeneratePrompt(string snapshot, string lang)
        {
            string rule = LWNTextHelper.ResolvePrompt("LWN_worldbg_generate"); // lwn-ignore: B
            if (string.IsNullOrWhiteSpace(rule))
            {
                rule = "你是这个世界观的生成者。根据下面提供的材料（文化百科、王国百科、关键人物百科），" // lwn-ignore: A
                    + "用 {LANG} 写一段 100~150 字的【世界格局】概述。\n" // lwn-ignore: A
                    + "要求：1. 只输出一段话，第一行写「=== 世界格局 ===」，第二行起是正文，不要输出标记以外的任何内容；" // lwn-ignore: A
                    + "2. 内容是静态世界观 lore：主要阵营与文化的名称和特征、地理、知名历史人物（用身份泛称如「帝国皇帝」，不写具体人名）、文化风俗；" // lwn-ignore: A
                    + "3. 禁止任何实时状态：势力强弱、存亡、战争、领地变动；" // lwn-ignore: A
                    + "4. 禁止编造材料中没有的信息，只用下面提供的材料；" // lwn-ignore: A
                    + "5. 语言：{LANG}。"; // lwn-ignore: A
            }
            rule = rule.Replace("{LANG}", lang);
            return rule + "\n\n=== 材料 ===\n" + snapshot; // lwn-ignore: A
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "（无百科）"; // lwn-ignore: A
            return text.Length > max ? text.Substring(0, max) + "…" : text;
        }
    }
}
