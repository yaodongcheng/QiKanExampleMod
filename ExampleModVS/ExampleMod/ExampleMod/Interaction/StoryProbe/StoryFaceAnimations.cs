using System;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **表情播放器**（2026-11-18 立）—— 原生面部表情的唯一出口。
    ///
    /// 表情是**顶点变形动画（morph）**，和骨骼动作相互独立、可同时播：
    /// 引擎资产 `Native/AssetPackages/morph_anims.tpac` 里每条片段都有名字，
    /// 名字原样传给 `Agent.SetAgentFacialAnimation`。全量名单（按版本）见
    /// `Scripts/face_animations.csv`（由 `Scripts/gen_face_animations.py` 生成），
    /// 机制与坑见 `Knowledge/骑砍2表情系统_引擎能力与实现.md`。
    ///
    /// 🔴 **通道选 Mid（1），不用 High**：原版对话在每句播完后会把 **High** 通道清空
    ///    （`MissionConversationLogic.OnConversationPlay`：这句没有 `[if:]` 标签就 `SetAgentFacialAnimation(High, "", false)`）。
    ///    我们的台词没有标签，写 High 会被紧接着清掉；Mid 是原版给"对话待机表情"用的通道，
    ///    同一时刻只被它覆盖，正是"这一句的表情"该待的地方。会话结束时原版会自动清 Mid。
    ///
    /// 🔴 名字写错**不会崩、也不会报给玩家**，只在引擎日志留
    ///    `Morph animation could not be found: xxx` —— 所以内容侧必须过离线闸门
    ///    （`Scripts/check_story_probe.py` 的 `Face` 校验）。
    /// </summary>
    public static class StoryFaceAnimations
    {
        /// <summary>中性待机表情：原版对话默认脸（`CharacterHelper.GetDefaultFaceIdle` 的基线值）。</summary>
        public const string Neutral = "convo_normal";

        /// <summary>给某个 agent 设一个持续表情；空名 = 不做任何事（保持上一句）。</summary>
        public static void Play(Agent agent, string face)
        {
            if (agent == null || string.IsNullOrEmpty(face)) return;
            if (!AgentControlHelper.SafeIsActive(agent)) return;
            if (!agent.IsHuman) return; // 只有人形有脸（马、动物没有面部变形帧）
            try
            {
                agent.SetAgentFacialAnimation(Agent.FacialAnimChannel.Mid, face, loop: true);
                DebugLogger.Log($"[StoryFace] agent#{agent.Index} <- {face}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryFace] play '{face}' failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 这一句的表情。写了名字 = 播它；空名字 = 回到该角色**原版默认待机脸**
        /// （`CharacterHelper.GetDefaultFaceIdle`，原版开对话时也是用它），不会一路延续上一句。
        /// </summary>
        public static void PlayOrDefault(Agent agent, string face)
        {
            if (agent == null) return;
            if (!string.IsNullOrEmpty(face)) { Play(agent, face); return; }
            if (!AgentControlHelper.SafeIsActive(agent) || !agent.IsHuman) return;
            string idle = Neutral;
            try
            {
                var character = agent.Character as CharacterObject;
                if (character != null)
                {
                    string defaultIdle = CharacterHelper.GetDefaultFaceIdle(character);
                    if (!string.IsNullOrEmpty(defaultIdle)) idle = defaultIdle;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[StoryFace] default idle lookup failed, fallback '" + Neutral + "': " + ex.Message);
            }
            Play(agent, idle);
        }

        /// <summary>清回中性脸（不传名字时用它，而不是传 ""，避免不同版本对空名的处理差异）。</summary>
        public static void Reset(Agent agent)
        {
            Play(agent, Neutral);
        }

        /// <summary>当前表情名（引擎回报；没设过返回空）。</summary>
        public static string Current(Agent agent)
        {
            try { return agent?.GetAgentFacialAnimation() ?? ""; }
            catch (Exception ex) { return "ERR: " + ex.Message; }
        }

        /// <summary>诊断：能不能播只看引擎能不能在资产包里找到这个名字（找不到就是静默无反应）。</summary>
        public static string Diagnose(Agent agent, string face)
        {
            if (agent == null) return "no agent";
            if (string.IsNullOrEmpty(face)) return "empty face name";
            try
            {
                agent.SetAgentFacialAnimation(Agent.FacialAnimChannel.Mid, face, loop: true);
                return $"OK: requested '{face}' (current='{Current(agent)}'); " +
                       "if nothing shows, the engine log says 'Morph animation could not be found'";
            }
            catch (Exception ex)
            {
                return $"ERR: {ex.Message}";
            }
        }
    }
}
