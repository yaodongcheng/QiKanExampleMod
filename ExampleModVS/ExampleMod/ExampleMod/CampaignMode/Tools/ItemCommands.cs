using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs.CampaignMode.Tools
{
    /// <summary>
    /// 通用物品命令 <c>custom.item</c>（2026-10-10 立）。
    ///
    /// 🔴 **为什么要有它**：原版 `campaign.add_item_to_main_party` **只认本地化显示名**
    ///    （`CampaignCheats.GetItemObject` 比的是 `ItemObject.Name`），中文客户端得敲中文、
    ///    还得用竖线分组（`<名> | <数量>`）—— 一旦名字对不上就报 `Item is not found`，
    ///    看着像"物品没注册"，极难排查（工程在施法体系 P0 探针上为此翻过两小时）。
    ///    本命令**一律按 StringId 查**，与语言无关 —— 这也是铁律 13 的正道
    ///    （"控制台取物品的正道 = 按 StringId 查"）。
    ///
    /// 用法（**首参可弃**：认不出的首参当占位符，回落打用法）：
    /// <code>
    /// custom.item                            打用法
    /// custom.item add &lt;StringId&gt; [数量]      按 StringId 发物品给主角部队（默认 1 个）
    /// custom.item list [关键字]              列物品：StringId + 显示名（关键字按 StringId 或显示名过滤）
    /// </code>
    ///
    /// 🔴 两条项目纪律：返回文本**纯英文**；**首参可弃**（拿不准用户会填什么，认不出就打用法、不报错）。
    /// </summary>
    public static class ItemCommands
    {
        private const int ListLimit = 30;      // 列表最多打这么多条，防刷屏

        [CommandLineFunctionality.CommandLineArgumentFunction("item", "custom")]
        public static string Item(List<string> args)
        {
            string sub = (args != null && args.Count > 0) ? (args[0] ?? string.Empty).ToLowerInvariant() : string.Empty;

            switch (sub)
            {
                case "add":
                case "give":
                    return Add(args);

                case "list":
                case "find":
                    return List(args);

                default:
                    return Usage(args != null && args.Count > 0 ? args[0] : null);
            }
        }

        private static string Usage(string rejectedFirstArg)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(rejectedFirstArg))
            {
                // 首参可弃：说明清楚"你这个参数我没用"，而不是报错
                sb.Append($"note: '{rejectedFirstArg}' is not a sub-command -> showing usage.\n");
            }
            sb.Append("custom.item - item utilities (lookup by StringId, language independent)\n");
            sb.Append("  custom.item add <StringId> [amount]   add items to the main party (default 1)\n");
            sb.Append("  custom.item list [keyword]            list items: StringId + display name\n");
            sb.Append("examples:\n");
            sb.Append("  custom.item add lwn_huntian_ling\n");
            sb.Append("  custom.item add lwn_huntian_ling 5\n");
            sb.Append("  custom.item list huntian\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ add
        private static string Add(List<string> args)
        {
            if (args == null || args.Count < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                return "Error: usage = custom.item add <StringId> [amount]";
            }

            if (Campaign.Current == null || Hero.MainHero == null)
            {
                return "Error: no campaign / main hero (this command works in a campaign, not in the main menu).";
            }

            string id = args[1].Trim();
            ItemObject item = MBObjectManager.Instance?.GetObject<ItemObject>(id);
            if (item == null)
            {
                // 铁律 5 的两轮策略：精确查不到 -> 动态遍历内存里已注册的对象做兜底。
                // 这里**不自动挑选**（子串唯一也要求用户敲全名），只把候选列出来让用户复制，
                // 免得"以为加的是 A、其实加的是 B"。
                return $"Error: no item with StringId '{id}'.\n" + SuggestById(id);
            }

            int amount = 1;
            if (args.Count >= 3 && !string.IsNullOrWhiteSpace(args[2]))
            {
                if (!int.TryParse(args[2].Trim(), out amount) || amount <= 0)
                {
                    return $"Error: amount '{args[2]}' is not a positive integer (usage = custom.item add <StringId> [amount]).";
                }
                if (amount > 9999) amount = 9999;      // 防手滑刷爆库存
            }

            // 🔴 铁律 4：资源进出一律走归口。凭空发放 = 第②类「收发 Grant」，
            //    单边对接世界，**用 null 显式标注虚空来源**（合法，非违规）。
            int moved = AgentControlHelper.TransferItems(null, Hero.MainHero, item, amount);
            if (moved <= 0)
            {
                return $"Error: transfer failed (item='{item.StringId}', amount={amount}). "
                     + "Is the main hero in a party?";
            }

            return $"OK: +{moved} x {item.StringId} (\"{DisplayName(item)}\") -> main party. "
                 + $"Party now holds {Hero.MainHero.PartyBelongedTo.ItemRoster.GetItemNumber(item)}.";
        }

        private static string SuggestById(string id)
        {
            var sb = new StringBuilder();
            int shown = 0;
            try
            {
                foreach (ItemObject it in MBObjectManager.Instance.GetObjectTypeList<ItemObject>())
                {
                    if (it == null || string.IsNullOrEmpty(it.StringId)) continue;
                    if (it.StringId.IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    sb.Append($"  {it.StringId}  (\"{DisplayName(it)}\")\n");
                    if (++shown >= ListLimit)
                    {
                        sb.Append($"  ... (stopped at {ListLimit}; narrow the keyword)\n");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                return $"  (candidate scan failed: {e.GetType().Name})";
            }
            return shown > 0 ? "Did you mean (StringId substring match):\n" + sb : "  (no StringId contains that text either)";
        }

        // ------------------------------------------------------------------ list
        private static string List(List<string> args)
        {
            if (Campaign.Current == null)
            {
                return "Error: no campaign (this command works in a campaign).";
            }

            string keyword = (args != null && args.Count >= 2 && !string.IsNullOrWhiteSpace(args[1]))
                ? args[1].Trim() : null;

            var sb = new StringBuilder();
            int shown = 0, total = 0;
            try
            {
                foreach (ItemObject it in MBObjectManager.Instance.GetObjectTypeList<ItemObject>())
                {
                    if (it == null || string.IsNullOrEmpty(it.StringId)) continue;

                    if (keyword != null)
                    {
                        // 两边都查：StringId（英文，好敲）与显示名（中文客户端能按中文找）
                        string name = DisplayName(it);
                        bool hit = it.StringId.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                                || (!string.IsNullOrEmpty(name) && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!hit) continue;
                    }

                    total++;
                    if (shown < ListLimit)
                    {
                        sb.Append($"  {it.StringId}  (\"{DisplayName(it)}\")\n");
                        shown++;
                    }
                }
            }
            catch (Exception e)
            {
                return $"Error while scanning items: {e.GetType().Name}: {e.Message}";
            }

            string head = keyword == null
                ? $"OK: items (showing {shown})\n"
                : $"OK: items matching '{keyword}' (showing {shown} of {total})\n";
            if (total > shown) sb.Append($"  ... (+{total - shown} more; narrow the keyword)\n");
            return head + (sb.Length > 0 ? sb.ToString() : "  (none)\n");
        }

        private static string DisplayName(ItemObject item)
        {
            try
            {
                return item.Name?.ToString() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
