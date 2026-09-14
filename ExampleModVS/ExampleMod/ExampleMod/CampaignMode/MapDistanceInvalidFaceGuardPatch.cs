using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 引擎裸调用守卫：<c>DefaultMapDistanceModel.GetClosestSettlementForNavigationMesh(PathFaceRecord)</c>
	/// 拿到**非法导航面**时不判 <c>IsValid()</c> 就往下走 —— 内存缓存（键恒 ≥0）必然未命中，
	/// 于是无条件调原生 <c>SandBox.MapScene.GetNavigationMeshCenterPosition(faceIndex)</c>
	/// （SandBox.dll 第 339 行 <c>_scene.GetNavMeshCenterPosition</c>）
	/// → **AccessViolation（段错误，游戏当场没，无堆栈、无日志）**。
	///
	/// 🔴 根因（太阁地图，2026-09-14 实机）：<c>Settlement.CurrentNavigationFace</c> 的来源是
	///   <c>MapSceneWrapper.GetFaceIndex(GatePosition)</c>（Settlement.cs <c>OnGameInitialized</c>）。
	///   **城门点不落在导航网格上** → 返回 <c>PathFaceRecord.NullFaceRecord</c> = (-1,-1,-1)。
	///   原版/织丰的据点和网格是一起烤出来的，门点必在网格上；太阁的日本图是导入的、据点由脚本铺点，
	///   有若干个铺到了网格外（离线体检：3675 对据点走不通／14 个据点与全世界都不通 = 雷 104）。
	///
	/// 🔴 触发链（1.2.12 反编译实锤，**纯引擎逻辑、与 mod 代码无关**）：
	///   大地图走时间 → DailyTickClan → <c>ClanVariablesCampaignBehavior.UpdateGovernorsOfClan</c>
	///   （给 AI 家族封地挑「手上没带兵」的领主当总督）→ <c>ChangeGovernorAction.Apply</c>
	///   → <c>TeleportHeroAction.ApplyDelayedTeleportToSettlementAsGovernor</c>
	///   → <c>DefaultDelayedTeleportationModel.GetTeleportationDelayAsHours</c>
	///   → <c>GetDistance(IMapPoint, Settlement, 300f, out _)</c>
	///   → 两面不相等 → 本方法 → 原生 → 💥
	///
	/// 处置：守卫放在**唯一收口处**。实证依据（两道独立检查）——
	///   ① 完整反编译 <c>DefaultMapDistanceModel</c>：调 <c>GetNavigationMeshCenterPosition</c> 只有两处，
	///      一处是缓存重建循环（下标恒为 [0, 面总数) 的合法值），另一处就是本方法；
	///   ② 1.2.12 全 DLL 二进制扫描：只有 `TaleWorlds.CampaignSystem.dll`（调用方）与
	///      `SandBox.dll`（方法声明方）出现该名字，无第三方调用者。
	///   → 封住本方法 = 封住全部 AV 入口。**不需要**再去补 SandBox 那一层。
	///   非法面 → 不查原生，返回一个**真实存在的兜底据点**（优先「面主」= 自身 CurrentNavigationFace
	///   等于该面的据点，多主时取离玩家最近者；无主时取全图离玩家最近者）。
	///   ⚠️ 兜底值在几何上是**猜**的，但下游算式
	///   <c>from.Position2D.Distance(to) - closest.GatePosition.Distance(to) + GetDistance(closest, to)</c>
	///   里后两项近似抵消 → 结果 ≈ 两地直线距离，**有界、不崩、不夸张**。
	///
	/// 日志：每个非法面下标只报一次（上限 <see cref="MaxDistinctFaceLogs"/> 条，超出折叠计数），
	///   写明「面下标／非法原因／面主据点清单／本次兜底给了谁」——这份日志就是改地图的清单。
	///
	/// 🔴 退役条件：太阁 Main_map 的导航网格修好、全部据点城门点回到网格上之后，本补丁可删
	///   （**两步法**：先注释掉类上的 [HarmonyPatch] 特性停用 → 文件留着一轮 → 实机验证不崩 → 再删文件）。
	///
	/// 版本：1.2.12 实证该方法存在（`public override Settlement GetClosestSettlementForNavigationMesh(PathFaceRecord)`）；
	///   1.3.15 起该方法已从引擎中删除（二进制 grep 0 命中 —— 模型被重构为 navigation-capability 式）
	///   → 本补丁在 1.3+ 被 Harmony **静默跳过**（字符串目标找不到即不挂），无害，也无需版本宏。
	///
	/// 姊妹补丁：<see cref="MapDistanceNullSettlementGuardPatch"/>（同一模型、另一个裸解引用，雷 107）。
	/// </summary>
	[HarmonyPatch(typeof(DefaultMapDistanceModel), "GetClosestSettlementForNavigationMesh", new[] { typeof(PathFaceRecord) })]
	public static class MapDistanceInvalidFaceGuardPatch
	{
		/// <summary>最多报多少个不同的非法面（超出后只累加计数，避免日志刷屏）。</summary>
		private const int MaxDistinctFaceLogs = 20;

		/// <summary>单条日志里最多列几个面主据点 id。</summary>
		private const int MaxOwnerIdsInLog = 12;

		private static readonly HashSet<int> _loggedFaceIndices = new HashSet<int>();
		private static int _suppressedLogCount;
		private static bool _suppressionNoteWritten;

		/// <summary>导航面总数（场景加载后恒定，缓存避免每次热路径都调一次原生包装）。</summary>
		private static int _faceCount = -1;

		[HarmonyPrefix]
		public static bool Prefix(PathFaceRecord face, ref Settlement __result)
		{
			bool usable;
			try
			{
				usable = IsFaceUsable(face);
			}
			catch
			{
				return true; // 守卫自身出错绝不改变引擎原行为
			}
			if (usable)
			{
				return true;
			}

			try
			{
				__result = ResolveInvalidFace(face);
			}
			catch (System.Exception ex)
			{
				DebugLogger.Log($"[MapFaceGuard] 兜底解析异常（面 {face.FaceIndex}）: {ex.Message}");
				__result = FirstSettlement();
			}
			return false;
		}

		/// <summary>面可用 = 非空记录（FaceIndex != -1）且下标在 [0, 面总数) 内。</summary>
		private static bool IsFaceUsable(PathFaceRecord face)
		{
			if (!face.IsValid())
			{
				return false;
			}
			return face.FaceIndex < GetFaceCount();
		}

		private static int GetFaceCount()
		{
			if (_faceCount > 0)
			{
				return _faceCount;
			}
			IMapScene wrapper = Campaign.Current?.MapSceneWrapper;
			if (wrapper == null)
			{
				return int.MaxValue; // 拿不到面数 → 退化为只靠 IsValid 判定
			}
			int count = wrapper.GetNumberOfNavigationMeshFaces();
			if (count <= 0)
			{
				return int.MaxValue;
			}
			_faceCount = count;
			return count;
		}

		/// <summary>
		/// 非法面兜底：找出「面主」（CurrentNavigationFace 等于该面的据点），取离玩家最近者；
		/// 没有面主（该面不属于任何据点，例如位置异常的部队）则取全图离玩家最近的据点。
		/// 只用纯向量运算，不回调距离模型 —— 避免递归。
		/// </summary>
		private static Settlement ResolveInvalidFace(PathFaceRecord face)
		{
			List<Settlement> owners = null;
			foreach (Settlement s in Settlement.All)
			{
				if (s != null && s.CurrentNavigationFace.FaceIndex == face.FaceIndex)
				{
					if (owners == null)
					{
						owners = new List<Settlement>();
					}
					owners.Add(s);
				}
			}

			Settlement fallback = PickNearestToPlayer(owners) ?? PickNearestToPlayer(null) ?? FirstSettlement();
			LogInvalidFace(face, owners, fallback);
			return fallback;
		}

		/// <summary>在 pool 里取离玩家最近的据点；pool 为 null/空则在全图取。拿不到玩家位置时取第一个（确定性）。</summary>
		private static Settlement PickNearestToPlayer(List<Settlement> pool)
		{
			if (pool != null && pool.Count == 1)
			{
				return pool[0];
			}

			MobileParty main = MobileParty.MainParty;
			IEnumerable<Settlement> source = (pool != null && pool.Count > 0) ? (IEnumerable<Settlement>)pool : Settlement.All;

			Settlement best = null;
			float bestDistSq = float.MaxValue;
			foreach (Settlement s in source)
			{
				if (s == null)
				{
					continue;
				}
				if (main == null)
				{
					return s;
				}
				float d = s.GatePosition.DistanceSquared(main.Position2D);
				if (d < bestDistSq)
				{
					bestDistSq = d;
					best = s;
				}
			}
			return best;
		}

		private static Settlement FirstSettlement()
		{
			return Settlement.All.Count > 0 ? Settlement.All[0] : null;
		}

		private static void LogInvalidFace(PathFaceRecord face, List<Settlement> owners, Settlement fallback)
		{
			if (!_loggedFaceIndices.Contains(face.FaceIndex))
			{
				if (_loggedFaceIndices.Count >= MaxDistinctFaceLogs)
				{
					_suppressedLogCount++;
					if (!_suppressionNoteWritten)
					{
						_suppressionNoteWritten = true;
						DebugLogger.Log($"[MapFaceGuard] 已报满 {MaxDistinctFaceLogs} 个不同非法面，后续只累加计数不再逐条打印。");
					}
					return;
				}
				_loggedFaceIndices.Add(face.FaceIndex);
			}
			else
			{
				return; // 同一面下标只报一次
			}

			string reason = !face.IsValid()
				? "FaceIndex = -1（空面记录：城门点不在导航网格上）"
				: $"FaceIndex 越界（>= 面总数 {GetFaceCount()}）";
			string ownerText = DescribeOwners(owners);

			DebugLogger.Log($"[MapFaceGuard] 拦住一次非法导航面，未调原生（否则必 AccessViolation 段错误）。"
				+ $"面下标={face.FaceIndex}；原因={reason}；面主据点 {ownerText}；"
				+ $"本次兜底返回={(fallback == null ? "null（世界里已无据点）" : fallback.StringId)}。"
				+ $" → 修法：地图编辑器里点 CheckPositions 定位，把这些据点的城门点挪回导航网格（雷 104）。"
				+ $"（同一面下标只报一次，上限 {MaxDistinctFaceLogs} 条）");
		}

		private static string DescribeOwners(List<Settlement> owners)
		{
			if (owners == null || owners.Count == 0)
			{
				return "无（该面不属于任何据点 —— 可能是位置异常的部队）";
			}
			var ids = new List<string>();
			for (int i = 0; i < owners.Count && i < MaxOwnerIdsInLog; i++)
			{
				ids.Add(owners[i].StringId);
			}
			string suffix = owners.Count > MaxOwnerIdsInLog ? $"，…共 {owners.Count} 个" : string.Empty;
			return $"{owners.Count} 个：[{string.Join(", ", ids)}{suffix}]";
		}
	}
}
