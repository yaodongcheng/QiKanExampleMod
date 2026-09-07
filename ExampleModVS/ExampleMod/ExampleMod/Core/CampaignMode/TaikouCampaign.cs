using TaleWorlds.CampaignSystem;

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
	}
}
