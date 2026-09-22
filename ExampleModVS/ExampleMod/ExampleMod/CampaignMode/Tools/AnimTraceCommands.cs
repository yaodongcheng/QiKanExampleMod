using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// **逐帧骨骼取证**（2026-09-22 立）—— 回答一个 ModKit 答不了的问题：
    /// **引擎「实际渲染出来」的姿势，跟 TRF 里写的是不是一回事。**
    ///
    /// 【为什么需要它】用户实机反馈：同一条动画在 ModKit 里平滑、在游戏里"腿摆得生硬"。
    /// 离线侧已逐条验过（源 FBX 31 关键帧 → TRF 31 帧 1:1、逐骨动作量与源差 3 位小数、
    /// 循环闭合 ≤0.18°、包里 `cyclic` 标志 19 条全对），**数据是干净的**。
    /// 而引擎把动画编译成 `OptimizedAnimation` 时会**逐骨削关键帧**（按"骨活跃度"位图，
    /// 见 `tools/tpactool/TpacTool.Lib/AnimationClip/OptimizedAnimation.cs`），这一步离线读不出来
    /// （TpacTool 的 `ReadData` 与真实数据不兼容，实测抛 `Frames not equal: 102 - -1863246975`）。
    /// ⇒ **只剩"在游戏里把每帧姿势打出来"这一条路。**
    ///
    /// 【怎么读】判据 = 把日志里的**逐帧转角**与 TRF 的逐帧转角对比：
    ///   · 日志平滑（每帧都在动、幅度接近）      ⇒ 引擎没削键，生硬来自素材本身
    ///   · 日志成段为 0 然后突然跳一下（阶梯）    ⇒ **引擎削键了**，要回 ModKit 调压缩/或改用别的导入方式
    ///   · `spd` 不是 1.00                       ⇒ 引擎在按速度重定时（另一类问题）
    ///   · `anim` 不是我们设的那条                ⇒ 通道被抢（第三类问题）
    ///
    /// 🔴 本件是**一次性诊断工具**，不是成品：诊断完按 §3.9 清场纪律删掉（或一直留着也行，
    ///    未 arm 时每帧只判一个 bool，零开销）。
    /// </summary>
    public class AnimTraceBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        /// <summary>全局唯一一份（命令侧通过它 arm）。</summary>
        public static AnimTraceBehavior Instance { get; private set; }

        private bool _armed;
        private Agent _agent;
        private string _actionName;
        private float _duration;
        private float _elapsed;
        private int _frame;
        private bool _allBones;
        private bool _raw;

        /// <summary>raw 档固定打这几根（含 pelvis —— 判定"脉冲是不是从根来的"要靠它）。</summary>
        private static readonly string[] RawBones =
        {
            "pelvis", "l_thigh", "l_calf", "l_foot", "r_thigh", "r_calf", "r_foot"
        };

        private readonly Dictionary<int, MatrixFrame> _prevFrame = new Dictionary<int, MatrixFrame>();
        private readonly List<int> _bones = new List<int>();
        private readonly List<string> _names = new List<string>();

        public override void OnCreated()
        {
            Instance = this;
            base.OnCreated();
        }

        public override void OnRemoveBehavior()
        {
            if (Instance == this)
                Instance = null;
            base.OnRemoveBehavior();
        }

        /// <summary>开始一段取证（由控制台命令调）。</summary>
        public void Arm(Agent agent, string actionName, float seconds, bool allBones, bool raw)
        {
            _agent = agent;
            _actionName = actionName;
            _duration = seconds > 0f ? seconds : 2f;
            _allBones = allBones;
            _raw = raw;
            _elapsed = 0f;
            _frame = 0;
            _prevFrame.Clear();
            _bones.Clear();
            _names.Clear();
            _armed = true;

            // 先把动作设上（与 custom.do_anim 同一套参数，保证两边可比）
            try
            {
                ActionIndexCache idx = ActionIndexCache.Create(actionName);
                if (idx == ActionIndexCache.act_none)
                {
                    DebugLogger.Log($"[AnimTrace] 动作 '{actionName}' 解析为 act_none —— 没注册，取证无意义");
                    _armed = false;
                    return;
                }
                // 🔴 与状态机同参数（含 `blendOutPeriodToNoAnim: 0`）—— 取证必须量生产路径，
                //    否则量到的不是飞行实际播的那一下。
                agent.SetActionChannel(0, idx, ignorePriority: true, blendInPeriod: 0.3f,
                                       blendOutPeriodToNoAnim: 0f);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AnimTrace] 设动作异常: {ex.Message}");
                _armed = false;
                return;
            }

            // 把骨序打一遍（引擎侧的名字/索引，用来和 TRF 的索引对号）
            try
            {
                Skeleton skel = agent.AgentVisuals?.GetSkeleton();
                if (skel == null)
                {
                    DebugLogger.Log("[AnimTrace] 拿不到 skeleton —— 该 agent 没有可视件？");
                    _armed = false;
                    return;
                }
                int n = skel.GetBoneCount();
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    string nm = skel.GetBoneName((sbyte)i) ?? "?";
                    _names.Add(nm);
                    bool pick = _raw
                        ? Array.IndexOf(RawBones, nm) >= 0
                        : (_allBones || IsLegBone(nm));
                    if (pick)
                        _bones.Add(i);
                    sb.Append(i).Append(':').Append(nm).Append(' ');
                }
                DebugLogger.Log($"[AnimTrace] 引擎骨序（{n} 根）: {sb}");
                DebugLogger.Log($"[AnimTrace] 本次取证骨 = {_bones.Count} 根（{(_allBones ? "全部" : "腿链筛法")}）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AnimTrace] 读骨序异常: {ex.Message}");
                _armed = false;
                return;
            }

            DebugLogger.Log($"[AnimTrace] === 开始：'{actionName}' {_duration:F1} 秒（每帧一行；" +
                            $"判据见 AnimTraceBehavior 类注释）===");
        }

        public override void OnMissionTick(float dt)
        {
            if (!_armed)
                return;

            try
            {
                if (_agent == null || !_agent.IsActive())
                {
                    DebugLogger.Log("[AnimTrace] agent 失效，终止取证");
                    _armed = false;
                    return;
                }
                Skeleton skel = _agent.AgentVisuals?.GetSkeleton();
                if (skel == null)
                    return;

                float prog = -1f, spd = -1f;
                string anim = "?";
                try
                {
                    prog = skel.GetAnimationParameterAtChannel(0);
                    spd = skel.GetAnimationSpeedAtChannel(0);
                    anim = skel.GetAnimationAtChannel(0) ?? "?";
                }
                catch { /* 引擎偶尔取不到，不影响骨骼读数 */ }

                StringBuilder sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "[AnimTrace] f={0:D3} t={1:F3} anim='{2}' prog={3:F3} spd={4:F2} |",
                    _frame, _elapsed, anim, prog, spd);

                for (int k = 0; k < _bones.Count; k++)
                {
                    int bi = _bones[k];
                    MatrixFrame cur = skel.GetBoneEntitialFrameWithIndex((sbyte)bi);
                    if (_raw)
                    {
                        // 原始基轴（9 个分量）—— 用来判两件事：
                        //   ① 矩阵是否正交（非正交 = 读到了半更新状态，测量作废）
                        //   ② 父子骨之间的相对角（= 膝/踝到底有没有在弯）
                        sb.AppendFormat(CultureInfo.InvariantCulture, " {0} f=({1:F3},{2:F3},{3:F3}) u=({4:F3},{5:F3},{6:F3}) s=({7:F3},{8:F3},{9:F3})",
                            _names[bi],
                            cur.rotation.f.X, cur.rotation.f.Y, cur.rotation.f.Z,
                            cur.rotation.u.X, cur.rotation.u.Y, cur.rotation.u.Z,
                            cur.rotation.s.X, cur.rotation.s.Y, cur.rotation.s.Z);
                    }
                    else if (_prevFrame.TryGetValue(bi, out MatrixFrame prev))
                    {
                        float deg = RotDeltaDeg(prev, cur);
                        float m = PosDelta(prev, cur);
                        sb.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:F2}d/{2:F4}m",
                                        _names[bi], deg, m);
                    }
                    _prevFrame[bi] = cur;
                }

                DebugLogger.Log(sb.ToString());

                _frame++;
                _elapsed += dt;
                if (_elapsed >= _duration)
                {
                    DebugLogger.Log($"[AnimTrace] === 结束：{_frame} 帧 / {_elapsed:F2} 秒 ===");
                    _armed = false;
                }
            }
            catch (Exception ex)
            {
                // 取证工具绝不能把游戏带崩
                DebugLogger.Log($"[AnimTrace] tick 异常，终止取证: {ex.Message}");
                _armed = false;
            }
        }

        /// <summary>腿链（大腿 / 小腿 / 脚 / 脚尖），按引擎骨名模糊匹配。</summary>
        private static bool IsLegBone(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            return s.Contains("thigh") || s.Contains("calf") || s.Contains("foot") || s.Contains("toe");
        }

        /// <summary>
        /// 相邻两帧之间的**转角代理值**（度）。
        ///
        /// 🔴 为什么不直接算"旋转矩阵的夹角"：那要先把 `Mat3` 转四元数，而 `Mat3` 的
        ///    f/s/u 是行还是列、引擎怎么排 —— 猜错就全错（铁律：反编译禁瞎猜）。
        ///    这里改用**三条基轴各自转了多少**取最大值：只用到点积，**与坐标系约定无关**，
        ///    且对任意旋转 θ 恒有 θ/√3 ≤ 本值 ≤ θ —— 判"平滑还是阶梯"足够。
        /// </summary>
        private static float RotDeltaDeg(MatrixFrame a, MatrixFrame b)
        {
            float d1 = AxisDeg(a.rotation.f, b.rotation.f);
            float d2 = AxisDeg(a.rotation.u, b.rotation.u);
            float d3 = AxisDeg(a.rotation.s, b.rotation.s);
            return Math.Max(d1, Math.Max(d2, d3));
        }

        private static float AxisDeg(Vec3 p, Vec3 q)
        {
            float dot = p.X * q.X + p.Y * q.Y + p.Z * q.Z;
            float lp = (float)Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            float lq = (float)Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
            if (lp < 1e-6f || lq < 1e-6f)
                return 0f;
            dot /= (lp * lq);
            if (dot > 1f) dot = 1f;
            if (dot < -1f) dot = -1f;
            return (float)(Math.Acos(dot) * 180.0 / Math.PI);
        }

        private static float PosDelta(MatrixFrame a, MatrixFrame b)
        {
            float dx = a.origin.X - b.origin.X;
            float dy = a.origin.Y - b.origin.Y;
            float dz = a.origin.Z - b.origin.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    /// <summary>
    /// 控制台入口 <c>custom.anim_trace</c>（2026-09-22）。逐帧骨骼取证，见 <see cref="AnimTraceBehavior"/>。
    ///
    /// <code>
    /// custom.anim_trace                          默认：act_fly_boost 跑 2 秒
    /// custom.anim_trace act_fly_cruise           指定动作，2 秒
    /// custom.anim_trace act_fly_boost 3          跑 3 秒
    /// custom.anim_trace act_fly_boost 3 all      不筛骨，全部 28 根都打
    /// custom.anim_trace 1                        首参可弃：认不出就用默认动作
    /// </code>
    ///
    /// 🔴 两条项目纪律：返回文本**纯英文**（要显示在游戏内控制台）；**首参可弃**。
    /// </summary>
    public static class AnimTraceCommands
    {
        private const string DefaultAction = "act_fly_boost";

        [CommandLineFunctionality.CommandLineArgumentFunction("anim_trace", "custom")]
        public static string ExecuteAnimTrace(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
                return "error: must be in a scene/mission to use this command.";

            AnimTraceBehavior beh = Mission.Current.GetMissionBehavior<AnimTraceBehavior>();
            if (beh == null)
                return "error: AnimTraceBehavior is not registered in this mission "
                     + "(check MySubModule.OnMissionBehaviorInitialize).";

            // ① 动作名：首参可弃 —— 认不出（不以 act_ 开头）就回落默认，并在返回里注明
            string action = DefaultAction;
            string note = string.Empty;
            if (args != null && args.Count >= 1 && !string.IsNullOrWhiteSpace(args[0]))
            {
                if (args[0].StartsWith("act_", StringComparison.OrdinalIgnoreCase))
                    action = args[0];
                else
                    note = $" [note: '{args[0]}' is not an action name -> using {DefaultAction}]";
            }

            // ② 时长（秒）
            float seconds = 2f;
            if (args != null && args.Count >= 2)
            {
                float v;
                if (float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0f)
                    seconds = v;
            }

            // ③ 全骨 / raw 档
            bool all = args != null && args.Count >= 3 &&
                       string.Equals(args[2], "all", StringComparison.OrdinalIgnoreCase);
            bool raw = args != null && args.Count >= 3 &&
                       string.Equals(args[2], "raw", StringComparison.OrdinalIgnoreCase);

            beh.Arm(Agent.Main, action, seconds, all, raw);
            return $"OK: tracing '{action}' for {seconds:F1}s on {Agent.Main.Name} "
                 + $"({(raw ? "RAW basis vectors" : all ? "all bones" : "leg chain")}). "
                 + $"read Debug/StoryEngine_RuntimeLog.txt, lines tagged [AnimTrace].{note}";
        }
    }
}
