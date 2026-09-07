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
			// v0：占位。后续在此初始化通用玩法层（内容包行为/列表挂载）。
			// 🔴 诊断点（2026-09-07）：Clan.ValidateInitialPosition NRE——dump 世界状态（DebugLogger 走 StoryEngine_RuntimeLog.txt）
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
				// (2026-09-07) 二次装载实验已完成使命（结论：Settlement.All=0 根因=settlements.xml 未闭合，已修）——移除
			}
			catch (Exception ex)
			{
				TaleWorlds.Library.Debug.PrintError($"[LWN-dump] dump failed: {ex.Message}");
				DebugLogger.Log($"[LWN-dump] dump FAILED: {ex.Message}");
			}
		}
	}
}
