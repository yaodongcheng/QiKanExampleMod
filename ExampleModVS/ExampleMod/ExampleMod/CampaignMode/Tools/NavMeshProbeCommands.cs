using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 大地图导航面（navmesh）探针（2026-09-09，京点击不开定居点界面排查）。
	///
	/// 背景：据点点击被吞的引擎链（SandBox.View MapScreen.HandleLeftMouseButtonClick 反编译实锤）：
	///   settlement 点击 → mapEntity.InteractionPosition（= Settlement.GatePosition）→
	///   GetFaceIndex(门位置) → DoesPathExistBetweenFaces(该面, 玩家当前面, false) ——
	///   任意一环失败 = 点击被静默 return（无任何提示）。
	///   本命令验证三环：①目标点是否有导航面 ②玩家当前面是否有效 ③两面是否同一陆块
	///   （同岛判定 = DoesPathExistBetweenFaces 的可控近似，界面同判定；
	///     1.3.0+ AreFacesOnSameIsland 已移除 → 走 V.SameIsland 的探路替代）。
	///
	/// 用法（游戏内 ~ 控制台，返回文本纯英文；诊断详情走 DebugLogger 中文）：
	///   custom.probe_face                  # 默认：京 gate (969.424, 421.563) ↔ 玩家当前位置
	///   custom.probe_face 800 500          # 任意坐标 ↔ 玩家当前位置
	/// </summary>
	public class NavMeshProbeCommands
	{
		/* 🔴 控制台命令注册委托签名 = (List<string>) → 返回 string；方法必须 public static
		   （签名/可见性不符 = 启动时 CollectCommandLineFunctions 绑定失败 ArgumentException（2026-09-09 自踩））
		   参数用 List 索引取值（与 string[] 用法一致）。 */
		[CommandLineFunctionality.CommandLineArgumentFunction("probe_face", "custom")]
		public static string ProbeFace(List<string> args)
		{
			try
			{
				Vec2 target;
				if (args.Count >= 2 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float px) &&
					float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float py))
				{
					target = new Vec2(px, py);
				}
				else
				{
					target = new Vec2(969.424f, 421.563f); // 京都 gate（默认探针点）
				}

				IMapScene map = Campaign.Current.MapSceneWrapper;
				Vec2 playerPos = V.Pos(MobileParty.MainParty);
				PathFaceRecord targetFace = V.FaceIndex(map, target);
				PathFaceRecord playerFace = MobileParty.MainParty.CurrentNavigationFace;
				// 同岛判定：1.2.12 = AreFacesOnSameIsland；1.3.0+ 该 API 已移除 → V.SameIsland 改用探路替代。
				bool sameIsland = targetFace.IsValid() && playerFace.IsValid() &&
					V.SameIsland(map, targetFace, target, playerFace, playerPos);

				TerrainType terrain = map.GetFaceTerrainType(targetFace);
				string line = $"[NavProbe] target=({target.X:F1},{target.Y:F1}) faceValid={targetFace.IsValid()} faceIndex={targetFace.FaceIndex} " +
					$"terrain={terrain} | playerPos=({playerPos.X:F1},{playerPos.Y:F1}) " +
					$"playerFaceValid={playerFace.IsValid()} playerFaceIndex={playerFace.FaceIndex} sameIsland={sameIsland} " +
					$"gate=({Settlement.Find("town_kyoto")?.GatePosition.X:F1},{Settlement.Find("town_kyoto")?.GatePosition.Y:F1})";
				DebugLogger.Log(line);
				return $"target_face_valid={targetFace.IsValid()} player_face_valid={playerFace.IsValid()} same_island={sameIsland} total_faces={map.GetNumberOfNavigationMeshFaces()}";
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[NavProbe] 探针异常: {ex}");
				return $"probe_failed: {ex.Message}";
			}
		}
	}
}
