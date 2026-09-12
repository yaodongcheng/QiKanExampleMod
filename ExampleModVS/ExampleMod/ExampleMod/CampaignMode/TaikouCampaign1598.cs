using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 太阁·**1598 The Great Peace**时代。
	/// 🔴 类名 = 该时代的 GameType 字符串（引擎 `GameTypeStringId => GetType().Name`）→
	///   必须与 Taikou/SubModule.xml 的 &lt;GameType value="TaikouCampaign1598"/&gt; **逐字一致**，
	///   改类名 = 改 GameType（两处必须同时改，否则该时代的段一个都加载不到）。
	/// 本时代的数据来源：SubModule.xml 注册的 `_1598` 后缀段（spclans/spkingdoms/settlements/
	///   taikou_heroes/taikou_lords）+ 跨代共用段。差异数据由 Scripts/gen_taikou_era_world.py 生成。
	/// </summary>
	public class TaikouCampaign1598 : TaikouCampaign
	{
		public TaikouCampaign1598(CampaignGameMode gameMode)
			: base(gameMode)
		{
		}
	}
}
