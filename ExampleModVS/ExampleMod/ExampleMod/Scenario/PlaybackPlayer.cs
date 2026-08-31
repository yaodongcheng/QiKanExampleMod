using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 演绎播放器（W5 最小可播）：把 story/ 分件一行行喂给 PlaybackDialog 面板。
    /// 推进模型 = 链式回调（VM.OnClosed → 下一行）：dialogue/narrator 行显示后挂起等玩家点"继续"（阻塞）；choice 行弹选项；
    /// 行级 when 门控（05 纪律 1，同源判定 = DslEvaluator）；文本 = textKey → LWNTextHelper.ResolveText（铁律 13；fallback = 中文原文）；
    /// 说话人名 = 引用 → 引擎显示名（找不到 = 原样，不崩——铁律 1）；Ctx::choice 选择槽 = "opt0/opt1…"（08b 纪律）；
    /// actor_enter/move/leave/camera = TODO 日志（T6 舞台执行）；演完 = 面板关闭 + IsPlaying 复位。
    /// 🔴 接缝（T6）：ScenarioExecutor 的 perform 桩 → 本播放器（Future：OnFinished 回调恢复执行器）。
    /// </summary>
    public static class PlaybackPlayer
    {
        public static string CurrentPlaybackId { get; private set; }
        public static bool IsPlaying => CurrentPlaybackId != null;

        private static List<PlaybackLine> _lines;
        private static int _idx;
        private static Action _onFinished;    // W6 恢复执行器用（当前 = null）

        /// <summary>开始演一段（已在演 = 拒绝 + 日志）</summary>
        public static void Play(string playbackId, Action onFinished = null)
        {
            try
            {
                if (IsPlaying) { DebugLogger.Log($"[Playback] 已在演 {CurrentPlaybackId}，拒绝 {playbackId}"); return; }
                var def = ScenarioLoader.FindPlayback(playbackId);
                if (def == null || def.Lines == null)
                {
                    DebugLogger.Log($"[Playback] 找不到分件 {playbackId}（先 custom.scn_list/all 看清单）");
                    return;
                }
                _lines = def.Lines;
                _idx = 0;
                _onFinished = onFinished;
                CurrentPlaybackId = playbackId;
                ScenarioContext.Instance.Set("choice", null);   // 事件上下文复用：选择槽每段清一次
                PlaybackDialogUI.VM.OnClosedHandler = null;
                PlaybackDialogUI.Open();
                DebugLogger.Log($"[Playback] ▶ 开始 {playbackId}（{def.Form}，{def.Lines.Count} 行）");
                MoveNext();
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[Playback] Play({playbackId}) 异常（不崩）: {e.Message}");
                Finish();
            }
        }

        private static void MoveNext()
        {
            while (_lines != null && _idx < _lines.Count)
            {
                var line = _lines[_idx++];
                try
                {
                    // 行级 when 门控（05 纪律 1）：不满足 = 跳行
                    if (!string.IsNullOrEmpty(line.When) && !DslEvaluator.Evaluate(line.When))
                        continue;

                    switch (line.Cmd)
                    {
                        case "dialogue":
                        case "monologue":
                        case "narrator":
                        {
                            string speakerName = line.Cmd == "narrator" ? "" : ResolveSpeakerName(line.Speaker);
                            PlaybackDialogUI.VM.OnClosedHandler = MoveNext;   // 点继续 → 下一行
                            PlaybackDialogUI.VM.Show(
                                speakerName,
                                LWNTextHelper.ResolveText(line.TextKey ?? "", line.Text),
                                null);                                // 立绘槽 W6 接入（缺 = 占位隐藏）
                            return;                                    // 挂起等回调
                        }
                        case "choice":
                        {
                            var options = line.Options ?? new List<PlaybackOption>();
                            var vms = new List<StoryOptionVM>();
                            int chosen = -1;
                            for (int i = 0; i < options.Count; i++)
                            {
                                int idx = i;
                                vms.Add(new StoryOptionVM(
                                    LWNTextHelper.ResolveText(options[i].TextKey ?? "", options[i].Text),
                                    () => chosen = idx));
                            }
                            PlaybackDialogUI.VM.OnClosedHandler = () =>
                            {
                                ScenarioContext.Instance.Set("choice", "opt" + chosen.ToString(System.Globalization.CultureInfo.InvariantCulture));
                                MoveNext();
                            };
                            PlaybackDialogUI.VM.ShowOptions(vms);
                            return;
                        }
                        case "effect":
                            ScenarioActions.Execute(new ScenarioScriptStep
                            {
                                Step = "effect", Action = line.Get("action"),
                                Extra = line.Extra != null ? FilterExtra(line.Extra) : null,
                            }, ScenarioContext.Instance);
                            break;
                        case "note":
                            DebugLogger.Log($"[Playback][Note] {line.TextKey ?? line.Text ?? ""}");
                            break;
                        case "actor_enter":
                        case "actor_move":
                        case "actor_leave":
                        case "camera":
                        case "actor_action":
                        case "scene_enter":
                        case "scene_exit":
                            DebugLogger.Log($"[Playback][TODO] 舞台行 {line.Cmd}（T6 舞台执行/镜头）——跳过");
                            break;
                        case "bgm_change":
                        case "se_start":
                        case "se_stop":
                        case "se_loop":
                            DebugLogger.Log($"[Playback][TODO] 音频行 {line.Cmd}（W6 音频）——跳过");
                            break;
                        default:
                            DebugLogger.Log($"[Playback] 未知行 {line.Cmd}（防御跳过）");
                            break;
                    }
                }
                catch (Exception e)
                {
                    DebugLogger.Log($"[Playback] 行 {line} 异常（跳过继续）: {e.Message}");
                }
            }
            Finish();
        }

        private static Dictionary<string, Newtonsoft.Json.Linq.JToken> FilterExtra(Dictionary<string, Newtonsoft.Json.Linq.JToken> extra)
        {
            return new Dictionary<string, Newtonsoft.Json.Linq.JToken>(extra);   // 原样透传（effect 参数）
        }

        private static string ResolveSpeakerName(string speakerRef)
        {
            if (string.IsNullOrEmpty(speakerRef)) return "";
            try
            {
                if (speakerRef.StartsWith("Hero::"))
                {
                    var h = AttributeResolver.FindHero(speakerRef);
                    if (h != null) return h.Name.ToString();
                }
                if (speakerRef == "Hero::MainHero")
                    return TaleWorlds.CampaignSystem.Hero.MainHero.Name.ToString();
                return speakerRef;   // Agent::/Ctx:: 槽/未知 = 原样（T6 接模板转名；不崩）
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[Playback] 说话人名解析失败（原样）: {speakerRef} → {e.Message}");
                return speakerRef;
            }
        }

        private static void Finish()
        {
            PlaybackDialogUI.VM.OnClosedHandler = null;
            PlaybackDialogUI.Close();
            var finished = _onFinished;
            string id = CurrentPlaybackId;
            _lines = null;
            CurrentPlaybackId = null;
            _onFinished = null;
            DebugLogger.Log($"[Playback] ■ {id} 演完（面板关闭）");
            try { finished?.Invoke(); } catch (Exception e) { DebugLogger.Log($"[Playback] 完成回调异常: {e.Message}"); }
        }

        /// <summary>中止（Esc/异常调用方）</summary>
        public static void Abort()
        {
            Finish();
        }
    }
}
