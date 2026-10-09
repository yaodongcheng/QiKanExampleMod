using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using TaleWorlds.ModuleManager;

namespace LivingWorldNpcs
{
	// ═══════════════════════════════════════════════════════════════════════════
	// 修正宝石（阶段 5）—— **数据契约 + 读取器 + 属性表**
	//
	// 一句话：**修正不是功能，是对属性的一组增量** —— 解算成"有效定义"之后，四段管线一行不用改。
	//   有效法术 = 基础术 ⊕ 修正₁ ⊕ … ⊕ 修正ₙ（§16.2）
	// ⇒ 加一颗新宝石 = 在内容包 Modifiers.xml 加一行数据，**不改代码**；
	//   只有"要改一个还没进属性表的字段"才要动本文件（补属性，不是加特例）。
	//
	// 数据在哪：任何模块（内容包）的 ModuleData/AssetRegistry/Modifiers.xml：
	//   <Modifiers>
	//     <Loadout seal="lwn_spell_seal" slots="3" />             ← 施法手环的宝石槽位数
	//     <Modifier id="gem_damage" stage="payload" cost="2" name="{=LWN_gem_damage}Damage+">
	//       <Mul field="damage" value="1.5" />
	//       <Set field="trail_particle" value="lwn_fire_trail" />
	//       <Set field="on_hit_cast" sub="sub_fire" />            ← 触发器 = 引用下面那个子块
	//       <Sub id="sub_fire">                                   ← 载荷 = 一个"装配块"（基础术 + 它自己的宝石 + 缩放）
	//         <Base spell="skyfall_meteor" />
	//         <Gems>gem_damage gem_area</Gems>
	//         <Scale damage="0.5" count="1" />
	//       </Sub>
	//     </Modifier>
	//   </Modifiers>
	//
	// 🔴 **五种操作，只有这五种**（§16.1）：设定 `<Set>` · 增减 `<Add>` · 乘 `<Mul>` · 开关（也是 `<Set>`，值是 true/false）
	//   · 追加引用（也是 `<Set>`，值是 id）。**新增一条修正不许加代码分支** —— 做不到 = 属性表缺东西 ⇒ 补属性。
	// 🔴 **身份型属性不许被修正改**（id / 族 / 三轴 / 弹药物品）—— 修正改的是"行为"，不是"这是什么法术"。
	//
	// 与具体世界观无关（铁律 3）：本文件里没有任何宝石名、没有任何内容包词汇 —— 认的全是数据里的 id。
	// 容错（铁律 1）：文件缺失 / 字段写错 / 子块引不到法术 → 跳过那一项 + 一条日志，绝不抛异常。
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>五种操作里的三种"写"（开关与追加引用都走 <see cref="Set"/>，只是值的类型不同）。</summary>
	public enum SpellOpKind
	{
		Set,
		Add,
		Mul,
	}

	/// <summary>一条属性操作：改哪个字段、怎么改、改多少。</summary>
	public sealed class ModifierOp
	{
		public string Field;
		public SpellOpKind Kind;

		/// <summary>数值操作的值。</summary>
		public float Number;

		/// <summary>文本/引用操作的值（<c>value="…"</c>）。</summary>
		public string Text;

		/// <summary>触发类操作引用的**子块 id**（<c>sub="…"</c>）。</summary>
		public string SubId;

		/// <summary>原始写法（诊断打印用：<c>damage ×1.5</c>）。</summary>
		public string Raw;
	}

	/// <summary>
	/// 触发载荷 = **一个"装配块"**（§16.4）：基础术 + 它自己的修正 + 一个缩放系数。
	/// 🔴 不是一个法术 id —— 只填 id 会把"触发器里塞带修正的法术"这半个能力直接丢掉。
	/// 🔴 子块**不继承外层修正**（独立解算），要什么显式写在子块里。
	/// </summary>
	public sealed class ModifierSubDef
	{
		public string Id;
		public string BaseSpell;
		public readonly List<string> Gems = new List<string>();

		/// <summary>子法术的伤害缩放（1 = 不缩放）。</summary>
		public float ScaleDamage = 1f;

		/// <summary>子法术的发数覆盖（&lt;=0 = 不覆盖，用基础术自己的）。</summary>
		public int ScaleCount;
	}

