using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>法术的运行时开关（诊断用；游戏内控制台 <c>custom.spell</c> 改）。</summary>
	public static class SpellDebug
	{
		/// <summary>每 0.05 秒的碰撞检查都打一行日志（验"扫掠到底打不打得到人"用）。默认关。</summary>
		public static bool Verbose;

		/// <summary>
		/// 同时在飞的投送物上限（计划 §4.4 纪律 5：**宁可拒绝施放，也不要"加了再说"**）。
		/// 32 的来由：阶段 4 最坏情况"30 个施法者 × 每人 3 发"也在同一量级。
		/// </summary>
		public static int MaxLive = 32;

		/// <summary>
		/// 飞行姿态的横滚角覆盖（度）。null = 用数据里每个法术自己的 <c>tilt_deg</c>。
		/// 🔴 用途：迁移时"网格本地朝向 ↔ 我们算的 frame"要重标一次（计划 §7.3）——
		///   用它与 <c>custom.spell cast</c> 配合，**一次实机就能试遍几个候选朝向**，不用改数据重开局。
		/// </summary>
		public static float? TiltOverrideDeg;

		/// <summary>
		/// 力度覆盖（0~1）。null = 用起手轴算出来的真实蓄力档位。
		/// 用途：<c>custom.spell power 0.2|1</c> + <c>cast</c>，**不用按住键**就能验"蓄满与不蓄满差多少"。
		/// </summary>
		public static float? PowerOverride;

		/// <summary>
		/// 飞行物**放大倍数**覆盖。null = 用数据里每个法术自己的 <c>scale</c>。
		/// 用途：<c>custom.spell scale 3</c> 当场把月牙放大三倍再 <c>cast</c> —— **不用重启游戏**。
		/// </summary>
		public static float? ScaleOverride;

		/// <summary>
		/// **命中半径**覆盖（米）。null = 用数据里的 <c>hit_radius</c>。
		/// 用途：<c>custom.spell hit 3</c> 让命中判定更宽松（薄片飞得快时肉眼判断容易"以为没中"）。
		/// </summary>
		public static float? HitRadiusOverride;

		/// <summary>
		/// **蓄力核**满蓄力时的放大倍率覆盖。null = 用数据里每个法术自己的 <c>charge_scale</c>。
		/// 用途：<c>custom.spell core 0.2</c> 按住 X 当场看核变大变小 —— **不用改数据、不用重启**
		/// （下次按住施法键就生效；核已经画出来时会等下一次施法）。
		/// </summary>
		public static float? ChargeScaleOverride;
	}

	/// <summary>
	/// 法术飞行物的宿主 —— 持有全部在飞的投送物，每帧推进、到期回收。
	///
	/// 挂载位置：<c>MySubModule.OnMissionBehaviorInitialize</c>，**必须在"玩法闸门"
	/// （<c>Settings.IsInteractionDisabled</c>）之前** —— 法术的主战场就是战场/攻城，
	/// 被那道闸门拦在外面就没意义了（同 FirearmFxLogic / SpellMissileTrace 的做派）。
	/// 无法术在飞时每帧只做一次 <c>Count</c> 判断，零开销。
	///
	/// 诊断（游戏内 <c>~</c> 控制台，返回文本纯英文、详情走 DebugLogger）：
	///   custom.spell                        状态（法术表 / 在飞数 / 上限 / 朝向覆盖）
	///   custom.spell list                   列出表里全部法术
	///   custom.spell cast &lt;spellId&gt;         从玩家眼睛沿视线直接放一发（**不用装备法印**，测试用）
	///   custom.spell npc [spellId]          让最近的那个人朝玩家放一发（**验"管线不关心施法者是谁"**，铁律 18）
	///   custom.spell tilt &lt;度&gt;|off          设置/清除飞行姿态的横滚角覆盖（§7.3 朝向标定）
	///   custom.spell verbose on|off         每个碰撞检查都打日志
	///   custom.spell cap &lt;n&gt;                改在飞上限
	///   custom.spell give [spellId]         给最近的 NPC 装一套"法印+法术弹"→ **它自己会放**（阶段 4 的验证）
	///   custom.spell power &lt;0~1&gt;|off         力度覆盖（验蓄力档位；配 cast 用）
	///   custom.spell scale &lt;倍率&gt;|off         **放大月牙**（当场生效，不用重启；配 cast 用）
	///   custom.spell hit &lt;米&gt;|off             命中半径覆盖（判定更宽松/更严）
	///   custom.spell core &lt;倍率&gt;|off          **手心蓄力核**的大小覆盖（按住 X 就能看到）
	///   custom.spell probe                  对最近的一个 agent 做三条实测定性（见方法注释）
	/// </summary>
	public class SpellProjectileLogic : MissionLogic
	{
		/// <summary>在飞的投送物（复用同一个 List，不在热路径 new）。</summary>
		private readonly List<SpellShot> _live = new List<SpellShot>();

		/// <summary>落点指示圈实体（放置/天降族的"法术会落在哪"，同一次只用一个）。</summary>
		private GameEntity _indicator;

		/// <summary>指示圈当前显示的是哪个法术的（换了法术要重建）。</summary>
		private string _indicatorSpellId;

		/// <summary>指示圈刷新节流（不是每帧都打射线）。</summary>
		private float _indicatorTimer;

		/// <summary>当前在飞数量。</summary>
		public int LiveCount
		{
			get { return _live.Count; }
		}

		/// <summary>玩家起手（相位机：蓄力 / 引导 / 瞬发）。</summary>
		private readonly SpellCastInput _castInput = new SpellCastInput();

		/// <summary>NPC 施法者（阶段 4）。</summary>
		private readonly SpellNpcCaster _npcCaster = new SpellNpcCaster();

		/// <summary>玩家相位机正在施法中吗（蓄力/引导中）—— 法印补丁靠它避免与相位机重复放同一发法术。</summary>
		public bool IsPlayerCasting
		{
			get { return _castInput.IsCasting; }
		}

		public SpellProjectileLogic()
		{
			// 预热法术表：把"首次查询才扫模块读文件"的活儿放在进场时做掉，
			// 免得第一发法术在战场中途卡一下（顺带每场景记一条"表里有什么"）。
			try
			{
				SpellStatusManager.Reset();   // 新场景 = 上一场挂的状态全清（状态是场景范围内的东西）
				DebugLogger.Log($"[Spell] 场景就绪：法术表 {SpellRegistry.Count} 条 / 族 {SpellRegistry.Families.Count} 个");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 预热失败：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>收一发投送物。返回 false = 并发已满，调用方负责把自己那发回收掉。</summary>
		public bool TryAddShot(SpellShot shot)
		{
			if (shot == null || shot.Delivery == null)
			{
				return false;
			}
			if (_live.Count >= SpellDebug.MaxLive)
			{
				DebugLogger.Log($"[Spell] 在飞已达上限 {SpellDebug.MaxLive} → 拒绝这次施放（计划 §4.4 纪律 5）");
				return false;
			}
			_live.Add(shot);
			return true;
		}

		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);
			if (MBCommon.IsPaused)
			{
				return;
			}

			// 🔴 输入系统：**只有玩法闸门关着（= 战场/竞技场/潜入）的时候才由我们推进**
			//    —— 那些场景里 InteractionMissionView 不在场（它才是平时的推进入口），
			//    两边都推 = 按住时长按两倍速走，长按阈值会变快（会悄悄改掉别的玩法行的手感）。
			if (Settings.Instance.IsInteractionDisabled())
			{
				ModInput.Tick(dt);
			}

			// 起手（玩家相位机）+ 阶段 4（NPC 施法者）
			_castInput.Tick(dt);
			_npcCaster.Tick(dt);

			// 持续状态（燃烧/中毒…）—— 与有没有在飞的投送物无关，所以放在前面
			SpellStatusManager.Tick(dt);

			// 落点指示圈（放置/天降族：手上有这类法术弹时在地上画个圈）
			UpdateGroundIndicator(dt);

			if (_live.Count == 0)
			{
				return;
			}
			// 倒着走：回收时 RemoveAt 不影响还没处理的下标
			for (int i = _live.Count - 1; i >= 0; i--)
			{
				SpellShot shot = _live[i];
				bool alive;
				try
				{
					alive = shot.Delivery != null && shot.Delivery.Tick(dt);
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[Spell] 投送推进异常（这一发直接回收）：{ex.GetType().Name} {ex.Message}");
					alive = false;
				}
				if (!alive)
				{
					Retire(i);
				}
			}
		}

		public override void OnRemoveBehavior()
		{
			base.OnRemoveBehavior();
			// 场景卸载：把还没结束的投送物全部清掉（否则实体留在世界里/留到下一个场景）
			for (int i = _live.Count - 1; i >= 0; i--)
			{
				Retire(i);
			}
			_live.Clear();
			SpellStatusManager.Reset();
			_castInput.Cancel();
			_npcCaster.Reset();
			HideIndicator();
		}

		// ─────────────────── 落点指示圈（放置/天降族的必备件）───────────────────

		/// <summary>
		/// 手上有"落点类"法术弹时，在准星指向的地面上画一个圈，告诉你法术会落在哪。
		/// 判据 = **手上武器的弹药**能查到法术、且那个法术的瞄准轴是 `ground`、且数据里给了
		/// <c>indicator</c> 网格（三者缺一就不画）。玩家专属（NPC 没有 UI，铁律 18）。
		/// 节流：每 0.05 秒才打一次射线（同碰撞检查的口径）。
		/// </summary>
		private void UpdateGroundIndicator(float dt)
		{
			_indicatorTimer += dt;
			if (_indicatorTimer < 0.05f)
			{
				return;
			}
			_indicatorTimer = 0f;

			Mission mission = Mission.Current;
			Agent player = mission != null ? mission.MainAgent : null;
			SpellDef spell = SpellWorld.ResolveWieldedSpell(player);
			if (spell == null || spell.Targeting != "ground" || string.IsNullOrEmpty(spell.Indicator))
			{
				HideIndicator();
				return;
			}

			Vec3 origin = player.Position;
			origin.z += player.GetEyeGlobalHeight();
			// 🔴 指示圈也按**相机朝向**（与施法方向同口径，否则圈和法术落点对不上）
			Vec3 point = SpellAim.ResolveSurfacePoint(origin, SpellWorld.CastDirection(player), spell.MaxDistance);
			ShowIndicator(spell, point);
		}

		/// <summary>取"手上这一发打的是哪个法术" —— 与法印补丁同一套取值口径（弹药 = 法术）。</summary>
		private static SpellDef ResolveWieldedSpell(Agent agent)
		{
			if (agent == null)
			{
				return null;
			}
			MissionWeapon wielded = agent.WieldedWeapon;
			if (wielded.IsEqualTo(MissionWeapon.Invalid) || wielded.Item == null)
			{
				return null;
			}
			WeaponComponentData usage = wielded.CurrentUsageItem;
			if (usage == null)
			{
				return null;
			}
			ItemObject ammo = usage.IsRangedWeapon && usage.IsConsumable ? wielded.Item : wielded.AmmoWeapon.Item;
			return ammo != null ? SpellRegistry.FindByAmmo(ammo.StringId) : null;
		}

		private void ShowIndicator(SpellDef spell, Vec3 point)
		{
			// 换了法术（指示圈网格不同）→ 重建
			if (_indicator != null && _indicatorSpellId != spell.Id)
			{
				HideIndicator();
			}
			if (_indicator == null)
			{
				_indicator = SpellWorld.SpawnMeshEntity(spell.Indicator, point, SpellMath.BuildGroundRotation(),
					spell.IndicatorScale);
				if (_indicator == null)
				{
					return;   // 网格查不到：日志已由 SpellWorld 记过，静默不画
				}
				_indicatorSpellId = spell.Id;
			}
			else
			{
				// 只挪位置（姿态是"躺在地上"，与位置无关）
				try
				{
					MatrixFrame frame = _indicator.GetGlobalFrame();
					frame.origin = point;
					_indicator.SetGlobalFrame(frame);
				}
				catch (Exception)
				{
					_indicator = null;
					_indicatorSpellId = null;
				}
			}
		}

		private void HideIndicator()
		{
			if (_indicator == null)
			{
				return;
			}
			try
			{
				_indicator.Remove(0);
			}
			catch (Exception)
			{
				// 实体可能已被引擎回收 —— 正常
			}
			_indicator = null;
			_indicatorSpellId = null;
		}

		/// <summary>按引用回收一发（引导法术"松手就停"走这条；找不到 = 它已经自己结束了）。</summary>
		public void RetireShot(SpellShot shot)
		{
			if (shot == null)
			{
				return;
			}
			for (int i = _live.Count - 1; i >= 0; i--)
			{
				if (_live[i] == shot)
				{
					Retire(i);
					return;
				}
			}
		}

		private void Retire(int index)
		{
			SpellShot shot = _live[index];
			_live.RemoveAt(index);
			try
			{
				if (shot.Delivery != null)
				{
					shot.Delivery.End();
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 投送回收异常：{ex.GetType().Name} {ex.Message}");
			}
		}

		// ═══════════════════════ 诊断命令（custom.spell）═══════════════════════

		/* 控制台命令注册纪律：委托签名 = public static string F(List<string>)；
		   首参一律"可弃"——解析不出来回落到"看状态"并注明（工作流约定，2026-09-14）。
		   返回文本一律纯英文（显示在游戏内 ~ 控制台），细节走 DebugLogger（中文）。 */

		[CommandLineFunctionality.CommandLineArgumentFunction("spell", "custom")]
		public static string Spell(List<string> args)
		{
			try
			{
				if (args == null || args.Count == 0)
				{
					return Status();
				}
				switch (args[0].Trim().ToLowerInvariant())
				{
					case "list":
						return List();
					case "cast":
						return Cast(args);
					case "npc":
						return NpcCast(args);
					case "give":
						return Give(args);
					case "power":
						return Power(args);
					case "scale":
						return Scale(args);
					case "hit":
						return Hit(args);
					case "core":
						return Core(args);
					case "tilt":
						return Tilt(args);
					case "verbose":
						return Verbose(args);
					case "cap":
						return Cap(args);
					case "probe":
						return Probe();
					default:
						return Status() + $" [note: '{args[0]}' is not a subcommand -> showing status]";
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 命令异常：{ex.GetType().Name} {ex.Message}");
				return "ERROR: " + ex.GetType().Name + " (see log)";
			}
		}

		private static string Status()
		{
			SpellProjectileLogic host = Mission.Current != null
				? Mission.Current.GetMissionBehavior<SpellProjectileLogic>()
				: null;
			string tilt = SpellDebug.TiltOverrideDeg.HasValue
				? SpellDebug.TiltOverrideDeg.Value.ToString("F1", CultureInfo.InvariantCulture)
				: "data";
			return $"OK: spells={SpellRegistry.Count} families={SpellRegistry.Families.Count}"
				+ $" live={(host != null ? host.LiveCount : 0)}/{SpellDebug.MaxLive}"
				+ $" verbose={(SpellDebug.Verbose ? "on" : "off")} tilt={tilt}";
		}

		private static string List()
		{
			if (SpellRegistry.Count == 0)
			{
				return "OK: spell table is empty (no content pack provides AssetRegistry/Spells.xml)";
			}
			var sb = new System.Text.StringBuilder("OK:");
			foreach (SpellDef def in SpellRegistry.All)
			{
				sb.Append($" [{def.Id} ammo={def.AmmoId} {def.Targeting}/{def.Delivery}/{string.Join("+", def.Payloads)}");
				sb.Append($" dmg={def.Damage:F0} r={def.Radius:F1} v={def.Speed:F0} life={def.MaxLifetime:F1}s]");
			}
			return sb.ToString();
		}

		private static string Cast(List<string> args)
		{
			if (args.Count < 2)
			{
				return "USAGE: custom.spell cast <spellId>";
			}
			Agent player = Agent.Main;
			if (player == null)
			{
				return "ERROR: no main agent in this scene.";
			}
			Vec3 origin = player.Position;
			origin.z += player.GetEyeGlobalHeight();
			SpellDef spell = SpellRegistry.FindById(args[1].Trim());
			if (spell == null)
			{
				return $"FAILED: '{args[1]}' is not a spell id (see 'custom.spell list')";
			}
			float power = SpellDebug.PowerOverride ?? 1f;
			bool ok = SpellCastFlow.Cast(player, spell, origin, player.LookDirection, power);
			return ok
				? $"OK: cast '{spell.Id}' power={power:F2}"
				: $"FAILED: '{spell.Id}' did not start (see log)";
		}

		/// <summary>
		/// 🔴 **NPC 施法验证**：让最近的那个人（不是玩家）朝玩家放一发。
		/// 证的是**铁律 18 那条**：<see cref="SpellCastFlow"/> 不关心施法者是谁 —— 判定、飞行、
		/// 命中、结算与玩家走的是同一套代码（阶段 4 只是把"谁在什么时候调它"换成脑驱动）。
		/// ⚠️ 阶段 1 的 NPC 施法**只有法术本身飞出去**：动作/姿势要靠它自己装备法印（走引擎的弩流程），
		///   或者等阶段 3 的相位机（读输入 → 那套对 NPC 换成读脑计划）。本命令是验证钩子，不是玩法。
		/// </summary>
		private static string NpcCast(List<string> args)
		{
			Mission mission = Mission.Current;
			Agent player = Agent.Main;
			if (mission == null || player == null)
			{
				return "ERROR: no mission / main agent.";
			}
			SpellDef spell = null;
			string note = "";
			if (args.Count >= 2)
			{
				spell = SpellRegistry.FindById(args[1].Trim());
				if (spell == null)
				{
					// 首参可弃（工作流约定）：给了个解析不出来的，回落到表里第一个法术并注明
					note = $" [note: '{args[1]}' is not a spell id -> using the first one]";
				}
			}
			if (spell == null)
			{
				foreach (SpellDef def in SpellRegistry.All)
				{
					spell = def;
					break;
				}
			}
			if (spell == null)
			{
				return "FAILED: the spell table is empty (no content pack provides AssetRegistry/Spells.xml).";
			}

			Agent npc = null;
			float bestSq = float.MaxValue;
			foreach (Agent agent in mission.Agents)
			{
				if (agent == null || agent == player || !agent.IsActive() || agent.Health <= 0f)
				{
					continue;
				}
				float d = agent.Position.DistanceSquared(player.Position);
				if (d < bestSq && d < 1600f)
				{
					bestSq = d;
					npc = agent;
				}
			}
			if (npc == null)
			{
				return "FAILED: no other agent within 40m - get closer to someone.";
			}

			Vec3 origin = npc.Position;
			origin.z += npc.GetEyeGlobalHeight();
			Vec3 aim = player.Position;
			aim.z += player.GetEyeGlobalHeight() * 0.5f;
			Vec3 direction = aim - origin;
			direction = direction.LengthSquared < 1e-6f ? npc.LookDirection : direction.NormalizedCopy();
			bool ok = SpellCastFlow.Cast(npc, spell, origin, direction);
			return ok
				? $"OK: NPC '{npc.Name}' cast '{spell.Id}'" + note
				: $"FAILED: NPC '{npc.Name}' cast did not start (see log)" + note;
		}

		private static string Tilt(List<string> args)
		{
			if (args.Count < 2)
			{
				return "USAGE: custom.spell tilt <degrees>|off";
			}
			string raw = args[1].Trim();
			if (raw.Equals("off", StringComparison.OrdinalIgnoreCase))
			{
				SpellDebug.TiltOverrideDeg = null;
				return "OK: tilt override cleared (using per-spell tilt_deg)";
			}
			float deg;
			if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out deg))
			{
				return $"ERROR: '{raw}' is not a number.";
			}
			SpellDebug.TiltOverrideDeg = deg;
			return $"OK: tilt override = {deg:F1} deg (fire a spell with 'cast' to see it)";
		}

		private static string Verbose(List<string> args)
		{
			if (args.Count < 2)
			{
				SpellDebug.Verbose = !SpellDebug.Verbose;
			}
			else
			{
				SpellDebug.Verbose = args[1].Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
			}
			return $"OK: verbose = {(SpellDebug.Verbose ? "on" : "off")}";
		}

		private static string Cap(List<string> args)
		{
			if (args.Count < 2)
			{
				return $"OK: cap = {SpellDebug.MaxLive}";
			}
			int n;
			if (!int.TryParse(args[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n <= 0)
			{
				return $"ERROR: '{args[1]}' is not a positive integer.";
			}
			SpellDebug.MaxLive = n;
			return $"OK: cap = {n}";
		}

		/// <summary>
		/// 🔴 三条实测定性 —— 计划 §4.3 那三个"必须实测、不能只看名字下结论"的点，一次跑完：
		///   ① `rayThickness` 是「沿线段扫球」还是「只在起点一个球」？
		///      → 拿最近的一个人，把射线**横向平移**开不同距离再打，记录到多远还能命中。
		///        能"擦到 1.2 米外的人" = 真的是粗射线（连续碰撞成立）。
		///   ② `RayCastForClosestAgent` 只返回最近的一个 → 穿透要靠调用方循环（代码里已经这么做）。
		///   ③ `Scene.RayCastForClosestEntityOrTerrain` 打不打 agent？
		///      → 同一段线段再喂给场景查询：它若在"人的距离"上报命中 = 打 agent；否则两条查询都得留着。
		/// 返回一行英文摘要，细节全在日志里。
		/// </summary>
		private static string Probe()
		{
			Mission mission = Mission.Current;
			Agent player = Agent.Main;
			if (mission == null || mission.Scene == null || player == null)
			{
				return "ERROR: no mission/scene/main agent.";
			}
			Agent target = null;
			float bestSq = float.MaxValue;
			foreach (Agent agent in mission.Agents)
			{
				if (agent == null || agent == player || !agent.IsActive() || agent.Health <= 0f)
				{
					continue;
				}
				float d = agent.Position.DistanceSquared(player.Position);
				if (d < bestSq && d < 3600f)
				{
					bestSq = d;
					target = agent;
				}
			}
			if (target == null)
			{
				return "FAILED: no other agent within 60m - get closer to someone.";
			}

			Vec3 eye = player.Position;
			eye.z += player.GetEyeGlobalHeight();
			Vec3 center = target.Position;
			center.z += target.GetEyeGlobalHeight() * 0.5f;
			Vec3 line = center - eye;
			float length = line.Length;
			if (length < 0.01f)
			{
				return "FAILED: target too close.";
			}
			Vec3 forward = line * (1f / length);
			Vec3 side = Vec3.CrossProduct(forward, Vec3.Up);
			if (side.LengthSquared < 1e-6f)
			{
				side = Vec3.CrossProduct(forward, Vec3.Forward);
			}
			side = side.NormalizedCopy();

			DebugLogger.Log($"[SpellProbe] 目标 {target.Name}(Idx={target.Index}) 距离 {length:F2}m "
				+ $"（引擎碰撞胶囊半径 {SafeCapsuleRadius(target):F2}m）· 法术默认命中半径 1.2m");
			// ① 中心线：应该必中
			float direct = RayDistance(mission, eye, center, 1.2f, player.Index);
			DebugLogger.Log($"[SpellProbe] ① 中心线（厚度 1.2）→ {(direct >= 0f ? $"{direct:F2}m 命中" : "未命中")}"
				+ "（预期命中；未命中 = 这条查询根本不可用）");
			// ② 横向平移：看多粗才算擦到
			string offsets = "";
			float maxOffset = 0f;
			for (float off = 0.5f; off <= 3.01f; off += 0.5f)
			{
				Vec3 from = eye + side * off;
				Vec3 to = center + side * off;
				float d = RayDistance(mission, from, to, 1.2f, player.Index);
				bool hit = d >= 0f;
				if (hit)
				{
					maxOffset = off;
				}
				offsets += $" {off:F1}:{(hit ? "HIT" : "-")}";
			}
			DebugLogger.Log($"[SpellProbe] ② 横向平移（射线平行于中心线，厚度固定 1.2）:{offsets}"
				+ $" ⇒ 最远仍命中 {maxOffset:F1}m —— 约等于射线粗半径"
				+ "（远大于 1.2 = 它比参数更粗；只有 0.5~1.0 = 是「沿线段扫球」，连续碰撞成立；"
				+ "全部未命中 = 厚度参数没生效）");
			// ③ 场景查询打不打 agent
			bool sceneHit = false;
			float sceneDist = -1f;
			try
			{
				Vec3 point;
				sceneHit = mission.Scene.RayCastForClosestEntityOrTerrain(eye, center, out sceneDist, out point,
					0.01f, BodyFlags.CommonCollisionExcludeFlagsForMissile);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[SpellProbe] ③ 场景查询异常：{ex.GetType().Name} {ex.Message}");
			}
			DebugLogger.Log($"[SpellProbe] ③ 场景查询（同一条中心线）→ "
				+ (sceneHit ? $"命中于 {sceneDist:F2}m" : "未命中")
				+ $"（人的距离 {length:F2}m）⇒ "
				+ (sceneHit && sceneDist < length - 0.5f ? "**打 agent**（可以两条查询合一）"
					: sceneHit ? "打到的是人后面/面前的东西，**不打 agent**（两条查询都要留）"
					: "**不打 agent**（两条查询都要留）"));

			return $"OK: probe done (offset hits max={maxOffset:F1}m) - details in log";
		}

		private static float RayDistance(Mission mission, Vec3 from, Vec3 to, float thickness, int excludeIndex)
		{
			try
			{
				float distance;
				Agent hit = V.RayCastForClosestAgent(mission, from, to, excludeIndex, thickness, out distance);
				return hit != null ? distance : -1f;
			}
			catch (Exception)
			{
				return -1f;
			}
		}

		/// <summary>
		/// 🔴 **阶段 4 的验证钩子**：给最近的那个 NPC 装一套"法印 + 法术弹"，
		/// 它下一拍（<see cref="SpellNpcCaster"/> 每秒扫一次）就会自己找敌人放法术 ——
		/// 判定、飞行、命中、结算与玩家**同一套代码**，差别只有触发源（铁律 18）。
		/// 不带参数 = 按 `ai_weight` **带权随机**抽一个允许 NPC 用的法术。
		/// </summary>
		private static string Give(List<string> args)
		{
			Mission mission = Mission.Current;
			Agent player = mission != null ? mission.MainAgent : null;
			if (mission == null || player == null)
			{
				return "ERROR: no mission / main agent.";
			}
			SpellDef spell = null;
			string note = "";
			if (args.Count >= 2)
			{
				spell = SpellRegistry.FindById(args[1].Trim());
				if (spell == null)
				{
					note = $" [note: '{args[1]}' is not a spell id -> weighted random pick]";
				}
			}
			if (spell == null)
			{
				spell = SpellNpcCaster.PickWeightedForAi();
			}
			if (spell == null)
			{
				return "FAILED: no AI-usable spell (needs seal= in the data + a family NPCs may use)." + note;
			}
			Agent npc = FindNearestOtherAgent(mission, player, 40f);
			if (npc == null)
			{
				return "FAILED: no other agent within 40m - get closer to someone." + note;
			}
			string error = SpellNpcCaster.EquipFor(npc, spell);
			if (!string.IsNullOrEmpty(error))
			{
				return "FAILED: " + error + note;
			}
			return $"OK: gave '{spell.Id}' (seal + ammo) to '{npc.Name}' - it should cast on its own within a few seconds" + note;
		}

		/// <summary>试探"蓄力档位"：设一个力度覆盖值，下一次 <c>custom.spell cast</c> 用它（0~1；off = 还原）。</summary>
		private static string Power(List<string> args)
		{
			if (args.Count < 2)
			{
				return $"OK: power override = {(SpellDebug.PowerOverride.HasValue ? SpellDebug.PowerOverride.Value.ToString("F2", CultureInfo.InvariantCulture) : "off")}";
			}
			string raw = args[1].Trim();
			if (raw.Equals("off", StringComparison.OrdinalIgnoreCase))
			{
				SpellDebug.PowerOverride = null;
				return "OK: power override cleared (1.0)";
			}
			float value;
			if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
			{
				return $"ERROR: '{raw}' is not a number.";
			}
			SpellDebug.PowerOverride = MathF.Max(0f, MathF.Min(1f, value));
			return $"OK: power override = {SpellDebug.PowerOverride.Value:F2} (used by 'cast')";
		}

		/// <summary>放大倍率覆盖（0.1~20 钳制；off = 还原成数据里的 scale）。</summary>
		private static string Scale(List<string> args)
		{
			if (args.Count < 2)
			{
				return $"OK: scale override = {(SpellDebug.ScaleOverride.HasValue ? SpellDebug.ScaleOverride.Value.ToString("F2", CultureInfo.InvariantCulture) : "off")}";
			}
			string raw = args[1].Trim();
			if (raw.Equals("off", StringComparison.OrdinalIgnoreCase))
			{
				SpellDebug.ScaleOverride = null;
				return "OK: scale override cleared (using per-spell scale)";
			}
			float value;
			if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
			{
				return $"ERROR: '{raw}' is not a number.";
			}
			SpellDebug.ScaleOverride = MathF.Max(0.1f, MathF.Min(20f, value));
			return $"OK: scale override = {SpellDebug.ScaleOverride.Value:F2}x (next 'cast' uses it)";
		}

		/// <summary>命中半径覆盖（米；0.1~10 钳制；off = 还原成数据里的 hit_radius）。</summary>
		private static string Hit(List<string> args)
		{
			if (args.Count < 2)
			{
				return $"OK: hit radius override = {(SpellDebug.HitRadiusOverride.HasValue ? SpellDebug.HitRadiusOverride.Value.ToString("F2", CultureInfo.InvariantCulture) : "off")}";
			}
			string raw = args[1].Trim();
			if (raw.Equals("off", StringComparison.OrdinalIgnoreCase))
			{
				SpellDebug.HitRadiusOverride = null;
				return "OK: hit radius override cleared (using per-spell hit_radius)";
			}
			float value;
			if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
			{
				return $"ERROR: '{raw}' is not a number.";
			}
			SpellDebug.HitRadiusOverride = MathF.Max(0.1f, MathF.Min(10f, value));
			return $"OK: hit radius override = {SpellDebug.HitRadiusOverride.Value:F2}m (next 'cast' uses it)";
		}

		/// <summary>蓄力核大小覆盖（0.02~5 钳制；off = 还原成数据里的 charge_scale）。</summary>
		private static string Core(List<string> args)
		{
			if (args.Count < 2)
			{
				return $"OK: charge core scale override = {(SpellDebug.ChargeScaleOverride.HasValue ? SpellDebug.ChargeScaleOverride.Value.ToString("F2", CultureInfo.InvariantCulture) : "off")}";
			}
			string raw = args[1].Trim();
			if (raw.Equals("off", StringComparison.OrdinalIgnoreCase))
			{
				SpellDebug.ChargeScaleOverride = null;
				return "OK: charge core override cleared (using per-spell charge_scale)";
			}
			float value;
			if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
			{
				return $"ERROR: '{raw}' is not a number.";
			}
			SpellDebug.ChargeScaleOverride = MathF.Max(0.02f, MathF.Min(5f, value));
			return $"OK: charge core override = {SpellDebug.ChargeScaleOverride.Value:F2}x (hold the cast key to see it)";
		}

		/// <summary>最近的另一个 agent（诊断命令共用）。</summary>
		private static Agent FindNearestOtherAgent(Mission mission, Agent self, float maxDistance)
		{
			Agent best = null;
			float bestSq = maxDistance * maxDistance;
			foreach (Agent agent in mission.Agents)
			{
				if (agent == null || agent == self || !agent.IsActive() || agent.Health <= 0f)
				{
					continue;
				}
				float d = agent.Position.DistanceSquared(self.Position);
				if (d < bestSq)
				{
					bestSq = d;
					best = agent;
				}
			}
			return best;
		}

		private static float SafeCapsuleRadius(Agent agent)
		{
			try
			{
				return agent.CollisionCapsule.Radius;
			}
			catch (Exception)
			{
				return -1f;
			}
		}
	}
}
