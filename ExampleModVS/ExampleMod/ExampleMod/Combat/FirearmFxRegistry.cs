using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.ModuleManager;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 火器表现契约读取器 —— 「开火时该播什么音效、放什么烟」（内容包通用契约，铁律 3：本类不感知任何具体世界观）。
	///
	/// 数据在哪：任何模块（内容包）的 <c>ModuleData/AssetRegistry/FirearmFx.xml</c>：
	///   &lt;Profile ammoClass="Cartridge"
	///            near="tk_teppo_fire_near" mid="tk_teppo_fire_mid" far="tk_teppo_fire_far"
	///            particle="tk_smoke_teppo" nearDist="22" farDist="75" /&gt;
	///
	/// 🔴 **火器的身份 = 它射什么弹**（<c>ammoClass</c> = 引擎 <see cref="WeaponClass"/> 枚举的名字）——
	///   不是物品 id、不是 mesh、更不是任何世界观词汇。加一种新火器（火箭、霰弹、大筒）= 加一行
	///   &lt;Profile&gt;，本类与 <see cref="FirearmFxLogic"/> 一行都不用改。
	/// ⚠️ **弓弩必须与火器用不同的 ammoClass**：原版弩兵的武器是 Crossbow + Bolt，若火器也用
	///   Bolt，弩兵开火会被一并认成火器（织丰全库没有普通弩，所以它碰不到这个问题；内容包会）。
	///
	/// 容错（铁律 1）：内容包没装 / 文件缺失 / 名字写错 / 数值非法 → 该项留空或跳过，
	///   只记一条日志，绝不抛异常。查不到 Profile = 不是火器 → 调用方直接返回，弓弩不受影响。
	///
	/// 使用姿势：懒加载（首次查询才扫模块 + 读文件）。
	/// </summary>
	public static class FirearmFxRegistry
	{
		private const string FileName = "FirearmFx.xml";

		/// <summary>一种火器口径的表现配置。</summary>
		public sealed class Profile
		{
			/// <summary>引擎 <see cref="WeaponClass"/> 枚举名（如 <c>Cartridge</c>）——查表键。</summary>
			public string AmmoClass;

			/// <summary>音效名（内容包 <c>module_sounds.xml</c> 里的 module_sound name）。空 = 不播。</summary>
			public string NearSound;
			public string MidSound;
			public string FarSound;

			/// <summary>枪口粒子名（引擎 <c>ParticleSystemManager</c> 里的名字）。空 = 不放。</summary>
			public string Particle;

			/// <summary>距离分档（米）：小于 NearDist 用近档，大于 FarDist 用远档，中间用中档。</summary>
			public float NearDist = 22f;
			public float FarDist = 75f;
		}

		private static bool _loaded;
		private static readonly object _lock = new object();
		private static readonly Dictionary<string, Profile> _byAmmoClass =
			new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// 按口径查火器配置。返回 null = 这个口径不是火器（调用方应直接跳过，不做任何表现）。
		/// </summary>
		public static Profile Find(WeaponClass ammoClass)
		{
			EnsureLoaded();
			Profile p;
			return _byAmmoClass.TryGetValue(ammoClass.ToString(), out p) ? p : null;
		}

		/// <summary>已登记的火器口径数量（诊断用）。</summary>
		public static int Count
		{
			get { EnsureLoaded(); return _byAmmoClass.Count; }
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
					DebugLogger.Log($"[FirearmFx] 扫描失败：{ex.GetType().Name} {ex.Message}");
				}
				DebugLogger.Log(_byAmmoClass.Count > 0
					? $"[FirearmFx] 火器口径就绪：{string.Join(" / ", _byAmmoClass.Keys)}"
					: "[FirearmFx] 火器口径表为空（无内容包提供 FirearmFx.xml）——火器不会有音效/烟雾");
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
					if (node.NodeType != XmlNodeType.Element || node.Name != "Profile")
					{
						continue;
					}
					string ammoClass = Attr(node, "ammoClass");
					if (string.IsNullOrEmpty(ammoClass))
					{
						DebugLogger.Log($"[FirearmFx] {Path.GetFileName(path)}：Profile 缺 ammoClass，跳过");
						continue;
					}
					Profile p = new Profile
					{
						AmmoClass = ammoClass,
						NearSound = Attr(node, "near"),
						MidSound = Attr(node, "mid"),
						FarSound = Attr(node, "far"),
						Particle = Attr(node, "particle"),
						NearDist = FloatAttr(node, "nearDist", 22f),
						FarDist = FloatAttr(node, "farDist", 75f),
					};
					// 🔴 后加载的覆盖先加载的（与骑砍「后注册覆盖」惯例一致，也便于内容包互相覆盖调参）
					_byAmmoClass[ammoClass] = p;
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[FirearmFx] 读取 {Path.GetFileName(path)} 失败：{ex.GetType().Name} {ex.Message}");
			}
		}

		private static string Attr(XmlNode node, string name)
		{
			XmlAttribute a = node.Attributes?[name];
			string v = a?.Value;
			return string.IsNullOrEmpty(v) ? null : v.Trim();
		}

		private static float FloatAttr(XmlNode node, string name, float fallback)
		{
			string raw = Attr(node, name);
			float v;
			if (!string.IsNullOrEmpty(raw)
				&& float.TryParse(raw, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out v)
				&& v > 0f)
			{
				return v;
			}
			return fallback;
		}
	}
}