	/// <summary>一颗宝石（一行数据）。</summary>
	public sealed class ModifierDef
	{
		public string Id;

		/// <summary>挂哪一段（装配界面的组织方式，§16.3）：<c>cast</c> / <c>aim</c> / <c>deliver</c> / <c>payload</c>。</summary>
		public string Stage;

		/// <summary>显示名（本地化串 <c>{=LWN_xxx}Fallback</c>，铁律 13）。</summary>
		public string Name;

		/// <summary>图标名（装配界面用；阶段 10）。</summary>
		public string Icon;

		/// <summary>说明文本（本地化串）。</summary>
		public string Desc;

		/// <summary>解锁凭证 = 必须拥有这个物品才能装（空 = 不做凭证检查）。</summary>
		public string ItemId;

		/// <summary>这颗宝石给法术加多少查克拉开销（阶段 6 读；现在只登记）。</summary>
		public float ChakraCostAdd;

		public readonly List<ModifierOp> Ops = new List<ModifierOp>();
		public readonly List<ModifierSubDef> Subs = new List<ModifierSubDef>();

		/// <summary>子块按 id 查（触发类操作解析 <c>sub="…"</c> 用）。</summary>
		public ModifierSubDef FindSub(string id)
		{
			for (int i = 0; i < Subs.Count; i++)
			{
				if (Subs[i].Id == id)
				{
					return Subs[i];
				}
			}
			return null;
		}
	}

	/// <summary>
	/// 宝石表读取器 —— 扫全部模块的 <c>ModuleData/AssetRegistry/Modifiers.xml</c>。
	/// 读取范式与 <see cref="SpellRegistry"/> 完全一致：懒加载、后加载覆盖先加载、缺项跳过不抛异常。
	/// </summary>
	public static class ModifierRegistry
	{
		private const string FileName = "Modifiers.xml";

		private static bool _loaded;
		private static readonly object _lock = new object();

		private static readonly Dictionary<string, ModifierDef> _byId =
			new Dictionary<string, ModifierDef>(StringComparer.Ordinal);

		/// <summary>按加载顺序排（诊断打印要稳定顺序；字典顺序不保证）。</summary>
		private static readonly List<ModifierDef> _ordered = new List<ModifierDef>();

		/// <summary>手环物品 id → 槽位数（&lt;Loadout&gt; 行）。查不到 = 默认 <see cref="DefaultSlots"/>。</summary>
		private static readonly Dictionary<string, int> _sealSlots =
			new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>没有 &lt;Loadout&gt; 行时手环自带的槽位数（数据没铺开时也能玩：一颗宝石也是宝石）。</summary>
		public const int DefaultSlots = 1;

		/// <summary>内容包给手环铺的槽位总表（诊断用）。</summary>
		public static IEnumerable<KeyValuePair<string, int>> SealSlots
		{
			get { EnsureLoaded(); return _sealSlots; }
		}

		public static int Count
		{
			get { EnsureLoaded(); return _byId.Count; }
		}

		public static IEnumerable<ModifierDef> All
		{
			get { EnsureLoaded(); return _ordered; }
		}

		public static ModifierDef Find(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}
			EnsureLoaded();
			ModifierDef def;
			return _byId.TryGetValue(id, out def) ? def : null;
		}

		/// <summary>手环（施法印记物品）自带几个槽位。空 id / 没登记 = <see cref="DefaultSlots"/>。</summary>
		public static int SlotsForSeal(string sealItemId)
		{
			EnsureLoaded();
			int slots;
			if (!string.IsNullOrEmpty(sealItemId) && _sealSlots.TryGetValue(sealItemId, out slots))
			{
				return slots;
			}
			return DefaultSlots;
		}

