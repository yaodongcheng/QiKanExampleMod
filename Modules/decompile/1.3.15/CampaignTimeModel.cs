using TaleWorlds.Core;

namespace TaleWorlds.CampaignSystem.ComponentInterfaces;

public abstract class CampaignTimeModel : MBGameModel<CampaignTimeModel>
{
	public abstract CampaignTime CampaignStartTime { get; }

	public abstract int SunRise { get; }

	public abstract int SunSet { get; }

	public abstract long TimeTicksPerMillisecond { get; }

	public abstract int MillisecondInSecond { get; }

	public abstract int SecondsInMinute { get; }

	public abstract int MinutesInHour { get; }

	public abstract int HoursInDay { get; }

	public abstract int DaysInWeek { get; }

	public abstract int WeeksInSeason { get; }

	public abstract int SeasonsInYear { get; }
}
You are not using the latest version of the tool, please update.
Latest version is '10.1.1.8388' (yours is '8.2.0.7535-95108c96')
