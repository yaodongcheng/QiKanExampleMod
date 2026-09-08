using System;
using TaleWorlds.CampaignSystem;
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

#if MB2_V1212
		protected override void OnInitialize()
		{
			base.OnInitialize();
			// 🔴 诊断 + 加固（2026-09-07 排雷 T1 结案）：
			//   自定义 GameType 下官方段被过滤 → Taikou 拷贝文件的文化引用经 GetPresumedObject
			//   创建「裸文化桩」（模板列表 null）→ 引擎 InitializeCompanionTemplateList NRE。
			//   ① 诊断：世界规模摘要（T2/T3 排雷仍要）；② 加固：CultureTemplateNullFix（通用基座兜底）。
			try
			{
				// 🔴 第 5 颗雷继续：LoadXML("Heroes") 静默失败（内部 try/catch 吞异常）→ 分离式重载（GetMerged+LoadXml 不吞，真相现形）
				// 🔴 第 5 颗雷收尾：EquipmentRosters 段官方只在读档时加载（InitializeDefaultCampaignObjects 7952）
				//   ——新战役不加载 → neutral roster 为裸桩 → Hero.SetInitialValues fallback NRE → Heroes 段被吞。
				//   补载（读档同序）→ 再分离式重载 Heroes。
				try
				{
					MBObjectManager.Instance.LoadXML("EquipmentRosters");
					DebugLogger.Log("[LWN-dump] EquipmentRosters 段已补载");
				}
				catch (Exception exEq)
				{
					DebugLogger.Log($"[LWN-dump] EquipmentRosters 段补载 FAILED: {exEq.Message}");
				}
				// 🔴 Heroes 由引擎主链加载（SandBoxSubModule.RegisterSubModuleObjects，2026-09-08 用户纠正：OnInitialize 抢载 = 半成品互搏）；
				//   不抢跑——只观测：主链结果见下方 Hero dump（ready=True = 主链成功）
				var objs = MBObjectManager.Instance;
				int nClan = 0;
				foreach (var c in objs.GetObjectTypeList<Clan>())
				{
					nClan++;
					string cid = c.StringId ?? "?";
					string cult = c.Culture != null ? c.Culture.StringId : "<NULL>";
					string kingdom = c.Kingdom != null ? c.Kingdom.StringId : "<NONE>";
					int sCount = 0;
					foreach (var s in c.Settlements) { sCount++; _ = s; }
					Vec2 ip = c.InitialPosition;
					DebugLogger.Log($"[LWN-dump] Clan {cid} | culture={cult} | kingdom={kingdom} | settlements={sCount} | initPos=({ip.X},{ip.Y})");
				}
				int nSett = 0;
				foreach (var s in Settlement.All) { nSett++; _ = s; }
				DebugLogger.Log($"[LWN-dump] Settlement.All.Count={nSett} | Clan.Count={nClan}");
				if (Campaign.Current.Models?.CharacterDevelopmentModel != null)
				{
					DebugLogger.Log("[LWN-dump] Models.CharacterDevelopmentModel=OK");
				}
				else
				{
					DebugLogger.Log("[LWN-dump] Models.CharacterDevelopmentModel=<NULL> (!!)");
				}
				// 🔴 第 12 雷探针：模型族存在性（TradeItemPrice/SettlementConsumption——工坊定价链依赖）
				try
				{
					var m = Campaign.Current.Models;
					DebugLogger.Log($"[LWN-dump] Model TradeItemPriceFactor={(m?.TradeItemPriceFactorModel != null ? "OK" : "<NULL>")} | SettlementConsumption={(m?.SettlementConsumptionModel != null ? "OK" : "<NULL>")}");
				}
				catch (Exception exM)
				{
					DebugLogger.Log($"[LWN-dump] 模型探针失败: {exM.Message}");
				}
				// 🔴 第 5 颗雷（Kingdom.OnNewGameCreated NRE = Leader null）：dump Hero→Clan 链定位断环
				int nHero = 0;
				foreach (var h in Campaign.Current.AliveHeroes) { nHero++; _ = h; }
				DebugLogger.Log($"[LWN-dump] Heroes.Count={nHero}");
				foreach (var h2 in objs.GetObjectTypeList<Hero>())
				{
					bool ready = h2.IsReady;
					string hClan = h2.Clan != null ? h2.Clan.StringId : "<NULL>";
					string heroChr = h2.CharacterObject != null ? h2.CharacterObject.StringId : "<NULL>";
					DebugLogger.Log($"[LWN-dump] Hero {h2.StringId} | ready={ready} | clan={hClan} | character={heroChr}");
				}
				foreach (var c2 in objs.GetObjectTypeList<Clan>())
				{
					string cl = c2.Leader != null ? c2.Leader.StringId : "<NULL>";
					string leaderClan = c2.Leader?.Clan != null ? c2.Leader.Clan.StringId : "<NULL>";
					DebugLogger.Log($"[LWN-dump] Clan {c2.StringId} | leader={cl} | leader.clan={leaderClan}");
				}
				var k = objs.GetObject<Kingdom>("kingdom_oda");
				DebugLogger.Log($"[LWN-dump] Kingdom kingdom_oda | RulingClan={(k?.RulingClan != null ? k.RulingClan.StringId : "<NULL>")} | InitialHomeLand={(k?.InitialHomeLand != null ? k.InitialHomeLand.StringId : "<NULL>")}");
				// 🔴 第 8 颗雷（Kingdom.InitialPosition NRE）：引擎 Kingdom.OnNewGameCreated 会把 InitialHomeLand 洗成
				//   Leader.HomeSettlement（=null，织田无家——GovernorOf 链全空）——置位必须在 partial-follow-up 时点
				//   （引擎洗白之后、HeroSpawn 之前）；OnInitialize 置位会被洗掉（上一轮教训）。
				//   InitialHomeLand private setter → 反射（CultureTemplateNullFix 同款手法，跨版本安全）。
				try
				{
					CampaignEvents.OnNewGameCreatedPartialFollowUpEvent.AddNonSerializedListener(this, OnNewGameCreatedPartialFollowUp);
				}
				catch (Exception exReg)
				{
					DebugLogger.Log($"[LWN-dump] partial-followup 注册 FAILED: {exReg.Message}");
				}
				// 加固：文化模板列表 null → 空（NRE 兜底；数据侧根治见 Scripts/check_taikou_xml_references.py）
				CultureTemplateNullFix.Apply();
				// 🔴 第 10 雷探针：CharacterObject.All 全貌（总数 + 前 12 个 id——确定 46 模板在不在 record）
				try
				{
					int chCount = 0;
					foreach (var ch in CharacterObject.All)
					{
						if (chCount < 12)
						{
							DebugLogger.Log($"[LWN-dump] Char[{chCount}] {ch.StringId} | occ={ch.Occupation} | lvl={ch.Level} | cult={(ch.Culture != null ? ch.Culture.StringId : "<NULL>")}");
						}
						chCount++;
					}
					DebugLogger.Log($"[LWN-dump] CharacterObject.All 总数={chCount}");
					var cg0 = MBObjectManager.Instance.GetObject<CharacterObject>("caravan_guard");
					DebugLogger.Log(cg0 != null
						? $"[LWN-dump] GetObject(caravan_guard)=存在 occ={cg0.Occupation} lvl={cg0.Level} ready={cg0.IsReady}"
						: "[LWN-dump] GetObject(caravan_guard)=<NULL>");
				}
				catch (Exception exCG)
				{
					DebugLogger.Log($"[LWN-dump] CaravanGuard 探针失败: {exCG.Message}");
				}
			}
			catch (Exception ex)
			{
				TaleWorlds.Library.Debug.PrintError($"[LWN-dump] diagnostics failed: {ex.Message}");
				DebugLogger.Log($"[LWN-dump] diagnostics FAILED: {ex.Message}");
			}
		}

		/// <summary>
		/// 第 8 颗雷（Kingdom.InitialPosition NRE）修复点：partial-follow-up 时点置位王国家宅。
		/// 时点依据：引擎 Kingdom.OnNewGameCreated 已执行（InitialHomeLand 被洗成 Leader.HomeSettlement=null），
		/// HeroSpawnCampaignBehavior（本方法的消费方）尚未运行——此刻置位恰好落进消费窗口。
		/// </summary>
		private void OnNewGameCreatedPartialFollowUp(CampaignGameStarter starter, int i)
		{
			try
			{
				// 🔴 第 12 雷探针：GetObjectTypeList<ItemObject>() 在 partial 时点的状态（i==10 FillItemsInAllCategories 依赖它）
				if (i <= 1 || i == 10)
				{
					var tl = MBObjectManager.Instance.GetObjectTypeList<ItemObject>();
					DebugLogger.Log($"[LWN-dump] partial i={i} | GetObjectTypeList<ItemObject>={(tl != null ? "非null count=" + tl.Count : "<NULL>")}");
				}
				// 🔴 GetObjectTypeList<T>() 在 partial-followup 时点返回 null（ObjectTypeRecords 命中失败——待记坑）；
				//   改用 Campaign.Current 战役集合（Kingdom/Clan 的权威列表，时点无关）
				var initHomeProp = typeof(Kingdom).GetProperty("InitialHomeLand");
				var initHomeSetter = initHomeProp?.GetSetMethod(true);
				foreach (var k3 in Campaign.Current.Kingdoms)
				{
					if (initHomeSetter != null && k3.InitialHomeLand == null && Settlement.All.Count > 0)
					{
						initHomeSetter.Invoke(k3, new object[] { Settlement.All[0] });
						DebugLogger.Log($"[LWN-dump] Kingdom {k3.StringId} InitialHomeLand 置位为王都（partial-followup）");
					}
				}
				foreach (var c4 in Campaign.Current.Clans)
				{
					if (c4.HomeSettlement == null && c4.Settlements.Count > 0)
					{
						c4.UpdateHomeSettlement(c4.Settlements[0]);
						DebugLogger.Log($"[LWN-dump] Clan {c4.StringId} HomeSettlement 置位（partial-followup）");
					}
				}
				// 🔴 第 9 颗雷（BuildWorkshopsAtGameStart NRE：城镇无 Notables → GetNotableOwnerForWorkshop 返回 null）
				//   根因链：CreateHeroAtOccupation 从文化 N&W 模板池挑职业——原 N&W 只有 Lord → merchant/artisan 生不出。
				//   已修复：数据层 N&W 补 merchant/artisan 模板（官方行为 SpawnNotablesAtGameStart 之后即可自行生成）。
				//   此处兜底 = 京 Notables 空才造（幂等防止官方行为已造时再重复）
				var notableTarget = Settlement.All.Count > 0 ? Settlement.All[0] : null;
				if (notableTarget != null && notableTarget.Notables.Count == 0)
				{
					var made = TaleWorlds.CampaignSystem.HeroCreator.CreateHeroAtOccupation(Occupation.Merchant, notableTarget);
					DebugLogger.Log($"[LWN-dump] 京名流兜底生成: {made?.StringId ?? "null（模板池无 Merchant）"}");
				}
			}
			catch (Exception exHome)
			{
				DebugLogger.Log($"[LWN-dump] 家宅置位 FAILED: {exHome.Message}");
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
			base.OnInitialize();
			DebugLogger.Log("[LWN-dump] LivingWorldCampaign: 1.5.x 空壳 OnInitialize（战役模式暂不接入）");
		}
#endif
	}
}