		/// <summary>
		/// **重读宝石表**（每次进场景调一次）—— 改 <c>Modifiers.xml</c> **不用重启游戏**。
		/// 与 <see cref="SpellRegistry.Reload"/> 同款：清空重来，避免"删掉的宝石还挂在表里"。
		/// </summary>
		public static void Reload()
		{
			lock (_lock)
			{
				_loaded = false;
				_byId.Clear();
				_ordered.Clear();
				_sealSlots.Clear();
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
					DebugLogger.Log($"[Spell] 宝石表扫描失败：{ex.GetType().Name} {ex.Message}");
				}
				DebugLogger.Log(_byId.Count > 0
					? $"[Spell] 宝石表就绪：{_byId.Count} 颗（{string.Join(" / ", _byId.Keys)}）"
					: "[Spell] 宝石表为空（本模块 ModuleData/AssetRegistry/Modifiers.xml 没加载）——装不了修正，法术照常放");
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
					if (node.Name == "Modifier")
					{
						LoadModifier(node, path);
					}
					else if (node.Name == "Loadout")
					{
						LoadLoadout(node, path);
					}
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 读取 {Path.GetFileName(path)} 失败：{ex.GetType().Name} {ex.Message}");
			}
		}

		private static void LoadLoadout(XmlNode node, string path)
		{
			string seal = Attr(node, "seal");
			string raw = Attr(node, "slots");
			int slots;
			if (string.IsNullOrEmpty(seal)
				|| string.IsNullOrEmpty(raw)
				|| !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out slots)
				|| slots < 0)
			{
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：Loadout 行要 seal + 非负整数 slots（seal='{seal}' slots='{raw}'），跳过");
				return;
			}
			_sealSlots[seal] = slots;
		}

		private static void LoadModifier(XmlNode node, string path)
		{
			string id = Attr(node, "id");
			if (string.IsNullOrEmpty(id))
			{
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：Modifier 缺 id，跳过");
				return;
			}
			ModifierDef def = new ModifierDef
			{
				Id = id,
				Stage = Fallback(Attr(node, "stage"), "payload"),
				Name = Fallback(Attr(node, "name"), id),
				Icon = Attr(node, "icon"),
				Desc = Attr(node, "desc"),
				ItemId = Attr(node, "item"),
				ChakraCostAdd = FloatAttr(node, "cost_add", 0f),
			};

			foreach (XmlNode child in node.ChildNodes)
			{
				if (child.NodeType != XmlNodeType.Element)
				{
					continue;
				}
				if (child.Name == "Sub")
				{
					LoadSub(def, child, path);
					continue;
				}
				LoadOp(def, child, path);
			}

			if (def.Ops.Count == 0)
			{
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：宝石 '{id}' 一条操作都没有 → 它什么也不做（数据漏了？）");
			}
			if (_byId.ContainsKey(id))
			{
				DebugLogger.Log($"[Spell] 宝石 id '{id}' 重复登记 → 后者覆盖前者");
				_ordered.Remove(_byId[id]);
			}
			_byId[id] = def;
			_ordered.Add(def);
		}

		private static void LoadOp(ModifierDef def, XmlNode node, string path)
		{
			SpellOpKind kind;
			switch (node.Name)
			{
				case "Set":
					kind = SpellOpKind.Set;
					break;
				case "Add":
					kind = SpellOpKind.Add;
					break;
				case "Mul":
					kind = SpellOpKind.Mul;
					break;
				default:
					DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：宝石 '{def.Id}' 里有认不出的写法 <{node.Name}>"
						+ "（只有 Set / Add / Mul / Sub 四种），跳过这一条");
					return;
			}

			string field = Attr(node, "field");
			string sub = Attr(node, "sub");
			if (string.IsNullOrEmpty(field))
			{
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：宝石 '{def.Id}' 的 <{node.Name}> 缺 field，跳过");
				return;
			}
			// 🔴 认不出的字段**在这里就报**（而不是运行期才发现）—— 属性表是唯一真源
			if (!SpellFields.Exists(field))
			{
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：宝石 '{def.Id}' 改的字段 '{field}' 不在属性表里"
					+ " → 跳过（拼错了，或者该字段还没进表：补 SpellFields，不要加特例）");
				return;
			}

			string value = sub != null ? sub : Attr(node, "value");
			SpellField spec = SpellFields.Get(field);
			if (spec != null && spec.Kind == SpellFieldKind.Number && sub == null
				&& (string.IsNullOrEmpty(value) || !IsNumber(value)))
			{
				// 🔴 数值字段填了个非数字（或者干脆没填）—— 挡在这里。
				//    放过去的话 Set 会把它当成 0、Add/Mul 会当成 0 → 静默变成另一个意思。
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：宝石 '{def.Id}' 的 <{node.Name} field=\"{field}\">"
					+ $" 值 '{value}' 不是数字 → 跳过（写法：<Mul field=\"damage\" value=\"1.5\" />）");
				return;
			}

			ModifierOp op = new ModifierOp
			{
				Field = field,
				Kind = kind,
				SubId = sub,
				Text = value,
			};
			op.Number = ParseFloat(value, 0f);
			op.Raw = DescribeOp(op);
			def.Ops.Add(op);
		}

		private static bool IsNumber(string raw)
		{
			float v;
			return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
		}

		private static void LoadSub(ModifierDef def, XmlNode node, string path)
		{
			string id = Attr(node, "id");
			if (string.IsNullOrEmpty(id))
			{
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：宝石 '{def.Id}' 的 <Sub> 缺 id，跳过");
				return;
			}
			ModifierSubDef sub = new ModifierSubDef { Id = id };
			foreach (XmlNode child in node.ChildNodes)
			{
				if (child.NodeType != XmlNodeType.Element)
				{
					continue;
				}
				if (child.Name == "Base")
				{
					sub.BaseSpell = Attr(child, "spell");
				}
				else if (child.Name == "Gems")
				{
					foreach (string gem in SplitList(child.InnerText))
					{
						sub.Gems.Add(gem);
					}
				}
				else if (child.Name == "Scale")
				{
					sub.ScaleDamage = FloatAttr(child, "damage", 1f);
					string count = Attr(child, "count");
					int n;
					if (!string.IsNullOrEmpty(count)
						&& int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
					{
						sub.ScaleCount = n;
					}
				}
			}
			if (string.IsNullOrEmpty(sub.BaseSpell))
			{
				DebugLogger.Log($"[Spell] {Path.GetFileName(path)}：宝石 '{def.Id}' 的子块 '{id}' 没有 <Base spell=\"…\" />"
					+ " → 这个触发器不会有用（数据漏了？）");
			}
			def.Subs.Add(sub);
		}

		private static IEnumerable<string> SplitList(string raw)
		{
			if (string.IsNullOrEmpty(raw))
			{
				yield break;
			}
			foreach (string piece in raw.Split(new[] { ' ', '\t', '\r', '\n', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
			{
				yield return piece.Trim();
			}
		}

		/// <summary>一条操作的显示文本（诊断打印用），如 <c>damage ×1.5</c> / <c>on_hit_cast = &lt;sub_x&gt;</c>。</summary>
		public static string DescribeOp(ModifierOp op)
		{
			if (op == null)
			{
				return "?";
			}
			string symbol = op.Kind == SpellOpKind.Set ? "=" : (op.Kind == SpellOpKind.Add ? "+" : "x");
			SpellField f = SpellFields.Get(op.Field);
			if (f != null && f.Kind == SpellFieldKind.Text)
			{
				return $"{op.Field} {symbol} {op.Text}";
			}
			return $"{op.Field} {symbol}{op.Number.ToString("0.###", CultureInfo.InvariantCulture)}";
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

		private static float FloatAttr(XmlNode node, string name, float fallback)
		{
			string raw = Attr(node, name);
			return string.IsNullOrEmpty(raw) ? fallback : ParseFloat(raw, fallback);
		}
	}

	/// <summary>属性表里一个字段的三种类型。</summary>
	internal enum SpellFieldKind
	{
		Number,
		Bool,
		Text,
	}

	/// <summary>属性表的一行：读它、写它、打印它 —— 三件事在同一个地方。</summary>
	internal sealed class SpellField
	{
		public string Name;
		public SpellFieldKind Kind;
		public string Note;

		/// <summary>数值型的最小值（写完之后钳一下；正数类字段用它兜住 0/负数）。</summary>
		public float Min = float.NegativeInfinity;

		/// <summary>布尔/文本类不允许 <c>+</c> / <c>×</c>（<c>Add</c>/<c>Mul</c> 会用 <c>Set</c> 兜）。</summary>
		public bool CanAddMul;

		public Func<SpellDef, float> ReadNum;
		public Func<SpellDef, bool> ReadBool;
		public Func<SpellDef, string> ReadText;

		public Action<SpellDef, float> WriteNum;
		public Action<SpellDef, bool> WriteBool;
		public Action<SpellDef, string> WriteText;

		/// <summary>打印这个字段在某个定义里的当前值。</summary>
		public string Describe(SpellDef def)
		{
			try
			{
				switch (Kind)
				{
					case SpellFieldKind.Number:
						return ReadNum(def).ToString("0.###", CultureInfo.InvariantCulture);
					case SpellFieldKind.Bool:
						return ReadBool(def) ? "true" : "false";
					default:
						string t = ReadText(def);
						return string.IsNullOrEmpty(t) ? "-" : t;
				}
			}
			catch (Exception)
			{
				return "?";
			}
		}
	}

	/// <summary>
	/// 🔴 **属性表（唯一真源）** —— <see cref="SpellDef"/> 填它 · Modifiers.xml 改它 · 代码读它 · 计划 §16.1 描述它。
	/// <para>
	/// 加一个可被修正的字段 = **在本表加一行 + 在八个作用点里接上读取**（§16.1 的八处：推进/定向/命中查询/
	/// 命中裁决/触发/到期/表现/结算）。**说不清它在哪一处被读的字段不许进表**（装饰品，删掉）。
	/// </para>
	/// <para>
	/// 🔴 身份型字段（id / 弹药物品 / 族 / 瞄准轴 / 投送轴 / 结算列表）**不在表里** —— 修正改的是行为，
	/// 不是"这是什么法术"（§16.1）。
	/// </para>
	/// </summary>
	internal static class SpellFields
	{
		private static readonly Dictionary<string, SpellField> _table =
			new Dictionary<string, SpellField>(StringComparer.Ordinal);

		/// <summary>表里全部字段名（诊断 / `custom.spell mods` 用）。</summary>
		public static IEnumerable<SpellField> All
		{
			get { return _table.Values; }
		}

		public static int Count
		{
			get { return _table.Count; }
		}

		public static bool Exists(string field)
		{
			return !string.IsNullOrEmpty(field) && _table.ContainsKey(field);
		}

		public static SpellField Get(string field)
		{
			SpellField f;
			return !string.IsNullOrEmpty(field) && _table.TryGetValue(field, out f) ? f : null;
		}

		/// <summary>
		/// 把一条操作落到定义上。返回 false = 这条操作无效（字段类型与操作不匹配等），<paramref name="error"/> 说明原因。
		/// 全程不抛异常（铁律 1）。
		/// </summary>
		public static bool Apply(SpellDef def, ModifierOp op, out string error)
		{
			error = null;
			if (def == null || op == null)
			{
				error = "null def/op";
				return false;
			}
			SpellField f = Get(op.Field);
			if (f == null)
			{
				error = $"field '{op.Field}' not in table";
				return false;
			}

			if (f.Kind == SpellFieldKind.Number)
			{
				if (op.Kind != SpellOpKind.Set && !f.CanAddMul)
				{
					error = $"field '{op.Field}' does not take + / x";
					return false;
				}
				float current = f.ReadNum(def);
				float value = op.Kind == SpellOpKind.Set
					? op.Number
					: (op.Kind == SpellOpKind.Add ? current + op.Number : current * op.Number);
				if (value < f.Min)
				{
					value = f.Min;
				}
				if (float.IsNaN(value) || float.IsInfinity(value))
				{
					error = $"field '{op.Field}' -> NaN/Inf";
					return false;
				}
				f.WriteNum(def, value);
				return true;
			}

			if (op.Kind != SpellOpKind.Set)
			{
				error = $"field '{op.Field}' is {f.Kind} (only Set makes sense)";
				return false;
			}
			if (f.Kind == SpellFieldKind.Bool)
			{
				if (op.Text == null)
				{
					// 没写值 = 想 `Set` 一个布尔但没说开还是关 —— 挡掉，别静默当成 false
					error = $"field '{op.Field}' needs value=\"true\"/\"false\"";
					return false;
				}
				f.WriteBool(def, IsTrue(op.Text));
				return true;
			}
			if (op.Text == null)
			{
				error = $"field '{op.Field}' needs a value";
				return false;
			}
			f.WriteText(def, op.Text);
			return true;
		}

		private static bool IsTrue(string raw)
		{
			return !string.IsNullOrEmpty(raw)
				&& (raw.Equals("true", StringComparison.OrdinalIgnoreCase)
					|| raw.Equals("on", StringComparison.OrdinalIgnoreCase)
					|| raw == "1");
		}

		// ─────────────────────────── 表体（加字段只动这里）───────────────────────────
		//
		// 三张书写工具，一行一个字段：
		//   N(名, 说明, 读, 写, 最小值)  —— 数值型（Set/Add/Mul 全支持）
		//   B(名, 说明, 读, 写)          —— 布尔型（Set 开/关）
		//   T(名, 说明, 读, 写)          —— 文本/引用型（Set 一个 id/枚举名）

		static SpellFields()
		{
			// ── ① 推进（速度 ← 重力 / 阻力 / 沿程加速；位置 ← 速度 × dt）──
			N("speed_scale", "初速倍率（推荐用它做加速/减速；speed 是绝对值）", d => d.SpeedScale, (d, v) => d.SpeedScale = v, 0f);
			N("speed", "初速绝对值（米/秒）", d => d.Speed, (d, v) => d.Speed = v, 0f);
			N("drag", "空气阻力（每秒衰减比例；0 = 不减速）", d => d.Drag, (d, v) => d.Drag = v, 0f);
			N("accel", "沿飞行方向的加速度（米/秒²；负数 = 边飞边慢）", d => d.Accel, (d, v) => d.Accel = v);
			N("gravity", "重力（米/秒²）—— 正 = 往下掉 · 0 = 平飞 · 负 = 反重力（往上飘）", d => d.Gravity, (d, v) => d.Gravity = v);
			N("turn_rate", "追踪转向速率（度/秒；0 = 直线飞）", d => d.TurnRate, (d, v) => d.TurnRate = v, 0f);
			N("turn_delay", "起飞后多少秒才开始追踪（秒）", d => d.TurnDelay, (d, v) => d.TurnDelay = v, 0f);

			// ── ② 定向（网格朝向 ← 速度方向 + 横滚 + 定向模式）──
			T("orient_mode", "定向模式：fixed（进入时定死，默认）/ velocity（每帧跟速度转）", d => d.OrientMode, (d, v) => d.OrientMode = v);
			N("tilt_deg", "飞行姿态的横滚角（度）", d => d.TiltDeg, (d, v) => d.TiltDeg = v);

			// ── ③ 命中查询（这一小段扫什么）──
			B("collide_terrain", "撞不撞地形/墙面（关掉 = 连查询都不做，更省）", d => d.CollideTerrain, (d, v) => d.CollideTerrain = v);
			B("collide_agent", "撞不撞人（关掉 = 只撞地形）", d => d.CollideAgent, (d, v) => d.CollideAgent = v);
			N("hit_radius", "命中半径（米，喂给引擎射线的粗细）", d => d.HitRadius, (d, v) => d.HitRadius = v, 0f);

			// ── ④ 命中裁决（撞上了算不算数、以后怎么走）──
			N("pierce", "最多能命中几个目标（1 = 打中一个就消失）", d => d.Pierce, (d, v) => d.Pierce = (int)v, 1f);
			B("hit_same_team", "打不打自己人（默认打 —— 引擎现状如此）", d => d.HitSameTeam, (d, v) => d.HitSameTeam = v);
			B("drill", "钻地：撞地形不停、继续飞（距离照算）", d => d.Drill, (d, v) => d.Drill = v);
			N("bounce_count", "能弹几下（0 = 撞到就结束）", d => d.BounceCount, (d, v) => d.BounceCount = (int)v, 0f);
			N("bounce_damping", "每次弹跳保留多少速度（0.6 = 弹完剩六成）", d => d.BounceDamping, (d, v) => d.BounceDamping = v, 0f);

			// ── ⑤ 触发（命中 / 定时 / 弹跳 / 分裂 → 派子法术）──
			T("on_hit_cast", "命中时放的子块 id（＝下面 <Sub id> 的名字）", d => d.OnHitCast, (d, v) => d.OnHitCast = v);
			T("on_timer_cast", "飞行 N 秒后放的子块 id", d => d.OnTimerCast, (d, v) => d.OnTimerCast = v);
			N("timer_seconds", "定时触发的 N（秒）", d => d.TimerSeconds, (d, v) => d.TimerSeconds = v, 0f);
			T("on_expire_cast", "到期/飞完时放的子块 id", d => d.OnExpireCast, (d, v) => d.OnExpireCast = v);
			T("on_bounce_cast", "每次弹跳时放的子块 id", d => d.OnBounceCast, (d, v) => d.OnBounceCast = v);
			N("trigger_depth", "子法术最多还能再套几层（防无限套娃，默认 3）", d => d.TriggerDepth, (d, v) => d.TriggerDepth = (int)v, 0f);
			N("split_at", "飞到行程的百分之几时分裂（0~1；0 = 不分裂）", d => d.SplitAt, (d, v) => d.SplitAt = v, 0f);
			N("split_count", "分裂成几发（含自己那一发之后的份数）", d => d.SplitCount, (d, v) => d.SplitCount = (int)v, 0f);
			T("leave_surface", "命中/到期时留下哪种地表（⚠️ 地表系统属阶段 8，现在只登记 + 告警）", d => d.LeaveSurface, (d, v) => d.LeaveSurface = v);

			// ── ⑥ 到期（距离 / 寿命 / 命中数耗尽）──
			N("max_distance", "最大飞行距离（米）", d => d.MaxDistance, (d, v) => d.MaxDistance = v, 0.01f);
			N("max_lifetime", "最长存活时间（秒）", d => d.MaxLifetime, (d, v) => d.MaxLifetime = v, 0.01f);
			N("duration", "放置族的存活时长（秒）", d => d.Duration, (d, v) => d.Duration = v, 0f);
			N("repeat_interval", "放置/引导族多久结算一次（秒）", d => d.RepeatInterval, (d, v) => d.RepeatInterval = v, 0.01f);
			N("start_delay", "放置族落地后延迟多久生效（秒）", d => d.StartDelay, (d, v) => d.StartDelay = v, 0f);
			N("start_delay_variance", "上面那个延迟的随机抖动（± 秒）", d => d.StartDelayVariance, (d, v) => d.StartDelayVariance = v, 0f);
			N("start_height", "天降高度（>0 = 从落点正上方这么多米砸下来）", d => d.StartHeight, (d, v) => d.StartHeight = v, 0f);

			// ── ⑦ 表现（网格 / 缩放 / 粒子 / 音效）──
			T("mesh", "飞行网格名", d => d.Mesh, (d, v) => d.Mesh = v);
			N("scale", "网格缩放", d => d.Scale, (d, v) => d.Scale = v, 0.001f);
			T("trail_particle", "拖尾粒子名", d => d.TrailParticle, (d, v) => d.TrailParticle = v);
			T("impact_particle", "命中爆散粒子名", d => d.ImpactParticle, (d, v) => d.ImpactParticle = v);
			T("charge_particle", "蓄力粒子名", d => d.ChargeParticle, (d, v) => d.ChargeParticle = v);
			T("charge_mesh", "蓄力核网格名", d => d.ChargeMesh, (d, v) => d.ChargeMesh = v);
			T("sound_release", "发射音效名", d => d.SoundRelease, (d, v) => d.SoundRelease = v);
			T("sound_hit", "命中音效名", d => d.SoundHit, (d, v) => d.SoundHit = v);
			T("indicator", "落点指示圈网格名", d => d.Indicator, (d, v) => d.Indicator = v);
			N("indicator_scale", "指示圈缩放", d => d.IndicatorScale, (d, v) => d.IndicatorScale = v, 0.001f);

			// ── ⑧ 结算（交给结算轴读的那些数）──
			N("damage", "伤害值", d => d.Damage, (d, v) => d.Damage = v, 0f);
			N("damage_variance", "伤害浮动（0.25 = 每次随机打到 75%~100%；0 = 不打折）", d => d.DamageVariance, (d, v) => d.DamageVariance = v, 0f);
			N("crit", "暴击率（0~1）", d => d.Crit, (d, v) => d.Crit = v, 0f);
			N("crit_mult", "暴击倍率（默认 2）", d => d.CritMult, (d, v) => d.CritMult = v, 0f);
			N("radius", "半径伤害的作用半径（米）", d => d.Radius, (d, v) => d.Radius = v, 0f);
			N("radius_falloff", "边缘衰减（0~1；边缘保留 1-falloff）", d => d.RadiusFalloff, (d, v) => d.RadiusFalloff = v, 0f);
			N("status_damage", "状态的每跳伤害", d => d.StatusDamage, (d, v) => d.StatusDamage = v, 0f);
			N("status_duration", "状态持续多久（秒）", d => d.StatusDuration, (d, v) => d.StatusDuration = v, 0f);
			N("status_interval", "状态每多少秒跳一次", d => d.StatusInterval, (d, v) => d.StatusInterval = v, 0.01f);
			T("status", "挂哪个状态（burn / poison / ...）", d => d.StatusId, (d, v) => d.StatusId = v);
			T("status_particle", "状态粒子名", d => d.StatusParticle, (d, v) => d.StatusParticle = v);
			T("damage_type", "伤害类型（引擎 DamageTypes：Cut / Pierce / Blunt）", d => d.DamageType.ToString(), (d, v) => SetDamageType(d, v));

			// ── 起手（瞄准/蓄力那几项）──
			N("count", "发数（一发变 N 发，由瞄准轴摊开）", d => d.Count, (d, v) => d.Count = (int)v, 1f);
			N("spread_deg", "多发散布角（度）", d => d.SpreadDeg, (d, v) => d.SpreadDeg = v, 0f);
			N("lock_max", "软锁最多锁几个目标", d => d.LockMax, (d, v) => d.LockMax = (int)v, 0f);
			N("lock_angle", "软锁的角度锥（度）", d => d.LockAngle, (d, v) => d.LockAngle = v, 0f);
			N("lock_range", "软锁的最远距离（米）", d => d.LockRange, (d, v) => d.LockRange = v, 0f);
			N("charge_time", "蓄满要按住多久（秒）", d => d.ChargeTime, (d, v) => d.ChargeTime = v, 0f);
			N("charge_bonus", "满蓄力的伤害加成倍率", d => d.ChargeBonus, (d, v) => d.ChargeBonus = v, 0f);
			N("charge_scale", "蓄力核满蓄力时的放大倍率", d => d.ChargeScale, (d, v) => d.ChargeScale = v, 0f);
			N("release_at", "出手帧（0~1 动作进度）", d => d.ReleaseAt, (d, v) => d.ReleaseAt = v, 0f);
			N("end_at", "收尾帧（0~1 动作进度）", d => d.EndAt, (d, v) => d.EndAt = v, 0f);

			// ── NPC 施法 ──
			B("ai", "允许 NPC 用这个法术", d => d.AiEnabled, (d, v) => d.AiEnabled = v);
			N("ai_weight", "NPC 选法术的权重", d => d.AiWeight, (d, v) => d.AiWeight = v, 0f);
			N("ai_cooldown", "NPC 放完后的冷却（秒）", d => d.AiCooldown, (d, v) => d.AiCooldown = v, 0f);
		}

		private static void SetDamageType(SpellDef def, string raw)
		{
			try
			{
				def.DamageType = (TaleWorlds.Core.DamageTypes)Enum.Parse(
					typeof(TaleWorlds.Core.DamageTypes), raw, true);
			}
			catch (Exception)
			{
				DebugLogger.Log($"[Spell] damage_type '{raw}' 不是引擎 DamageTypes 的名字 → 忽略这次改动");
			}
		}

		private static void N(string name, string note, Func<SpellDef, float> read, Action<SpellDef, float> write,
			float min = float.NegativeInfinity)
		{
			_table[name] = new SpellField
			{
				Name = name,
				Kind = SpellFieldKind.Number,
				Note = note,
				CanAddMul = true,
				Min = min,
				ReadNum = read,
				WriteNum = write,
			};
		}

		private static void B(string name, string note, Func<SpellDef, bool> read, Action<SpellDef, bool> write)
		{
			_table[name] = new SpellField
			{
				Name = name,
				Kind = SpellFieldKind.Bool,
				Note = note,
				CanAddMul = false,
				ReadBool = read,
				WriteBool = write,
			};
		}

		private static void T(string name, string note, Func<SpellDef, string> read, Action<SpellDef, string> write)
		{
			_table[name] = new SpellField
			{
				Name = name,
				Kind = SpellFieldKind.Text,
				Note = note,
				CanAddMul = false,
				ReadText = read,
				WriteText = write,
			};
		}
	}
}
