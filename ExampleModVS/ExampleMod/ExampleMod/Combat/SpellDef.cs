using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.ModuleManager;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 法术数据契约读取器 —— 一行法术 = 一条 <c>&lt;Spell&gt;</c>（内容包通用契约，铁律 3：本类不感知任何具体世界观）。
	///
	/// 数据在哪：任何模块（内容包）的 <c>ModuleData/AssetRegistry/Spells.xml</c>：
	///   &lt;Spells&gt;
	///     &lt;Family id="投射" targeting="…" delivery="…" payloads="…" /&gt;   ← 可选：声明/覆盖族预设
	///     &lt;Spell id="…" ammo="…" family="projectile" mesh="…" damage="60" … /&gt;
	///   &lt;/Spells&gt;
	///
	/// 🔴 **法术的身份 = 它用哪个弹药物品**（<c>ammo</c> = 物品 StringId，铁律 20）——
	///   不是法术名、不是 mesh。加一个新法术 = 加一行 &lt;Spell&gt; + 一个弹药物品，本类与其余四个类一行都不用改。
	/// 🔴 **一个法术 = 一个弹药 = 一个视觉**（禁止两行 &lt;Spell&gt; 共用同一个 ammo：改一行会静默影响另一行）。
	///
	/// 四段模型（详参 plans/法术体系-通用施法框架.md §二）：
	///   起手 Trigger（阶段 3）· 瞄准 Targeting · 投送 Delivery · 结算 Payloads
	///   <c>family</c> = 这四段常用组合的**预设**；三轴字段写了就覆盖族的默认值（§6.2「先选族，再按需覆盖」）。
	///   族本身也是数据：内建表给的是世界观无关的通用预设，内容包可加 &lt;Family&gt; 行扩展或覆盖。
	///
	/// 容错（铁律 1）：内容包没装 / 文件缺失 / 字段写错 → 跳过该项 + 一条日志，绝不抛异常。
	///   查不到 = 这个弹药不是法术弹 → 引擎照常发它的导弹（兜底路径，见计划 §5.4 第 6 条）。
	///
	/// 使用姿势：懒加载（首次查询才扫模块 + 读文件），之后整局缓存。
	/// </summary>
	// ── 数据契约：一行法术长什么样（与"怎么读"分开 —— 加字段只动这里）──

	/// <summary>法术族 = 四段常用组合的预设（给配表用，不给代码用）。</summary>
	public sealed class SpellFamily
	{
		/// <summary>族名 = 法术 id 的前缀规约（计划 §2.4：<c>projectile_yinmo</c> 一眼看出是投射族）。</summary>
		public string Id;

		/// <summary>瞄准轴默认实现 id（空 = 该族没定，法术必须自己写）。</summary>
		public string Targeting;

		/// <summary>投送轴默认实现 id。</summary>
		public string Delivery;

		/// <summary>结算轴默认实现 id（逗号分隔，可多个）。</summary>
		public string Payloads;
	}

	/// <summary>一行法术的完整数据（字段全部数据化 —— 这是自管实体路线白赚的：旧路线这些全写死在引擎里）。</summary>
	public sealed class SpellDef
	{
		// ── 身份 ──
		/// <summary>法术 id（StringId，铁律 20）。</summary>
		public string Id;

		/// <summary>哪个弹药物品 = 这个法术（物品 StringId）。唯一键。</summary>
		public string AmmoId;

		/// <summary>法术族 id（决定三轴默认值）。</summary>
		public string Family;

		// ── 四段（family 给默认，写了就覆盖）──
		/// <summary>瞄准轴实现 id。</summary>
		public string Targeting;

		/// <summary>投送轴实现 id。</summary>
		public string Delivery;

		/// <summary>
		/// 结算轴实现 id 列表（每个都收到同一次命中事件）。
		/// ⚠️ 刻意**不加 readonly**：<see cref="Clone"/> 要用 MemberwiseClone 复制整份定义，
		/// 而"解算会就地改的容器"必须换成新的一份（readonly 字段只有构造函数里能重新赋值）。
		/// </summary>
		public List<string> Payloads = new List<string>();

		// ── 视觉（全在内容包，LWN 只认名字）──
		/// <summary>飞行网格名（MetaMesh 名）。空 = 不画飞行物（但仍会飞、会命中）。</summary>
		public string Mesh;

		/// <summary>网格缩放（默认 1；网格资产改尺寸前的手感旋钮，运行时生效、不用重编资产）。</summary>
		public float Scale = 1f;

		/// <summary>
		/// 飞行姿态的**横滚角**（度，绕飞行轴；默认 0 = 网格本地"上"贴世界"上"）。
		/// 🔴 用法：网格的飞行轴必须是本地 **+Z**（原版弹丸约定：<c>bolt_bl_a</c> 尖端在 +Z 0.4785）。
		///   朝向不对时**先调这个数**（数据改、不用重编资产），别急着去改网格几何。
		/// </summary>
		public float TiltDeg;

		/// <summary>拖尾粒子名（挂在飞行实体上，引擎自动带着走）。空 = 不挂。</summary>
		public string TrailParticle;

		/// <summary>命中爆散粒子名（在命中点世界坐标炸一次）。空 = 不放。</summary>
		public string ImpactParticle;

		/// <summary>蓄力粒子名（**阶段 3 用**：蓄力法阵/手上发光）。阶段 1 解析但不读。</summary>
		public string ChargeParticle;

		/// <summary>发射音效名（内容包 module_sounds.xml 名）。空 = 不播。</summary>
		public string SoundRelease;

		/// <summary>命中音效名。空 = 不播。</summary>
		public string SoundHit;

		// ── 投送 ──
		/// <summary>初速（米/秒）。手感的唯一旋钮（原版弩 60）。</summary>
		public float Speed = 60f;

		/// <summary>
		/// 初速 × 修正倍率 —— 🔴 **发射解算唯一读这个**（<see cref="SpellAim.Emit"/> 与天降的起落速度）。
		/// 只加一处的理由：两处都乘一遍 = 挂"加速"宝石会变成平方。
		/// </summary>
		public float EffectiveSpeed
		{
			get { return Speed * (SpeedScale > 0.01f ? SpeedScale : 0.01f); }
		}

		/// <summary>
		/// 重力（米/秒²）—— 🔴 **带符号**：<c>&gt;0</c> = 往下掉（瞄准时按抛物线解算初速）·
		/// <c>0</c> = 平飞 · <c>&lt;0</c> = **反重力**（往上飘；不平抛解算，直接照瞄准方向打出去）。
		/// </summary>
		public float Gravity;

		/// <summary>
		/// **天降**：&gt;0 = 不从施法者手里飞出去，而是从**落点正上方这么多米**处朝落点砸下来
		/// （"瞄准轴给落点、投送轴复用直线飞（从上方起）" —— 计划 §2.2 ProjectileStrike 行）。
		/// 0 = 普通投射（从施法者手上出发）。
		/// </summary>
		public float StartHeight;

		/// <summary>
		/// **追踪**：转向速率（度/秒）。0 = 直线飞；&gt;0 = 每帧把速度方向朝目标转一点（限速转弯）。
		/// 目标优先取意图里的**目标引用**（软锁给的那个，取它"现在"的位置），没有就取瞄准点。
		/// </summary>
		public float TurnRate;

		/// <summary>发数（一发变 N 发）。由**瞄准轴**摊成 N 条意图（契约 1 的意图表就是干这个的）。</summary>
		public int Count = 1;

		/// <summary>多发散布角（度，围绕瞄准方向随机）。</summary>
		public float SpreadDeg;

		/// <summary>最大飞行距离（米）。与 max_lifetime 先到先算（计划 §十一 第 3 条）。</summary>
		public float MaxDistance = 120f;

		/// <summary>最长存活时间（秒）。</summary>
		public float MaxLifetime = 4f;

		/// <summary>命中半径（米）：喂给引擎射线的 rayThickness（= 沿线段扫一个这个半径的球 = 连续碰撞）。</summary>
		public float HitRadius = 1.2f;

		/// <summary>最多能命中几个目标（1 = 打中一个就结束；&gt;1 = 穿透继续飞）。</summary>
		public int Pierce = 1;

		/// <summary>放置族：这一片留在世界上多久（秒）。0 = 用 max_lifetime。</summary>
		public float Duration;

		/// <summary>放置族：每多少秒向结算上报一次（契约 3 —— 节流是投送的事）。</summary>
		public float RepeatInterval = 1f;

		/// <summary>放置族：落地后延迟多久才第一次生效（秒）。</summary>
		public float StartDelay;

		/// <summary>放置族：上面那个延迟的随机抖动（±，秒）—— 多个陷阱同时放不会整齐划一地一起响。</summary>
		public float StartDelayVariance;

		/// <summary>
		/// 落点指示圈：在地上画一个圈告诉你"法术会落在哪"（放置/天降族的必备件，计划 §3.6）。
		/// 值 = 网格名；空 = 不画。
		/// 🔴 网格约定：**平面法线 = 本地 +Y**（我们会对齐世界朝上）。现成可用件 = `lwn_flight_sigil`
		///   （法阵，2.6 m 见方，本体"竖着"导入 —— LWN 的 prefab 里也是靠 90° X 旋转才躺平的）。
		/// </summary>
		public string Indicator;

		/// <summary>指示圈缩放（默认 1 = 网格原尺寸）。</summary>
		public float IndicatorScale = 1f;

		/// <summary>
		/// 软锁最多锁几个目标（多目标法术用；1 = 单目标）。由**瞄准轴** `locked` 读。
		/// </summary>
		public int LockMax = 1;

		/// <summary>软锁的角度锥（度）—— 只锁视线正前方这个范围内的目标。</summary>
		public float LockAngle = 15f;

		/// <summary>软锁的最远距离（米）。</summary>
		public float LockRange = 40f;

		// ── 结算 ──
		/// <summary>伤害值。</summary>
		public float Damage = 60f;

		/// <summary>伤害类型（引擎 <see cref="DamageTypes"/> 枚举名，如 Cut / Pierce / Blunt）。</summary>
		public DamageTypes DamageType = DamageTypes.Cut;

		/// <summary>半径伤害的作用半径（米）。</summary>
		public float Radius = 3.5f;

		/// <summary>半径伤害的边缘衰减（0~1）：边缘保留 <c>1 - falloff</c> 的伤害。</summary>
		public float RadiusFalloff = 0.5f;

		/// <summary>状态 id（`status` 结算用；如 burn / poison / bleed）。空 = 不给状态。</summary>
		public string StatusId;

		/// <summary>状态每次跳的伤害。</summary>
		public float StatusDamage;

		/// <summary>状态持续多久（秒）。</summary>
		public float StatusDuration;

		/// <summary>状态每多少秒跳一次。</summary>
		public float StatusInterval = 1f;

		/// <summary>状态挂在目标身上的粒子名（空 = 无视觉）。</summary>
		public string StatusParticle;

		// ── 起手（阶段 3：蓄力 / 引导 / 瞬发）──
		/// <summary>蓄满需要按住多久（秒）。0 = 不可蓄力（按下即满蓄力）。</summary>
		public float ChargeTime;

		/// <summary>蓄满时的伤害加成倍率（0.5 = 满蓄力 1.5 倍伤害；0 = 蓄力不改伤害）。</summary>
		public float ChargeBonus;

		/// <summary>蓄力时手心的核用什么网格（默认 = `lwn_yinmo_core` 那个能量核；空 = 不画核）。</summary>
		public string ChargeMesh = "lwn_yinmo_core";

		/// <summary>
		/// 蓄力核**满蓄力时**的放大倍率（默认 <c>0.375</c> —— 核网格 ⌀0.72 m，即满蓄力 ⌀0.27 m）。
		/// 🔴 球的生长曲线 = **从 0 长到 1**（按蓄力进度线性）—— 起手几乎看不见，蓄满就不再变（2026-09-24 用户裁定："过程直观"）。
		/// 🔴 **嫌核太大/太小就改这个数**（数据改、不用重编资产也不用重启：法术表进场景时重读）。
		/// 2026-09-24 用户裁定：原来的 1.5 倍（⌀1.08 m）太大，缩到四分之一。
		/// </summary>
		public float ChargeScale = 0.375f;

		/// <summary>
		/// NPC 施法（阶段 4）：这个法术**允许 NPC 用**吗（默认允许）。
		/// 引擎侧还会再过一道"只开框架认的那几类族"的闸门（见 <see cref="SpellNpcCaster"/>）。
		/// </summary>
		public bool AiEnabled = true;

		/// <summary>NPC 选法术时的权重（带权随机；默认 1）。</summary>
		public float AiWeight = 1f;

		/// <summary>NPC 放完这个法术后的冷却（秒；默认 5）。</summary>
		public float AiCooldown = 5f;

		/// <summary>
		/// 施法者（法印）的物品 id —— **只有"给 NPC 发一套法印"这类测试/装配用途会读它**
		/// （正常玩法里法印是玩家自己装的）。空 = 该法术没有配套法印，NPC 装配命令会跳过。
		/// </summary>
		public string SealItem;

		// ── 起手（阶段 3 用；阶段 1 引擎开火即出手，字段先留着，表结构不用再改）──
		/// <summary>出手帧阈值（动画进度 0~1）。</summary>
		public float ReleaseAt = 0.42f;

		/// <summary>收尾帧阈值（动画进度 0~1）。</summary>
		public float EndAt = 0.8f;

		/// <summary>施法方式：normal（有前摇）/ channel（按住持续）/ instant（瞬发）。</summary>
		public string CastType = "normal";

		// ═══════════════════════════════════════════════════════════════════════
		// 阶段 5：**修正机制**读的那些属性（计划 §16.1 的八个作用点）
		//
		// 🔴 纪律：**每一个字段都必须能说出它在八处（①推进 ②定向 ③命中查询 ④命中裁决 ⑤触发 ⑥到期
		//   ⑦表现 ⑧结算）的哪一处被读**；说不出来的 = 装饰品，不进 <see cref="SpellFields"/> 那张表。
		// 🔴 这些字段全部默认"等于现状" —— 所以**旧法术的行为一个都不变**（阶段 5 的验收第 2 条）。
		// ═══════════════════════════════════════════════════════════════════════

		// ── ① 推进 ──
		/// <summary>初速倍率（修正改速度走它；<see cref="Speed"/> 是绝对值、留给数据直接设）。</summary>
		public float SpeedScale = 1f;

		/// <summary>空气阻力（每秒衰减比例；0 = 不减速）。</summary>
		public float Drag;

		/// <summary>沿飞行方向的加速度（米/秒²；负数 = 边飞边慢）。</summary>
		public float Accel;

		/// <summary>起飞后多少秒才开始追踪（秒；0 = 立刻追）。</summary>
		public float TurnDelay;

		// ── ② 定向 ──
		/// <summary>
		/// 网格朝向模式：<c>fixed</c>（**默认** = 进入时算一次、之后只挪位置 —— 现状）
		/// 或 <c>velocity</c>（每帧按当前速度方向重算 —— 追踪弹才需要）。
		/// </summary>
		public string OrientMode = "fixed";

		// ── ③ 命中查询 ──
		/// <summary>撞不撞地形/墙面（关掉 = 连这条查询都不做，省一次原生调用）。</summary>
		public bool CollideTerrain = true;

		/// <summary>撞不撞人。</summary>
		public bool CollideAgent = true;

		// ── ④ 命中裁决 ──
		/// <summary>打不打自己人（**默认打** —— 引擎现状如此，改默认值 = 改旧法术行为）。</summary>
		public bool HitSameTeam = true;

		/// <summary>能弹几下（0 = 撞到就结束）。撞地形时按命中法线反射速度。</summary>
		public int BounceCount;

		/// <summary>每次弹跳保留多少速度（0.6 = 弹完剩六成）。</summary>
		public float BounceDamping = 0.6f;

		/// <summary>钻地：撞地形**不停**、继续飞（飞行距离照算，也不报命中）。</summary>
		public bool Drill;

		// ── ⑤ 触发（载荷 = 一个"装配块"，见 <see cref="SpellTriggerPayload"/>）──
		/// <summary>命中时放的子块 id（＝宝石里 <c>&lt;Sub id="…"&gt;</c> 的名字），空 = 不触发。</summary>
		public string OnHitCast;

		/// <summary>飞行 <see cref="TimerSeconds"/> 秒后放的子块 id（在空中放，不是命中）。</summary>
		public string OnTimerCast;

		/// <summary>定时触发的秒数。</summary>
		public float TimerSeconds;

		/// <summary>到期 / 飞完时放的子块 id。</summary>
		public string OnExpireCast;

		/// <summary>每次弹跳时放的子块 id。</summary>
		public string OnBounceCast;

		/// <summary>子法术最多还能再套几层（防无限套娃；解算时读，默认 3）。</summary>
		public int TriggerDepth = 3;

		/// <summary>飞到行程的百分之几时分裂（0~1；0 = 不分裂）。</summary>
		public float SplitAt;

		/// <summary>分裂成几发。</summary>
		public int SplitCount;

		/// <summary>
		/// 命中/到期时留下哪种地表（阶段 8 的世界层读它）。
		/// ⚠️ **现在没有读取点会真的造地表** —— 投送会在命中时打一条"要等阶段 8"的告警，
		/// 免得数据作者以为自己配错了（计划 §16.5 把"留地表"归到 D 组：等后面的层）。
		/// </summary>
		public string LeaveSurface;

		// ── ⑧ 结算 ──
		/// <summary>伤害浮动（0.25 = 每次随机乘 0.75~1.0；**0 = 不浮动** —— 旧法术的默认值）。</summary>
		public float DamageVariance;

		/// <summary>暴击率（0~1；**0 = 不暴击** —— 旧法术的默认值）。</summary>
		public float Crit;

		/// <summary>暴击倍率（默认 2 = FCS 那套）。</summary>
		public float CritMult = 2f;

		// ── 运行时（不来自 XML）──
		/// <summary>
		/// 触发载荷表：子块 id → 解算好的子法术（有效定义）。
		/// 🔴 **由 <see cref="SpellResolver"/> 在解算时填**，只有"带触发器的有效定义"里才有内容；
		/// 基础术（表里那一条）永远是空的 —— 所以它不参与数据解析，也不进存档。
		/// </summary>
		public Dictionary<string, SpellTriggerPayload> Triggers =
			new Dictionary<string, SpellTriggerPayload>(StringComparer.Ordinal);

		/// <summary>
		/// 这份定义**已经解算过**了（配装修正已落完）—— <see cref="SpellLoadout.ResolveFor"/> 见到它就原样放行。
		/// 🔴 两个用途：① 触发的子法术**不继承外层配装**（§16.4：子块独立解算）
		///   ② 上层已经解算过的定义被第二次施放时**不会重复叠一遍修正**。
		/// </summary>
		public bool Resolved;

		/// <summary>
		/// 复制一份定义（修正解算的起点）。
		/// 🔴 用 <c>MemberwiseClone</c> 自动带上**全部**字段 ⇒ **以后加字段不用动这里**（少一处漂移源）；
		///   代价是"解算会就地改的两个容器"必须显式换成新的一份（否则会改到表里那条共享定义）。
		/// </summary>
		public SpellDef Clone()
		{
			SpellDef copy = (SpellDef)MemberwiseClone();
			copy.Payloads = new List<string>(Payloads);
			copy.Triggers = new Dictionary<string, SpellTriggerPayload>(Triggers, StringComparer.Ordinal);
			return copy;
		}

		/// <summary>
		/// 同一条法术吗 —— 🔴 **比 id，不比引用**。
		/// 理由：阶段 5 之后手里拿的可能是**解算出来的副本**（配了宝石时）⇒ 拿引用比会永远不等，
		/// 后果是"换法术了 = 打断"与"同法术状态刷新不叠"这两条**同时静默失效**。
		/// </summary>
		public bool SameAs(SpellDef other)
		{
			return other != null && string.Equals(Id, other.Id, StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// 一个**触发载荷** = 一个"装配块"（计划 §16.4）：子法术的完整有效定义 + 它自己的缩放。
	/// 🔴 不是一个法术 id —— 只填 id 会把"触发器里塞带修正的法术"这半个能力直接丢掉。
	/// </summary>
	public sealed class SpellTriggerPayload
	{
		/// <summary>子块 id（诊断用）。</summary>
		public string Id;

		/// <summary>子块独立解算后的**完整法术定义**（含它自己的宝石，不继承外层修正）。</summary>
		public SpellDef Spell;

		/// <summary>解算时的层级（0 = 直接由基础术触发；最深 <see cref="SpellDef.TriggerDepth"/>）。</summary>
		public int Depth;
	}


	/// <summary>
	/// 法术表读取器 —— 扫全部模块的 <c>ModuleData/AssetRegistry/Spells.xml</c>。
	/// 读取范式照抄 <see cref="FirearmFxRegistry"/>：懒加载、后加载覆盖先加载、缺项跳过不抛异常。
	/// </summary>
	public static class SpellRegistry
	{
		private const string FileName = "Spells.xml";

		private static bool _loaded;
		private static readonly object _lock = new object();

		/// <summary>弹药物品 StringId → 法术（唯一键，认领用）。</summary>
		private static readonly Dictionary<string, SpellDef> _byAmmo =
			new Dictionary<string, SpellDef>(StringComparer.Ordinal);

		private static readonly Dictionary<string, SpellDef> _byId =
			new Dictionary<string, SpellDef>(StringComparer.Ordinal);

		/// <summary>按加载顺序排的法术（`Dictionary` 顺序不保证，所以另存一份）—— 见 <see cref="DefaultFlightSpell"/>。</summary>
		private static readonly List<SpellDef> _ordered = new List<SpellDef>();

		private static readonly Dictionary<string, SpellFamily> _families =
			new Dictionary<string, SpellFamily>(StringComparer.Ordinal);

		/// <summary>
		/// 内建族预设 —— 计划 §2.4 那张表，全部是**世界观无关的通用组合**（不属任何内容包）。
		/// 内容包可以在自己的 Spells.xml 里加 &lt;Family&gt; 行覆盖或扩展（后加载的覆盖先加载的）。
		/// 🔴 轴上写了一个**还没有实现**的 id（如阶段 2 的 place / skyfall）= 用该族的法术会被跳过并记日志，
		///   这是**正确报错**（不是崩、也不是静默变成别的东西）。
		/// </summary>
		private static void RegisterBuiltInFamilies()
		{
			AddFamily("projectile", "aim",    "projectile", "damage,area");   // 投射：准星 → 直线飞 → 单体+半径
			AddFamily("channel",    "aim",    "channel",    "damage");        // 引导：按住 → 持续存在 → 按间隔重复结算
			AddFamily("skyfall",    "ground", "projectile", "area");          // 天降：落点 → 从上方落下 → 范围
			AddFamily("place",      "ground", "place",      "area");          // 放置/区域：落点 → 留在原地 → 按间隔重复
			AddFamily("move",       "aim",    "move",       "damage");        // 移动：施法者自己位移 → 沿途伤害
			AddFamily("rune",       "ground", "rune",       "damage");        // 符文：落点 → 留在地上 → 踩中触发
			AddFamily("buff",       "self",   "instant",    "status");        // 增益：自身 → 瞬时 → 给状态
			AddFamily("heal",       "self",   "instant",    "heal");          // 治疗：自身/软锁 → 瞬时 → 回血
			AddFamily("summon",     "ground", "instant",    "summon");        // 召唤：落点 → 瞬时 → 生成实体
			AddFamily("morph",      "locked", "instant",    "morph");         // 变形：软锁 → 瞬时 → 换模型换属性
			AddFamily("teleport",   "ground", "move",       "none");          // 传送：落点 → 位移 → 落点效果
		}

		private static void AddFamily(string id, string targeting, string delivery, string payloads)
		{
			_families[id] = new SpellFamily
			{
				Id = id,
				Targeting = targeting,
				Delivery = delivery,
				Payloads = payloads,
			};
		}

		/// <summary>按弹药 StringId 查法术。返回 null = 这个弹药不是法术弹（调用方照旧放行引擎）。</summary>
		public static SpellDef FindByAmmo(string ammoId)
		{
			if (string.IsNullOrEmpty(ammoId))
			{
				return null;
			}
			EnsureLoaded();
			SpellDef def;
			return _byAmmo.TryGetValue(ammoId, out def) ? def : null;
		}

		/// <summary>按法术 id 查（诊断/命令用）。</summary>
		public static SpellDef FindById(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}
			EnsureLoaded();
			SpellDef def;
			return _byId.TryGetValue(id, out def) ? def : null;
		}

		/// <summary>已登记的法术数量（诊断用）。</summary>
		public static int Count
		{
			get { EnsureLoaded(); return _byAmmo.Count; }
		}

		/// <summary>已解析的族（内建 + 内容包声明）。</summary>
		public static IReadOnlyDictionary<string, SpellFamily> Families
		{
			get { EnsureLoaded(); return _families; }
		}

		/// <summary>全部法术（诊断用）。</summary>
		public static IEnumerable<SpellDef> All
		{
			get { EnsureLoaded(); return _byId.Values; }
		}

		/// <summary>
		/// **飞行默认法术** —— 飞行中"不要求装备"（2026-09-24 用户裁定）时放哪一个：
		/// 表里**第一条 `family="projectile"`** 的法术；没有 projectile 就取第一条法术；一条法术都没有 = null（飞行中不放法术）。
		/// 🔴 想让某个法术当飞行默认，把它**排在表里前面**即可（跨模块时按模块加载顺序）。
		/// </summary>
		public static SpellDef DefaultFlightSpell
		{
			get
			{
				EnsureLoaded();
				for (int i = 0; i < _ordered.Count; i++)
				{
					if (_ordered[i].Family == "projectile")
					{
						return _ordered[i];
					}
				}
				return _ordered.Count > 0 ? _ordered[0] : null;
			}
		}

		/// <summary>
		/// **重读法术表**（每次进场景调一次，见 <see cref="SpellProjectileLogic"/> 的构造）——
		/// 这样改 <c>Spells.xml</c> **不用重启游戏**，重进一次战场就生效（调数值的循环快一个数量级）。
		/// 🔴 清空重来：内建族由 <see cref="EnsureLoaded"/> 重新登记，内容包的族与法术从文件重新读。
		/// </summary>
		public static void Reload()
		{
			lock (_lock)
			{
				_loaded = false;
				_byAmmo.Clear();
				_byId.Clear();
				_ordered.Clear();
				_families.Clear();
			}
			EnsureLoaded();
		}

		private static void EnsureLoaded()
		{
			if (_loaded)
			{
				return;
			}
			lock (_lock)
			{
				if (_loaded)
				{
					return;
				}
				RegisterBuiltInFamilies();
				try
				{
					foreach (ModuleInfo module in ModuleHelper.GetModules())
					{
						string path = Path.Combine(ModuleHelper.GetModuleFullPath(module.Id),
							"ModuleData", "AssetRegistry", FileName);
						if (File.Exists(path))
						{
							LoadFile(path);
						}
					}
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[Spell] 扫描失败：{ex.GetType().Name} {ex.Message}");
				}
				DebugLogger.Log(_byAmmo.Count > 0
					? $"[Spell] 法术表就绪：{_byAmmo.Count} 条（{string.Join(" / ", _byAmmo.Keys)}）· 族 {_families.Count} 个"
					: "[Spell] 法术表为空（本模块 ModuleData/AssetRegistry/Spells.xml 没加载）——法印开火照走引擎导弹");
				_loaded = true;
			}
		}

		private static void LoadFile(string path)
		{
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(path);
				XmlNode root = doc.DocumentElement;
				if (root == null)
				{
					return;
				}
				foreach (XmlNode node in root.ChildNodes)
				{
					if (node.NodeType != XmlNodeType.Element)
					{
						continue;
					}
					if (node.Name == "Family")
					{
						LoadFamily(node, path);
					}
					else if (node.Name == "Spell")
					{
						LoadSpell(node, path);
					}
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 读取 {Path.GetFileName(path)} 失败：{ex.GetType().Name} {ex.Message}");
			}
		}

		private static void LoadFamily(XmlNode node, string path)
		{
			string id = Attr(node, "id");
			if (string.IsNullOrEmpty(id))
			{
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：Family 缺 id，跳过");
				return;
			}
			SpellFamily f = new SpellFamily
			{
				Id = id,
				Targeting = Attr(node, "targeting"),
				Delivery = Attr(node, "delivery"),
				Payloads = Attr(node, "payloads"),
			};
			// 后加载的覆盖先加载的（与骑砍「后注册覆盖」惯例一致，也便于内容包互相覆盖）
			_families[id] = f;
		}

		private static void LoadSpell(XmlNode node, string path)
		{
			string id = Attr(node, "id");
			string ammo = Attr(node, "ammo");
			string family = Attr(node, "family");
			if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(ammo) || string.IsNullOrEmpty(family))
			{
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：法术缺 id/ammo/family"
					+ $"（id='{id}' ammo='{ammo}' family='{family}'），跳过");
				return;
			}

			SpellFamily fam;
			if (!_families.TryGetValue(family, out fam))
			{
				// 🔴 认不出的族 = 整个法术不加载（数据填错的兜底；不是"退回引擎导弹"那条路径）
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：法术 '{id}' 的族 '{family}' 认不出 → 不加载");
				return;
			}

			SpellDef def = new SpellDef
			{
				Id = id,
				AmmoId = ammo,
				Family = family,
				// 三轴：写了覆盖，没写吃族默认
				Targeting = Fallback(Attr(node, "targeting"), fam.Targeting),
				Delivery = Fallback(Attr(node, "delivery"), fam.Delivery),
				Mesh = Attr(node, "mesh"),
				Scale = FloatAttr(node, "scale", 1f),
				TiltDeg = FloatAttrAllowZero(node, "tilt_deg", 0f),
				TrailParticle = Attr(node, "trail_particle"),
				ImpactParticle = Attr(node, "impact_particle"),
				ChargeParticle = Attr(node, "charge_particle"),
				SoundRelease = Attr(node, "sound_release"),
				SoundHit = Attr(node, "sound_hit"),
				Speed = FloatAttr(node, "speed", 60f),
				Gravity = FloatAttrAllowZero(node, "gravity", 0f),
				StartHeight = FloatAttrAllowZero(node, "start_height", 0f),
				TurnRate = FloatAttrAllowZero(node, "turn_rate", 0f),
				Count = IntAttr(node, "count", 1),
				SpreadDeg = FloatAttrAllowZero(node, "spread_deg", 0f),
				MaxDistance = FloatAttr(node, "max_distance", 120f),
				MaxLifetime = FloatAttr(node, "max_lifetime", 4f),
				HitRadius = FloatAttr(node, "hit_radius", 1.2f),
				Pierce = IntAttr(node, "pierce", 1),
				Duration = FloatAttrAllowZero(node, "duration", 0f),
				RepeatInterval = FloatAttr(node, "repeat_interval", 1f),
				StartDelay = FloatAttrAllowZero(node, "start_delay", 0f),
				StartDelayVariance = FloatAttrAllowZero(node, "start_delay_variance", 0f),
				Indicator = Attr(node, "indicator"),
				IndicatorScale = FloatAttr(node, "indicator_scale", 1f),
				LockMax = IntAttr(node, "lock_max", 1),
				LockAngle = FloatAttr(node, "lock_angle", 15f),
				LockRange = FloatAttr(node, "lock_range", 40f),
				Damage = FloatAttrAllowZero(node, "damage", 60f),
				Radius = FloatAttrAllowZero(node, "radius", 3.5f),
				RadiusFalloff = FloatAttrAllowZero(node, "radius_falloff", 0.5f),
				StatusId = Attr(node, "status"),
				StatusDamage = FloatAttrAllowZero(node, "status_damage", 0f),
				StatusDuration = FloatAttrAllowZero(node, "status_duration", 0f),
				StatusInterval = FloatAttr(node, "status_interval", 1f),
				StatusParticle = Attr(node, "status_particle"),
				ChargeTime = FloatAttrAllowZero(node, "charge_time", 0f),
				ChargeBonus = FloatAttrAllowZero(node, "charge_bonus", 0f),
				ChargeMesh = Fallback(Attr(node, "charge_mesh"), "lwn_yinmo_core"),
				ChargeScale = FloatAttr(node, "charge_scale", 0.375f),
				AiEnabled = BoolAttr(node, "ai", true),
				AiWeight = FloatAttr(node, "ai_weight", 1f),
				AiCooldown = FloatAttr(node, "ai_cooldown", 5f),
				SealItem = Attr(node, "seal"),
				ReleaseAt = FloatAttrAllowZero(node, "release_at", 0.42f),
				EndAt = FloatAttrAllowZero(node, "end_at", 0.8f),
				CastType = Fallback(Attr(node, "cast_type"), "normal"),
				DamageType = ParseDamageType(Attr(node, "damage_type"), id),

				// ── 阶段 5：修正机制读的属性（默认值 = 现状，所以旧法术行为一个都不变）──
				SpeedScale = FloatAttr(node, "speed_scale", 1f),
				Drag = FloatAttrAllowZero(node, "drag", 0f),
				Accel = FloatAttrAllowZero(node, "accel", 0f),
				TurnDelay = FloatAttrAllowZero(node, "turn_delay", 0f),
				OrientMode = Fallback(Attr(node, "orient_mode"), "fixed"),
				CollideTerrain = BoolAttr(node, "collide_terrain", true),
				CollideAgent = BoolAttr(node, "collide_agent", true),
				HitSameTeam = BoolAttr(node, "hit_same_team", true),
				BounceCount = IntAttr(node, "bounce_count", 0),
				BounceDamping = FloatAttrAllowZero(node, "bounce_damping", 0.6f),
				Drill = BoolAttr(node, "drill", false),
				OnHitCast = Attr(node, "on_hit_cast"),
				OnTimerCast = Attr(node, "on_timer_cast"),
				TimerSeconds = FloatAttrAllowZero(node, "timer_seconds", 0f),
				OnExpireCast = Attr(node, "on_expire_cast"),
				OnBounceCast = Attr(node, "on_bounce_cast"),
				TriggerDepth = IntAttr(node, "trigger_depth", 3),
				SplitAt = FloatAttrAllowZero(node, "split_at", 0f),
				SplitCount = IntAttr(node, "split_count", 0),
				LeaveSurface = Attr(node, "leave_surface"),
				DamageVariance = FloatAttrAllowZero(node, "damage_variance", 0f),
				Crit = FloatAttrAllowZero(node, "crit", 0f),
				CritMult = FloatAttr(node, "crit_mult", 2f),
			};

			string payloads = Fallback(Attr(node, "payloads"), fam.Payloads);
			if (!string.IsNullOrEmpty(payloads))
			{
				foreach (string piece in payloads.Split(','))
				{
					string trimmed = piece.Trim();
					if (trimmed.Length > 0)
					{
						def.Payloads.Add(trimmed);
					}
				}
			}

			if (_byAmmo.ContainsKey(ammo))
			{
				// 一个法术 = 一个弹药 = 一个视觉（计划 §6.2）：重复 ammo 会让两行互相覆盖，是数据错误
				DebugLogger.Log($"[Spell] 弹药 '{ammo}' 被多个法术登记（已有 '{_byAmmo[ammo].Id}'，又来 '{id}'）"
					+ " → 后者覆盖前者（一个法术应该独占一个弹药）");
			}
			_byAmmo[ammo] = def;
			_byId[id] = def;
			// 记加载顺序（字典顺序不保证）—— 「飞行默认法术」这类"取第一条"的用途靠它
			if (!_ordered.Contains(def))
			{
				_ordered.Add(def);
			}
		}

		private static DamageTypes ParseDamageType(string raw, string spellId)
		{
			if (string.IsNullOrEmpty(raw))
			{
				return DamageTypes.Cut;
			}
			try
			{
				return (DamageTypes)Enum.Parse(typeof(DamageTypes), raw, true);
			}
			catch (Exception)
			{
				DebugLogger.Log($"[Spell] 法术 '{spellId}' 的 damage_type '{raw}' 不是引擎 DamageTypes 的名字 → 退回 Cut");
				return DamageTypes.Cut;
			}
		}

		private static string Fallback(string value, string fallbackValue)
		{
			return string.IsNullOrEmpty(value) ? fallbackValue : value;
		}

		private static string Attr(XmlNode node, string name)
		{
			XmlAttribute a = node.Attributes?[name];
			string v = a?.Value;
			return string.IsNullOrEmpty(v) ? null : v.Trim();
		}

		private static float ParseFloat(string raw, float fallback)
		{
			float v;
			if (!string.IsNullOrEmpty(raw)
				&& float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
			{
				return v;
			}
			return fallback;
		}

		/// <summary>只接受 &gt;0 的值（尺寸/速度/寿命/半径这类"不能为零"的字段）。</summary>
		private static float FloatAttr(XmlNode node, string name, float fallback)
		{
			float v = ParseFloat(Attr(node, name), fallback);
			return v > 0f ? v : fallback;
		}

		/// <summary>允许 0（重力 0 = 平飞、转向 0 = 直线、衰减 0 = 不衰减 —— 0 是有意义的值）。</summary>
		private static float FloatAttrAllowZero(XmlNode node, string name, float fallback)
		{
			string raw = Attr(node, name);
			return string.IsNullOrEmpty(raw) ? fallback : ParseFloat(raw, fallback);
		}

		/// <summary>布尔属性（"true"/"1" = 真；不写 = 用默认值）。</summary>
		private static bool BoolAttr(XmlNode node, string name, bool fallback)
		{
			string raw = Attr(node, name);
			if (string.IsNullOrEmpty(raw))
			{
				return fallback;
			}
			return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1";
		}

		private static int IntAttr(XmlNode node, string name, int fallback)		{
			int v;
			if (!string.IsNullOrEmpty(Attr(node, name))
				&& int.TryParse(Attr(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
			{
				return v;
			}
			return fallback;
		}
	}
}
