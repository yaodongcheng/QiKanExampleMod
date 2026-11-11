using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// RedDeadRedemption2 内容包（最小版：1 文化 / 1 王国 / 1 家族 / 2 领主 / 1 据点）的 thin 战役类。
	/// 三单元架构铁则 3：类名 = GameType = <c>RedDeadRedemption2Campaign</c>，必须与两处逐字一致：
	///   ① <c>Modules/RedDeadRedemption2/SubModule.xml</c> 的 &lt;GameType value="RedDeadRedemption2Campaign"/&gt;
	///   ② 本类名。
	/// 出生点 = 圣丹尼斯城外（RDR2 大地图坐标 670.3 / 262.0，出生点在城西约 8 米）。
	/// </summary>
	public class RedDeadRedemption2Campaign : LivingWorldCampaign
	{
		public RedDeadRedemption2Campaign(CampaignGameMode gameMode)
			: base(gameMode)
		{
		}

		/// <summary>玩家出生点（大地图单位）。</summary>
		public override Vec2? StartingPosition => Rd2MinStartingPosition;

		public static Vec2 Rd2MinStartingPosition => new Vec2(662.0f, 257.0f);
	}
}
