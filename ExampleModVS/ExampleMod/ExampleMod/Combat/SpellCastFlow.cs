using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 施法唯一入口 —— 瞄准 → 投送 → 结算串起来。
	///
	/// 🔴 **不关心施法者是谁**（铁律 18 玩家/NPC 平权）：入口就是
	///   <c>(施法者 Agent, 法术 id, 起点, 方向)</c>，玩家与 NPC 走的是同一个函数。
	///   · 阶段 1 的玩家壳 = <see cref="SpellSealFirePatch"/>（拦下法印开火，调本函数）
	///   · 阶段 4 的 NPC 壳 = 脑驱动的动作队列（同样调本函数，判定与结算一行不改）
	///
	/// 🔴 **起手必须走函数调用、不走引擎事件**（计划 §5.2）：阶段 3 甩掉弩脚手架时，
	///   只换调用者，投送与结算那一层**一行都不用动**。
	///
	/// 一次施法的时序（计划 §4.2）：
	///   ① 瞄准轴产出一张"施法意图"表（契约 1；阶段 1 恒 1 条）
	///   ② 逐条意图建一个投送物（契约 2：Begin 之后由 <see cref="SpellProjectileLogic"/> 每帧 Tick）
	///   ③ 结算实现挂到那一发上（契约 3：结算只认"收到一次命中"，节流归投送）
	/// </summary>
	public static class SpellCastFlow
	{
		/// <summary>意图表缓冲（复用，不在热路径 new）。</summary>
		private static readonly List<SpellCastIntent> _intents = new List<SpellCastIntent>();

		/// <summary>已记过日志的失败原因（防刷屏）。</summary>
		private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>按法术 id 施放（诊断/脚本用）。返回 false = 这次施法没发生。</summary>
		public static bool Cast(Agent caster, string spellId, Vec3 origin, Vec3 direction)
		{
			SpellDef spell = SpellRegistry.FindById(spellId);
			if (spell == null)
			{
				WarnOnce("id:" + spellId, $"法术 id '{spellId}' 不在表里");
				return false;
			}
			return Cast(caster, spell, origin, direction);
		}

		/// <summary>施放一次法术。返回 false = 没有产生任何投送物（数据缺件 / 并发已满 / 宿主不在场）。</summary>
		public static bool Cast(Agent caster, SpellDef spell, Vec3 origin, Vec3 direction)
		{
			return Cast(caster, spell, origin, direction, 1f);
		}

		/// <summary>
		/// 施放一次法术，带上**蓄力档位**（<paramref name="power"/> 0~1，阶段 3）：
		/// 起手轴算出来的力度会一路带到结算（伤害按 <c>1 + charge_bonus × power</c> 放大）。
		/// </summary>
		public static bool Cast(Agent caster, SpellDef spell, Vec3 origin, Vec3 direction, float power)
		{
			return Cast(caster, spell, origin, direction, power, out _);
		}

		/// <summary>
		/// 同上，但把**第一个投送物**交回给调用方 —— 引导（channel）要靠它才能在松手时把法术停掉。
		/// </summary>
		public static SpellShot CastAndReturn(Agent caster, SpellDef spell, Vec3 origin, Vec3 direction, float power)
		{
			SpellShot first;
			return Cast(caster, spell, origin, direction, power, out first) ? first : null;
		}

		private static bool Cast(Agent caster, SpellDef spell, Vec3 origin, Vec3 direction, float power,
			out SpellShot firstShot)
		{
			firstShot = null;
			if (spell == null)
			{
				return false;
			}
			if (caster == null || !AgentControlHelper.SafeIsActive(caster))
			{
				return false;
			}
			Mission mission = Mission.Current;
			if (mission == null)
			{
				return false;
			}
			SpellProjectileLogic host = mission.GetMissionBehavior<SpellProjectileLogic>();
			if (host == null)
			{
				// 宿主不在场（自定义战斗 / 主菜单试玩等没挂行为的地方）→ 什么都不做，
				// 让调用者（法印补丁）退回引擎原路。
				WarnOnce("host", "本场景没有挂 SpellProjectileLogic —— 法术不生效");
				return false;
			}

			// 🔴 方向取证（玩家自己施法时每次一行）：见 SpellWorld.DescribeAimSources 的字段说明
			if (mission.MainAgent == caster)
			{
				DebugLogger.Log($"[Spell] 方向对比（{spell.Id}）：{SpellWorld.DescribeAimSources(caster, direction)}");
			}

			// ① 瞄准轴：产出一张意图表（阶段 1 恒 1 条）
			ISpellTargeting targeting = SpellTargetingRegistry.Get(spell.Targeting);
			if (targeting == null)
			{
				WarnOnce("targeting:" + spell.Targeting,
					$"法术 '{spell.Id}' 的瞄准实现 '{spell.Targeting}' 还没做 → 不施放");
				return false;
			}
			_intents.Clear();
			try
			{
				targeting.BuildIntents(new SpellCastRequest
				{
					Caster = caster,
					Spell = spell,
					Origin = origin,
					Direction = direction,
					Power = power,
				}, _intents);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 瞄准 '{spell.Targeting}' 异常：{ex.GetType().Name} {ex.Message}");
				return false;
			}
			if (_intents.Count == 0)
			{
				return false;
			}

			// ② 投送轴：每条意图建一个投送物
			ISpellDelivery delivery = SpellDeliveryRegistry.Get(spell.Delivery);
			if (delivery == null)
			{
				WarnOnce("delivery:" + spell.Delivery,
					$"法术 '{spell.Id}' 的投送实现 '{spell.Delivery}' 还没做 → 不施放");
				return false;
			}

			int started = 0;
			for (int i = 0; i < _intents.Count; i++)
			{
				SpellShot shot = StartShot(host, delivery, _intents[i], spell);
				if (shot != null)
				{
					started++;
					if (firstShot == null)
					{
						firstShot = shot;
					}
				}
			}

			if (started > 0)
			{
				// 起手的表现只播一次（多发法术不是"放 N 次"）；一发都没起来就什么都不播
				SpellWorld.PlaySound(spell.SoundRelease, origin);
				DebugLogger.Log($"[Spell] 施放 '{spell.Id}'（{caster.Name}）意图 {_intents.Count} 条 / 起飞 {started} 条"
					+ $" · 起点 {Fmt(origin)} · 弹速 {spell.Speed:F0} · 在飞 {host.LiveCount}");
			}
			return started > 0;
		}

		/// <summary>把一条意图变成一发在飞的投送物（挂上结算实现、交给宿主）。返回 null = 这一发没起来。</summary>
		private static SpellShot StartShot(SpellProjectileLogic host, ISpellDelivery delivery,
			SpellCastIntent intent, SpellDef spell)
		{
			SpellShot shot = new SpellShot(intent);

			// ③ 结算轴：同一个命中事件，每个实现都收到一次
			for (int p = 0; p < spell.Payloads.Count; p++)
			{
				ISpellPayload payload = SpellPayloadRegistry.Get(spell.Payloads[p]);
				if (payload == null)
				{
					WarnOnce("payload:" + spell.Payloads[p],
						$"法术 '{spell.Id}' 的结算实现 '{spell.Payloads[p]}' 还没做 → 这一项不生效");
					continue;
				}
				shot.Payloads.Add(payload);
			}

			ISpellDeliveryInstance instance;
			try
			{
				instance = delivery.Begin(shot);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 投送 '{spell.Delivery}' 进入异常：{ex.GetType().Name} {ex.Message}");
				return null;
			}
			if (instance == null)
			{
				return null;
			}
			shot.Delivery = instance;
			if (!host.TryAddShot(shot))
			{
				// 并发已满：宁可拒绝，也不要"加了再说"（计划 §4.4 纪律 5）
				try
				{
					instance.End();
				}
				catch (Exception)
				{
					// 回收失败不改变结论
				}
				return null;
			}
			return shot;
		}

		private static string Fmt(Vec3 v)
		{
			return string.Format(System.Globalization.CultureInfo.InvariantCulture,
				"({0:F1},{1:F1},{2:F1})", v.x, v.y, v.z);
		}

		private static void WarnOnce(string key, string message)
		{
			if (_warned.Add(key))
			{
				DebugLogger.Log($"[Spell] {message}");
			}
		}
	}
}
