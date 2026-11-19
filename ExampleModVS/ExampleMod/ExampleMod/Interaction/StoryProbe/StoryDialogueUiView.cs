using System;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 511 剧情对话的**自建面板**（2026-11-19，用户裁定：只在 511 玩法对话里用）。
    ///
    /// 观感对齐原版对话流：左下角「金色名字 + 黑底长条台词」，右侧「黑底长条选项」，
    /// 悬停显示检定/后果说明，没有选项时给「继续 / 离开」。
    /// 但是它**不是**原版对话 UI 的替身：它不读 ConversationManager，也不启动引擎对话流
    /// （原因见 StoryDialogueUiRunner 顶部注释）。
    ///
    /// 层与输入：对话期间独占键盘与鼠标（玩家不能边走边聊），关面板必须把这两样一起还回去。
    /// </summary>
    public static class StoryDialogueUiView
    {
        // 与 IM 面板同序（400）：那一档已实机验证「Mission 内可以用鼠标点按钮」。
        private const int LayerOrder = 400;

        private static GauntletLayer _layer;
        private static ScreenBase _layerOwnerScreen;
        private static StoryDialogueVM _vm;
#if !MB2_V1212
        private static GauntletMovieIdentifier _movie;
#else
        private static IGauntletMovie _movie;
#endif
        private static bool _maskApplied;

        public static bool IsOpen => _layer != null;

        // ═══════════════════════════════════════════════════════════════
        // 开 / 关
        // ═══════════════════════════════════════════════════════════════

        public static bool Open()
        {
            if (_layer != null) { Refresh(); return true; }
            try
            {
                _vm = new StoryDialogueVM();
                _layer = V.NewLayer(LayerOrder, "StoryDialogueLayer");
                _movie = _layer.LoadMovie("StoryDialogueUi", _vm);
                ApplyInputMask();

                var screen = ScreenManager.TopScreen;
                if (screen != null)
                {
                    screen.AddLayer(_layer);
                    _layerOwnerScreen = screen;
                }
                else
                {
                    DebugLogger.Log("[StoryDialogueUI] open failed: no top screen");
                    Close();
                    return false;
                }
                Refresh();
                DebugLogger.Log($"[StoryDialogueUI] panel opened order={LayerOrder} screen={screen.GetType().Name}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryDialogueUI] open failed: {ex}");
                Close();
                return false;
            }
        }

        /// <summary>移除面板。幂等；层已随屏销毁时只清引用（摘层守卫三件套）。</summary>
        public static void Close()
        {
            HideTooltipNow();
            if (_layer == null)
            {
                _vm = null;
                _movie = null;
                _maskApplied = false;
                return;
            }

            bool layerDead = V.LayerFinalized(_layer);
            try
            {
                if (!layerDead)
                {
                    if (_movie != null)
                    {
                        _layer.ReleaseMovie(_movie);
                        _movie = null;
                    }
                    if (_layerOwnerScreen != null && _layerOwnerScreen.HasLayer(_layer))
                        _layerOwnerScreen.RemoveLayer(_layer);
                    else if (ScreenManager.TopScreen != null && ScreenManager.TopScreen.HasLayer(_layer))
                        ScreenManager.TopScreen.RemoveLayer(_layer);
                    _layer.InputRestrictions.ResetInputRestrictions();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryDialogueUI] close failed: {ex.Message}");
            }

            _layer = null;
            _layerOwnerScreen = null;
            _movie = null;
            _vm = null;
            _maskApplied = false;
        }

        public static void Refresh()
        {
            // 列表重建 = 旧 HintWidget 被销毁，引擎收不到 HoverEnd ⇒ 浮框会留在屏幕上。
            HideTooltipNow();
            try { _vm?.Refresh(); }
            catch (Exception ex) { DebugLogger.Log($"[StoryDialogueUI] refresh failed: {ex.Message}"); }
        }

        /// <summary>关掉引擎的悬停浮框（HoverEnd 收不到时的兜底）。</summary>
        public static void HideTooltipNow()
        {
            try { InformationManager.HideTooltip(); }
            catch (Exception ex) { DebugLogger.Log("[StoryDialogueUI] hide tooltip failed: " + ex.Message); }
        }

        public static void ShowHint(string hint)
        {
            try { _vm?.ShowHint(hint); } catch { }
        }

        public static void ClearHint()
        {
            try { _vm?.ClearHint(); } catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        // 每帧输入
        // ═══════════════════════════════════════════════════════════════

        private static float _diagTimer;

        public static void Tick(float dt)
        {
            if (_layer == null) return;
            if (!StoryDialogueUiRunner.IsRunning)
            {
                Close();
                return;
            }

            // 输入诊断（每 3 秒一条）：点击没反应时，先看这一行区分「层没拿到鼠标」与「按钮没被点到」。
            _diagTimer += dt;
            if (_diagTimer >= 3f)
            {
                _diagTimer = 0f;
                try
                {
                    var pos = Input.MousePositionRanged;
                    DebugLogger.Log($"[StoryDialogueUI] diag hit={_layer.IsHitThisFrame} focus={_layer.IsFocusLayer}"
                        + $" mask={_layer.InputRestrictions.InputUsageMask} mouse=({pos.X:F0},{pos.Y:F0})"
                        + $" opts={StoryDialogueUiRunner.Options.Count} hint={_vm?.IsHintVisible}");
                }
                catch (Exception ex) { DebugLogger.Log("[StoryDialogueUI] diag failed: " + ex.Message); }
            }

            try
            {
                int optionCount = StoryDialogueUiRunner.Options.Count;
                for (int i = 0; i < optionCount && i < 9; i++)
                {
                    if (Input.IsKeyPressed(DigitKey(i + 1)))
                    {
                        StoryDialogueUiRunner.Select(i);
                        return;
                    }
                }

                if (StoryDialogueUiRunner.IsContinuePending
                    && (Input.IsKeyPressed(InputKey.Enter) || Input.IsKeyPressed(InputKey.Space)))
                {
                    StoryDialogueUiRunner.Continue();
                    return;
                }

                if (Input.IsKeyPressed(InputKey.Escape))
                {
                    StoryDialogueUiRunner.End("player left");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryDialogueUI] tick failed: {ex.Message}");
            }
        }

        private static InputKey DigitKey(int n)
        {
            switch (n)
            {
                case 1: return InputKey.D1;
                case 2: return InputKey.D2;
                case 3: return InputKey.D3;
                case 4: return InputKey.D4;
                case 5: return InputKey.D5;
                case 6: return InputKey.D6;
                case 7: return InputKey.D7;
                case 8: return InputKey.D8;
                default: return InputKey.D9;
            }
        }

        private static void ApplyInputMask()
        {
            if (_layer == null || _maskApplied) return;
            try
            {
                // 对话框打开 = 独占鼠标与键盘：鼠标点选项，键盘不能同时操作角色。
                _layer.InputRestrictions.SetInputRestrictions(
                    true, InputUsageMask.MouseButtons | InputUsageMask.Keyboardkeys);
                _maskApplied = true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryDialogueUI] input mask failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 文本
        // ═══════════════════════════════════════════════════════════════

        public static string CurrentSpeakerName()
        {
            try
            {
                var node = StoryDialogueUiRunner.Node;
                var session = StoryDialogueUiRunner.Session;
                if (node == null) return "";
                string identity = node.Speaker;
                var agent = ResolveIdentity(session, identity);
                if (agent != null) return agent.Name?.ToString() ?? "";
                // 没有 Mission 内的 Agent（例如台外说话人）→ 用剧情登记名。
                string npcId = identity;
                var def = session?.Definition;
                if ((identity == "npc" || identity == "questioner")
                    && def?.Participants != null && def.Participants.Contains(identity))
                    npcId = session.Request?.ActorId;
                return StoryProbeMissionBehavior.NpcName(npcId) ?? "";
            }
            catch { return ""; }
        }

        public static string CurrentLineText()
        {
            try
            {
                var node = StoryDialogueUiRunner.Node;
                if (node == null) return "";
                string text = null;
                try { text = node.LazyNpcLine?.Invoke(); } catch (Exception ex) { DebugLogger.Log($"[StoryDialogueUI] line failed: {ex.Message}"); }
                if (string.IsNullOrEmpty(text)) text = node.NpcLine;
                return text ?? "";
            }
            catch { return ""; }
        }

        public static string ContinueLabel()
        {
            var node = StoryDialogueUiRunner.Node;
            bool endsHere = node == null || string.IsNullOrEmpty(node.NextNode);
            return endsHere
                ? LWNTextHelper.ResolveText("LWN_dialogue_leave", "Leave")
                : LWNTextHelper.ResolveText("LWN_dialogue_continue", "Continue");
        }

        private static Agent ResolveIdentity(StoryDialogueSession session, string identity)
        {
            if (string.IsNullOrEmpty(identity)) return null;
            if (identity == "player") return Agent.Main;
            var host = StoryProbeMissionBehavior.Current;
            if (host == null) return null;
            var def = session?.Definition;
            string actor = identity;
            if ((identity == "npc" || identity == "questioner")
                && def?.Participants != null && def.Participants.Contains(identity))
                actor = session?.Request?.ActorId;
            else if (def?.Actors != null && !def.Actors.Contains(identity))
                return null;
            return host.GetAgent(actor);
        }
    }
}