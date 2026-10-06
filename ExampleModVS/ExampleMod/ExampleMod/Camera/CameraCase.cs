using System;
using System.Collections.Generic;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **接管瞬间"方向与臂长从哪来"**（Camera.csv 的 `Seed` 列）。
    /// </summary>
    public enum CameraSeed
    {
        /// <summary>照抄引擎相机：方向 = 引擎此刻的视线；本行 ArmLength/Fov 填 0 的项**保留引擎当时的值**。</summary>
        Engine = 0,

        /// <summary>方向用本行的 ArmYaw/ArmPitch（原模板机位口径）。</summary>
        Row = 1,
    }

    /// <summary>
    /// **Camera.csv 的一行**（2026-10-05 立）—— 全部机位 case 的唯一来源。
    ///
    /// 一张行 = 一个机位 case，两半内容：
    ///   · <see cref="Param"/> = 摆相机的那组数（<see cref="SpringArmCameraParam"/>，20 个数值列）
    ///   · 行为开关 = 相机**怎么用**这组数（播种来源 / 鼠标接管 / 俯仰钳位 / 锚点口径）
    ///
    /// 🔴 **数值列语义冻结、只许追加**（老行缺列 = 0 = 旧行为）；
    ///    `ScriptName`（中文描述，仅人读、零消费者）已于 2026-10-06 删除 —— 文件同时从 GBK 转成
    ///    **UTF-8（无 BOM、CRLF）**（与同目录 `Emotion.csv` 一致；老 GBK 在编辑器里全是乱码）。
    /// 🔴 **0/1 开关的类型行写 `float`**（不是 bool）—— `bool.TryParse("1")` 为 false、
    ///    `DynamicRecord.GetFloat` 只认 float ⇒ 一律读 `GetFloat(...) > 0.5f`。
    /// 🔴 表里没有的行 = **明确失败**（调用方打日志、不接管），**不做代码兜底**。
    ///
    /// 消费方：<see cref="SpringArmCameraView"/>（一次性机位 + 跟随机位）·
    /// <c>Flight/FlightCameraRig</c>（`fly_*` 四档）· <c>Combat/GrappleAimCamera</c>（`grapple_shot`）·
    /// <c>Combat/GrapplePull</c>（`grapple_pull`）· <c>custom.cam</c> 调试台。
    /// </summary>
    public sealed class CameraCase
    {
        // ───────────────────────── 身份 ─────────────────────────

        /// <summary>取用句柄（= CSV 的 ID 列）。</summary>
        public string Id;

        /// <summary>一次性机位的锚点选择：Player / Speaker / Listener / AnchorWorld（只喂 `UseCameraTemlate` 那条路）。</summary>
        public string AttachType;

        // ───────────────────────── 机位数值 ─────────────────────────

        /// <summary>摆相机的那组数（可热改 —— `custom.cam set` 改的就是它）。</summary>
        public SpringArmCameraParam Param;

        // ───────────────────────── 行为开关 ─────────────────────────

        /// <summary>接管瞬间方向/臂长从哪来（见 <see cref="CameraSeed"/>）。</summary>
        public CameraSeed Seed;

        /// <summary>1 = 相机自己接管"看"（每帧鼠标驱动 + 向 <c>CameraLook</c> 供方向）。</summary>
        public bool MouseLook;

        /// <summary>鼠标灵敏度（每像素多少度；最终值还要乘引擎的 `Input.MouseSensitivity`）。</summary>
        public float LookSens;

        /// <summary>俯仰钳位（度）。</summary>
        public float PitchMin, PitchMax;

        /// <summary>1 = 环绕点贴**动画头骨**（认得出动画蹲姿；含头上抬 0.10 米、6/s 平滑）。</summary>
        public bool AnchorFollowHead;

        /// <summary>锚点定高（米，从脚底算；≤0 = 引擎公式）。仅在 <see cref="AnchorFollowHead"/> = false 时生效。</summary>
        public float AnchorHeight;

        // ───────────────────────── 装载 ─────────────────────────

        // 缓存：表（DataTable 引用）换了就重建 —— 老表里的 case 对象一律作废，
        // 免得"改了 CSV 却还拿着旧对象"（`custom.cam` 热改写的就是这份缓存里的对象）。
        private static Dictionary<string, CameraCase> _cache;
        private static DataTable _cacheSource;

        /// <summary>表里有没有这一行（没有 = false，调用方自己决定"不接管 + 打日志"）。</summary>
        public static bool TryGet(string caseName, out CameraCase kase)
        {
            kase = null;
            if (string.IsNullOrWhiteSpace(caseName))
                return false;

            DataTable table = GameDatabase.Camera;
            if (table == null)
                return false;

            if (!ReferenceEquals(table, _cacheSource))
            {
                _cache = new Dictionary<string, CameraCase>();
                _cacheSource = table;
            }

            if (_cache.TryGetValue(caseName, out kase))
                return kase != null;

            DynamicRecord row = table.GetByID(caseName);
            kase = row == null ? null : Build(row);
            _cache[caseName] = kase;      // 缺行也记下来（同一个表内不必反复查）
            return kase != null;
        }

        /// <summary>表里全部 case 的 ID（`custom.cam list` 用）。表没加载/为空 → 空表。</summary>
        public static List<string> All()
        {
            var list = new List<string>();
            DataTable table = GameDatabase.Camera;
            if (table == null)
                return list;
            foreach (DynamicRecord row in table.GetAll())
            {
                if (row == null)
                    continue;
                string id = row.GetString("ID");
                if (string.IsNullOrEmpty(id))
                    continue;
                list.Add(id);
            }
            return list;
        }

        /// <summary>表行 → case（列缺失 = 0 = 旧行为；`Seed` 只认 "Row"，其余一律 Engine）。</summary>
        private static CameraCase Build(DynamicRecord row)
        {
            return new CameraCase
            {
                Id = row.GetString("ID"),
                AttachType = row.GetString("AttachType"),
                Param = new SpringArmCameraParam
                {
                    // ── 16 个原有列（语义冻结）──
                    PivotX = row.GetFloat("PivotX"),
                    PivotY = row.GetFloat("PivotY"),
                    PivotZ = row.GetFloat("PivotZ"),
                    ArmLength = row.GetFloat("ArmLength"),
                    ArmYaw = row.GetFloat("ArmYaw"),
                    ArmPitch = row.GetFloat("ArmPitch"),
                    SocketX = row.GetFloat("SocketX"),
                    SocketY = row.GetFloat("SocketY"),
                    SocketZ = row.GetFloat("SocketZ"),
                    SelfYaw = row.GetFloat("SelfYaw"),
                    SelfPitch = row.GetFloat("SelfPitch"),
                    SelfRoll = row.GetFloat("SelfRoll"),
                    Fov = row.GetFloat("Fov"),
                    // ── 7 个新数值列 ──
                    IsAnchorWorld = row.GetFloat("IsAnchorWorld") > 0.5f,
                    UseEngineEyeHeight = row.GetFloat("UseEngineEyeHeight") > 0.5f,
                    LagSpeed = row.GetFloat("LagSpeed"),
                    LagMaxDistance = row.GetFloat("LagMaxDistance"),
                    FovPerVz = row.GetFloat("FovPerVz"),
                    ArmPerVz = row.GetFloat("ArmPerVz"),
                    RollPerYawRate = row.GetFloat("RollPerYawRate"),
                },
                Seed = string.Equals(row.GetString("Seed"), "Row", StringComparison.OrdinalIgnoreCase)
                    ? CameraSeed.Row : CameraSeed.Engine,
                MouseLook = row.GetFloat("MouseLook") > 0.5f,
                LookSens = row.GetFloat("LookSens"),
                PitchMin = row.GetFloat("PitchMin"),
                PitchMax = row.GetFloat("PitchMax"),
                AnchorFollowHead = row.GetFloat("AnchorFollowHead") > 0.5f,
                AnchorHeight = row.GetFloat("AnchorHeight"),
            };
        }

        /// <summary>一行摘要（`custom.cam show` / 日志用；格式与 `FlightCameraRig.Describe` 同源。
        /// 🔴 全英文 —— 控制台返回文本禁中文（项目纪律）；那张人类可读的中文标签在 CSV 第 3 行，只给人看。</summary>
        public string Describe()
        {
            SpringArmCameraParam p = Param;
            return string.Format(
                "{0} | attach={1} arm={2:F1} fov={3:F0} yaw={4:F1} pitch={5:F1} "
                + "pivot=({6:F2},{7:F2},{8:F2}) socket=({9:F2},{10:F2},{11:F2}) self=({12:F1},{13:F1},{14:F1})"
                + " | anchorWorld={15} engineEye={16} lag={17:F1} lagmax={18:F1} fovvz={19:F2} armvz={20:F3} rollyaw={21:F3}"
                + " | seed={22} mouse={23} sens={24:F3} pitchClamp=[{25:F0},{26:F0}] followHead={27} anchorH={28:F2}",
                Id, AttachType, p.ArmLength, p.Fov, p.ArmYaw, p.ArmPitch,
                p.PivotX, p.PivotY, p.PivotZ, p.SocketX, p.SocketY, p.SocketZ,
                p.SelfYaw, p.SelfPitch, p.SelfRoll,
                p.IsAnchorWorld ? 1 : 0, p.UseEngineEyeHeight ? 1 : 0,
                p.LagSpeed, p.LagMaxDistance, p.FovPerVz, p.ArmPerVz, p.RollPerYawRate,
                Seed, MouseLook ? 1 : 0, LookSens, PitchMin, PitchMax,
                AnchorFollowHead ? 1 : 0, AnchorHeight);
        }
    }
}
