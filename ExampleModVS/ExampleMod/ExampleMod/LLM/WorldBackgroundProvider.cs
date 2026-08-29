using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
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
                // 本地化：LWN_worldbg_section_culture（=== 文化 ===，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_worldbg_section_culture"));
                // 🔴 2026-08-29（衣谷三国实锤 + 用户发现「百科无文化栏目」）：13 州文化百科文本 =
                // 原版帝国正史逐字复制粘贴（同一段在 ≥2 个文化重复）。占位文本检测——收集先行，
                // 同一段百科文本出现在 ≥2 个不同文化 = mod 复制粘贴的占位（原版世界各文化文本
                // 互不相同，天然不触发），该文本不采纳（只保留文化名——州名本身就是有价值的地理材料）。
                var textCount = new Dictionary<string, int>();
                var cultureText = new Dictionary<CultureObject, string>();
                var liveCultures = new List<CultureObject>();
                foreach (var culture in MBObjectManager.Instance.GetObjectTypeList<CultureObject>())
                {
                    if (culture == null || string.IsNullOrEmpty(culture.Name?.ToString())) continue;
                    // 🔴 2026-08-29（实机：三国档生成的世界观全是卡拉德亚）：文化百科字符串 = 原版整个
                    // 卡拉迪亚正史；非卡拉迪亚 mod 下原版文化对象仍残留在注册表（无定居点、无王国归属）
                    // → 材料被原版文化百科淹没。活文化判定：被现存王国引用 或 属于现存定居点 → 收材料；
                    // 注册表残留（无地无国）→ 排除。
                    bool live = Kingdom.All.Any(k => k.Culture == culture)
                        || Settlement.All.Any(s => s.Culture == culture);
                    if (!live) continue;
                    string enc = culture.EncyclopediaText?.ToString();
                    liveCultures.Add(culture);
                    cultureText[culture] = enc;
                    if (!string.IsNullOrWhiteSpace(enc))
                        textCount[enc] = textCount.TryGetValue(enc, out var n) ? n + 1 : 1;
                }
                int ci = 1;
                foreach (var culture in liveCultures)
                {
                    string enc = cultureText[culture];
                    if (enc != null && textCount.TryGetValue(enc, out var n) && n >= 2) enc = null;   // 占位副本 → 文本不采纳
                    // 本地化：LWN_worldbg_culture_line（{NUM}. {NAME}：{TEXT}，双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_worldbg_culture_line",
                        ("NUM", ci.ToString()), ("NAME", culture.Name.ToString()), ("TEXT", Truncate(enc, 600))));
                    ci++;
                }

                sb.AppendLine();
                // 本地化：LWN_worldbg_section_kingdom（=== 王国 ===，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_worldbg_section_kingdom"));
                int ki = 1;
                foreach (var kingdom in Kingdom.All)
                {
                    if (kingdom == null || string.IsNullOrEmpty(kingdom.Name?.ToString())) continue;
                    // 本地化：LWN_worldbg_no_leader（无在位领袖，双桶）
                    string leaderName = kingdom.RulingClan?.Leader?.Name?.ToString() ?? LWNTextHelper.ResolvePrompt("LWN_worldbg_no_leader");
                    // 本地化：LWN_worldbg_no_culture（无文化，双桶）
                    string cult = kingdom.Culture?.Name?.ToString() ?? LWNTextHelper.ResolvePrompt("LWN_worldbg_no_culture");
                    // 本地化：LWN_worldbg_kingdom_line（{NUM}. {NAME}（文化：{CULT}，领袖：{LEADER}）：{TEXT}，双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_worldbg_kingdom_line",
                        ("NUM", ki.ToString()), ("NAME", kingdom.Name.ToString()),
                        ("CULT", cult), ("LEADER", leaderName),
                        ("TEXT", Truncate(kingdom.EncyclopediaText?.ToString(), 600))));
                    int hi = 1;
                    foreach (var hero in SelectKeyHeroes(kingdom))
                    {
                        if (hero == null) continue;
                        string clanEnc = Truncate(hero.Clan?.EncyclopediaText?.ToString(), 300);
                        string heroEnc = Truncate(hero.EncyclopediaText?.ToString(), 300);
                        string detail = !string.IsNullOrWhiteSpace(heroEnc) ? heroEnc : clanEnc;
                        if (string.IsNullOrWhiteSpace(detail)) continue;
                        // 本地化：LWN_worldbg_no_clan（无家族，双桶）
                        string clanName = hero.Clan?.Name?.ToString() ?? LWNTextHelper.ResolvePrompt("LWN_worldbg_no_clan");
                        // 本地化：LWN_worldbg_key_person_line（关键人物{NUM}，双桶）
                        sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_worldbg_key_person_line",
                            ("NUM", hi.ToString()), ("NAME", hero.Name.ToString()),
                            ("CLAN", clanName), ("TEXT", detail)));
                        hi++;
                    }
                    ki++;
                }

                string result = sb.ToString();
                if (result.Length > 8000)
                    // 本地化：LWN_worldbg_truncated（材料超长截断，双桶）
                    result = result.Substring(0, 8000) + LWNTextHelper.ResolvePrompt("LWN_worldbg_truncated");
                return result;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldBg] BuildMaterialSnapshot 异常: {ex.Message} → 空快照");
                // 本地化：LWN_worldbg_failed（材料采集失败，双桶）
                return LWNTextHelper.ResolvePrompt("LWN_worldbg_failed");
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
                // 本地化：LWN_worldbg_generate_fallback（生成规则 C# 兜底，双桶）
                rule = LWNTextHelper.ResolvePrompt("LWN_worldbg_generate_fallback");
            rule = rule.Replace("{LANG}", lang);
            // 本地化：LWN_worldbg_materials_header（\n\n=== 材料 ===\n，双桶）
            return rule + LWNTextHelper.ResolvePrompt("LWN_worldbg_materials_header") + snapshot;
        }

        private static string Truncate(string text, int max)
        {
            // 本地化：LWN_worldbg_no_encyclopedia（无百科，双桶）
            if (string.IsNullOrEmpty(text)) return LWNTextHelper.ResolvePrompt("LWN_worldbg_no_encyclopedia");
            return text.Length > max ? text.Substring(0, max) + "…" : text;
        }
    }
}
