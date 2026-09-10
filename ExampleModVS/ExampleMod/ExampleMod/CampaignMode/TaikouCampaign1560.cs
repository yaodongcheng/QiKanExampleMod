using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 太阁·**1560 日轮**时代（现有 v0 世界：京归织田家）。
	/// 🔴 类名 = 该时代的 GameType 字符串（引擎 `GameTypeStringId => GetType().Name`）→
	///   必须与 Taikou/SubModule.xml 的 &lt;GameType value="TaikouCampaign1560"/&gt; **逐字一致**，
	///   改类名 = 改 GameType（两处必须同时改，否则该时代的段一个都加载不到）。
	/// 本时代的数据来源（SubModule.xml 注册）：
	///   settlements.xml / spclans.xml / spkingdoms.xml / taikou_heroes.xml ← 基线套（含 1560 GameType）
	///   其余段（文化/物品/模板/文本…）跨时代共用。
	/// </summary>
	public class TaikouCampaign1560 : TaikouCampaign
	{
		public TaikouCampaign1560(CampaignGameMode gameMode)
			: base(gameMode)
		{
		}
	}
}
