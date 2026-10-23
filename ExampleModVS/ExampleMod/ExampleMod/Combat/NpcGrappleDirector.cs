using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **NPC 钩索总管 + 自主 AI**（2026-10-23 立；方案 = `plans/钩索-实施计划.md` §二十五）。
	///
	/// 每帧：
	///   · 推进所有 NPC 那套钩索（<see cref="GrappleLogic.TickAllNpcSessions"/>）；
	///   · 按 <see cref="ScanInterval"/> 扫一次：NPC **实际带着绳+钩**、活着、不在别的交互里、冷却结束，
	///     就按两种用途之一出手：
	///       - **绑人**：敌对目标在 <see cref="BindRange"/> 内 + 视线通过 → 钩人；
	///       - **钩墙**：移动输入顶着障碍 / 目标在上方 → 找墙上的钩点 + 落点解算成功 → 拉自己。
	///
	/// 🔴 任何场景都跑（用户 2026-10-23 拍板），但只认"实际带着两件"的 NPC（同玩家的物品门槛）。
	/// 🔴 玩家独占的东西（相机 / UI / 弹药栏）一概不碰。
	/// 🔴 上限 / 冷却 / 距离都是**性能与公平**的闸门，不是"NPC 不能做"。
	/// </summary>
	public class NpcGrappleDirector : MissionLogic
	{
		/// <summary>当前场景的总管（命令要发"走过去把他扛起来"这类请求时用）。</summary>
		public static NpcGrappleDirector Instance { get; private set; }

		// ── 总开关与节奏（命令可调；默认值也是"能看出效果但不刷屏"）──
		public static bool Enabled = true;
		public static bool BindEnabled = true;
		public static bool WallPullEnabled = true;
		public static bool Verbose = false;

		public static float ScanInterval = 0.7f;
		public static float ScanRadius = 50f;
		public static float BindRange = 20f;
		public static float WallRange = 20f;

		public static float BindCooldown = 25f;
		public static float WallCooldown = 15f;
		public static float GlobalCooldown = 2f;

		public static int MaxSessions = 3;
		public static int MaxBindings = 2;
		public static int MaxPulls = 2;

		/// <summary>离玩家超过这个距离的 NPC 钩索直接终止（省实体 / 省逻辑）。</summary>
		public static float MaxDistance = 120f;

		/// <summary>AI 绑人的目标默认多久自动放开（0 = 永不；防止把玩家永久锁死）。</summary>
		public static float BindHoldSeconds = 8f;

		private static readonly MBList<Agent> _nearby = new MBList<Agent>();
		private static readonly MBList<Agent> _enemies = new MBList<Agent>();

		private readonly Dictionary<Agent, float> _nextBind = new Dictionary<Agent, float>();
		private readonly Dictionary<Agent, float> _nextWall = new Dictionary<Agent, float>();
		private float _scanTimer;
		private float _lastDecisionTime = -999f;

		// 「走过去把这根绳绑着的人扛起来」请求（命令 `npc carry` 用）
		private Agent _carryCarrier;
		private float _carryDeadline;
		private float _carryReissue;

		public override void OnCreated()
		{
			base.OnCreated();
			Instance = this;
		}

		public override void OnBehaviorInitialize()
		{
			base.OnBehaviorInitialize();
			Instance = this;
		}

		/// <summary>请求：让这个 NPC 走到自己绳上那个人身边，到 3 m 内自动扛起。</summary>
		public void RequestCarry(Agent carrier)
		{
			_carryCarrier = carrier;
			_carryDeadline = MBCommon.GetTotalMissionTime() + 8f;
			_carryReissue = 0f;
		}

		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);

			// NPC 那套的机制每帧都要走（与 AI 开关无关）
			GrappleLogic.TickAllNpcSessions(dt);
			TickCarryRequest(dt);

			if (!Enabled) return;
			_scanTimer += dt;
			if (_scanTimer < ScanInterval) return;
			_scanTimer = 0f;

			try { Scan(); }
			catch (Exception ex)
			{
				DebugLogger.Log($"[GrappleAI] 扫描异常：{ex.GetType().Name} {ex.Message}");
			}
		}

		protected override void OnEndMission()
		{
			base.OnEndMission();
			try { GrappleLogic.TerminateAllNpcSessions("mission end"); } catch (Exception) { }
			_nextBind.Clear();
			_nextWall.Clear();
			_scanTimer = 0f;
			_lastDecisionTime = -999f;
			_carryCarrier = null;
			if (ReferenceEquals(Instance, this)) Instance = null;
		}

		/// <summary>
		/// 推进 `npc carry` 的"先走过去"：每 0.4 s 重发移动指令，到
		/// <see cref="GrappleCarry.MaxLiftDistance"/> 内就调 `CarryPickUp`；8 秒不到就放弃。
		/// 扛起来之后的移动归命令层（`npc walkto`），这里不再管。
		/// </summary>
		private void TickCarryRequest(float dt)
		{
			if (_carryCarrier == null) return;
			Agent c = _carryCarrier;
			if (!AgentControlHelper.SafeIsActive(c))
			{
				_carryCarrier = null;
				return;
			}

			GrappleLogic session = GrappleLogic.GetSession(c);
			Agent carried = session != null ? session.BoundTarget : null;
			if (session == null || carried == null)
			{
				DebugLogger.Log($"[GrappleAI] 扛人请求作废：'{SafeName(c)}' 的绳上已经没人了");
				_carryCarrier = null;
				return;
			}

			float d = 0f;
			try { d = c.Position.Distance(carried.Position); } catch (Exception) { }
			if (d <= GrappleCarry.MaxLiftDistance)
			{
				try { AgentControlHelper.StopMovement(c); } catch (Exception) { }
				if (session.CarryPickUp(out string why, c))
				{
					DebugLogger.Log($"[GrappleAI] 扛人：'{SafeName(c)}' 走到 {d:F2} m ⇒ 扛起 '{SafeName(carried)}'");
				}
				else
				{
					DebugLogger.Log($"[GrappleAI] 扛人失败（{d:F2} m）：{why}");
				}
				_carryCarrier = null;
				return;
			}

			if (MBCommon.GetTotalMissionTime() >= _carryDeadline)
			{
				try { AgentControlHelper.StopMovement(c); } catch (Exception) { }
				DebugLogger.Log($"[GrappleAI] 扛人请求超时：'{SafeName(c)}' 还没走到 '{SafeName(carried)}'（{d:F1} m）");
				_carryCarrier = null;
				return;
			}

			_carryReissue -= dt;
			if (_carryReissue <= 0f)
			{
				_carryReissue = 0.4f;
				try { AgentControlHelper.ScriptedMoveToAgent(c, carried, false); } catch (Exception) { }
			}
		}

		private static string SafeName(Agent a)
		{
			try { return a != null && a.Name != null ? a.Name.ToString() : "?"; }
			catch (Exception) { return "?"; }
		}

		// ─────────────────────────── 扫描 / 决策 ───────────────────────────

		private void Scan()
		{
			Mission mission = Mission.Current;
			Agent player = mission != null ? mission.MainAgent : null;
			if (mission == null || player == null) return;

			float now = MBCommon.GetTotalMissionTime();

			// ① 清理：主人没了 / 离玩家太远 / 已经空转的那套
			int active = 0, bindings = 0, pulls = 0;
			List<GrappleLogic> sessions = GrappleLogic.GetNpcSessions();
			for (int i = 0; i < sessions.Count; i++)
			{
				GrappleLogic s = sessions[i];
				Agent o = s.Owner;
				if (o == null || !AgentControlHelper.SafeIsActive(o))
				{
					s.TerminateSession("owner gone");
					continue;
				}
				if (o.Position.Distance(player.Position) > MaxDistance)
				{
					s.TerminateSession("too far from player");
					continue;
				}
				if (!s.IsSessionBusy)
				{
					s.TerminateSession("idle");
					continue;
				}
				active++;
				if (s.HasBoundTarget) bindings++;
				if (s.IsPullingSelf) pulls++;
			}

			CleanupCooldowns(_nextBind, now);
			CleanupCooldowns(_nextWall, now);

			if (now - _lastDecisionTime < GlobalCooldown) return;
			if (active >= MaxSessions) return;

			// ② 只扫玩家附近的 NPC（不遍历全场）
			_nearby.Clear();
			mission.GetNearbyAgents(new Vec2(player.Position.x, player.Position.y), ScanRadius, _nearby);
			for (int i = 0; i < _nearby.Count; i++)
			{
				Agent npc = _nearby[i];
				if (!Eligible(mission, npc, player)) continue;

				// 先绑人（更直接），再钩墙
				if (BindEnabled && bindings < MaxBindings && OffCooldown(_nextBind, npc, now))
				{
					Agent target = FindBindTarget(mission, npc);
					if (target != null && StartBind(npc, target, now)) return;
				}

				if (WallPullEnabled && pulls < MaxPulls && OffCooldown(_nextWall, npc, now))
				{
					if (TryFindWallPoint(mission, npc, out Vec3 point) && StartWallPull(npc, point, now)) return;
				}
			}
		}

		private static bool Eligible(Mission mission, Agent a, Agent player)
		{
			if (a == null || ReferenceEquals(a, player)) return false;
			if (!AgentControlHelper.SafeIsActive(a) || a.Health <= 0f) return false;
			if (a.IsMount || !a.IsHuman || a.Character == null) return false;
			try { if (!V.IsAgentAI(a)) return false; } catch (Exception) { }
			try
			{
				string monster = a.Monster?.StringId;
				if (monster != null && monster.IndexOf("child", StringComparison.OrdinalIgnoreCase) >= 0) return false;
			}
			catch (Exception) { }
			if (a.IsUsingGameObject) return false;
			if (GrappleLogic.IsAgentInGrapple(a) || GrappleBind.IsTargetClaimed(a)) return false;
			try { if (PairActionLogic.Current != null && PairActionLogic.Current.IsBusy(a)) return false; } catch (Exception) { }
			try
			{
				if ((a.GetScriptedFlags() & Agent.AIScriptedFrameFlags.InConversation) != 0) return false;
			}
			catch (Exception) { }
			// 物品门槛（同玩家）：没带绳+钩的 NPC 不用
			if (!GrappleFirePatch.HasRopeEquipped(a) || !GrappleFirePatch.HasHookEquipped(a)) return false;
			return true;
		}

		/// <summary>
		/// **找最近的屋顶/高处**（2026-10-23 用户要的命令：`custom.grapple npc pull roof`）。
		/// 围绕 NPC 扫 8 个方向 × 4 个仰角，取"落点明显高于自己、水平最近"的那一个钩点。
		/// </summary>
		public static bool TryFindRoofPoint(Agent npc, out Vec3 point)
		{
			point = default(Vec3);
			Mission mission = Mission.Current;
			Scene scene = mission != null ? mission.Scene : null;
			if (npc == null || scene == null) return false;

			float eye = MathF.Max(1.2f, npc.GetEyeGlobalHeight());
			Vec3 eyePos = npc.Position + Vec3.Up * eye;
			float[] pitches = { 5f, 20f, 35f, 50f };
			float bestDist = float.MaxValue;
			bool found = false;

			for (int a = 0; a < 8; a++)
			{
				float ang = a * MathF.PI / 4f;
				Vec3 flat = new Vec3(MathF.Cos(ang), MathF.Sin(ang), 0f);
				for (int i = 0; i < pitches.Length; i++)
				{
					float rad = pitches[i] * MathF.PI / 180f;
					Vec3 dir = flat * MathF.Cos(rad) + Vec3.Up * MathF.Sin(rad);
					Vec3 to = eyePos + dir * WallRange;
					Agent victim = null;
					Vec3 hitPoint = Vec3.Zero;
					Vec3 normal = Vec3.Up;
					bool hit = false;
					try
					{
						hit = SpellSweep.FindNearestHit(mission, eyePos, to, 0.2f, npc.Index, npc.Team, null, null,
							out victim, out hitPoint, out normal);
					}
					catch (Exception) { }
					if (!hit || victim != null) continue;

					GrappleLanding.Result landing = GrappleLanding.Solve(scene, hitPoint, npc.Position, normal);
					if (!landing.Found) continue;
					if (landing.Endpoint.z - npc.Position.z < 1.2f) continue;   // 必须明显更高（屋顶/高处）
					float horiz = new Vec2(landing.Endpoint.x - npc.Position.x,
						landing.Endpoint.y - npc.Position.y).Length;
					if (horiz > WallRange) continue;
					if (horiz < bestDist)
					{
						bestDist = horiz;
						point = hitPoint;
						found = true;
					}
				}
			}
			return found;
		}

		/// <summary>找一个可绑的敌对目标：视线通过、没被别人绑、没在忙自己的钩索。</summary>
		private static Agent FindBindTarget(Mission mission, Agent npc)
		{
			if (npc.Team == null) return null;
			try
			{
				_enemies.Clear();
				mission.GetNearbyEnemyAgents(new Vec2(npc.Position.x, npc.Position.y), BindRange, npc.Team, _enemies);
				Agent best = null;
				float bestD = BindRange;
				for (int i = 0; i < _enemies.Count; i++)
				{
					Agent t = _enemies[i];
					if (t == null || !AgentControlHelper.SafeIsActive(t) || t.Health <= 0f || t.IsMount) continue;
					try { if (!ReferenceEquals(t, Agent.Main) && !V.IsAgentAI(t)) continue; } catch (Exception) { }
					if (GrappleBind.IsTargetClaimed(t) || GrappleLogic.IsAgentInGrapple(t)) continue;
					string monster = null;
					try { monster = t.Monster?.StringId; } catch (Exception) { }
					if (monster != null && monster.IndexOf("child", StringComparison.OrdinalIgnoreCase) >= 0) continue;
					if (!NpcSightSystem.CanAgentSeeTarget(npc, t, BindRange, 180f)) continue;
					float d = t.Position.Distance(npc.Position);
					if (d < bestD) { bestD = d; best = t; }
				}
				return best;
			}
			catch (Exception) { return null; }
		}

		private bool StartBind(Agent npc, Agent target, float now)
		{
			GrappleLogic session = GrappleLogic.EnsureFor(npc);
			if (session == null) return false;
			Vec3 aim = GrappleBind.ChestOf(target);
			if (!session.BeginNpcThrow(aim, aimAgent: true, autoPull: false, out string why, BindHoldSeconds, target))
			{
				Log($"'{npc.Name}' 绑人起手失败：{why}", true);
				_nextBind[npc] = now + 5f;
				return false;
			}
			_nextBind[npc] = now + BindCooldown;
			float d = npc.Position.Distance(target.Position);
			string approach = d > GrappleNpcKit.ApproachDistance ? $"（先走近到 {GrappleNpcKit.ApproachDistance:F0}m 内再出手）" : "";
			Log($"'{npc.Name}' → 绑 '{target.Name}'（{d:F1}m{approach}，{BindHoldSeconds:F0}s 后自动放）", false);
			return true;
		}

		/// <summary>钩墙：先判断"值不值得"（顶着障碍 / 目标在上方），再找钩点 + 落点解算。</summary>
		private static bool TryFindWallPoint(Mission mission, Agent npc, out Vec3 point)
		{
			point = default(Vec3);
			Scene scene = mission.Scene;
			if (scene == null) return false;

			// 方向：优先敌人方向（目标在上方 / 被隔断），否则移动输入，最后看朝向
			Agent enemy = null;
			try { enemy = FindNearestEnemyForWall(mission, npc); } catch (Exception) { }
			Vec3 flatDir;
			if (enemy != null)
			{
				Vec3 d = enemy.Position - npc.Position;
				d.z = 0f;
				flatDir = d.LengthSquared > 1e-4f ? d.NormalizedCopy() : npc.LookDirection;
			}
			else
			{
				Vec2 input = npc.MovementInputVector;
				flatDir = input.LengthSquared > 1e-4f
					? new Vec3(input.x, input.y, 0f).NormalizedCopy()
					: npc.LookDirection;
			}
			flatDir.z = 0f;
			if (flatDir.LengthSquared < 1e-4f) return false;
			flatDir = flatDir.NormalizedCopy();

			bool wants = false;
			if (enemy != null && enemy.Position.z - npc.Position.z > 1.5f) wants = true;
			if (!wants)
			{
				// 移动输入顶着障碍（3 米内）⇒ 值得爬
				Vec3 from = npc.Position + Vec3.Up * 1.0f;
				Vec3 to = from + flatDir * 3f;
				try
				{
					if (scene.RayCastForClosestEntityOrTerrain(from, to, out float _d, out Vec3 _p, 0.01f,
						BodyFlags.CommonCollisionExcludeFlagsForMissile))
					{
						wants = true;
					}
				}
				catch (Exception) { }
			}
			if (!wants) return false;

			float eye = MathF.Max(1.2f, npc.GetEyeGlobalHeight());
			Vec3 eyePos = npc.Position + Vec3.Up * eye;
			float[] pitches = { 0f, 20f, 40f };
			for (int i = 0; i < pitches.Length; i++)
			{
				float rad = pitches[i] * MathF.PI / 180f;
				Vec3 dir = flatDir * MathF.Cos(rad) + Vec3.Up * MathF.Sin(rad);
				Vec3 to = eyePos + dir * WallRange;
				Agent victim = null;
				Vec3 hitPoint = Vec3.Zero;
				Vec3 normal = Vec3.Up;
				bool hit = false;
				try
				{
					hit = SpellSweep.FindNearestHit(mission, eyePos, to, 0.2f, npc.Index, npc.Team, null, null,
						out victim, out hitPoint, out normal);
				}
				catch (Exception) { }
				if (!hit || victim != null) continue;

				GrappleLanding.Result landing = GrappleLanding.Solve(scene, hitPoint, npc.Position, normal);
				if (!landing.Found) continue;
				Vec3 d2 = landing.Endpoint - npc.Position;
				float horizontal = new Vec2(d2.x, d2.y).Length;
				if (d2.Length < 3f) continue;
				if (d2.z < 1.0f && horizontal < 3f) continue;
				point = hitPoint;
				return true;
			}
			return false;
		}

		private static Agent FindNearestEnemyForWall(Mission mission, Agent npc)
		{
			if (npc.Team == null) return null;
			_enemies.Clear();
			mission.GetNearbyEnemyAgents(new Vec2(npc.Position.x, npc.Position.y), ScanRadius, npc.Team, _enemies);
			Agent best = null;
			float bestD = ScanRadius;
			for (int i = 0; i < _enemies.Count; i++)
			{
				Agent t = _enemies[i];
				if (t == null || !AgentControlHelper.SafeIsActive(t) || t.Health <= 0f || t.IsMount) continue;
				float d = t.Position.Distance(npc.Position);
				if (d < bestD) { bestD = d; best = t; }
			}
			return best;
		}

		private bool StartWallPull(Agent npc, Vec3 point, float now)
		{
			// 马上拉不动（引擎硬约束）；先不做"自动下马再爬"，等后续。
			if (npc.MountAgent != null)
			{
				Log($"'{npc.Name}' 骑着马 ⇒ 跳过钩墙（先不做马上自动下马）", true);
				return false;
			}
			GrappleLogic session = GrappleLogic.EnsureFor(npc);
			if (session == null) return false;
			if (!session.BeginNpcThrow(point, aimAgent: false, autoPull: true, out string why))
			{
				Log($"'{npc.Name}' 钩墙起手失败：{why}", true);
				_nextWall[npc] = now + 5f;
				return false;
			}
			_nextWall[npc] = now + WallCooldown;
			Log($"'{npc.Name}' → 钩墙拉自己 @ ({point.x:F1},{point.y:F1},{point.z:F1})", false);
			return true;
		}

		private static bool OffCooldown(Dictionary<Agent, float> table, Agent a, float now)
		{
			return !table.TryGetValue(a, out float until) || now >= until;
		}

		private static void CleanupCooldowns(Dictionary<Agent, float> table, float now)
		{
			if (table.Count == 0) return;
			List<Agent> dead = null;
			foreach (KeyValuePair<Agent, float> kv in table)
			{
				if (kv.Key == null || !AgentControlHelper.SafeIsActive(kv.Key) || now > kv.Value + 60f)
				{
					if (dead == null) dead = new List<Agent>();
					dead.Add(kv.Key);
				}
			}
			if (dead == null) return;
			for (int i = 0; i < dead.Count; i++) table.Remove(dead[i]);
		}

		private static void Log(string msg, bool verboseOnly)
		{
			if (verboseOnly && !Verbose) return;
			DebugLogger.Log("[GrappleAI] " + msg);
		}
	}
}
