using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.ModuleManager;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 种族 → 允许性别 表读取器（**内容包通用契约**，铁律 3：本类不感知任何具体世界观）。
	///
	/// 数据在哪：任何模块（内容包）的 <c>ModuleData/AssetRegistry/RaceGenders.xml</c>：
	///   &lt;Race id="lwn_nobunaga" gender="male" /&gt;      ← 每 race 一条；gender = male / female / both
	/// 由内容包生成器产出（生成物禁手改；本仓库范本 = Scripts/gen_taikou_sw2_heads.py）。
	///
	/// 🔴 **为什么必须是这张离线表，而不是运行时从角色反推**（2026-09-16 实机崩过一次）：
	///    一个换头角色 = 一个 race，而一个 race 只做单一性别的 skin —— 性别不符时引擎取不到皮肤，
	///    直接 native AV（不是"落兜底皮肤"）。所以要在捏脸界面上把不符的 race 挡住。
	///    一开始用「扫 CharacterObject 反推每个 race 的性别」，**错在时代**：六代领主是按时代
	///    互斥加载的，1598 代只加载 18 个带 race 的领主，另外 10 个男 race 那一代没人用 →
	///    表里缺项 → 过滤失效 → 玩家点到漏网的男 race → 崩。表的来源必须是**与时代无关**的
	///    挑件表（与 skins.xml 同源同生成器）。
	///
	/// 消费方：<c>CampaignMode/FaceGenRaceGenderFilterPatch.cs</c>（捏脸「种族」下拉按性别置灰）。
	/// 使用姿势：懒加载（首次查询才扫模块 + 读盘；静态构造器 / OnSubModuleLoad 禁止触碰）。
	/// 内容包缺失 / 文件损坏 → 空表 + 一条 [RaceGender] 日志，不抛（铁律 1 风格）。
	/// 🔴 表里查不到的 race **不限制**（安全缺省：别的 mod 的 race 我们不知道，不替人家做主）。
	/// </summary>
	public static class RaceGenderRegistry
	{
		public const int Male = 1;
		public const int Female = 2;
		public const int Both = Male | Female;

		private const string FileName = "RaceGenders.xml";

		private static bool _loaded;
		private static readonly object _lock = new object();
		private static readonly Dictionary<string, int> _byRaceId = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>该 race 允许的性别位掩码；表里没有这一条 → <see cref="Both"/>（不限制）。</summary>
		public static int GetAllowedGenders(string raceId)
		{
			if (string.IsNullOrEmpty(raceId))
			{
				return Both;
			}
			EnsureLoaded();
			int mask;
			return _byRaceId.TryGetValue(raceId, out mask) ? mask : Both;
		}

		/// <summary>表里有没有这一条。诊断用：下拉里出现没登记的 race = 内容包漏生成这张表。</summary>
		public static bool HasEntry(string raceId)
		{
			if (string.IsNullOrEmpty(raceId))
			{
				return false;
			}
			EnsureLoaded();
			return _byRaceId.ContainsKey(raceId);
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
					DebugLogger.Log($"[RaceGender] 扫描失败：{ex.GetType().Name} {ex.Message}");
				}
				DebugLogger.Log($"[RaceGender] 种族性别表就绪：{_byRaceId.Count} 条");
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
				DebugLogger.Log($"[RaceGender] 解析失败 {path}：{ex.Message}");
				return;
			}
			XmlNode root = doc.DocumentElement;
			if (root == null)
			{
				return;
			}
			foreach (XmlNode node in root.ChildNodes)
			{
				if (node.NodeType != XmlNodeType.Element || node.Name != "Race")
				{
					continue; // 注释等一律跳过（铁律 22：列表型节点里禁夹注释，但读侧也不该因此崩）
				}
				try
				{
					string id = node.Attributes?["id"]?.Value;
					string gender = node.Attributes?["gender"]?.Value;
					if (string.IsNullOrEmpty(id))
					{
						continue;
					}
					int mask;
					switch (gender)
					{
						case "male":
							mask = Male;
							break;
						case "female":
							mask = Female;
							break;
						case "both":
							mask = Both;
							break;
						default:
							DebugLogger.Log($"[RaceGender] {id} 的 gender 取值不认识（'{gender}'）→ 按不限制处理");
							mask = Both;
							break;
					}
					_byRaceId[id] = mask;
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[RaceGender] 单条解析失败（已跳过）：{ex.GetType().Name} {ex.Message}");
				}
			}
		}
	}
}
