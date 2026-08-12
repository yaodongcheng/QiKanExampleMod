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

        /// <summary>私聊左栏列表上限。</summary>
        public const int MaxDirectList = 8;

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
        private static readonly Dictionary<string, ImMode> _modes = new Dictionary<string, ImMode>();

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
                // 按最后时间倒序；超上限丢最久
                _directIndex.Sort((a, b) => b.LastTimestamp.CompareTo(a.LastTimestamp));
                while (_directIndex.Count > MaxDirectList)
                    _directIndex.RemoveAt(_directIndex.Count - 1);
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

        public static ImMode GetMode(string convId)
        {
            lock (_lock)
            {
                return _modes.TryGetValue(convId, out var m) ? m : ImMode.Chat;
            }
        }

        public static void SetMode(string convId, ImMode mode)
        {
            if (string.IsNullOrEmpty(convId)) return;
            lock (_lock)
            {
                _modes[convId] = mode;
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

    /// <summary>IM 会话模式（闲聊 / 密令）。</summary>
    public enum ImMode
    {
        Chat,       // 闲聊：普通消息 → NPC LLM 回复
        Command,    // 密令：文本 → LLM 计划 → 批准卡片 → 执行（仅 Mission 内可用）
    }
}
