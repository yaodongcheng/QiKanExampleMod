using System;
using LivingWorldNpcs.Animation;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **「扛人」适配器**（2026-10-10 立；**2026-10-19 改成通用双人动作运行时的消费者**）。
	///
	/// 分工（改架构后）：
	///   · **站位 / 同帧起播 / 每帧摆位 / 结束提交 / 控制权租约** = <see cref="PairActionInstance"/>
	///     （数据 = `ModuleData/DesignData/PairActions.csv` 的 `grapple_carry` 行）；
	///   · **本类只保留钩索专属**：绑缚前置校验、武器收/还、马背 handoff 触发、
	///     以及两侧状态机驱动（`carry.xml` / `bind.xml`）。
	///
	/// 🔴 **竖直 0.68 米不在这里**（这是最容易找错地方的一件）：把人托到肩上那一下
	///    **在被扛者自己的动画里**（根骨位置轨 z：站姿基准 0 → 肩上 +0.66，`tpaccli` / `trf_root_travel.py` 实测）。
	///    引擎渲染骨骼时会应用那条轨 ⇒ **我们一个字都不写 Z**（本来也写不进去，见
	///    [Knowledge/骑砍2Agent运动与位置机制.md](../../../Knowledge/骑砍2Agent运动与位置机制.md)）。
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
		public enum CarryPhase { None, Riding, PuttingDown, OnHorse }

		// ───────────────────────────── 可调参数（命令热调；重启复位）─────────────────────────────

		/// <summary>总开关（`custom.grapple carry on|off`）。关 = 交互面板不出现【扛起】、命令直接拒。</summary>
		public static bool Enabled = true;

		/// <summary>起扛时两者最远距离（米）—— 太远够不着。**从脚底量**（同钩索那条先例）。</summary>
		public static float MaxLiftDistance = 3.0f;

		/// <summary>
		/// **起扛时把扛人者手里的武器收进鞘**（`custom.grapple carry sheathe 0|1`，默认开）。
		///
		/// 🔴 为什么（2026-10-13 用户）：扛着人时手里还握着钩索绳（那件是 `Type=Bow`）⇒ 屏幕上挂着
		///    **准星**、人还端着弓的待机姿势，跟"抱着一个人"打架。收进鞘就干净了。
		/// 放下动画演完会**自动把钩索拿回手里**（前提：玩家自己没在扛人期间另拿了别的）。
		/// </summary>
		public static bool SheatheOnCarry = true;

		/// <summary>
		/// 收武器 / 还武器之后，给"收起钩索 = 松手放人"那条出口（<see cref="GrappleLogic"/> 出口②）的
		/// **豁免窗**（秒）。
		/// 🔴 为什么需要：`TryToWieldWeaponInSlot` 写下去，引擎要下一帧才认（`WieldedWeapon` 才变）——
		///    中间那一帧 `IsHoldingGrapple()` 仍是 false ⇒ 出口② 会把绳判成"被收起"当场放人。
		/// </summary>
		public static float StowGraceSeconds = 1.2f;

		/// <summary>放下动画播完之后、还没等到状态机出机时的兜底超时（秒）。</summary>
		public static float PutdownTimeoutSeconds = 6.0f;

		/// <summary>
		/// 摆位微调（米；**默认全 0**，因为对齐量本来就烘在被扛者的动画里）。
		/// 只在实机看着对不齐时才动：`custom.grapple carry offset &lt;前后&gt; [左右]`。
		/// 正的前后 = 往扛人者**面朝方向**挪；正的左右 = 往扛人者**右手侧**挪。
		/// </summary>
		public static float OffsetForward = 0f;
		public static float OffsetRight = 0f;

		// ───────────────────── 马背载人（2026-10-14 立；用户要"荒野大镖客"那种）─────────────────────
		//
		// **要什么**：扛着人时**瞄着马**可以把他放到马背**靠后**的位置，之后这个人的位置**从"绑玩家"
		//   改成"绑马背"** —— 玩家骑马就能带着他跑。靠近再【从马背拿下】把他放回地面。
		//
		// 🔴 **竖直那一段（悬在马背上）只能由动画给** —— agent 的 Z 写不进去（写多少都贴地，
		//    见 Knowledge/骑砍2Agent运动与位置机制.md），所以"人在马背上"靠的是 clip 的**根骨位置轨**
		//    （`beikangqi_*` 的 +0.68 米就是这个套路）。⇒ **需要一条新动画**：
		//    根骨 z ≈ 马背高（≈1.40 m）、趴/横在马背上、循环。
		//    ⚠️ 在它出来之前，这条状态**占位指向 `beikangqi_idle`**（见 action_sets.xml）——
		//    机制能跑、能验位置/跟随，只是"人悬在马肚子高度"看着不对。

				/// <summary>
		/// **马背朝向探针**（秒；0 = 关，`custom.grapple carry horseprobe <秒>`）—— 人搁马上时每这么多秒打一行：
		/// 马的朝向 / 我们写进去的朝向 / **回读**人自己的 `LookDirection` / 两者夹角。
		/// 判据："人没跟着马转"这类问题看这一行就够：写进去的和回读的对不上 = 引擎把朝向改回去了；
		/// 马那一列**恒定不变** = 我们读马的朝向读错了（见 <see cref="ForwardOf"/>）。
		/// </summary>
		public static float HorseProbeSeconds = 0f;
		private float _horseProbeTimer;

		/// <summary>放到马背时**朝马屁股方向偏多少米**（默认 0.55 —— 马身 ~2.4 m，鞍在中间、臀在这后面）。</summary>
		public static float HorseBackOffset = 0.55f;

		/// <summary>放到马背时**朝马右手侧偏多少米**（默认 0 —— 正对脊线）。</summary>
		public static float HorseSideOffset = 0f;

		/// <summary>
		/// 放到马背上之后，那个人**朝哪**：
		///   · `along`（默认）= 跟马同向（头朝马头，顺着脊线趴）
		///   · `across` = 横过来（头朝马的右手侧 —— "打横驮着"，荒野大镖客那种）
		///   · `reverse` = 跟马反向（头朝马尾）
		/// 命令 `custom.grapple carry horseface along|across|reverse`。
		/// </summary>
		public static string HorseFaceMode = "along";

		/// <summary>
		/// **放到马背时允许的最远距离**（米）—— 只当**命令路径**的兜底保险；
		/// 真正决定"给不给【放上马背】"的是**面板焦点**（与"身边有什么尸体 / NPC"同一套检测），不是这个数。
		/// 🔴 2026-10-14 教训：**别再拿它当"附近有没有马"的判据** —— 那是我自己另造的第二套口径，
		///    与面板焦点打架 ⇒ 用户看到"隔老远也显示 / 走近了却不显示"两个 bug 来回翻。
		/// </summary>
		public static float MaxHorseDistance = 4.0f;

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
		private Agent _horse;          // 人搁在哪匹马的背上（OnHorse 相位；null = 没搁马上）
		private GrappleBind _bind;              // 只为了收摊时把"扛人者"从被绑者那侧清掉（别留悬空引用）
		private AgentAnimStateMachine _machine;
		private CarryAnimContext _ctx;
		private PairActionInstance _pair;       // 2026-10-19：站位/起播/摆位/结束交给通用双人动作运行时
		private CarryPhase _phase = CarryPhase.None;
		private float _t;
		private string _note = "-";
		private bool _weSheathed;      // 武器是"我们"收的才由我们还（玩家自己收的/自己换的，不碰）
		private EquipmentIndex _stowSlot = EquipmentIndex.None;   // 收武器**之前**记下的"绳在哪个槽"
		private MissionWeapon _stowWeapon;                        // 收之前那件东西**本身**（引擎弄丢时原样塞回）
		private float _stowWatchTimer;                            // "看着点"那班岗的计时（见 WatchStowSlot）

		/// <summary>"看着点"的查岗间隔（秒）—— 收完武器后每这么久看一次那个槽还在不在。</summary>
		public const float StowWatchSeconds = 0.25f;
		private float _stowGrace;      // >0 = 给出口② 的豁免窗（见 StowGraceSeconds）

		public bool IsActive => _phase != CarryPhase.None && _carried != null;
		public Agent Carrier => _carrier;
		public Agent Carried => _carried ?? MountPair?.Defender;

		/// <summary>人现在搁在哪匹马的背上（没搁 = null）。交互面板"从马背拿下"的判据。</summary>
		public Agent Horse => _horse ?? MountPair?.Attacker;

		/// <summary>人是不是正搁在**这匹**马上。</summary>
		public bool IsOnHorse(Agent horse) => horse != null && ReferenceEquals(Horse, horse);
		public CarryPhase Phase => _phase;
		public string Note => _note;

		/// <summary>
		/// 当前有没有一对「坐骑 ↔ 人」的挂接 pair（2026-10-19：马背走 handoff，旧 pair 演完就退场，
		/// 之后 **位置由后继 pair 每帧摆**；`GrappleCarry` 只负责查询/触发）。
		/// </summary>
		private static PairActionInstance MountPair
		{
			get
			{
				// 适配器按**自己拥有的两台状态机**找行（carry / bind），再顺着 `HandoffTo` 找后继。
				if (!PairActionTable.FindByMachines(CarryAnimMachine.Name, BindAnimMachine.Name, out PairActionDef carryDef)
					|| string.IsNullOrWhiteSpace(carryDef.HandoffTo))
					return null;

				PairActionLogic logic = PairActionLogic.Current;
				if (logic == null) return null;
				PairActionInstance inst = logic.FindByDefId(carryDef.HandoffTo);
				return inst != null && !inst.IsDone ? inst : null;
			}
		}

		/// <summary>
		/// **"收起钩索 = 松手放人"那条出口现在要不要让路**（消费者 = <see cref="GrappleLogic"/> 出口②）。
		/// 扛着人期间一律豁免 —— 那时武器是**我们收的**（见 <see cref="SheatheOnCarry"/>），
		/// 不能被当成"玩家把钩索收起来了"；还武器之后再撑 <see cref="StowGraceSeconds"/> 秒等引擎认账。
		/// </summary>
		public bool SuppressStowCheck => IsActive || _stowGrace > 0f;

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

			// 让被扛者知道扛人者是谁 —— 之后每帧只用来读它的速度（被扛行 ⇄ 被扛走的判据）
			bind.SetCarrier(carrier);

			// 🔴 2026-10-19：站位 / 同帧起播 / 每帧摆位 / 结束提交全部交给通用双人动作运行时。
			//    两侧播什么仍由本类的 carry.xml + GrappleBind 的 bind.xml 决定（驱动只 Force 时刻名）。
			PairActionLogic logic = PairActionLogic.Current;
			if (logic == null)
			{
				RollbackBegin();
				why = "PairActionLogic not mounted (not in a mission?)";
				return false;
			}
			if (!PairActionTable.FindByMachines(CarryAnimMachine.Name, BindAnimMachine.Name, out PairActionDef def)
				|| def == null)
			{
				RollbackBegin();
				why = $"no PairActions.csv row with machines '{CarryAnimMachine.Name}/{BindAnimMachine.Name}'";
				return false;
			}

			if (!logic.Begin(def, carrier, carried, new CarryMachineDriver(this), new BindMachineDriver(this),
					ignoreApproachDistance: false, defenderHeldByOther: true, out why))
			{
				RollbackBegin();
				return false;
			}

			_pair = logic.GetByAgent(carrier);
			if (_pair != null)
			{
				_pair.ExtraAttachRight = OffsetRight;
				_pair.ExtraAttachForward = OffsetForward;
			}

			// 🔴 **顺手把武器收进鞘**（2026-10-13 用户要的）：不收的话手里还握着绳（弓），
			//    屏幕上有准星、人端着弓，跟"抱着人"打架。放下（HardStop）时自动还回去。
			if (SheatheOnCarry)
				SheatheCarrier(carrier);

			DebugLogger.Log($"[Grapple] 扛人：开始（{SafeName(carrier)} 扛 {SafeName(carried)}，"
				+ $"距离 {d:F2} m）—— 走 PairAction 'grapple_carry'（先走到站位，再同帧起播扛起/被扛起）");
			return true;
		}

		/// <summary>`Begin` 失败时把临时建好的状态清干净（不碰 agent）。</summary>
		private void RollbackBegin()
		{
			_machine = null;
			_ctx = null;
			_phase = CarryPhase.None;
			_carried = null;
			_carrier = null;
			_bind = null;
			_pair = null;
			_t = 0f;
			_note = "-";
		}

		/// <summary>每帧推进（<see cref="GrappleLogic.OnMissionTick"/> 调；扛着时必然每帧）。</summary>
		public void Tick(float dt, GrappleBind bind)
		{
			// 豁免窗的倒计时**要跑在 IsActive 早退之前** —— 它恰恰是"已经不扛了"之后的那段时间
			// （还武器 → 引擎认账之间的那一两帧），见 StowGraceSeconds。
			if (_stowGrace > 0f)
				_stowGrace -= dt;

			if (!IsActive) return;

			// ── 失效守卫：任一方没了 / 换了目标 ⇒ 立刻散伙（**不再摆位**，让人自己倒回地面）──
			//    🔴 马背相位只看**马**和**人**：扛人者（玩家）走远了不管（人已经交给马了）。
			bool carrierNeeded = _phase != CarryPhase.OnHorse;
			if (!AgentControlHelper.SafeIsActive(_carried)
				|| (carrierNeeded && !AgentControlHelper.SafeIsActive(_carrier))
				|| (_horse != null && !AgentControlHelper.SafeIsActive(_horse)))
			{
				HardStop(_horse != null ? "horse gone" : "agent gone");
				return;
			}
			if (bind == null || !bind.IsActive || !ReferenceEquals(bind.Target, _carried))
			{
				HardStop("target no longer bound by this rope");
				return;
			}

			_t += dt;

			// ⓪ **看着点那个槽**（我们收过武器才查）：引擎把它弄丢就原样塞回去（见 WatchStowSlot）
			WatchStowSlot(dt);

			// ⓪′ 🔴 **不用再同步"脑里占位的锚点"了**（2026-10-20 单权威收口）：
			//     `BoundAction` 已改成**纯占位**（一个字都不碰身体）—— 位置/朝向/限速全归 `GrappleBind`，
			//     这里每帧摆位不会再跟脑那边 0.2 s 一次的"钉回锚点"互相打脸。

			// ① 2026-10-19：站位 / 每帧摆位 / 扛人者状态机 Tick 全部在 PairActionInstance 里
			//    （它由 PairActionLogic 每帧推进；本类只在驱动里补"扛人者那台机"的上下文）。
			if (_pair == null)
			{
				HardStop("pair lost");
				return;
			}
			if (_pair.IsDone)
			{
				HardStop("pair finished");
				return;
			}
			_note = "pair: " + _pair.Phase;

			// ② 放下/收势超时兜底（正常路径由 pair 的 Ending 收尾）
			if (_phase == CarryPhase.PuttingDown && _t >= PutdownTimeoutSeconds + 6f)
			{
				DebugLogger.Log($"[Grapple] 扛人：🔴 放下超时（{_t:F1}s）⇒ 强制收摊");
				_pair.Abort("put down timeout");
				HardStop("put down timeout");
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
			if (_pair == null || _pair.IsDone)
			{
				HardStop("put down: no active pair");
				return false;
			}
			_pair.RequestStop("put down (" + reason + ")");
			DebugLogger.Log($"[Grapple] 扛人：放下（{reason}）—— pair 会 Force put-trigger（两侧），"
				+ "退出 clip 演完再松手");
			return true;
		}

		/// <summary>
		/// **把人放到马背上**（用户 2026-10-14：扛着人 + 瞄着马 ⇒ 放马背靠后的位置，之后位置**绑马**）。
		///
		/// 与"放下"的区别：放下 = 人落地、扛人关系结束；这条 = **人换了个"载体"**（玩家 → 马），
		/// 扛人关系也结束（扛人者那侧演完"放下"就自由了），但**被扛的这个人继续由本控制器每帧摆位**
		/// （贴在马背后靠的位置上），直到【从马背拿下】或松绳。
		///
		/// 🔴 **竖直方向（悬在马背上）不在代码里** —— 靠那个人自己的 clip 根骨位置轨给（见类顶部的说明），
		///    所以这条相位在拿到新动画之前是**占位演出**（人看上去悬在马肚子高度）。
		/// </summary>
		public bool PlaceOnHorse(Agent horse, GrappleBind bind, out string why)
		{
			why = "-";
			if (!Enabled) { why = "carry disabled"; return false; }
			if (!IsActive || _phase != CarryPhase.Riding) { why = "not carrying anyone (phase = " + _phase + ")"; return false; }
			if (horse == null) { why = "no horse"; return false; }
			if (!AgentControlHelper.SafeIsActive(horse)) { why = "horse inactive"; return false; }
			// 🔴 判据 = `HorseComponent.IsRideable`（2026-10-14 实机：用户那匹是**驮马**，
			//    `Agent.IsMount` 会把驮马判死 ⇒ 面板不给【放上马背】）。见 GrappleCommands.IsRideableMount。
			if (!AgentControlHelper.IsRideableMount(horse)) { why = "that is not a rideable horse"; return false; }
			float d = 0f;
			try { Vec3 v = horse.Position - _carried.Position; v.z = 0f; d = v.Length; } catch (Exception) { }
			if (d > MaxHorseDistance) { why = $"too far from the horse ({d:F1} m > {MaxHorseDistance:F1} m)"; return false; }
			if (_pair == null || _pair.IsDone) { why = "no active pair"; return false; }

			// 🔴 交接目标 / 进入事件全部来自 CSV：
			//    本行 `HandoffTo` → 后继行 `EnterEvent`（例如 grapple_carry → mounted_on_mount / horse-trigger）。
			string successorId = _pair.Def?.HandoffTo;
			if (string.IsNullOrWhiteSpace(successorId))
			{
				why = $"pair '{_pair.Def?.Id}' has no HandoffTo (add it in PairActions.csv)";
				return false;
			}
			if (!PairActionTable.TryGet(successorId, out PairActionDef successor) || successor == null)
			{
				why = $"handoff target '{successorId}' not found in PairActions.csv";
				return false;
			}
			if (string.IsNullOrWhiteSpace(successor.EnterEvent))
			{
				why = $"handoff target '{successorId}' has no EnterEvent (add it in PairActions.csv)";
				return false;
			}

			// 被扛者那侧：按后继行的 EnterEvent 进挂接状态；扛人者那侧由 pair 的 handoff 演"放下"。
			bind?.ForceEvent(successor.EnterEvent);

			if (!_pair.RequestHandoff(successorId, horse, out why))
				return false;

			// 后继 pair 已经建好：把旧的马背旋钮（horseoffset / horseface）映射成它的运行期偏移。
			PairActionInstance mountPair = MountPair;
			if (mountPair != null)
			{
				mountPair.ExtraAttachForward = -HorseBackOffset - mountPair.Def.Stance.Forward;
				mountPair.ExtraAttachRight = HorseSideOffset - mountPair.Def.Stance.Right;
				mountPair.ExtraAttachYawDeg = HorseFaceMode == "across" ? 90f
					: HorseFaceMode == "reverse" ? 180f : 0f;
			}

			_horse = horse;
			_phase = CarryPhase.OnHorse;
			_t = 0f;
			_note = "handing off to mount";

			DebugLogger.Log($"[Grapple] 马背：把 {SafeName(_carried)} 放到 {SafeName(horse)} 的背上"
				+ $"（后偏 {HorseBackOffset:F2} m · 侧偏 {HorseSideOffset:F2} m · 朝向 {HorseFaceMode}）"
				+ $" —— 交接给 PairAction '{successorId}'（坐骑 ↔ 人），之后位置**跟着马走**"
				+ "（⚠️ 悬空高度靠动画根骨轨：现在占位指向 beikangqi_idle，新 clip 出来换指向即可）");
			return true;
		}

		/// <summary>
		/// **从马背拿下**（放回地面）：人落到马旁边、播"被放下"（→ 站缚），扛人关系结束。
		/// 玩家离得远也能敲（命令路径）；面板那条要求人在交互距离内。
		/// </summary>
		public bool TakeOffHorse(GrappleBind bind, string reason)
		{
			PairActionInstance mountPair = MountPair;
			Agent horse = mountPair?.Attacker ?? _horse;
			Agent carried = mountPair?.Defender ?? _carried;
			if (horse == null || carried == null) return false;

			// 落点：马旁边（右脚侧 1 米）—— 先摆到位再播"被放下"，不然人会从马背上"瞬移"到原地
			try
			{
				Vec3 f = ForwardOf(horse, mount: true);
				Vec3 right = new Vec3(f.y, -f.x, 0f);
				Vec3 p = horse.Position + right * 1.0f;
				p.z = carried.Position.z;
				carried.TeleportToPosition(p);
			}
			catch (Exception) { }

			// 出挂接状态的事件也来自 CSV（后继行的 ExitEvent）。
			string exitEvent = mountPair?.Def?.ExitEvent;
			if (string.IsNullOrWhiteSpace(exitEvent))
				DebugLogger.Log("[Grapple] 马背：后继 pair 缺 ExitEvent —— 取下时被扛者不会回站缚（检查 PairActions.csv）");
			else
				bind?.ForceEvent(exitEvent);

			if (mountPair != null)
				mountPair.RequestStop("taken off horse (" + reason + ")");
			if (_pair != null && !_pair.IsDone)
				_pair.Abort("taken off horse");

			DebugLogger.Log($"[Grapple] 马背：把 {SafeName(carried)} 从 {SafeName(horse)} 背上拿下（{reason}）—— 落地回站缚");
			HardStop("taken off horse (" + reason + ")");
			return true;
		}

		/// <summary>立刻散伙（不再摆位、不再管动画）—— 用于 agent 没了之类的异常路径。</summary>
		public void HardStop(string reason)
		{
			// 马背的后继 pair 独立存在（人已经交给马了）；松绳/收摊时也要把它收掉。
			try
			{
				PairActionInstance mountPair = MountPair;
				if (mountPair != null && !mountPair.IsDone)
					mountPair.Abort("carry stopped: " + reason);
			}
			catch (Exception) { }

			if (_carried == null && _carrier == null) return;
			DebugLogger.Log($"[Grapple] 扛人：结束（{reason}）");

			// 先把 pair 收掉（归还玩家控制权 / AI），再还武器、清本地状态。
			try { if (_pair != null && !_pair.IsDone) _pair.Abort("carry stopped: " + reason); } catch (Exception) { }
			_pair = null;

			// 🔴 **还武器要排在收摊之前**（`_carrier` 一清就没对象了）。
			//    无论走哪条路结束都还 —— "我们收走的，我们负责还"；玩家自己另拿了东西就不抢（见方法注释）。
			RestoreCarrierWeapon();
			// 🔴 **收摊后一律开豁免窗**（2026-10-14 实机事故的止血）：此前只有"还武器成功"才开窗
			//    ⇒ 一旦还失败，玩家手上是空的 → `IsHoldingGrapple()` 假 → 那条"收起钩索 = 放人"的出口
			//    **在同一毫秒把整条绑放掉**（日志实证：结束 → 1 毫秒后"player stowed the grapple"），
			//    表现就是"放下来之后对方再也不能被扛起"。窗口期内出口②会让路，玩家自己拔回来即可。
			if (_stowGrace <= 0f)
				_stowGrace = StowGraceSeconds;
			try { _bind?.SetCarrier(null); } catch (Exception) { }   // 别在被绑者那侧留个悬空的扛人者引用
			_carrier = null;
			_carried = null;
			_horse = null;
			_bind = null;
			_machine = null;
			_ctx = null;
			_phase = CarryPhase.None;
			_t = 0f;
			_note = "stopped (" + reason + ")";
		}

		// ───────────────────────────── 武器收 / 还（2026-10-13 立）─────────────────────────────
		//
		// 🔴 起因（用户）："扛起一个人的时候，希望自动收起武器 —— 现在看还处于钩锁拿起状态、有准星不太好。"
		//    钩索绳是 `Type=Bow`：握在手里 = 引擎给弓的那套（准星 + 端弓待机），跟"抱着一个人"打架。
		//    收进鞘 = 手上空着 ⇒ 准星没了、待机回徒手。
		//
		// 🔴 **连带必须改的一处**（见 `GrappleLogic` 出口② 的注释）：那条"收起钩索 = 松手放人"的规矩
		//    会把"我们主动收武器"误判成玩家收起了钩索 ⇒ 用 `SuppressStowCheck` 给它开豁免。

		/// <summary>把扛人者双手的武器收进鞘（成功置 <see cref="_weSheathed"/>，放下时由我们负责还）。</summary>
		private void SheatheCarrier(Agent a)
		{
			if (a == null) return;
			try
			{
				// 🔴 **先记住"绳在哪个槽、整件东西长什么样"再收**（2026-10-14 实机事故）：
				//    收完之后绳会**从装备里消失**（用户实机："不是收起武器，是丢在了地上，我得捡回来"）。
				//    实测日志（`还不了武器 … 当前装备：… Weapon3=-`）证实：收起那一刻还在 Weapon3，
				//    扛人期间没了。备忘录 = 万一引擎真弄丢，我们能**原样塞回去**。
				_stowSlot = FindItemSlot(a, GrappleFirePatch.RopeItemId);
				_stowWeapon = _stowSlot != EquipmentIndex.None ? a.Equipment[_stowSlot] : default(MissionWeapon);
				DebugLogger.Log($"[Grapple] 扛人：收武器前记下绳的槽位 = {_stowSlot}"
					+ (_stowSlot == EquipmentIndex.None ? "（🔴 没找到 —— 收完就还不了了）"
						: $"（{_stowWeapon.Item?.StringId}）"));
				// 先主手后副手（与 `CombatManager` / 原版 `StandingPoint.AutoSheathWeapons` 同一套写法）
				a.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
				a.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
				_weSheathed = true;
				_stowGrace = StowGraceSeconds;
				_stowWatchTimer = 0f;          // 开"看着点"那班岗（下面 Tick 里跑）
				DebugLogger.Log($"[Grapple] 扛人：已收起 {SafeName(a)} 的武器（扛着期间手上不拿东西 —— 不要准星/端弓姿势）");
			}
			catch (Exception ex)
			{
				_weSheathed = false;
				DebugLogger.Log($"[Grapple] 扛人：收武器失败（忽略，照常扛）: {ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>
		/// **"看着点"那班岗**（扛人期间每 0.25 s 一次）：收完武器之后盯着那个槽，**空了就原样塞回去**。
		///
		/// 🔴 为什么要有它（2026-10-14 实机）：收起那一下引擎会把绳**从装备里弄没**（掉在地上），
		///    用户得自己捡回来才能用。塞回去之后**东西在槽里、但手上不拿** —— 正好就是我们想要的状态
		///    （不要准星/端弓），比"真收进鞘"还干净。
		/// ⚠️ 只在**我们收过武器**、且这一班岗还没下的时候查（玩家自己中途拔刀/换武器都不碰）。
		/// </summary>
		private void WatchStowSlot(float dt)
		{
			if (!_weSheathed || _carrier == null || _stowSlot == EquipmentIndex.None || _stowWeapon.Item == null)
				return;
			_stowWatchTimer += dt;
			if (_stowWatchTimer < StowWatchSeconds) return;
			_stowWatchTimer = 0f;
			try
			{
				if (!AgentControlHelper.SafeIsActive(_carrier)) return;
				if (_carrier.Equipment[_stowSlot].Item != null) return;      // 还在 ⇒ 没事
				// 空了 ⇒ 引擎把它弄丢了：原样塞回去（**不 wield** —— 手上空着正是我们要的）
				MissionWeapon w = _stowWeapon;
				_carrier.EquipWeaponWithNewEntity(_stowSlot, ref w);
				DebugLogger.Log($"[Grapple] 扛人：🔴 绳从装备里消失了（引擎弄丢的，槽 {_stowSlot}）—— 已原样塞回"
					+ "（东西在槽里、手上不拿 = 本来要的状态）。若这条反复出现说明引擎**见不得**把它收起来");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 扛人：塞回绳失败（{ex.GetType().Name}: {ex.Message}）—— 放下时会再试一次");
			}
		}

		/// <summary>
		/// **把钩索还回扛人者手里**（扛人结束的收尾）。三条前提缺一不可：
		///   ① 是**我们**收的（<see cref="_weSheathed"/> —— 玩家自己收的不管）；
		///   ② 玩家现在手上是空的（扛人期间自己又拔了刀 ⇒ 别抢他的）；
		///   ③ 还找得到那个槽（<see cref="_stowSlot"/> 优先 —— 收之前记下的那个；找不到再现场扫一遍）。
		/// 🔴 **每一条早退都要有日志**（2026-10-14）：此前全是静默 return，实机只能看到"武器没还回来"，
		///    查不出是哪一条挡的（日志里连一行都没有）。
		/// </summary>
		private void RestoreCarrierWeapon()
		{
			Agent a = _carrier;
			bool weSheathed = _weSheathed;
			EquipmentIndex stowSlot = _stowSlot;
			_weSheathed = false;
			_stowSlot = EquipmentIndex.None;
			if (!weSheathed || a == null)
			{
				DebugLogger.Log($"[Grapple] 扛人：不还武器（weSheathed={weSheathed}，carrier={(a == null ? "null" : "ok")}）");
				return;
			}
			try
			{
				if (!AgentControlHelper.SafeIsActive(a))
				{
					DebugLogger.Log("[Grapple] 扛人：不还武器（扛人者已失效）");
					return;
				}
				if (a.WieldedWeapon.Item != null)
				{
					DebugLogger.Log($"[Grapple] 扛人：不还武器（手上已有 '{a.WieldedWeapon.Item.StringId}' —— 不抢玩家的）");
					return;
				}
				// 优先用"收之前记下的槽"；它要是失效了（装备被动过）再现场扫一遍
				EquipmentIndex slot = stowSlot;
				if (slot == EquipmentIndex.None || !SlotHasItem(a, slot, GrappleFirePatch.RopeItemId))
				{
					slot = FindItemSlot(a, GrappleFirePatch.RopeItemId);
				}
				// 🔴 装备里真没有 ⇒ 把"收之前存下的那件"原样塞回它自己的槽（见 WatchStowSlot 的注释）
				if (slot == EquipmentIndex.None && stowSlot != EquipmentIndex.None && _stowWeapon.Item != null)
				{
					MissionWeapon w = _stowWeapon;
					a.EquipWeaponWithNewEntity(stowSlot, ref w);
					slot = stowSlot;
					DebugLogger.Log($"[Grapple] 扛人：装备里没有绳 ⇒ 已把收之前存的那件原样塞回槽 {stowSlot}");
				}
				if (slot == EquipmentIndex.None)
				{
					DebugLogger.Log($"[Grapple] 扛人：🔴 还不了武器 —— 装备里找不到 '{GrappleFirePatch.RopeItemId}'"
						+ $"（收武器时记的槽 = {stowSlot}）。当前装备：{DumpWeaponSlots(a)}");
					return;
				}
				a.TryToWieldWeaponInSlot(slot, Agent.WeaponWieldActionType.Instant, false);
				_stowGrace = StowGraceSeconds;
				DebugLogger.Log($"[Grapple] 扛人：钩索已还回 {SafeName(a)} 手里（槽 {slot}）");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 扛人：还武器失败（忽略 —— 用数字键自己拿）: {ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>某个槽里是不是那件东西（用于校验"收之前记下的槽"还有效）。</summary>
		private static bool SlotHasItem(Agent a, EquipmentIndex slot, string itemId)
		{
			try
			{
				if (slot < EquipmentIndex.WeaponItemBeginSlot || slot >= EquipmentIndex.NumAllWeaponSlots) return false;
				MissionWeapon w = a.Equipment[slot];
				return w.Item != null && w.Item.StringId == itemId;
			}
			catch (Exception) { return false; }
		}

		/// <summary>把 5 个武器槽的内容打成一行（还不了武器时的取证用）。</summary>
		private static string DumpWeaponSlots(Agent a)
		{
			try
			{
				var sb = new System.Text.StringBuilder();
				for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
					slot < EquipmentIndex.NumAllWeaponSlots; slot++)
				{
					MissionWeapon w = a.Equipment[slot];
					sb.Append(slot).Append('=').Append(w.Item != null ? w.Item.StringId : "-").Append(' ');
				}
				return sb.ToString();
			}
			catch (Exception) { return "(dump failed)"; }
		}

		/// <summary>在装备里找某件物品所在的槽（口径同 `GrappleFirePatch.FindDartSlot`）。找不到 = None。</summary>
		private static EquipmentIndex FindItemSlot(Agent a, string itemId)
		{
			try
			{
				for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
					slot < EquipmentIndex.NumAllWeaponSlots; slot++)
				{
					MissionWeapon w = a.Equipment[slot];
					if (w.Item != null && w.Item.StringId == itemId) return slot;
				}
			}
			catch (Exception) { }
			return EquipmentIndex.None;
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
		/// <summary>
		/// 🪦 **2026-10-19 起不再调用**：每帧摆位已移到 `PairActionInstance.TryPlaceAttached`
		/// （站位由 `PairActions.csv` 的 `grapple_carry` 行驱动）。保留本方法仅作旧行为对照。
		/// </summary>
		private void AttachCarried()
		{
			// 🔴 **马背相位**：贴的是**马**（不是扛人者）—— 马背靠后的位置 + 沿马脊线的朝向。
			//    竖直（悬在马背上）**不在这里写**，靠那个人自己的 clip 根骨位置轨（见类顶部说明）。
			if (_horse != null)
			{
				AttachCarriedToHorse();
				return;
			}
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
		/// **把人摆到马背靠后的位置**（每帧）：XZ 贴马、朝向按 <see cref="HorseFaceMode"/>。
		/// 前后/左右两个偏移都是**沿马自己**量的（前进方向 / 右手方向），所以马转弯时人跟着转。
		/// </summary>
		private void AttachCarriedToHorse()
		{
			try
			{
				Vec3 f = ForwardOf(_horse, mount: true);   // 马：优先读它自己的"运动方向"（= 朝向），见 ForwardOf
				Vec3 right = new Vec3(f.y, -f.x, 0f);
				Vec3 p = _horse.Position - f * HorseBackOffset + right * HorseSideOffset;
				if (WriteZ) p.z = _horse.Position.z + ZLift;
				_carried.TeleportToPosition(p);

				// 朝向：along = 跟马同向 · reverse = 反向 · across = 横过来（头朝马的右手侧）
				Vec3 face = HorseFaceMode == "across" ? right
					: HorseFaceMode == "reverse" ? -f
					: f;
				_carried.SetMovementDirection(face.AsVec2);

				// 探针（默认关）：写在**写完朝向之后**，这样"写进去的"与"回读的"能直接对比
				if (HorseProbeSeconds > 0f)
				{
					_horseProbeTimer += 1f / 60f;
					if (_horseProbeTimer >= HorseProbeSeconds)
					{
						_horseProbeTimer = 0f;
						Vec3 back = Vec3.Forward;
						try { back = _carried.LookDirection; back.z = 0f; } catch (Exception) { }
						float dot = 0f;
						try
						{
							if (back.LengthSquared > 1e-4f)
								dot = Vec3.DotProduct(face.NormalizedCopy(), back.NormalizedCopy());
						}
						catch (Exception) { }
						DebugLogger.Log($"[Grapple] 马背探针：马 {SafeName(_horse)} 朝向 = {Fmt(f)}（源 {_lastForwardSrc} · 模式 {HorseFaceMode}）"
							+ $" · 写入朝向 = {Fmt(face)} · 回读人朝向 = {Fmt(back)}"
							+ $" · 夹角 = {(float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * 180.0 / Math.PI):F0}°"
							+ $"（回读≈写入 = 引擎收下了；马那列恒定 = 读马的朝向读错了）");
					}
				}
			}
			catch (Exception) { }
		}

		/// <summary>
		/// 扛人者的**水平朝向**（配对表演那条先例：站位/朝向基准 = 角色朝向，**不是**相机方向 ——
		/// 配对动画的位移是沿角色前方推的）。相机那边见 CLAUDE.md 铁律 35。
		/// </summary>
		/// <summary>人的"面朝方向"（读 look —— 人横着走时运动方向 ≠ 面朝方向，不能拿运动方向）。</summary>
		private static Vec3 ForwardOf(Agent a) => ForwardOf(a, mount: false);

		/// <summary>
		/// 取"某个 agent 现在面朝哪"（水平、已归一化）—— **按物种分源**（2026-10-14 用户：
		/// "马有没有自己的 movementDirection 呢，别用 look 吧"）：
		///
		///   · **马（mount:true）**：先读 **`GetMovementDirection()`** —— 我们自己的轮子里写着
		///     "**转向 = `SetMovementDirection(Vec2)`**"，即引擎的**运动方向就是它的朝向**（马不会横着走）；
		///     读不到（站着不动时可能是零向量）再退到**世界帧 `Frame`**（引擎渲染它用的那一帧），最后才 look。
		///   · **人（mount:false）**：先读 **look**（面朝），再 Frame、最后运动方向。
		///
		/// 🔴 原来只有一个源 `LookFrame.rotation.f`，读不到就**静默**退回世界 +Y 常量 —— 对**马**这种坐骑
		///    agent，`LookFrame` 很可能就是退化的 ⇒ 摆位用的"前向量"成了常量 ⇒ 人跟着马**平移**、
		///    既不绕着马转也不自己转（实机症状："挂到马身上不跟着马转"）。
		///    现在四个源依次试，**全失败才退回常量，且第一次会打日志**（不再静默）。
		/// </summary>
		private static Vec3 ForwardOf(Agent a, bool mount)
		{
			Vec3 f;
			if (mount)
			{
				if (TryFlat(a, () => a.GetMovementDirection().ToVec3(), out f)) { NoteForwardSource(a, "moveDir"); return f; }
				if (TryFlat(a, () => a.Frame.rotation.f, out f)) { NoteForwardSource(a, "frame"); return f; }
			}
			if (TryFlat(a, () => a.LookFrame.rotation.f, out f)) { NoteForwardSource(a, "look"); return f; }
			if (TryFlat(a, () => a.Frame.rotation.f, out f)) { NoteForwardSource(a, "frame"); return f; }
			if (TryFlat(a, () => a.GetMovementDirection().ToVec3(), out f)) { NoteForwardSource(a, "moveDir"); return f; }
			NoteForwardSource(a, "NONE");
			return Vec3.Forward;
		}

		/// <summary>取向量的水平分量并归一化；长度太小（退化）返回 false。</summary>
		private static bool TryFlat(Agent a, Func<Vec3> get, out Vec3 flat)
		{
			flat = Vec3.Forward;
			try
			{
				Vec3 v = get();
				v.z = 0f;
				if (v.LengthSquared > 1e-4f) { flat = v.NormalizedCopy(); return true; }
			}
			catch (Exception) { }
			return false;
		}

		/// <summary>记下"这次用的是哪个源"，变了就打一行（首次必打）—— 排查"读不到朝向"时一眼定位。</summary>
		private static string _lastForwardSrc;
		private static void NoteForwardSource(Agent a, string src)
		{
			if (string.Equals(_lastForwardSrc, src, StringComparison.Ordinal)) return;
			_lastForwardSrc = src;
			if (src == "NONE")
			{
				DebugLogger.Log($"[Grapple] 扛人：🔴 读不到 {SafeName(a)} 的朝向（moveDir / frame / look 全空）"
					+ " —— 摆位改用世界 +Y 常量（人不会跟着他转！请把这一行发给开发）");
			}
			else
			{
				DebugLogger.Log($"[Grapple] 扛人：读 {SafeName(a)} 的朝向改用了 '{src}' 这个源（马应优先 moveDir；人优先 look）");
			}
		}

		/// <summary>
		/// 命令层：摆位微调（米；默认 0 —— 正式站位在 `PairActions.csv` 的 `Stance_*`）。
		/// 这是**运行期额外偏移**，叠加在 CSV 站位上；扛着时改立即生效。
		/// </summary>
		public void SetRuntimeOffset(float forward, float right)
		{
			OffsetForward = forward;
			OffsetRight = right;
			if (_pair != null)
			{
				_pair.ExtraAttachForward = forward;
				_pair.ExtraAttachRight = right;
			}
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

		/// <summary>给 pair 的扛人者驱动用：填上下文 + 推进状态机（原来在 `Tick` 里那一段）。</summary>
		private void TickCarrierMachine(float dt)
		{
			if (_machine == null || _ctx == null || _carrier == null) return;
			_ctx.Moving = IsCarrierMoving();
			_ctx.AnimRemainFrac = _machine.CurrentRemainFrac;
			_machine.Tick(_carrier, dt);
		}

		/// <summary>
		/// 事件 token 从 `PairActions.csv` 读（`EnterEvent` / `ExitEvent`，两侧共用）。
		/// 🔴 **没有代码兜底**：表里空 = 直接报错（数据驱动，事件名不在 C# 里写第二份）。
		/// </summary>
		private string PairEvent(bool enter)
		{
			PairActionDef def = _pair?.Def;
			string token = def != null ? (enter ? def.EnterEvent : def.ExitEvent) : null;
			if (!string.IsNullOrWhiteSpace(token))
				return token;

			DebugLogger.Log($"[Grapple] 🔴 pair '{def?.Id ?? "?"}' 缺 {(enter ? "EnterEvent" : "ExitEvent")}"
							+ " —— 状态机驱动无法起播/收势（检查 PairActions.csv）");
			return null;
		}

		// ═════════════════════════ 双人动作驱动（2026-10-19）═════════════════════════
		//
		// pair 运行时只认 IPairSideDriver；**状态机仍归本类 / GrappleBind 所有**。
		// 驱动只做三件事：按时刻 token Force 进/出、每帧推进（扛人者那台）、报告出机。

		/// <summary>扛人者一侧：carry.xml（扛起 / 扛行 / 扛走 / 放下）。</summary>
		private sealed class CarryMachineDriver : IPairSideDriver
		{
			private readonly GrappleCarry _owner;

			public CarryMachineDriver(GrappleCarry owner) { _owner = owner; }

			public bool CanStart(Agent agent, out string why)
			{
				why = _owner._machine == null ? "carry state machine not created" : "-";
				return _owner._machine != null;
			}

			public bool Start(Agent agent, out string why)
			{
				why = "-";
				string token = _owner.PairEvent(true);
				if (string.IsNullOrEmpty(token)) { why = "missing EnterEvent in PairActions.csv"; return false; }
				_owner.ForceCarrier(token);
				return true;
			}

			public void Tick(Agent agent, float dt) => _owner.TickCarrierMachine(dt);

			public bool IsFinished(Agent agent)
				=> _owner._machine == null
				|| _owner._machine.Current == AgentAnimStateMachine.OutsideState;

			public bool KeepsPairAlive => false;

			public void RequestExit(Agent agent)
			{
				string token = _owner.PairEvent(false);
				if (!string.IsNullOrEmpty(token)) _owner.ForceCarrier(token);
			}

			public void Release(Agent agent) { }
		}

		/// <summary>被扛者一侧：bind.xml（状态机由 <see cref="GrappleBind"/> 自己 Tick）。</summary>
		private sealed class BindMachineDriver : IPairSideDriver
		{
			private readonly GrappleCarry _owner;

			public BindMachineDriver(GrappleCarry owner) { _owner = owner; }

			public bool CanStart(Agent agent, out string why)
			{
				bool ok = _owner._bind != null && _owner._bind.IsActive;
				why = ok ? "-" : "bind is not active";
				return ok;
			}

			public bool Start(Agent agent, out string why)
			{
				why = "-";
				string token = _owner.PairEvent(true);
				if (string.IsNullOrEmpty(token)) { why = "missing EnterEvent in PairActions.csv"; return false; }
				_owner._bind?.ForceEvent(token);
				return true;
			}

			// bind.xml 的 Tick 归 GrappleBind，pair 不重复推进。
			public void Tick(Agent agent, float dt) { }

			// 放下的收尾（被放下 → 站缚）由 GrappleBind 继续；pair 只等扛人者那侧出机。
			public bool IsFinished(Agent agent) => true;

			public bool KeepsPairAlive => false;

			public void RequestExit(Agent agent)
			{
				string token = _owner.PairEvent(false);
				if (!string.IsNullOrEmpty(token)) _owner._bind?.ForceEvent(token);
			}

			public void Release(Agent agent) { }
		}

		// ───────────────────────────── 诊断 ─────────────────────────────

		/// <summary>一行状态（命令 `custom.grapple carry state`）。**纯英文**（控制台纪律）。</summary>
		public string StatusLine()
		{
			if (!IsActive)
			{
				return $"carry: idle (switch {(Enabled ? "on" : "off")}) | last: {_note}";
			}
			string pairState = _pair != null ? _pair.Phase.ToString() : "-";
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
			string horseNote = Horse != null
				? $" onHorse={SafeName(Horse)} (back {HorseBackOffset:F2}/side {HorseSideOffset:F2}/{HorseFaceMode})"
				: "";
			return $"carry: {_phase} | pair={pairState} | carrier={SafeName(_carrier)} ({carState}/{carAct}){horseNote}"
				+ $" | carried={SafeName(_carried)} act={carriedAct}"
				+ $" | dist={d:F2}m moving={(IsCarrierMoving() ? "True" : "False")}"
				+ $" | offset={OffsetForward:F2}/{OffsetRight:F2}"
				+ $" | sheathe={(SheatheOnCarry ? "on" : "off")} weStowed={_weSheathed} grace={_stowGrace:F1}s"
				+ $" | t={_t:F1}s";
		}

		private static string Fmt(Vec3 v) => $"({v.x:F2},{v.y:F2})";

		private static string SafeName(Agent a)
		{
			try { return a?.Name?.ToString() ?? "?"; }
			catch (Exception) { return "?"; }
		}
	}
}
