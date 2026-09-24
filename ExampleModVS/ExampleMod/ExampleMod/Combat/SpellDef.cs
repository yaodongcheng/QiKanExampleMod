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

		/// <summary>结算轴实现 id 列表（每个都收到同一次命中事件）。</summary>
		public readonly List<string> Payloads = new List<string>();

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

		/// <summary>重力（米/秒²）。0 = 平飞；&gt;0 = 抛物线下坠（瞄准时按抛物线解算初速）。</summary>
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
		///   （法阵，2.6 m 见方，本体"竖着"导入 —— Taikou 的 prefab 里也是靠 90° X 旋转才躺平的）。
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
					: "[Spell] 法术表为空（无内容包提供 AssetRegistry/Spells.xml）——法印开火照走引擎导弹");
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
