using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.ModuleManager;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选人目录读取器——**建世界之前**就能用的选人数据（内容包通用契约，铁律 3：本类不感知任何具体世界观）。
	///
	/// 为什么需要它：选人界面原本读**活世界对象**（`Kingdom.All` / `Clan.Heroes`），
	/// 只能等世界建好（长 loading）之后才弹得出来。把选人提到 loading **之前**，
	/// 就必须有一份静态的「该时代有哪些可扮演的人、他们归谁、显示成什么」。
	///
	/// 数据在哪：任何模块（内容包）的 <c>ModuleData/AssetRegistry/HeroCatalog.xml</c>：
	///   &lt;RealmType id="warrior" name="{=KEY}fallback" order="1"/&gt;   ← 筛档标签（全时代共用；界面最左列）
	///   &lt;Era id="1560"&gt;
	///     &lt;Realm id="kingdom_oda" name="{=KEY}fallback" type="warrior" order="1"/&gt;
	///     &lt;Realm id="" name="{=KEY}fallback" order="99"/&gt;   ← 无所属那一档（不带 type，由家族决定）
	///     &lt;House id="clan_oda" realm="kingdom_oda" name="{=KEY}…" type="warrior" order="1"/&gt;
	///     &lt;Lord id="lord_1_oda" house="clan_oda" order="1" name="{=KEY}…"
	///           identity="{=KEY}…" seat="{=KEY}…"
	///           bustup="lwnprof_bustup_195" mini="lwnprof_mini_195"/&gt;
	///   &lt;/Era&gt;
	/// 由内容包生成器产出（生成物禁手改；本仓库范本 = Scripts/gen_taikou_hero_catalog.py）。
	///
	/// 🔴 **按「年份」取时代**（不是战役类名）——年份是世界无关的数字，LWN 侧因此不必知道
	///   任何内容包的类名/文件名。年份来自 <see cref="EraCatalog.SelectedEraYear"/>。
	/// 🔴 **目录的 id 集合 == 该时代会建出来的英雄集合**（生成器与 taikou_heroes 段同源）——
	///   所以不存在「目录里有、世界里没有」的漂移；守卫见 Scripts/check_hero_profile_keys.py。
	/// 🔴 所有显示名都是 <c>{=KEY}fallback</c> **原样串**，由界面交给 TextObject 走引擎本地化
	///   （本类不认识任何具体词汇）。
	///
	/// 使用姿势：懒加载（首次查询才扫模块 + 读文件）。内容包缺失/文件损坏 → 空表 + 一条日志，不抛（铁律 1）。
	/// </summary>
	public static class HeroCatalogRegistry
	{
		private const string FileName = "HeroCatalog.xml";

		private static bool _loaded;
		private static readonly object _lock = new object();
		private static readonly Dictionary<int, EraData> _byYear = new Dictionary<int, EraData>();
		private static readonly List<RealmType> _realmTypes = new List<RealmType>();

		/// <summary>一个可选王国（含「无所属」那一档，其 <see cref="Id"/> 为空串）。</summary>
		public sealed class Realm
		{
			public string Id;
			public int Order;
			/// <summary>显示名原样串（<c>{=KEY}fallback</c>）。</summary>
			public string NameRaw;
			/// <summary>势力类型（筛档 id，如 <c>warrior</c>）；无所属那一档为空串（由家族决定）。</summary>
			public string Type;
		}

		/// <summary>一个家族（挂在某个 <see cref="Realm"/> 下）。</summary>
		public sealed class House
		{
			public string Id;
			public string RealmId;
			public int Order;
			public string NameRaw;
			/// <summary>势力类型（筛档 id，如 <c>trader</c>）。</summary>
			public string Type;
		}

		/// <summary>
		/// 一个筛档（选人界面最左列的一档，如 warrior/trader/ninja/pirate/others）。
		/// 🔴 档位由**内容包**定义（<c>&lt;RealmType&gt;</c>），标签也是内容包的键——
		///   基座不认识「忍者/海贼」这类具体词汇（铁律 3）。「全部」那一档由界面自己加。
		/// </summary>
		public sealed class RealmType
		{
			public string Id;
			public int Order;
			public string NameRaw;
		}

		/// <summary>一个可扮演的领主。</summary>
		public sealed class Lord
		{
			public string Id;
			public string HouseId;
			public int Order;
			public string NameRaw;
			/// <summary>身份（太阁5 原文，如"足轻组头"）；源数据没有 = 空串。</summary>
			public string IdentityRaw;
			/// <summary>据点显示名；源数据查不到 = 空串（界面不显示该行）。</summary>
			public string SeatRaw;
			/// <summary>半身立绘 sprite 名（生成器按**时代**挑好的那张卡）；空 = 界面回落到立绘表首张。</summary>
			public string BustupSprite;
			/// <summary>小头像 sprite 名（同上一张卡的小图）；空 = 同上回落。</summary>
			public string MiniSprite;
		}

		/// <summary>一个时代的完整选人数据。</summary>
		public sealed class EraData
		{
			public readonly List<Realm> Realms = new List<Realm>();
			public readonly List<House> Houses = new List<House>();
			public readonly List<Lord> Lords = new List<Lord>();
			private readonly Dictionary<string, List<Lord>> _lordsByHouse = new Dictionary<string, List<Lord>>();
			private readonly Dictionary<string, Lord> _lordById = new Dictionary<string, Lord>();
			private readonly Dictionary<string, House> _houseById = new Dictionary<string, House>();

			internal void Index()
			{
				foreach (House h in Houses)
				{
					_houseById[h.Id] = h;
					_lordsByHouse[h.Id] = new List<Lord>();
				}
				foreach (Lord l in Lords)
				{
					_lordById[l.Id] = l;
					if (l.HouseId != null && _lordsByHouse.TryGetValue(l.HouseId, out List<Lord> list))
					{
						list.Add(l);
					}
				}
			}

			/// <summary>该家族下的领主（按目录 order）。</summary>
			public List<Lord> LordsOf(string houseId)
			{
				return (houseId != null && _lordsByHouse.TryGetValue(houseId, out List<Lord> list))
					? list : new List<Lord>();
			}

			/// <summary>该王国下的家族（按目录 order；realmId 空串 = 「无所属」那一档）。</summary>
			public List<House> HousesOf(string realmId)
			{
				realmId = realmId ?? string.Empty;
				var outList = new List<House>();
				foreach (House h in Houses)
				{
					if ((h.RealmId ?? string.Empty) == realmId)
					{
						outList.Add(h);
					}
				}
				return outList;
			}

			public Lord GetLord(string heroId)
			{
				return (heroId != null && _lordById.TryGetValue(heroId, out Lord l)) ? l : null;
			}

			public House GetHouse(string houseId)
			{
				return (houseId != null && _houseById.TryGetValue(houseId, out House h)) ? h : null;
			}
		}

		/// <summary>取某年份的选人数据；没有该年份 = null（界面据此显示"该时代无事可做"）。</summary>
		public static EraData GetEra(int year)
		{
			EnsureLoaded();
			return _byYear.TryGetValue(year, out EraData d) ? d : null;
		}

		/// <summary>
		/// 全部筛档（按目录 order；内容包没配 = 空表，界面只显示「全部」那一档）。
		/// 各时代共用一份（档位是世界观级概念，不随年代变）。
		/// </summary>
		public static List<RealmType> GetRealmTypes()
		{
			EnsureLoaded();
			return _realmTypes;
		}

		/// <summary>该年份是否有可扮演的人（菜单入口是否可用的判据）。</summary>
		public static bool HasEra(int year)
		{
			EraData d = GetEra(year);
			return d != null && d.Lords.Count > 0;
		}

		// ───────────────────────── 加载 ─────────────────────────

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
					DebugLogger.Log($"[HeroCatalog] 扫描失败：{ex.GetType().Name} {ex.Message}");
				}
				foreach (EraData d in _byYear.Values)
				{
					d.Realms.Sort((a, b) => a.Order.CompareTo(b.Order));
					d.Houses.Sort((a, b) => a.Order.CompareTo(b.Order));
					d.Lords.Sort((a, b) => a.Order.CompareTo(b.Order));
					d.Index();
				}
				_realmTypes.Sort((a, b) => a.Order.CompareTo(b.Order));
				var summary = new List<string>();
				foreach (KeyValuePair<int, EraData> kv in _byYear)
				{
					summary.Add($"{kv.Key}:{kv.Value.Lords.Count}人");
				}
				DebugLogger.Log($"[HeroCatalog] 选人目录就绪：{(summary.Count > 0 ? string.Join(" / ", summary) : "（空）")}"
					+ $"；筛档 {_realmTypes.Count} 个");
				_loaded = true;
			}
		}

		private static void LoadFile(string path)
		{
			var doc = new XmlDocument();
			try
			{
				doc.Load(path);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[HeroCatalog] 解析失败 {path}：{ex.Message}");
				return;
			}
			XmlNode root = doc.DocumentElement;
			if (root == null)
			{
				return;
			}
			foreach (XmlNode eraNode in root.ChildNodes)
			{
				if (eraNode.Name == "RealmType")
				{
					// 筛档（全时代共用；界面左列按 order 排）
					string tid = Attr(eraNode, "id");
					if (!string.IsNullOrEmpty(tid) && _realmTypes.FindIndex(t => t.Id == tid) < 0)
					{
						_realmTypes.Add(new RealmType
						{
							Id = tid, Order = IntAttr(eraNode, "order"), NameRaw = Attr(eraNode, "name"),
						});
					}
					continue;
				}
				if (eraNode.Name != "Era")
				{
					continue;
				}
				if (!int.TryParse(Attr(eraNode, "id"), out int year))
				{
					DebugLogger.Log($"[HeroCatalog] 跳过 <Era id=\"{Attr(eraNode, "id")}\">：年份不是数字");
					continue;
				}
				if (!_byYear.TryGetValue(year, out EraData data))
				{
					data = new EraData();
					_byYear[year] = data;
				}
				foreach (XmlNode n in eraNode.ChildNodes)
				{
					try
					{
						switch (n.Name)
						{
							case "Realm":
								data.Realms.Add(new Realm
								{
									Id = Attr(n, "id"), Order = IntAttr(n, "order"),
									NameRaw = Attr(n, "name"), Type = Attr(n, "type"),
								});
								break;
							case "House":
								data.Houses.Add(new House
								{
									Id = Attr(n, "id"), RealmId = Attr(n, "realm"),
									Order = IntAttr(n, "order"), NameRaw = Attr(n, "name"),
									Type = Attr(n, "type"),
								});
								break;
							case "Lord":
								string id = Attr(n, "id");
								if (string.IsNullOrEmpty(id))
								{
									break;
								}
								data.Lords.Add(new Lord
								{
									Id = id, HouseId = Attr(n, "house"), Order = IntAttr(n, "order"),
									NameRaw = Attr(n, "name"), IdentityRaw = Attr(n, "identity"),
									SeatRaw = Attr(n, "seat"),
									BustupSprite = Attr(n, "bustup"), MiniSprite = Attr(n, "mini"),
								});
								break;
						}
					}
					catch (Exception ex)
					{
						// 单条坏了不拖垮整表（铁律 2 精神：外部数据不可信）
						DebugLogger.Log($"[HeroCatalog] 跳过一条 <{n.Name}>：{ex.Message}");
					}
				}
			}
		}

		private static string Attr(XmlNode node, string name)
		{
			return node.Attributes?[name]?.Value ?? string.Empty;
		}

		private static int IntAttr(XmlNode node, string name)
		{
			return int.TryParse(Attr(node, name), out int v) ? v : 0;
		}
	}
}
