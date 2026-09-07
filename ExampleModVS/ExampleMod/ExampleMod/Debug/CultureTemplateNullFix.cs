using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 文化「模板列表」空壳修复（通用，2026-09-07 用户裁定：文化条目先行加固 —— 即使桩文化进来了也不炸）。
	///
	/// 背景：自定义 GameType 下官方 SPCultures 段被 IncludedGameTypes 白名单过滤，但 Taikou 拷贝文件
	///   （物品/工艺件/音乐/装备模板）带 1800+ 处 Culture.&lt;原版文化&gt; 引用 → XML 引用解析经
	///   MBObjectManager.GetPresumedObject 以「引用创建」方式生成裸文化对象（只记 id，从未 Deserialize）
	///   → NotableAndWandererTemplates/LordTemplates 等模板列表为 null
	///   → 引擎 CompanionsCampaignBehavior.InitializeCompanionTemplateList 无 null 保护 → NRE（实机 2026-09-07 22:32）。
	///
	/// 本类 = 加固层：战役启动时给所有文化的三个模板列表补空列表/剔除 null 条目。
	///   数据层根治 = Scripts/check_taikou_xml_references.py + sanitize_taikou_cultures.py（引用一律自给）；
	///   任何内容包（三国等）复合同样问题由本类兜底 —— 通用基座职责。
	///
	/// 跨版本策略：属性名以运行时反射存在性为准（1.2.12 = CampaignSystem.dll CultureObject
	///   { get; private set; }；1.5.x 属性名/归属若变 → GetProperty null → 跳过仅记日志，不崩）。
	/// 防御：任意异常只记日志绝不抛出。
	/// </summary>
	public static class CultureTemplateNullFix
	{
		/// <summary>要保证非空的模板列表属性（引擎行为消费点：CompanionsCampaignBehavior 的 N&W；
		/// LordsCampaignBehavior 等消费 LordTemplates/RebelliousHeroTemplates）。</summary>
		private static readonly string[] TargetProperties =
		{
			"NotableAndWandererTemplates",
			"LordTemplates",
			"RebelliousHeroTemplates",
		};

		public static int Apply()
		{
			int fixedCount = 0;
			try
			{
				foreach (CultureObject culture in MBObjectManager.Instance.GetObjectTypeList<CultureObject>())
				{
					foreach (string propName in TargetProperties)
					{
						fixedCount += FixCultureList(culture, propName);
					}
				}
				DebugLogger.Log($"[CultureTemplateNullFix] 完成：共修复 {fixedCount} 处文化模板列表（null→空 / 剔除 null 条目）。");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[CultureTemplateNullFix] 整体异常（放弃本轮，不抛出）: {ex.Message}");
			}
			return fixedCount;
		}

		private static int FixCultureList(CultureObject culture, string propName)
		{
			try
			{
				string cultureId = culture.StringId ?? "?";
				// 跨版本反射：属性不存在（改名/挪库）→ 跳过（记日志），与 AgentDamageModelCultureNullFix 同款防御
				PropertyInfo prop = typeof(CultureObject).GetProperty(propName,
					BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
				if (prop == null)
				{
					DebugLogger.Log($"[CultureTemplateNullFix] 属性 {propName} 在本版本不存在，跳过（无加固）。");
					return 0;
				}

				IEnumerable current = prop.GetValue(culture) as IEnumerable;
				if (current == null)
				{
					// null -> 空列表（MBReadOnlyList 公开构造已验证 1.2.12）
					SetViaReflection(prop, culture, new MBReadOnlyList<CharacterObject>(new List<CharacterObject>()));
					DebugLogger.Log($"[CultureTemplateNullFix] Culture {cultureId} | {propName}: null → 空列表。");
					return 1;
				}

				// 列表存在但可能含 null 条目（引用解析失败的轮廓）→ 剔除 null 一致性重建
				List<CharacterObject> cleaned = null;
				foreach (object entry in current)
				{
					if (entry == null)
					{
						cleaned = cleaned ?? new List<CharacterObject>();
					}
					else if (cleaned == null)
					{
						cleaned = new List<CharacterObject> { (CharacterObject)entry };
					}
					else
					{
						cleaned.Add((CharacterObject)entry);
					}
				}
				if (cleaned != null && cleaned.Count != CountOf(current))
				{
					SetViaReflection(prop, culture, new MBReadOnlyList<CharacterObject>(cleaned));
					DebugLogger.Log($"[CultureTemplateNullFix] Culture {cultureId} | {propName}: 剔除 null 条目后重建。");
					return 1;
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[CultureTemplateNullFix] Culture {culture?.StringId} | {propName} 修复异常: {ex.Message}");
			}
			return 0;
		}

		private static int CountOf(IEnumerable list)
		{
			int n = 0;
			foreach (object _ in list) { n++; }
			return n;
		}

		private static void SetViaReflection(PropertyInfo prop, CultureObject culture, object value)
		{
			// private setter：PropertyInfo.SetValue 只走 public setter 会失败，必须显式取非公共 setter
			MethodInfo setter = prop.GetSetMethod(true);
			if (setter == null)
			{
				return;
			}
			setter.Invoke(culture, new object[] { value });
		}
	}
}
