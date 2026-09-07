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
			: base(gameMode)
		{
		}

		protected override void OnInitialize()
		{
			base.OnInitialize();
			// 🔴 诊断 + 加固（2026-09-07 排雷 T1 结案）：
			//   自定义 GameType 下官方段被过滤 → Taikou 拷贝文件的文化引用经 GetPresumedObject
			//   创建「裸文化桩」（模板列表 null）→ 引擎 InitializeCompanionTemplateList NRE。
			//   ① 诊断：世界规模摘要（T2/T3 排雷仍要）；② 加固：CultureTemplateNullFix（通用基座兜底）。
			try
			{
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
				DebugLogger.Log($"[LWN-dump] Kingdom kingdom_oda | RulingClan={(k?.RulingClan != null ? k.RulingClan.StringId : "<NULL>")}");
				// 加固：文化模板列表 null → 空（NRE 兜底；数据侧根治见 Scripts/check_taikou_xml_references.py）
				CultureTemplateNullFix.Apply();
			}
			catch (Exception ex)
			{
				TaleWorlds.Library.Debug.PrintError($"[LWN-dump] diagnostics failed: {ex.Message}");
				DebugLogger.Log($"[LWN-dump] diagnostics FAILED: {ex.Message}");
			}
		}
	}
}
