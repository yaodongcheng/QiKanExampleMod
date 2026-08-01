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
                    // 弹窗默认标题兜底：事件
                    title ?? LWNTextHelper.ResolveText("LWN_pipeline_popup_default_title", "Event"),
                    message,
                    true,       // canOptionA = true
                    optionBText != null,  // canOptionB
                    // 弹窗默认按钮 A 文本兜底：是
                    optionAText ?? LWNTextHelper.ResolveText("LWN_pipeline_popup_default_option_a", "Yes"),
                    // 弹窗默认按钮 B 文本兜底：否
                    optionBText ?? LWNTextHelper.ResolveText("LWN_pipeline_popup_default_option_b", "No"),
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
        public static void PushWorldEvent(WorldEvent worldEvent, string narrativeText)
        {
            if (worldEvent == null) return;

            try
            {
                string msg = narrativeText;
                if (string.IsNullOrEmpty(msg))
                    msg = BuildEventNarrative(worldEvent);

                if (worldEvent.Severity >= 70)
                    NinjaNotificationManager.Show(msg, () => { });
                else
                    InformationManager.DisplayMessage(new InformationMessage(msg));

                DebugLogger.Log($"[NotificationPipeline] PushWorldEvent: {worldEvent.Type} severity={worldEvent.Severity}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NotificationPipeline] PushWorldEvent failed: {ex.Message}");
            }
        }

        /// <summary>根据事件数据生成富有风味的叙事文本。</summary>
        private static string BuildEventNarrative(WorldEvent e)
        {
            return BuildEventNarrativePublic(e);
        }

        /// <summary>公开的事件叙事构建（供 WorldEventNotificationController 等外部调用）。</summary>
        public static string BuildEventNarrativePublic(WorldEvent e)
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
        private static string TryGetEventNotifyFromCSV(WorldEvent e)
        {
            try
            {
                // 叙事查表兜底：地点未知时默认显示"某地"
                string loc = e.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_pipeline_placeholder_location", "somewhere");
                // 叙事查表兜底：目标未知时默认称呼"村民"
                string target = e.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_pipeline_placeholder_target", "a villager");
                // 叙事查表兜底：团伙作案默认"一伙歹徒"，无名加害方默认"加害方"
                string instigator = e.IsGenericInstigator ? LWNTextHelper.ResolveText("LWN_pipeline_placeholder_bandit_gang", "a gang of bandits") : (e.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_pipeline_placeholder_perpetrator", "the culprit"));
                string phaseSuffix = e.Phase == WorldEventPhase.Impending ? "_Impending" : "_Consummated";

                // ① 优先：阶段感知条目 EventNotify_Assassination_Impending
                string phasedId = $"EventNotify_{e.Type}{phaseSuffix}";
                var phasedResult = LookupNarrativeById(phasedId);
                if (phasedResult != null)
                    return phasedResult.Replace("{LOCATION}", loc)
                                       .Replace("{TARGET}", target)
                                       .Replace("{NPC}", instigator);

                // ② 回落：通用条目 EventNotify_Assassination
                string eventId = $"EventNotify_{e.Type}";
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
                string xmlKey = $"LWN_narr_{id.ToLower()}";
                return LWNTextHelper.TryResolveText(xmlKey);
            }
            catch { return null; }
        }

        /// <summary>硬编码兜底叙事（暗探情报渠道，禁止上帝视角）。</summary>
        private static string BuildEventNarrativeHardcoded(WorldEvent e)
        {
            // 事件叙事兜底：地点未知时默认显示"某地"
            string loc = e.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_pipeline_placeholder_location", "somewhere");
            // 事件叙事兜底：目标未知时默认称呼"村民"
            string victim = e.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_pipeline_placeholder_target", "a villager");
            // 事件叙事兜底：加害方未知时默认"一伙人"
            string instigator = e.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_pipeline_placeholder_instigator", "a group of people");
            // 事件严重度标签：≥80 万分紧急 / ≥50 需要关注
            string severityTag = e.Severity >= 80 ? LWNTextHelper.ResolveText("LWN_pipeline_severity_critical", "Urgent") : e.Severity >= 50 ? LWNTextHelper.ResolveText("LWN_pipeline_severity_attention", "Needs attention") : "";
            bool impending = e.Phase == WorldEventPhase.Impending;

            return e.Type switch
            {
                // ── 匪患 ──
                EventType.BanditRaid => impending
                    // 事件叙事兜底：匪患案发（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_banditraid_impending",
                        "Spy report — {INSTIGATOR} has been gathering men around {LOCATION} lately, harrying passing caravans. The locals are terrified, and the spy reads the intent as plain: {VICTIM} and the villagers are in mortal danger.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim))
                    // 事件叙事兜底：匪患案发（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_banditraid_consummated",
                        "Urgent — {INSTIGATOR}'s gang raided {LOCATION} last night. {VICTIM} and the villagers suffered heavy losses; weeping echoes across the fields. The spy has confirmed it.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim)),

                // ── 绑架 ──
                EventType.Kidnapping => impending
                    // 事件叙事兜底：绑架（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_kidnapping_impending",
                        "Spy report — {INSTIGATOR} has been seen around {LOCATION} often lately, quietly probing {VICTIM}'s daily movements. The spy judges: this one means {VICTIM} harm, and it is only a matter of time.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim))
                    // 事件叙事兜底：绑架（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_kidnapping_consummated",
                        "Urgent — {VICTIM} has been abducted by {INSTIGATOR}. The family begs with no one to turn to; the kidnappers have yet to send a ransom demand. The spy is tracking their whereabouts.",
                        ("INSTIGATOR", instigator), ("VICTIM", victim)),

                // ── 饥荒 ──
                EventType.Famine => impending
                    // 事件叙事兜底：饥荒（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_famine_impending",
                        "Spy report — {LOCATION}'s granaries are nearly empty, and the folk have been living on wild greens for days. Unless outside help arrives, deaths by starvation are only a matter of time.",
                        ("LOCATION", loc))
                    // 事件叙事兜底：饥荒（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_famine_consummated",
                        "Urgent — famine in {LOCATION} has spiraled out of control. Grain is gone, and the dead lie in the streets. The spy shakes his head: we came too late.",
                        ("LOCATION", loc)),

                // ── 背叛 ──
                EventType.Betrayal => impending
                    // 事件叙事兜底：背叛（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_betrayal_impending",
                        "Spy report — {INSTIGATOR} has been dealing furtively with powers beyond {LOCATION}, and has grown ever colder toward {VICTIM}. The spy notes: this one has two minds, and {VICTIM} is still in the dark.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim))
                    // 事件叙事兜底：背叛（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_betrayal_consummated",
                        "Urgent — {LOCATION} has a traitor within. {INSTIGATOR} has betrayed {VICTIM} and made off with a large sum. The spy notes: that knife came from behind.",
                        ("LOCATION", loc), ("INSTIGATOR", instigator), ("VICTIM", victim)),

                // ── 债务陷阱 ──
                EventType.DebtTrap => impending
                    // 事件叙事兜底：债务陷阱（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_debt_trap_impending",
                        "Spy report — {INSTIGATOR} is pressing {VICTIM} hard over a debt whose interest has grown beyond any hope of repayment. Drag it out further and {VICTIM}'s land deed will change hands.",
                        ("INSTIGATOR", instigator), ("VICTIM", victim))
                    // 事件叙事兜底：债务陷阱（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_debt_trap_consummated",
                        "Urgent — {INSTIGATOR} has driven {VICTIM} to the wall. The deed was seized, and the family has lost their home. The spy notes: lawful robbery is crueler than any bandit.",
                        ("INSTIGATOR", instigator), ("VICTIM", victim)),

                // ── 情仇 ──
                EventType.RomanticConflict =>
                    // 事件叙事兜底：情仇（未发生）暗探急报
                    LWNTextHelper.ResolveCompound("LWN_pipeline_event_romantic_conflict",
                        "Spy report — someone in {LOCATION} is lovesick. {VICTIM} is tangled in a romance with no clean way out, and both families are losing face.",
                        ("LOCATION", loc), ("VICTIM", victim)),

                // ── 冤案 ──
                EventType.FalseAccusation => impending
                    // 事件叙事兜底：冤案（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_false_accusation_impending",
                        "Spy report — {INSTIGATOR} is spreading damaging words about {VICTIM} throughout {LOCATION}. The evidence is not yet set in stone, but the rumor is already abroad. The spy notes: unless someone steps in, a wrongful verdict will be set.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim))
                    // 事件叙事兜底：冤案（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_false_accusation_consummated",
                        "Urgent — {VICTIM} has been convicted. The spy notes: no evidence was ever found. This was not a trial; it was murder.",
                        ("VICTIM", victim)),

                // ── 继承争端 ──
                EventType.InheritanceDispute =>
                    // 事件叙事兜底：继承争端（未发生）暗探急报
                    LWNTextHelper.ResolveCompound("LWN_pipeline_event_inheritance_dispute",
                        "Spy report — the old clan head of {LOCATION} has passed. The heirs are at daggers drawn, and {VICTIM}'s claim is being openly challenged by {INSTIGATOR}.",
                        ("LOCATION", loc), ("VICTIM", victim), ("INSTIGATOR", instigator)),

                // ── 逃犯 ──
                EventType.Fugitive => impending
                    // 事件叙事兜底：逃犯（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_fugitive_impending",
                        "Spy report — a fugitive named {VICTIM} is hiding near {LOCATION}. The hunters have put a heavy price on this one's head, but the story may not be so simple. The spy advises my lord to look into it personally.",
                        ("LOCATION", loc), ("VICTIM", victim))
                    // 事件叙事兜底：逃犯（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_fugitive_consummated",
                        "Spy report — every trace of {VICTIM} is gone. Fled, perhaps, or dragged back by the hunters. {LOCATION} has returned to its uneasy calm.",
                        ("VICTIM", victim), ("LOCATION", loc)),

                // ── 贸易争端 ──
                EventType.TradeDispute => impending
                    // 事件叙事兜底：贸易争端（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_trade_dispute_impending",
                        "Spy report — {INSTIGATOR} is crushing {VICTIM}'s trade in {LOCATION}. Price-slashing, cut supply lines, whispered slander — dirty tricks, but nothing that has come to blows yet.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim))
                    // 事件叙事兜底：贸易争端（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_trade_dispute_consummated",
                        "Urgent — {INSTIGATOR} has cornered the market in {LOCATION}. {VICTIM}'s trade is ruined outright. The spy notes: a merchant's war can kill without a drop of blood.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim)),

                // ── 贵族冲突 ──
                EventType.NobleConflict => impending
                    // 事件叙事兜底：贵族冲突（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_noble_conflict_impending",
                        "Spy report — {INSTIGATOR} has been moving troops along {LOCATION}'s border lately. The spy judges {VICTIM} the likely target — the clash could flare at any moment.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim))
                    // 事件叙事兜底：贵族冲突（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_noble_conflict_consummated",
                        "Urgent — {INSTIGATOR} and {VICTIM} have come to blows on {LOCATION}'s border. Smoke and blood cover the field. The spy has confirmed the battle.",
                        ("INSTIGATOR", instigator), ("VICTIM", victim), ("LOCATION", loc)),

                // ── 圣物失窃 ──
                EventType.SacredTheft => impending
                    // 事件叙事兜底：圣物失窃（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_sacred_theft_impending",
                        "Spy report — {INSTIGATOR} has been sending people to scout around {LOCATION} of late; the aim seems to be the clan's heirloom relic. The spy notes: this one has coveted it for a long time — action is only a matter of time. Should the {VICTIM} clan lose the relic, their lineage would be broken.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim))
                    // 事件叙事兜底：圣物失窃（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_sacred_theft_consummated",
                        "Urgent — the heirloom relic of {LOCATION} was stolen last night. The trail at the scene points to {INSTIGATOR}'s men. The elders of the {VICTIM} clan bowed their heads — the lineage is broken. The spy has confirmed it.",
                        ("LOCATION", loc), ("INSTIGATOR", instigator), ("VICTIM", victim)),

                // ── 行刺 ──
                EventType.Assassination => impending
                    // 事件叙事兜底：行刺（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_assassination_impending",
                        "Spy report — {INSTIGATOR} has been skulking about, sending men to watch {VICTIM}'s movements near {LOCATION}. The spy notes: this is the portent of an assassination, and {VICTIM} is in grave danger. If my lord means to stop it, he must decide at once.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc), ("VICTIM", victim))
                    // 事件叙事兜底：行刺（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_assassination_consummated",
                        "Urgent — {VICTIM} has been slain in {LOCATION}. The spy has confirmed it, and the trail of the blade points to {INSTIGATOR}. Everyone in the region is on edge, wondering who is next.",
                        ("VICTIM", victim), ("LOCATION", loc), ("INSTIGATOR", instigator)),

                // ── 宿敌复仇 ──
                EventType.NemesisRevenge => impending
                    // 事件叙事兜底：宿敌复仇（未发生）暗探急报
                    ? LWNTextHelper.ResolveCompound("LWN_pipeline_event_nemesis_revenge_impending",
                        "Spy report — {INSTIGATOR} is coming for you. That old scar still aches. The spy notes: this one draws closer to {LOCATION} with every passing day.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc))
                    // 事件叙事兜底：宿敌复仇（已发生）急报
                    : LWNTextHelper.ResolveCompound("LWN_pipeline_event_nemesis_revenge_consummated",
                        "Spy report — {INSTIGATOR} has tracked you here to {LOCATION}. That old scar still aches. What was bound to come has come.",
                        ("INSTIGATOR", instigator), ("LOCATION", loc)),

                // 事件叙事兜底：未知事件类型的兜底情报
                _ => LWNTextHelper.ResolveCompound("LWN_pipeline_event_default",
                    "Spy report — something has happened in {LOCATION}; the details are still being investigated.",
                    ("LOCATION", loc))
            };
        }
    }
}
