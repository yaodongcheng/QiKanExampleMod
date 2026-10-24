using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// RD2 内容包（荒野大镖客 2 世界）的 thin 战役类（三单元架构铁则 3）。
	/// 类名 = GameType = <c>RD2Campaign</c>，必须与两处逐字一致：
	///   ① <c>Modules/RD2/SubModule.xml</c> 的 &lt;GameType value="RD2Campaign"/&gt;
	///   ② 本类名。
	/// 出生点 = 瓦伦丁（大地图 u/v，848 单位制；世界坐标 -300.6, 778.1）。
	/// </summary>
	public class RD2Campaign : LivingWorldCampaign
	{
		public RD2Campaign(CampaignGameMode gameMode)
			: base(gameMode)
		{
		}

		/// <summary>玩家出生点（大地图单位，瓦伦丁）。</summary>
		public override Vec2? StartingPosition => Rd2StartingPosition;

		public static Vec2 Rd2StartingPosition => new Vec2(509f, 229f);
	}
}
