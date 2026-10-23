using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **NPC 出手的一次「租约」**（2026-10-23 立；方案 = `plans/钩索-实施计划.md` §二十五）。
	///
	/// 做什么：把一个 NPC 临时变成"玩家那样"的钩索使用者——
	///   ① 确认它**实际带着**绳 + 钩两件（同玩家的物品门槛）；
	///   ② 保存原武器 / 动作集 / AI 暂停状态；
	///   ③ 握绳、必要时切 `as_human_warrior`（村民等播不了钩索动作）；
	///   ④ 播 `ready → hold → release`，进入 release 的同一帧从**这只手**放钩；
	///   ⑤ 整套钩索结束（放人 / 落地 / 终止）时还原武器、动作集、AI。
	///
	/// 🔴 道具与动作和玩家同一套资产、同一套动作名 —— 这里不新建任何资产。
	/// 🔴 相机、UI、风噪一律不碰（用户裁定：NPC 平权，除 UI/镜头/性能）。
	/// </summary>
	internal sealed class GrappleNpcKit
	{
		/// <summary>ready 播多久再进 hold（秒）。和玩家"拉弓"那一下对齐，略短。</summary>
		public static float ReadySeconds = 0.45f;

		/// <summary>hold 播多久再 release（秒）。玩家可以一直瞄；NPC 是脚本出手，给个短停顿。</summary>
		public static float HoldSeconds = 0.35f;

		/// <summary>release 播多久算这一段演完（秒；与玩家的 `ReleaseSeconds` 同源）。</summary>
		public static float ReleaseSeconds = 0.566f;

		/// <summary>目标超过这个距离就先走过去（原版 AI 用远程武器前也会先贴近）。</summary>
		public static float ApproachDistance = 6f;

		/// <summary>走近超时（秒）；超时后若还在 20 m 内就硬出手，否则放弃。</summary>
		public static float ApproachTimeoutSeconds = 8f;

		/// <summary>走近期间重发移动指令的间隔（秒；目标会动）。</summary>
		public static float ApproachReissueSeconds = 0.4f;

		private enum Phase { None, Approach, Ready, Hold, Release, Done }

		private Agent _owner;
		private GrappleLogic _session;
		private Vec3 _aim;
		private Agent _aimTarget;
		private bool _aimAgent;
		private bool _autoPull;
		private Phase _phase;
		private float _timer;
		private bool _lease;
		private EquipmentIndex _ropeSlot;
		private float _approachReissue;
		private bool _sequenceStarted;

		private EquipmentIndex _savedMain;
		private EquipmentIndex _savedOff;
		private MBActionSet _savedSet;
		private bool _haveSavedSet;
		private Agent.AIScriptedFrameFlags _savedFlags;
		private bool _savedPaused;
		private bool _pausedByUs;
		private bool _savedWasAI = true;
		private bool _controllerChanged;

		/// <summary>非战斗时的脑侧占位（战斗里脑不跑，不建）。</summary>
		private GrappleUseAction _brainAction;

		/// <summary>这套租约还活着吗（武器/动作集/AI 还没还原）。</summary>
		public bool Active => _lease;

		/// <summary>开始一次出手。返回 false + why = 缺件/改不了动作集，什么都没动。</summary>
		public bool BeginThrow(Agent owner, GrappleLogic session, Vec3 aim, bool aimAgent, bool autoPull,
			Agent aimTarget, out string why)
		{
			why = "-";
			if (owner == null || session == null) { why = "no owner/session"; return false; }
			if (!AgentControlHelper.SafeIsActive(owner)) { why = "owner inactive"; return false; }
			if (owner.Character == null || owner.Monster == null) { why = "owner has no character/monster"; return false; }

			EquipmentIndex ropeSlot = GrappleFirePatch.FindItemSlot(owner, GrappleFirePatch.RopeItemId);
			if (ropeSlot == EquipmentIndex.None)
			{
				why = "no rope equipped (give the NPC the grapple items first: 'custom.grapple npc equip')";
				return false;
			}
			if (!GrappleFirePatch.HasHookEquipped(owner))
			{
				why = "no hook equipped (give the NPC the grapple items first: 'custom.grapple npc equip')";
				return false;
			}

			if (_lease) EndLease("restart");

			_owner = owner;
			_session = session;
			_aim = aim;
			_aimTarget = aimTarget;
			_aimAgent = aimAgent;
			_autoPull = autoPull;

			// 原始旗标在**走近之前**就存下来（走近期间 ScriptedMoveToAgent 会改旗标，不能拿它当原值）
			try { _savedFlags = owner.GetScriptedFlags(); } catch (Exception) { _savedFlags = 0; }

			// 0. 打断坐椅子/对话之类的交互（否则引擎每帧会覆盖我们的动作通道）
			try
			{
				if (owner.IsUsingGameObject)
					owner.StopUsingGameObject(true, Agent.StopUsingGameObjectFlags.None);
			}
			catch (Exception) { }

			_ropeSlot = ropeSlot;
			_sequenceStarted = false;
			_controllerChanged = false;
			_pausedByUs = false;

			float dist = 0f;
			if (aimTarget != null && AgentControlHelper.SafeIsActive(aimTarget))
			{
				try { dist = owner.Position.Distance(aimTarget.Position); } catch (Exception) { }
			}
			bool needApproach = aimTarget != null && dist > ApproachDistance;

			bool battle = false;
			try { battle = Settings.Instance != null && Settings.Instance.IsInteractionDisabled(); }
			catch (Exception) { }

			// ⑤ **非战斗：脑侧占位 + 第一人称旁白**（战斗里脑整体早退，由 Mission tick 全权驱动）
			if (!battle)
			{
				try
				{
					AgentBrain brain = AgentAIController.GetBrainForAgent(owner);
					if (brain != null)
					{
						string targetName = aimTarget != null ? aimTarget.Name?.ToString() : null;
						string narration = !string.IsNullOrEmpty(targetName)
							? (needApproach ? $"我逼近 {targetName}，准备用钩索" : $"我朝 {targetName} 甩出了钩索")
							: "我用钩索勾住了前面的高处";
						_brainAction = new GrappleUseAction(narration);
						brain.RunReactiveAction(_brainAction);
						DebugLogger.Log($"[GrappleNPC] 脑侧占位：{narration}");
					}
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[GrappleNPC] 脑侧占位失败（{ex.GetType().Name}）—— 继续（身体归租约）");
				}
			}

			_lease = true;

			// ⑥ 太远 ⇒ 先走过去（原版 AI 用远程武器前也会先贴近目标；脑侧队列已被占位）
			if (needApproach)
			{
				_phase = Phase.Approach;
				_timer = 0f;
				_approachReissue = 0f;
				try { AgentControlHelper.ScriptedMoveToAgent(owner, aimTarget, isRun: dist > 12f); } catch (Exception) { }
				DebugLogger.Log($"[GrappleNPC] '{owner.Name}' 先靠近 '{aimTarget.Name}'（{dist:F1}m → {ApproachDistance:F1}m 内再出手）");
				return true;
			}

			return StartThrowSequence(out why);
		}

		/// <summary>走近到距离内（或超时）⇒ 接管身体、切动作集、播 ready→hold→release。</summary>
		private bool StartThrowSequence(out string why)
		{
			why = "-";
			Agent owner = _owner;
			GrappleLogic session = _session;
			if (owner == null || session == null)
			{
				why = "no owner/session";
				EndLease("no owner");
				return false;
			}

			// 接近之后再保存当前武器/动作集/旗标（走近期间他可能换过武器）
			try { _savedMain = V.MainWpn(owner); } catch (Exception) { _savedMain = EquipmentIndex.None; }
			try { _savedOff = V.OffWpn(owner); } catch (Exception) { _savedOff = EquipmentIndex.None; }
			try { _savedSet = owner.ActionSet; _haveSavedSet = _savedSet.IsValid; }
			catch (Exception) { _haveSavedSet = false; }
			try { _savedPaused = owner.IsPaused; } catch (Exception) { _savedPaused = false; }
			try { _savedWasAI = V.IsAgentAI(owner); } catch (Exception) { _savedWasAI = true; }

			try { AgentControlHelper.StopMovement(owner); } catch (Exception) { }

			// ① 摘控制器：出手这一两秒里**引擎 AI 不许再写武器/动作输入**
			//    （城镇里不冻 AI，光压旗标挡不住 HumanAIComponent 换武器）。
			try
			{
				V.SetAgentControllerNone(owner);
				_controllerChanged = true;
			}
			catch (Exception) { _controllerChanged = false; }

			// ② 动作集：钩索的动作名只在 `as_human_warrior` 里
			try
			{
				string setName = _haveSavedSet ? _savedSet.GetName() : null;
				if (setName != "as_human_warrior")
				{
					MBActionSet warrior = MBActionSet.GetActionSet("as_human_warrior");
					if (warrior.IsValid)
					{
						AnimationSystemData data = owner.Monster.FillAnimationSystemData(
							warrior, owner.Character.GetStepSize(), hasClippingPlane: false);
						owner.SetActionSet(ref data);
					}
				}
			}
			catch (Exception ex)
			{
				why = "action set switch failed: " + ex.Message;
				// 半途失败：把已经动过的动作集还原，别把人家留在 warrior 集上。
				try
				{
					if (_haveSavedSet && AgentControlHelper.SafeIsActive(owner))
					{
						AnimationSystemData data = owner.Monster.FillAnimationSystemData(
							_savedSet, owner.Character.GetStepSize(), hasClippingPlane: false);
						owner.SetActionSet(ref data);
					}
				}
				catch (Exception) { }
				// 控制器也要还（它在动作集之前就被摘掉了）
				if (_controllerChanged && AgentControlHelper.SafeIsActive(owner))
				{
					try
					{
						if (_savedWasAI) V.SetAgentAI(owner);
						else V.SetAgentControllerNone(owner);
					}
					catch (Exception) { }
					_controllerChanged = false;
				}
				EndLease("action set failed");
				return false;
			}

			// ③ 握绳（道具门槛已在上面查过）
			try { owner.TryToWieldWeaponInSlot(_ropeSlot, Agent.WeaponWieldActionType.Instant, false); }
			catch (Exception ex) { DebugLogger.Log($"[GrappleNPC] 握绳失败（{ex.GetType().Name}）—— 继续播动作"); }

			// ④ 按住他：别一边出手一边走位/挥刀
			try { AgentControlHelper.HoldInPlace(owner); } catch (Exception) { }
			try { AgentControlHelper.SuspendNavigator(owner); } catch (Exception) { }
			try
			{
				owner.SetScriptedFlags(_savedFlags
					| Agent.AIScriptedFrameFlags.DoNotRun
					| Agent.AIScriptedFrameFlags.NoAttack
					| Agent.AIScriptedFrameFlags.InConversation);
			}
			catch (Exception) { }

			// ⑤ 战场才冻 AI（城镇冻 AI 的坑见 GrappleBind.ShouldPauseAI）
			bool battle = false;
			try { battle = Settings.Instance != null && Settings.Instance.IsInteractionDisabled(); }
			catch (Exception) { }
			if (battle && !_savedPaused)
			{
				try { owner.SetIsAIPaused(true); _pausedByUs = true; } catch (Exception) { }
			}

			_sequenceStarted = true;
			_phase = Phase.Ready;
			_timer = 0f;
			FaceAim();                       // 站定先转身（之后 Ready/Hold 每帧继续跟）
			Play("act_grapple_ground_ready");
			session.NpcHandKitEnabled = true;

			DebugLogger.Log($"[GrappleNPC] 出手：'{owner.Name}' 物品齐（绳+钩）⇒ ready → hold → release");
			return true;
		}

		/// <summary>靠近目标到 <see cref="ApproachDistance"/> 内（超时也允许在 20 m 内硬出手）。</summary>
		private void ApproachTick(float dt)
		{
			_timer += dt;
			Agent target = _aimTarget;
			if (target == null || !AgentControlHelper.SafeIsActive(target))
			{
				EndLease("target gone");
				return;
			}
			float dist = 0f;
			try { dist = _owner.Position.Distance(target.Position); } catch (Exception) { }
			if (dist <= ApproachDistance)
			{
				StartThrowSequence(out _);
				return;
			}
			if (_timer >= ApproachTimeoutSeconds)
			{
				if (dist <= 20f) StartThrowSequence(out _);
				else EndLease("approach timeout");
				return;
			}
			// 走近期间不强行写朝向（会和脚本寻路打架）；站定出手时再转身。
			_approachReissue -= dt;
			if (_approachReissue <= 0f)
			{
				_approachReissue = ApproachReissueSeconds;
				try { AgentControlHelper.ScriptedMoveToAgent(_owner, target, isRun: dist > 12f); } catch (Exception) { }
			}
		}

		/// <summary>每帧推进出手动作；到 release 那一刻把钩放出去。</summary>
		public void Tick(float dt)
		{
			if (!_lease || _owner == null || _session == null) return;
			if (!AgentControlHelper.SafeIsActive(_owner))
			{
				EndLease("owner gone");
				return;
			}

			if (_phase == Phase.Approach)
			{
				ApproachTick(dt);
				return;
			}

			_timer += dt;
			if (_phase == Phase.Ready || _phase == Phase.Hold || _phase == Phase.Release)
			{
				FaceAim();
			}
			switch (_phase)
			{
				case Phase.Ready:
					if (_timer >= ReadySeconds)
					{
						_phase = Phase.Hold;
						_timer = 0f;
						Play("act_grapple_ground_hold");
					}
					break;

				case Phase.Hold:
					if (_timer >= HoldSeconds)
					{
						_phase = Phase.Release;
						_timer = 0f;
						Play("act_grapple_ground_release");
						// 目标在 0.8 秒的 ready/hold 里可能动了：放钩这一拍重新取一次胸口。
						Vec3 aim = _aim;
						if (_aimTarget != null && AgentControlHelper.SafeIsActive(_aimTarget))
						{
							aim = GrappleBind.ChestOf(_aimTarget);
						}
						string r = _session.ThrowAtForOwner(aim, _aimAgent, _autoPull);
						DebugLogger.Log($"[GrappleNPC] '{_owner.Name}' 放钩：{r}");
						if (r != null && r.StartsWith("Error", StringComparison.Ordinal))
						{
							EndLease("throw failed: " + r);
						}
					}
					break;

				case Phase.Release:
					if (_timer >= ReleaseSeconds)
					{
						_phase = Phase.Done;      // 动作演完；租约继续（绳/动作集/道具要等整套结束才还）
					}
					break;
			}
		}

		/// <summary>
		/// **出手前先转身面向目标**（2026-10-23 用户要求："背对着我放有点离谱"）。
		/// 原版 AI 用远程武器前也会先朝目标；我们是脚本出手，得自己补这一拍。
		/// 只转水平朝向（z 清零）；目标每帧重新取，wall 点则用固定点。
		/// </summary>
		private void FaceAim()
		{
			if (_owner == null) return;
			try
			{
				Vec3 aim = _aim;
				if (_aimTarget != null && AgentControlHelper.SafeIsActive(_aimTarget))
				{
					aim = GrappleBind.ChestOf(_aimTarget);
				}
				Vec3 d = aim - _owner.Position;
				d.z = 0f;
				if (d.LengthSquared < 1e-4f) return;
				Vec2 dir = new Vec2(d.x, d.y);
				dir = dir.Normalized();
				_owner.SetMovementDirection(dir);
				_owner.LookDirection = new Vec3(dir.x, dir.y, 0f);
			}
			catch (Exception) { }
		}

		/// <summary>结束租约：还原武器 / 动作集 / AI / 旗标，并收起手部道具。幂等。</summary>
		public void EndLease(string reason)
		{
			if (!_lease) return;
			_lease = false;
			_phase = Phase.None;

			Agent owner = _owner;
			if (owner != null && AgentControlHelper.SafeIsActive(owner))
			{
				// 只有真正进过出手段才需要还武器/动作集； Approach 阶段这些都没动过。
				if (_sequenceStarted)
				{
					try
					{
						if (_savedMain != EquipmentIndex.None)
						owner.TryToWieldWeaponInSlot(_savedMain, Agent.WeaponWieldActionType.Instant, false);
					else
						owner.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
				}
				catch (Exception) { }

				try
				{
					if (_savedOff != EquipmentIndex.None)
						owner.TryToWieldWeaponInSlot(_savedOff, Agent.WeaponWieldActionType.Instant, false);
					else
						owner.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
				}
				catch (Exception) { }

				// 动作集放在武器之后还原
				try
				{
					if (_haveSavedSet)
					{
						AnimationSystemData data = owner.Monster.FillAnimationSystemData(
							_savedSet, owner.Character.GetStepSize(), hasClippingPlane: false);
						owner.SetActionSet(ref data);
					}
				}
				catch (Exception) { }

					_sequenceStarted = false;
				}

				try { owner.SetScriptedFlags(_savedFlags); } catch (Exception) { }
				if (_controllerChanged)
				{
					try
					{
						if (_savedWasAI) V.SetAgentAI(owner);
						else V.SetAgentControllerNone(owner);
					}
					catch (Exception) { }
					_controllerChanged = false;
				}
				if (_pausedByUs)
				{
					try { owner.SetIsAIPaused(_savedPaused); } catch (Exception) { }
				}
				try { AgentControlHelper.StopMovement(owner); } catch (Exception) { }
				// 出手期间按住过他：解冻后必须把导航器 + 限速还回去，否则他会一直站着。
				try { AgentControlHelper.ResumeNavigator(owner); } catch (Exception) { }
				try { owner.SetMaximumSpeedLimit(-1f, false); } catch (Exception) { }
			}

			GrappleLogic session = _session;
			if (session != null)
			{
				session.NpcHandKitEnabled = false;
				try { session.HideNpcHandKitVisuals(); } catch (Exception) { }
			}

			// 脑侧占位：置完成，脑下一拍自己收尾（不在战斗里，脑是活的）。
			if (_brainAction != null)
			{
				_brainAction.Done = true;
				_brainAction = null;
			}

			DebugLogger.Log($"[GrappleNPC] 收摊（{reason}）：武器 / 动作集 / AI 已还原");
			_owner = null;
			_session = null;
			_aimTarget = null;
			_pausedByUs = false;
		}

		private void Play(string action)
		{
			if (_owner == null || string.IsNullOrEmpty(action)) return;
			try
			{
				ActionIndexCache idx = ActionIndexCache.Create(action);
				if (idx == ActionIndexCache.act_none)
				{
					DebugLogger.Log($"[GrappleNPC] 动作 '{action}' 解析不到（动作集里没有？）—— 这一段不会有动画");
					return;
				}
				_owner.SetActionChannel(0, idx, ignorePriority: true,
					blendInPeriod: 0.12f, blendOutPeriodToNoAnim: 0.3f, startProgress: 0f);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[GrappleNPC] 播 '{action}' 失败（{ex.GetType().Name}）—— 继续");
			}
		}
	}
}
