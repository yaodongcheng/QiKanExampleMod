using System;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **台词配动作的播放器**（2026-11-13 立；同日改版：数据不再单独成表）。
    ///
    /// 🔴 动作写在**对话节点自己身上**（`Dialogues/*.dialog.json` 的 `LineActions`，
    ///    与 `TextKey` 的行数一一对应）。曾经的 `StoryLineActions.json` 已删 —— 一句话和它的动作
    ///    本来就该长在一起，分两张表必然会漂移。
    ///
    /// 🔴 动作必须在该 NPC 的 action set 里（人形 = `as_human_warrior`，CLAUDE.md 铁律 41）。
    ///    挑动作走 `.claude/skills/story-line-action/SKILL.md`；离线闸门在
    ///    `Scripts/check_story_probe.py`（`NPC cannot play it`）。
    /// </summary>
    public static class StoryLineActions
    {
        /// <summary>真实台词节点展示时播放该句的动作；下一句由原版推进，不靠定时器。</summary>
        public static void Play(StoryProbeMissionBehavior host, Agent agent, string[] acts)
        {
            if (host == null || agent == null || acts == null) return;
            if (acts.Length > 1)
            { DebugLogger.Log("[StoryLineActions] rejected multiple actions on a single-sentence node"); return; }
            if (acts.Length == 1 && !string.IsNullOrEmpty(acts[0])) PlayOne(agent, acts[0], 0);
        }

        private static void PlayOne(Agent agent, string act, int index)
        {
            if (agent == null || !AgentControlHelper.SafeIsActive(agent)) return;
            try
            {
                AgentControlHelper.ForcePlayAction(agent, act, restoreAfter: true);
                DebugLogger.Log($"[StoryLineActions] #{index} -> {act}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryLineActions] play '{act}' failed: {ex.Message}");
            }
        }

        /// <summary>诊断：这条动作对**这个 agent** 到底能不能播（查不到 ⇒ 播了也没反应）。</summary>
        public static string Diagnose(Agent agent, string act)
        {
            if (agent == null) return "no agent";
            try
            {
                var set = agent.ActionSet;
                int index = MBActionSet.GetAnimationIndexOfAction(set, ActionIndexCache.Create(act));
                return index > 0
                    ? $"OK: '{act}' resolves in this agent's action set (index={index})"
                    : $"NO: '{act}' is NOT in this agent's action set (plays nothing)";
            }
            catch (Exception ex)
            {
                return $"ERR: {ex.Message}";
            }
        }
    }
}
