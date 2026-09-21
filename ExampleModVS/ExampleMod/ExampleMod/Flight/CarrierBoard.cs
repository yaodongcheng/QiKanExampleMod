using System;
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
        private bool _meshWarned;

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

            // 🔴🔴 硬门禁：**预制体引用的网格必须真的存在**，否则 `GameEntity.Instantiate`
            //     会 native 访问违例把游戏打崩，**try/catch 拦不住**（AV 属"损坏状态异常"，
            //     .NET 默认不允许捕获）。2026-09-21 实机踩过。
            //     探针用法照抄引擎自己的 `MetaMesh.GetCopy(name, showErrors:false, mayReturnNull:true)`
            //     —— mayReturnNull 就是"找不到给我 null，别崩"。
            if (!MeshExists(FlightTuning.CarrierMeshName))
            {
                LogMeshMissingOnce($"网格 '{FlightTuning.CarrierMeshName}' 不在任何已加载的包里（资产还没装）");
                return false;
            }

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

        /// <summary>网格是否已在某个已加载的包里。找不到返回 false，**不会崩**。</summary>
        private static bool MeshExists(string meshName)
        {
            if (string.IsNullOrEmpty(meshName))
                return false;
            try
            {
                MetaMesh probe = MetaMesh.GetCopy(meshName, showErrors: false, mayReturnNull: true);
                return probe != null && probe.IsValid;
            }
            catch
            {
                return false;   // 探针本身出问题就当作"不存在"，宁可不生成也不崩
            }
        }

        private void LogMeshMissingOnce(string why)
        {
            if (_meshWarned)
                return;
            _meshWarned = true;
            DebugLogger.Log($"[Flight] 载具没生成（{why}）—— 飞不起来，先确认资产是否进了包。");
        }

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
