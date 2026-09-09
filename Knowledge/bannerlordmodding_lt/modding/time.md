# Time / Date

<!-- 源: https://docs.bannerlordmodding.lt/modding/time/ | 抓取日期: 2026-09-09 -->

## Campaign Time

[CampaignTime Struct Reference](https://apidoc.bannerlord.com/v/1.1.0/struct_tale_worlds_1_1_campaign_system_1_1_campaign_time.html)

```
int hour = (int)CampaignTime.Now.CurrentHourInDay;
Campaign.CurrentTime { return (float)CampaignTime.Now.ToHours; }
```

Now, Never, IsFuture, IsPast, IsNow, IsDayTime, IsNightTime

```
if (lastPrayTime == CampaignTime.Never) {}
```

### From game start

```
(int)Campaign.Current.CampaignStartTime.ElapsedDaysUntilNow
```

### Get details

GetHourOfDay, GetDayOfWeek, GetDayOfSeason, GetDayOfYear, GetSeasonOfYear, GetYear

```
if (CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Winter) {}
```

### Time diff from/to in normal time units

CampaignTime.ElapsedDaysUntilNow (Milliseconds, Seconds, Hours, Days, Weeks, Seasons, Years)

RemainingMillisecondsFromNow ...

### Time diff from/to in CampaignTime

MillisecondsFromNow, SecondsFromNow, MinutesFromNow, HoursFromNow, DaysFromNow, WeeksFromNow, YearsFromNow

```
if (lastPrayTime.ElapsedHoursUntilNow > 24) {}
```

### Conversion to normal Time units

ToMilliseconds, ToSeconds. ToMinutes, ToHours, ToDays, ToWeeks, ToSeasons, ToYears

```
CampaignTime value.ToSeconds
```

### Conversion to CampaignTime

public static CampaignTime Milliseconds(long valueInMilliseconds), Seconds, Minutes, Hours, Days, Weeks, Seasons, Years

```
return CampaignTime.Seconds((long)seconds);
```

### SetTimeSpeed

```
Campaign.SetTimeSpeed (0/1/2)
```

## Mission Time

float Mission.Current.CurrentTime (in passed seconds)
