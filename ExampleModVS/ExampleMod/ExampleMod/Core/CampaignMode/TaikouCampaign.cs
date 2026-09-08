using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 日本战国内容包的 thin 战役适配类（三单元架构铁则 3：每内容包 = LWN 里 3 行）。
	/// 存在的意义 = 类名与 Taikou 数据包 SubModule.xml 的 <GameType value="TaikouCampaign"/> 匹配
	/// （引擎 IncludedGameTypes 按战役类名过滤 XML 数据）；骨架全部来自父类 LivingWorldCampaign。
	/// </summary>
	public class TaikouCampaign : LivingWorldCampaign
	{
		public TaikouCampaign(CampaignGameMode gameMode)
			: base(gameMode)
		{
		}

		/// <summary>
		/// 玩家出生点（日本图世界米坐标 = 京（969.4, 421.6）门前偏外侧 (985, 428)——离城区一箭地，
		/// 开局可见城塔立面；初版 (973,421) 落在城圈内贴城墙，视线被塔占位模型挡（2026-09-08 用户三次确认）。
		/// 🔴 引擎 Campaign.DefaultStartingPosition 非 virtual（两个调用点都锁定基类实现）——
		/// 出生位置由建号 Content 的 OnCharacterCreationFinalized 写入（织丰母本同法，shokuho.txt:164613）。
		/// </summary>
		public static Vec2 TaikouStartingPosition => new Vec2(985f, 428f);
	}
}
