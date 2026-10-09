using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 飞行载具 = **脚下那张法阵**（2026-09-21 重做，合一版）。
    ///
    /// 它干两件事，**由同一个实体的两个部分分别承担**（预制体 `lwn_flight_sigil` 里声明）：
    ///   ① **物理面**：根实体带 `<physics shape="bo_wooden_platform_a"/>` ——
    ///      引擎把玩家算在它的顶面上，板往上走人跟着走。这是唯一能改变玩家高度的办法
    ///      （引擎写不进 agent 的 Z，见 Knowledge/骑砍2Agent运动与位置机制.md §6）。
    ///   ② **视觉**：子实体带我们的法阵网格，抬到承载面上方一点点。
    ///
    /// 🔴🔴 **为什么不再用"隐形木板 + 另挂法阵"两个实体**（2026-09-21 实机摔死人）：
    ///    那一版靠 `SetVisibilityExcludeParents(false)` 把木板藏起来，**实测把碰撞一起干掉了** ——
    ///    隐藏成功后 1.8 秒主角掉下去摔死（日志 `载具可见性 -> 隐藏 | 根vis=False` → `玩家 agent 失效`）。
    ///    ⇒ **"让承载物隐形"这条路走不通**，改成"**承载物本身就是可见的法阵**"。
    ///    附带好处：少一个实体、少一次 SetFrame、少一整层可见性同步代码。
    ///
    /// 🔴 三条不可违反的机制事实（全部实测过，违反就没效果甚至炸）：
    ///   1. **只能逐帧瞬移**（<c>SetFrame</c> 改坐标）。用物理速度驱动 = 完全不托人。
    ///   2. **不能用加速度闭环校正** —— 半秒内数值爆炸到 5.97e14。
    ///   3. **不要碰玩家的 Controller**（那条路会让角色被搬走、镜头丢失）。
    /// </summary>
    public sealed class CarrierBoard
    {
        private Scene _scene;
        private GameEntity _carrier;

        /// <summary>载具原点（在碰撞体中心底面）的世界坐标。</summary>
        public Vec3 Origin { get; private set; }

        /// <summary>载具是否已就位。</summary>
        public bool IsSpawned => IsAlive(_carrier);

        /// <summary>
        /// 在指定位置生成载具。预制体自带碰撞 + 法阵网格，**不需要再挂任何东西**。
        /// </summary>
        /// <param name="boardTopWorldPos">
        /// **板面**（承载面）要落在的世界坐标 —— 注意口径是"板面"，不是"板原点"，
        /// 原点会被压到它下面 <see cref="FlightTuning.CarrierTopLocalZ"/> 处。
        /// </param>
        /// <returns>成功返回 true。</returns>
        public bool Spawn(Scene scene, Vec3 boardTopWorldPos)
        {
            if (scene == null)
                return false;

            Remove();                     // 幂等：重复调不会漏掉旧实体

            // 🔴🔴 这里原来有一道"预制体引用的网格必须在包里"的探针 —— **2026-10-10 连同
            //     `FlightTuning.CarrierMeshName` 一起删了**：载具预制体现在**一个网格都不引用**
            //     （纯物理体 = 真的看不见），`Instantiate` 没有网格可查，那条 AV 陷阱不存在了。
            //     ⚠️ **哪天给载具加回网格，探针必须加回来**（网格缺失 = native 访问违例，当场崩、拦不住）。

            _scene = scene;

            MatrixFrame frame = MatrixFrame.Identity;
            frame.origin = new Vec3(
                boardTopWorldPos.x,
                boardTopWorldPos.y,
                boardTopWorldPos.z - FlightTuning.CarrierTopLocalZ);   // 板面 → 原点

            try
            {
                _carrier = GameEntity.Instantiate(scene, FlightTuning.CarrierPrefab, frame);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 载具生成异常（prefab={FlightTuning.CarrierPrefab}）: {ex.Message}");
                _carrier = null;
                return false;
            }

            if (!IsAlive(_carrier))
            {
                DebugLogger.Log($"[Flight] 载具生成失败：prefab '{FlightTuning.CarrierPrefab}' 找不到（铁律 5：核心资产要用两轮查找兜底）");
                return false;
            }

            Origin = frame.origin;
            return true;
        }

        /// <summary>
        /// 每帧瞬移 —— **逐字照搬已实测丝滑的那套**（FlySpike `PropSpikeMissionView`）：
        /// <code>
        /// MatrixFrame f0 = entity.GetFrame();      // (1) 读回【真实】坐标
        /// f0.origin += velocity * dt;              // (2) 加增量
        /// entity.SetFrame(ref f0);                 // (3) 写回
        /// </code>
        /// 🔴 关键是第 (1) 步：**读回真实坐标，不用自己记的值**。
        ///    自己记 Origin 再覆盖 = 引擎若动过板就会被拽回去 → 互相打架（2026-09-21 教训）。
        /// </summary>
        public void MoveBy(Vec3 delta)
        {
            if (!IsSpawned)
                return;

            MatrixFrame f;
            try { f = _carrier.GetFrame(); }
            catch (Exception ex) { DebugLogger.Log($"[Flight] 读载具坐标异常: {ex.Message}"); return; }

            f.origin = new Vec3(f.origin.x + delta.x, f.origin.y + delta.y, f.origin.z + delta.z);

            try { _carrier.SetFrame(ref f); }
            catch (Exception ex) { DebugLogger.Log($"[Flight] 移动载具异常: {ex.Message}"); return; }

            Origin = f.origin;      // 只用于日志/诊断
        }

        /// <summary>直接设到指定原点（同样走瞬移，不碰物理）。</summary>
        public void MoveTo(Vec3 newOrigin)
        {
            if (!IsSpawned)
                return;

            MatrixFrame frame;
            try
            {
                frame = _carrier.GetFrame();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 读载具坐标异常: {ex.Message}");
                return;
            }

            frame.origin = newOrigin;
            try
            {
                _carrier.SetFrame(ref frame);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 移动载具异常: {ex.Message}");
                return;
            }

            Origin = newOrigin;
        }

        /// <summary>拆掉载具。</summary>
        public void Remove()
        {
            if (!IsAlive(_carrier))
            {
                _carrier = null;
                return;
            }
            try
            {
                _carrier.Remove(0);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 拆除载具异常: {ex.Message}");
            }
            _carrier = null;
        }

        /// <summary>
        /// 玩家**真实碰撞体**的底面高度（世界 z）。定位载具用它，**不要用 `agent.Position`**。
        ///
        /// 🔴 为什么（2026-09-21 用户指出）：`Position` 只是个约定俗成的"原点"，**二段跳时人在空中**，
        ///    它跟碰撞体不一定同高；而引擎把真实碰撞胶囊给出来了
        ///    （<see cref="Agent.CollisionCapsule"/>：世界坐标下的两端点 P1/P2 + 半径）。
        ///    底面 = 两端点里较低那个 − 半径。
        ///
        /// 取不到就回落到 <see cref="Agent.Position"/> 的 z（宁可差一点，不能让飞行起不来）。
        /// </summary>
        public static float CollisionCapsuleBottomZ(Agent agent)
        {
            if (agent == null)
                return 0f;
            try
            {
                CapsuleData capsule = agent.CollisionCapsule;
                float lowEnd = Math.Min(capsule.P1.z, capsule.P2.z);
                return lowEnd - capsule.Radius;
            }
            catch
            {
                try { return agent.Position.z; }
                catch { return 0f; }
            }
        }

        /// <summary>一行碰撞体摘要（起飞时打进日志，用来核对手算的偏移对不对）。</summary>
        public static string DescribeCapsule(Agent agent)
        {
            if (agent == null)
                return "capsule=(none)";
            try
            {
                CapsuleData capsule = agent.CollisionCapsule;
                Vec3 pos = agent.Position;
                return string.Format(
                    "pos.z={0:F3} capsule P1.z={1:F3} P2.z={2:F3} r={3:F3} bottom={4:F3} land={5}",
                    pos.z, capsule.P1.z, capsule.P2.z, capsule.Radius,
                    CollisionCapsuleBottomZ(agent), agent.IsOnLand() ? 1 : 0);
            }
            catch (Exception ex)
            {
                return "capsule=(err " + ex.Message + ")";
            }
        }

        // ═════════════════════════ 足迹 / 搭便车（2026-10-10 立）═════════════════════════
        //
        // 起因（用户报告）："钩锁拉人起飞的时候，如果身边有其他 agent，会把其他人也带飞。"
        // 根因 = 承载面是**原版 5 米见方的大木板**（谁站在上面谁被抬走）；预制体已缩到 1.59 × 1.55，
        //        这里再补一道兜底：起飞头几帧把仍站在板上的**别人**请下去（那时候板还低，掉不疼）。

        /// <summary>
        /// 载具的**物理足迹**（世界坐标）：中心 / X 半宽 / Y 半深 / 顶面高。
        ///
        /// 🔴 用引擎自己的物理包围盒问（`GetPhysicsMinMax` —— 1.2.12 是实例方法、1.3/1.5 是扩展方法，
        ///    调用写法一模一样，**不需要版本分支**）。**不要**在代码里拿"5.29 × 5.16 乘缩放"算：
        ///    尺寸的真源是预制体 + 引擎，写死就对不上了（改一次 scale 就得改一遍代码）。
        /// </summary>
        public bool TryGetFootprint(out Vec3 center, out float halfX, out float halfY, out float topZ)
        {
            center = Vec3.Zero;
            halfX = 0f;
            halfY = 0f;
            topZ = 0f;
            if (!IsSpawned)
                return false;

            try
            {
                // includeChildren:true —— 物理体挂在**子节点**上（`lwn_flight_sigil_body`，见 lwn_prefabs.xml：
                // 根节点的 transform 会被 Instantiate 的 frame 冲掉，所以物理体与缩放都写子节点）。
                _carrier.GetPhysicsMinMax(true, out Vec3 mn, out Vec3 mx, returnLocal: false);
                center = new Vec3((mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f, (mn.z + mx.z) * 0.5f);
                halfX = (mx.x - mn.x) * 0.5f;
                halfY = (mx.y - mn.y) * 0.5f;
                topZ = mx.z;
                return halfX > 0.01f && halfY > 0.01f;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 读载具物理盒异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>板面上有几个"别人"（<paramref name="keep"/> 之外、脚踩在板面上的 agent）。诊断用。</summary>
        public int CountRiders(Agent keep, float margin = 0.1f)
        {
            return RidersOnBoard(keep, margin).Count;
        }

        /// <summary>
        /// 允许"请人下车"的**最大半足迹**（米）：超过它 = 预制体那个 `scale` 没生效（物理体没跟着缩）。
        /// 那时候挪人毫无意义 —— 挪到板边之外还得再挪 5 米，人只会在原地抖 ⇒ **宁可不挪**，打一行日志。
        /// （数值口径：缩放生效时半足迹 ≈ 0.77 米；1.2 给的是一倍余量。）
        /// </summary>
        private const float MaxEvictableHalfExtent = 1.2f;

        private bool _bigBoardWarned;

        /// <summary>
        /// **把"搭便车"的人请下板**，返回请下去了几个。
        ///
        /// 🔴 **只在板还低的时候调用**（调用方按 <see cref="FlightTuning.CarrierEvictMaxLift"/> 把关）：
        ///    低空把人挪到板外，他最多掉一两米；飞高了再挪 = 从空中丢人。
        /// 挪法 = 从板心朝"他自己那一侧"往外挪到板边之外；那一侧没地方站（悬空/悬崖）就换下一侧
        ///    （八向都试）；**八向都不行就不挪** —— 宁可先带着他，也不能把人丢进虚空。
        /// 🔴 不碰 <paramref name="keep"/>（玩家自己），他是本轮唯一该被抬走的人。
        /// </summary>
        public int EvictRiders(Agent keep, float margin)
        {
            if (!IsSpawned || !TryGetFootprint(out Vec3 center, out float halfX, out float halfY, out float topZ))
                return 0;

            if (MathF.Max(halfX, halfY) > MaxEvictableHalfExtent)
            {
                // 足迹没缩下来（多半是预制体的 scale 没生效）—— 这时候挪人只会让人原地抖，不做。
                if (!_bigBoardWarned)
                {
                    _bigBoardWarned = true;
                    DebugLogger.Log($"[Flight] ⚠ 载具足迹 {halfX * 2f:F2} × {halfY * 2f:F2} m 比预期大得多"
                        + "（预制体的 scale 没生效？）—— 本次不请人下车（挪了也还在板面上）。"
                        + "请核对 Prefabs/lwn_prefabs.xml 的 lwn_flight_sigil_body。");
                }
                return 0;
            }

            List<Agent> riders = RidersOnBoard(keep, 0.15f);
            int evicted = 0;
            foreach (Agent rider in riders)
            {
                if (!TryFindEvictSpot(rider, center, halfX, halfY, topZ, margin, out Vec3 spot))
                    continue;
                try
                {
                    // 🔴 Z 由引擎按地形重算（写进去的值只是"落点意向"，见 Knowledge/骑砍2Agent运动与位置机制.md §6）
                    rider.TeleportToPosition(spot);
                    evicted++;
                    DebugLogger.Log($"[Flight] 载具搭便车：'{DescribeAgent(rider)}' 已请下板 → ({spot.x:F1},{spot.y:F1},{spot.z:F1})");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Flight] 请人下板失败（忽略）: {ex.GetType().Name} {ex.Message}");
                }
            }
            return evicted;
        }

        /// <summary>列出"站在板面上"的其他 agent（判据 = 脚底贴着板面且水平落在足迹内）。</summary>
        private List<Agent> RidersOnBoard(Agent keep, float margin)
        {
            var list = new List<Agent>();
            if (!IsSpawned || !TryGetFootprint(out Vec3 c, out float hx, out float hy, out float top))
                return list;

            Mission mission = Mission.Current;
            if (mission == null)
                return list;

            try
            {
                foreach (Agent a in mission.Agents)
                {
                    if (a == null || a == keep)
                        continue;
                    if (!AgentControlHelper.SafeIsActive(a))
                        continue;

                    float feet = CollisionCapsuleBottomZ(a);
                    // 站在板面上（脚底≈顶面）；在板底下 / 跳在半空的都不算
                    if (feet < top - 0.35f || feet > top + 0.45f)
                        continue;

                    Vec3 p = a.Position;
                    if (Math.Abs(p.x - c.x) > hx + margin || Math.Abs(p.y - c.y) > hy + margin)
                        continue;

                    list.Add(a);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 扫载具乘客异常（当作没人）: {ex.Message}");
            }
            return list;
        }

        /// <summary>给某个乘客找一个"板外、站得住"的落点（八向里挑第一个），找不到返回 false。</summary>
        private bool TryFindEvictSpot(Agent rider, Vec3 center, float halfX, float halfY, float topZ,
                                      float margin, out Vec3 spot)
        {
            spot = Vec3.Zero;
            if (_scene == null)
                return false;

            Vec3 p = rider.Position;
            float dx = p.x - center.x;
            float dy = p.y - center.y;
            float baseAngle = (Math.Abs(dx) < 0.01f && Math.Abs(dy) < 0.01f)
                ? 0f                                   // 正好站在板心（多半就是被玩家挤过来的）⇒ 从 0° 起试
                : MathF.Atan2(dy, dx);

            float radius = MathF.Max(halfX, halfY) + margin;
            for (int i = 0; i < 8; i++)
            {
                float ang = baseAngle + i * (MathF.PI / 4f);
                float tx = center.x + MathF.Cos(ang) * radius;
                float ty = center.y + MathF.Sin(ang) * radius;
                float gz = GroundZOf(new Vec3(tx, ty, topZ + 1.5f));
                if (float.IsNaN(gz))
                    continue;                          // 那一侧没有地面（悬空/出界）
                if (MathF.Abs(gz - topZ) > 2f)
                    continue;                          // 落差太大（悬崖 / 屋顶）—— 别把人丢过去
                spot = new Vec3(tx, ty, gz + 0.05f);
                return true;
            }
            return false;
        }

        /// <summary>某点下方的地面高度（口径同 §22.4：`GetGroundHeightAtPositionMT`；拿不到 = NaN）。</summary>
        private float GroundZOf(Vec3 probe)
        {
            try
            {
                float z = _scene.GetGroundHeightAtPositionMT(probe, BodyFlags.CommonCollisionExcludeFlags);
                if (float.IsNaN(z) || z > 1e5f || z < -1e5f)
                    return float.NaN;
                return z;
            }
            catch (Exception)
            {
                return float.NaN;
            }
        }

        private static string DescribeAgent(Agent a)
        {
            try
            {
                string name = a.Name;
                return string.IsNullOrEmpty(name) ? ("agent#" + a.Index) : name;
            }
            catch
            {
                return "agent";
            }
        }

        // 🪦 2026-10-10 删掉两个辅助（`MeshExists` / `LogMeshMissingOnce` + `_meshWarned` 字段）——
        //    它们是给"预制体引用的网格必须在包里"那条 AV 门禁用的；载具现在**不带任何网格**，
        //    门禁没有对象可查。**加回网格时把探针一起加回来**（见 Spawn 里的注释）。

        private static bool IsAlive(GameEntity entity)
        {
            // 🔴 别用 entity.IsValid 之外的写法：GameEntity 是 native 包装，
            //    已销毁时访问成员可能直接抛（FlySpike 用的就是这一对判据）。
            return entity != null && entity.Pointer != UIntPtr.Zero;
        }

        public string Describe()
        {
            return string.Format("carrier={0} origin=({1:F2},{2:F2},{3:F2})",
                IsSpawned ? "on" : "off", Origin.x, Origin.y, Origin.z);
        }
    }
}
