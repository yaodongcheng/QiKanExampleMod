using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
// ⚠️ 不要 `using LivingWorldNpcs.Debug;` —— 那是**源码目录名**不是命名空间（编译报 CS0234）。
//    `DebugLogger` 本身就在 `LivingWorldNpcs` 里，本类在 `LivingWorldNpcs.Combat` ⇒ 父命名空间直接可见。

namespace LivingWorldNpcs.Combat
{
    /// <summary>
    /// 螺旋丸的运行时驱动（2026-09-27）。
    ///
    /// 干三件事：
    ///   ① 实例化 prefab `rasengan`（Taikou\Prefabs\spell_rasengan.xml），
    ///      按名字抓三个子实体：`rasengan_core` / `rasengan_blade` / `rasengan_air`
    ///   ② **每帧**让三个环各转各的（绕螺旋丸自己的局部 Z）
    ///   ③ 蓄力段：**尺度与瞬时转速共用同一个进度 u** ⇒ 越转越快、同时长大
    ///
    /// 🔴 三条实机/反编译得来的硬约束（别改回去）：
    ///   1. **别用 `SetLocalFrame`** —— 1.2.12 的 `GameEntity` 里根本没有这个方法
    ///      （1.3.15 才加）。跨版本安全的写法 = `SetGlobalFrame`（1.2.12 单参 / 1.3.15 带默认参，
    ///      调用点写 `SetGlobalFrame(frame)` 两边都能编）。
    ///   2. **别读 `Vector3` 那类 WorldPosition**：位置一律从 `GetGlobalFrame()` 拿。
    ///   3. 角度**自己累加**（`deg += dps*dt`），不要用"总时长 × 恒定转速" —— 那样转速一变就跳。
    ///
    /// 单位与方向：转速填**度/秒**；正数 = **俯视逆时针**（`Mat3.RotateAboutUp` 的约定）。
    /// </summary>
    public class RasenganController : MissionLogic
    {
        /// <summary>prefab 的注册名 = XML 里**根实体的 name**（不是文件名！踩过一次：
        /// 文件名 `spell_rasengan.xml` 而根实体叫 `rasengan`，用文件名查 `PrefabExists` 会 false）。</summary>
        public const string PrefabName = "rasengan";

        private class Ring
        {
            public GameEntity Entity;
            public float AngleDeg;          // 累加角度（度）—— 累加而非绝对，转速变化时才连续
            public float MaxDps;            // 峰值转速（度/秒）
            public Vec3 LocalOffset;        // 相对根的偏移（spawn 时读一次）
        }

        private readonly List<Ring> _rings = new List<Ring>();
        private GameEntity _root;
        private float _elapsed;
        private bool _charging;

        // 可调旋钮（验收命令 custom.rasengan 能改）
        private float _chargeSeconds = 1.5f;   // 蓄力时长
        private float _startScale = 0.15f;     // 起始尺度倍率（别给 0 —— 从"无"里冒出来很假）
        private float _accelPow = 2f;          // 加速曲线指数：u² 越转越急、1 线性、3 更爆
        private float _bladeDps = 360f;
        private float _airDps = 200f;
        private float _coreDps = 0f;           // 球是均匀发光，转不转看不出来

        public static RasenganController Current { get; private set; }

        /// <summary>拿当前 Mission 上的实例，没有就挂一个（命令入口调它）。</summary>
        public static RasenganController Ensure()
        {
            Mission mission = Mission.Current;
            if (mission == null) return null;
            if (Current != null && Current.Mission == mission) return Current;
            RasenganController c = new RasenganController();
            mission.AddMissionBehavior(c);
            return c;
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            Current = this;
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            if (Current == this) Current = null;
            _rings.Clear();
            _root = null;
        }

        // ───────────────────────── 生成 ─────────────────────────

