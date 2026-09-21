using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 飞行载具 = 一块**看不见的实心板**（2026-09-21）。
    ///
    /// 它干两件事：
    ///   ① **物理面**：引擎把玩家算在它的顶面上 —— 板往上走，人跟着走。
    ///      这是唯一能改变玩家高度的办法（引擎写不进 agent 的 Z，见
    ///      Knowledge/骑砍2Agent运动与位置机制.md §6）。
    ///   ② **法阵挂点**：脚下那张法阵跟着它走（板本身看不见）。
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
        private GameEntity _sigil;
        private bool _sigilWarned;

        /// <summary>板原点（在中心底面）的世界坐标。玩家脚下 = 这个点 + 板顶面高度。</summary>
        public Vec3 Origin { get; private set; }

        /// <summary>物理板是否已就位。</summary>
        public bool IsSpawned => IsAlive(_carrier);

        /// <summary>法阵是否已就位（资产没做好时为 false —— 不影响飞行）。</summary>
        public bool HasSigil => IsAlive(_sigil);

        /// <summary>
        /// 在指定位置生成载具。
        /// </summary>
        /// <param name="feetWorldPos">玩家脚底的世界坐标（板会被压到它下面）。</param>
        /// <returns>成功返回 true。</returns>
        public bool Spawn(Scene scene, Vec3 feetWorldPos)
        {
            if (scene == null)
                return false;

            Remove();                     // 幂等：重复调不会漏掉旧实体

            _scene = scene;

            MatrixFrame frame = MatrixFrame.Identity;
            frame.origin = new Vec3(
                feetWorldPos.x,
                feetWorldPos.y,
                feetWorldPos.z - FlightTuning.CarrierFeetOffset);

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

            // 🔴 默认【不隐藏】—— 板隐形的就没法用肉眼确认它真的生成了、真的在托人。
            //    阶段 1 调试让它显示；要出货了再用 custom.flight hide on 关掉。
            if (FlightTuning.HideCarrier)
            {
                try
                {
                    // 只隐藏网格，**不动物理体** —— 板还得当"地面"用
                    _carrier.SetVisibilityExcludeParents(false);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Flight] 隐藏载具网格失败（板会显示出来，但飞行不受影响）: {ex.Message}");
                }
            }

            SpawnSigil(scene);
            return true;
        }

        private void SpawnSigil(Scene scene)
        {
            if (string.IsNullOrEmpty(FlightTuning.SigilPrefab))
                return;

            // 🔴🔴 硬门禁：**预制体引用的网格必须真的存在，否则 Instantiate 会 native 访问违例把游戏打崩。**
            //     2026-09-21 实机踩过：法阵网格还没进包、预制体先放进了 Taikou\Prefabs\
            //     → Instantiate 当场 AccessViolationException，try/catch **拦不住**
            //     （AV 属"损坏状态异常"，.NET 默认不允许捕获）。
            //     探针用法照抄引擎自己的 `MetaMesh.GetCopy(holsterMeshName, showErrors:false, mayReturnNull:true)`
            //     —— mayReturnNull 就是"找不到给我 null，别崩"。
            if (!MeshExists(FlightTuning.SigilMeshName))
            {
                LogSigilMissingOnce($"网格 '{FlightTuning.SigilMeshName}' 不在任何已加载的包里（资产还没做/还没装）");
                return;
            }

            MatrixFrame frame = MatrixFrame.Identity;
            frame.origin = new Vec3(Origin.x, Origin.y, Origin.z + FlightTuning.SigilLiftZ);

            try
            {
                _sigil = GameEntity.Instantiate(scene, FlightTuning.SigilPrefab, frame);
            }
            catch (Exception ex)
            {
                LogSigilMissingOnce($"生成异常: {ex.Message}");
                _sigil = null;
                return;
            }

            if (!IsAlive(_sigil))
            {
                LogSigilMissingOnce($"prefab '{FlightTuning.SigilPrefab}' 找不到");
                _sigil = null;
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
                return false;   // 探针本身出问题就当作"不存在"，宁可不显示法阵也不崩
            }
        }

        private void LogSigilMissingOnce(string why)
        {
            if (_sigilWarned)
                return;
            _sigilWarned = true;
            DebugLogger.Log($"[Flight] 法阵没挂上（{why}）—— 只飞不显示法阵，飞行本身不受影响。");
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

            if (HasSigil)
            {
                MatrixFrame sf = MatrixFrame.Identity;
                sf.origin = new Vec3(f.origin.x, f.origin.y, f.origin.z + FlightTuning.SigilLiftZ);
                try { _sigil.SetFrame(ref sf); } catch { /* 法阵是纯视觉 */ }
            }
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

            if (HasSigil)
            {
                MatrixFrame sf = MatrixFrame.Identity;
                sf.origin = new Vec3(newOrigin.x, newOrigin.y, newOrigin.z + FlightTuning.SigilLiftZ);
                try
                {
                    _sigil.SetFrame(ref sf);
                }
                catch
                {
                    // 法阵是纯视觉，动不了不该拦住飞行
                }
            }
        }

        /// <summary>拆掉载具与法阵。</summary>
        public void Remove()
        {
            RemoveEntity(ref _carrier);
            RemoveEntity(ref _sigil);
        }

        private static void RemoveEntity(ref GameEntity entity)
        {
            if (!IsAlive(entity))
            {
                entity = null;
                return;
            }
            try
            {
                entity.Remove(0);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 拆除实体异常: {ex.Message}");
            }
            entity = null;
        }

        private static bool IsAlive(GameEntity entity)
        {
            // 🔴 别用 entity.IsValid 之外的写法：GameEntity 是 native 包装，
            //    已销毁时访问成员可能直接抛（FlySpike 用的就是这一对判据）。
            return entity != null && entity.Pointer != UIntPtr.Zero;
        }

        public string Describe()
        {
            return string.Format("carrier={0} sigil={1} origin=({2:F2},{3:F2},{4:F2})",
                IsSpawned ? "on" : "off",
                HasSigil ? "on" : "off",
                Origin.x, Origin.y, Origin.z);
        }
    }
}
