using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 通知管道 — 统一的事件推送入口。
    /// 替代旧的 quest.AddLog() 文本日志，改用 CK3 式的弹窗 + 可选选择。
    ///
    /// 三种模式：
    ///   1. Notify — 纯提示弹窗（NinjaNotification），玩家点确定关闭
    ///   2. PopupWithChoice — InquiryData 双按钮选择弹窗（如旅途事件"救人/赶路"）
    ///   3. DirectorPush — 导演推送（就近发现/路途拦截/酒馆传闻 等场景自动选择合适推送方式）
    /// </summary>
    public static class NotificationPipeline
    {
        /// <summary>纯提示弹窗 — 使用 NinjaNotification 旁白式推送。</summary>
        public static void Notify(string message, string emotion = "normal")
        {
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                // 走 NinjaNotification 弹窗（玩家看到右上角滑入通知）
                NinjaNotificationManager.Show(message, () => { });
                DebugLogger.Log($"[NotificationPipeline] Notify: {message}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NotificationPipeline] Notify failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 双按钮选择弹窗 — 使用 InquiryData。
        /// 典型用例：旅途中遇到受伤者 → "救人"(+经验) / "赶路"(无变化)
        /// </summary>
        public static void PopupWithChoice(
            string title,
            string message,
            string optionAText,
            Action onOptionA,
            string optionBText,
            Action onOptionB)
        {
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    title ?? "事件",
                    message,
                    true,       // canOptionA = true
                    optionBText != null,  // canOptionB
                    optionAText ?? "是",
                    optionBText ?? "否",
                    () => {
                        try { onOptionA?.Invoke(); }
                        catch (Exception ex) { DebugLogger.Log($"PopupChoice A error: {ex.Message}"); }
                    },
                    () => {
                        try { onOptionB?.Invoke(); }
                        catch (Exception ex) { DebugLogger.Log($"PopupChoice B error: {ex.Message}"); }
                    }));

                DebugLogger.Log($"[NotificationPipeline] PopupWithChoice: {title} — {message}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NotificationPipeline] PopupWithChoice failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 世界事件推送 — 根据严重度自动选择推送强度，生成类型相关的叙事文本。
        /// </summary>
        public static void PushWorldEvent(WorldEventData worldEvent, string narrativeText)
        {
            if (worldEvent == null) return;

            try
            {
                string msg = narrativeText;
                if (string.IsNullOrEmpty(msg))
                    msg = BuildEventNarrative(worldEvent);

                if (worldEvent.Severity >= 7)
                    NinjaNotificationManager.Show(msg, () => { });
                else
                    InformationManager.DisplayMessage(new InformationMessage(msg));

                DebugLogger.Log($"[NotificationPipeline] PushWorldEvent: {worldEvent.EventType} severity={worldEvent.Severity}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NotificationPipeline] PushWorldEvent failed: {ex.Message}");
            }
        }

        /// <summary>根据事件数据生成富有风味的叙事文本。</summary>
        private static string BuildEventNarrative(WorldEventData e)
        {
            return BuildEventNarrativePublic(e);
        }

        /// <summary>公开的事件叙事构建（供 WorldEventNotificationController 等外部调用）。</summary>
        public static string BuildEventNarrativePublic(WorldEventData e)
        {
            if (e == null) return null;

            // 优先从 Narrative.csv 查表（EventNotify_* 条目）
            string csvText = TryGetEventNotifyFromCSV(e);
            if (!string.IsNullOrEmpty(csvText))
                return csvText;

            // 兜底：硬编码
            return BuildEventNarrativeHardcoded(e);
        }

        /// <summary>尝试从 Narrative.csv 读取事件通知文本。</summary>
        private static string TryGetEventNotifyFromCSV(WorldEventData e)
        {
            try
            {
                string eventId = $"EventNotify_{e.EventType}";
                var filters = new NarrativeFilters { EventName = eventId };
                var result = NarrativeResolver.Resolve(filters);
                if (result != null && !string.IsNullOrEmpty(result.Text) && result.Text != "……")
                {
                    string text = result.Text;
                    string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
                    string target = e.TargetHero?.Name?.ToString() ?? "村民";
                    string instigator = e.IsGenericInstigator ? "一伙歹徒" : (e.InstigatorHero?.Name?.ToString() ?? "加害方");
                    return text.Replace("{LOCATION}", loc)
                               .Replace("{TARGET}", target)
                               .Replace("{NPC}", instigator);
                }
            }
            catch { }
            return null;
        }

        /// <summary>硬编码兜底叙事。</summary>
        private static string BuildEventNarrativeHardcoded(WorldEventData e)
        {
            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string victim = e.TargetHero?.Name?.ToString() ?? "村民";
            string instigator = e.InstigatorHero?.Name?.ToString() ?? "一伙人";
            string severityTag = e.Severity >= 8 ? "危急" : e.Severity >= 5 ? "严重" : "";

            return e.EventType switch
            {
                WorldEventType.BanditRaid =>
                    $"⚠ {severityTag} {instigator}正带人劫掠{loc}！{victim}和村民们危在旦夕。",
                WorldEventType.Kidnapping =>
                    $"⚠ {severityTag} {victim}被{instigator}绑走了！家人焦急万分，时间紧迫。",
                WorldEventType.Famine =>
                    $"⚠ {severityTag} {loc}粮食告罄！百姓以野菜充饥，急需救援物资。",
                WorldEventType.Betrayal =>
                    $"⚠ {severityTag} {loc}出了内鬼！{instigator}背叛了{victim}，内部人心惶惶。",
                WorldEventType.DebtTrap =>
                    $"⚠ {severityTag} {victim}被{instigator}逼债逼得走投无路，眼看就要家破人亡。",
                WorldEventType.RomanticConflict =>
                    $"💔 {loc}有人为情所困——{victim}卷入了一场无法脱身的情感纠葛。",
                WorldEventType.FalseAccusation =>
                    $"⚖ {severityTag} {victim}被冤枉了！真凶另有其人，但证据不足就要被定罪。",
                WorldEventType.InheritanceDispute =>
                    $"⚔ {loc}的老族长走了，继承人之间剑拔弩张——{victim}的继承权遭到挑战。",
                WorldEventType.Fugitive =>
                    $"🏃 {loc}附近藏着一个逃犯——{victim}。追捕方悬了重赏，但这个人的故事可能没那么简单。",
                WorldEventType.TradeDispute =>
                    $"📊 {loc}的商人闹翻了——{instigator}垄断了市场，{victim}的生意做不下去。",
                WorldEventType.NobleConflict =>
                    $"⚔ {severityTag} {instigator}在{loc}边境集结兵力，与{victim}的冲突一触即发。",
                WorldEventType.SacredTheft =>
                    $"🔮 {severityTag} {loc}的圣物被{instigator}盗走！这不止是财物——是{victim}一族传承的信物。",
                WorldEventType.Assassination =>
                    $"🗡 {severityTag} {victim}被刺杀了！{loc}陷入混乱，人人自危——下一个轮到谁？",
                WorldEventType.NemesisRevenge =>
                    $"💀 {instigator}回来了——那道疤还在疼。他在找你。",
                _ => $"⚠ {loc}发生了事件。"
            };
        }
    }
}
