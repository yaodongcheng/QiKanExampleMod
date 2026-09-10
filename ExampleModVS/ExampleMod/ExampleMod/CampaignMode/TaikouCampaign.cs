using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 太阁内容包的 thin 战役**基类**（时代无关；三单元架构铁则 3）。
	/// 🔴 时代切换模型（2026-09-10 spike，见 plans/时代剧本切换-验证.md）：
	///   引擎的 XML 段过滤键 = **战役类名**（`GameType.GameTypeStringId => GetType().Name`）→
	///   **每个时代必须有自己的具体类**（类名即该时代的 GameType 字符串）：
	///     TaikouCampaign1560 → SubModule.xml 里 &lt;GameType value="TaikouCampaign1560"/&gt;
	///     TaikouCampaign1582 → <GameType value="TaikouCampaign1582"/>
	///   本类**不是**任何时代的 GameType（无 SubModule 段引用它，仅作具体类的父类）——
	///   有意保留原名：老存档里的对象类型仍能对上，且两时代共享的字段/出生点只写一份。
	/// </summary>
	public abstract class TaikouCampaign : LivingWorldCampaign
	{
		protected TaikouCampaign(CampaignGameMode gameMode)
			: base(gameMode)
		{
		}

		/// <summary>
		/// 玩家出生点（日本图世界米坐标 = 京（969.4, 421.6）门前偏外侧 (985, 428)——离城区一箭地，
		/// 开局可见城塔立面；初版 (973,421) 落在城圈内贴城墙，视线被塔占位模型挡（2026-09-08 用户三次确认）。
		/// 🔴 写入时点 = 世界创建期的 OnNewGameCreatedPartialFollowUp（父类统一处理）——引擎
		///   Campaign.DefaultStartingPosition 非 virtual（两个调用点都锁定基类实现）且 1.3.15 起已被移除，
		///   而地图相机的初始目标在建号完成「推入地图状态」时就已读取主队坐标 → 创世界期写入才赶得上，
		///   相机因此天然对准玩家（原 MapScreenCameraPatch 已按此删除）。
		/// </summary>
		public override Vec2? StartingPosition => TaikouStartingPosition;

		/// <summary>出生点常量（建号内容类同用；见上条说明）。</summary>
		public static Vec2 TaikouStartingPosition => new Vec2(985f, 428f);
	}
}
