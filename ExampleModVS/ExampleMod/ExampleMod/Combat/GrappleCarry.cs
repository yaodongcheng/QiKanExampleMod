using System;
using LivingWorldNpcs.Animation;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **步骤 6「扛人」的配对控制器**（2026-10-10 立；方案 = plans\钩索-实施计划.md §十五）。
	///
	/// 管一对 agent：**扛人者**（v1 = 玩家）+ **被扛者**（= 被钩索捆住的那个人）。
	/// 两边各有自己的状态机（扛人者 = `carry.xml`；被扛者 = `bind.xml` 里那三个"被扛"状态），
	/// 本文件负责**把它们对齐**：
	///   ① **同帧起播**（扛起 / 放下各 Force 一次，两个 Agent 同一帧进状态）；
	///   ② **每帧摆位**（被扛者的世界位置与朝向 —— 见 <see cref="AttachCarried"/>）；
	///   ③ 结束与收尾（放下动画播完才松手）。
	///
	/// 🔴 **竖直 0.68 米不在这里**（这是最容易找错地方的一件）：把人托到肩上那一下
	///    **在被扛者自己的动画里**（根骨位置轨 z：站姿基准 0 → 肩上 +0.66，`tpaccli` / `trf_root_travel.py` 实测）。
	///    引擎渲染骨骼时会应用那条轨 ⇒ **我们一个字都不写 Z**（本来也写不进去，见
	///    [Knowledge/骑砍2Agent运动与位置机制.md](../../../Knowledge/骑砍2Agent运动与位置机制.md)）。
	///    同理，被扛者的**前后 / 左右相对位移也在它自己的动画里**（重定向时就是"以扛人者为参考系"做的）
	///    ⇒ 摆位**只做 XZ 贴住 + 朝向对齐，不叠任何偏移**，否则位移叠成双份。
	///
	/// 🔴 **平权（铁律 18）**：两边都是 Agent，本文件里没有"玩家"分支 —— 将来 NPC 扛人直接调
	///    <see cref="Begin"/> 即可（v1 的入口恰好是玩家而已）。
	///
	/// 🔴 **为什么扛人者那一侧不用管腿**：玩家扛着人时**不冻结**，0 号通道上是"抱人"的姿态、
	///    腿由引擎的**移动层**按速度挑 ⇒ 读作"手在抱人、腿照走"（引擎本来的分层方式，
	///    见 [Knowledge/骑砍2动画通道与上下半身分层.md](../../../Knowledge/骑砍2动画通道与上下半身分层.md)）。
	/// </summary>
	internal sealed class GrappleCarry
	{
		public enum CarryPhase { None, Riding, PuttingDown }

		// ───────────────────────────── 可调参数（命令热调；重启复位）─────────────────────────────

		/// <summary>总开关（`custom.grapple carry on|off`）。关 = 交互面板不出现【扛起】、命令直接拒。</summary>
		public static bool Enabled = true;

		/// <summary>起扛时两者最远距离（米）—— 太远够不着。**从脚底量**（同钩索那条先例）。</summary>
		public static float MaxLiftDistance = 3.0f;

		/// <summary>放下动画播完之后、还没等到状态机出机时的兜底超时（秒）。</summary>
		public static float PutdownTimeoutSeconds = 6.0f;

		/// <summary>
		/// 摆位微调（米；**默认全 0**，因为对齐量本来就烘在被扛者的动画里）。
		/// 只在实机看着对不齐时才动：`custom.grapple carry offset &lt;前后&gt; [左右]`。
		/// 正的前后 = 往扛人者**面朝方向**挪；正的左右 = 往扛人者**右手侧**挪。
		/// </summary>
		public static float OffsetForward = 0f;
		public static float OffsetRight = 0f;

		/// <summary>
		/// **扛着的时候要不要把扛人者的 Z 也写给被扛者**（用户 2026-10-10："z 也尝试一下"）。
		///
		/// 引擎的既有事实是 **agent 的 Z 写不进去、会自动贴地**（见
		/// [Knowledge/骑砍2Agent运动与位置机制.md](../../../Knowledge/骑砍2Agent运动与位置机制.md) §2），
		/// 所以这一条**多半是空转** —— 但"多半"不算数，**实测为准**：
		/// 开着它跑一段，看 `[Grapple] 扛人 Z 探针` 那行里"写入 z"与"回读 z"差多少。
		/// · 差值 ≈ 0 ⇒ 引擎收下了（那就能靠它做高低差）；
		/// · 回读跳回地面 ⇒ 引擎按地形重算了（那就死心，竖直全靠动画的根骨位置轨）。
		///
		/// 🔴 竖直姿态**本来**由被扛者自己的动画负责（`beikangqi_*` 的根骨位置轨 +0.68 米，
		/// 实机已确认"人确实离地"）—— 这个开关只是给个额外的杠，不是承重墙。
		/// </summary>
		public static bool WriteZ = true;

		/// <summary>在扛人者的 Z 之上再加多少（米）—— 整体抬高/压低被扛者用；默认 0。</summary>
		public static float ZLift = 0f;

		/// <summary>Z 探针的输出间隔（秒；0 = 关）。</summary>
		public static float ZProbeSeconds = 1.0f;
		private float _zProbeTimer;

		// ───────────────────────────── 运行时状态 ─────────────────────────────

		private Agent _carrier;
		private Agent _carried;
		private GrappleBind _bind;              // 只为了收摊时把"扛人者"从被绑者那侧清掉（别留悬空引用）
		private AgentAnimStateMachine _machine;
		private CarryAnimContext _ctx;
		private CarryPhase _phase = CarryPhase.None;
		private float _t;
		private string _note = "-";

		public bool IsActive => _phase != CarryPhase.None && _carried != null;
		public Agent Carrier => _carrier;
		public Agent Carried => _carried;
		public CarryPhase Phase => _phase;
		public string Note => _note;

		/// <summary>被扛者是不是**这一个**（交互面板 / 命令用）。</summary>
		public bool IsCarrying(Agent a) => IsActive && ReferenceEquals(_carried, a);

		// ───────────────────────────── 起 / 走 / 停 ─────────────────────────────

		/// <summary>
		/// **开始扛**：要求被扛者**正被本钩捆着且是站着的**（"扛起"那条动画的根骨首帧 = 站姿基准，
		/// 从趴着的人身上起扛姿势对不上 —— 趴着时先等他自己起来，或敲 `bind anim standup`）。
		/// </summary>
		public bool Begin(Agent carrier, Agent carried, GrappleBind bind, out string why)
		{
			why = "-";
			if (!Enabled) { why = "carry disabled"; return false; }
			if (carrier == null || carried == null) { why = "no agent"; return false; }
			if (ReferenceEquals(carrier, carried)) { why = "self"; return false; }
			if (!AgentControlHelper.SafeIsActive(carrier)) { why = "carrier inactive"; return false; }
			if (!AgentControlHelper.SafeIsActive(carried)) { why = "carried inactive"; return false; }
			if (IsActive) { why = "already carrying"; return false; }
			if (bind == null || !bind.IsActive || !ReferenceEquals(bind.Target, carried))
			{
				why = "target is not bound by this rope";
				return false;
			}
			if (bind.IsLying)
			{
				why = "target is lying down (stand them up first: bind anim standup)";
				return false;
			}
			float d = 0f;
			try
			{
				Vec3 v = carried.Position - carrier.Position;
				v.z = 0f;
				d = v.Length;
			}
			catch (Exception) { }
			if (d > MaxLiftDistance)
			{
				why = $"too far ({d:F1} m > {MaxLiftDistance:F1} m)";
				return false;
			}

			_carrier = carrier;
			_carried = carried;
			_bind = bind;
			_ctx = new CarryAnimContext();
			_machine = AnimMachineRegistry.Create(CarryAnimMachine.Name, _ctx);
			_phase = CarryPhase.Riding;
			_t = 0f;
			_note = "riding";

			// 🔴 **同帧起播**：扛人者一侧 Force `carry-trigger`（carry.xml：outside → 扛起）；
			//    被扛者一侧 Force 同一个时刻名（bind.xml：站缚 → 被扛起）。两边的状态机各自
			//    从自己的定义里读"这个时刻该进哪个状态" —— **C# 里一个状态名都没有**。
			ForceCarrier(BindAnimConditions.CarryTrigger);
			bind.ForceEvent(BindAnimConditions.CarryTrigger);
			bind.SetCarrier(carrier);

			// 让被扛者知道扛人者是谁 —— 之后每帧只用来读它的速度（被扛行 ⇄ 被扛走的判据）
			AttachCarried();

			DebugLogger.Log($"[Grapple] 扛人：开始（{SafeName(carrier)} 扛 {SafeName(carried)}，"
				+ $"距离 {d:F2} m）—— 两边同帧起播 扛起 / 被扛起");
			return true;
		}

		/// <summary>每帧推进（<see cref="GrappleLogic.OnMissionTick"/> 调；扛着时必然每帧）。</summary>
		public void Tick(float dt, GrappleBind bind)
		{
			if (!IsActive) return;

			// ── 失效守卫：任一方没了 / 换了目标 ⇒ 立刻散伙（**不再摆位**，让人自己倒回地面）──
			if (!AgentControlHelper.SafeIsActive(_carrier) || !AgentControlHelper.SafeIsActive(_carried))
			{
				HardStop("agent gone");
				return;
			}
			if (bind == null || !bind.IsActive || !ReferenceEquals(bind.Target, _carried))
			{
				HardStop("target no longer bound by this rope");
				return;
			}

			_t += dt;

			// ① **每帧摆位**（放下过程中也要摆 —— 否则人会立刻跳回自己脚下那一点）
			AttachCarried();

			// ② 扛人者那台机：填上下文 + 推进
			if (_machine != null && _ctx != null)
			{
				_ctx.Moving = IsCarrierMoving();
				_ctx.AnimRemainFrac = _machine.CurrentRemainFrac;
				_machine.Tick(_carrier, dt);
			}

			// ③ 放下中：扛人者那台机走到机外（放下动画剩 15% 出机，见 carry.xml）⇒ 收摊
			if (_phase == CarryPhase.PuttingDown)
			{
				bool done = _machine == null
							|| _machine.Current == AgentAnimStateMachine.OutsideState;
				if (done)
				{
					HardStop("put down finished");
				}
				else if (_t >= PutdownTimeoutSeconds + 6f)
				{
					DebugLogger.Log($"[Grapple] 扛人：🔴 放下超时（{_t:F1}s，状态={_machine?.Current}）⇒ 强制收摊");
					HardStop("put down timeout");
				}
			}
		}

		/// <summary>
		/// **放下**（玩家主动 / 松绳 / 收摊都走这里）：两边同帧起播"放下 / 被放下"，
		/// **摆位继续**到放下动画演完（<see cref="Tick"/> ③ 负责收尾）。
		/// </summary>
		public bool PutDown(GrappleBind bind, string reason)
		{
			if (!IsActive || _phase != CarryPhase.Riding) return false;
			_phase = CarryPhase.PuttingDown;
			_t = 0f;
			_note = "putting down (" + reason + ")";
			ForceCarrier(BindAnimConditions.PutTrigger);
			bind?.ForceEvent(BindAnimConditions.PutTrigger);
			DebugLogger.Log($"[Grapple] 扛人：放下（{reason}）—— 两边同帧起播 放下 / 被放下"
				+ $"，动画演完再松手（摆位继续）");
			return true;
		}

		/// <summary>立刻散伙（不再摆位、不再管动画）—— 用于 agent 没了之类的异常路径。</summary>
		public void HardStop(string reason)
		{
			if (_carried == null && _carrier == null) return;
			DebugLogger.Log($"[Grapple] 扛人：结束（{reason}）");
			try { _bind?.SetCarrier(null); } catch (Exception) { }   // 别在被绑者那侧留个悬空的扛人者引用
			_carrier = null;
			_carried = null;
			_bind = null;
			_machine = null;
			_ctx = null;
			_phase = CarryPhase.None;
			_t = 0f;
			_note = "stopped (" + reason + ")";
		}

		// ───────────────────────────── 摆位（这一版的核心）─────────────────────────────

		/// <summary>
		/// 🔴 **每帧两件事**：把被扛者摆到扛人者身上 + 朝向对齐。
		///
		/// · **XZ 贴住**：`TeleportToPosition` 只写 X/Y，**Z 由引擎按地形贴地**
		///   （agent 的 Z 写不进去 —— 骑砍底层行为）。
		/// · **竖直 0.68 米 / 前后相对位移 / 左右侧 —— 全在被扛者自己的动画里**（根骨位置轨），
		///   所以这里**不叠任何偏移**（要微调只用 <see cref="OffsetForward"/> / <see cref="OffsetRight"/>，
		///   默认 0）。
		/// · **朝向只认 `SetMovementDirection`**（`LookDirection` 赋值实测转不动）——
		///   照"配对表演"那条已验证先例（`custom.exec_pair`）。
		/// </summary>
		private void AttachCarried()
		{
			try
			{
				Vec3 fwd = ForwardOf(_carrier);
				Vec3 p = _carrier.Position;
				if (OffsetForward != 0f || OffsetRight != 0f)
				{
					Vec3 right = new Vec3(fwd.y, -fwd.x, 0f);
					p += fwd * OffsetForward + right * OffsetRight;   // 只动水平（两个偏移向量的 z 都是 0）
				}
				// 🔴 **Z 也写一份**（用户 2026-10-10 要求"试一下"）：写进去的是扛人者的 Z（＋ ZLift）。
				//    引擎多半会按地形重算把 Z 吃掉 —— 收没收下由下面的探针说话，别靠猜。
				if (WriteZ) p.z = _carrier.Position.z + ZLift;
				_carried.TeleportToPosition(p);
				_carried.SetMovementDirection(fwd.AsVec2);

				// Z 探针（限频）：`写入 z` vs `回读 z` 差多少 —— 这是"Z 到底写不写得进去"的唯一判据。
				if (ZProbeSeconds > 0f)
				{
					_zProbeTimer += 1f / 60f;
					if (_zProbeTimer >= ZProbeSeconds)
					{
						_zProbeTimer = 0f;
						float back = 0f;
						try { back = _carried.Position.z; } catch (Exception) { }
						DebugLogger.Log($"[Grapple] 扛人 Z 探针：carrier.z={_carrier.Position.z:F3} 写入 z={p.z:F3}"
							+ $" → 回读 carried.z={back:F3}"
							+ (Math.Abs(back - p.z) < 0.05f ? "（差值≈0：引擎收下了）" : "（回读偏离：引擎按地形重算了）"));
					}
				}
			}
			catch (Exception) { }
		}

		/// <summary>
		/// 扛人者的**水平朝向**（配对表演那条先例：站位/朝向基准 = 角色朝向，**不是**相机方向 ——
		/// 配对动画的位移是沿角色前方推的）。相机那边见 CLAUDE.md 铁律 35。
		/// </summary>
		private static Vec3 ForwardOf(Agent a)
		{
			try
			{
				Vec3 f = a.LookFrame.rotation.f;
				f.z = 0f;
				if (f.LengthSquared > 1e-4f) return f.NormalizedCopy();
			}
			catch (Exception) { }
			return Vec3.Forward;
		}

		/// <summary>扛人者在不在动（判据口径 = <see cref="BindAnimConditions.CarrierMovingSpeed"/>；
		/// 用 `AverageVelocity` 防瞬时抖动让两个状态每帧互踢）。</summary>
		private bool IsCarrierMoving()
		{
			try
			{
				Vec3 v = _carrier.AverageVelocity;
				return new Vec2(v.x, v.y).Length > BindAnimConditions.CarrierMovingSpeed;
			}
			catch (Exception) { return false; }
		}

		/// <summary>扛人者那台机：按"时刻名"Force（<see cref="GrappleBind.ForceEvent"/> 的对偶）。</summary>
		private void ForceCarrier(string whenToken)
		{
			if (_machine == null || _carrier == null || string.IsNullOrEmpty(whenToken)) return;
			if (_machine.TryEventTarget(whenToken, out string state))
			{
				_machine.Force(_carrier, state, CarryAnimMachine.AnimBlendIn);
				return;
			}
			DebugLogger.Log($"[Grapple] 扛人：状态机里没有时刻 '{whenToken}' 的边（carry.xml 改坏了？）");
		}

		// ───────────────────────────── 诊断 ─────────────────────────────

		/// <summary>一行状态（命令 `custom.grapple carry state`）。**纯英文**（控制台纪律）。</summary>
		public string StatusLine()
		{
			if (!IsActive)
			{
				return $"carry: idle (switch {(Enabled ? "on" : "off")}) | last: {_note}";
			}
			string carState = _machine != null ? _machine.Current ?? "-" : "(no machine)";
			string carAct = _machine != null ? _machine.CurrentAction ?? "-" : "-";
			string carriedAct = "-";
			try { carriedAct = V.ActName(_carried, 0); } catch (Exception) { }
			float d = 0f;
			try
			{
				Vec3 v = _carried.Position - _carrier.Position;
				v.z = 0f;
				d = v.Length;
			}
			catch (Exception) { }
			return $"carry: {_phase} | carrier={SafeName(_carrier)} ({carState}/{carAct})"
				+ $" | carried={SafeName(_carried)} act={carriedAct}"
				+ $" | dist={d:F2}m moving={(IsCarrierMoving() ? "True" : "False")}"
				+ $" | offset={OffsetForward:F2}/{OffsetRight:F2}"
				+ $" | t={_t:F1}s";
		}

		private static string SafeName(Agent a)
		{
			try { return a?.Name?.ToString() ?? "?"; }
			catch (Exception) { return "?"; }
		}
	}
}
