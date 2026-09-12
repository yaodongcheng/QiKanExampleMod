using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using TaleWorlds.ModuleManager;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 英雄画像表读取器（选人详情页的数据源）——**内容包通用契约**（铁律 3：本类不感知任何具体世界观）。
	///
	/// 数据在哪：任何模块（内容包）的 <c>ModuleData/AssetRegistry/HeroProfiles.xml</c>：
	///   &lt;HeroProfile id="..." birth="1534" die="1582" clan="..." culture="..."
	///                command="96" force="87" govern="92" wisdom="95" charm="88"
	///                soldier="90" mount="85" ... medical="30" /&gt;       ← 每英雄一条
	///   &lt;Recommended order="1" id="..." storyType="{=KEY}fallback" storyGoal="{=KEY}fallback" /&gt;
	/// 由内容包生成器产出（生成物禁手改；本仓库范本 = Scripts/gen_taikou_hero_profiles.py）。
	///
	/// 🔴 键 = 英雄 StringId，与 <c>spnpccharacters.xml</c> 的 NPCCharacter id、
	///    <c>ProfileStages.csv</c> 的 StringId **三处同键**——不同键就查不到画像/立绘。
	///
	/// 使用姿势：懒加载（首次查询才扫模块 + 读文件；静态构造器 / OnSubModuleLoad 禁止触碰）。
	/// 内容包缺失 / 文件损坏 → 空表 + 一条 [HeroProfile] 日志，不抛（铁律 1 风格）。
	/// </summary>
	public static class HeroProfileRegistry
	{
		private const string FileName = "HeroProfiles.xml";

		/// <summary>技能项数（与生成器 SKILLS 表同序同长：足轻…医术）。</summary>
		public const int SkillCount = 16;

		private static bool _loaded;
		private static readonly object _lock = new object();
		private static readonly Dictionary<string, Profile> _byId = new Dictionary<string, Profile>();
		private static readonly List<Recommendation> _recommended = new List<Recommendation>();

		/// <summary>一名英雄的画像（太阁侧原始数值；换算成骑砍数值是**另一件事**，见 plan §五）。</summary>
		public sealed class Profile
		{
			public string Id;
			public int Birth;
			public int Die;

			/// <summary>数据源里的家族 id（如 clan_oda_1）——展示用，与游戏内 Clan 未必同名。</summary>
			public string Clan;

			/// <summary>数据源里的文化 id（如 kinai）。</summary>
			public string Culture;

			// 五维（0–100）
			public int Command, Force, Govern, Wisdom, Charm;

			/// <summary>16 项技能（0–100，顺序见 <see cref="SkinNames"/> 同序）。</summary>
			public readonly int[] Skills = new int[SkillCount];

			/// <summary>生卒（1560 − Birth 即开局年龄；Die=0 表示数据缺失）。</summary>
			public bool HasLifespan => Birth > 0;
		}

		/// <summary>「推荐」条目（型别 / 目标描述是**玩家可见文本**，字段里存的是 <c>{=KEY}fallback</c> 原样串）。</summary>
		public sealed class Recommendation
		{
			public int Order;
			public string HeroId;
			public string StoryTypeRaw;
			public string StoryGoalRaw;
		}

		/// <summary>查一名英雄的画像；无记录返回 null（占位期英雄 / 未进全量数据时的正常情况）。</summary>
		public static Profile GetProfile(string heroId)
		{
			EnsureLoaded();
			if (string.IsNullOrEmpty(heroId))
			{
				return null;
			}
			return _byId.TryGetValue(heroId, out Profile p) ? p : null;
		}

		/// <summary>「推荐」人清单（按 order 升序；内容包没配 = 空表）。</summary>
		public static IReadOnlyList<Recommendation> Recommendations
		{
			get
			{
				EnsureLoaded();
				return _recommended;
			}
		}

		// ───────────────────────── 立绘（转发给 PortraitRegistry，界面只认这一个入口）─────────────────────────

		/// <summary>取半身立绘 sprite 名（无 = null）。取**首张**卡（多阶段角色的第 1 阶段）。</summary>
		public static string GetBustupSpriteName(string heroId)
		{
			var list = PortraitRegistry.GetStagePortraits(heroId);
			return (list != null && list.Count > 0) ? list[0].BustupSpriteName : null;
		}

		/// <summary>取小头像 sprite 名（无 = null）。列表行用。</summary>
		public static string GetMiniheadSpriteName(string heroId)
		{
			var list = PortraitRegistry.GetStagePortraits(heroId);
			return (list != null && list.Count > 0) ? list[0].MiniheadSpriteName : null;
		}

		/// <summary>
		/// 🔴 **显示点必调**：把这张立绘的纹理按需加载进显存（幂等）。
		/// 立绘内容包用的是**按需单张加载**（SpriteCategory 不是整分类驻流，`SpriteAssetsManager` 的
		/// `GetOrLoad` 是唯一入口）——**只把 sprite 名绑到 prefab = 纹理没加载 = 画不出来**
		/// （2026-09-12 实机症状：选好人进详情页，立绘位置空白；同因还会让列表小头像不显示）。
		/// 配额由 SpriteAssetsManager 内部 LRU 管（bustup 12 张 / mini 64 张）。
		/// </summary>
		public static void EnsurePortraitLoaded(string spriteName)
		{
			if (string.IsNullOrEmpty(spriteName))
			{
				return;
			}
			try
			{
				SpriteAssetsManager.GetOrLoad(spriteName);
			}
			catch (Exception ex)
			{
				// 立绘加载失败不该拦住房界面（铁律 1 精神）
				DebugLogger.Log($"[HeroProfile] 立绘加载失败 {spriteName}：{ex.GetType().Name} {ex.Message}");
			}
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
					DebugLogger.Log($"[HeroProfile] 扫描失败：{ex.GetType().Name} {ex.Message}");
				}
				_recommended.Sort((a, b) => a.Order.CompareTo(b.Order));
				DebugLogger.Log($"[HeroProfile] 画像表就绪：英雄 {_byId.Count} 名 / 推荐 {_recommended.Count} 名");
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
				DebugLogger.Log($"[HeroProfile] 解析失败 {path}：{ex.Message}");
				return;
			}

			XmlNode root = doc.DocumentElement;
			if (root == null)
			{
				return;
			}
			foreach (XmlNode node in root.ChildNodes)
			{
				try
				{
					if (node.Name == "HeroProfile")
					{
						Profile p = ReadProfile(node);
						if (p != null)
						{
							_byId[p.Id] = p;
						}
					}
					else if (node.Name == "DimensionLabels")
					{
						ReadLabels(node, DimensionIds, _dimensionLabels);
					}
					else if (node.Name == "SkillLabels")
					{
						ReadLabels(node, SkillIds, _skillLabels);
					}
					else if (node.Name == "Recommended")
					{
						string id = Attr(node, "id");
						if (!string.IsNullOrEmpty(id))
						{
							_recommended.Add(new Recommendation
							{
								Order = ParseInt(Attr(node, "order"), 0),
								HeroId = id,
								StoryTypeRaw = Attr(node, "storyType"),
								StoryGoalRaw = Attr(node, "storyGoal"),
							});
						}
					}
				}
				catch (Exception ex)
				{
					// 单条坏了不拖垮整表（铁律 2 精神：外部数据不可信）
					DebugLogger.Log($"[HeroProfile] 跳过一条 <{node.Name}>：{ex.Message}");
				}
			}
		}

		/// <summary>
		/// 读一组标签（&lt;Label field="soldier" text="{=TAIKOU_skill_soldier}Spearmen" /&gt;）。
		/// 按 <paramref name="fields"/> 的下标落位；缺的项保留原字段名（不崩、不空白）。
		/// </summary>
		private static void ReadLabels(XmlNode parent, string[] fields, string[] target)
		{
			foreach (XmlNode node in parent.ChildNodes)
			{
				if (node.Name != "Label")
				{
					continue;
				}
				string field = Attr(node, "field");
				string text = Attr(node, "text");
				if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(text))
				{
					continue;
				}
				int idx = Array.IndexOf(fields, field);
				if (idx >= 0)
				{
					target[idx] = text;
				}
			}
		}

		private static Profile ReadProfile(XmlNode node)
		{
			string id = Attr(node, "id");
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}
			var p = new Profile
			{
				Id = id,
				Birth = ParseInt(Attr(node, "birth"), 0),
				Die = ParseInt(Attr(node, "die"), 0),
				Clan = Attr(node, "clan"),
				Culture = Attr(node, "culture"),
				Command = ParseInt(Attr(node, "command"), 0),
				Force = ParseInt(Attr(node, "force"), 0),
				Govern = ParseInt(Attr(node, "govern"), 0),
				Wisdom = ParseInt(Attr(node, "wisdom"), 0),
				Charm = ParseInt(Attr(node, "charm"), 0),
			};
			for (int i = 0; i < SkillCount; i++)
			{
				p.Skills[i] = ParseInt(Attr(node, SkillIds[i]), 0);
			}
			return p;
		}

		/// <summary>技能 XML 属性名，顺序 = 详情页显示顺序（与生成器 SKILLS 表**必须同序**）。</summary>
		internal static readonly string[] SkillIds =
		{
			"soldier", "mount", "gun", "navy", "archer", "combat", "military", "ninja",
			"build", "farm", "mine", "arthmetic", "etiquette", "debate", "tea", "medical"
		};

		/// <summary>五维 XML 属性名（顺序 = 详情页显示顺序）。</summary>
		internal static readonly string[] DimensionIds =
		{
			"command", "force", "govern", "wisdom", "charm"
		};

		/// <summary>
		/// 五维标签（<c>{=KEY}fallback</c> 原样串，按 <see cref="DimensionIds"/> 同序）。
		/// 🔴 标签由**内容包**在 HeroProfiles.xml 的 &lt;DimensionLabels&gt; 里给——
		///   本类不认识任何具体词汇（铁律 3）；内容包没配时退回字段名。
		/// </summary>
		/// <summary>五维标签（按 <see cref="DimensionIds"/> 同序；来源同上）。</summary>
		public static string[] DimensionLabels
		{
			get
			{
				EnsureLoaded();
				return _dimensionLabels;
			}
		}

		/// <summary>技能标签（按 <see cref="SkillIds"/> 同序；来源同上）。</summary>
		public static string[] SkillLabels
		{
			get
			{
				EnsureLoaded();
				return _skillLabels;
			}
		}

		private static readonly string[] _dimensionLabels = (string[])DimensionIds.Clone();
		private static readonly string[] _skillLabels = (string[])SkillIds.Clone();

		private static string Attr(XmlNode node, string name)
		{
			XmlAttribute a = node.Attributes?[name];
			return a?.Value ?? string.Empty;
		}

		private static int ParseInt(string raw, int fallback)
		{
			return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
				? v : fallback;
		}
	}
}
