using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **有效定义的解算**（阶段 5 的核心，计划 §16.2）：
	/// <code>有效法术 = 基础术 ⊕ 修正₁ ⊕ … ⊕ 修正ₙ</code>
	///
	/// 🔴 **解算结果仍然是同一个 <see cref="SpellDef"/> 类型** ⇒ 四段管线（瞄准/投送/结算）一行不用改 ——
	///   这正是"修正不是功能、是对属性的操作"那句话的落地方式。
	/// 🔴 **修正只改行为、不改身份**：法术 id / 族 / 三轴 / 弹药物品不在属性表里（§16.1）。
	/// 🔴 **套娃限深**：子块解算到 <see cref="SpellDef.TriggerDepth"/> 层就**不再给它挂触发器**
	///   （数据写大了也不怕 —— 再夹一道 <see cref="HardDepthCap"/>）。
	/// 🔴 零修正 = **零开销**：没有宝石时原样返回表里那一条（连复制都不做）。
	/// </summary>
	public static class SpellResolver
	{
		/// <summary>硬上限（数据里的 trigger_depth 再大也不会超过它 —— 防递归爆栈）。</summary>
		public const int HardDepthCap = 8;

		/// <summary>解算失败的原因（每个宝石 id 只记一次，防刷屏）。</summary>
		private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>
		/// 解算：把 <paramref name="gems"/> 里的修正依次落到基础术的副本上。
		/// </summary>
		/// <param name="trace">
		/// 非 null 时逐条记录"哪颗宝石把哪个字段改成了什么"（<c>custom.spell build</c> 靠它打印中间结果）。
		/// </param>
		public static SpellDef Resolve(SpellDef baseDef, IList<string> gems, int depth, List<string> trace)
		{
			if (baseDef == null)
			{
				return null;
			}
			if (gems == null || gems.Count == 0)
			{
				return baseDef;   // 零修正：原样返回（共享定义，谁都别就地改它）
			}

			int maxDepth = baseDef.TriggerDepth > 0 ? baseDef.TriggerDepth : 3;
			if (maxDepth > HardDepthCap)
			{
				maxDepth = HardDepthCap;
			}
			bool allowTriggers = depth < maxDepth;

			SpellDef def = baseDef.Clone();
			def.Resolved = true;
			for (int i = 0; i < gems.Count; i++)
			{
				string gemId = gems[i];
				if (string.IsNullOrEmpty(gemId))
				{
					continue;
				}
				ModifierDef gem = ModifierRegistry.Find(gemId);
				if (gem == null)
				{
					if (_warned.Add("gem:" + gemId))
					{
						DebugLogger.Log($"[Spell] 宝石 '{gemId}' 不在宝石表里 → 这一颗不生效"
							+ "（检查内容包 ModuleData/AssetRegistry/Modifiers.xml）");
					}
					if (trace != null)
					{
						trace.Add($"{gemId}: NOT FOUND (skipped)");
					}
					continue;
				}

				for (int k = 0; k < gem.Ops.Count; k++)
				{
					ModifierOp op = gem.Ops[k];
					if (!allowTriggers && IsTriggerField(op.Field))
					{
						if (trace != null)
						{
							trace.Add($"{gemId}: {op.Raw} SKIPPED (trigger depth {depth} >= {maxDepth})");
						}
						continue;
					}
					string before = FieldText(def, op.Field);
					string error;
					bool ok = SpellFields.Apply(def, op, out error);
					if (trace == null)
					{
						continue;
					}
					if (ok)
					{
						trace.Add($"{op.Raw} ({gemId})   [{before} -> {FieldText(def, op.Field)}]");
					}
					else
					{
						trace.Add($"{gemId}: {op.Raw} FAILED ({error})");
					}
				}

				if (!allowTriggers)
				{
					continue;
				}
				for (int k = 0; k < gem.Subs.Count; k++)
				{
					ModifierSubDef sub = gem.Subs[k];
					SpellTriggerPayload payload = BuildPayload(sub, depth + 1, gemId);
					if (payload != null)
					{
						def.Triggers[sub.Id] = payload;
						if (trace != null)
						{
							trace.Add($"{gemId}: sub '{sub.Id}' -> spell '{payload.Spell.Id}' (depth {payload.Depth})");
						}
					}
				}
			}
			return def;
		}

		/// <summary>逗号/空格分隔的宝石串（命令与存档里的写法）→ 解算。</summary>
		public static SpellDef ResolveCsv(SpellDef baseDef, string csv, int depth, List<string> trace)
		{
			if (string.IsNullOrEmpty(csv))
			{
				return baseDef;
			}
			List<string> gems = new List<string>();
			foreach (string piece in csv.Split(new[] { ',', ' ', '|', '\t' }, StringSplitOptions.RemoveEmptyEntries))
			{
				gems.Add(piece.Trim());
			}
			return Resolve(baseDef, gems, depth, trace);
		}

		/// <summary>
		/// 分裂出来的子体**不再分裂**（`split_at` 清零）。
		/// 🔴 防的是"一次分裂变两发、两发各自又分裂"的指数增长；其余修正一律保留（与 Noita 一致）。
		/// </summary>
		public static SpellDef StripSplit(SpellDef def)
		{
			if (def == null || def.SplitAt <= 0f)
			{
				return def;
			}
			SpellDef copy = def.Clone();
			copy.SplitAt = 0f;
			copy.SplitCount = 0;
			return copy;
		}

		private static SpellTriggerPayload BuildPayload(ModifierSubDef sub, int depth, string gemId)
		{
			SpellDef childBase = SpellRegistry.FindById(sub.BaseSpell);
			if (childBase == null)
			{
				if (_warned.Add("sub:" + gemId + ":" + sub.Id))
				{
					DebugLogger.Log($"[Spell] 宝石 '{gemId}' 的子块 '{sub.Id}' 引的法术 '{sub.BaseSpell}' 不在法术表里"
						+ " → 这个触发器不会响（检查 <Base spell=\"…\" />）");
				}
				return null;
			}
			SpellDef child = Resolve(childBase, sub.Gems, depth, null);
			// 🔴 子块**独立解算、不继承外层配装**（§16.4）：就算它一颗宝石都没有，
			//    也要交出去一份"已解算"的定义，否则施放时会被当成基础术再叠一遍外层宝石。
			if (!child.Resolved)
			{
				child = child.Clone();
				child.Resolved = true;
			}
			bool scaleDamage = Math.Abs(sub.ScaleDamage - 1f) > 1e-4f;
			bool scaleCount = sub.ScaleCount > 0;
			if (scaleDamage || scaleCount)
			{
				// child 上面已经保证是**我们自己的一份副本**（不是表里那条共享定义），可以放心就地改。
				if (scaleDamage)
				{
					child.Damage *= sub.ScaleDamage;
				}
				if (scaleCount)
				{
					child.Count = sub.ScaleCount;
				}
			}
			return new SpellTriggerPayload { Id = sub.Id, Spell = child, Depth = depth };
		}

		/// <summary>触发类字段（限深时要跳过它们）。</summary>
		private static bool IsTriggerField(string field)
		{
			return field == "on_hit_cast" || field == "on_timer_cast"
				|| field == "on_expire_cast" || field == "on_bounce_cast";
		}

		/// <summary>某个字段在定义里的当前值文本（写回执用；字段不在表里就返回 "-"）。</summary>
		private static string FieldText(SpellDef def, string field)
		{
			SpellField f = SpellFields.Get(field);
			return f == null ? "-" : f.Describe(def);
		}

		/// <summary>
		/// 打印"有效定义与基础术的差异"（<c>custom.spell build</c> 的正文）。
		/// 🔴 只列**变了的**字段 —— 属性表 70 多个字段全打印会把控制台刷爆。
		/// </summary>
		public static List<string> Diff(SpellDef baseDef, SpellDef effective)
		{
			List<string> changed = new List<string>();
			if (baseDef == null || effective == null)
			{
				return changed;
			}
			foreach (SpellField f in SpellFields.All)
			{
				string a = f.Describe(baseDef);
				string b = f.Describe(effective);
				if (!string.Equals(a, b, StringComparison.Ordinal))
				{
					changed.Add($"{f.Name}={b}(was {a})");
				}
			}
			// 顺序稳定（属性表是字典，打印顺序会飘）—— 便于肉眼比对与脚本抓取
			changed.Sort(StringComparer.Ordinal);
			return changed;
		}

		/// <summary>有效定义的一行摘要（诊断用）。</summary>
		public static string Summary(SpellDef def)
		{
			if (def == null)
			{
				return "none";
			}
			return string.Format(CultureInfo.InvariantCulture,
				"{0} {1}/{2}/{3} dmg={4:0.#} v={5:0.#} pierce={6} bounce={7} trig={8}",
				def.Id, def.Targeting, def.Delivery, string.Join("+", def.Payloads),
				def.Damage, def.Speed * def.SpeedScale, def.Pierce, def.BounceCount, def.Triggers.Count);
		}
	}
}
