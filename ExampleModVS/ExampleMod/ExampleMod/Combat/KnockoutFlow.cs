using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 击晕单管线（玩家/NPC 共享，2026-08-13 平权重构）：
    /// Roll（判定）→ PlayStrikeAnim（挥击起手）→ 起手延迟 → Resolve（结算）。
    /// 判定公式、属性估算、袭击记账、击晕落地、反击事件、目击广播全共享——
    /// 改成功率只改一处（重构前玩家 TryKnockoutAgent 与 NPC KnockoutInlineState 各写一份，
    /// 2026-08-13 用户裁定「尽量复用一个管线，只在必要的地方做差异化」）。
    ///
    /// 必要差异化（参数/内部判断）：
    /// ① maxRate：玩家 0.95 / NPC 0.85（NPC 非玩家，成功率上限收敛）；
    /// ② 挥击动画：玩家永远 as_human_warrior → SetPose（避免 native AnimationSystemData
    ///    替换触发异步 AI tick 竞态）；NPC 可能是村民 action set → ForcePlayAction
    ///    （战斗动作运行时不可达，action_set 继承链陷阱见 Knowledge/击晕机制…§二）；
    /// ③ 起手延迟：玩家 400ms / NPC 0.5s——调用方决定，Resolve 前自行等待；
    /// ④ 播报文案：第一人称 vs 第三人称视角不同，调用方自己播。
    /// </summary>
    public static class KnockoutFlow
    {
        /// <summary>击晕掷点结果（调用方播报用）。</summary>
        public sealed class RollResult
        {
            public bool Success;      // 掷点 ≥ 目标阈值（d20 风格：高分成功直觉）
            public float SuccessRate; // 成功率（UI 难度预览复用）
            public float Roll;        // 掷点 0..1
            public float Threshold;   // 目标阈值 = 1 − SuccessRate
            public bool IsChild;      // 儿童 100% 免疫（骨骼不兼容死亡动画，见 Knowledge/击晕机制…§三）
        }

        /// <summary>成功率（纯计算，不掷点）：UI 难度预览与 Roll 共用同一公式。
        /// 🔴 2026-08-21（debug 覆盖）：StealSuccessRateOverride ≥ 0 → 强制成功率（跳过公式与 maxRate 钳制，
        /// 调试语义：设多少就是多少；-1 = 关闭）。热改：custom.plan_debug steal_rate。</summary>
        public static float ComputeSuccessRate(Agent attacker, Agent target, float maxRate = 0.85f)
        {
            if (attacker == null || target == null) return 0.5f;
            float ov = Settings.Instance.StealSuccessRateOverride;
            if (ov >= 0f) return MathF.Max(0.05f, MathF.Min(0.95f, ov));
            var (aVigor, aControl) = AgentStatsHelper.GetAgentStats(attacker);
            var (tVigor, tControl) = AgentStatsHelper.GetAgentStats(target);
            float aSum = aVigor + aControl;
            float tSum = tVigor + tControl;
            return tSum > 0
                ? MathF.Max(0.05f, MathF.Min(maxRate, 0.5f * (aSum / (float)tSum)))
                : 0.85f;
        }

        /// <summary>击晕判定（纯函数）：attacker Vigor+Control vs target Vigor+Control 比率式；
        /// 儿童（monster StringId 含 "child"）成功率强制 0（100% 免疫）。</summary>
        public static RollResult Roll(Agent attacker, Agent target, float maxRate = 0.85f)
        {
            var r = new RollResult();
            if (target == null) { r.Threshold = 1f; return r; }

            string monsterId = target.Monster?.StringId;
            r.IsChild = monsterId?.Contains("child") == true;

            r.SuccessRate = ComputeSuccessRate(attacker, target, maxRate);
            r.Roll = MBRandom.RandomFloat;
            r.Threshold = 1f - r.SuccessRate;
            r.Success = !r.IsChild && r.Roll >= r.Threshold;
            return r;
        }

        /// <summary>挥击起手：面向目标 + 播攻击动画（有主手武器 act_1h_bash，空手 act_shield_bash）。
        /// 玩家永远 as_human_warrior → SetPose 跳过 SetActionSet（避免异步 AI tick 竞态）；
        /// NPC 可能是村民/农民 action set → ForcePlayAction 先切 as_human_warrior 再播放。</summary>
        public static void PlayStrikeAnim(Agent attacker, Agent target)
        {
            if (attacker == null || target == null || !attacker.IsActive()) return;

            AgentControlHelper.FaceToActor(attacker, target);

            string attackAnim = V.MainWpn(attacker) != EquipmentIndex.None ? "act_1h_bash" : "act_shield_bash";
            if (attacker.IsMainAgent)
                AgentControlHelper.SetPose(attacker, attackAnim);
            else
                AgentControlHelper.ForcePlayAction(attacker, attackAnim);
        }

        /// <summary>结算（起手延迟后调用一次）：无论成败袭击记账 → 成功击晕落地 / 失败反击
        /// / 儿童免疫不反击 → 目击广播（受害者始终排除）。</summary>
        public static void Resolve(Agent attacker, Agent target, RollResult r)
        {
            if (attacker == null || target == null || r == null) return;

            // 出手即是袭击，记账（受害者身价累计进 PendingWorldEvent，赔偿基础值）
            AgentAIController.Instance?.RecordAssaultVictim(target);
            // 🔴 2026-08-16（方案 G3①/K2）：犯罪感知（同场景随从记忆照写——亲历者，无第三方目击
            // 只影响世界层反应不影响随从亲历）+ 犯罪当场关切（有目击者 → 概率冒泡，延迟确认在
            // PlayerMissionEventLogic tick）。成功击晕 = Knockout 罪，失败反击 = AttackAlly 罪。
            AttackTriggerMissionLogic.ReportPlayerMisconduct(r.Success ? "Knockout" : "AttackAlly");

            if (r.Success && target.IsActive())
            {
                // 成功：目标倒地 + 击晕事件。★ 必须先标记受害者状态再广播第三方目击——
                // 否则证人 Brain 处理 WitnessCrime_GatherOnLook 时调 IsKnockedOut(victim)
                // 返回 false（event 尚未入队），罪行被错误归类为 Steal。
                AgentControlHelper.ForcePlayAction(target, "act_death_fall_front");
                target.SetScriptedFlags(Agent.AIScriptedFrameFlags.DoNotRun | Agent.AIScriptedFrameFlags.NoAttack);
                AgentAIController.Instance?.SendEventToAgent(target, "event_agent_knocked_out");
            }
            else if (!r.IsChild)
            {
                // 失败（非儿童）：目标察觉反击，直接进战斗（sight check 拦不住直接事件）
                AgentAIController.Instance?.SendEventToAgent(target, "event_agent_damaged", attacker, target);
            }
            // 儿童免疫：不反击（目击广播已发，周围成人会反应）

            // 第三方目击广播：受害者始终 exclude（背后出手 sight check 必然 false，受害者走直接事件）
            AgentAIController.Instance?.BroadcastEventInRange(
                target.Position, 20f, "WitnessCrime",
                exclude: new HashSet<Agent> { target },
                requireSight: true,
                attacker, target);
        }

        /// <summary>起立动画时长（秒）：act_stand_up_to_front 完整播放时长。起身期间禁止
        /// 原生战斗 AI 接管——否则动画通道被移动/攻击动画覆盖，人起不来（实机：随从躺着参战）。</summary>
        public const float RiseAnimDuration = 2.0f;

        /// <summary>
        /// 击晕起身（醒来释放路径共用，2026-08-14）：播放起立动画（act_stand_up_to_front，与倒地
        /// act_death_fall_front 配对：面朝下倒地 → 面朝下起身）+ 清除脑的击晕标记（IsStunned）。
        /// **返回起立动画时长（秒）**——调用方必须在该时长之后再入队战斗动作/交还原生 AI
        /// （KnockoutFlow.Resolve 播的是纯脚本动画，引擎原生 knockdown 状态机未介入，不会自动起立）。
        /// 调用方流程：ClearAllActions（清击晕 StayAction + 解锁旗标）→ StandUp → 延迟 → 战斗/回岗。
        /// </summary>
        public static float StandUp(Agent agent)
        {
            if (agent == null || !agent.IsActive()) return 0f;
            var brain = AgentAIController.GetBrainForAgent(agent);
            if (brain != null) brain.IsStunned = false;
            AgentControlHelper.ForcePlayAction(agent, "act_stand_up_to_front");
            return RiseAnimDuration;
        }
    }
}
