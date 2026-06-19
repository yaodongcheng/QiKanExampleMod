using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>尝试从 Narrative.csv 读取事件通知文本（阶段优先）。</summary>
        private static string TryGetEventNotifyFromCSV(WorldEventData e)
        {
            try
            {
                string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
                string target = e.TargetHero?.Name?.ToString() ?? "村民";
                string instigator = e.IsGenericInstigator ? "一伙歹徒" : (e.InstigatorHero?.Name?.ToString() ?? "加害方");
                string phaseSuffix = e.Phase == WorldEventPhase.Impending ? "_Impending" : "_Consummated";

                // ① 优先：阶段感知条目 EventNotify_Assassination_Impending
                string phasedId = $"EventNotify_{e.EventType}{phaseSuffix}";
                var phasedResult = LookupNarrativeById(phasedId);
                if (phasedResult != null)
                    return phasedResult.Replace("{LOCATION}", loc)
                                       .Replace("{TARGET}", target)
                                       .Replace("{NPC}", instigator);

                // ② 回落：通用条目 EventNotify_Assassination
                string eventId = $"EventNotify_{e.EventType}";
                var result = LookupNarrativeById(eventId);
                if (result != null)
                    return result.Replace("{LOCATION}", loc)
                                 .Replace("{TARGET}", target)
                                 .Replace("{NPC}", instigator);
            }
            catch { }
            return null;
        }

        /// <summary>按 ID 精确查询 Narrative 表，返回纯文本或 null。</summary>
        private static string LookupNarrativeById(string id)
        {
            try
            {
                var table = GameDatabase.Narrative;
                if (table == null) return null;
                var allRows = table.GetAll().ToList();
                if (allRows.Count == 0) return null;

                var match = allRows.FirstOrDefault(r =>
                    string.Equals(r.GetString("ID"), id, StringComparison.OrdinalIgnoreCase));
                if (match == null) return null;

                var lines = match.GetList("Text");
                string text = "";
                if (lines != null && lines.Count > 0)
                    text = lines[MBRandom.RandomInt(lines.Count)];
                if (string.IsNullOrEmpty(text))
                    text = match.GetString("Text");
                if (string.IsNullOrEmpty(text) || text == "Any" || text == "……")
                    return null;

                return text;
            }
            catch { return null; }
        }

        /// <summary>硬编码兜底叙事（暗探情报渠道，禁止上帝视角）。</summary>
        private static string BuildEventNarrativeHardcoded(WorldEventData e)
        {
            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string victim = e.TargetHero?.Name?.ToString() ?? "村民";
            string instigator = e.InstigatorHero?.Name?.ToString() ?? "一伙人";
            string severityTag = e.Severity >= 8 ? "万分紧急" : e.Severity >= 5 ? "需要关注" : "";
            bool impending = e.Phase == WorldEventPhase.Impending;

            return e.EventType switch
            {
                // ── 匪患 ──
                WorldEventType.BanditRaid => impending
                    ? $"暗探来报——{instigator}近日在{loc}周边集结人手，频繁骚扰过往商队。当地村民人心惶惶，暗探判断其意图已是明摆着的——{victim}和乡亲们危在旦夕。"
                    : $"急报——{instigator}的匪帮已于昨夜洗劫了{loc}。{victim}和村民们损失惨重，哭声遍野。暗探已确认属实。",

                // ── 绑架 ──
                WorldEventType.Kidnapping => impending
                    ? $"暗探来报——{instigator}近日频频在{loc}出没，暗中打探{victim}的日常行踪。暗探判断：此人欲对{victim}不利，动手只是时间问题。"
                    : $"急报——{victim}已被{instigator}绑走。家人哭求无门，绑匪至今未递赎金要求。暗探正在追查去向。",

                // ── 饥荒 ──
                WorldEventType.Famine => impending
                    ? $"暗探来报——{loc}的粮仓已经见底，百姓靠野菜充饥已有多日。若再无外援，饿死人只是迟早的事。"
                    : $"急报——{loc}的饥荒已经失控。粮食耗尽，饿殍遍野。暗探摇头：来得太晚了。",

                // ── 背叛 ──
                WorldEventType.Betrayal => impending
                    ? $"暗探来报——{instigator}近日与{loc}以外的势力暗中往来频繁，对{victim}的态度也愈发冷淡。暗探评注：此人恐有二心，{victim}尚蒙在鼓里。"
                    : $"急报——{loc}出了内鬼。{instigator}已背叛{victim}，卷走大笔钱财。暗探评注：这刀是从背后捅的。",

                // ── 债务陷阱 ──
                WorldEventType.DebtTrap => impending
                    ? $"暗探来报——{instigator}正步步紧逼{victim}还债，利息已滚到了还不清的数目。再拖下去，{victim}的地契就要易手了。"
                    : $"急报——{victim}已被{instigator}逼到绝路。地契被收走，一家人失去了安身之所。暗探评注：合法的抢劫，比匪帮更狠。",

                // ── 情仇 ──
                WorldEventType.RomanticConflict =>
                    $"暗探来报——{loc}有人为情所困。{victim}卷入了一场无法脱身的情感纠葛，两家人的脸面都挂不住了。",

                // ── 冤案 ──
                WorldEventType.FalseAccusation => impending
                    ? $"暗探来报——{instigator}正在{loc}四处散布不利于{victim}的言论。证据尚未坐实，但流言已经传开。暗探评注：若无人出面，冤案恐将铸成。"
                    : $"急报——{victim}已被定罪。暗探评注：证据始终没能找到，这不是审判，是谋杀。",

                // ── 继承争端 ──
                WorldEventType.InheritanceDispute =>
                    $"暗探来报——{loc}的老族长走了。继承人之间剑拔弩张，{victim}的继承权正被{instigator}公开挑战。",

                // ── 逃犯 ──
                WorldEventType.Fugitive => impending
                    ? $"暗探来报——{loc}附近藏着一个逃犯，名为{victim}。追捕方悬了重赏，但这个人的故事可能没那么简单。暗探建议主公亲自过问。"
                    : $"暗探来报——{victim}的踪迹已彻底断了。也许是逃走了，也许是被人抓回去了。{loc}又恢复了表面的平静。",

                // ── 贸易争端 ──
                WorldEventType.TradeDispute => impending
                    ? $"暗探来报——{instigator}正在{loc}打压{victim}的生意。压价、断货、散布谣言——手段不干净，但还没到撕破脸的程度。"
                    : $"急报——{instigator}已垄断了{loc}的市场。{victim}的生意彻底垮了。暗探评注：商人的战争，不见血也能要命。",

                // ── 贵族冲突 ──
                WorldEventType.NobleConflict => impending
                    ? $"暗探来报——{instigator}近日在{loc}边境频频调动兵力。暗探判断其目标极可能是{victim}——摩擦一触即发。"
                    : $"急报——{instigator}与{victim}已在{loc}边境兵戎相见。烟尘滚滚，血流成河。暗探已确认交战属实。",

                // ── 圣物失窃 ──
                WorldEventType.SacredTheft => impending
                    ? $"暗探来报——{instigator}近日频频遣人在{loc}附近打探，目标似乎与当地的祖传圣物有关。暗探评注：此人觊觎已久，动手只是时间问题。{victim}一族若丢了圣物，传承便断了。"
                    : $"急报——{loc}的祖传圣物已于昨夜失窃。现场线索指向{instigator}的人。{victim}一族的族老们低下了头——传承断了。暗探已确认属实。",

                // ── 行刺 ──
                WorldEventType.Assassination => impending
                    ? $"暗探来报——{instigator}近日行踪诡秘，暗中遣人在{loc}附近观察{victim}的行踪。暗探评注：此乃行刺前兆，{victim}恐有大难。主公若想阻止，当速作决断。"
                    : $"急报——{victim}已于{loc}遇刺身亡。暗探已确认属实，刺客身份指向{instigator}。当地人人自危，都在猜下一个是谁。",

                // ── 宿敌复仇 ──
                WorldEventType.NemesisRevenge => impending
                    ? $"暗探来报——{instigator}正在找你。那道疤还在疼。暗探评注：此人离{loc}越来越近了。"
                    : $"暗探来报——{instigator}已经找到{loc}来了。那道疤还在疼。该来的终于来了。",

                _ => $"暗探来报——{loc}出了事，具体情况尚待追查。"
            };
        }
    }
}
