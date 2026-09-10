using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 通用战役世界本体（自定义 GameType 的战役基类）。
	/// 引擎 Campaign 基类负责世界数据装载（按 SubModule.xml 注册的 IncludedGameTypes 过滤 XML）；
	/// 本类 = 各内容包共享的战役骨架（生命周期/后续太阁与三国共用的玩法行为挂载点）。
	/// 内容包仅需一个 thin 子类（如 <see cref="TaikouCampaign"/>）——引擎按战役类名匹配
	/// IncludedGameTypes（实证：官方 Campaign↔SandBoxCampaign、织丰 ShokuhoCampaign↔类名）。
	/// </summary>
	public class LivingWorldCampaign : Campaign
	{
		/// <summary>当前通用战役（cast 到派生类用；派生类可 new 隐藏，见 TaikouCampaign）。</summary>
		public new static LivingWorldCampaign Current => (LivingWorldCampaign)Campaign.Current;

		public LivingWorldCampaign(CampaignGameMode gameMode)
#if MB2_V1212
			: base(gameMode)
#else
			// 🔴 1.5.0+：Campaign 构造改为 (CampaignGameMode, AdvancedStartOptionsData)（时代开局数据）。
			//   必须传非 null 实例——null 会在 CampaignOptions..ctor → TryGetSeed 解引用 NRE（实机 2026-09-08 16:45）。
			//   空实例 = 无种子/无场景设定 = 引擎默认展开；本分支现仅保证编译 + 启动存活（v1 再接 1.5.x 建号体系）。
			: base(gameMode, new TaleWorlds.CampaignSystem.AdvancedStartOptions.AdvancedStartOptionsData())
#endif
		{
		}

		/// <summary>
		/// 内容包出生点（thin 子类覆写；null = 不改，保持引擎默认位置）。
		/// 🔴 为什么必须由内容包声明、且在**世界创建期**就写入：大地图相机的初始目标 =
		///   主队运行时坐标（反编译实锤：MapCameraView.Initialize 读 MobileParty.MainParty.Position2D），
		///   而这次读取发生在「建号完成 → 推入地图状态」那一步——比建号完成回调更早。
		///   所以坐标只要在创世界时就写好，相机天然对准玩家，不需要任何事后 teleport。
		///   时点安全性（Campaign.DoLoadingForGameType 的 NewCampaign 分支顺序实锤）：
		///   InitializeMainParty（引擎把主队放到默认坐标）→ … → OnNewGameCreated（本类写入）→ 建号。
		///   且 LoadMapScene 在更早的 LoadVisualsThirdState，写入时地图已加载 → 导航面能正常算出。
		/// 不引引擎默认值 Campaign.DefaultStartingPosition 的原因：该属性 1.3.15 起已被移除
		///   （1.2.12 = 1 命中 / 1.3.15、1.4.6、1.5.1 = 0 命中），基类引用它会挂掉 1.5.x 编译。
		/// </summary>
		public virtual Vec2? StartingPosition => null;

#if MB2_V1212
		protected override void OnInitialize()
		{
			base.OnInitialize();
			// 🔴 读档早退（2026-09-08 实机 21:44）——读档时对象表未恢复（GetObjectTypeList<T>() 为 null）：
			//   下面的补载/注册只服务新建档；读档链走官方恢复（同族坑：partial-followup 时点 GetObjectTypeList null）。
			if (CampaignGameLoadingType == Campaign.GameLoadingType.SavedCampaign)
			{
				return;
			}

			// 🔴 EquipmentRosters 段补载（第 5 颗雷）：该段官方**只在读档链**加载（InitializeDefaultCampaignObjects），
			//   新战役不加载 → neutral roster 为裸桩 → Hero.SetInitialValues 的 fallback NRE → Heroes 段被吞。
			//   补载（读档同序）即可。⚠️ 这段是真修复，不是诊断，勿当遗留删除。
			try
			{
				MBObjectManager.Instance.LoadXML("EquipmentRosters");
				DebugLogger.Log("[LWN-campaign] EquipmentRosters 段已补载");
			}
			catch (Exception exEq)
			{
				DebugLogger.Log($"[LWN-campaign] EquipmentRosters 段补载 FAILED: {exEq.Message}");
			}

			// 🔴 家宅/王国家园置位（第 8 颗雷）：必须挂在 partial-follow-up 时点——
			//   引擎 Kingdom.OnNewGameCreated 会把 InitialHomeLand 写成 Leader.HomeSettlement（此刻多为 null），
			//   而消费方 HeroSpawnCampaignBehavior 在其后运行 ⇒ 置位要落在「引擎洗白之后、HeroSpawn 之前」这个窗口。
			//   OnInitialize 里置位会被引擎覆盖（上一轮教训）。
			try
			{
				CampaignEvents.OnNewGameCreatedPartialFollowUpEvent.AddNonSerializedListener(this, OnNewGameCreatedPartialFollowUp);
			}
			catch (Exception exReg)
			{
				DebugLogger.Log($"[LWN-campaign] partial-followup 注册 FAILED: {exReg.Message}");
			}

			// ── 以下为**已退役**的一次性诊断/兜底（2026-09-10 按诊断生命周期纪律下线，勿恢复）──
			//   · 世界规模 dump（Clan/Settlement/Hero/CharacterObject/Models 探针，原 [LWN-dump] 块约 120 行）：
			//     同类检查改由离线脚本承担 —— check_taikou_xml_references（交叉引用/列表污染）、
			//     check_culture_references（文化悬空 + 角色模板文化属性）、check_scene_entities（场景必备实体）、
			//     check_culture_text_variants / check_language_registration / check_scene_consumables。
			//   · CultureTemplateNullFix（文化模板列表 null→空）：数据侧已治本（拷贝文件 1835 处原版文化引用
			//     → 自家文化，见 Scripts/sanitize_taikou_cultures.py），不变量由 check_culture_references.py 常驻守。
			//     ⚠️ 2026-09-10 14:50 曾复发（InitializeCompanionTemplateList NRE）→ 真因是雷 52（名字池元素里夹注释），
			//     纯数据修即解决。**禁止恢复已退役的兜底补丁**。
		}

		/// <summary>
		/// 新档创建期的分步修复点（引擎对本事件循环调用 100 次，i=0 最早）。
		/// 承载两件事：①出生点置位（相机正确性）②家宅/王国家园置位（防领主刷部队 NRE）。
		/// </summary>
		private void OnNewGameCreatedPartialFollowUp(CampaignGameStarter starter, int i)
		{
			try
			{
				// 🔴 出生点提前置位（2026-09-10）——相机正确性的正解，替代已退役的 MapScreenCameraPatch：
				//   地图相机的初始目标 = 主队坐标（MapCameraView.Initialize 读 MainParty.Position2D），
				//   而它的读取时点早于建号完成回调 → 只有在这里（世界创建期）写好才赶得上。
				if (i == 0 && MobileParty.MainParty != null)
				{
					Vec2? spawn = StartingPosition;
					if (spawn.HasValue)
					{
						MobileParty.MainParty.Position2D = spawn.Value;
						DebugLogger.Log($"[LWN-campaign] 出生点置位 MainParty.Position2D=({spawn.Value.X:F1},{spawn.Value.Y:F1})" +
							"（世界创建期写入 → 早于地图相机初始化，相机天然对准玩家）");
					}
				}

				// 🔴 时点注意：GetObjectTypeList<T>() 在 partial-followup 时点返回 null（ObjectTypeRecords 命中失败）
				//   → 一律改用 Campaign.Current 的战役集合（Kingdom/Clan 的权威列表，时点无关）。
				// 🔴 根因（雷 53，2026-09-10 定位）：引擎自己算不出家宅——距离缓存过期（只有 1 个据点有场景实体
				//   → 据点对 0 对 → 全局最大据点距恒 0 → 打分末步 `1 − 0/0 = NaN` → `NaN > 0` 恒假 → 一个都选不中）。
				//   治本 = 据点齐全 + 重生成 settlements_distance_cache.bin（T4）；在那之前这段置位不能删
				//   （InitialHomeLand 为 null 会让领主刷部队时 NRE：Kingdom.InitialPosition 裸解引用）。
				var initHomeProp = typeof(Kingdom).GetProperty("InitialHomeLand");
				var initHomeSetter = initHomeProp?.GetSetMethod(true);
				foreach (var k3 in Campaign.Current.Kingdoms)
				{
					if (initHomeSetter != null && k3.InitialHomeLand == null && Settlement.All.Count > 0)
					{
						initHomeSetter.Invoke(k3, new object[] { Settlement.All[0] });
						DebugLogger.Log($"[LWN-campaign] Kingdom {k3.StringId} InitialHomeLand 置位为王都（partial-followup）");
					}
				}
				// 🔴 2026-09-10 修正（实机日志实锤 + 反编译定位）：原写法 `c4.UpdateHomeSettlement(c4.Settlements[0])`
				//   是**空转**——`Clan.UpdateHomeSettlement(据点)` 在 HomeSettlement == null 时**忽略传入值**，改为遍历
				//   所有要塞按 FindSettlementScoreForBeingHomeSettlement 打分挑一个；建世界早期该打分一个都挑不中
				//   → 函数直接 return，什么都没做（症状：100 步 partial-followup 打 100 行日志 = 一次都没生效）。
				//   改法同 Kingdom 段：反射直写 HomeSettlement 私有 setter（值 = 该家族第一个据点；家族单城时与引擎打分同解）。
				//   英雄侧不必补循环：Hero.HomeSettlement 的 getter 在 _homeSettlement == null 时会自行惰性重算。
				var clanHomeProp = typeof(Clan).GetProperty("HomeSettlement");
				var clanHomeSetter = clanHomeProp?.GetSetMethod(true);
				foreach (var c4 in Campaign.Current.Clans)
				{
					if (clanHomeSetter != null && c4.HomeSettlement == null && c4.Settlements.Count > 0)
					{
						clanHomeSetter.Invoke(c4, new object[] { c4.Settlements[0] });
						DebugLogger.Log($"[LWN-campaign] Clan {c4.StringId} HomeSettlement 置位为 {c4.Settlements[0].StringId}（partial-followup）");
					}
				}
				// 注：「京名流兜底生成」（Notables 空则造一个 Merchant）已于 2026-09-10 退役——
				//   根因（第 9 颗雷）已在数据侧治本（文化 N&W 模板池补 merchant/artisan），运行日志长期 0 次触发。
			}
			catch (Exception exHome)
			{
				DebugLogger.Log($"[LWN-campaign] 新档分步修复 FAILED: {exHome.Message}");
			}
		}
#else
		// 🔴 1.5.x：战役模式暂不接入（裁定：CampaignModeActivator——该版本机不装 Taikou 数据包 = 纯功能包，
		//   本类不会被实例化）。本支只负责编译通过；1.2.12 独有 API（Clan.InitialPosition /
		//   Kingdom.InitialHomeLand / Clan.UpdateHomeSettlement / HeroCreator.CreateHeroAtOccupation /
		//   GameModels.SettlementConsumptionModel）在 1.5.2 已改名/移除（等价物：InitialHomeSettlement /
		//   SetInitialHomeSettlement / HeroCreator.CreateNotable）——v1 接入 1.5.x 建号体系（CharacterCreationManager）时
		//   按 1.5.2 等价 API 重写本类。
		protected override void OnInitialize()
		{
			// 🔴 第 5 颗雷（1.5.2 版，2026-09-08）：EquipmentRosters 段新战役不加载——反编译实锤：
			//   Campaign.InitializeDefaultCampaignObjects（官方读档链）里才 LoadXML("EquipmentRosters")；
			//   Campaign.OnInitialize 早期执行 InitializeDefaultEquipments →
			//   GetObject("default_battle_equipment_roster_neutral") 为 null → .DefaultEquipment NRE。
			//   （1.2.12 同因，修复同式。）补载（读档同序）→ 再走 base.OnInitialize。
			try
			{
				MBObjectManager.Instance.LoadXML("EquipmentRosters");
			}
			catch (Exception exEq)
			{
				DebugLogger.Log($"[LWN-campaign] EquipmentRosters 段补载 FAILED: {exEq.Message}");
			}
			base.OnInitialize();
		}
#endif
	}
}
