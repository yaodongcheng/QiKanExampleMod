using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IM 静态存储：
    /// - 群聊消息（party/clan/kingdom 三个固定频道，各自上限 100 条，先进先出）；
    /// - 私聊索引（<see cref="ImDirectEntry"/>，记录最近私聊对象与最后时间，供左栏「最近的单个人的聊天」）；
    /// - 未读计数 / 会话模式：纯运行时（不存档，读档清零）。
    ///
    /// 存档（Phase 5 接线）：群聊每频道一个 key（GuardJson 数组裁剪丢最老）、私聊索引一个 key。
    /// </summary>
    public static class ImChatStore
    {
        /// <summary>群聊单频道消息上限（不可能无限存，需求 6）。</summary>
        public const int MaxGroupMessages = 100;

        /// <summary>私聊左栏列表显示上限（完整版+缩略版共用，2026-08-17 用户裁定 = 6；只裁剪显示，索引全量保留）。</summary>
        public const int MaxDirectList = 6;

        public const string ChannelParty = "party";
        public const string ChannelClan = "clan";
        public const string ChannelKingdom = "kingdom";

        public static readonly string[] GroupChannelIds = { ChannelParty, ChannelClan, ChannelKingdom };

        private static readonly object _lock = new object();

        // 群聊消息（channelId → 有序消息列表，最旧在前）
        private static readonly Dictionary<string, List<ImMessage>> _groupMessages =
            new Dictionary<string, List<ImMessage>>();

        // 私聊索引（按 LastTimestamp 倒序维护）
        private static readonly List<ImDirectEntry> _directIndex = new List<ImDirectEntry>();

        // ── 运行时状态（不存档）──
        private static readonly Dictionary<string, int> _unread = new Dictionary<string, int>();

        // ───────────────────────── 群聊 ─────────────────────────

        public static List<ImMessage> GetGroupMessages(string channelId)
        {
            lock (_lock)
            {
                if (_groupMessages.TryGetValue(channelId, out var list))
                    return new List<ImMessage>(list);
                return new List<ImMessage>();
            }
        }

        public static void AppendGroupMessage(string channelId, ImMessage msg)
        {
            if (msg == null) return;
            // 🔴 2026-08-13（在场标记失效根因）：频道归属以参数为准统一写入——原 ImChatManager
            // 玩家发言（:271）/ NPC 回复投递（:510）两处漏设 ConvId → DisplaySenderName 的
            // ConvId=="party" 判断恒 false，队伍频道（在场/他处）标记不显示。写入点统一由 Store 兜底。
            msg.ConvId = channelId;
            // 🔴 玩家视角日志（2026-08-11 用户裁定）：全部 IM 消息统一落日志——一行 = 玩家面板看到的一条。
            // 排查/复盘时按时间顺序重建玩家视角（[IM-Store] 前缀；Content 空回退 PlanSummary；换行转义保一行一条）。
            string text = string.IsNullOrWhiteSpace(msg.Content) ? (msg.PlanSummary ?? "") : msg.Content;
            text = text.Replace("\r", "\\r").Replace("\n", "\\n");
            DebugLogger.Log($"[IM-Store] {channelId} {msg.SenderName} [{msg.Kind}]: {text}");
            lock (_lock)
            {
                if (!_groupMessages.TryGetValue(channelId, out var list))
                {
                    list = new List<ImMessage>();
                    _groupMessages[channelId] = list;
                }
                list.Add(msg);
                while (list.Count > MaxGroupMessages)
                    list.RemoveAt(0);
            }
        }

        /// <summary>按索引区间移除（🔴 2026-08-12：拒绝计划 = 抛弃计划——命令→陈述→卡片整段抹除，
        /// 不再进后续上下文（群聊【频道近期消息】）与 UI）。越界安全钳制。</summary>
        public static void RemoveMessageRange(string channelId, int startIndex, int count)
        {
            if (count <= 0 || startIndex < 0) return;
            lock (_lock)
            {
                if (!_groupMessages.TryGetValue(channelId, out var list)) return;
                int end = Math.Min(startIndex + count, list.Count);
                for (int i = end - 1; i >= startIndex; i--)
                    list.RemoveAt(i);
            }
        }

        // ───────────────────────── 私聊索引 ─────────────────────────

        public static void TouchDirectChat(string heroId, double ts)
        {
            if (string.IsNullOrEmpty(heroId)) return;
            lock (_lock)
            {
                var entry = _directIndex.FirstOrDefault(e => e.HeroId == heroId);
                if (entry == null)
                {
                    _directIndex.Add(new ImDirectEntry(heroId, ts));
                }
                else
                {
                    entry.LastTimestamp = Math.Max(entry.LastTimestamp, ts);
                }
                // 按最后时间倒序（新的在前）
                _directIndex.Sort((a, b) => b.LastTimestamp.CompareTo(a.LastTimestamp));
                // 🔴 2026-08-17（用户裁定：容量上限只裁剪显示，不删数据）：
                // 索引**全量保留**（数据还在，存档/重新打开都能找回）——显示层 GetRecentDirectChats(cap) 取前 N 个，
                // 超限条目只是 UI 上看不到（先进先出：新的在前、旧的沉底）。
            }
        }

        /// <summary>最近的私聊对象列表（左栏数据源，按最后时间倒序）。</summary>
        public static List<ImDirectEntry> GetRecentDirectChats(int cap = MaxDirectList)
        {
            lock (_lock)
            {
                return _directIndex.Take(cap).ToList();
            }
        }

        // ───────────────────────── 运行时状态 ─────────────────────────

        public static int GetUnread(string convId)
        {
            lock (_lock)
            {
                return _unread.TryGetValue(convId, out var v) ? v : 0;
            }
        }

        public static void IncUnread(string convId)
        {
            if (string.IsNullOrEmpty(convId)) return;
            lock (_lock)
            {
                _unread.TryGetValue(convId, out var v);
                _unread[convId] = v + 1;
            }
        }

        public static void ClearUnread(string convId)
        {
            if (string.IsNullOrEmpty(convId)) return;
            lock (_lock)
            {
                _unread.Remove(convId);
            }
        }

        /// <summary>
        /// 🔴 2026-08-17（呼出按钮徽标口径）：总未读数 = 三固定频道（party/clan/kingdom）
        /// + 全部私聊索引（_directIndex 全量，非左栏显示上限 6）之和。查看会话后 ClearUnread → 数字回落。
        /// </summary>
        public static int GetTotalUnread()
        {
            lock (_lock)
            {
                int total = 0;
                foreach (var cid in GroupChannelIds)
                {
                    _unread.TryGetValue(cid, out var v);
                    total += v;
                }
                foreach (var e in _directIndex)
                {
                    if (e == null || string.IsNullOrEmpty(e.HeroId)) continue;
                    _unread.TryGetValue("direct_" + e.HeroId, out var v);
                    total += v;
                }
                return total;
            }
        }

        // 🔴 2026-08-12（合并闲聊/计划模式）：ImMode/GetMode/SetMode/_modes 已整体删除——
        // 模式指示文本改为从会话状态派生（ImCommandFlow.GetPhase），玩家消息恒走闲聊管线。

        /// <summary>
        /// 🔴 2026-08-23（跨档残留修复）：新档创建时清空全部运行时状态——
        /// 群聊消息 + 私聊索引 + 未读计数。此前只有读档路径（SerializeSlot 的 IsLoading）
        /// 覆盖式恢复；同进程「主菜单 → 直接开新档」时 static 残留旧档数据（实机：
        /// 新档 party 频道残留旧档 51 条消息、左栏旧私聊对象），且新档首次保存会把
        /// 旧数据序列化进新档存档 = 真串档。读档路径不受影响（Deserialize 在 IsLoading 时覆盖）。
        /// </summary>
        public static void ResetAll()
        {
            lock (_lock)
            {
                _groupMessages.Clear();
                _directIndex.Clear();
                _unread.Clear();
            }
        }

        // ───────────────────────── 存档 ─────────────────────────

        public static string SerializeGroup(string channelId)
        {
            lock (_lock)
            {
                var list = _groupMessages.TryGetValue(channelId, out var l) ? l : new List<ImMessage>();
                return JsonConvert.SerializeObject(list);
            }
        }

        public static void DeserializeGroup(string channelId, string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                var list = JsonConvert.DeserializeObject<List<ImMessage>>(json);
                if (list == null) return;
                lock (_lock)
                {
                    // 异常存档超限兜底：恢复时重新按上限收缩（防读档后消息数越界）
                    while (list.Count > MaxGroupMessages)
                        list.RemoveAt(0);
                    // 🔴 2026-08-12 清扫：Generating 占位行是 Mission 瞬态（buggy 构建的 RemoveGenerating
                    // 副本 bug 会让它残留在存档里）——读档一律丢弃，避免「思考中…」气泡永远卡在历史里
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (list[i] != null && list[i].Kind == ImMessageKind.Generating)
                            list.RemoveAt(i);
                    }
                    _groupMessages[channelId] = list;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChatStore] DeserializeGroup({channelId}) 失败: {ex.Message}");
            }
        }

        public static string SerializeDirectIndex()
        {
            lock (_lock)
            {
                return JsonConvert.SerializeObject(_directIndex);
            }
        }

        public static void DeserializeDirectIndex(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                var list = JsonConvert.DeserializeObject<List<ImDirectEntry>>(json);
                if (list == null) return;
                lock (_lock)
                {
                    _directIndex.Clear();
                    _directIndex.AddRange(list);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChatStore] DeserializeDirectIndex 失败: {ex.Message}");
            }
        }
    }
}
