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

namespace LivingWorldNpcs
{
	/// <summary>
	/// 大地图导航面（navmesh）探针（2026-09-09，京点击不开定居点界面排查）。
	///
	/// 背景：据点点击被吞的引擎链（SandBox.View MapScreen.HandleLeftMouseButtonClick 反编译实锤）：
	///   settlement 点击 → mapEntity.InteractionPosition（= Settlement.GatePosition）→
	///   GetFaceIndex(门位置) → DoesPathExistBetweenFaces(该面, 玩家当前面, false) ——
	///   任意一环失败 = 点击被静默 return（无任何提示）。
	///   本命令验证三环：①目标点是否有导航面 ②玩家当前面是否有效 ③两面是否同一陆块
	///   （AreFacesOnSameIsland = DoesPathExistBetweenFaces 的可控近似，界面同判定）。
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
				PathFaceRecord targetFace = map.GetFaceIndex(target);
				PathFaceRecord playerFace = MobileParty.MainParty.CurrentNavigationFace;
				bool sameIsland = targetFace.IsValid() && playerFace.IsValid() &&
					map.AreFacesOnSameIsland(targetFace, playerFace, false);

				TerrainType terrain = map.GetFaceTerrainType(targetFace);
				string line = $"[NavProbe] target=({target.X:F1},{target.Y:F1}) faceValid={targetFace.IsValid()} faceIndex={targetFace.FaceIndex} " +
					$"terrain={terrain} | playerPos=({MobileParty.MainParty.Position2D.X:F1},{MobileParty.MainParty.Position2D.Y:F1}) " +
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
