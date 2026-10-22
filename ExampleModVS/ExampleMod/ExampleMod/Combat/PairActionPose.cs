using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 双人动作的**站位解算**（2026-10-19）。
    ///
    /// 只做纯数学 + 地面查询，不碰播放/控制权。坐标口径与 `trf` 完全一致：
    ///   · **+Y = 角色正前**、**+X = 角色右手侧**、**+Z = 上**；
    ///   · 偏航角 `yaw` 用 `atan2(x, y)`（度），0 = 正前，+90 = 右手侧。
    ///
    /// `stance` 的方向：**defender 相对 attacker**，写在 attacker 的本地系里。
    /// 发起者的目标点 = `defender.Position − R(attackerYaw) × (right, forward)`。
    /// </summary>
    public static class PairActionPose
    {
        /// <summary>两人落点高度差上限（米）——超过就拒绝（台阶/屋顶/悬空）。</summary>
        public const float MaxHeightDelta = 1.5f;

        /// <summary>地面拿不到时的哨兵判据（与钩索/飞行同口径）。</summary>
        public static bool IsValidGround(float z)
            => !float.IsNaN(z) && z < 1e5f && z > -1e5f;

        // ───────────────────────────── 朝向 ─────────────────────────────

        /// <summary>
        /// 取"某个 agent 现在面朝哪"（水平、已归一化）。
        /// 人优先 look；坐骑优先 moveDir（马不会横着走）。四个源依次试，全失败才退回世界 +Y（并打日志）。
        /// 范本 = `GrappleCarry.ForwardOf`（2026-10-14 实机验证）。
        /// </summary>
        public static Vec3 ForwardOf(Agent a, bool mount = false)
        {
            Vec3 f;
            if (a == null)
                return Vec3.Forward;

            if (mount)
            {
                if (TryFlat(() => a.GetMovementDirection().ToVec3(), out f)) return f;
                if (TryFlat(() => a.Frame.rotation.f, out f)) return f;
            }
            if (TryFlat(() => a.LookFrame.rotation.f, out f)) return f;
            if (TryFlat(() => a.Frame.rotation.f, out f)) return f;
            if (TryFlat(() => a.GetMovementDirection().ToVec3(), out f)) return f;

            DebugLogger.Log($"[Pair] 🔴 读不到 '{Describe(a)}' 的朝向（look/frame/moveDir 全空）—— 退回世界 +Y");
            return Vec3.Forward;
        }

        private static bool TryFlat(Func<Vec3> get, out Vec3 flat)
        {
            flat = Vec3.Forward;
            try
            {
                Vec3 v = get();
                v.z = 0f;
                if (v.LengthSquared > 1e-4f)
                {
                    flat = v.NormalizedCopy();
                    return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>水平朝向 → 偏航角（度，`atan2(x,y)`）。</summary>
        public static float YawDegOf(Vec3 forward)
        {
            forward.z = 0f;
            if (forward.LengthSquared < 1e-6f)
                return 0f;
            return MathF.Atan2(forward.x, forward.y) * (180f / MathF.PI);
        }

        /// <summary>把角度折到 (-180, 180]。</summary>
        public static float Normalize180(float deg)
        {
            while (deg > 180f) deg -= 360f;
            while (deg <= -180f) deg += 360f;
            return deg;
        }

        /// <summary>偏航角（度）→ 前向量（+Y 起算，+90 = 右手侧）。</summary>
        public static Vec3 ForwardFromYaw(float yawDeg)
        {
            float r = yawDeg * (MathF.PI / 180f);
            return new Vec3(MathF.Sin(r), MathF.Cos(r), 0f);
        }

        /// <summary>偏航角（度）→ 右向量（前向 +90°）。</summary>
        public static Vec3 RightFromYaw(float yawDeg)
        {
            float r = yawDeg * (MathF.PI / 180f);
            return new Vec3(MathF.Cos(r), -MathF.Sin(r), 0f);
        }

        // ───────────────────────────── 站位 ─────────────────────────────

        /// <summary>
        /// 算**发起者**应该站到哪、两人起播时各朝哪。
        ///
        /// 🔴 **2026-10-22 起：按「触发那一刻的连线」定最终朝向**（用户裁定）——
        ///   旧模型 = 发起者保持当前朝向、走到 `D − R(当前朝向)×stance`（可能是横向平移）；
        ///   新模型 = 让 stance 向量指向连线方向 ⇒ 发起者**只沿连线走**，两人一起转身。
        ///
        ///   由 stance 指回连线：
        ///     stance 在发起者本地系里的方位角 = `atan2(Right, Forward)`（扛人 = -83.4°）
        ///     最终朝向 = 连线方位角 − 上述方位角
        ///   站位点 = `D − u × |stance|`（沿连线离目标 |stance| 米；近了就是沿连线退一点）。
        ///   `defenderYaw = attackerYaw + Stance_Direction`。
        ///
        /// `lineDir` = **冻结的**“发起者→目标”水平方向（零向量 = 现算）。
        /// 发起点在触发时冻结，PrePosition 期间只用它 + 目标实时位置重算 —— 发起者走歪了也会回到连线。
        /// </summary>
        public static bool TryComputeStartPose(PairActionDef def, Agent attacker, Agent defender,
            Vec3 lineDir, out Vec3 attackerSpot, out float attackerYawDeg, out float defenderYawDeg, out string why)
        {
            attackerSpot = Vec3.Zero;
            attackerYawDeg = 0f;
            defenderYawDeg = 0f;
            why = "-";

            if (def == null || attacker == null || defender == null)
            {
                why = "def / attacker / defender is null";
                return false;
            }
            if (!AgentControlHelper.SafeIsActive(attacker) || !AgentControlHelper.SafeIsActive(defender))
            {
                why = "attacker or defender is not active";
                return false;
            }

            float stanceLen = MathF.Sqrt(def.Stance.Right * def.Stance.Right
                                       + def.Stance.Forward * def.Stance.Forward);

            // 连线方向：优先用冻结值，否则现算（触发那一刻）。
            Vec3 u = Vec3.Zero;
            bool haveLine = false;
            if (lineDir.LengthSquared > 1e-6f)
            {
                u = lineDir.NormalizedCopy();
                haveLine = true;
            }
            else
            {
                Vec3 toDef = defender.Position - attacker.Position;
                toDef.z = 0f;
                if (toDef.LengthSquared > 1e-4f)
                {
                    u = toDef.NormalizedCopy();
                    haveLine = true;
                }
            }

            // 退化（没有站位偏移 / 两人重合）：保持旧行为 —— 发起者当前朝向、站到目标脚下。
            if (stanceLen < 1e-4f || !haveLine)
            {
                attackerYawDeg = YawDegOf(ForwardOf(attacker));
                defenderYawDeg = Normalize180(attackerYawDeg + def.Stance.DirectionDeg);
                attackerSpot = defender.Position;
                attackerSpot.z = defender.Position.z;
                return TrySnapToGround(ref attackerSpot, defender.Position.z, out why);
            }

            float lineYaw = YawDegOf(u);
            float stanceLocalYaw = MathF.Atan2(def.Stance.Right, def.Stance.Forward) * (180f / MathF.PI);

            attackerYawDeg = Normalize180(lineYaw - stanceLocalYaw);
            defenderYawDeg = Normalize180(attackerYawDeg + def.Stance.DirectionDeg);

            attackerSpot = defender.Position - u * stanceLen;
            attackerSpot.z = defender.Position.z;

            if (!TrySnapToGround(ref attackerSpot, defender.Position.z, out why))
                return false;

            return true;
        }

        /// <summary>旧入口（现算连线）：命令预览 / 没有冻结连线的调用方用。</summary>
        public static bool TryComputeStartPose(PairActionDef def, Agent attacker, Agent defender,
            out Vec3 attackerSpot, out float attackerYawDeg, out float defenderYawDeg, out string why)
            => TryComputeStartPose(def, attacker, defender, Vec3.Zero,
                out attackerSpot, out attackerYawDeg, out defenderYawDeg, out why);

        /// <summary>
        /// 算**被挂接者**这一帧应该摆在哪、朝哪（锚点 = attacker 或坐骑）。
        /// 竖直不在这里写（agent 的 Z 由引擎按地形重算；抬升/横躺的视觉靠 clip 根骨轨）。
        /// </summary>
        public static bool TryComputeAttachPose(PairActionDef def, Agent anchor,
            out Vec3 defenderPos, out Vec3 defenderForward, out string why)
        {
            defenderPos = Vec3.Zero;
            defenderForward = Vec3.Forward;
            why = "-";

            if (def == null || anchor == null || !AgentControlHelper.SafeIsActive(anchor))
            {
                why = "anchor is null / inactive";
                return false;
            }

            Vec3 anchorForward = ForwardOf(anchor, anchor.IsMount);
            float anchorYaw = YawDegOf(anchorForward);
            Vec3 f = ForwardFromYaw(anchorYaw);
            Vec3 r = RightFromYaw(anchorYaw);

            defenderPos = anchor.Position
                        + r * def.Stance.Right
                        + f * def.Stance.Forward;
            defenderPos.z = anchor.Position.z;
            defenderForward = ForwardFromYaw(anchorYaw + def.Stance.DirectionDeg);
            return true;
        }

        /// <summary>目标点贴地 + navmesh 修正；高度差过大则拒绝。</summary>
        public static bool TrySnapToGround(ref Vec3 spot, float referenceZ, out string why)
        {
            why = "-";
            Scene scene = Mission.Current != null ? Mission.Current.Scene : null;
            if (scene != null)
            {
                try { V.NavMeshSnap(scene, ref spot); }
                catch (Exception) { }

                float gz = V.GroundHeight(scene, spot, BodyFlags.CommonCollisionExcludeFlags);
                if (!IsValidGround(gz))
                {
                    why = $"no ground under target spot ({spot.x:F1},{spot.y:F1})";
                    return false;
                }
                spot.z = gz;
            }

            float dz = MathF.Abs(spot.z - referenceZ);
            if (dz > MaxHeightDelta)
            {
                why = $"height difference {dz:F1} m > {MaxHeightDelta:F1} m";
                return false;
            }
            return true;
        }

        /// <summary>发起者离目标站位点有多远（水平）。</summary>
        public static float HorizontalDistance(Vec3 a, Vec3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>一行的 agent 名（日志/命令用，纯英文）。</summary>
        public static string Describe(Agent a)
        {
            if (a == null) return "?";
            try
            {
                string name = a.Name;
                return string.IsNullOrWhiteSpace(name) ? ("agent#" + a.Index) : name;
            }
            catch (Exception) { return "?"; }
        }
    }
}