        /// <summary>在指定世界位置实例化一颗螺旋丸。返回根实体（失败 null）。</summary>
        public GameEntity Spawn(Vec3 position)
        {
            Mission mission = Mission.Current;
            if (mission == null || mission.Scene == null) return null;
            if (!GameEntity.PrefabExists(PrefabName))
            {
                DebugLogger.Log($"[Rasengan] prefab '{PrefabName}' 不在当前场景 —— 检查 Taikou/Prefabs/spell_rasengan.xml 是否被加载");
                return null;
            }

            MatrixFrame frame = MatrixFrame.Identity;
            frame.origin = position;
            GameEntity root = GameEntity.Instantiate(mission.Scene, PrefabName, frame);
            if (root == null) return null;

            _rings.Clear();
            _root = root;
            _elapsed = 0f;
            _charging = true;

            // 按名字抓三个环。`GetChildren()` 只给**直接子节点** —— prefab 里这三个正好都在根下。
            MatrixFrame rootGF = root.GetGlobalFrame();
            foreach (GameEntity child in root.GetChildren())
            {
                float dps;
                switch (child.Name)
                {
                    case "rasengan_blade": dps = _bladeDps; break;
                    case "rasengan_air":   dps = _airDps;   break;
                    case "rasengan_core":  dps = _coreDps;  break;
                    default: continue;
                }
                _rings.Add(new Ring
                {
                    Entity = child,
                    AngleDeg = 0f,
                    MaxDps = dps,
                    // 记一次局部偏移。spawn 那一刻根还没转过 ⇒ 世界偏移 == 局部偏移（够用；
                    // 根如果之后要倾斜，就把它当基准继续用，误差只在根自身旋转非单位阵时出现）
                    LocalOffset = child.GetGlobalFrame().origin - rootGF.origin,
                });
            }

            DebugLogger.Log($"[Rasengan] spawn 于 ({position.x:F1},{position.y:F1},{position.z:F1})，抓到 {_rings.Count} 个环");
            return root;
        }

        // ───────────────────────── 每帧 ─────────────────────────

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_root == null || _rings.Count == 0) return;

            _elapsed += dt;

            // 蓄力进度 u：0 → 1。充满就停在那儿（保持转 + 保持满尺寸）
            float u = _chargeSeconds <= 0f ? 1f : Math.Min(1f, _elapsed / _chargeSeconds);
            if (u >= 1f) _charging = false;

            // 尺度与转速共用同一个 u —— 两条曲线同时到峰值，观感才"聚"
            float scale = _startScale + (1f - _startScale) * u;
            float accel = (float)Math.Pow(u, _accelPow);

            MatrixFrame rootGF = _root.GetGlobalFrame();
            for (int i = 0; i < _rings.Count; i++)
            {
                Ring r = _rings[i];
                if (r.Entity == null) continue;

                // 🔴 每帧算**瞬时**转速再加进角度 —— 这是"越转越快"的唯一正确写法
                float dps = r.MaxDps * accel;
                r.AngleDeg = (r.AngleDeg + dps * dt) % 360f;

                Mat3 rot = Mat3.Identity;
                rot.RotateAboutUp(r.AngleDeg * MathF.DegToRad);   // 局部 Z（吃弧度）
                rot.ApplyScaleLocal(scale);

                Vec3 worldPos = rootGF.origin + rootGF.rotation.TransformToParent(r.LocalOffset);
                Mat3 worldRot = rootGF.rotation.TransformToParent(rot);
                r.Entity.SetGlobalFrame(new MatrixFrame(worldRot, worldPos));
            }
        }

        // ───────────────────────── 旋钮（给命令用）─────────────────────────

        public void Configure(float chargeSeconds, float bladeDps, float airDps, float coreDps)
        {
            // 传负数 = 该项不动（命令只给一部分参数时用）
            if (chargeSeconds >= 0f) _chargeSeconds = chargeSeconds;
            if (bladeDps >= 0f) _bladeDps = bladeDps;
            if (airDps >= 0f) _airDps = airDps;
            if (coreDps >= 0f) _coreDps = coreDps;

            // 🔴 必须同步到**已经生成**的那些环 —— `Ring.MaxDps` 是 spawn 那一刻拷贝的快照，
            //    不同步的话 `custom.rasengan spin` 只对"下次 spawn"生效，眼前这颗纹丝不动。
            for (int i = 0; i < _rings.Count; i++)
            {
                Ring r = _rings[i];
                if (r.Entity == null) continue;
                switch (r.Entity.Name)
                {
                    case "rasengan_blade": r.MaxDps = _bladeDps; break;
                    case "rasengan_air":   r.MaxDps = _airDps;   break;
                    case "rasengan_core":  r.MaxDps = _coreDps;  break;
                }
            }
        }

        /// <summary>重播蓄力（把 u 归零、角度不清零 —— 角度清了会看到"跳一下"）。</summary>
        public void Recharge()
        {
            _elapsed = 0f;
            _charging = true;
        }

        public string Status()
        {
            float u = _chargeSeconds <= 0f ? 1f : Math.Min(1f, _elapsed / _chargeSeconds);
            return $"rasengan: rings={_rings.Count} u={u:F2} charge={_chargeSeconds:F1}s "
                 + $"dps(blade/air/core)={_bladeDps:F0}/{_airDps:F0}/{_coreDps:F0} startScale={_startScale:F2}";
        }
    }
}
