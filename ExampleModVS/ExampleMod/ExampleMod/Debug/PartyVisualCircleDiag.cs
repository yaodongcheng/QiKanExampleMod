using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 地图定居点醒目圈（黄圈）诊断（2026-09-09，vanilla 对照发现：Taikou 没有黄圈）。
	///
	/// 反编译基准（1.2.12 SandBox.View.dll PartyVisual + SandBox Main_map 场景实锤）：
	///   - 黄圈 = 定居点胶囊子实体：<tags><tag name="map_settlement_circle"/></tags> + decal_component
	///     material="decal_city_circle_a"（官方场景 119 个 map_settlement_circle —— 每定居点一个，
	///     含 village 特维亚同款黄圈）。
	///   - PartyVisual.RefreshTownPhysicalEntitiesState（AntiCompile 实锤）遍历胶囊 children，
	///     遇到 tag map_settlement_circle → CircleLocalFrame = 实体的圈位（后续 hover 圈/交互定位全靠它）。
	///   - 🔴 Taikou 场景只有 capsule + town_kyoto(mesh) 两个儿童 —— 没 circle/gate/banner 娃 →
	///   黄圈不显示 + CircleLocalFrame=Identity。
	/// 本补丁两枚：①ctor + ②RefreshTownPhysicalEntitiesState（前后缀）——
	///   记录：京胶囊的孩子 tag 清单（缺哪些 vanilla 必备娃）+ gate 可通行性 + CircleLocalFrame 是否生效。
	/// 版本：typeof + 字符串方法名（SandBox.View 已引用；1.2.12/1.5.x 验证过方法名存在，改名 = 静默跳过）。
	/// </summary>
	public class PartyVisualCircleDiag
	{
		private const string CircleTag = "map_settlement_circle";

		// 🔴 类型+方法都用字符串名（运行期解析，无编译期类型身份依赖）：
		// typeof/构造函数直引的补丁曾引发启动崩 "Undefined target method ... CtorDiag::Prefix"（2026-09-09 自踩）。
		// 🔴 圈读取的真实方法 = PartyVisual.OnStartup（18944~按方法体，含 map_settlement_circle 标签遍历）
		//   —— 之前误挂 RefreshTownPhysicalEntitiesState（只切 body flags，无圈读取）——本次纠正。
		[HarmonyPatch("SandBox.View.Map.PartyVisual", "OnStartup")]
		public static class RefreshDiag
		{
			[HarmonyPrefix]
			private static void Prefix(PartyVisual __instance)
			{
				try
				{
					PartyBase party = __instance.PartyBase;
					if (party?.Settlement == null)
					{
						return;
					}
					string settId = party.Settlement.StringId;
					bool travelOk = PartyBase.IsPositionOkForTraveling(party.Settlement.GatePosition);
					GameEntity strategicEntity = __instance.StrategicEntity;

					// 从战略实体（胶囊）遍历全部 children tag（vanilla 同源口：GetChildrenRecursive）
					var tagCounts = new Dictionary<string, int>();
					CollectTagsRecursive(strategicEntity, tagCounts);

					string[] vanillaMustTags = { CircleTag, "main_map_city_gate", "map_banner_placeholder", "bo_town", "town_circle_decal" };
					string missing = string.Join(",", vanillaMustTags.Where(t => !tagCounts.ContainsKey(t)).DefaultIfEmpty("（无缺失）"));
					string present = string.Join(",", tagCounts.Where(kv => vanillaMustTags.Contains(kv.Key)).Select(kv => $"{kv.Key}×{kv.Value}").DefaultIfEmpty("（无）"));

					DebugLogger.Log($"[PartyVisualDiag] OnStartup: settlement={settId} " +
						$"gate=({party.Settlement.GatePosition.X:F1},{party.Settlement.GatePosition.Y:F1}) gateTravelOk={travelOk} " +
						$"strategicEntityFound={(strategicEntity != null)} 必备娃 present=[{present}] missing=[{missing}] " +
						$"totalChildrenTags={tagCounts.Count}");
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[PartyVisualDiag] OnStartup 诊断段异常: {ex.Message}");
				}
			}

			[HarmonyPostfix]
			private static void Postfix(PartyVisual __instance)
			{
				try
				{
					MatrixFrame f = __instance.CircleLocalFrame;
					bool circleHasRealFrame = f.origin.X != 0f || f.origin.Y != 0f;
					DebugLogger.Log($"[PartyVisualDiag] CircleLocalFrame 生效={circleHasRealFrame} " +
						$"origin=({f.origin.X:F1},{f.origin.Y:F1},{f.origin.Z:F1})");
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[PartyVisualDiag] Postfix 诊断段异常: {ex.Message}");
				}
			}

			private static void CollectTagsRecursive(GameEntity entity, Dictionary<string, int> tagCounts)
			{
				if (entity == null)
				{
					return;
				}
				foreach (string tag in entity.Tags)
				{
					if (!tagCounts.ContainsKey(tag))
					{
						tagCounts[tag] = 0;
					}
					tagCounts[tag]++;
				}
				for (int i = 0; i < entity.ChildCount; i++)
				{
					CollectTagsRecursive(entity.GetChild(i), tagCounts);
				}
			}
		}
	}
}
